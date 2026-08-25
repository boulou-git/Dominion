#if UNITY_INCLUDE_TESTS
using NUnit.Framework;

public sealed class ResolutionFailureTests
{
    [Test]
    public void TriggerRejection_AbortsActiveResolutionState()
    {
        GameStateSnapshot state = new GameStateSnapshot
        {
            MatchId = "resolution-failure-test",
            Version = 1,
            AuthorityEpoch = 1,
            IsStarted = true,
            IsInitialised = true,
            ActivePlayerId = "player-1",
            TurnNumber = 1,
            Phase = GameRules.BuyPhase
        };
        state.Players.Add(new PlayerStateSnapshot { PlayerId = "player-1", Buys = 1, Coins = 3 });

        Assert.That(ResolutionQueue.TryBegin(state, "player-1", out ResolutionQueue queue, out string beginError), Is.True, beginError);
        queue.Events.Publish(GameEvent.CardPlayed("player-1", 999, "base:missing"));
        state.Resolution.SelectedInstanceIds.Add(42);
        state.Resolution.SelectedDefinitionIds.Add("base:missing");
        state.Resolution.AttackProtectedPlayerIds.Add("player-2");
        state.Resolution.LastSelectionCount = 1;
        state.Resolution.LastSelectedCardCost = 3;
        state.Resolution.LastMovedCardInstanceId = 42;

        TriggerResolutionResult result = TriggerResolver.ResolvePending(queue, state, _ => null, null);

        Assert.That(result.Status, Is.EqualTo(EffectResolutionStatus.Rejected));
        Assert.That(state.Resolution.IsActive, Is.False);
        Assert.That(state.Resolution.OwnerPlayerId, Is.Empty);
        Assert.That(state.Resolution.PendingEvents, Is.Empty);
        Assert.That(state.Resolution.PendingDecision.IsPending, Is.False);
        Assert.That(state.Resolution.SelectedInstanceIds, Is.Empty);
        Assert.That(state.Resolution.SelectedDefinitionIds, Is.Empty);
        Assert.That(state.Resolution.AttackProtectedPlayerIds, Is.Empty);
        Assert.That(state.Resolution.LastSelectionCount, Is.Zero);
        Assert.That(state.Resolution.LastSelectedCardCost, Is.EqualTo(-1));
        Assert.That(state.Resolution.LastMovedCardInstanceId, Is.Zero);
    }
}
#endif
