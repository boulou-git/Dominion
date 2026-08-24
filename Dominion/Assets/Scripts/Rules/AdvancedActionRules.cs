using System;
using System.Collections.Generic;

/// <summary>
/// Reusable continuations for action-card effects that repeatedly inspect cards and
/// may pause for player choices. Nothing here knows specific card ids.
/// </summary>
public static class AdvancedActionRules
{
    private const string DrawToSizePrefix = "draw_to_hand_size_skipping_type|";
    private const string MoveAllOrderedPrefix = "move_all_ordered|";

    public static bool IsContinuation(string operation)
    {
        if (string.IsNullOrEmpty(operation)) return false;
        return operation.StartsWith(DrawToSizePrefix, StringComparison.OrdinalIgnoreCase) ||
               operation.StartsWith(MoveAllOrderedPrefix, StringComparison.OrdinalIgnoreCase);
    }

    public static GameRuleResult TryStartDrawToHandSizeSkippingType(
        GameStateSnapshot state,
        PlayerStateSnapshot player,
        ResolutionQueue resolution,
        int targetHandSize,
        string skippableCardType,
        string prompt,
        int sourceCardInstanceId,
        GameEvent triggerEvent,
        string timing,
        int listenerCardInstanceId,
        int abilityIndex,
        int effectIndex,
        Func<string, ExtensionCardData> resolve,
        System.Random random)
    {
        if (state == null || player == null || resolution == null || resolve == null || targetHandSize < 0 || string.IsNullOrWhiteSpace(skippableCardType))
            return GameRuleResult.Rejected("Invalid draw-to-hand-size effect.", resolution != null ? resolution.Events.SnapshotHistory() : null);

        List<int> revealed = CardZoneRules.ResolveZone(player, CardZone.Revealed);
        if (revealed == null) return GameRuleResult.Rejected("Revealed zone is unavailable.", resolution.Events.SnapshotHistory());
        if (revealed.Count > 0) return GameRuleResult.Rejected("Cannot start draw-to-hand-size while cards are already revealed.", resolution.Events.SnapshotHistory());

        return ContinueDrawToHandSize(state, player, resolution, targetHandSize, skippableCardType, prompt,
            sourceCardInstanceId, triggerEvent, timing, listenerCardInstanceId, abilityIndex, effectIndex, resolve, random);
    }

    public static GameRuleResult TryStartMoveAllOrdered(
        GameStateSnapshot state,
        PlayerStateSnapshot player,
        ResolutionQueue resolution,
        CardZone sourceZone,
        CardZone destinationZone,
        string prompt,
        int sourceCardInstanceId,
        GameEvent triggerEvent,
        string timing,
        int listenerCardInstanceId,
        int abilityIndex,
        int effectIndex)
    {
        if (state == null || player == null || resolution == null || sourceZone == CardZone.None || destinationZone == CardZone.None || sourceZone == destinationZone)
            return GameRuleResult.Rejected("Invalid ordered move effect.", resolution != null ? resolution.Events.SnapshotHistory() : null);

        return ContinueMoveAllOrdered(player, resolution, sourceZone, destinationZone, prompt, sourceCardInstanceId,
            triggerEvent, timing, listenerCardInstanceId, abilityIndex, effectIndex);
    }

    public static GameRuleResult ResolveContinuation(
        GameStateSnapshot state,
        PlayerStateSnapshot player,
        ResolutionQueue resolution,
        PendingDecisionSnapshot continuation,
        Func<string, ExtensionCardData> resolve,
        System.Random random)
    {
        if (state == null || player == null || resolution == null || continuation == null || resolve == null)
            return GameRuleResult.Rejected("Advanced action continuation is invalid.", resolution != null ? resolution.Events.SnapshotHistory() : null);

        string operation = continuation.Operation ?? string.Empty;
        if (operation.StartsWith(DrawToSizePrefix, StringComparison.OrdinalIgnoreCase))
            return ResolveDrawToHandSizeDecision(state, player, resolution, continuation, resolve, random);
        if (operation.StartsWith(MoveAllOrderedPrefix, StringComparison.OrdinalIgnoreCase))
            return ResolveMoveAllOrderedDecision(player, resolution, continuation);

        return GameRuleResult.Rejected("Unsupported advanced action continuation: " + operation, resolution.Events.SnapshotHistory());
    }

