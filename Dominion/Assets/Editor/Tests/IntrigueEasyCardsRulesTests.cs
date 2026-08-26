#if UNITY_INCLUDE_TESTS
using System;
using System.Linq;
using NUnit.Framework;

public sealed class IntrigueEasyCardsRulesTests
{
    [SetUp]
    public void ReloadCatalogs()
    {
        ExtensionCatalog.Reload();
        ScoringRules.Reload();
    }

    [Test]
    public void Taudis_RevealsHandAndDrawsWhenItContainsNoAction()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);
        int taudis = AddCard(state, player, "intrigue:taudis", CardZone.Hand);
        int shownCopper = AddCard(state, player, "base:cuivre", CardZone.Hand);
        AddCard(state, player, "base:cuivre", CardZone.Deck);
        AddCard(state, player, "base:argent", CardZone.Deck);

        GameRuleResult result = Play(state, player, taudis);

        Assert.That(result.Status, Is.EqualTo(GameRuleStatus.Applied), result.Error);
        Assert.That(player.Actions, Is.EqualTo(2));
        Assert.That(player.Hand.Count, Is.EqualTo(3));
        Assert.That(state.Journal.Any(entry => entry.CardDefinitionId == "base:cuivre" && entry.Kind == JournalRules.RevealKind), Is.True);
        Assert.That(player.Hand, Does.Contain(shownCopper));

        GameStateSnapshot actionState = NewState(out PlayerStateSnapshot actionPlayer);
        int actionTaudis = AddCard(actionState, actionPlayer, "intrigue:taudis", CardZone.Hand);
        int village = AddCard(actionState, actionPlayer, "base:village", CardZone.Hand);
        int deckCopper = AddCard(actionState, actionPlayer, "base:cuivre", CardZone.Deck);
        GameRuleResult actionResult = Play(actionState, actionPlayer, actionTaudis);

        Assert.That(actionResult.Status, Is.EqualTo(GameRuleStatus.Applied), actionResult.Error);
        CollectionAssert.AreEqual(new[] { village }, actionPlayer.Hand);
        CollectionAssert.AreEqual(new[] { deckCopper }, actionPlayer.Deck);
    }

    [Test]
    public void Intendant_TrashOptionSurvivesNestedCardDecision()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);
        int intendant = AddCard(state, player, "intrigue:intendant", CardZone.Hand);
        int copper = AddCard(state, player, "base:cuivre", CardZone.Hand);
        int estate = AddCard(state, player, "base:domaine", CardZone.Hand);

        GameRuleResult played = Play(state, player, intendant);
        string optionDecisionId = state.Resolution.PendingDecision.DecisionId;
        GameRuleResult optionChosen = GameRules.TrySubmitOptionDecision(
            state, player.PlayerId, optionDecisionId, new[] { "trash" }, ResolveDefinition, new Random(1));

        Assert.That(played.Status, Is.EqualTo(GameRuleStatus.WaitingForChoice), played.Error);
        Assert.That(optionChosen.Status, Is.EqualTo(GameRuleStatus.WaitingForChoice), optionChosen.Error);
        CollectionAssert.AreEqual(new[] { "trash" }, state.Resolution.SelectedOptionIds);
        Assert.That(GameStateValidator.TryValidate(state, out string pendingValidationError), Is.True, pendingValidationError);
        string cardDecisionId = state.Resolution.PendingDecision.DecisionId;

        GameRuleResult resolved = GameRules.TrySubmitDecision(
            state, player.PlayerId, cardDecisionId, new[] { copper, estate }, ResolveDefinition, new Random(1));

        Assert.That(resolved.Status, Is.EqualTo(GameRuleStatus.Applied), resolved.Error);
        CollectionAssert.AreEquivalent(new[] { copper, estate }, state.TrashedCards);
        Assert.That(state.Resolution.IsActive, Is.False);
        Assert.That(GameStateValidator.TryValidate(state, out string validationError), Is.True, validationError);
    }

    [Test]
    public void Baron_GainsEstateWhenNoEstateIsDiscarded()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);
        int baron = AddCard(state, player, "intrigue:baron", CardZone.Hand);
        state.SupplyPiles.Add(new SupplyPileSnapshot("base:domaine", 8));

        GameRuleResult result = Play(state, player, baron);

        Assert.That(result.Status, Is.EqualTo(GameRuleStatus.Applied), result.Error);
        Assert.That(player.Buys, Is.EqualTo(2));
        Assert.That(player.Coins, Is.Zero);
        Assert.That(player.Discard.Count, Is.EqualTo(1));
        Assert.That(DefinitionId(state, player.Discard.Single()), Is.EqualTo("base:domaine"));

        GameStateSnapshot discardState = NewState(out PlayerStateSnapshot discardPlayer);
        int discardBaron = AddCard(discardState, discardPlayer, "intrigue:baron", CardZone.Hand);
        int estate = AddCard(discardState, discardPlayer, "base:domaine", CardZone.Hand);
        discardState.SupplyPiles.Add(new SupplyPileSnapshot("base:domaine", 8));
        GameRuleResult discardPlayed = Play(discardState, discardPlayer, discardBaron);
        GameRuleResult discarded = GameRules.TrySubmitDecision(discardState, discardPlayer.PlayerId,
            discardState.Resolution.PendingDecision.DecisionId, new[] { estate }, ResolveDefinition, new Random(1));

        Assert.That(discardPlayed.Status, Is.EqualTo(GameRuleStatus.WaitingForChoice), discardPlayed.Error);
        Assert.That(discarded.Status, Is.EqualTo(GameRuleStatus.Applied), discarded.Error);
        Assert.That(discardPlayer.Coins, Is.EqualTo(4));
        CollectionAssert.AreEqual(new[] { estate }, discardPlayer.Discard);
        Assert.That(discardState.SupplyPiles.Single().RemainingCount, Is.EqualTo(8));
    }

    [Test]
    public void Moulin_AllowsPassOrExactlyTwoDiscards()
    {
        GameStateSnapshot passState = NewState(out PlayerStateSnapshot passPlayer);
        int passMoulin = AddCard(passState, passPlayer, "intrigue:moulin", CardZone.Hand);
        AddCard(passState, passPlayer, "base:cuivre", CardZone.Hand);
        AddCard(passState, passPlayer, "base:domaine", CardZone.Hand);
        GameRuleResult passPlayed = Play(passState, passPlayer, passMoulin);
        GameRuleResult passed = GameRules.TrySubmitDecision(passState, passPlayer.PlayerId,
            passState.Resolution.PendingDecision.DecisionId, Array.Empty<int>(), ResolveDefinition, new Random(1));

        Assert.That(passPlayed.Status, Is.EqualTo(GameRuleStatus.WaitingForChoice), passPlayed.Error);
        Assert.That(passed.Status, Is.EqualTo(GameRuleStatus.Applied), passed.Error);
        Assert.That(passPlayer.Coins, Is.Zero);
        Assert.That(passPlayer.Discard, Is.Empty);

        GameStateSnapshot discardState = NewState(out PlayerStateSnapshot discardPlayer);
        int discardMoulin = AddCard(discardState, discardPlayer, "intrigue:moulin", CardZone.Hand);
        int copper = AddCard(discardState, discardPlayer, "base:cuivre", CardZone.Hand);
        int estate = AddCard(discardState, discardPlayer, "base:domaine", CardZone.Hand);
        Play(discardState, discardPlayer, discardMoulin);
        GameRuleResult discarded = GameRules.TrySubmitDecision(discardState, discardPlayer.PlayerId,
            discardState.Resolution.PendingDecision.DecisionId, new[] { copper, estate }, ResolveDefinition, new Random(1));

        Assert.That(discarded.Status, Is.EqualTo(GameRuleStatus.Applied), discarded.Error);
        Assert.That(discardPlayer.Coins, Is.EqualTo(2));
        CollectionAssert.AreEquivalent(new[] { copper, estate }, discardPlayer.Discard);
    }

    [Test]
    public void VillageMinier_CanTrashItsOwnPlayedInstanceForCoins()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);
        int village = AddCard(state, player, "intrigue:village_minier", CardZone.Hand);

        GameRuleResult played = Play(state, player, village);
        GameRuleResult resolved = GameRules.TrySubmitOptionDecision(state, player.PlayerId,
            state.Resolution.PendingDecision.DecisionId, new[] { "trash" }, ResolveDefinition, new Random(1));

        Assert.That(played.Status, Is.EqualTo(GameRuleStatus.WaitingForChoice), played.Error);
        Assert.That(resolved.Status, Is.EqualTo(GameRuleStatus.Applied), resolved.Error);
        CollectionAssert.AreEqual(new[] { village }, state.TrashedCards);
        Assert.That(player.InPlay, Is.Empty);
        Assert.That(player.Actions, Is.EqualTo(2));
        Assert.That(player.Coins, Is.EqualTo(2));
    }

    [Test]
    public void Comptoir_GainsSilverOnlyAfterTrashingTwoCards()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);
        int comptoir = AddCard(state, player, "intrigue:comptoir", CardZone.Hand);
        int copper = AddCard(state, player, "base:cuivre", CardZone.Hand);
        int estate = AddCard(state, player, "base:domaine", CardZone.Hand);
        state.SupplyPiles.Add(new SupplyPileSnapshot("base:argent", 10));

        GameRuleResult played = Play(state, player, comptoir);
        GameRuleResult resolved = GameRules.TrySubmitDecision(state, player.PlayerId,
            state.Resolution.PendingDecision.DecisionId, new[] { copper, estate }, ResolveDefinition, new Random(1));

        Assert.That(played.Status, Is.EqualTo(GameRuleStatus.WaitingForChoice), played.Error);
        Assert.That(resolved.Status, Is.EqualTo(GameRuleStatus.Applied), resolved.Error);
        CollectionAssert.AreEquivalent(new[] { copper, estate }, state.TrashedCards);
        Assert.That(player.Hand.Count, Is.EqualTo(1));
        Assert.That(DefinitionId(state, player.Hand.Single()), Is.EqualTo("base:argent"));

        GameStateSnapshot shortState = NewState(out PlayerStateSnapshot shortPlayer);
        int shortComptoir = AddCard(shortState, shortPlayer, "intrigue:comptoir", CardZone.Hand);
        int onlyCard = AddCard(shortState, shortPlayer, "base:cuivre", CardZone.Hand);
        shortState.SupplyPiles.Add(new SupplyPileSnapshot("base:argent", 10));
        GameRuleResult shortPlayed = Play(shortState, shortPlayer, shortComptoir);
        GameRuleResult shortResolved = GameRules.TrySubmitDecision(shortState, shortPlayer.PlayerId,
            shortState.Resolution.PendingDecision.DecisionId, new[] { onlyCard }, ResolveDefinition, new Random(1));

        Assert.That(shortPlayed.Status, Is.EqualTo(GameRuleStatus.WaitingForChoice), shortPlayed.Error);
        Assert.That(shortResolved.Status, Is.EqualTo(GameRuleStatus.Applied), shortResolved.Error);
        Assert.That(shortPlayer.Hand, Is.Empty);
        Assert.That(shortState.SupplyPiles.Single().RemainingCount, Is.EqualTo(10));
    }

    [Test]
    public void HaremAndNobles_ApplyPlayEffectsAndScoreTwoPointsEach()
    {
        GameStateSnapshot haremState = NewState(out PlayerStateSnapshot haremPlayer);
        haremState.Phase = GameRules.BuyPhase;
        int harem = AddCard(haremState, haremPlayer, "intrigue:harem", CardZone.Hand);
        GameRuleResult haremPlayed = Play(haremState, haremPlayer, harem);

        Assert.That(haremPlayed.Status, Is.EqualTo(GameRuleStatus.Applied), haremPlayed.Error);
        Assert.That(haremPlayer.Coins, Is.EqualTo(2));
        Assert.That(ScoringRules.CalculatePlayerScore(haremState, haremPlayer).VictoryPoints, Is.EqualTo(2));

        GameStateSnapshot noblesState = NewState(out PlayerStateSnapshot noblesPlayer);
        int nobles = AddCard(noblesState, noblesPlayer, "intrigue:nobles", CardZone.Hand);
        GameRuleResult noblesPlayed = Play(noblesState, noblesPlayer, nobles);
        GameRuleResult noblesResolved = GameRules.TrySubmitOptionDecision(noblesState, noblesPlayer.PlayerId,
            noblesState.Resolution.PendingDecision.DecisionId, new[] { "actions" }, ResolveDefinition, new Random(1));

        Assert.That(noblesPlayed.Status, Is.EqualTo(GameRuleStatus.WaitingForChoice), noblesPlayed.Error);
        Assert.That(noblesResolved.Status, Is.EqualTo(GameRuleStatus.Applied), noblesResolved.Error);
        Assert.That(noblesPlayer.Actions, Is.EqualTo(2));
        Assert.That(ScoringRules.CalculatePlayerScore(noblesState, noblesPlayer).VictoryPoints, Is.EqualTo(2));
    }

    [Test]
    public void Duc_ScoresOnePointPerOwnedDuchyForEachCopy()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);
        AddCard(state, player, "intrigue:duc", CardZone.Deck);
        AddCard(state, player, "intrigue:duc", CardZone.Discard);
        AddCard(state, player, "base:duche", CardZone.Hand);
        AddCard(state, player, "base:duche", CardZone.Deck);
        AddCard(state, player, "base:duche", CardZone.Discard);

        PlayerScoreResult score = ScoringRules.CalculatePlayerScore(state, player);

        Assert.That(score.VictoryPoints, Is.EqualTo(15));
        CardScoreBreakdown duke = score.Breakdown.Single(entry => entry.DefinitionId == "intrigue:duc");
        Assert.That(duke.PointsPerCopy, Is.EqualTo(3));
        Assert.That(duke.TotalPoints, Is.EqualTo(6));
    }

    private static GameRuleResult Play(GameStateSnapshot state, PlayerStateSnapshot player, int instanceId) =>
        GameRules.TryPlayCard(state, player.PlayerId, instanceId, ResolveDefinition, new Random(1));

    private static int AddCard(GameStateSnapshot state, PlayerStateSnapshot player, string definitionId, CardZone zone)
    {
        Assert.That(CardInstanceRules.TryCreateOwnedCard(state, player, definitionId, zone,
            out int instanceId, out string error), Is.True, error);
        return instanceId;
    }

    private static string DefinitionId(GameStateSnapshot state, int instanceId) =>
        state.CardInstances.Single(card => card.InstanceId == instanceId).DefinitionId;

    private static ExtensionCardData ResolveDefinition(string definitionId)
    {
        if (string.IsNullOrWhiteSpace(definitionId)) return null;
        int separator = definitionId.IndexOf(':');
        if (separator <= 0 || separator >= definitionId.Length - 1) return null;
        return ExtensionCatalog.FindCard(definitionId.Substring(0, separator), definitionId.Substring(separator + 1));
    }

    private static GameStateSnapshot NewState(out PlayerStateSnapshot player)
    {
        GameStateSnapshot state = new GameStateSnapshot
        {
            MatchId = "intrigue-easy-tests",
            Version = 1,
            AuthorityEpoch = 1,
            IsStarted = true,
            IsInitialised = true,
            ActivePlayerId = "player-1",
            TurnNumber = 1,
            Phase = GameRules.ActionPhase,
            NextCardInstanceId = 1
        };
        player = new PlayerStateSnapshot
        {
            PlayerId = "player-1",
            ActorNumber = 1,
            NickName = "Player 1",
            IsConnected = true,
            Actions = 1,
            Buys = 1
        };
        state.Players.Add(player);
        return state;
    }
}
#endif
