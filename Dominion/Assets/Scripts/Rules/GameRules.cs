using System;
using System.Collections.Generic;

public enum GameRuleStatus
{
    Applied,
    WaitingForChoice,
    Rejected
}

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

    public static GameRuleResult Applied(List<GameEvent> events) => new GameRuleResult(GameRuleStatus.Applied, string.Empty, events);
    public static GameRuleResult WaitingForChoice(List<GameEvent> events) => new GameRuleResult(GameRuleStatus.WaitingForChoice, string.Empty, events);
    public static GameRuleResult Rejected(string error, List<GameEvent> events = null) => new GameRuleResult(GameRuleStatus.Rejected, error, events);
}

public static class GameRules
{
    public const string ActionPhase = "Action";
    public const string BuyPhase = "Buy";
    public const string CleanupPhase = "Cleanup";

    public static GameRuleResult TryPlayCard(GameStateSnapshot state, string playerId, int instanceId,
        Func<string, ExtensionCardData> resolveCardDefinition, System.Random random)
    {
        if (state == null) return GameRuleResult.Rejected("Game state is null.");
        if (string.IsNullOrEmpty(playerId)) return GameRuleResult.Rejected("Player id is missing.");
        if (instanceId <= 0) return GameRuleResult.Rejected("Card instance id is invalid.");
        if (resolveCardDefinition == null) return GameRuleResult.Rejected("Card definition resolver is missing.");
        PlayerStateSnapshot player = FindPlayer(state, playerId);
        if (player == null) return GameRuleResult.Rejected("Player was not found.");
        if (player.Hand == null || player.InPlay == null || !player.Hand.Contains(instanceId)) return GameRuleResult.Rejected("Card is not in the player's hand.");
        CardInstance instance = FindCardInstance(state, instanceId);
        if (instance == null) return GameRuleResult.Rejected("Card instance was not found.");
        if (!string.Equals(instance.OwnerPlayerId, playerId, StringComparison.Ordinal)) return GameRuleResult.Rejected("Card does not belong to the requesting player.");
        ExtensionCardData definition = resolveCardDefinition(instance.DefinitionId);
        if (definition == null) return GameRuleResult.Rejected("Card definition could not be resolved: " + instance.DefinitionId);
        string policyError = ValidatePlayPolicy(state, player, definition, out bool consumesAction);
        if (!string.IsNullOrEmpty(policyError)) return GameRuleResult.Rejected(policyError);
        if (!ResolutionQueue.TryBegin(state, playerId, out ResolutionQueue resolution, out string resolutionError)) return GameRuleResult.Rejected(resolutionError);
        if (!CardZoneRules.MoveCard(player, CardZone.Hand, CardZone.InPlay, instanceId)) return GameRuleResult.Rejected("Could not move the card from hand to in-play.");
        if (consumesAction) player.Actions--;
        resolution.Events.Publish(GameEvent.CardPlayed(playerId, instanceId, instance.DefinitionId));
        TriggerResolutionResult triggerResult = TriggerResolver.ResolvePending(resolution, state, resolveCardDefinition, random);
        List<GameEvent> events = resolution.Events.SnapshotHistory();
        if (triggerResult.Status == EffectResolutionStatus.Rejected) return GameRuleResult.Rejected("Could not resolve CardPlayed triggers for " + instance.DefinitionId + ": " + triggerResult.Error, events);
        if (triggerResult.Status == EffectResolutionStatus.WaitingForChoice)
        {
            if (!resolution.IsWaitingForDecision) return GameRuleResult.Rejected("A trigger reported WaitingForChoice without creating a durable PendingDecision.", events);
            return GameRuleResult.WaitingForChoice(events);
        }
        if (triggerResult.AbilitiesMatched == 0 || triggerResult.EffectsResolved == 0) return GameRuleResult.Rejected("Card has no resolvable declarative play effects yet: " + instance.DefinitionId, events);
        resolution.CompleteIfIdle();
        return GameRuleResult.Applied(events);
    }

