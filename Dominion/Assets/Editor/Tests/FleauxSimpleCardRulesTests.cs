#if UNITY_INCLUDE_TESTS
using System;
using System.Linq;
using NUnit.Framework;

public sealed class FleauxSimpleCardRulesTests
{
    [SetUp]
    public void Reload() => ExtensionCatalog.Reload();

    [Test]
    public void Herboristerie_DrawsForEachMissingTonique()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);
        AddOwned(state, player, "base:cuivre", CardZone.Deck);
        AddOwned(state, player, "base:argent", CardZone.Deck);
        CardInstance herboristerie = AddOwned(state, player, "fleaux:herboristerie", CardZone.Hand);
        state.SpecialPiles.Add(new SpecialPileSnapshot("fleaux:toniques", "Toniques"));

        GameRuleResult result = GameRules.TryPlayCard(state, player.PlayerId, herboristerie.InstanceId, Resolve, new Random(1));

        Assert.That(result.Status, Is.EqualTo(GameRuleStatus.Applied), result.Error);
        Assert.That(player.Hand.Count, Is.EqualTo(2));
    }

    [Test]
    public void PaladinGain_TakesDivineBanner()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);
        state.SupplyPiles.Add(new SupplyPileSnapshot("fleaux:paladin", 10));
        CardInstance banner = new CardInstance(state.NextCardInstanceId++, "fleaux:etendard_divin", string.Empty);
        state.CardInstances.Add(banner);
        state.UnownedArtifacts.Add(banner.InstanceId);
        Assert.That(ResolutionQueue.TryBegin(state, player.PlayerId, out ResolutionQueue queue, out string beginError), Is.True, beginError);
        Assert.That(GainRules.TryGainFromSupply(state, player, "fleaux:paladin", CardZone.Discard,
            0, queue.Events, out _, out string gainError), Is.True, gainError);

        TriggerResolutionResult resolution = TriggerResolver.ResolvePending(queue, state, Resolve, new Random(1));

        Assert.That(resolution.Status, Is.EqualTo(EffectResolutionStatus.Applied), resolution.Error);
        Assert.That(player.Artifacts, Does.Contain(banner.InstanceId));
        Assert.That(banner.OwnerPlayerId, Is.EqualTo(player.PlayerId));
    }

    [Test]
    public void CabinetOfCuriosities_CountsDistinctTypesInPlay()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);
        state.Phase = GameRules.BuyPhase;
        AddOwned(state, player, "base:village", CardZone.InPlay);
        AddOwned(state, player, "base:domaine", CardZone.InPlay);
        CardInstance cabinet = AddOwned(state, player, "fleaux:cabinet_des_curiosites", CardZone.Hand);

        GameRuleResult result = GameRules.TryPlayCard(state, player.PlayerId, cabinet.InstanceId, Resolve, new Random(1));

        Assert.That(result.Status, Is.EqualTo(GameRuleStatus.Applied), result.Error);
        Assert.That(player.Buys, Is.EqualTo(2));
        Assert.That(player.Coins, Is.EqualTo(3));
    }

    [Test]
    public void Insomnie_AppliesNextCleanupPenalty()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);
        CardInstance insomnia = AddOwned(state, player, "fleaux:insomnie", CardZone.Hand);

        GameRuleResult result = GameRules.TryPlayCard(state, player.PlayerId, insomnia.InstanceId, Resolve, new Random(1));

        Assert.That(result.Status, Is.EqualTo(GameRuleStatus.Applied), result.Error);
        Assert.That(player.NextCleanupDrawModifier, Is.EqualTo(-1));
    }

    [Test]
    public void Fossoyeur_ReactionDecisionResumesExternalHandListenerOnce()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);
        CardInstance fossoyeur = AddOwned(state, player, "fleaux:fossoyeur", CardZone.Hand);
        CardInstance discard = AddOwned(state, player, "base:cuivre", CardZone.Hand);
        AddOwned(state, player, "base:argent", CardZone.Deck);
        CardInstance disease = new CardInstance(state.NextCardInstanceId++, "fleaux:fievre", string.Empty);
        state.CardInstances.Add(disease);
        SpecialPileSnapshot pile = new SpecialPileSnapshot("fleaux:maladies", "Maladies");
        pile.CardInstanceIds.Add(disease.InstanceId);
        state.SpecialPiles.Add(pile);
        Assert.That(ResolutionQueue.TryBegin(state, player.PlayerId, out ResolutionQueue queue, out string beginError), Is.True, beginError);
        Assert.That(SpecialPileRules.TryGainTop(state, player, pile.PileId, CardZone.Discard, 0,
            queue.Events, Resolve, out _, out string gainError), Is.True, gainError);

        TriggerResolutionResult first = TriggerResolver.ResolvePending(queue, state, Resolve, new Random(1));

        Assert.That(first.Status, Is.EqualTo(EffectResolutionStatus.WaitingForChoice), first.Error);
        Assert.That(queue.PendingDecision.ListenerCardInstanceId, Is.EqualTo(fossoyeur.InstanceId));
        Assert.That(queue.PendingDecision.ListenerScope, Is.EqualTo(DeclarativeRuleVocabulary.InHandScope));
        string revealDecision = queue.PendingDecision.DecisionId;
        GameRuleResult reveal = GameRules.TrySubmitOptionDecision(state, player.PlayerId, revealDecision,
            new[] { "reveal" }, Resolve, new Random(1));
        Assert.That(reveal.Status, Is.EqualTo(GameRuleStatus.WaitingForChoice), reveal.Error);
        Assert.That(queue.PendingDecision.Operation, Is.EqualTo("choose_cards"));

        string discardDecision = queue.PendingDecision.DecisionId;
        GameRuleResult finished = GameRules.TrySubmitDecision(state, player.PlayerId, discardDecision,
            new[] { discard.InstanceId }, Resolve, new Random(1));

        Assert.That(finished.Status, Is.EqualTo(GameRuleStatus.Applied), finished.Error);
        Assert.That(player.Hand, Does.Contain(fossoyeur.InstanceId));
        Assert.That(player.Discard, Does.Contain(discard.InstanceId));
        Assert.That(player.Hand.Count(id => id == fossoyeur.InstanceId), Is.EqualTo(1));
        Assert.That(state.Resolution.IsActive, Is.False);
    }

    [Test]
    public void ExternalReactionContinuation_AdvancesToTheNextHandListener()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);
        CardInstance first = AddOwned(state, player, "fleaux:fossoyeur", CardZone.Hand);
        CardInstance second = AddOwned(state, player, "fleaux:fossoyeur", CardZone.Hand);
        CardInstance disease = new CardInstance(state.NextCardInstanceId++, "fleaux:fievre", string.Empty);
        state.CardInstances.Add(disease);
        SpecialPileSnapshot pile = new SpecialPileSnapshot("fleaux:maladies", "Maladies");
        pile.CardInstanceIds.Add(disease.InstanceId);
        state.SpecialPiles.Add(pile);
        Assert.That(ResolutionQueue.TryBegin(state, player.PlayerId, out ResolutionQueue queue, out string beginError), Is.True, beginError);
        Assert.That(SpecialPileRules.TryGainTop(state, player, pile.PileId, CardZone.Discard, 0,
            queue.Events, Resolve, out _, out string gainError), Is.True, gainError);
        Assert.That(TriggerResolver.ResolvePending(queue, state, Resolve, new Random(1)).Status,
            Is.EqualTo(EffectResolutionStatus.WaitingForChoice));
        Assert.That(queue.PendingDecision.ListenerCardInstanceId, Is.EqualTo(first.InstanceId));

        GameRuleResult firstPass = GameRules.TrySubmitOptionDecision(state, player.PlayerId,
            queue.PendingDecision.DecisionId, Array.Empty<string>(), Resolve, new Random(1));

        Assert.That(firstPass.Status, Is.EqualTo(GameRuleStatus.WaitingForChoice), firstPass.Error);
        Assert.That(queue.PendingDecision.ListenerCardInstanceId, Is.EqualTo(second.InstanceId));
        GameRuleResult secondPass = GameRules.TrySubmitOptionDecision(state, player.PlayerId,
            queue.PendingDecision.DecisionId, Array.Empty<string>(), Resolve, new Random(1));
        Assert.That(secondPass.Status, Is.EqualTo(GameRuleStatus.Applied), secondPass.Error);
    }

    [Test]
    public void MarcheClandestin_DiscardsTheReactingCopyAndGainsGold()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);
        CardInstance market = AddOwned(state, player, "fleaux:marche_clandestin", CardZone.Hand);
        CardInstance copper = AddOwned(state, player, "base:cuivre", CardZone.Hand);
        state.SupplyPiles.Add(new SupplyPileSnapshot("base:or", 30));
        Assert.That(ResolutionQueue.TryBegin(state, player.PlayerId, out ResolutionQueue queue, out string beginError), Is.True, beginError);
        Assert.That(TrashRules.TryTrashFromZone(state, player, CardZone.Hand, copper.InstanceId, 0,
            queue.Events, out string trashError), Is.True, trashError);

        TriggerResolutionResult first = TriggerResolver.ResolvePending(queue, state, Resolve, new Random(1));
        Assert.That(first.Status, Is.EqualTo(EffectResolutionStatus.WaitingForChoice), first.Error);
        Assert.That(queue.PendingDecision.ListenerCardInstanceId, Is.EqualTo(market.InstanceId));
        GameRuleResult reacted = GameRules.TrySubmitOptionDecision(state, player.PlayerId,
            queue.PendingDecision.DecisionId, new[] { "react" }, Resolve, new Random(1));

        Assert.That(reacted.Status, Is.EqualTo(GameRuleStatus.Applied), reacted.Error);
        Assert.That(player.Hand, Does.Not.Contain(market.InstanceId));
        Assert.That(player.Discard, Does.Contain(market.InstanceId));
        Assert.That(player.Discard.Select(id => state.CardInstances.Find(card => card.InstanceId == id).DefinitionId),
            Does.Contain("base:or"));
    }

    private static GameStateSnapshot NewState(out PlayerStateSnapshot player)
    {
        GameStateSnapshot state = new GameStateSnapshot
        {
            IsStarted = true,
            ActivePlayerId = "p1",
            Phase = GameRules.ActionPhase,
            TurnNumber = 1
        };
        player = new PlayerStateSnapshot { PlayerId = "p1", NickName = "P1", Actions = 1, Buys = 1 };
        state.Players.Add(player);
        return state;
    }

    private static CardInstance AddOwned(GameStateSnapshot state, PlayerStateSnapshot player, string definitionId, CardZone zone)
    {
        CardInstance instance = new CardInstance(state.NextCardInstanceId++, definitionId, player.PlayerId);
        state.CardInstances.Add(instance);
        CardZoneRules.ResolveZone(player, zone).Add(instance.InstanceId);
        return instance;
    }

    private static ExtensionCardData Resolve(string definitionId)
    {
        return RoomGameSetup.TryResolveCard(definitionId, out _, out ExtensionCardData card) ? card : null;
    }
}
#endif
