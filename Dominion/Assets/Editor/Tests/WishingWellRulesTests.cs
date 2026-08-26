#if UNITY_INCLUDE_TESTS
using System;
using System.Linq;
using NUnit.Framework;

public sealed class WishingWellRulesTests
{
    [SetUp]
    public void ReloadCatalog() => ExtensionCatalog.Reload();

    [Test]
    public void PuitsAuxSouhaits_DrawsThenTakesNamedRevealedCardIntoHand()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);
        int well = AddCard(state, player, "intrigue:puits_aux_souhaits", CardZone.Hand);
        int estate = AddCard(state, player, "base:domaine", CardZone.Deck);
        int copper = AddCard(state, player, "base:cuivre", CardZone.Deck);
        state.SupplyPiles.Add(new SupplyPileSnapshot("base:domaine", 8));
        state.SupplyPiles.Add(new SupplyPileSnapshot("base:cuivre", 46));

        GameRuleResult played = Play(state, player, well);

        Assert.That(played.Status, Is.EqualTo(GameRuleStatus.WaitingForChoice), played.Error);
        Assert.That(player.Actions, Is.EqualTo(1));
        CollectionAssert.AreEqual(new[] { copper }, player.Hand);
        Assert.That(state.Resolution.PendingDecision.Operation, Is.EqualTo("name_card"));
        Assert.That(state.Resolution.PendingDecision.Zone, Is.EqualTo("options"));
        CollectionAssert.Contains(state.Resolution.PendingDecision.CandidateDefinitionIds, "base:domaine");
        int estateIndex = state.Resolution.PendingDecision.CandidateDefinitionIds.IndexOf("base:domaine");
        Assert.That(state.Resolution.PendingDecision.CandidateOptionLabels[estateIndex], Is.EqualTo("Domaine"));

        GameRuleResult named = SubmitName(state, player, "base:domaine");

        Assert.That(named.Status, Is.EqualTo(GameRuleStatus.Applied), named.Error);
        CollectionAssert.AreEquivalent(new[] { copper, estate }, player.Hand);
        Assert.That(player.Deck, Is.Empty);
        Assert.That(player.Inspected, Is.Empty);
        Assert.That(state.Journal.Any(entry => entry.Kind == JournalRules.RevealKind && entry.CardDefinitionId == "base:domaine"), Is.True);
        Assert.That(state.Resolution.IsActive, Is.False);
        Assert.That(GameStateValidator.TryValidate(state, out string validationError), Is.True, validationError);
    }

    [Test]
    public void PuitsAuxSouhaits_LeavesDifferentRevealedCardOnTopOfDeck()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);
        int well = AddCard(state, player, "intrigue:puits_aux_souhaits", CardZone.Hand);
        int estate = AddCard(state, player, "base:domaine", CardZone.Deck);
        int copper = AddCard(state, player, "base:cuivre", CardZone.Deck);
        state.SupplyPiles.Add(new SupplyPileSnapshot("base:cuivre", 46));

        GameRuleResult played = Play(state, player, well);
        GameRuleResult named = SubmitName(state, player, "base:cuivre");

        Assert.That(played.Status, Is.EqualTo(GameRuleStatus.WaitingForChoice), played.Error);
        Assert.That(named.Status, Is.EqualTo(GameRuleStatus.Applied), named.Error);
        CollectionAssert.AreEqual(new[] { copper }, player.Hand);
        CollectionAssert.AreEqual(new[] { estate }, player.Deck);
        Assert.That(player.Deck.Last(), Is.EqualTo(estate));
        Assert.That(player.Inspected, Is.Empty);
        Assert.That(state.Journal.Any(entry => entry.CardDefinitionId == "base:domaine"), Is.True);
    }

    private static GameRuleResult SubmitName(GameStateSnapshot state, PlayerStateSnapshot player, string definitionId) =>
        GameRules.TrySubmitOptionDecision(state, player.PlayerId, state.Resolution.PendingDecision.DecisionId,
            new[] { definitionId }, ResolveDefinition, new Random(1));

    private static GameRuleResult Play(GameStateSnapshot state, PlayerStateSnapshot player, int instanceId) =>
        GameRules.TryPlayCard(state, player.PlayerId, instanceId, ResolveDefinition, new Random(1));

    private static int AddCard(GameStateSnapshot state, PlayerStateSnapshot player, string definitionId, CardZone zone)
    {
        Assert.That(CardInstanceRules.TryCreateOwnedCard(state, player, definitionId, zone,
            out int instanceId, out string error), Is.True, error);
        return instanceId;
    }

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
            MatchId = "wishing-well-tests",
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
            NickName = "player-1",
            IsConnected = true,
            Actions = 1,
            Buys = 1
        };
        state.Players.Add(player);
        return state;
    }
}
#endif