    private static GameRuleResult ResolveDrawToHandSizeDecision(
        GameStateSnapshot state,
        PlayerStateSnapshot player,
        ResolutionQueue resolution,
        PendingDecisionSnapshot continuation,
        Func<string, ExtensionCardData> resolve,
        System.Random random)
    {
        string cardType = (continuation.Operation ?? string.Empty).Substring(DrawToSizePrefix.Length);
        if (string.IsNullOrWhiteSpace(cardType) || continuation.TargetHandSize < 0)
            return GameRuleResult.Rejected("Draw-to-hand-size continuation metadata is invalid.", resolution.Events.SnapshotHistory());

        if (!CardZoneRules.TryParseZone(continuation.Zone, out CardZone zone) || zone != CardZone.Revealed)
            return GameRuleResult.Rejected("Draw-to-hand-size decision must use the revealed zone.", resolution.Events.SnapshotHistory());

        List<int> candidates = continuation.CandidateInstanceIds != null
            ? new List<int>(continuation.CandidateInstanceIds)
            : new List<int>();
        if (candidates.Count != 1)
            return GameRuleResult.Rejected("Draw-to-hand-size decision must concern exactly one revealed card.", resolution.Events.SnapshotHistory());

        int currentId = candidates[0];
        List<int> selected = resolution.TakeSelectedInstanceIds();
        if (selected.Count > 1 || (selected.Count == 1 && selected[0] != currentId))
            return GameRuleResult.Rejected("Draw-to-hand-size selection is invalid.", resolution.Events.SnapshotHistory());

        // Selecting the card means setting it aside. Not selecting it means keeping it in hand.
        if (selected.Count == 0 && !CardZoneRules.MoveCard(player, CardZone.Revealed, CardZone.Hand, currentId))
            return GameRuleResult.Rejected("Could not move the revealed card into hand.", resolution.Events.SnapshotHistory());

        return ContinueDrawToHandSize(state, player, resolution, continuation.TargetHandSize, cardType, continuation.Prompt,
            continuation.SourceCardInstanceId, RestoreEvent(continuation), continuation.Timing, continuation.ListenerCardInstanceId,
            continuation.AbilityIndex, continuation.EffectIndex, resolve, random);
    }

    private static GameRuleResult ContinueDrawToHandSize(
        GameStateSnapshot state,
        PlayerStateSnapshot player,
        ResolutionQueue resolution,
        int targetHandSize,
        string skippableCardType,
        string prompt,
        int sourceCardInstanceId,
        GameEvent triggerEvent,
        string timing,
        int listenerCardInstanceId,
        int abilityIndex,
        int effectIndex,
        Func<string, ExtensionCardData> resolve,
        System.Random random)
    {
        if (player.Hand == null) return GameRuleResult.Rejected("Draw-to-hand-size requires a hand zone.", resolution.Events.SnapshotHistory());

        while (player.Hand.Count < targetHandSize)
        {
            if (!CardZoneRules.TryMoveTopCardFromDeck(player, CardZone.Revealed, random, out int instanceId, out string error))
                return GameRuleResult.Rejected(error, resolution.Events.SnapshotHistory());
            if (instanceId <= 0) return FinishSetAside(state, player, resolution, sourceCardInstanceId);

            CardInstance instance = FindCard(state, instanceId);
            ExtensionCardData definition = instance != null ? resolve(instance.DefinitionId) : null;
            if (instance == null || definition == null)
                return GameRuleResult.Rejected("Revealed card definition could not be resolved.", resolution.Events.SnapshotHistory());

            if (!CardDefinitionRules.HasType(definition, skippableCardType))
            {
                if (!CardZoneRules.MoveCard(player, CardZone.Revealed, CardZone.Hand, instanceId))
                    return GameRuleResult.Rejected("Could not move revealed card into hand.", resolution.Events.SnapshotHistory());
                continue;
            }

            string operation = DrawToSizePrefix + skippableCardType;
            if (!resolution.TrySuspendForDecision(player.PlayerId, operation, "revealed",
                string.IsNullOrWhiteSpace(prompt) ? "Vous pouvez mettre cette carte de côté." : prompt,
                sourceCardInstanceId, 0, 1, new[] { instanceId }, triggerEvent, timing,
                listenerCardInstanceId, abilityIndex, effectIndex, out string suspendError))
                return GameRuleResult.Rejected(suspendError, resolution.Events.SnapshotHistory());

            resolution.PendingDecision.TargetHandSize = targetHandSize;
            return GameRuleResult.WaitingForChoice(resolution.Events.SnapshotHistory());
        }

        return FinishSetAside(state, player, resolution, sourceCardInstanceId);
    }

