#if UNITY_INCLUDE_TESTS
using System;
using System.Linq;
using NUnit.Framework;

public sealed class FleauxRemainingCardsRulesTests
{
    [SetUp]
    public void Reload() => ExtensionCatalog.Reload();

    [Test]
    public void Fleaux_AllKingdomCardsHaveValidatedAbilities()
    {
        ExtensionPackageData extension = ExtensionCatalog.Find("fleaux");
        Assert.That(extension, Is.Not.Null);
        Assert.That(ExtensionCatalog.TryValidatePackage(extension, out string error), Is.True, error);
        Assert.That(extension.cards.Where(card => card == null || card.abilities == null || card.abilities.Count == 0), Is.Empty);
    }

    [Test]
    public void PilleurDeTombes_RepeatsOneChoicePerEmptyKingdomPile()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);
        state.SupplyPiles.Add(new SupplyPileSnapshot("fleaux:rats", 0, true));
        state.SupplyPiles.Add(new SupplyPileSnapshot("fleaux:paladin", 10, true));
        CardInstance robber = AddOwned(state, player, "fleaux:pilleur_de_tombes", CardZone.Hand);

        GameRuleResult first = GameRules.TryPlayCard(state, player.PlayerId, robber.InstanceId, Resolve, new Random(1));
        Assert.That(first.Status, Is.EqualTo(GameRuleStatus.WaitingForChoice), first.Error);
        GameRuleResult second = GameRules.TrySubmitOptionDecision(state, player.PlayerId,
            state.Resolution.PendingDecision.DecisionId, new[] { "coin" }, Resolve, new Random(1));
        Assert.That(second.Status, Is.EqualTo(GameRuleStatus.WaitingForChoice), second.Error);
        GameRuleResult finished = GameRules.TrySubmitOptionDecision(state, player.PlayerId,
            state.Resolution.PendingDecision.DecisionId, new[] { "coin" }, Resolve, new Random(1));

        Assert.That(finished.Status, Is.EqualTo(GameRuleStatus.Applied), finished.Error);
        Assert.That(player.Coins, Is.EqualTo(2));
    }

    [Test]
    public void PilleurDeTombes_TrashChoiceResumesRemainingChoices()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);
        state.SupplyPiles.Add(new SupplyPileSnapshot("fleaux:rats", 0, true));
        CardInstance copper = AddOwned(state, player, "base:cuivre", CardZone.Hand);
        CardInstance robber = AddOwned(state, player, "fleaux:pilleur_de_tombes", CardZone.Hand);

        GameRuleResult option = GameRules.TryPlayCard(state, player.PlayerId, robber.InstanceId, Resolve, new Random(1));
        Assert.That(option.Status, Is.EqualTo(GameRuleStatus.WaitingForChoice), option.Error);
        GameRuleResult cardChoice = GameRules.TrySubmitOptionDecision(state, player.PlayerId,
            state.Resolution.PendingDecision.DecisionId, new[] { "trash" }, Resolve, new Random(1));
        Assert.That(cardChoice.Status, Is.EqualTo(GameRuleStatus.WaitingForChoice), cardChoice.Error);
        GameRuleResult remainingChoice = GameRules.TrySubmitDecision(state, player.PlayerId,
            state.Resolution.PendingDecision.DecisionId, new[] { copper.InstanceId }, Resolve, new Random(1));
        Assert.That(remainingChoice.Status, Is.EqualTo(GameRuleStatus.WaitingForChoice), remainingChoice.Error);
        GameRuleResult finished = GameRules.TrySubmitOptionDecision(state, player.PlayerId,
            state.Resolution.PendingDecision.DecisionId, new[] { "coin" }, Resolve, new Random(1));

        Assert.That(finished.Status, Is.EqualTo(GameRuleStatus.Applied), finished.Error);
        Assert.That(state.TrashedCards, Does.Contain(copper.InstanceId));
        Assert.That(player.Coins, Is.EqualTo(1));
    }

    [Test]
    public void Ossuaire_GainsUpToCostFourFromTrashIntoDiscard()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);
        CardInstance silver = new CardInstance(state.NextCardInstanceId++, "base:argent", string.Empty);
        state.CardInstances.Add(silver);
        state.TrashedCards.Add(silver.InstanceId);
        CardInstance ossuary = AddOwned(state, player, "fleaux:ossuaire", CardZone.Hand);

        GameRuleResult options = GameRules.TryPlayCard(state, player.PlayerId, ossuary.InstanceId, Resolve, new Random(1));
        Assert.That(options.Status, Is.EqualTo(GameRuleStatus.WaitingForChoice), options.Error);
        GameRuleResult cardChoice = GameRules.TrySubmitOptionDecision(state, player.PlayerId,
            state.Resolution.PendingDecision.DecisionId, new[] { "gain_trash" }, Resolve, new Random(1));

        Assert.That(cardChoice.Status, Is.EqualTo(GameRuleStatus.WaitingForChoice), cardChoice.Error);
        Assert.That(state.Resolution.PendingDecision.CandidateInstanceIds, Does.Contain(silver.InstanceId));
        GameRuleResult finished = GameRules.TrySubmitDecision(state, player.PlayerId,
            state.Resolution.PendingDecision.DecisionId, new[] { silver.InstanceId }, Resolve, new Random(1));

        Assert.That(finished.Status, Is.EqualTo(GameRuleStatus.Applied), finished.Error);
        Assert.That(player.Discard, Does.Contain(silver.InstanceId));
        Assert.That(state.TrashedCards.Contains(silver.InstanceId), Is.False);
    }

    [Test]
    public void Ossuaire_TrashesOnlyNonTreasureFromDiscard()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);
        CardInstance copper = AddOwned(state, player, "base:cuivre", CardZone.Discard);
        CardInstance estate = AddOwned(state, player, "base:domaine", CardZone.Discard);
        CardInstance ossuary = AddOwned(state, player, "fleaux:ossuaire", CardZone.Hand);

        GameRuleResult options = GameRules.TryPlayCard(state, player.PlayerId, ossuary.InstanceId, Resolve, new Random(1));
        Assert.That(options.Status, Is.EqualTo(GameRuleStatus.WaitingForChoice), options.Error);
        GameRuleResult cardChoice = GameRules.TrySubmitOptionDecision(state, player.PlayerId,
            state.Resolution.PendingDecision.DecisionId, new[] { "trash_discard" }, Resolve, new Random(1));

        Assert.That(cardChoice.Status, Is.EqualTo(GameRuleStatus.WaitingForChoice), cardChoice.Error);
        Assert.That(state.Resolution.PendingDecision.CandidateInstanceIds, Does.Contain(estate.InstanceId));
        Assert.That(state.Resolution.PendingDecision.CandidateInstanceIds.Contains(copper.InstanceId), Is.False);
        GameRuleResult finished = GameRules.TrySubmitDecision(state, player.PlayerId,
            state.Resolution.PendingDecision.DecisionId, new[] { estate.InstanceId }, Resolve, new Random(1));

        Assert.That(finished.Status, Is.EqualTo(GameRuleStatus.Applied), finished.Error);
        Assert.That(state.TrashedCards, Does.Contain(estate.InstanceId));
        Assert.That(player.Discard, Does.Contain(copper.InstanceId));
    }

    [Test]
    public void Inquisiteur_DiscardsNamedTreasureAndRewardsAttacker()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot attacker);
        PlayerStateSnapshot defender = new PlayerStateSnapshot { PlayerId = "p2", NickName = "P2" };
        state.Players.Add(defender);
        CardInstance copper = AddOwned(state, defender, "base:cuivre", CardZone.Hand);
        CardInstance inquisitor = AddOwned(state, attacker, "fleaux:inquisiteur", CardZone.Hand);

        GameRuleResult waiting = GameRules.TryPlayCard(state, attacker.PlayerId, inquisitor.InstanceId, Resolve, new Random(1));
        Assert.That(waiting.Status, Is.EqualTo(GameRuleStatus.WaitingForChoice), waiting.Error);
        Assert.That(state.Resolution.PendingDecision.CandidateDefinitionIds, Does.Contain("base:cuivre"));
        GameRuleResult finished = GameRules.TrySubmitOptionDecision(state, attacker.PlayerId,
            state.Resolution.PendingDecision.DecisionId, new[] { "base:cuivre" }, Resolve, new Random(1));

        Assert.That(finished.Status, Is.EqualTo(GameRuleStatus.Applied), finished.Error);
        Assert.That(defender.Hand.Contains(copper.InstanceId), Is.False);
        Assert.That(defender.Discard.Contains(copper.InstanceId), Is.True);
        Assert.That(attacker.Coins, Is.EqualTo(3));
    }

    [Test]
    public void Necromancien_TakesArtifactOnlyForAnEarlierTrash()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);
        player.CardsTrashedThisTurn = 1;
        CardInstance artifact = new CardInstance(state.NextCardInstanceId++, "fleaux:necronomicon", string.Empty);
        state.CardInstances.Add(artifact); state.UnownedArtifacts.Add(artifact.InstanceId);
        AddOwned(state, player, "base:cuivre", CardZone.Deck);
        CardInstance necromancer = AddOwned(state, player, "fleaux:necromancien", CardZone.Hand);

        GameRuleResult waiting = GameRules.TryPlayCard(state, player.PlayerId, necromancer.InstanceId, Resolve, new Random(1));

        Assert.That(waiting.Status, Is.EqualTo(GameRuleStatus.WaitingForChoice), waiting.Error);
        Assert.That(player.Artifacts.Contains(artifact.InstanceId), Is.True);
        GameRuleResult cardChoice = GameRules.TrySubmitOptionDecision(state, player.PlayerId,
            state.Resolution.PendingDecision.DecisionId, new[] { "hand" }, Resolve, new Random(1));
        Assert.That(cardChoice.Status, Is.EqualTo(GameRuleStatus.WaitingForChoice), cardChoice.Error);
        int copperId = player.Hand.Single();
        GameRuleResult finished = GameRules.TrySubmitDecision(state, player.PlayerId,
            state.Resolution.PendingDecision.DecisionId, new[] { copperId }, Resolve, new Random(1));
        Assert.That(finished.Status, Is.EqualTo(GameRuleStatus.Applied), finished.Error);
        Assert.That(state.TrashedCards, Does.Contain(copperId));
    }

    [Test]
    public void Necromancien_CanTrashActionFromSupplyWithoutAwardingArtifactForThatTrash()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);
        SupplyPileSnapshot actionPile = new SupplyPileSnapshot("base:village", 10, true);
        SupplyPileSnapshot treasurePile = new SupplyPileSnapshot("base:argent", 10);
        state.SupplyPiles.Add(actionPile);
        state.SupplyPiles.Add(treasurePile);
        AddOwned(state, player, "base:cuivre", CardZone.Deck);
        CardInstance necromancer = AddOwned(state, player, "fleaux:necromancien", CardZone.Hand);

        GameRuleResult sourceChoice = GameRules.TryPlayCard(state, player.PlayerId, necromancer.InstanceId, Resolve, new Random(1));
        Assert.That(sourceChoice.Status, Is.EqualTo(GameRuleStatus.WaitingForChoice), sourceChoice.Error);
        GameRuleResult supplyChoice = GameRules.TrySubmitOptionDecision(state, player.PlayerId,
            state.Resolution.PendingDecision.DecisionId, new[] { "supply" }, Resolve, new Random(1));

        Assert.That(supplyChoice.Status, Is.EqualTo(GameRuleStatus.WaitingForChoice), supplyChoice.Error);
        Assert.That(state.Resolution.PendingDecision.CandidateDefinitionIds, Does.Contain("base:village"));
        Assert.That(state.Resolution.PendingDecision.CandidateDefinitionIds, Does.Not.Contain("base:argent"));
        GameRuleResult finished = GameRules.TrySubmitSupplyDecision(state, player.PlayerId,
            state.Resolution.PendingDecision.DecisionId, new[] { "base:village" }, Resolve, new Random(1));

        Assert.That(finished.Status, Is.EqualTo(GameRuleStatus.Applied), finished.Error);
        Assert.That(actionPile.RemainingCount, Is.EqualTo(9));
        Assert.That(player.CardsTrashedThisTurn, Is.EqualTo(1));
        Assert.That(player.Artifacts, Is.Empty);
        Assert.That(state.TrashedCards.Select(id => state.CardInstances.Single(card => card.InstanceId == id).DefinitionId),
            Does.Contain("base:village"));
    }

    [Test]
    public void CharretteFunebre_CanTrashItselfForFiveCoins()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);
        CardInstance cart = AddOwned(state, player, "fleaux:charrette_funebre", CardZone.Hand);

        GameRuleResult choice = GameRules.TryPlayCard(state, player.PlayerId, cart.InstanceId, Resolve, new Random(1));
        Assert.That(choice.Status, Is.EqualTo(GameRuleStatus.WaitingForChoice), choice.Error);
        GameRuleResult finished = GameRules.TrySubmitOptionDecision(state, player.PlayerId,
            state.Resolution.PendingDecision.DecisionId, new[] { "self" }, Resolve, new Random(1));

        Assert.That(finished.Status, Is.EqualTo(GameRuleStatus.Applied), finished.Error);
        Assert.That(state.TrashedCards, Does.Contain(cart.InstanceId));
        Assert.That(player.InPlay.Contains(cart.InstanceId), Is.False);
        Assert.That(player.Coins, Is.EqualTo(5));
    }

    [Test]
    public void CharretteFunebre_CanTrashActionFromHandForFiveCoins()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);
        CardInstance action = AddOwned(state, player, "base:village", CardZone.Hand);
        CardInstance cart = AddOwned(state, player, "fleaux:charrette_funebre", CardZone.Hand);

        GameRuleResult sourceChoice = GameRules.TryPlayCard(state, player.PlayerId, cart.InstanceId, Resolve, new Random(1));
        Assert.That(sourceChoice.Status, Is.EqualTo(GameRuleStatus.WaitingForChoice), sourceChoice.Error);
        GameRuleResult cardChoice = GameRules.TrySubmitOptionDecision(state, player.PlayerId,
            state.Resolution.PendingDecision.DecisionId, new[] { "action" }, Resolve, new Random(1));
        Assert.That(cardChoice.Status, Is.EqualTo(GameRuleStatus.WaitingForChoice), cardChoice.Error);
        GameRuleResult finished = GameRules.TrySubmitDecision(state, player.PlayerId,
            state.Resolution.PendingDecision.DecisionId, new[] { action.InstanceId }, Resolve, new Random(1));

        Assert.That(finished.Status, Is.EqualTo(GameRuleStatus.Applied), finished.Error);
        Assert.That(state.TrashedCards, Does.Contain(action.InstanceId));
        Assert.That(player.InPlay, Does.Contain(cart.InstanceId));
        Assert.That(player.Coins, Is.EqualTo(5));
    }

    [Test]
    public void Cloitre_DiscardsThenKeepsOneCardUntilNextTurn()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);
        CardInstance first = AddOwned(state, player, "base:cuivre", CardZone.Deck);
        CardInstance second = AddOwned(state, player, "base:argent", CardZone.Deck);
        CardInstance kept = AddOwned(state, player, "base:domaine", CardZone.Deck);
        CardInstance cloister = AddOwned(state, player, "fleaux:cloitre", CardZone.Hand);

        GameRuleResult discardChoice = GameRules.TryPlayCard(state, player.PlayerId, cloister.InstanceId, Resolve, new Random(1));
        Assert.That(discardChoice.Status, Is.EqualTo(GameRuleStatus.WaitingForChoice), discardChoice.Error);
        GameRuleResult keepChoice = GameRules.TrySubmitDecision(state, player.PlayerId,
            state.Resolution.PendingDecision.DecisionId, new[] { first.InstanceId, second.InstanceId }, Resolve, new Random(1));
        Assert.That(keepChoice.Status, Is.EqualTo(GameRuleStatus.WaitingForChoice), keepChoice.Error);
        GameRuleResult finished = GameRules.TrySubmitDecision(state, player.PlayerId,
            state.Resolution.PendingDecision.DecisionId, new[] { kept.InstanceId }, Resolve, new Random(1));

        Assert.That(finished.Status, Is.EqualTo(GameRuleStatus.Applied), finished.Error);
        Assert.That(state.SetAsideCards.Exists(entry => entry.CardInstanceId == kept.InstanceId), Is.True);
        Assert.That(player.Discard, Does.Contain(first.InstanceId));
        Assert.That(player.Discard, Does.Contain(second.InstanceId));
    }

    [Test]
    public void Cachot_SetAsideActionIsPlayedAtOwnersNextTurn()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);
        CardInstance village = AddOwned(state, player, "base:village", CardZone.Deck);
        CardInstance dungeon = AddOwned(state, player, "fleaux:cachot", CardZone.Hand);

        GameRuleResult played = GameRules.TryPlayCard(state, player.PlayerId, dungeon.InstanceId, Resolve, new Random(1));
        Assert.That(played.Status, Is.EqualTo(GameRuleStatus.Applied), played.Error);
        Assert.That(state.SetAsideCards.Exists(entry => entry.CardInstanceId == village.InstanceId), Is.True);
        state.TurnNumber = 2;
        GameRuleResult nextTurn = TurnLifecycleRules.TryResolveTurnStarted(state, player, Resolve, new Random(1));

        Assert.That(nextTurn.Status, Is.EqualTo(GameRuleStatus.Applied), nextTurn.Error);
        Assert.That(player.InPlay.Contains(village.InstanceId), Is.True);
        Assert.That(player.ResolvedDurationCards.Contains(dungeon.InstanceId), Is.True);
    }

    [Test]
    public void MortVivant_ReturnsToItsSupplyPileAtTurnEnd()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);
        SupplyPileSnapshot pile = new SupplyPileSnapshot("fleaux:mort_vivant", 9, true);
        state.SupplyPiles.Add(pile);
        AddOwned(state, player, "base:cuivre", CardZone.Deck);
        AddOwned(state, player, "base:argent", CardZone.Deck);
        CardInstance undead = AddOwned(state, player, "fleaux:mort_vivant", CardZone.Hand);
        Assert.That(ResolutionQueue.TryBegin(state, player.PlayerId, out ResolutionQueue queue, out string beginError), Is.True, beginError);
        Assert.That(TrashRules.TryTrashFromHand(state, player, undead.InstanceId, 0, queue.Events, out string trashError), Is.True, trashError);
        TriggerResolutionResult resolved = TriggerResolver.ResolvePending(queue, state, Resolve, new Random(1));

        Assert.That(resolved.Status, Is.EqualTo(EffectResolutionStatus.Applied), resolved.Error);
        Assert.That(state.SetAsideCards.Exists(entry => entry.CardInstanceId == undead.InstanceId), Is.True);
        Assert.That(SetAsideRules.TryResolveTurnEnd(state, player, out string returnError), Is.True, returnError);
        Assert.That(pile.RemainingCount, Is.EqualTo(10));
        Assert.That(state.CardInstances.Exists(card => card.InstanceId == undead.InstanceId), Is.False);
    }

    [Test]
    public void ExecutionPublique_DrawsToEightAndEndsActionPhase()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);
        AddOwned(state, player, "base:domaine", CardZone.Hand);
        AddOwned(state, player, "base:duche", CardZone.Hand);
        AddOwned(state, player, "base:malediction", CardZone.Hand);
        AddOwned(state, player, "base:village", CardZone.Deck);
        for (int index = 0; index < 4; index++) AddOwned(state, player, "base:cuivre", CardZone.Deck);
        CardInstance execution = AddOwned(state, player, "fleaux:execution_publique", CardZone.Hand);

        GameRuleResult waiting = GameRules.TryPlayCard(state, player.PlayerId, execution.InstanceId, Resolve, new Random(1));
        Assert.That(waiting.Status, Is.EqualTo(GameRuleStatus.WaitingForChoice), waiting.Error);
        Assert.That(player.Hand.Count, Is.EqualTo(8));
        GameRuleResult finished = GameRules.TrySubmitDecision(state, player.PlayerId,
            state.Resolution.PendingDecision.DecisionId, Array.Empty<int>(), Resolve, new Random(1));

        Assert.That(finished.Status, Is.EqualTo(GameRuleStatus.Applied), finished.Error);
        Assert.That(state.Phase, Is.EqualTo(GameRules.BuyPhase));
    }

    [Test]
    public void Elixir_DoublePlaysThenTrashesSelectedNonDurationAction()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);
        SupplyPileSnapshot pile = new SupplyPileSnapshot("fleaux:elixir", 9, true);
        state.SupplyPiles.Add(pile);
        AddOwned(state, player, "base:cuivre", CardZone.Deck);
        AddOwned(state, player, "base:argent", CardZone.Deck);
        CardInstance village = AddOwned(state, player, "base:village", CardZone.Hand);
        CardInstance cultist = AddOwned(state, player, "fleaux:cultiste", CardZone.Hand);
        CardInstance elixir = AddOwned(state, player, "fleaux:elixir", CardZone.Hand);

        GameRuleResult options = GameRules.TryPlayCard(state, player.PlayerId, elixir.InstanceId, Resolve, new Random(1));
        Assert.That(options.Status, Is.EqualTo(GameRuleStatus.WaitingForChoice), options.Error);
        GameRuleResult cardChoice = GameRules.TrySubmitOptionDecision(state, player.PlayerId,
            state.Resolution.PendingDecision.DecisionId, new[] { "coins", "double" }, Resolve, new Random(1));
        Assert.That(cardChoice.Status, Is.EqualTo(GameRuleStatus.WaitingForChoice), cardChoice.Error);
        Assert.That(state.Resolution.PendingDecision.CandidateInstanceIds, Does.Contain(village.InstanceId));
        Assert.That(state.Resolution.PendingDecision.CandidateInstanceIds.Contains(cultist.InstanceId), Is.False);
        GameRuleResult finished = GameRules.TrySubmitDecision(state, player.PlayerId,
            state.Resolution.PendingDecision.DecisionId, new[] { village.InstanceId }, Resolve, new Random(1));

        Assert.That(finished.Status, Is.EqualTo(GameRuleStatus.Applied), finished.Error);
        Assert.That(player.Coins, Is.EqualTo(3));
        Assert.That(state.TrashedCards, Does.Contain(village.InstanceId));
        Assert.That(pile.RemainingCount, Is.EqualTo(10));
    }

    private static GameStateSnapshot NewState(out PlayerStateSnapshot player)
    {
        GameStateSnapshot state = new GameStateSnapshot
        {
            IsStarted = true, ActivePlayerId = "p1", Phase = GameRules.ActionPhase, TurnNumber = 1
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

    private static ExtensionCardData Resolve(string definitionId) =>
        RoomGameSetup.TryResolveCard(definitionId, out _, out ExtensionCardData card) ? card : null;
}
#endif
