using System.Collections.Generic;

/// <summary>
/// Shared deterministic rules for trashing cards. This is the semantic layer that
/// moves a physical owned card into the match-wide trash and emits CardTrashed.
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

        if (!CardMutationRules.TryResolveOwnedCardInZone(
                state,
                owner,
                sourceZone,
                instanceId,
                out CardInstance instance,
                out List<int> source,
                out string validationError))
        {
            error = "Could not trash card: " + validationError;
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
}
