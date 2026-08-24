using System.Collections.Generic;

/// <summary>
/// Generic discard operations. These rules mutate zones and emit semantic events but
/// know nothing about UI, Photon or specific cards.
/// </summary>
public static class DiscardRules
{
    public static bool TryDiscardSelectedFromHand(
        GameStateSnapshot state,
        PlayerStateSnapshot player,
        IEnumerable<int> selectedInstanceIds,
        int sourceCardInstanceId,
        GameEventBus eventBus,
        out string error)
    {
        return TryDiscardSelected(state, player, CardZone.Hand, selectedInstanceIds, sourceCardInstanceId, eventBus, out error);
    }

    public static bool TryDiscardSelected(
        GameStateSnapshot state,
        PlayerStateSnapshot player,
        CardZone sourceZone,
        IEnumerable<int> selectedInstanceIds,
        int sourceCardInstanceId,
        GameEventBus eventBus,
        out string error)
    {
        error = string.Empty;
        if (state == null || player == null)
        {
            error = "Discard requires a game state and player.";
            return false;
        }

        List<int> source = CardZoneRules.ResolveZone(player, sourceZone);
        if (source == null || player.Discard == null)
        {
            error = "Discard requires valid source and discard zones.";
            return false;
        }
        if (eventBus == null)
        {
            error = "Discard requires an active game event bus.";
            return false;
        }

        List<int> selected = selectedInstanceIds != null
            ? new List<int>(selectedInstanceIds)
            : new List<int>();

        foreach (int instanceId in selected)
        {
            CardInstance instance = state.CardInstances != null
                ? state.CardInstances.Find(card => card != null && card.InstanceId == instanceId)
                : null;
            if (instance == null)
            {
                error = "Selected discard card instance was not found: " + instanceId;
                return false;
            }
            if (instance.OwnerPlayerId != player.PlayerId || !source.Contains(instanceId))
            {
                error = "Selected discard card is not in the expected source zone: " + instanceId;
                return false;
            }
        }

        foreach (int instanceId in selected)
        {
            CardInstance instance = state.CardInstances.Find(card => card != null && card.InstanceId == instanceId);
            if (!CardZoneRules.MoveCard(player, sourceZone, CardZone.Discard, instanceId))
            {
                error = "Could not move selected card to discard: " + instanceId;
                return false;
            }

            eventBus.Publish(GameEvent.CardDiscarded(
                player.PlayerId,
                instanceId,
                instance.DefinitionId,
                sourceCardInstanceId));
        }

        return true;
    }
}
