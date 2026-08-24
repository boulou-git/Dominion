using System;
using System.Collections.Generic;

public enum CardPlayKind
{
    Action,
    Treasure
}

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
/// All ways of playing a card should converge here. Card-type-specific rules are policy
/// checks inside this one pipeline, not separate copies of the same play implementation.
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
        CardPlayKind playKind,
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

        string policyError = ValidatePlayPolicy(state, player, definition, playKind);
        if (!string.IsNullOrEmpty(policyError))
            return GameRuleResult.Rejected(policyError);

        player.Hand.Remove(instanceId);
        player.InPlay.Add(instanceId);

        if (playKind == CardPlayKind.Action)
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

    private static string ValidatePlayPolicy(
        GameStateSnapshot state,
        PlayerStateSnapshot player,
        ExtensionCardData definition,
        CardPlayKind playKind)
    {
        switch (playKind)
        {
            case CardPlayKind.Action:
                if (!string.Equals(state.Phase, ActionPhase, StringComparison.Ordinal))
                    return "Action cards can only be played during the Action phase.";
                if (player.Actions <= 0)
                    return "No Actions remain.";
                if (!HasType(definition, "Action"))
                    return "Card is not an Action.";
                return string.Empty;

            case CardPlayKind.Treasure:
                if (!string.Equals(state.Phase, BuyPhase, StringComparison.Ordinal))
                    return "Treasure cards can only be played during the Buy phase.";
                if (!HasType(definition, "Trésor"))
                    return "Card is not a Treasure.";
                return string.Empty;

            default:
                return "Unsupported card play kind: " + playKind;
        }
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
