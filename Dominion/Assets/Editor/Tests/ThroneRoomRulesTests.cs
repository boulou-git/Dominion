#if UNITY_INCLUDE_TESTS
using System;
using NUnit.Framework;

public sealed class ThroneRoomRulesTests
{
    [SetUp]
    public void Reload() => ExtensionCatalog.Reload();

    [Test]
    public void ThroneRoomOnThroneRoom_WithOnlyOneRemainingAction_DoesNotBlock()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);
        CardInstance outer = AddOwned(state, player, "base:salle_du_trone", CardZone.Hand);
        CardInstance inner = AddOwned(state, player, "base:salle_du_trone", CardZone.Hand);
        CardInstance village = AddOwned(state, player, "base:village", CardZone.Hand);
        for (int index = 0; index < 3; index++)
            AddOwned(state, player, "base:cuivre", CardZone.Deck);

        GameRuleResult chooseInner = GameRules.TryPlayCard(
            state, player.PlayerId, outer.InstanceId, Resolve, new Random(1));
        Assert.That(chooseInner.Status, Is.EqualTo(GameRuleStatus.WaitingForChoice), chooseInner.Error);

        GameRuleResult chooseOnlyAction = GameRules.TrySubmitDecision(
            state, player.PlayerId, state.Resolution.PendingDecision.DecisionId,
            new[] { inner.InstanceId }, Resolve, new Random(1));
        Assert.That(chooseOnlyAction.Status, Is.EqualTo(GameRuleStatus.WaitingForChoice), chooseOnlyAction.Error);
        CollectionAssert.AreEqual(new[] { village.InstanceId }, state.Resolution.PendingDecision.CandidateInstanceIds);

        GameRuleResult finished = GameRules.TrySubmitDecision(
            state, player.PlayerId, state.Resolution.PendingDecision.DecisionId,
            new[] { village.InstanceId }, Resolve, new Random(1));

        Assert.That(finished.Status, Is.EqualTo(GameRuleStatus.Applied), finished.Error);
        Assert.That(state.Resolution.PendingDecision.IsPending, Is.False);
        Assert.That(player.InPlay, Does.Contain(inner.InstanceId));
        Assert.That(player.InPlay, Does.Contain(village.InstanceId));
    }

    private static GameStateSnapshot NewState(out PlayerStateSnapshot player)
    {
        GameStateSnapshot state = new GameStateSnapshot
        {
            IsStarted = true,
            ActivePlayerId = "p1",
            Phase = GameRules.ActionPhase,
            TurnNumber = 1,
            NextCardInstanceId = 1
        };
        player = new PlayerStateSnapshot { PlayerId = "p1", NickName = "P1", Actions = 1, Buys = 1 };
        state.Players.Add(player);
        return state;
    }

    private static CardInstance AddOwned(GameStateSnapshot state, PlayerStateSnapshot player,
        string definitionId, CardZone zone)
    {
        CardInstance instance = new CardInstance(state.NextCardInstanceId++, definitionId, player.PlayerId);
        state.CardInstances.Add(instance);
        CardZoneRules.ResolveZone(player, zone).Add(instance.InstanceId);
        return instance;
    }

    private static ExtensionCardData Resolve(string definitionId) =>
        RoomGameSetup.TryResolveCard(definitionId, out _, out ExtensionCardData card) ? card : null;
}
#endif
