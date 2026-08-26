#if UNITY_INCLUDE_TESTS
using NUnit.Framework;

public sealed class TrashZoneRulesTests
{
    [Test]
    public void TrashZone_IsMatchWideAndNotPlayerOwned()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);
        state.TrashedCards.Add(42);

        Assert.That(CardZoneRules.TryParseZone("trash", out CardZone zone), Is.True);
        Assert.That(zone, Is.EqualTo(CardZone.Trash));
        Assert.That(CardZoneRules.ResolveZone(player, CardZone.Trash), Is.Null);
        Assert.That(CardZoneRules.ResolveZone(state, player, CardZone.Trash), Is.SameAs(state.TrashedCards));
    }

    [Test]
    public void GainFromTrash_TransfersExistingInstanceWithoutCreatingAnotherCard()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot firstPlayer);
        PlayerStateSnapshot secondPlayer = NewPlayer("player-2");
        state.Players.Add(secondPlayer);

        CardInstance card = new CardInstance(1, "base:village", firstPlayer.PlayerId);
        state.CardInstances.Add(card);
        state.TrashedCards.Add(card.InstanceId);
        state.NextCardInstanceId = 2;

        bool gained = GainRules.TryGainFromTrash(
            state,
            secondPlayer,
            card.InstanceId,
            CardZone.Discard,
            0,
            null,
            out string error);

        Assert.That(gained, Is.True, error);
        Assert.That(state.TrashedCards, Is.Empty);
        CollectionAssert.AreEqual(new[] { card.InstanceId }, secondPlayer.Discard);
        Assert.That(card.OwnerPlayerId, Is.EqualTo(secondPlayer.PlayerId));
        Assert.That(state.CardInstances.Count, Is.EqualTo(1));
        Assert.That(state.NextCardInstanceId, Is.EqualTo(2));
        Assert.That(GameStateValidator.TryValidate(state, out string validationError), Is.True, validationError);
    }

    [Test]
    public void GainFromTrash_RejectsCardThatIsNotInTrashWithoutMutation()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);
        CardInstance card = new CardInstance(1, "base:village", player.PlayerId);
        state.CardInstances.Add(card);
        player.Hand.Add(card.InstanceId);
        state.NextCardInstanceId = 2;

        bool gained = GainRules.TryGainFromTrash(
            state,
            player,
            card.InstanceId,
            CardZone.Discard,
            0,
            null,
            out string error);

        Assert.That(gained, Is.False);
        StringAssert.Contains("not present in the trash", error);
        CollectionAssert.AreEqual(new[] { card.InstanceId }, player.Hand);
        Assert.That(player.Discard, Is.Empty);
        Assert.That(state.TrashedCards, Is.Empty);
        Assert.That(card.OwnerPlayerId, Is.EqualTo(player.PlayerId));
    }

    private static GameStateSnapshot NewState(out PlayerStateSnapshot player)
    {
        GameStateSnapshot state = new GameStateSnapshot
        {
            MatchId = "trash-zone-tests",
            Version = 1,
            AuthorityEpoch = 1,
            IsStarted = true,
            IsInitialised = true,
            ActivePlayerId = "player-1",
            TurnNumber = 1,
            Phase = GameRules.ActionPhase,
            NextCardInstanceId = 1
        };

        player = NewPlayer("player-1");
        state.Players.Add(player);
        return state;
    }

    private static PlayerStateSnapshot NewPlayer(string id)
    {
        return new PlayerStateSnapshot
        {
            PlayerId = id,
            ActorNumber = id == "player-1" ? 1 : 2,
            NickName = id,
            IsConnected = true,
            Actions = 1,
            Buys = 1,
            Coins = 0
        };
    }
}
#endif
