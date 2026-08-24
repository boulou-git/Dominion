using System;
using System.Collections.Generic;

public enum GameRuleStatus
{
    Applied,
    WaitingForChoice,
    Rejected
}

/// <summary>
/// Result of one rules operation on an authoritative working-copy state.
/// Generated events are ordered and belong to this resolution only.
/// </summary>
public sealed class GameRuleResult
{
    public GameRuleStatus Status { get; }
    public string Error { get; }
    public List<GameEvent> Events { get; }

    public bool Succeeded => Status == GameRuleStatus.Applied;

    private GameRuleResult(GameRuleStatus status, string error, List<GameEvent> events)
    {
        Status = status;
        Error = error ?? string.Empty;
        Events = events ?? new List<GameEvent>();
    }

    public static GameRuleResult Applied(List<GameEvent> events)
    {
        return new GameRuleResult(GameRuleStatus.Applied, string.Empty, events);
    }

    public static GameRuleResult WaitingForChoice(List<GameEvent> events)
    {
        return new GameRuleResult(GameRuleStatus.WaitingForChoice, string.Empty, events);
    }

    public static GameRuleResult Rejected(string error, List<GameEvent> events = null)
    {
        return new GameRuleResult(GameRuleStatus.Rejected, error, events);
    }
}

/// <summary>
/// Deterministic Dominion rules that know nothing about Photon, scenes or UI.
/// The caller owns cloning/committing the GameStateSnapshot and injects card lookup/randomness.
///
/// Normal card play and buying converge here; NetworkGameState should only validate command
/// freshness/identity, clone the state, call this layer and commit successful results.
/// </summary>
public static class GameRules
{
    public const string ActionPhase = "Action";
    public const string BuyPhase = "Buy";
    public const string CleanupPhase = "Cleanup";

    /// <summary>
    /// Plays one card from hand onto the board, pays its play cost, emits CardPlayed,
    /// then resolves the card's declarative "play" abilities in order.
    ///
    /// IMPORTANT: this mutates the supplied working copy. A Rejected/Waiting result must
    /// not be committed until the caller has the infrastructure required to resume it.
    /// </summary>
    public static GameRuleResult TryPlayCard(
        GameStateSnapshot state,
        string playerId,
        int instanceId,
        Func<string, ExtensionCardData> resolveCardDefinition,
        System.Random random)
    {
        List<GameEvent> events = new List<GameEvent>();

        if (state == null)
            return GameRuleResult.Rejected("Game state is null.");
        if (string.IsNullOrEmpty(playerId))
            return GameRuleResult.Rejected("Player id is missing.");
        if (instanceId <= 0)
            return GameRuleResult.Rejected("Card instance id is invalid.");
        if (resolveCardDefinition == null)
            return GameRuleResult.Rejected("Card definition resolver is missing.");

        PlayerStateSnapshot player = FindPlayer(state, playerId);
        if (player == null)
            return GameRuleResult.Rejected("Player was not found.");
        if (player.Hand == null || player.InPlay == null || !player.Hand.Contains(instanceId))
            return GameRuleResult.Rejected("Card is not in the player's hand.");

        CardInstance instance = FindCardInstance(state, instanceId);
        if (instance == null)
            return GameRuleResult.Rejected("Card instance was not found.");
        if (!string.Equals(instance.OwnerPlayerId, playerId, StringComparison.Ordinal))
            return GameRuleResult.Rejected("Card does not belong to the requesting player.");

        ExtensionCardData definition = resolveCardDefinition(instance.DefinitionId);
        if (definition == null)
            return GameRuleResult.Rejected("Card definition could not be resolved: " + instance.DefinitionId);

        bool consumesAction;
        string policyError = ValidatePlayPolicy(state, player, definition, out consumesAction);
        if (!string.IsNullOrEmpty(policyError))
            return GameRuleResult.Rejected(policyError);

        if (!CardZoneRules.MoveCard(player, CardZone.Hand, CardZone.InPlay, instanceId))
            return GameRuleResult.Rejected("Could not move the card from hand to in-play.");

        if (consumesAction)
            player.Actions--;

        events.Add(GameEvent.CardPlayed(playerId, instanceId, instance.DefinitionId));

        AbilityResolutionResult abilityResult = AbilityResolver.ResolvePlay(
            definition,
            new EffectExecutionContext(state, player, instanceId, random));

        if (abilityResult.Status == EffectResolutionStatus.Rejected)
        {
            return GameRuleResult.Rejected(
                "Could not resolve play ability for " + instance.DefinitionId + ": " + abilityResult.Error,
                events);
        }

        if (abilityResult.Status == EffectResolutionStatus.WaitingForChoice)
            return GameRuleResult.WaitingForChoice(events);

        if (abilityResult.AbilitiesMatched == 0 || abilityResult.EffectsResolved == 0)
        {
            return GameRuleResult.Rejected(
                "Card has no resolvable declarative play effects yet: " + instance.DefinitionId,
                events);
        }

        return GameRuleResult.Applied(events);
    }

