#if UNITY_INCLUDE_TESTS
using System;
using System.Linq;
using NUnit.Framework;

public sealed class IntrigueRemainingCardsRulesTests
{
    [SetUp]
    public void ReloadCatalog() => ExtensionCatalog.Reload();

    [Test]
    public void ConspirateurAndFerronnerieUseGenericTurnAndGainedTypeConditions()
    {
        GameStateSnapshot conspiracyState = NewState(out PlayerStateSnapshot player);
        player.Actions = 1;
        player.ActionsPlayedThisTurn = 2;
        int conspirator = AddCard(conspiracyState, player, "intrigue:conspirateur", CardZone.Hand);
        int drawn = AddCard(conspiracyState, player, "base:cuivre", CardZone.Deck);

        GameRuleResult conspiracy = Play(conspiracyState, player, conspirator);

        Assert.That(conspiracy.Status, Is.EqualTo(GameRuleStatus.Applied), conspiracy.Error);
        Assert.That(player.ActionsPlayedThisTurn, Is.EqualTo(3));
        Assert.That(player.Coins, Is.EqualTo(2));
        Assert.That(player.Actions, Is.EqualTo(1));
        CollectionAssert.Contains(player.Hand, drawn);

        GameStateSnapshot ironworksState = NewState(out PlayerStateSnapshot smith);
        smith.Actions = 1;
        int ironworks = AddCard(ironworksState, smith, "intrigue:ferronnerie", CardZone.Hand);
        int bonusDraw = AddCard(ironworksState, smith, "base:cuivre", CardZone.Deck);
        ironworksState.SupplyPiles.Add(new SupplyPileSnapshot("intrigue:moulin", 10));

        Assert.That(Play(ironworksState, smith, ironworks).Status, Is.EqualTo(GameRuleStatus.WaitingForChoice));
        GameRuleResult gained = SubmitSupply(ironworksState, smith, "intrigue:moulin");

        Assert.That(gained.Status, Is.EqualTo(GameRuleStatus.Applied), gained.Error);
        Assert.That(smith.Actions, Is.EqualTo(1));
        Assert.That(smith.Coins, Is.Zero);
        CollectionAssert.Contains(smith.Hand, bonusDraw);
        Assert.That(smith.Discard.Select(id => DefinitionId(ironworksState, id)), Does.Contain("intrigue:moulin"));
    }

    [Test]
    public void PassageSecretInsertsAtChosenPositionAndPatrouilleOrdersRemainingCards()
    {
        GameStateSnapshot passageState = NewState(out PlayerStateSnapshot player);
        int passage = AddCard(passageState, player, "intrigue:passage_secret", CardZone.Hand);
        int province = AddCard(passageState, player, "base:province", CardZone.Deck);
        int copper = AddCard(passageState, player, "base:cuivre", CardZone.Deck);
        AddCard(passageState, player, "base:argent", CardZone.Deck);

        Assert.That(Play(passageState, player, passage).Status, Is.EqualTo(GameRuleStatus.WaitingForChoice));
        Assert.That(SubmitCards(passageState, player, copper).Status, Is.EqualTo(GameRuleStatus.WaitingForChoice));
        GameRuleResult inserted = SubmitOptions(passageState, player, "0");

        Assert.That(inserted.Status, Is.EqualTo(GameRuleStatus.Applied), inserted.Error);
        CollectionAssert.AreEqual(new[] { copper, province }, player.Deck);
        Assert.That(player.Inspected, Is.Empty);

        GameStateSnapshot patrolState = NewState(out PlayerStateSnapshot patrolPlayer);
        int patrol = AddCard(patrolState, patrolPlayer, "intrigue:patrouille", CardZone.Hand);
        int gold = AddCard(patrolState, patrolPlayer, "base:or", CardZone.Deck);
        int silver = AddCard(patrolState, patrolPlayer, "base:argent", CardZone.Deck);
        int curse = AddCard(patrolState, patrolPlayer, "base:malediction", CardZone.Deck);
        int estate = AddCard(patrolState, patrolPlayer, "base:domaine", CardZone.Deck);
        AddCard(patrolState, patrolPlayer, "base:cuivre", CardZone.Deck);
        AddCard(patrolState, patrolPlayer, "base:cuivre", CardZone.Deck);
        AddCard(patrolState, patrolPlayer, "base:cuivre", CardZone.Deck);

        Assert.That(Play(patrolState, patrolPlayer, patrol).Status, Is.EqualTo(GameRuleStatus.WaitingForChoice));
        GameRuleResult ordered = SubmitCards(patrolState, patrolPlayer, silver);

        Assert.That(ordered.Status, Is.EqualTo(GameRuleStatus.Applied), ordered.Error);
        CollectionAssert.Contains(patrolPlayer.Hand, estate);
        CollectionAssert.Contains(patrolPlayer.Hand, curse);
        CollectionAssert.AreEqual(new[] { silver, gold }, patrolPlayer.Deck);
        Assert.That(patrolState.Journal.Count(entry => entry.Kind == JournalRules.RevealKind), Is.EqualTo(4));
    }

