using System;
using System.Collections.Generic;

/// <summary>Durable, reconnect-safe scheduling for cards kept between turns.</summary>
public static class SetAsideRules
{
    public const string ReturnToHand = "hand";
    public const string PlayAtTurnStart = "play";
    public const string PlayActionOtherwiseHand = "play_action_or_hand";
    public const string ReturnToSupplyAtTurnEnd = "supply_at_turn_end";

    public static bool TryScheduleFromZone(GameStateSnapshot state, PlayerStateSnapshot player, CardZone sourceZone,
        int instanceId, int sourceCardInstanceId, string returnMode, out string error)
    {
        error = string.Empty;
        if (state == null || player == null || instanceId <= 0 || string.IsNullOrWhiteSpace(returnMode))
        { error = "Set-aside request is incomplete."; return false; }
        List<int> source = CardZoneRules.ResolveZone(state, player, sourceZone);
        if (source == null || !source.Remove(instanceId))
        { error = "Set-aside card is not in the requested source zone."; return false; }
        return AddSchedule(state, player, instanceId, sourceCardInstanceId, returnMode, out error);
    }

    public static bool TryScheduleTopDeck(GameStateSnapshot state, PlayerStateSnapshot player, System.Random random,
        int sourceCardInstanceId, string returnMode, out string error)
    {
        error = string.Empty;
        if (!CardZoneRules.TryMoveTopCardFromDeck(player, CardZone.Inspected, random, out int instanceId, out error)) return false;
        if (instanceId <= 0) return true;
        return TryScheduleFromZone(state, player, CardZone.Inspected, instanceId, sourceCardInstanceId, returnMode, out error);
    }

    public static bool TryResolveTurnStart(GameStateSnapshot state, PlayerStateSnapshot player, ResolutionQueue queue,
        Func<string, ExtensionCardData> resolve, out string error)
    {
        error = string.Empty;
        if (state == null || player == null || queue == null || resolve == null)
        { error = "Set-aside turn-start resolution is incomplete."; return false; }
        if (state.SetAsideCards == null) state.SetAsideCards = new List<SetAsideCardSnapshot>();
        foreach (SetAsideCardSnapshot entry in state.SetAsideCards.ToArray())
        {
            if (entry == null || entry.PlayerId != player.PlayerId || entry.DueTurnNumber > state.TurnNumber ||
                string.Equals(entry.ReturnMode, ReturnToSupplyAtTurnEnd, StringComparison.OrdinalIgnoreCase)) continue;
            CardInstance card = Find(state, entry.CardInstanceId);
            ExtensionCardData definition = card != null ? resolve(card.DefinitionId) : null;
            if (card == null || definition == null)
            { error = "A due set-aside card could not be resolved."; return false; }
            bool play = string.Equals(entry.ReturnMode, PlayAtTurnStart, StringComparison.OrdinalIgnoreCase) ||
                (string.Equals(entry.ReturnMode, PlayActionOtherwiseHand, StringComparison.OrdinalIgnoreCase) &&
                 CardDefinitionRules.HasType(definition, "Action"));
            (play ? player.InPlay : player.Hand).Add(card.InstanceId);
            state.SetAsideCards.Remove(entry);
            if (play)
            {
                if (CardDefinitionRules.HasType(definition, "Action")) player.ActionsPlayedThisTurn++;
                queue.Events.Publish(GameEvent.CardPlayed(player.PlayerId, card.InstanceId, card.DefinitionId));
            }
            MarkDurationSourceResolved(state, player, entry.SourceCardInstanceId, resolve);
        }
        return true;
    }

    public static bool TryResolveTurnEnd(GameStateSnapshot state, PlayerStateSnapshot player, out string error)
    {
        error = string.Empty;
        if (state == null || player == null) { error = "Set-aside turn-end resolution is incomplete."; return false; }
        if (state.SetAsideCards == null) return true;
        foreach (SetAsideCardSnapshot entry in state.SetAsideCards.ToArray())
        {
            if (entry == null || entry.PlayerId != player.PlayerId ||
                !string.Equals(entry.ReturnMode, ReturnToSupplyAtTurnEnd, StringComparison.OrdinalIgnoreCase)) continue;
            CardInstance card = Find(state, entry.CardInstanceId);
            SupplyPileSnapshot pile = card != null && state.SupplyPiles != null
                ? state.SupplyPiles.Find(candidate => candidate != null &&
                    string.Equals(candidate.DefinitionId, card.DefinitionId, StringComparison.OrdinalIgnoreCase))
                : null;
            if (card == null || pile == null)
            { error = "Set-aside card cannot return to its Supply pile."; return false; }
            pile.RemainingCount++;
            state.CardInstances.Remove(card);
            state.SetAsideCards.Remove(entry);
        }
        return true;
    }

    private static bool AddSchedule(GameStateSnapshot state, PlayerStateSnapshot player, int instanceId,
        int sourceCardInstanceId, string returnMode, out string error)
    {
        error = string.Empty;
        if (state.SetAsideCards == null) state.SetAsideCards = new List<SetAsideCardSnapshot>();
        if (state.SetAsideCards.Exists(entry => entry != null && entry.CardInstanceId == instanceId))
        { error = "Card is already set aside."; return false; }
        state.SetAsideCards.Add(new SetAsideCardSnapshot
        {
            PlayerId = player.PlayerId,
            CardInstanceId = instanceId,
            SourceCardInstanceId = sourceCardInstanceId,
            DueTurnNumber = NextTurnNumberFor(state, player.PlayerId),
            ReturnMode = returnMode
        });
        return true;
    }

    private static int NextTurnNumberFor(GameStateSnapshot state, string playerId)
    {
        if (state.Players == null || state.Players.Count == 0) return state.TurnNumber + 1;
        int active = state.Players.FindIndex(player => player != null && player.PlayerId == state.ActivePlayerId);
        int target = state.Players.FindIndex(player => player != null && player.PlayerId == playerId);
        if (active < 0 || target < 0) return state.TurnNumber + state.Players.Count;
        int delta = (target - active + state.Players.Count) % state.Players.Count;
        if (delta == 0) delta = state.Players.Count;
        return state.TurnNumber + delta;
    }

    private static void MarkDurationSourceResolved(GameStateSnapshot state, PlayerStateSnapshot player, int sourceId,
        Func<string, ExtensionCardData> resolve)
    {
        if (sourceId > 0 && player.InPlay != null && player.InPlay.Contains(sourceId))
            DurationRules.TryMarkResolved(state, player, sourceId, resolve, out _);
    }

    private static CardInstance Find(GameStateSnapshot state, int instanceId) =>
        state.CardInstances != null ? state.CardInstances.Find(card => card != null && card.InstanceId == instanceId) : null;
}
