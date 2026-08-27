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
        List<CardInstance> selectedInstances = new List<CardInstance>(selected.Count);

        // Validate the whole selection before mutating anything. This keeps discard
        // operations atomic when a stale/invalid decision reaches the rules layer.
        foreach (int instanceId in selected)
        {
            if (!CardMutationRules.TryResolveOwnedCardInZone(
                    state,
                    player,
                    sourceZone,
                    instanceId,
                    out CardInstance instance,
                    out _,
                    out string validationError))
            {
                error = "Selected discard card is invalid: " + instanceId + ". " + validationError;
                return false;
            }

            selectedInstances.Add(instance);
        }

        for (int index = 0; index < selected.Count; index++)
        {
            int instanceId = selected[index];
            CardInstance instance = selectedInstances[index];
            if (!CardZoneRules.MoveCard(source, player.Discard, instanceId))
            {
                error = "Could not move selected card to discard: " + instanceId;
                return false;
            }

            player.CardsDiscardedThisTurn++;
            eventBus.Publish(GameEvent.CardDiscarded(
                player.PlayerId,
                instanceId,
                instance.DefinitionId,
                sourceCardInstanceId));
        }

        return true;
    }
}
