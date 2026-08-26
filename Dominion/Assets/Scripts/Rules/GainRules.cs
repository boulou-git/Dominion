using System;
using System.Collections.Generic;

/// <summary>
/// Shared deterministic rules for gaining cards.
/// Supply gains create a new physical instance and decrement a Reserve pile.
/// Trash gains reuse an existing physical instance from the match-wide trash.
/// </summary>
public static class GainRules
{
    public static bool CanGainFromSupply(GameStateSnapshot state, string definitionId, out string error)
    {
        error = string.Empty;

        if (state == null)
        {
            error = "Game state is null.";
            return false;
        }

        if (string.IsNullOrEmpty(definitionId))
        {
            error = "Gain card definition id is missing.";
            return false;
        }

        SupplyPileSnapshot pile = FindSupplyPile(state, definitionId);
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

        return true;
    }

    public static bool TryGainFromSupply(
        GameStateSnapshot state,
        PlayerStateSnapshot owner,
        string definitionId,
        CardZone destination,
        int sourceCardInstanceId,
        GameEventBus eventBus,
        out int gainedInstanceId,
        out string error)
    {
        gainedInstanceId = 0;
        error = string.Empty;

        if (owner == null || string.IsNullOrEmpty(owner.PlayerId))
        {
            error = "Gain owner is missing.";
            return false;
        }

        if (destination == CardZone.None || destination == CardZone.Trash)
        {
            error = "Supply gain requires a player-owned destination zone.";
            return false;
        }

        if (!CanGainFromSupply(state, definitionId, out error))
            return false;

        SupplyPileSnapshot pile = FindSupplyPile(state, definitionId);
        if (!CardInstanceRules.TryCreateOwnedCard(
                state,
                owner,
                definitionId,
                destination,
                out gainedInstanceId,
                out error))
            return false;

        pile.RemainingCount--;

        if (eventBus != null)
        {
            eventBus.Publish(GameEvent.CardGained(
                owner.PlayerId,
                gainedInstanceId,
                definitionId,
                destination,
                sourceCardInstanceId));

            if (pile.RemainingCount == 0)
                eventBus.Publish(GameEvent.PileEmptied(definitionId, sourceCardInstanceId));
        }

        return true;
    }

    /// <summary>
    /// Gains one existing physical card from the match-wide trash.
    /// Unlike a Supply gain, no new CardInstance is created and no pile count changes.
    /// Ownership is transferred to the gaining player before CardGained is emitted.
    /// </summary>
    public static bool TryGainFromTrash(
        GameStateSnapshot state,
        PlayerStateSnapshot newOwner,
        int instanceId,
        CardZone destination,
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

        if (newOwner == null || string.IsNullOrEmpty(newOwner.PlayerId))
        {
            error = "Gain owner is missing.";
            return false;
        }

        if (instanceId <= 0)
        {
            error = "Trash gain requires a valid card instance id.";
            return false;
        }

        if (destination == CardZone.None || destination == CardZone.Trash)
        {
            error = "Trash gain requires a player-owned destination zone.";
            return false;
        }

        List<int> trash = CardZoneRules.ResolveZone(state, newOwner, CardZone.Trash);
        List<int> destinationZone = CardZoneRules.ResolveZone(newOwner, destination);
        if (trash == null || destinationZone == null)
        {
            error = "Trash gain source or destination zone is unavailable.";
            return false;
        }

        if (!trash.Contains(instanceId))
        {
            error = "Card is not present in the trash: " + instanceId;
            return false;
        }

        CardInstance instance = state.CardInstances != null
            ? state.CardInstances.Find(card => card != null && card.InstanceId == instanceId)
            : null;
        if (instance == null)
        {
            error = "Trashed card instance could not be resolved: " + instanceId;
            return false;
        }

        if (!CardZoneRules.MoveCard(trash, destinationZone, instanceId))
        {
            error = "Could not move card from trash to destination.";
            return false;
        }

        instance.OwnerPlayerId = newOwner.PlayerId;

        eventBus?.Publish(GameEvent.CardGained(
            newOwner.PlayerId,
            instance.InstanceId,
            instance.DefinitionId,
            destination,
            sourceCardInstanceId));

        return true;
    }

    private static SupplyPileSnapshot FindSupplyPile(GameStateSnapshot state, string definitionId)
    {
        if (state == null || state.SupplyPiles == null || string.IsNullOrEmpty(definitionId))
            return null;

        return state.SupplyPiles.Find(pile =>
            pile != null &&
            string.Equals(pile.DefinitionId, definitionId, StringComparison.OrdinalIgnoreCase));
    }
}