    public static GameRuleResult TryBuyCard(GameStateSnapshot state, string playerId, string definitionId,
        Func<string, ExtensionCardData> resolveCardDefinition, System.Random random = null)
    {
        if (state == null) return GameRuleResult.Rejected("Game state is null.");
        if (string.IsNullOrEmpty(playerId)) return GameRuleResult.Rejected("Player id is missing.");
        if (string.IsNullOrEmpty(definitionId)) return GameRuleResult.Rejected("Card definition id is missing.");
        if (resolveCardDefinition == null) return GameRuleResult.Rejected("Card definition resolver is missing.");
        if (!string.Equals(state.Phase, BuyPhase, StringComparison.Ordinal)) return GameRuleResult.Rejected("Cards can only be bought during the Buy phase.");
        PlayerStateSnapshot player = FindPlayer(state, playerId);
        if (player == null) return GameRuleResult.Rejected("Player was not found.");
        if (player.Buys <= 0) return GameRuleResult.Rejected("No Buys remain.");
        ExtensionCardData definition = resolveCardDefinition(definitionId);
        if (definition == null) return GameRuleResult.Rejected("Card definition could not be resolved: " + definitionId);
        if (definition.cost < 0) return GameRuleResult.Rejected("Card cost cannot be negative.");
        if (definition.cost > player.Coins) return GameRuleResult.Rejected("Not enough Coins to buy card: " + definitionId);
        if (!ResolutionQueue.TryBegin(state, playerId, out ResolutionQueue resolution, out string resolutionError)) return GameRuleResult.Rejected(resolutionError);
        player.Coins -= definition.cost; player.Buys--;
        if (!GainRules.TryGainFromSupply(state, player, definitionId, CardZone.Discard, 0, resolution.Events, out _, out string gainError))
            return GameRuleResult.Rejected(gainError, resolution.Events.SnapshotHistory());
        TriggerResolutionResult triggerResult = TriggerResolver.ResolvePending(resolution, state, resolveCardDefinition, random);
        List<GameEvent> events = resolution.Events.SnapshotHistory();
        if (triggerResult.Status == EffectResolutionStatus.Rejected) return GameRuleResult.Rejected(triggerResult.Error, events);
        if (triggerResult.Status == EffectResolutionStatus.WaitingForChoice)
        {
            if (!resolution.IsWaitingForDecision) return GameRuleResult.Rejected("A trigger reported WaitingForChoice without creating a durable PendingDecision.", events);
            return GameRuleResult.WaitingForChoice(events);
        }
        if (player.Buys <= 0 || (player.Coins <= 0 && !HandContainsType(state, player, resolveCardDefinition, "Trésor"))) state.Phase = CleanupPhase;
        resolution.CompleteIfIdle();
        return GameRuleResult.Applied(events);
    }

    public static GameRuleResult TrySubmitDecision(GameStateSnapshot state, string playerId, string decisionId,
        int[] selectedInstanceIds, Func<string, ExtensionCardData> resolveCardDefinition, System.Random random)
    {
        if (!PrepareDecisionResume(state, playerId, decisionId, resolveCardDefinition, out ResolutionQueue resolution, out GameRuleResult rejected))
            return rejected;
        if (!resolution.TrySubmitDecision(playerId, decisionId, selectedInstanceIds, out PendingDecisionSnapshot continuation, out string decisionError))
            return GameRuleResult.Rejected(decisionError, resolution.Events.SnapshotHistory());
        PlayerStateSnapshot player = FindPlayer(state, playerId);
        if (!CardZoneRules.TryParseZone(continuation.Zone, out CardZone choiceZone))
            return GameRuleResult.Rejected("Decision source zone is invalid.", resolution.Events.SnapshotHistory());
        List<int> sourceZone = CardZoneRules.ResolveZone(player, choiceZone);
        if (sourceZone == null) return GameRuleResult.Rejected("Decision source zone is unavailable.", resolution.Events.SnapshotHistory());
        foreach (int instanceId in resolution.SelectedInstanceIds)
            if (!sourceZone.Contains(instanceId)) return GameRuleResult.Rejected("Selected card is no longer in the decision source zone.", resolution.Events.SnapshotHistory());

        if (string.Equals(continuation.Operation, "discard_down_to", StringComparison.OrdinalIgnoreCase))
            return ResolveDiscardDownDecision(state, player, resolution, continuation, resolveCardDefinition, random);

        return ResumeDecision(state, resolution, continuation, resolveCardDefinition, random);
    }

