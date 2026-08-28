#if UNITY_INCLUDE_TESTS
using NUnit.Framework;

public sealed class PlayerBoardSelectionModelTests
{
    [Test]
    public void ManualInspectionPersistsAcrossSameTurnUpdatesWhenFollowIsEnabled()
    {
        GameStateSnapshot state = State("p1", out PlayerStateSnapshot first, out PlayerStateSnapshot second);
        PlayerBoardSelectionModel model = new PlayerBoardSelectionModel();

        Assert.That(model.Synchronise(state), Is.True);
        Assert.That(model.ViewedPlayerId, Is.EqualTo(first.PlayerId));
        Assert.That(model.SelectPlayer(state, second.PlayerId), Is.True);

        state.Version++;
        Assert.That(model.Synchronise(state), Is.False);
        Assert.That(model.ViewedPlayerId, Is.EqualTo(second.PlayerId));

        state.ActivePlayerId = second.PlayerId;
        state.TurnNumber++;
        model.Synchronise(state);
        Assert.That(model.ViewedPlayerId, Is.EqualTo(second.PlayerId));
    }

    [Test]
    public void FollowToggleControlsAutomaticTurnChanges()
    {
        GameStateSnapshot state = State("p1", out PlayerStateSnapshot first, out PlayerStateSnapshot second);
        PlayerBoardSelectionModel model = new PlayerBoardSelectionModel();
        model.Synchronise(state);
        model.SetFollowActivePlayer(state, false);

        state.ActivePlayerId = second.PlayerId;
        state.TurnNumber++;
        model.Synchronise(state);
        Assert.That(model.ViewedPlayerId, Is.EqualTo(first.PlayerId));

        Assert.That(model.SetFollowActivePlayer(state, true), Is.True);
        Assert.That(model.ViewedPlayerId, Is.EqualTo(second.PlayerId));
    }

    [Test]
    public void FollowEnabled_JumpsToTheNewActivePlayer()
    {
        GameStateSnapshot state = State("p1", out PlayerStateSnapshot first, out PlayerStateSnapshot second);
        PlayerBoardSelectionModel model = new PlayerBoardSelectionModel();
        model.Synchronise(state);
        model.SelectPlayer(state, second.PlayerId);

        state.ActivePlayerId = second.PlayerId;
        state.TurnNumber++;
        model.Synchronise(state);
        Assert.That(model.ViewedPlayerId, Is.EqualTo(second.PlayerId));

        state.ActivePlayerId = first.PlayerId;
        state.TurnNumber++;
        Assert.That(model.Synchronise(state), Is.True);
        Assert.That(model.ViewedPlayerId, Is.EqualTo(first.PlayerId));
    }

    private static GameStateSnapshot State(string activePlayerId,
        out PlayerStateSnapshot first, out PlayerStateSnapshot second)
    {
        GameStateSnapshot state = new GameStateSnapshot
        {
            IsStarted = true,
            ActivePlayerId = activePlayerId,
            TurnNumber = 1
        };
        first = new PlayerStateSnapshot { PlayerId = "p1", NickName = "Premier" };
        second = new PlayerStateSnapshot { PlayerId = "p2", NickName = "Second" };
        state.Players.Add(first);
        state.Players.Add(second);
        return state;
    }
}
#endif
