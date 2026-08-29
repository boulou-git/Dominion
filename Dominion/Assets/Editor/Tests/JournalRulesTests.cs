#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;

public sealed class JournalRulesTests
{
    [Test]
    public void Chat_IsSanitisedAndLimitedToOneMessagePerSecondPerPlayer()
    {
        GameStateSnapshot state = State();
        Assert.IsTrue(JournalRules.TryRecordChat(state, "p1", "  Bonjour\n tout le monde  ", 10000, out string firstError), firstError);
        Assert.AreEqual("Bonjour tout le monde", state.Journal[0].Message);
        Assert.IsFalse(JournalRules.TryRecordChat(state, "p1", "Trop vite", 10999, out _));
        Assert.IsTrue(JournalRules.TryRecordChat(state, "p2", "Autre joueur", 10999, out string otherError), otherError);
        Assert.IsTrue(JournalRules.TryRecordChat(state, "p1", "Après une seconde", 11000, out string nextError), nextError);
        Assert.AreEqual(3, state.Journal.Count);
    }

    [Test]
    public void PlayedAndGainedEvents_BecomeSemanticJournalEntries()
    {
        GameStateSnapshot state = State();
        JournalRules.RecordEvents(state, new[]
        {
            GameEvent.CardPlayed("p1", 1, "base:village"),
            GameEvent.CardGained("p2", 2, "base:or", CardZone.Discard),
            GameEvent.CardDiscarded("p1", 3, "base:cuivre")
        });
        Assert.AreEqual(2, state.Journal.Count);
        Assert.AreEqual(JournalRules.PlayedKind, state.Journal[0].Kind);
        Assert.AreEqual(JournalRules.GainedKind, state.Journal[1].Kind);
        Assert.AreEqual("base:or", state.Journal[1].CardDefinitionId);
    }

    [Test]
    public void OptionChoice_UsesTheVisibleOptionLabel()
    {
        GameStateSnapshot state = State();
        PendingDecisionSnapshot decision = new PendingDecisionSnapshot
        {
            PlayerId = "p1",
            CandidateDefinitionIds = new List<string> { "cards", "coins" },
            CandidateOptionLabels = new List<string> { "+1 Carte", "+2 Pièces" }
        };
        JournalRules.RecordOptionChoice(state, decision, new[] { "coins" });
        Assert.AreEqual(JournalRules.ChoiceKind, state.Journal[0].Kind);
        Assert.AreEqual("+2 Pièces", state.Journal[0].Message);
    }

    [Test]
    public void Journal_DropsOldestEntriesAfterReplicatedHistoryLimit()
    {
        GameStateSnapshot state = State();
        for (int i = 0; i < 80; i++)
            JournalRules.RecordReveal(state, state.Players[0], "base:cuivre");

        Assert.AreEqual(64, state.Journal.Count);
        Assert.AreEqual(17, state.Journal[0].Sequence);
        Assert.AreEqual(80, state.Journal[state.Journal.Count - 1].Sequence);
        Assert.AreEqual(81, state.NextJournalSequence);
    }

    private static GameStateSnapshot State()
    {
        return new GameStateSnapshot
        {
            TurnNumber = 3,
            Players = new List<PlayerStateSnapshot>
            {
                new PlayerStateSnapshot { PlayerId = "p1", NickName = "Alice", IsConnected = true },
                new PlayerStateSnapshot { PlayerId = "p2", NickName = "Bob", IsConnected = true }
            }
        };
    }
}
#endif
