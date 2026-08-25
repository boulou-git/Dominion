using System;
using System.Collections.Generic;

/// <summary>
/// Shared validation for mutations of an existing owned card.
/// This class deliberately does not emit gameplay events: discard, trash and other
/// semantic rules stay responsible for their own side effects and event types.
/// </summary>
public static class CardMutationRules
{
    public static bool TryResolveOwnedCardInZone(
        GameStateSnapshot state,
        PlayerStateSnapshot owner,
        CardZone sourceZone,
        int instanceId,
        out CardInstance instance,
        out List<int> source,
        out string error)
    {
        instance = null;
        source = null;
        error = string.Empty;

        if (state == null)
        {
            error = "Game state is null.";
            return false;
        }

        if (owner == null || string.IsNullOrEmpty(owner.PlayerId))
        {
            error = "Card owner is missing.";
            return false;
        }

        if (sourceZone == CardZone.None)
        {
            error = "Card source zone is invalid.";
            return false;
        }

        if (instanceId <= 0)
        {
            error = "Card instance id is invalid.";
            return false;
        }

        source = CardZoneRules.ResolveZone(owner, sourceZone);
        if (source == null || !source.Contains(instanceId))
        {
            error = "Card is not in the expected source zone.";
            return false;
        }

        if (state.CardInstances == null)
        {
            error = "Card instance collection is missing.";
            return false;
        }

        instance = state.CardInstances.Find(card => card != null && card.InstanceId == instanceId);
        if (instance == null)
        {
            error = "Card instance was not found.";
            return false;
        }

        if (!string.Equals(instance.OwnerPlayerId, owner.PlayerId, StringComparison.Ordinal))
        {
            error = "Card does not belong to the expected player.";
            return false;
        }

        return true;
    }
}
