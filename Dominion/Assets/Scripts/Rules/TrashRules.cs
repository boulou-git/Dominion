using System;
using System.Collections.Generic;

/// <summary>
/// Shared deterministic rules for trashing cards. This is the only rules primitive that
/// removes a physical card from a player zone and places it in the match-wide trash zone.
/// </summary>
public static class TrashRules
{
    public static bool TryTrashFromHand(
        GameStateSnapshot state,
        PlayerStateSnapshot owner,
        int instanceId,
        int sourceCardInstanceId,
        GameEventBus eventBus,
        out string error)
    {
        return TryTrashFromZone(state, owner, CardZone.Hand, instanceId, sourceCardInstanceId, eventBus, out error);
    }

    public static bool TryTrashFromZone(
        GameStateSnapshot state,
        PlayerStateSnapshot owner,
        CardZone sourceZone,
        int instanceId,
        int sourceCardInstanceId,
        GameEventBus eventBus,
        out string error)
    {
        error = string.Empty;

        if (state == null)
        {
            error = "Game state is null.";
            return false;
        }

        if (owner == null || string.IsNullOrEmpty(owner.PlayerId))
        {
            error = "Trash owner is missing.";
            return false;
        }

        if (sourceZone == CardZone.None)
        {
            error = "Trash source zone is invalid.";
            return false;
        }

        if (instanceId <= 0)
        {
            error = "Trash card instance id is invalid.";
            return false;
        }

        List<int> source = CardZoneRules.ResolveZone(owner, sourceZone);
        if (source == null || !source.Contains(instanceId))
        {
            error = "Card is not in the expected trash source zone.";
            return false;
        }

        CardInstance instance = FindCardInstance(state, instanceId);
        if (instance == null)
        {
            error = "Trash card instance was not found.";
            return false;
        }

        if (!string.Equals(instance.OwnerPlayerId, owner.PlayerId, StringComparison.Ordinal))
        {
            error = "Trash card does not belong to the expected player.";
            return false;
        }

        if (state.TrashedCards == null)
            state.TrashedCards = new List<int>();

        if (!CardZoneRules.MoveCard(source, state.TrashedCards, instanceId))
        {
            error = "Could not move card to trash.";
            return false;
        }

        eventBus?.Publish(new GameEvent(
            GameEventType.CardTrashed,
            owner.PlayerId,
            instanceId,
            instance.DefinitionId,
            sourceCardInstanceId,
            CardZone.None));

        return true;
    }

    private static CardInstance FindCardInstance(GameStateSnapshot state, int instanceId)
    {
        if (state == null || state.CardInstances == null)
            return null;

        return state.CardInstances.Find(card => card != null && card.InstanceId == instanceId);
    }
}
