using System;
using System.Collections.Generic;

/// <summary>Tracks the two-cleanup lifetime of Action — Duration cards.</summary>
public static class DurationRules
{
    public static bool TryMarkResolved(GameStateSnapshot state, PlayerStateSnapshot player, int instanceId,
        Func<string, ExtensionCardData> resolve, out string error)
    {
        error = string.Empty;
        if (state == null || player == null || instanceId <= 0 || resolve == null ||
            player.InPlay == null || !player.InPlay.Contains(instanceId))
        { error = "Resolved Duration source is not in play."; return false; }
        CardInstance instance = state.CardInstances != null
            ? state.CardInstances.Find(card => card != null && card.InstanceId == instanceId)
            : null;
        if (instance == null || !CardDefinitionRules.HasType(resolve(instance.DefinitionId), "Durée"))
        { error = "Resolved Duration source is not a Duration card."; return false; }
        if (player.ResolvedDurationCards == null) player.ResolvedDurationCards = new List<int>();
        if (!player.ResolvedDurationCards.Contains(instanceId)) player.ResolvedDurationCards.Add(instanceId);
        return true;
    }

    public static void MoveCleanupInPlayCards(GameStateSnapshot state, PlayerStateSnapshot player,
        Func<string, ExtensionCardData> resolve)
    {
        if (state == null || player == null || player.InPlay == null || player.Discard == null || resolve == null) return;
        if (player.ResolvedDurationCards == null) player.ResolvedDurationCards = new List<int>();
        foreach (int instanceId in player.InPlay.ToArray())
        {
            CardInstance instance = state.CardInstances != null
                ? state.CardInstances.Find(card => card != null && card.InstanceId == instanceId)
                : null;
            bool duration = instance != null && CardDefinitionRules.HasType(resolve(instance.DefinitionId), "Durée");
            if (duration && !player.ResolvedDurationCards.Contains(instanceId)) continue;
            CardZoneRules.MoveCard(player, CardZone.InPlay, CardZone.Discard, instanceId);
            player.ResolvedDurationCards.Remove(instanceId);
        }
    }
}