    public static GameRuleResult TrySubmitSupplyDecision(GameStateSnapshot state, string playerId, string decisionId,
        string[] selectedDefinitionIds, Func<string, ExtensionCardData> resolveCardDefinition, System.Random random)
    {
        if (!PrepareDecisionResume(state, playerId, decisionId, resolveCardDefinition, out ResolutionQueue resolution, out GameRuleResult rejected))
            return rejected;
        if (!resolution.TrySubmitSupplyDecision(playerId, decisionId, selectedDefinitionIds, out PendingDecisionSnapshot continuation, out string decisionError))
            return GameRuleResult.Rejected(decisionError, resolution.Events.SnapshotHistory());

        foreach (string definitionId in resolution.SelectedDefinitionIds)
        {
            SupplyPileSnapshot pile = state.SupplyPiles != null
                ? state.SupplyPiles.Find(item => item != null && string.Equals(item.DefinitionId, definitionId, StringComparison.OrdinalIgnoreCase))
                : null;
            if (pile == null || pile.RemainingCount <= 0)
                return GameRuleResult.Rejected("Selected supply pile is no longer available: " + definitionId, resolution.Events.SnapshotHistory());
        }

        return ResumeDecision(state, resolution, continuation, resolveCardDefinition, random);
    }

    private static GameRuleResult ResolveDiscardDownDecision(GameStateSnapshot state, PlayerStateSnapshot responder,
        ResolutionQueue resolution, PendingDecisionSnapshot continuation,
        Func<string, ExtensionCardData> resolveCardDefinition, System.Random random)
    {
        List<int> selected = resolution.TakeSelectedInstanceIds();
        if (!DiscardRules.TryDiscardSelectedFromHand(
                state,
                responder,
                selected,
                continuation.SourceCardInstanceId,
                resolution.Events,
                out string discardError))
            return GameRuleResult.Rejected(discardError, resolution.Events.SnapshotHistory());

        List<string> remaining = continuation.RemainingPlayerIds != null
            ? new List<string>(continuation.RemainingPlayerIds)
            : new List<string>();

        while (remaining.Count > 0)
        {
            string nextPlayerId = remaining[0];
            remaining.RemoveAt(0);
            PlayerStateSnapshot nextPlayer = FindPlayer(state, nextPlayerId);
            if (nextPlayer == null || nextPlayer.Hand == null || nextPlayer.Hand.Count <= continuation.TargetHandSize)
                continue;

            if (!resolution.TrySuspendForDiscardDownDecision(
                    nextPlayer.PlayerId,
                    continuation.Prompt,
                    continuation.SourceCardInstanceId,
                    continuation.TargetHandSize,
                    nextPlayer.Hand,
                    remaining,
                    RestoreTriggerEvent(continuation),
                    continuation.Timing,
                    continuation.ListenerCardInstanceId,
                    continuation.AbilityIndex,
                    continuation.EffectIndex,
                    out string suspendError))
                return GameRuleResult.Rejected(suspendError, resolution.Events.SnapshotHistory());

            return GameRuleResult.WaitingForChoice(resolution.Events.SnapshotHistory());
        }

        return ResumeDecision(state, resolution, continuation, resolveCardDefinition, random);
    }

    private static GameEvent RestoreTriggerEvent(PendingDecisionSnapshot continuation)
    {
        if (continuation == null || continuation.TriggerEvent == null) return null;
        return continuation.TriggerEvent.TryToRuntime(out GameEvent gameEvent) ? gameEvent : null;
    }

