using System;
using System.Collections.Generic;

/// <summary>
/// Pure card-instance creation rules. This is the only place that allocates a new
/// CardInstance id and attaches the new instance to a resolvable player or match zone.
/// It deliberately does not decide why the card exists (setup, gain, reward, etc.).
/// </summary>
public static class CardInstanceRules
{
    public static bool TryCreateOwnedCard(
        GameStateSnapshot state,
        PlayerStateSnapshot owner,
        string definitionId,
        CardZone destination,
        out int instanceId,
        out string error)
    {
        instanceId = 0;
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

        if (string.IsNullOrEmpty(definitionId))
        {
            error = "Card definition id is missing.";
            return false;
        }

        if (state.CardInstances == null)
        {
            error = "Card instance collection is missing.";
            return false;
        }

        List<int> destinationZone = CardZoneRules.ResolveZone(state, owner, destination);
        if (destinationZone == null)
        {
            error = "Unsupported destination zone: " + destination;
            return false;
        }

        if (state.NextCardInstanceId < 1)
        {
            error = "Next card instance id is invalid.";
            return false;
        }

        instanceId = state.NextCardInstanceId++;
        state.CardInstances.Add(new CardInstance(instanceId, definitionId, owner.PlayerId));
        destinationZone.Add(instanceId);
        return true;
    }
}