    [Test]
    public void CourtisanChoosesOneDifferentOptionPerRevealedType()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);
        int courtier = AddCard(state, player, "intrigue:courtisan", CardZone.Hand);
        int harem = AddCard(state, player, "intrigue:harem", CardZone.Hand);
        state.SupplyPiles.Add(new SupplyPileSnapshot("base:or", 10));

        Assert.That(Play(state, player, courtier).Status, Is.EqualTo(GameRuleStatus.WaitingForChoice));
        Assert.That(SubmitCards(state, player, harem).Status, Is.EqualTo(GameRuleStatus.WaitingForChoice));
        Assert.That(state.Resolution.PendingDecision.MinSelections, Is.EqualTo(2));
        GameRuleResult options = SubmitOptions(state, player, "coins", "gold");

        Assert.That(options.Status, Is.EqualTo(GameRuleStatus.Applied), options.Error);
        Assert.That(player.Coins, Is.EqualTo(3));
        Assert.That(player.Discard.Select(id => DefinitionId(state, id)), Does.Contain("base:or"));
        Assert.That(state.Journal.Any(entry => entry.CardDefinitionId == "intrigue:harem"), Is.True);
    }

    [Test]
    public void DiplomateReactionDrawsDiscardsThenResumesAttack()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot attacker, out PlayerStateSnapshot defender);
        int militia = AddCard(state, attacker, "base:milice", CardZone.Hand);
        int diplomat = AddCard(state, defender, "intrigue:diplomate", CardZone.Hand);
        int first = AddCard(state, defender, "base:cuivre", CardZone.Hand);
        int second = AddCard(state, defender, "base:cuivre", CardZone.Hand);
        int third = AddCard(state, defender, "base:cuivre", CardZone.Hand);
        int fourth = AddCard(state, defender, "base:cuivre", CardZone.Hand);
        AddCard(state, defender, "base:argent", CardZone.Deck);
        AddCard(state, defender, "base:or", CardZone.Deck);

        Assert.That(Play(state, attacker, militia).Status, Is.EqualTo(GameRuleStatus.WaitingForChoice));
        Assert.That(SubmitCards(state, defender, diplomat).Status, Is.EqualTo(GameRuleStatus.WaitingForChoice));
        Assert.That(defender.Hand.Count, Is.EqualTo(7));
        Assert.That(SubmitCards(state, defender, first, second, third).Status, Is.EqualTo(GameRuleStatus.WaitingForChoice));
        GameRuleResult attackResolved = SubmitCards(state, defender, fourth);

        Assert.That(attackResolved.Status, Is.EqualTo(GameRuleStatus.Applied), attackResolved.Error);
        Assert.That(defender.Hand.Count, Is.EqualTo(3));
        Assert.That(attacker.Coins, Is.EqualTo(2));
        Assert.That(state.Journal.Any(entry => entry.CardDefinitionId == "intrigue:diplomate"), Is.True);
    }

    [Test]
    public void LarbinAndBourreauApplyChoicesOnlyToEligibleUnprotectedPlayers()
    {
        GameStateSnapshot minionState = NewState(out PlayerStateSnapshot attacker, out PlayerStateSnapshot affected, out PlayerStateSnapshot shortHand);
        int minion = AddCard(minionState, attacker, "intrigue:larbin", CardZone.Hand);
        for (int i = 0; i < 5; i++) AddCard(minionState, affected, "base:cuivre", CardZone.Hand);
        for (int i = 0; i < 4; i++) AddCard(minionState, shortHand, "base:cuivre", CardZone.Hand);
        for (int i = 0; i < 4; i++) AddCard(minionState, attacker, "base:cuivre", CardZone.Deck);
        for (int i = 0; i < 4; i++) AddCard(minionState, affected, "base:argent", CardZone.Deck);

        Assert.That(Play(minionState, attacker, minion).Status, Is.EqualTo(GameRuleStatus.WaitingForChoice));
        GameRuleResult redrawn = SubmitOptions(minionState, attacker, "redraw");

        Assert.That(redrawn.Status, Is.EqualTo(GameRuleStatus.Applied), redrawn.Error);
        Assert.That(attacker.Hand.Count, Is.EqualTo(4));
        Assert.That(affected.Hand.Count, Is.EqualTo(4));
        Assert.That(shortHand.Hand.Count, Is.EqualTo(4));

        GameStateSnapshot torturerState = NewState(out PlayerStateSnapshot torturer, out PlayerStateSnapshot discarder, out PlayerStateSnapshot gainer);
        int torturerCard = AddCard(torturerState, torturer, "intrigue:bourreau", CardZone.Hand);
        for (int i = 0; i < 3; i++) AddCard(torturerState, torturer, "base:cuivre", CardZone.Deck);
        int discardOne = AddCard(torturerState, discarder, "base:cuivre", CardZone.Hand);
        int discardTwo = AddCard(torturerState, discarder, "base:cuivre", CardZone.Hand);
        AddCard(torturerState, gainer, "base:cuivre", CardZone.Hand);
        torturerState.SupplyPiles.Add(new SupplyPileSnapshot("base:malediction", 10));

        Assert.That(Play(torturerState, torturer, torturerCard).Status, Is.EqualTo(GameRuleStatus.WaitingForChoice));
        Assert.That(SubmitOptions(torturerState, discarder, "discard").Status, Is.EqualTo(GameRuleStatus.WaitingForChoice));
        Assert.That(SubmitCards(torturerState, discarder, discardOne, discardTwo).Status, Is.EqualTo(GameRuleStatus.WaitingForChoice));
        GameRuleResult curseChosen = SubmitOptions(torturerState, gainer, "gain");

        Assert.That(curseChosen.Status, Is.EqualTo(GameRuleStatus.Applied), curseChosen.Error);
        Assert.That(discarder.Hand, Is.Empty);
        Assert.That(gainer.Hand.Select(id => DefinitionId(torturerState, id)), Does.Contain("base:malediction"));
    }

    [Test]
    public void RemplacementAndAmeliorationUseRememberedCostsAndDestinations()
    {
        GameStateSnapshot replacementState = NewState(out PlayerStateSnapshot attacker, out PlayerStateSnapshot victim);
        int replacement = AddCard(replacementState, attacker, "intrigue:remplacement", CardZone.Hand);
        int copper = AddCard(replacementState, attacker, "base:cuivre", CardZone.Hand);
        replacementState.SupplyPiles.Add(new SupplyPileSnapshot("base:domaine", 8));
        replacementState.SupplyPiles.Add(new SupplyPileSnapshot("base:malediction", 10));

        Assert.That(Play(replacementState, attacker, replacement).Status, Is.EqualTo(GameRuleStatus.WaitingForChoice));
        Assert.That(SubmitCards(replacementState, attacker, copper).Status, Is.EqualTo(GameRuleStatus.WaitingForChoice));
        GameRuleResult replaced = SubmitSupply(replacementState, attacker, "base:domaine");

        Assert.That(replaced.Status, Is.EqualTo(GameRuleStatus.Applied), replaced.Error);
        CollectionAssert.Contains(replacementState.TrashedCards, copper);
        Assert.That(attacker.Discard.Select(id => DefinitionId(replacementState, id)), Does.Contain("base:domaine"));
        Assert.That(victim.Discard.Select(id => DefinitionId(replacementState, id)), Does.Contain("base:malediction"));

        GameStateSnapshot upgradeState = NewState(out PlayerStateSnapshot upgrader);
        int upgrade = AddCard(upgradeState, upgrader, "intrigue:amelioration", CardZone.Hand);
        int estate = AddCard(upgradeState, upgrader, "base:domaine", CardZone.Hand);
        AddCard(upgradeState, upgrader, "base:cuivre", CardZone.Deck);
        upgradeState.SupplyPiles.Add(new SupplyPileSnapshot("base:argent", 10));
        upgradeState.SupplyPiles.Add(new SupplyPileSnapshot("base:domaine", 8));

        Assert.That(Play(upgradeState, upgrader, upgrade).Status, Is.EqualTo(GameRuleStatus.WaitingForChoice));
        Assert.That(SubmitCards(upgradeState, upgrader, estate).Status, Is.EqualTo(GameRuleStatus.WaitingForChoice));
        CollectionAssert.AreEqual(new[] { "base:argent" }, upgradeState.Resolution.PendingDecision.CandidateDefinitionIds);
        GameRuleResult upgraded = SubmitSupply(upgradeState, upgrader, "base:argent");

        Assert.That(upgraded.Status, Is.EqualTo(GameRuleStatus.Applied), upgraded.Error);
        CollectionAssert.Contains(upgradeState.TrashedCards, estate);
        Assert.That(upgrader.Discard.Select(id => DefinitionId(upgradeState, id)), Does.Contain("base:argent"));
        Assert.That(GameStateValidator.TryValidate(upgradeState, out string validationError), Is.True, validationError);
    }

    private static GameRuleResult Play(GameStateSnapshot state, PlayerStateSnapshot player, int instanceId) =>
        GameRules.TryPlayCard(state, player.PlayerId, instanceId, ResolveDefinition, new Random(1));

    private static GameRuleResult SubmitCards(GameStateSnapshot state, PlayerStateSnapshot player, params int[] ids) =>
        GameRules.TrySubmitDecision(state, player.PlayerId, state.Resolution.PendingDecision.DecisionId, ids, ResolveDefinition, new Random(1));

    private static GameRuleResult SubmitOptions(GameStateSnapshot state, PlayerStateSnapshot player, params string[] ids) =>
        GameRules.TrySubmitOptionDecision(state, player.PlayerId, state.Resolution.PendingDecision.DecisionId, ids, ResolveDefinition, new Random(1));

    private static GameRuleResult SubmitSupply(GameStateSnapshot state, PlayerStateSnapshot player, string id) =>
        GameRules.TrySubmitSupplyDecision(state, player.PlayerId, state.Resolution.PendingDecision.DecisionId, new[] { id }, ResolveDefinition, new Random(1));

    private static int AddCard(GameStateSnapshot state, PlayerStateSnapshot player, string definitionId, CardZone zone)
    {
        Assert.That(CardInstanceRules.TryCreateOwnedCard(state, player, definitionId, zone,
            out int instanceId, out string error), Is.True, error);
        return instanceId;
    }

    private static string DefinitionId(GameStateSnapshot state, int id) =>
        state.CardInstances.Single(card => card.InstanceId == id).DefinitionId;

    private static ExtensionCardData ResolveDefinition(string definitionId)
    {
        if (string.IsNullOrWhiteSpace(definitionId)) return null;
        int separator = definitionId.IndexOf(':');
        return separator > 0 ? ExtensionCatalog.FindCard(definitionId.Substring(0, separator), definitionId.Substring(separator + 1)) : null;
    }

    private static GameStateSnapshot NewState(out PlayerStateSnapshot first) => NewState(out first, out _, out _);
    private static GameStateSnapshot NewState(out PlayerStateSnapshot first, out PlayerStateSnapshot second) => NewState(out first, out second, out _);

    private static GameStateSnapshot NewState(out PlayerStateSnapshot first, out PlayerStateSnapshot second, out PlayerStateSnapshot third)
    {
        GameStateSnapshot state = new GameStateSnapshot
        {
            MatchId = "remaining-intrigue-tests", Version = 1, AuthorityEpoch = 1,
            IsStarted = true, IsInitialised = true, ActivePlayerId = "player-1",
            TurnNumber = 1, Phase = GameRules.ActionPhase, NextCardInstanceId = 1
        };
        first = Player("player-1", 1, 3); state.Players.Add(first);
        second = Player("player-2", 2, 0); third = Player("player-3", 3, 0);
        state.Players.Add(second); state.Players.Add(third);
        return state;
    }

    private static PlayerStateSnapshot Player(string id, int actor, int actions) => new PlayerStateSnapshot
    {
        PlayerId = id, ActorNumber = actor, NickName = id, IsConnected = true,
        Actions = actions, Buys = 1
    };
}
#endif
