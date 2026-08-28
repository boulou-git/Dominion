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
            state.Resolution.PendingDecision.DecisionId, new[] { "action" }, Resolve, new Random(1));
        Assert.That(second.Status, Is.EqualTo(GameRuleStatus.WaitingForChoice), second.Error);
        GameRuleResult finished = GameRules.TrySubmitOptionDecision(state, player.PlayerId,
            state.Resolution.PendingDecision.DecisionId, new[] { "action" }, Resolve, new Random(1));

        Assert.That(finished.Status, Is.EqualTo(GameRuleStatus.Applied), finished.Error);
        Assert.That(player.Actions, Is.EqualTo(2));
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
        GameRuleResult finished = GameRules.TrySubmitDecision(state, player.PlayerId,
            state.Resolution.PendingDecision.DecisionId, Array.Empty<int>(), Resolve, new Random(1));
        Assert.That(finished.Status, Is.EqualTo(GameRuleStatus.Applied), finished.Error);
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
    public void Elixir_DoublePlaysThenTrashesSelectedAction()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);
        SupplyPileSnapshot pile = new SupplyPileSnapshot("fleaux:elixir", 9, true);
        state.SupplyPiles.Add(pile);
        AddOwned(state, player, "base:cuivre", CardZone.Deck);
        AddOwned(state, player, "base:argent", CardZone.Deck);
        CardInstance village = AddOwned(state, player, "base:village", CardZone.Hand);
        CardInstance elixir = AddOwned(state, player, "fleaux:elixir", CardZone.Hand);

        GameRuleResult options = GameRules.TryPlayCard(state, player.PlayerId, elixir.InstanceId, Resolve, new Random(1));
        Assert.That(options.Status, Is.EqualTo(GameRuleStatus.WaitingForChoice), options.Error);
        GameRuleResult cardChoice = GameRules.TrySubmitOptionDecision(state, player.PlayerId,
            state.Resolution.PendingDecision.DecisionId, new[] { "coins", "double" }, Resolve, new Random(1));
        Assert.That(cardChoice.Status, Is.EqualTo(GameRuleStatus.WaitingForChoice), cardChoice.Error);
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