    private static bool PrepareDecisionResume(GameStateSnapshot state, string playerId, string decisionId,
        Func<string, ExtensionCardData> resolveCardDefinition, out ResolutionQueue resolution, out GameRuleResult rejected)
    {
        resolution = null; rejected = null;
        if (state == null) { rejected = GameRuleResult.Rejected("Game state is null."); return false; }
        if (string.IsNullOrEmpty(playerId)) { rejected = GameRuleResult.Rejected("Decision player id is missing."); return false; }
        if (string.IsNullOrEmpty(decisionId)) { rejected = GameRuleResult.Rejected("Decision id is missing."); return false; }
        if (resolveCardDefinition == null) { rejected = GameRuleResult.Rejected("Card definition resolver is missing."); return false; }
        if (!ResolutionQueue.TryResume(state, out resolution, out string resumeError)) { rejected = GameRuleResult.Rejected(resumeError); return false; }
        if (FindPlayer(state, playerId) == null) { rejected = GameRuleResult.Rejected("Decision player was not found.", resolution.Events.SnapshotHistory()); return false; }
        return true;
    }

    private static GameRuleResult ResumeDecision(GameStateSnapshot state, ResolutionQueue resolution,
        PendingDecisionSnapshot continuation, Func<string, ExtensionCardData> resolveCardDefinition, System.Random random)
    {
        TriggerResolutionResult triggerResult = TriggerResolver.ResumeSubjectDecision(resolution, continuation, state, resolveCardDefinition, random);
        List<GameEvent> events = resolution.Events.SnapshotHistory();
        if (triggerResult.Status == EffectResolutionStatus.Rejected) return GameRuleResult.Rejected(triggerResult.Error, events);
        if (triggerResult.Status == EffectResolutionStatus.WaitingForChoice)
        {
            if (!resolution.IsWaitingForDecision) return GameRuleResult.Rejected("Resumed trigger reported WaitingForChoice without a durable PendingDecision.", events);
            return GameRuleResult.WaitingForChoice(events);
        }
        resolution.CompleteIfIdle();
        return GameRuleResult.Applied(events);
    }

    private static string ValidatePlayPolicy(GameStateSnapshot state, PlayerStateSnapshot player, ExtensionCardData definition, out bool consumesAction)
    {
        consumesAction = false;
        if (string.Equals(state.Phase, ActionPhase, StringComparison.Ordinal))
        {
            if (!CardDefinitionRules.HasType(definition, "Action")) return "Only Action cards can be played during the Action phase.";
            if (player.Actions <= 0) return "No Actions remain.";
            consumesAction = true; return string.Empty;
        }
        if (string.Equals(state.Phase, BuyPhase, StringComparison.Ordinal))
        {
            if (!CardDefinitionRules.HasType(definition, "Trésor")) return "Only Treasure cards can be played during the Buy phase.";
            return string.Empty;
        }
        return "Cards cannot be played during phase: " + (state.Phase ?? string.Empty);
    }

    private static bool HandContainsType(GameStateSnapshot state, PlayerStateSnapshot player, Func<string, ExtensionCardData> resolveCardDefinition, string type)
    {
        if (state == null || player == null || player.Hand == null || resolveCardDefinition == null) return false;
        foreach (int instanceId in player.Hand)
        {
            CardInstance instance = FindCardInstance(state, instanceId);
            if (instance != null && CardDefinitionRules.HasType(resolveCardDefinition(instance.DefinitionId), type)) return true;
        }
        return false;
    }

    private static PlayerStateSnapshot FindPlayer(GameStateSnapshot state, string playerId)
    {
        if (state == null || state.Players == null) return null;
        return state.Players.Find(player => player != null && player.PlayerId == playerId);
    }

    private static CardInstance FindCardInstance(GameStateSnapshot state, int instanceId)
    {
        if (state == null || state.CardInstances == null) return null;
        return state.CardInstances.Find(card => card != null && card.InstanceId == instanceId);
    }
}