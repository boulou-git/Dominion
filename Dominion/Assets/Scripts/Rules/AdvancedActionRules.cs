using System;
using System.Collections.Generic;

/// <summary>
/// Reusable continuations for action-card effects that may pause for one or more
/// player choices. Nothing here knows specific card ids.
/// </summary>
public static class AdvancedActionRules
{
    private const string DrawToSizePrefix = "draw_to_hand_size_skipping_type|";
    private const string MoveAllOrderedPrefix = "move_all_ordered|";
    private const string SimultaneousPassLeftOperation = "simultaneous_pass_left";
    private const string ReplaceEachOtherTopPrefix = "replace_each_other_top_card|";

    public static bool IsContinuation(string operation)
    {
        if (string.IsNullOrEmpty(operation)) return false;
        return operation.StartsWith(DrawToSizePrefix, StringComparison.OrdinalIgnoreCase) ||
               operation.StartsWith(MoveAllOrderedPrefix, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(operation, SimultaneousPassLeftOperation, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSupplyContinuation(string operation) =>
        !string.IsNullOrEmpty(operation) && operation.StartsWith(ReplaceEachOtherTopPrefix, StringComparison.OrdinalIgnoreCase);

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

    public static GameRuleResult TryStartSimultaneousPassLeft(
        GameStateSnapshot state,
        PlayerStateSnapshot actor,
        ResolutionQueue resolution,
        string prompt,
        int sourceCardInstanceId,
        GameEvent triggerEvent,
        string timing,
        int listenerCardInstanceId,
        int abilityIndex,
        int effectIndex)
    {
        if (state == null || actor == null || resolution == null || state.Players == null)
            return GameRuleResult.Rejected("Invalid simultaneous pass effect.", resolution != null ? resolution.Events.SnapshotHistory() : null);

        List<PlayerStateSnapshot> participants = PlayersFromActorToLeft(state, actor);
        if (participants.Count <= 1) return GameRuleResult.Applied(resolution.Events.SnapshotHistory());

        resolution.ClearStagedCardSelections();
        List<string> remaining = new List<string>();
        for (int index = 1; index < participants.Count; index++) remaining.Add(participants[index].PlayerId);
        PlayerStateSnapshot first = participants[0];
        if (!resolution.TrySuspendForDecision(first.PlayerId, SimultaneousPassLeftOperation, "hand",
                string.IsNullOrWhiteSpace(prompt) ? "Choisissez une carte à passer à votre gauche." : prompt,
                sourceCardInstanceId, 1, 1, first.Hand, triggerEvent, timing, listenerCardInstanceId,
                abilityIndex, effectIndex, out string error))
            return GameRuleResult.Rejected(error, resolution.Events.SnapshotHistory());
        resolution.PendingDecision.RemainingPlayerIds.AddRange(remaining);
        return GameRuleResult.WaitingForChoice(resolution.Events.SnapshotHistory());
    }

    public static GameRuleResult TryStartReplaceEachOtherTopCard(
        GameStateSnapshot state,
        PlayerStateSnapshot actor,
        ResolutionQueue resolution,
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
        if (state == null || actor == null || resolution == null || resolve == null || state.Players == null)
            return GameRuleResult.Rejected("Invalid replace-top-card effect.", resolution != null ? resolution.Events.SnapshotHistory() : null);
        List<string> targets = OtherPlayersFromActorToLeft(state, actor, resolution);
        return ContinueReplaceEachOtherTopCard(state, actor, resolution, targets, prompt, sourceCardInstanceId,
            triggerEvent, timing, listenerCardInstanceId, abilityIndex, effectIndex, resolve, random);
    }

    public static GameRuleResult ResolveSupplyContinuation(
        GameStateSnapshot state,
        PlayerStateSnapshot actor,
        ResolutionQueue resolution,
        PendingDecisionSnapshot continuation,
        Func<string, ExtensionCardData> resolve,
        System.Random random)
    {
        if (state == null || actor == null || resolution == null || continuation == null || resolve == null ||
            !IsSupplyContinuation(continuation.Operation))
            return GameRuleResult.Rejected("Replace-top-card continuation is invalid.", resolution != null ? resolution.Events.SnapshotHistory() : null);

        string targetPlayerId = continuation.Operation.Substring(ReplaceEachOtherTopPrefix.Length);
        PlayerStateSnapshot target = FindPlayer(state, targetPlayerId);
        List<string> selected = resolution.TakeSelectedDefinitionIds();
        if (target == null || selected.Count != 1)
            return GameRuleResult.Rejected("Replace-top-card gain selection is invalid.", resolution.Events.SnapshotHistory());
        if (!GainRules.TryGainFromSupply(state, target, selected[0], CardZone.Discard,
                continuation.SourceCardInstanceId, resolution.Events, out _, out string gainError))
            return GameRuleResult.Rejected(gainError, resolution.Events.SnapshotHistory());

        List<string> remaining = continuation.RemainingPlayerIds != null
            ? new List<string>(continuation.RemainingPlayerIds)
            : new List<string>();
        return ContinueReplaceEachOtherTopCard(state, actor, resolution, remaining, continuation.Prompt,
            continuation.SourceCardInstanceId, RestoreEvent(continuation), continuation.Timing,
            continuation.ListenerCardInstanceId, continuation.AbilityIndex, continuation.EffectIndex, resolve, random);
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
        if (string.Equals(operation, SimultaneousPassLeftOperation, StringComparison.OrdinalIgnoreCase))
            return ResolveSimultaneousPassLeftDecision(state, player, resolution, continuation);

        return GameRuleResult.Rejected("Unsupported advanced action continuation: " + operation, resolution.Events.SnapshotHistory());
    }

    private static GameRuleResult ResolveSimultaneousPassLeftDecision(
        GameStateSnapshot state,
        PlayerStateSnapshot player,
        ResolutionQueue resolution,
        PendingDecisionSnapshot continuation)
    {
        List<int> selected = resolution.TakeSelectedInstanceIds();
        if (selected.Count != 1 || player.Hand == null || !player.Hand.Contains(selected[0]))
            return GameRuleResult.Rejected("Simultaneous pass selection is invalid.", resolution.Events.SnapshotHistory());
        if (!resolution.TryStageCardSelection(player.PlayerId, selected[0], out string stageError))
            return GameRuleResult.Rejected(stageError, resolution.Events.SnapshotHistory());

        List<string> remaining = continuation.RemainingPlayerIds != null
            ? new List<string>(continuation.RemainingPlayerIds)
            : new List<string>();
        while (remaining.Count > 0)
        {
            string playerId = remaining[0]; remaining.RemoveAt(0);
            PlayerStateSnapshot next = FindPlayer(state, playerId);
            if (next == null || next.Hand == null || next.Hand.Count == 0)
                return GameRuleResult.Rejected("A simultaneous pass participant is no longer eligible.", resolution.Events.SnapshotHistory());
            if (!resolution.TrySuspendForDecision(next.PlayerId, SimultaneousPassLeftOperation, "hand", continuation.Prompt,
                    continuation.SourceCardInstanceId, 1, 1, next.Hand, RestoreEvent(continuation), continuation.Timing,
                    continuation.ListenerCardInstanceId, continuation.AbilityIndex, continuation.EffectIndex, out string error))
                return GameRuleResult.Rejected(error, resolution.Events.SnapshotHistory());
            resolution.PendingDecision.RemainingPlayerIds.AddRange(remaining);
            return GameRuleResult.WaitingForChoice(resolution.Events.SnapshotHistory());
        }

        return ApplyStagedPassLeft(state, resolution);
    }

    private static GameRuleResult ApplyStagedPassLeft(GameStateSnapshot state, ResolutionQueue resolution)
    {
        List<string> playerIds = new List<string>(resolution.StagedSelectionPlayerIds);
        List<int> cardIds = new List<int>(resolution.StagedSelectedInstanceIds);
        if (playerIds.Count < 2 || playerIds.Count != cardIds.Count)
            return GameRuleResult.Rejected("Simultaneous pass staging is incomplete.", resolution.Events.SnapshotHistory());

        List<PlayerStateSnapshot> players = new List<PlayerStateSnapshot>();
        List<CardInstance> cards = new List<CardInstance>();
        for (int index = 0; index < playerIds.Count; index++)
        {
            PlayerStateSnapshot owner = FindPlayer(state, playerIds[index]);
            CardInstance card = FindCard(state, cardIds[index]);
            if (owner == null || owner.Hand == null || !owner.Hand.Contains(cardIds[index]) || card == null ||
                !string.Equals(card.OwnerPlayerId, owner.PlayerId, StringComparison.Ordinal))
                return GameRuleResult.Rejected("A staged pass card is no longer owned in hand.", resolution.Events.SnapshotHistory());
            players.Add(owner); cards.Add(card);
        }

        for (int index = 0; index < players.Count; index++) players[index].Hand.Remove(cardIds[index]);
        for (int index = 0; index < players.Count; index++)
        {
            PlayerStateSnapshot recipient = players[(index + 1) % players.Count];
            recipient.Hand.Add(cardIds[index]);
            cards[index].OwnerPlayerId = recipient.PlayerId;
        }
        resolution.ClearStagedCardSelections();
        return GameRuleResult.Applied(resolution.Events.SnapshotHistory());
    }

    private static List<PlayerStateSnapshot> PlayersFromActorToLeft(GameStateSnapshot state, PlayerStateSnapshot actor)
    {
        List<PlayerStateSnapshot> result = new List<PlayerStateSnapshot>();
        if (state == null || state.Players == null || state.Players.Count == 0 || actor == null) return result;
        int actorIndex = state.Players.FindIndex(candidate => candidate != null && candidate.PlayerId == actor.PlayerId);
        if (actorIndex < 0) return result;
        for (int offset = 0; offset < state.Players.Count; offset++)
        {
            PlayerStateSnapshot candidate = state.Players[(actorIndex + offset) % state.Players.Count];
            if (candidate != null && candidate.Hand != null && candidate.Hand.Count > 0) result.Add(candidate);
        }
        return result;
    }

    private static List<string> OtherPlayersFromActorToLeft(GameStateSnapshot state, PlayerStateSnapshot actor, ResolutionQueue resolution)
    {
        List<string> result = new List<string>();
        if (state == null || state.Players == null || state.Players.Count <= 1 || actor == null) return result;
        int actorIndex = state.Players.FindIndex(candidate => candidate != null && candidate.PlayerId == actor.PlayerId);
        if (actorIndex < 0) return result;
        for (int offset = 1; offset < state.Players.Count; offset++)
        {
            PlayerStateSnapshot candidate = state.Players[(actorIndex + offset) % state.Players.Count];
            if (candidate != null && (resolution == null || !resolution.IsAttackProtected(candidate.PlayerId)))
                result.Add(candidate.PlayerId);
        }
        return result;
    }

    private static GameRuleResult ContinueReplaceEachOtherTopCard(
        GameStateSnapshot state,
        PlayerStateSnapshot actor,
        ResolutionQueue resolution,
        List<string> remainingPlayerIds,
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
        List<string> remaining = remainingPlayerIds != null ? new List<string>(remainingPlayerIds) : new List<string>();
        while (remaining.Count > 0)
        {
            string targetId = remaining[0]; remaining.RemoveAt(0);
            PlayerStateSnapshot target = FindPlayer(state, targetId);
            if (target == null) continue;
            if (!TrashRules.TryTrashTopCardOfDeck(state, target, random, sourceCardInstanceId, resolution.Events,
                    out int trashedInstanceId, out string trashError))
                return GameRuleResult.Rejected(trashError, resolution.Events.SnapshotHistory());
            if (trashedInstanceId <= 0) continue;
            CardInstance trashed = FindCard(state, trashedInstanceId);
            ExtensionCardData trashedDefinition = trashed != null ? resolve(trashed.DefinitionId) : null;
            if (trashedDefinition == null)
                return GameRuleResult.Rejected("Trashed top-card definition could not be resolved.", resolution.Events.SnapshotHistory());

            List<string> candidates = ExactCostSupplyCandidates(state, trashedDefinition.cost, resolve);
            if (candidates.Count == 0) continue;
            string operation = ReplaceEachOtherTopPrefix + target.PlayerId;
            if (!resolution.TrySuspendForSupplyDecision(actor.PlayerId, operation,
                    string.IsNullOrWhiteSpace(prompt) ? "Choisissez la carte que cet adversaire reçoit." : prompt,
                    sourceCardInstanceId, 1, 1, candidates, triggerEvent, timing, listenerCardInstanceId,
                    abilityIndex, effectIndex, out string suspendError))
                return GameRuleResult.Rejected(suspendError, resolution.Events.SnapshotHistory());
            resolution.PendingDecision.RemainingPlayerIds.AddRange(remaining);
            return GameRuleResult.WaitingForChoice(resolution.Events.SnapshotHistory());
        }
        return GameRuleResult.Applied(resolution.Events.SnapshotHistory());
    }

    private static List<string> ExactCostSupplyCandidates(GameStateSnapshot state, int cost,
        Func<string, ExtensionCardData> resolve)
    {
        List<string> result = new List<string>();
        if (state == null || state.SupplyPiles == null || resolve == null || cost < 0) return result;
        foreach (SupplyPileSnapshot pile in state.SupplyPiles)
        {
            if (pile == null || pile.RemainingCount <= 0 || string.IsNullOrEmpty(pile.DefinitionId)) continue;
            ExtensionCardData definition = resolve(pile.DefinitionId);
            if (definition != null && definition.cost == cost) result.Add(pile.DefinitionId);
        }
        return result;
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

    private static PlayerStateSnapshot FindPlayer(GameStateSnapshot state, string playerId)
    {
        return state != null && state.Players != null && !string.IsNullOrEmpty(playerId)
            ? state.Players.Find(player => player != null && player.PlayerId == playerId)
            : null;
    }

    private static GameEvent RestoreEvent(PendingDecisionSnapshot continuation)
    {
        return continuation != null && continuation.TriggerEvent != null && continuation.TriggerEvent.TryToRuntime(out GameEvent gameEvent)
            ? gameEvent
            : null;
    }
}
