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
/// Commands mutate a working copy, publish semantic GameEvents into the state's durable
/// ResolutionQueue, then let TriggerResolver resolve those events before returning.
/// </summary>
public static class GameRules
{
    public const string ActionPhase = "Action";
    public const string BuyPhase = "Buy";
    public const string CleanupPhase = "Cleanup";

    public static GameRuleResult TryPlayCard(
        GameStateSnapshot state,
        string playerId,
        int instanceId,
        Func<string, ExtensionCardData> resolveCardDefinition,
        System.Random random)
    {
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

        if (!ResolutionQueue.TryBegin(state, playerId, out ResolutionQueue resolution, out string resolutionError))
            return GameRuleResult.Rejected(resolutionError);

        if (!CardZoneRules.MoveCard(player, CardZone.Hand, CardZone.InPlay, instanceId))
            return GameRuleResult.Rejected("Could not move the card from hand to in-play.");

        if (consumesAction)
            player.Actions--;

        resolution.Events.Publish(GameEvent.CardPlayed(playerId, instanceId, instance.DefinitionId));

        TriggerResolutionResult triggerResult = TriggerResolver.ResolvePending(
            resolution.Events,
            state,
            resolveCardDefinition,
            random);

        List<GameEvent> events = resolution.Events.SnapshotHistory();
        if (triggerResult.Status == EffectResolutionStatus.Rejected)
        {
            return GameRuleResult.Rejected(
                "Could not resolve CardPlayed triggers for " + instance.DefinitionId + ": " + triggerResult.Error,
                events);
        }

        if (triggerResult.Status == EffectResolutionStatus.WaitingForChoice)
        {
            if (!resolution.IsWaitingForDecision)
            {
                return GameRuleResult.Rejected(
                    "A trigger reported WaitingForChoice without creating a durable PendingDecision.",
                    events);
            }

            return GameRuleResult.WaitingForChoice(events);
        }

        // During the current migration every playable card must still declare at least
        // one concrete play effect. This keeps the pre-event behaviour unchanged.
        if (triggerResult.AbilitiesMatched == 0 || triggerResult.EffectsResolved == 0)
        {
            return GameRuleResult.Rejected(
                "Card has no resolvable declarative play effects yet: " + instance.DefinitionId,
                events);
        }

        resolution.CompleteIfIdle();
        return GameRuleResult.Applied(events);
    }

    /// <summary>
    /// Pays one Buy and the card cost, delegates the gain to GainRules, then resolves the
    /// resulting CardGained/PileEmptied event chain before deciding whether cleanup starts.
    /// Random remains injectable for deterministic tests and future gain triggers that draw.
    /// </summary>
    public static GameRuleResult TryBuyCard(
        GameStateSnapshot state,
        string playerId,
        string definitionId,
        Func<string, ExtensionCardData> resolveCardDefinition,
        System.Random random = null)
    {
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

        if (!ResolutionQueue.TryBegin(state, playerId, out ResolutionQueue resolution, out string resolutionError))
            return GameRuleResult.Rejected(resolutionError);

        player.Coins -= definition.cost;
        player.Buys--;

        if (!GainRules.TryGainFromSupply(
                state,
                player,
                definitionId,
                CardZone.Discard,
                0,
                resolution.Events,
                out _,
                out string gainError))
            return GameRuleResult.Rejected(gainError, resolution.Events.SnapshotHistory());

        TriggerResolutionResult triggerResult = TriggerResolver.ResolvePending(
            resolution.Events,
            state,
            resolveCardDefinition,
            random);

        List<GameEvent> events = resolution.Events.SnapshotHistory();
        if (triggerResult.Status == EffectResolutionStatus.Rejected)
            return GameRuleResult.Rejected(triggerResult.Error, events);

        if (triggerResult.Status == EffectResolutionStatus.WaitingForChoice)
        {
            if (!resolution.IsWaitingForDecision)
            {
                return GameRuleResult.Rejected(
                    "A trigger reported WaitingForChoice without creating a durable PendingDecision.",
                    events);
            }

            return GameRuleResult.WaitingForChoice(events);
        }

        // Keep Cleanup as a short visible/interactable stage so the UI can animate the
        // hand and in-play cards before the authoritative cleanup/draw is committed.
        if (player.Buys <= 0 ||
            (player.Coins <= 0 && !HandContainsType(state, player, resolveCardDefinition, "Trésor")))
            state.Phase = CleanupPhase;

        resolution.CompleteIfIdle();
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
