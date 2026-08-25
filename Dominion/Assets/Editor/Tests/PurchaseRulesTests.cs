#if UNITY_INCLUDE_TESTS
using System;
using System.Linq;
using NUnit.Framework;

public sealed class PurchaseRulesTests
{
    [Test]
    public void BuyAvailableCard_AppliesPurchaseExactlyOnce()
    {
        const string definitionId = "base:argent";
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);
        state.Phase = GameRules.BuyPhase;
        player.Coins = 5;
        player.Buys = 1;
        state.SupplyPiles.Add(new SupplyPileSnapshot(definitionId, 3));

        ExtensionCardData definition = new ExtensionCardData
        {
            id = "argent",
            name = "Argent",
            cost = 3
        };

        GameRuleResult result = GameRules.TryBuyCard(
            state,
            player.PlayerId,
            definitionId,
            requestedId => string.Equals(requestedId, definitionId, StringComparison.OrdinalIgnoreCase) ? definition : null,
            new Random(1234));

        Assert.That(result.Status, Is.EqualTo(GameRuleStatus.Applied), result.Error);
        Assert.That(player.Coins, Is.EqualTo(2));
        Assert.That(player.Buys, Is.Zero);
        Assert.That(state.SupplyPiles.Single().RemainingCount, Is.EqualTo(2));
        Assert.That(player.Discard.Count, Is.EqualTo(1));
        Assert.That(state.CardInstances.Count, Is.EqualTo(1));
        Assert.That(state.CardInstances.Single().InstanceId, Is.EqualTo(player.Discard.Single()));
        Assert.That(state.CardInstances.Single().DefinitionId, Is.EqualTo(definitionId));
        Assert.That(state.NextCardInstanceId, Is.EqualTo(2));
        Assert.That(state.Phase, Is.EqualTo(GameRules.CleanupPhase));
        Assert.That(state.Resolution.IsActive, Is.False);
        Assert.That(GameStateValidator.TryValidate(state, out string validationError), Is.True, validationError);
    }

    private static GameStateSnapshot NewState(out PlayerStateSnapshot player)
    {
        GameStateSnapshot state = new GameStateSnapshot
        {
            MatchId = "purchase-rules-test",
            Version = 1,
            AuthorityEpoch = 1,
            IsStarted = true,
            IsInitialised = true,
            ActivePlayerId = "player-1",
            TurnNumber = 1,
            Phase = GameRules.BuyPhase,
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