    /// <summary>
    /// Pays one Buy and the card cost, then delegates the actual gain to GainRules.
    /// Buying never creates CardInstance objects or mutates pile counts directly here.
    /// </summary>
    public static GameRuleResult TryBuyCard(
        GameStateSnapshot state,
        string playerId,
        string definitionId,
        Func<string, ExtensionCardData> resolveCardDefinition)
    {
        List<GameEvent> events = new List<GameEvent>();

        if (state == null)
            return GameRuleResult.Rejected("Game state is null.");
        if (string.IsNullOrEmpty(playerId))
            return GameRuleResult.Rejected("Player id is missing.");
        if (string.IsNullOrEmpty(definitionId))
            return GameRuleResult.Rejected("Card definition id is missing.");
        if (resolveCardDefinition == null)
            return GameRuleResult.Rejected("Card definition resolver is missing.");
        if (!string.Equals(state.Phase, BuyPhase, StringComparison.Ordinal))
            return GameRuleResult.Rejected("Cards can only be bought during the Buy phase.");

        PlayerStateSnapshot player = FindPlayer(state, playerId);
        if (player == null)
            return GameRuleResult.Rejected("Player was not found.");
        if (player.Buys <= 0)
            return GameRuleResult.Rejected("No Buys remain.");

        ExtensionCardData definition = resolveCardDefinition(definitionId);
        if (definition == null)
            return GameRuleResult.Rejected("Card definition could not be resolved: " + definitionId);
        if (definition.cost < 0)
            return GameRuleResult.Rejected("Card cost cannot be negative.");
        if (definition.cost > player.Coins)
            return GameRuleResult.Rejected("Not enough Coins to buy card: " + definitionId);

        player.Coins -= definition.cost;
        player.Buys--;

        if (!GainRules.TryGainFromSupply(
                state,
                player,
                definitionId,
                CardZone.Discard,
                0,
                events,
                out _,
                out string gainError))
            return GameRuleResult.Rejected(gainError, events);

        // Keep Cleanup as a short visible/interactable stage so the UI can animate the
        // hand and in-play cards before the authoritative cleanup/draw is committed.
        if (player.Buys <= 0 ||
            (player.Coins <= 0 && !HandContainsType(state, player, resolveCardDefinition, "Trésor")))
            state.Phase = CleanupPhase;

        return GameRuleResult.Applied(events);
    }

    private static string ValidatePlayPolicy(
        GameStateSnapshot state,
        PlayerStateSnapshot player,
        ExtensionCardData definition,
        out bool consumesAction)
    {
        consumesAction = false;

        if (string.Equals(state.Phase, ActionPhase, StringComparison.Ordinal))
        {
            if (!HasType(definition, "Action"))
                return "Only Action cards can be played during the Action phase.";
            if (player.Actions <= 0)
                return "No Actions remain.";

            consumesAction = true;
            return string.Empty;
        }

        if (string.Equals(state.Phase, BuyPhase, StringComparison.Ordinal))
        {
            if (!HasType(definition, "Trésor"))
                return "Only Treasure cards can be played during the Buy phase.";

            return string.Empty;
        }

        return "Cards cannot be played during phase: " + (state.Phase ?? string.Empty);
    }

    private static bool HandContainsType(
        GameStateSnapshot state,
        PlayerStateSnapshot player,
        Func<string, ExtensionCardData> resolveCardDefinition,
        string type)
    {
        if (state == null || player == null || player.Hand == null || resolveCardDefinition == null)
            return false;

        foreach (int instanceId in player.Hand)
        {
            CardInstance instance = FindCardInstance(state, instanceId);
            if (instance == null)
                continue;

            ExtensionCardData definition = resolveCardDefinition(instance.DefinitionId);
            if (HasType(definition, type))
                return true;
        }

        return false;
    }

    private static bool HasType(ExtensionCardData definition, string type)
    {
        if (definition == null || definition.types == null || string.IsNullOrEmpty(type))
            return false;

        for (int i = 0; i < definition.types.Count; i++)
        {
            if (string.Equals(definition.types[i], type, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static PlayerStateSnapshot FindPlayer(GameStateSnapshot state, string playerId)
    {
        if (state == null || state.Players == null)
            return null;

        return state.Players.Find(player => player != null && player.PlayerId == playerId);
    }

    private static CardInstance FindCardInstance(GameStateSnapshot state, int instanceId)
    {
        if (state == null || state.CardInstances == null)
            return null;

        return state.CardInstances.Find(card => card != null && card.InstanceId == instanceId);
    }
}
