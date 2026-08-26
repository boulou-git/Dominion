#if UNITY_INCLUDE_TESTS
using System;
using System.Linq;
using NUnit.Framework;

public sealed class BridgeRulesTests
{
    [SetUp]
    public void ReloadCatalog() => ExtensionCatalog.Reload();

    [Test]
    public void Pont_AddsResourcesAndReducesPurchaseCostForCurrentTurn()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);
        int bridge = AddCard(state, player, "intrigue:pont", CardZone.Hand);
        state.SupplyPiles.Add(new SupplyPileSnapshot("base:argent", 10));

        GameRuleResult played = Play(state, player, bridge);

        Assert.That(played.Status, Is.EqualTo(GameRuleStatus.Applied), played.Error);
        Assert.That(player.Buys, Is.EqualTo(2));
        Assert.That(player.Coins, Is.EqualTo(1));
        Assert.That(player.CostReductionThisTurn, Is.EqualTo(1));
        Assert.That(CostRules.GetEffectiveCost(state, ResolveDefinition("base:argent")), Is.EqualTo(2));

        player.Coins = 2;
        state.Phase = GameRules.BuyPhase;
        GameRuleResult bought = GameRules.TryBuyCard(state, player.PlayerId, "base:argent", ResolveDefinition, new Random(1));

        Assert.That(bought.Status, Is.EqualTo(GameRuleStatus.Applied), bought.Error);
        Assert.That(player.Coins, Is.Zero);
        Assert.That(player.Buys, Is.EqualTo(1));
        Assert.That(player.Discard.Select(id => DefinitionId(state, id)), Does.Contain("base:argent"));
        Assert.That(GameStateValidator.TryValidate(state, out string validationError), Is.True, validationError);
    }

    [Test]
    public void Pont_ReductionsStackClampAtZeroAndCanBeResetAtTurnBoundary()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);
        player.Actions = 2;
        int firstBridge = AddCard(state, player, "intrigue:pont", CardZone.Hand);
        int secondBridge = AddCard(state, player, "intrigue:pont", CardZone.Hand);

        Assert.That(Play(state, player, firstBridge).Status, Is.EqualTo(GameRuleStatus.Applied));
        Assert.That(Play(state, player, secondBridge).Status, Is.EqualTo(GameRuleStatus.Applied));

        Assert.That(player.CostReductionThisTurn, Is.EqualTo(2));
        Assert.That(CostRules.GetEffectiveCost(state, ResolveDefinition("base:cuivre")), Is.Zero);
        Assert.That(CostRules.GetEffectiveCost(state, ResolveDefinition("base:domaine")), Is.Zero);
        Assert.That(CostRules.GetEffectiveCost(state, ResolveDefinition("base:province")), Is.EqualTo(6));

        CostRules.ResetForTurn(player);

        Assert.That(player.CostReductionThisTurn, Is.Zero);
        Assert.That(CostRules.GetEffectiveCost(state, ResolveDefinition("base:province")), Is.EqualTo(8));
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
            MatchId = "bridge-tests",
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
