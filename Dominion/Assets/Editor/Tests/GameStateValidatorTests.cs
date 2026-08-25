#if UNITY_INCLUDE_TESTS
using NUnit.Framework;

public sealed class GameStateValidatorTests
{
    [Test]
    public void ValidSnapshot_PassesValidation()
    {
        GameStateSnapshot state = CreateValidState();

        bool valid = GameStateValidator.TryValidate(state, out string error);

        Assert.That(valid, Is.True, error);
        Assert.That(error, Is.Empty);
    }

    [Test]
    public void CardInTwoZones_IsRejected()
    {
        GameStateSnapshot state = CreateValidState();
        state.Players[0].Hand.Add(1);

        bool valid = GameStateValidator.TryValidate(state, out string error);

        Assert.That(valid, Is.False);
        StringAssert.Contains("appears in both", error);
    }

    [Test]
    public void LegacyUnversionedSnapshot_UpgradesToCurrentSchema()
    {
        GameStateSnapshot state = CreateValidState();
        state.SchemaVersion = 0;

        bool upgraded = GameStateSnapshotMigration.TryUpgradeToCurrent(state, out string error);

        Assert.That(upgraded, Is.True, error);
        Assert.That(state.SchemaVersion, Is.EqualTo(GameStateSnapshot.CurrentSchemaVersion));
        Assert.That(GameStateValidator.TryValidate(state, out error), Is.True, error);
    }

    [Test]
    public void FutureSchemaSnapshot_IsRejected()
    {
        GameStateSnapshot state = CreateValidState();
        state.SchemaVersion = GameStateSnapshot.CurrentSchemaVersion + 1;

        bool upgraded = GameStateSnapshotMigration.TryUpgradeToCurrent(state, out string error);

        Assert.That(upgraded, Is.False);
        StringAssert.Contains("newer than supported", error);
    }

    private static GameStateSnapshot CreateValidState()
    {
        GameStateSnapshot state = new GameStateSnapshot
        {
            MatchId = "test-match",
            Version = 1,
            AuthorityEpoch = 1,
            IsStarted = true,
            IsInitialised = true,
            ActivePlayerId = "player-1",
            TurnNumber = 1,
            Phase = "Action",
            NextCardInstanceId = 2
        };

        PlayerStateSnapshot player = new PlayerStateSnapshot
        {
            PlayerId = "player-1",
            ActorNumber = 1,
            NickName = "Player 1",
            IsConnected = true,
            Actions = 1,
            Buys = 1,
            Coins = 0
        };
        player.Deck.Add(1);
        state.Players.Add(player);
        state.CardInstances.Add(new CardInstance(1, "base:cuivre", player.PlayerId));
        return state;
    }
}
#endif
