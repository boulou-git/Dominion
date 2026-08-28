#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;

public sealed class PlayerColorAssignmentTests
{
    [Test]
    public void Assignment_IsStableAndIndependentFromSnapshotOrder()
    {
        GameStateSnapshot first = State("p3", "p1", "p4", "p2");
        GameStateSnapshot second = State("p2", "p4", "p1", "p3");

        foreach (string id in new[] { "p1", "p2", "p3", "p4" })
            Assert.That(PlayerColorAssignment.ResolvePaletteIndex(first, id, 8),
                Is.EqualTo(PlayerColorAssignment.ResolvePaletteIndex(second, id, 8)));
    }

    [Test]
    public void Assignment_UsesDistinctSlotsWhilePaletteHasRoom()
    {
        GameStateSnapshot state = State("p1", "p2", "p3", "p4");
        HashSet<int> slots = new HashSet<int>();

        foreach (PlayerStateSnapshot player in state.Players)
            slots.Add(PlayerColorAssignment.ResolvePaletteIndex(state, player.PlayerId, 8));

        Assert.That(slots.Count, Is.EqualTo(4));
    }

    [Test]
    public void MissingPalette_ReturnsNoColor()
    {
        Assert.That(PlayerColorAssignment.ResolvePaletteIndex(State("p1"), "p1", 0), Is.EqualTo(-1));
    }

    private static GameStateSnapshot State(params string[] playerIds)
    {
        GameStateSnapshot state = new GameStateSnapshot();
        foreach (string id in playerIds)
            state.Players.Add(new PlayerStateSnapshot { PlayerId = id, NickName = id });
        return state;
    }
}
#endif
