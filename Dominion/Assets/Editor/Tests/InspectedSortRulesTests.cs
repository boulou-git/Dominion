#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public sealed class InspectedSortRulesTests
{
    [SetUp] public void Reload() { ExtensionCatalog.Reload(); ScoringRules.Reload(); }

    [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)]
    public void Soldier_OneDraftResolvesEveryDestination(int mode)
    {
        var state = Start(out PlayerStateSnapshot player, out int first, out int second);
        int[] trash = mode == 1 ? new[] { first } : mode == 2 ? new[] { first, second } : new int[0];
        int[] discard = mode == 1 ? new[] { second } : mode == 3 ? new[] { first, second } : mode == 4 ? new[] { first } : new int[0];
        int[] deck = mode == 0 ? new[] { second, first } : mode == 4 ? new[] { second } : new int[0];
        var d = state.Resolution.PendingDecision;
        Assert.IsTrue(InspectedSortRules.CanSort(d, Resolve("base:soldat")));
        Assert.IsFalse(DecisionPresentation.IsQuickChoice(d, Resolve("base:soldat")));
        var result = InspectedSortRules.Submit(state, player.PlayerId, d.DecisionId, trash, discard, deck, Resolve, new System.Random(1));
        Assert.AreNotEqual(GameRuleStatus.Rejected, result.Status, result.Error);
        result = InspectedSortRules.Continue(state, result, Resolve, new System.Random(1));
        Assert.AreEqual(GameRuleStatus.Applied, result.Status, result.Error);
        CollectionAssert.AreEqual(deck, player.Deck);
        CollectionAssert.AreEquivalent(discard, player.Discard);
        CollectionAssert.AreEquivalent(trash, state.TrashedCards);
        Assert.IsEmpty(player.Inspected);
        Assert.IsFalse(state.Resolution.PendingDecision.IsPending);
        Assert.IsEmpty(state.Resolution.SortPlans);
    }

    [Test]
    public void Sorting_RejectsDuplicateAssignmentsBeforeChangingCards()
    {
        var state = Start(out PlayerStateSnapshot player, out int first, out int second);
        string id = state.Resolution.PendingDecision.DecisionId;
        var result = InspectedSortRules.Submit(state, player.PlayerId, id,
            new[] { first }, new[] { first }, new[] { second }, Resolve, new System.Random(1));
        Assert.AreEqual(GameRuleStatus.Rejected, result.Status);
        Assert.AreEqual(id, state.Resolution.PendingDecision.DecisionId);
        Assert.IsEmpty(state.Resolution.SortPlans);
        CollectionAssert.AreEquivalent(new[] { first, second }, player.Inspected);
    }

    [TestCase(false)] [TestCase(true)]
    public void Sorting_WaitsForReactionAndRetainsTheDraftAcrossSerialization(bool react)
    {
        var state = Start(out PlayerStateSnapshot player, out int first, out int second);
        var gravedigger = Add(state, player, "fleaux:fossoyeur", CardZone.Hand);
        var result = InspectedSortRules.Submit(state, player.PlayerId, state.Resolution.PendingDecision.DecisionId,
            new[] { first }, new int[0], new[] { second }, Resolve, new System.Random(1));
        Assert.AreEqual(GameRuleStatus.WaitingForChoice, result.Status, result.Error);
        result = InspectedSortRules.Continue(state, result, Resolve, new System.Random(1));
        Assert.AreEqual(gravedigger.InstanceId, state.Resolution.PendingDecision.ListenerCardInstanceId);
        Assert.IsNotEmpty(state.Resolution.SortPlans);
        Assert.IsTrue(player.Inspected.Contains(second));
        state = JsonUtility.FromJson<GameStateSnapshot>(JsonUtility.ToJson(state));
        player = state.Players[0];
        result = GameRules.TrySubmitOptionDecision(state, player.PlayerId, state.Resolution.PendingDecision.DecisionId,
            react ? new[] { "react" } : new string[0], Resolve, new System.Random(1));
        Assert.AreNotEqual(GameRuleStatus.Rejected, result.Status, result.Error);
        result = InspectedSortRules.Continue(state, result, Resolve, new System.Random(1));
        Assert.AreEqual(GameRuleStatus.Applied, result.Status, result.Error);
        CollectionAssert.AreEqual(new[] { second }, player.Deck);
        Assert.IsTrue(state.TrashedCards.Contains(first));
        Assert.IsEmpty(state.Resolution.SortPlans);
    }

    private static GameStateSnapshot Start(out PlayerStateSnapshot player, out int first, out int second)
    {
        var state = new GameStateSnapshot { ActivePlayerId = "p1", IsStarted = true };
        player = new PlayerStateSnapshot { PlayerId = "p1", NickName = "P1", Actions = 1, Buys = 1 };
        state.Players.Add(player);
        first = Add(state, player, "base:cuivre", CardZone.Deck).InstanceId;
        second = Add(state, player, "base:argent", CardZone.Deck).InstanceId;
        Add(state, player, "base:domaine", CardZone.Deck);
        var soldier = Add(state, player, "base:soldat", CardZone.Hand);
        var result = GameRules.TryPlayCard(state, player.PlayerId, soldier.InstanceId, Resolve, new System.Random(1));
        Assert.AreEqual(GameRuleStatus.WaitingForChoice, result.Status, result.Error);
        return state;
    }
    private static CardInstance Add(GameStateSnapshot state, PlayerStateSnapshot player, string definition, CardZone zone)
    {
        var card = new CardInstance(state.NextCardInstanceId++, definition, player.PlayerId);
        state.CardInstances.Add(card); CardZoneRules.ResolveZone(player, zone).Add(card.InstanceId); return card;
    }
    private static ExtensionCardData Resolve(string id) => RoomGameSetup.TryResolveCard(id, out _, out ExtensionCardData card) ? card : null;
}
#endif
