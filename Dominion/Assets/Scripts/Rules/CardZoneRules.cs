using System;
using System.Collections.Generic;

/// <summary>
/// Pure helpers for manipulating Dominion card zones.
///
/// A deck's top card is the last item in its list. These helpers deliberately know
/// nothing about Photon, UI or card definitions, so setup, cleanup and card effects
/// all share exactly the same movement/shuffle semantics.
/// </summary>
public static class CardZoneRules
{
    public static bool MoveCard(List<int> source, List<int> destination, int instanceId)
    {
        if (source == null || destination == null || instanceId <= 0)
            return false;

        int index = source.IndexOf(instanceId);
        if (index < 0)
            return false;

        source.RemoveAt(index);
        destination.Add(instanceId);
        return true;
    }

    /// <summary>
    /// Moves every card from source to destination. When reverseOrder is true, cards
    /// are appended from source right-to-left before source is cleared.
    /// </summary>
    public static bool MoveAll(List<int> source, List<int> destination, bool reverseOrder = false)
    {
        if (source == null || destination == null)
            return false;

        if (ReferenceEquals(source, destination))
            return false;

        if (reverseOrder)
        {
            for (int i = source.Count - 1; i >= 0; i--)
                destination.Add(source[i]);
        }
        else
        {
            destination.AddRange(source);
        }

        source.Clear();
        return true;
    }

    public static bool Shuffle(List<int> cards, System.Random random)
    {
        if (cards == null || random == null)
            return false;

        for (int i = cards.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            int temp = cards[i];
            cards[i] = cards[j];
            cards[j] = temp;
        }

        return true;
    }

    /// <summary>
    /// Draws up to count cards. If the deck empties, the discard pile is moved into
    /// the deck and shuffled before drawing continues. Running out of all cards is a
    /// normal successful short draw; malformed zones or missing required randomness reject.
    /// </summary>
    public static bool DrawCards(
        PlayerStateSnapshot player,
        int count,
        System.Random random,
        out string error)
    {
        error = string.Empty;

        if (player == null)
        {
            error = "Player is null.";
            return false;
        }

        if (count < 0)
        {
            error = "Draw count cannot be negative.";
            return false;
        }

        if (player.Deck == null || player.Hand == null || player.Discard == null)
        {
            error = "Draw requires deck, hand and discard zones.";
            return false;
        }

        for (int i = 0; i < count; i++)
        {
            if (player.Deck.Count == 0)
            {
                if (player.Discard.Count == 0)
                    break;

                if (random == null)
                {
                    error = "Draw requires an injected random source when the discard pile must be shuffled.";
                    return false;
                }

                if (!MoveAll(player.Discard, player.Deck) || !Shuffle(player.Deck, random))
                {
                    error = "Could not reshuffle the discard pile into the deck.";
                    return false;
                }
            }

            int topIndex = player.Deck.Count - 1;
            int instanceId = player.Deck[topIndex];
            player.Deck.RemoveAt(topIndex);
            player.Hand.Add(instanceId);
        }

        return true;
    }
}
