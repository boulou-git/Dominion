using System;
using System.Collections.Generic;

/// <summary>
/// Public information log. A reveal is game information visible to every player;
/// private look/inspect effects must never call this helper.
/// </summary>
public static class JournalRules
{
    public const string RevealKind = "reveal";
    private const int MaxEntries = 64;

    public static void RecordReveal(GameStateSnapshot state, PlayerStateSnapshot player, int cardInstanceId)
    {
        if (state == null || cardInstanceId <= 0 || state.CardInstances == null) return;
        CardInstance card = state.CardInstances.Find(x => x != null && x.InstanceId == cardInstanceId);
        if (card != null) RecordReveal(state, player, card.DefinitionId);
    }

    public static void RecordReveal(GameStateSnapshot state, PlayerStateSnapshot player, string definitionId)
    {
        if (state == null || player == null || string.IsNullOrWhiteSpace(definitionId)) return;
        if (state.Journal == null) state.Journal = new List<GameJournalEntrySnapshot>();
        if (state.NextJournalSequence <= 0)
        {
            int max = 0;
            foreach (GameJournalEntrySnapshot entry in state.Journal)
                if (entry != null && entry.Sequence > max) max = entry.Sequence;
            state.NextJournalSequence = max + 1;
        }

        state.Journal.Add(new GameJournalEntrySnapshot
        {
            Sequence = state.NextJournalSequence++,
            TurnNumber = state.TurnNumber,
            Kind = RevealKind,
            PlayerId = player.PlayerId,
            PlayerName = string.IsNullOrWhiteSpace(player.NickName) ? "Joueur" : player.NickName,
            CardDefinitionId = definitionId
        });

        while (state.Journal.Count > MaxEntries)
            state.Journal.RemoveAt(0);
    }

    public static void RecordRevealZone(GameStateSnapshot state, PlayerStateSnapshot player, CardZone zone)
    {
        List<int> cards = CardZoneRules.ResolveZone(player, zone);
        if (cards == null) return;
        foreach (int id in new List<int>(cards)) RecordReveal(state, player, id);
    }
}
