#if UNITY_INCLUDE_TESTS
using System;
using System.Linq;
using NUnit.Framework;

public sealed class MascaradeRulesTests
{
    [SetUp]
    public void ReloadCatalog() => ExtensionCatalog.Reload();

    [Test]
    public void Mascarade_StagesEveryChoiceThenTransfersAllCardsTogether()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot first, out PlayerStateSnapshot second, out PlayerStateSnapshot third);
        int mascarade = AddCard(state, first, "intrigue:mascarade", CardZone.Hand);
        int firstPassed = AddCard(state, first, "base:cuivre", CardZone.Hand);
        AddCard(state, first, "base:argent", CardZone.Deck);
        AddCard(state, first, "base:or", CardZone.Deck);
        int secondPassed = AddCard(state, second, "base:argent", CardZone.Hand);
        int thirdPassed = AddCard(state, third, "base:domaine", CardZone.Hand);

        GameRuleResult played = Play(state, first, mascarade);
        Assert.That(played.Status, Is.EqualTo(GameRuleStatus.WaitingForChoice), played.Error);
        Assert.That(state.Resolution.PendingDecision.PlayerId, Is.EqualTo(first.PlayerId));

        GameRuleResult firstChoice = Submit(state, first, firstPassed);
        Assert.That(firstChoice.Status, Is.EqualTo(GameRuleStatus.WaitingForChoice), firstChoice.Error);
        Assert.That(state.Resolution.PendingDecision.PlayerId, Is.EqualTo(second.PlayerId));
        Assert.That(first.Hand, Does.Contain(firstPassed));
        Assert.That(second.Hand, Does.Contain(secondPassed));
        Assert.That(third.Hand, Does.Contain(thirdPassed));
        CollectionAssert.AreEqual(new[] { first.PlayerId }, state.Resolution.StagedSelectionPlayerIds);

        GameRuleResult secondChoice = Submit(state, second, secondPassed);
        Assert.That(secondChoice.Status, Is.EqualTo(GameRuleStatus.WaitingForChoice), secondChoice.Error);
        Assert.That(state.Resolution.PendingDecision.PlayerId, Is.EqualTo(third.PlayerId));
        Assert.That(first.Hand, Does.Contain(firstPassed));
        Assert.That(second.Hand, Does.Contain(secondPassed));
        CollectionAssert.AreEqual(new[] { first.PlayerId, second.PlayerId }, state.Resolution.StagedSelectionPlayerIds);
        Assert.That(GameStateValidator.TryValidate(state, out string pendingError), Is.True, pendingError);

        GameRuleResult thirdChoice = Submit(state, third, thirdPassed);
        Assert.That(thirdChoice.Status, Is.EqualTo(GameRuleStatus.WaitingForChoice), thirdChoice.Error);
        Assert.That(state.Resolution.PendingDecision.PlayerId, Is.EqualTo(first.PlayerId));
        Assert.That(state.Resolution.StagedSelectionPlayerIds, Is.Empty);
        Assert.That(first.Hand, Does.Contain(thirdPassed));
        Assert.That(second.Hand, Does.Contain(firstPassed));
        Assert.That(third.Hand, Does.Contain(secondPassed));
        Assert.That(Owner(state, thirdPassed), Is.EqualTo(first.PlayerId));
        Assert.That(Owner(state, firstPassed), Is.EqualTo(second.PlayerId));
        Assert.That(Owner(state, secondPassed), Is.EqualTo(third.PlayerId));

        GameRuleResult trashed = Submit(state, first, thirdPassed);
        Assert.That(trashed.Status, Is.EqualTo(GameRuleStatus.Applied), trashed.Error);
        CollectionAssert.AreEqual(new[] { thirdPassed }, state.TrashedCards);
        Assert.That(Owner(state, thirdPassed), Is.EqualTo(first.PlayerId));
        Assert.That(state.Resolution.IsActive, Is.False);
        Assert.That(GameStateValidator.TryValidate(state, out string validationError), Is.True, validationError);
    }

    [Test]
    public void Mascarade_SkipsPlayersWhoseHandsAreEmpty()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot first, out PlayerStateSnapshot empty, out PlayerStateSnapshot third);
        int mascarade = AddCard(state, first, "intrigue:mascarade", CardZone.Hand);
        int firstPassed = AddCard(state, first, "base:cuivre", CardZone.Hand);
        int thirdPassed = AddCard(state, third, "base:domaine", CardZone.Hand);

        GameRuleResult played = Play(state, first, mascarade);
        GameRuleResult firstChoice = Submit(state, first, firstPassed);

        Assert.That(played.Status, Is.EqualTo(GameRuleStatus.WaitingForChoice), played.Error);
        Assert.That(firstChoice.Status, Is.EqualTo(GameRuleStatus.WaitingForChoice), firstChoice.Error);
        Assert.That(state.Resolution.PendingDecision.PlayerId, Is.EqualTo(third.PlayerId));
        Assert.That(empty.Hand, Is.Empty);

        GameRuleResult thirdChoice = Submit(state, third, thirdPassed);
        Assert.That(thirdChoice.Status, Is.EqualTo(GameRuleStatus.WaitingForChoice), thirdChoice.Error);
        Assert.That(first.Hand, Does.Contain(thirdPassed));
        Assert.That(third.Hand, Does.Contain(firstPassed));
        Assert.That(empty.Hand, Is.Empty);

        GameRuleResult noTrash = GameRules.TrySubmitDecision(state, first.PlayerId,
            state.Resolution.PendingDecision.DecisionId, Array.Empty<int>(), ResolveDefinition, new Random(1));
        Assert.That(noTrash.Status, Is.EqualTo(GameRuleStatus.Applied), noTrash.Error);
        Assert.That(state.TrashedCards, Is.Empty);
    }

    private static GameRuleResult Submit(GameStateSnapshot state, PlayerStateSnapshot player, int selectedId) =>
        GameRules.TrySubmitDecision(state, player.PlayerId, state.Resolution.PendingDecision.DecisionId,
            new[] { selectedId }, ResolveDefinition, new Random(1));

    private static GameRuleResult Play(GameStateSnapshot state, PlayerStateSnapshot player, int instanceId) =>
        GameRules.TryPlayCard(state, player.PlayerId, instanceId, ResolveDefinition, new Random(1));

    private static int AddCard(GameStateSnapshot state, PlayerStateSnapshot player, string definitionId, CardZone zone)
    {
        Assert.That(CardInstanceRules.TryCreateOwnedCard(state, player, definitionId, zone,
            out int instanceId, out string error), Is.True, error);
        return instanceId;
    }

    private static string Owner(GameStateSnapshot state, int instanceId) =>
        state.CardInstances.Single(card => card.InstanceId == instanceId).OwnerPlayerId;

    private static ExtensionCardData ResolveDefinition(string definitionId)
    {
        if (string.IsNullOrWhiteSpace(definitionId)) return null;
        int separator = definitionId.IndexOf(':');
        if (separator <= 0 || separator >= definitionId.Length - 1) return null;
        return ExtensionCatalog.FindCard(definitionId.Substring(0, separator), definitionId.Substring(separator + 1));
    }

    private static GameStateSnapshot NewState(out PlayerStateSnapshot first, out PlayerStateSnapshot second, out PlayerStateSnapshot third)
    {
        GameStateSnapshot state = new GameStateSnapshot
        {
            MatchId = "mascarade-tests",
            Version = 1,
            AuthorityEpoch = 1,
            IsStarted = true,
            IsInitialised = true,
            ActivePlayerId = "player-1",
            TurnNumber = 1,
            Phase = GameRules.ActionPhase,
            NextCardInstanceId = 1
        };
        first = NewPlayer("player-1", 1, 1);
        second = NewPlayer("player-2", 2, 0);
        third = NewPlayer("player-3", 3, 0);
        state.Players.Add(first); state.Players.Add(second); state.Players.Add(third);
        return state;
    }

    private static PlayerStateSnapshot NewPlayer(string id, int actorNumber, int actions) => new PlayerStateSnapshot
    {
        PlayerId = id,
        ActorNumber = actorNumber,
        NickName = id,
        IsConnected = true,
        Actions = actions,
        Buys = 1
    };
}
#endif
