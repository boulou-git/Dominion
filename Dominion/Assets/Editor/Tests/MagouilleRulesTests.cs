#if UNITY_INCLUDE_TESTS
using System;
using System.Linq;
using NUnit.Framework;

public sealed class MagouilleRulesTests
{
    [SetUp]
    public void ReloadCatalog() => ExtensionCatalog.Reload();

    [Test]
    public void Magouille_TrashesEachTopCardAndLetsAttackerChooseExactCostReplacement()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot attacker, out PlayerStateSnapshot firstVictim, out PlayerStateSnapshot secondVictim);
        int magouille = AddCard(state, attacker, "intrigue:magouille", CardZone.Hand);
        int copper = AddCard(state, firstVictim, "base:cuivre", CardZone.Deck);
        int estate = AddCard(state, secondVictim, "base:domaine", CardZone.Deck);
        state.SupplyPiles.Add(new SupplyPileSnapshot("base:malediction", 10));
        state.SupplyPiles.Add(new SupplyPileSnapshot("base:domaine", 8));

        GameRuleResult played = Play(state, attacker, magouille);

        Assert.That(played.Status, Is.EqualTo(GameRuleStatus.WaitingForChoice), played.Error);
        Assert.That(attacker.Coins, Is.EqualTo(2));
        Assert.That(state.Resolution.PendingDecision.Zone, Is.EqualTo("supply"));
        Assert.That(state.Resolution.PendingDecision.PlayerId, Is.EqualTo(attacker.PlayerId));
        CollectionAssert.AreEqual(new[] { "base:malediction" }, state.Resolution.PendingDecision.CandidateDefinitionIds);
        CollectionAssert.Contains(state.TrashedCards, copper);
        Assert.That(firstVictim.Deck, Is.Empty);
        Assert.That(secondVictim.Deck, Does.Contain(estate));

        GameRuleResult firstReplacement = SubmitSupply(state, attacker, "base:malediction");

        Assert.That(firstReplacement.Status, Is.EqualTo(GameRuleStatus.WaitingForChoice), firstReplacement.Error);
        Assert.That(DefinitionId(state, firstVictim.Discard.Single()), Is.EqualTo("base:malediction"));
        CollectionAssert.AreEqual(new[] { "base:domaine" }, state.Resolution.PendingDecision.CandidateDefinitionIds);
        CollectionAssert.AreEquivalent(new[] { copper, estate }, state.TrashedCards);
        Assert.That(GameStateValidator.TryValidate(state, out string pendingError), Is.True, pendingError);

        GameRuleResult secondReplacement = SubmitSupply(state, attacker, "base:domaine");

        Assert.That(secondReplacement.Status, Is.EqualTo(GameRuleStatus.Applied), secondReplacement.Error);
        Assert.That(DefinitionId(state, secondVictim.Discard.Single()), Is.EqualTo("base:domaine"));
        Assert.That(Owner(state, firstVictim.Discard.Single()), Is.EqualTo(firstVictim.PlayerId));
        Assert.That(Owner(state, secondVictim.Discard.Single()), Is.EqualTo(secondVictim.PlayerId));
        Assert.That(Owner(state, copper), Is.EqualTo(firstVictim.PlayerId));
        Assert.That(Owner(state, estate), Is.EqualTo(secondVictim.PlayerId));
        Assert.That(state.Resolution.IsActive, Is.False);
        Assert.That(GameStateValidator.TryValidate(state, out string validationError), Is.True, validationError);
    }

    [Test]
    public void Magouille_DoesNotAffectPlayerProtectedByDouves()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot attacker, out PlayerStateSnapshot protectedPlayer, out PlayerStateSnapshot emptyPlayer);
        int magouille = AddCard(state, attacker, "intrigue:magouille", CardZone.Hand);
        int moat = AddCard(state, protectedPlayer, "base:douves", CardZone.Hand);
        int copper = AddCard(state, protectedPlayer, "base:cuivre", CardZone.Deck);
        state.SupplyPiles.Add(new SupplyPileSnapshot("base:malediction", 10));

        GameRuleResult played = Play(state, attacker, magouille);
        Assert.That(played.Status, Is.EqualTo(GameRuleStatus.WaitingForChoice), played.Error);
        Assert.That(state.Resolution.PendingDecision.Operation, Is.EqualTo("block_attack_reaction"));

        GameRuleResult protectedResult = GameRules.TrySubmitDecision(state, protectedPlayer.PlayerId,
            state.Resolution.PendingDecision.DecisionId, new[] { moat }, ResolveDefinition, new Random(1));

        Assert.That(protectedResult.Status, Is.EqualTo(GameRuleStatus.Applied), protectedResult.Error);
        Assert.That(attacker.Coins, Is.EqualTo(2));
        CollectionAssert.AreEqual(new[] { copper }, protectedPlayer.Deck);
        Assert.That(state.TrashedCards, Is.Empty);
        Assert.That(protectedPlayer.Discard, Is.Empty);
        Assert.That(emptyPlayer.Hand, Is.Empty);
    }

    private static GameRuleResult SubmitSupply(GameStateSnapshot state, PlayerStateSnapshot attacker, string definitionId) =>
        GameRules.TrySubmitSupplyDecision(state, attacker.PlayerId, state.Resolution.PendingDecision.DecisionId,
            new[] { definitionId }, ResolveDefinition, new Random(1));

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
            MatchId = "magouille-tests",
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
