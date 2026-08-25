using System;
using System.Collections.Generic;

/// <summary>
/// Stable zone vocabulary used by the deterministic rules layer.
/// None is reserved for events/operations that do not target a player card zone.
/// </summary>
public enum CardZone
{
    None,
    Deck,
    Hand,
    Discard,
    InPlay,
    Inspected,

    // Compatibility alias for in-progress snapshots/effects created before the
    // private inspected zone was named correctly. This does NOT imply public reveal.
    Revealed = Inspected
}

/// <summary>
/// Pure helpers for manipulating Dominion card zones.
/// A deck's top card is the last item in its list.
/// </summary>
public static class CardZoneRules
{
    public static bool TryParseZone(string value, out CardZone zone)
    {
        zone = CardZone.None;
        if (string.IsNullOrWhiteSpace(value)) return false;
        string normalized = value.Trim().Replace("_", string.Empty).Replace("-", string.Empty);
        if (string.Equals(normalized, "deck", StringComparison.OrdinalIgnoreCase)) { zone = CardZone.Deck; return true; }
        if (string.Equals(normalized, "hand", StringComparison.OrdinalIgnoreCase)) { zone = CardZone.Hand; return true; }
        if (string.Equals(normalized, "discard", StringComparison.OrdinalIgnoreCase)) { zone = CardZone.Discard; return true; }
        if (string.Equals(normalized, "inplay", StringComparison.OrdinalIgnoreCase)) { zone = CardZone.InPlay; return true; }
        if (string.Equals(normalized, "inspected", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "revealed", StringComparison.OrdinalIgnoreCase))
        { zone = CardZone.Inspected; return true; }
        return false;
    }

    public static List<int> ResolveZone(PlayerStateSnapshot player, CardZone zone)
    {
        if (player == null) return null;
        switch (zone)
        {
            case CardZone.Deck: return player.Deck;
            case CardZone.Hand: return player.Hand;
            case CardZone.Discard: return player.Discard;
            case CardZone.InPlay: return player.InPlay;
            case CardZone.Inspected:
                if (player.Inspected == null) player.Inspected = new List<int>();
                return player.Inspected;
            default: return null;
        }
    }

    public static bool MoveCard(List<int> source, List<int> destination, int instanceId)
    {
        if (source == null || destination == null || instanceId <= 0) return false;
        int index = source.IndexOf(instanceId);
        if (index < 0) return false;
        source.RemoveAt(index);
        destination.Add(instanceId);
        return true;
    }

    public static bool MoveCard(PlayerStateSnapshot player, CardZone source, CardZone destination, int instanceId)
    {
        return MoveCard(ResolveZone(player, source), ResolveZone(player, destination), instanceId);
    }

    public static bool MoveAll(List<int> source, List<int> destination, bool reverseOrder = false)
    {
        if (source == null || destination == null || ReferenceEquals(source, destination)) return false;
        if (reverseOrder)
        {
            for (int i = source.Count - 1; i >= 0; i--) destination.Add(source[i]);
        }
        else destination.AddRange(source);
        source.Clear();
        return true;
    }

    public static bool MoveAll(PlayerStateSnapshot player, CardZone source, CardZone destination, bool reverseOrder = false)
    {
        return MoveAll(ResolveZone(player, source), ResolveZone(player, destination), reverseOrder);
    }

    public static bool Shuffle(List<int> cards, System.Random random)
    {
        if (cards == null || random == null) return false;
        for (int i = cards.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            int temp = cards[i]; cards[i] = cards[j]; cards[j] = temp;
        }
        return true;
    }

    /// <summary>
    /// Moves the top card of a player's deck to another zone. If the deck is empty,
    /// the discard pile is reshuffled first, exactly like drawing a card.
    /// Returns instanceId = 0 when no card is available in either deck or discard.
    /// </summary>
    public static bool TryMoveTopCardFromDeck(PlayerStateSnapshot player, CardZone destination, System.Random random,
        out int instanceId, out string error)
    {
        instanceId = 0;
        error = string.Empty;
        if (player == null) { error = "Player is null."; return false; }
        if (destination == CardZone.None || destination == CardZone.Deck)
        { error = "Top-deck movement requires a destination other than deck."; return false; }

        List<int> destinationZone = ResolveZone(player, destination);
        if (destinationZone == null)
        { error = "Top-deck movement destination zone is unavailable."; return false; }

        if (!TryPrepareDeckForTopCard(player, random, "Top-deck movement", out bool hasCard, out error))
            return false;
        if (!hasCard) return true;
        if (!TryTakeTopCard(player.Deck, out instanceId))
        { error = "Top-deck movement could not take the prepared top card."; return false; }

        destinationZone.Add(instanceId);
        return true;
    }

    public static bool DrawCards(PlayerStateSnapshot player, int count, System.Random random, out string error)
    {
        error = string.Empty;
        if (player == null) { error = "Player is null."; return false; }
        if (count < 0) { error = "Draw count cannot be negative."; return false; }
        if (player.Hand == null)
        { error = "Draw requires a hand zone."; return false; }

        for (int i = 0; i < count; i++)
        {
            if (!TryPrepareDeckForTopCard(player, random, "Draw", out bool hasCard, out error))
                return false;
            if (!hasCard) break;
            if (!TryTakeTopCard(player.Deck, out int instanceId))
            { error = "Draw could not take the prepared top card."; return false; }

            player.Hand.Add(instanceId);
        }
        return true;
    }

    /// <summary>
    /// Ensures a top card is available by reshuffling discard into deck only when needed.
    /// This is shared by every rule that consumes the top of the deck so draw/reveal/
    /// inspect effects cannot drift into subtly different reshuffle behaviour.
    /// </summary>
    private static bool TryPrepareDeckForTopCard(PlayerStateSnapshot player, System.Random random, string operationLabel,
        out bool hasCard, out string error)
    {
        hasCard = false;
        error = string.Empty;
        if (player == null)
        { error = operationLabel + " requires a player."; return false; }
        if (player.Deck == null || player.Discard == null)
        { error = operationLabel + " requires deck and discard zones."; return false; }

        if (player.Deck.Count > 0)
        {
            hasCard = true;
            return true;
        }

        if (player.Discard.Count == 0)
            return true;

        if (random == null)
        { error = operationLabel + " requires an injected random source when the discard pile must be shuffled."; return false; }
        if (!MoveAll(player.Discard, player.Deck) || !Shuffle(player.Deck, random))
        { error = "Could not reshuffle the discard pile into the deck."; return false; }

        hasCard = player.Deck.Count > 0;
        return true;
    }

    private static bool TryTakeTopCard(List<int> deck, out int instanceId)
    {
        instanceId = 0;
        if (deck == null || deck.Count == 0) return false;
        int topIndex = deck.Count - 1;
        instanceId = deck[topIndex];
        deck.RemoveAt(topIndex);
        return true;
    }
}
