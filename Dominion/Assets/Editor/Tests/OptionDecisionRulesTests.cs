#if UNITY_INCLUDE_TESTS
using System;
using NUnit.Framework;

public sealed class OptionDecisionRulesTests
{
    [Test]
    public void Pion_RequiresTwoDistinctOptionsAndAppliesOnlyThoseSelected()
    {
        ExtensionCatalog.Reload();
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);
        Assert.That(CardInstanceRules.TryCreateOwnedCard(state, player, "intrigue:pion", CardZone.Hand,
            out int pionId, out string pionError), Is.True, pionError);
        Assert.That(CardInstanceRules.TryCreateOwnedCard(state, player, "base:cuivre", CardZone.Deck,
            out int copperId, out string copperError), Is.True, copperError);

        GameRuleResult played = GameRules.TryPlayCard(state, player.PlayerId, pionId, ResolveDefinition, new Random(1));

        Assert.That(played.Status, Is.EqualTo(GameRuleStatus.WaitingForChoice), played.Error);
        PendingDecisionSnapshot decision = state.Resolution.PendingDecision;
        Assert.That(decision.Zone, Is.EqualTo("options"));
        Assert.That(decision.MinSelections, Is.EqualTo(2));
        Assert.That(decision.MaxSelections, Is.EqualTo(2));
        CollectionAssert.AreEquivalent(new[] { "card", "action", "buy", "coin" }, decision.CandidateDefinitionIds);
        CollectionAssert.AreEquivalent(new[] { "+1 Carte", "+1 Action", "+1 Achat", "+1 🪙" }, decision.CandidateOptionLabels);

        GameRuleResult duplicate = GameRules.TrySubmitOptionDecision(
            state, player.PlayerId, decision.DecisionId, new[] { "card", "card" }, ResolveDefinition, new Random(1));

        Assert.That(duplicate.Status, Is.EqualTo(GameRuleStatus.Rejected));
        StringAssert.Contains("duplicate option", duplicate.Error);
        Assert.That(state.Resolution.PendingDecision.IsPending, Is.True);

        GameRuleResult resolved = GameRules.TrySubmitOptionDecision(
            state, player.PlayerId, decision.DecisionId, new[] { "card", "action" }, ResolveDefinition, new Random(1));

        Assert.That(resolved.Status, Is.EqualTo(GameRuleStatus.Applied), resolved.Error);
        CollectionAssert.AreEqual(new[] { copperId }, player.Hand);
        Assert.That(player.Actions, Is.EqualTo(1));
        Assert.That(player.Buys, Is.EqualTo(1));
        Assert.That(player.Coins, Is.Zero);
        Assert.That(state.Resolution.IsActive, Is.False);
        Assert.That(GameStateValidator.TryValidate(state, out string validationError), Is.True, validationError);
    }

    [Test]
    public void Pion_CanChooseBuyAndCoinWithoutDrawing()
    {
        ExtensionCatalog.Reload();
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);
        Assert.That(CardInstanceRules.TryCreateOwnedCard(state, player, "intrigue:pion", CardZone.Hand,
            out int pionId, out string pionError), Is.True, pionError);

        GameRuleResult played = GameRules.TryPlayCard(state, player.PlayerId, pionId, ResolveDefinition, new Random(1));
        string decisionId = state.Resolution.PendingDecision.DecisionId;
        GameRuleResult resolved = GameRules.TrySubmitOptionDecision(
            state, player.PlayerId, decisionId, new[] { "buy", "coin" }, ResolveDefinition, new Random(1));

        Assert.That(played.Status, Is.EqualTo(GameRuleStatus.WaitingForChoice), played.Error);
        Assert.That(resolved.Status, Is.EqualTo(GameRuleStatus.Applied), resolved.Error);
        Assert.That(player.Hand, Is.Empty);
        Assert.That(player.Actions, Is.Zero);
        Assert.That(player.Buys, Is.EqualTo(2));
        Assert.That(player.Coins, Is.EqualTo(1));
        Assert.That(state.Resolution.IsActive, Is.False);
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
            MatchId = "option-decision-tests",
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
            Buys = 1,
            Coins = 0
        };
        state.Players.Add(player);
        return state;
    }
}
#endif
