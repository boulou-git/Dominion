using System;

/// <summary>
/// Shared deterministic rules for gaining cards from the Reserve.
/// Buying a card and future declarative gain effects must converge here so pile counts,
/// instance creation, destination and emitted events cannot drift apart.
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

    private static SupplyPileSnapshot FindSupplyPile(GameStateSnapshot state, string definitionId)
    {
        if (state == null || state.SupplyPiles == null || string.IsNullOrEmpty(definitionId))
            return null;

        return state.SupplyPiles.Find(pile =>
            pile != null &&
            string.Equals(pile.DefinitionId, definitionId, StringComparison.OrdinalIgnoreCase));
    }
}
