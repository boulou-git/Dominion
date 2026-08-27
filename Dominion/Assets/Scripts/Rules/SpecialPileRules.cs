using System;
using System.Collections.Generic;

/// <summary>Deterministic operations for extension-owned piles outside the Supply.</summary>
public static class SpecialPileRules
{
    public static SpecialPileSnapshot Find(GameStateSnapshot state, string pileId)
    {
        if (state == null || state.SpecialPiles == null || string.IsNullOrWhiteSpace(pileId)) return null;
        return state.SpecialPiles.Find(pile => pile != null &&
            string.Equals(pile.PileId, pileId, StringComparison.OrdinalIgnoreCase));
    }

    public static bool TryGainTop(GameStateSnapshot state, PlayerStateSnapshot owner, string pileId,
        CardZone destination, int sourceCardInstanceId, GameEventBus eventBus,
        Func<string, ExtensionCardData> resolve, out int gainedInstanceId, out string error)
    {
        gainedInstanceId = 0;
        error = string.Empty;
        if (state == null || owner == null || string.IsNullOrEmpty(owner.PlayerId))
        { error = "Special-pile gain requires a state and owner."; return false; }
        List<int> destinationZone = CardZoneRules.ResolveZone(owner, destination);
        if (destinationZone == null)
        { error = "Special-pile gain destination is invalid."; return false; }
        SpecialPileSnapshot pile = Find(state, pileId);
        if (pile == null || pile.CardInstanceIds == null)
        { error = "Special pile was not found: " + pileId; return false; }
        if (pile.CardInstanceIds.Count == 0) return true;

        int topIndex = pile.CardInstanceIds.Count - 1;
        int instanceId = pile.CardInstanceIds[topIndex];
        CardInstance instance = state.CardInstances != null
            ? state.CardInstances.Find(card => card != null && card.InstanceId == instanceId)
            : null;
        if (instance == null || !string.IsNullOrEmpty(instance.OwnerPlayerId))
        { error = "Special pile contains an invalid physical card."; return false; }

        pile.CardInstanceIds.RemoveAt(topIndex);
        destinationZone.Add(instanceId);
        instance.OwnerPlayerId = owner.PlayerId;
        owner.CardsGainedThisTurn++;
        gainedInstanceId = instanceId;
        eventBus?.Publish(GameEvent.CardGained(owner.PlayerId, instanceId, instance.DefinitionId,
            destination, sourceCardInstanceId));
        ExtensionCardData definition = resolve != null ? resolve(instance.DefinitionId) : null;
        if (CardDefinitionRules.HasType(definition, "Maladie"))
            eventBus?.Publish(GameEvent.DiseaseGained(owner.PlayerId, instanceId, instance.DefinitionId,
                destination, sourceCardInstanceId));
        return true;
    }

    public static bool TryReturn(GameStateSnapshot state, PlayerStateSnapshot owner, int instanceId,
        string pileId, out string error)
    {
        error = string.Empty;
        if (state == null || owner == null) { error = "Special-pile return requires a state and owner."; return false; }
        SpecialPileSnapshot pile = Find(state, pileId);
        CardInstance instance = state.CardInstances != null
            ? state.CardInstances.Find(card => card != null && card.InstanceId == instanceId)
            : null;
        if (pile == null || instance == null || !string.Equals(instance.OwnerPlayerId, owner.PlayerId, StringComparison.Ordinal))
        { error = "Special-pile return is invalid."; return false; }

        List<int>[] zones = { owner.Deck, owner.Hand, owner.Discard, owner.InPlay, owner.Inspected };
        bool removed = false;
        foreach (List<int> zone in zones)
            if (zone != null && zone.Remove(instanceId)) { removed = true; break; }
        if (!removed) { error = "Returned card is not in an owned zone."; return false; }
        instance.OwnerPlayerId = string.Empty;
        pile.CardInstanceIds.Add(instanceId);
        return true;
    }
}
