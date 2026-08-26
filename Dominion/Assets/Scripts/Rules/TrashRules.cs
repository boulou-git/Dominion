using System;
using System.Collections.Generic;

/// <summary>
/// Shared deterministic rules for trashing cards. This is the semantic layer that
/// moves a physical owned card into the match-wide trash and emits CardTrashed.
/// </summary>
public static class TrashRules
{
    /// <summary>
    /// Trashes the top card of a Supply pile by creating its physical instance directly
    /// in the match-wide trash. The acting player is recorded as its initial owner so
    /// snapshots retain a valid owner until a later gain transfers ownership.
    /// </summary>
    public static bool TryTrashFromSupply(
        GameStateSnapshot state,
        PlayerStateSnapshot actor,
        string definitionId,
        int sourceCardInstanceId,
        GameEventBus eventBus,
        out int trashedInstanceId,
        out string error)
    {
        trashedInstanceId = 0;
        error = string.Empty;

        if (state == null)
        {
            error = "Game state is null.";
            return false;
        }

        if (actor == null || string.IsNullOrEmpty(actor.PlayerId))
        {
            error = "Trash actor is missing.";
            return false;
        }

        if (string.IsNullOrEmpty(definitionId))
        {
            error = "Trash card definition id is missing.";
            return false;
        }

        SupplyPileSnapshot pile = state.SupplyPiles != null
            ? state.SupplyPiles.Find(candidate => candidate != null &&
                string.Equals(candidate.DefinitionId, definitionId, StringComparison.OrdinalIgnoreCase))
            : null;
        if (pile == null)
        {
            error = "Supply pile was not found: " + definitionId;
            return false;
        }

        if (pile.RemainingCount <= 0)
        {
            error = "Supply pile is empty: " + definitionId;
            return false;
        }

        if (!CardInstanceRules.TryCreateOwnedCard(
                state,
                actor,
                definitionId,
                CardZone.Trash,
                out trashedInstanceId,
                out error))
            return false;

        pile.RemainingCount--;
        eventBus?.Publish(GameEvent.CardTrashed(
            actor.PlayerId,
            trashedInstanceId,
            definitionId,
            sourceCardInstanceId));

        if (pile.RemainingCount == 0)
            eventBus?.Publish(GameEvent.PileEmptied(definitionId, sourceCardInstanceId));

        return true;
    }

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

    public static bool TryTrashTopCardOfDeck(
        GameStateSnapshot state,
        PlayerStateSnapshot owner,
        System.Random random,
        int sourceCardInstanceId,
        GameEventBus eventBus,
        out int trashedInstanceId,
        out string error)
    {
        trashedInstanceId = 0;
        if (state == null || owner == null)
        {
            error = "Top-card trash requires a game state and player.";
            return false;
        }
        if (!CardZoneRules.TryMoveTopCardFromDeck(owner, CardZone.Inspected, random, out int instanceId, out error))
            return false;
        if (instanceId <= 0) return true;
        if (!TryTrashFromZone(state, owner, CardZone.Inspected, instanceId, sourceCardInstanceId, eventBus, out error))
            return false;
        trashedInstanceId = instanceId;
        return true;
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