    private static GameRuleResult FinishSetAside(
        GameStateSnapshot state,
        PlayerStateSnapshot player,
        ResolutionQueue resolution,
        int sourceCardInstanceId)
    {
        List<int> revealed = CardZoneRules.ResolveZone(player, CardZone.Revealed);
        if (revealed == null || revealed.Count == 0) return GameRuleResult.Applied(resolution.Events.SnapshotHistory());

        List<int> toDiscard = new List<int>(revealed);
        if (!DiscardRules.TryDiscardSelected(state, player, CardZone.Revealed, toDiscard, sourceCardInstanceId,
            resolution.Events, out string error))
            return GameRuleResult.Rejected(error, resolution.Events.SnapshotHistory());

        return GameRuleResult.Applied(resolution.Events.SnapshotHistory());
    }

    private static GameRuleResult ResolveMoveAllOrderedDecision(
        PlayerStateSnapshot player,
        ResolutionQueue resolution,
        PendingDecisionSnapshot continuation)
    {
        if (!CardZoneRules.TryParseZone(continuation.Zone, out CardZone sourceZone))
            return GameRuleResult.Rejected("Ordered move source zone is invalid.", resolution.Events.SnapshotHistory());

        string destinationText = (continuation.Operation ?? string.Empty).Substring(MoveAllOrderedPrefix.Length);
        if (!CardZoneRules.TryParseZone(destinationText, out CardZone destinationZone) || sourceZone == destinationZone)
            return GameRuleResult.Rejected("Ordered move destination zone is invalid.", resolution.Events.SnapshotHistory());

        List<int> selected = resolution.TakeSelectedInstanceIds();
        if (selected.Count != 1 || !CardZoneRules.MoveCard(player, sourceZone, destinationZone, selected[0]))
            return GameRuleResult.Rejected("Ordered move selection could not be moved.", resolution.Events.SnapshotHistory());

        return ContinueMoveAllOrdered(player, resolution, sourceZone, destinationZone, continuation.Prompt,
            continuation.SourceCardInstanceId, RestoreEvent(continuation), continuation.Timing,
            continuation.ListenerCardInstanceId, continuation.AbilityIndex, continuation.EffectIndex);
    }

    private static GameRuleResult ContinueMoveAllOrdered(
        PlayerStateSnapshot player,
        ResolutionQueue resolution,
        CardZone sourceZone,
        CardZone destinationZone,
        string prompt,
        int sourceCardInstanceId,
        GameEvent triggerEvent,
        string timing,
        int listenerCardInstanceId,
        int abilityIndex,
        int effectIndex)
    {
        List<int> source = CardZoneRules.ResolveZone(player, sourceZone);
        if (source == null) return GameRuleResult.Rejected("Ordered move source zone is unavailable.", resolution.Events.SnapshotHistory());
        if (source.Count == 0) return GameRuleResult.Applied(resolution.Events.SnapshotHistory());

        if (source.Count == 1)
        {
            int last = source[0];
            if (!CardZoneRules.MoveCard(player, sourceZone, destinationZone, last))
                return GameRuleResult.Rejected("Could not move final ordered card.", resolution.Events.SnapshotHistory());
            return GameRuleResult.Applied(resolution.Events.SnapshotHistory());
        }

        string operation = MoveAllOrderedPrefix + destinationZone.ToString().ToLowerInvariant();
        if (!resolution.TrySuspendForDecision(player.PlayerId, operation, sourceZone.ToString().ToLowerInvariant(),
            string.IsNullOrWhiteSpace(prompt) ? "Choisissez la prochaine carte à déplacer." : prompt,
            sourceCardInstanceId, 1, 1, source, triggerEvent, timing, listenerCardInstanceId,
            abilityIndex, effectIndex, out string error))
            return GameRuleResult.Rejected(error, resolution.Events.SnapshotHistory());

        return GameRuleResult.WaitingForChoice(resolution.Events.SnapshotHistory());
    }

    private static CardInstance FindCard(GameStateSnapshot state, int instanceId)
    {
        return state != null && state.CardInstances != null
            ? state.CardInstances.Find(card => card != null && card.InstanceId == instanceId)
            : null;
    }

    private static GameEvent RestoreEvent(PendingDecisionSnapshot continuation)
    {
        return continuation != null && continuation.TriggerEvent != null && continuation.TriggerEvent.TryToRuntime(out GameEvent gameEvent)
            ? gameEvent
            : null;
    }
}
