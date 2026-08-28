#if UNITY_INCLUDE_TESTS
using System;
using System.Linq;
using NUnit.Framework;

public sealed class FleauxArtifactRulesTests
{
    [SetUp]
    public void ReloadCatalog() => ExtensionCatalog.Reload();

    [Test]
    public void DivineBanner_AddsOneCleanupCardOnlyOncePerTurn()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);
        AddArtifact(state, player, "fleaux:etendard_divin");

        Assert.That(TurnLifecycleRules.TryResolveTurnEnded(state, player, Resolve, new Random(1)).Status,
            Is.EqualTo(GameRuleStatus.Applied));
        Assert.That(TurnLifecycleRules.TryResolveTurnEnded(state, player, Resolve, new Random(1)).Status,
            Is.EqualTo(GameRuleStatus.Applied));

        Assert.That(player.NextCleanupDrawModifier, Is.EqualTo(1));
    }

    [Test]
    public void BoneBag_CanPutFirstGainedCardOnDeck()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);
        AddArtifact(state, player, "fleaux:sac_d_ossements");
        state.SupplyPiles.Add(new SupplyPileSnapshot("base:cuivre", 46));
        Assert.That(ResolutionQueue.TryBegin(state, player.PlayerId, out ResolutionQueue queue, out string beginError),
            Is.True, beginError);
        Assert.That(GainRules.TryGainFromSupply(state, player, "base:cuivre", CardZone.Discard, 0,
            queue.Events, out int gainedId, out string gainError), Is.True, gainError);

        TriggerResolutionResult waiting = TriggerResolver.ResolvePending(queue, state, Resolve, new Random(1));
        Assert.That(waiting.Status, Is.EqualTo(EffectResolutionStatus.WaitingForChoice), waiting.Error);
        GameRuleResult result = GameRules.TrySubmitOptionDecision(state, player.PlayerId,
            queue.PendingDecision.DecisionId, new[] { "top" }, Resolve, new Random(1));

        Assert.That(result.Status, Is.EqualTo(GameRuleStatus.Applied), result.Error);
        Assert.That(player.Deck.Last(), Is.EqualTo(gainedId));
        Assert.That(player.Discard.Contains(gainedId), Is.False);
    }

    [Test]
    public void Necronomicon_CanTopdeckDiscardCostingAtMostThree()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);
        AddArtifact(state, player, "fleaux:necronomicon");
        CardInstance copper = AddOwned(state, player, "base:cuivre", CardZone.Discard);

        GameRuleResult waiting = TurnLifecycleRules.TryResolveTurnEnded(state, player, Resolve, new Random(1));
        Assert.That(waiting.Status, Is.EqualTo(GameRuleStatus.WaitingForChoice), waiting.Error);
        GameRuleResult result = GameRules.TrySubmitDecision(state, player.PlayerId,
            state.Resolution.PendingDecision.DecisionId, new[] { copper.InstanceId }, Resolve, new Random(1));

        Assert.That(result.Status, Is.EqualTo(GameRuleStatus.Applied), result.Error);
        Assert.That(player.Deck.Last(), Is.EqualTo(copper.InstanceId));
        Assert.That(player.Discard.Contains(copper.InstanceId), Is.False);
    }

    [Test]
    public void Phylactery_ReplacesDiscardBeforeSubjectTriggerAndReturnsCardNextTurn()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);
        AddArtifact(state, player, "fleaux:phylactere");
        CardInstance beggar = AddOwned(state, player, "fleaux:mendiant", CardZone.Hand);
        Assert.That(ResolutionQueue.TryBegin(state, player.PlayerId, out ResolutionQueue queue, out string beginError),
            Is.True, beginError);
        Assert.That(DiscardRules.TryDiscardSelectedFromHand(state, player, new[] { beggar.InstanceId }, 0,
            queue.Events, out string discardError), Is.True, discardError);

        TriggerResolutionResult waiting = TriggerResolver.ResolvePending(queue, state, Resolve, new Random(1));
        Assert.That(waiting.Status, Is.EqualTo(EffectResolutionStatus.WaitingForChoice), waiting.Error);
        GameRuleResult result = GameRules.TrySubmitOptionDecision(state, player.PlayerId,
            queue.PendingDecision.DecisionId, new[] { "save" }, Resolve, new Random(1));

        Assert.That(result.Status, Is.EqualTo(GameRuleStatus.Applied), result.Error);
        Assert.That(player.Discard.Contains(beggar.InstanceId), Is.False);
        Assert.That(state.SetAsideCards.Exists(entry => entry.CardInstanceId == beggar.InstanceId), Is.True);
        Assert.That(player.Actions, Is.EqualTo(1), "Mendiant's discard trigger must not fire when the discard is replaced.");
        state.TurnNumber++;
        Assert.That(TurnLifecycleRules.TryResolveTurnStarted(state, player, Resolve, new Random(1)).Status,
            Is.EqualTo(GameRuleStatus.Applied));
        Assert.That(player.Hand.Contains(beggar.InstanceId), Is.True);
    }

    [Test]
    public void Phylactery_DiscardAndTrashShareOneUsePerTurn()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);
        AddArtifact(state, player, "fleaux:phylactere");
        CardInstance copper = AddOwned(state, player, "base:cuivre", CardZone.Hand);
        CardInstance estate = AddOwned(state, player, "base:domaine", CardZone.Hand);
        Assert.That(ResolutionQueue.TryBegin(state, player.PlayerId, out ResolutionQueue queue, out string beginError),
            Is.True, beginError);
        Assert.That(DiscardRules.TryDiscardSelectedFromHand(state, player, new[] { copper.InstanceId }, 0,
            queue.Events, out string discardError), Is.True, discardError);
        Assert.That(TriggerResolver.ResolvePending(queue, state, Resolve, new Random(1)).Status,
            Is.EqualTo(EffectResolutionStatus.WaitingForChoice));
        Assert.That(GameRules.TrySubmitOptionDecision(state, player.PlayerId, queue.PendingDecision.DecisionId,
            Array.Empty<string>(), Resolve, new Random(1)).Status, Is.EqualTo(GameRuleStatus.Applied));

        Assert.That(ResolutionQueue.TryBegin(state, player.PlayerId, out queue, out beginError), Is.True, beginError);
        Assert.That(TrashRules.TryTrashFromHand(state, player, estate.InstanceId, 0, queue.Events, out string trashError),
            Is.True, trashError);
        TriggerResolutionResult second = TriggerResolver.ResolvePending(queue, state, Resolve, new Random(1));

        Assert.That(second.Status, Is.EqualTo(EffectResolutionStatus.Applied), second.Error);
        Assert.That(state.TrashedCards.Contains(estate.InstanceId), Is.True);
        Assert.That(queue.IsWaitingForDecision, Is.False);
    }

    private static GameStateSnapshot NewState(out PlayerStateSnapshot player)
    {
        GameStateSnapshot state = new GameStateSnapshot
        {
            ActivePlayerId = "p1",
            IsStarted = true,
            Phase = GameRules.ActionPhase,
            TurnNumber = 1
        };
        player = new PlayerStateSnapshot { PlayerId = "p1", NickName = "P1", Actions = 1, Buys = 1 };
        state.Players.Add(player);
        return state;
    }

    private static CardInstance AddArtifact(GameStateSnapshot state, PlayerStateSnapshot player, string definitionId)
    {
        CardInstance artifact = new CardInstance(state.NextCardInstanceId++, definitionId, player.PlayerId);
        state.CardInstances.Add(artifact);
        player.Artifacts.Add(artifact.InstanceId);
        return artifact;
    }

    private static CardInstance AddOwned(GameStateSnapshot state, PlayerStateSnapshot player, string definitionId, CardZone zone)
    {
        CardInstance card = new CardInstance(state.NextCardInstanceId++, definitionId, player.PlayerId);
        state.CardInstances.Add(card);
        CardZoneRules.ResolveZone(player, zone).Add(card.InstanceId);
        return card;
    }

    private static ExtensionCardData Resolve(string definitionId) =>
        RoomGameSetup.TryResolveCard(definitionId, out _, out ExtensionCardData card) ? card : null;
}
#endif
