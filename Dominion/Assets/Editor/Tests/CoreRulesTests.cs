#if UNITY_INCLUDE_TESTS
using System;
using System.Linq;
using NUnit.Framework;

public sealed class CoreRulesTests
{
    [Test]
    public void DrawCards_UsesLastDeckItemAsTopCard()
    {
        PlayerStateSnapshot player = NewPlayer();
        player.Deck.AddRange(new[] { 1, 2, 3 });

        bool drawn = CardZoneRules.DrawCards(player, 2, new Random(1), out string error);

        Assert.That(drawn, Is.True, error);
        CollectionAssert.AreEqual(new[] { 1 }, player.Deck);
        CollectionAssert.AreEqual(new[] { 3, 2 }, player.Hand);
    }

    [Test]
    public void DrawCards_ReshufflesDiscardWhenDeckIsEmpty()
    {
        PlayerStateSnapshot player = NewPlayer();
        player.Discard.AddRange(new[] { 1, 2, 3 });

        bool drawn = CardZoneRules.DrawCards(player, 2, new Random(1234), out string error);

        Assert.That(drawn, Is.True, error);
        Assert.That(player.Hand.Count, Is.EqualTo(2));
        Assert.That(player.Deck.Count, Is.EqualTo(1));
        Assert.That(player.Discard, Is.Empty);
        CollectionAssert.AreEquivalent(new[] { 1, 2, 3 }, player.Hand.Concat(player.Deck).ToArray());
    }

    [Test]
    public void MoveTopCard_AndDrawUseIdenticalReshuffleSemantics()
    {
        PlayerStateSnapshot drawPlayer = NewPlayer();
        PlayerStateSnapshot inspectPlayer = NewPlayer();
        drawPlayer.Discard.AddRange(new[] { 1, 2, 3, 4 });
        inspectPlayer.Discard.AddRange(new[] { 1, 2, 3, 4 });

        bool drawn = CardZoneRules.DrawCards(drawPlayer, 1, new Random(9876), out string drawError);
        bool moved = CardZoneRules.TryMoveTopCardFromDeck(inspectPlayer, CardZone.Inspected, new Random(9876),
            out int movedInstanceId, out string moveError);

        Assert.That(drawn, Is.True, drawError);
        Assert.That(moved, Is.True, moveError);
        Assert.That(drawPlayer.Hand.Single(), Is.EqualTo(movedInstanceId));
        CollectionAssert.AreEqual(drawPlayer.Deck, inspectPlayer.Deck);
        Assert.That(drawPlayer.Discard, Is.Empty);
        Assert.That(inspectPlayer.Discard, Is.Empty);
        CollectionAssert.AreEqual(new[] { movedInstanceId }, inspectPlayer.Inspected);
    }

    [Test]
    public void MoveTopCard_WithNoCards_SucceedsWithoutInventingCard()
    {
        PlayerStateSnapshot player = NewPlayer();

        bool moved = CardZoneRules.TryMoveTopCardFromDeck(player, CardZone.Inspected, null,
            out int instanceId, out string error);

        Assert.That(moved, Is.True, error);
        Assert.That(instanceId, Is.Zero);
        Assert.That(player.Inspected, Is.Empty);
    }

    [Test]
    public void CreateOwnedCard_AllocatesStableUniqueInstanceIds()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);

        Assert.That(CardInstanceRules.TryCreateOwnedCard(state, player, "base:cuivre", CardZone.Hand,
            out int firstId, out string firstError), Is.True, firstError);
        Assert.That(CardInstanceRules.TryCreateOwnedCard(state, player, "base:argent", CardZone.Discard,
            out int secondId, out string secondError), Is.True, secondError);

        Assert.That(firstId, Is.EqualTo(1));
        Assert.That(secondId, Is.EqualTo(2));
        Assert.That(state.NextCardInstanceId, Is.EqualTo(3));
        Assert.That(state.CardInstances.Select(card => card.InstanceId), Is.Unique);
        Assert.That(GameStateValidator.TryValidate(state, out string validationError), Is.True, validationError);
    }

    [Test]
    public void GainFromSupply_CreatesOwnedCardAndDecrementsPile()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);
        state.SupplyPiles.Add(new SupplyPileSnapshot("base:argent", 2));

        bool gained = GainRules.TryGainFromSupply(state, player, "base:argent", CardZone.Discard,
            0, null, out int instanceId, out string error);

        Assert.That(gained, Is.True, error);
        Assert.That(instanceId, Is.EqualTo(1));
        Assert.That(state.SupplyPiles[0].RemainingCount, Is.EqualTo(1));
        CollectionAssert.AreEqual(new[] { 1 }, player.Discard);
        Assert.That(state.CardInstances.Single().DefinitionId, Is.EqualTo("base:argent"));
        Assert.That(GameStateValidator.TryValidate(state, out string validationError), Is.True, validationError);
    }

    [Test]
    public void GainFromEmptySupply_IsRejectedWithoutMutation()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);
        state.SupplyPiles.Add(new SupplyPileSnapshot("base:argent", 0));

        bool gained = GainRules.TryGainFromSupply(state, player, "base:argent", CardZone.Discard,
            0, null, out int instanceId, out string error);

        Assert.That(gained, Is.False);
        Assert.That(instanceId, Is.Zero);
        StringAssert.Contains("empty", error);
        Assert.That(state.NextCardInstanceId, Is.EqualTo(1));
        Assert.That(state.CardInstances, Is.Empty);
        Assert.That(player.Discard, Is.Empty);
        Assert.That(state.SupplyPiles[0].RemainingCount, Is.Zero);
    }

    [Test]
    public void TrashFromHand_MovesCardToTrashAndKeepsRegistryEntry()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);
        Assert.That(CardInstanceRules.TryCreateOwnedCard(state, player, "base:cuivre", CardZone.Hand,
            out int instanceId, out string createError), Is.True, createError);

        bool trashed = TrashRules.TryTrashFromHand(state, player, instanceId, 0, null, out string error);

        Assert.That(trashed, Is.True, error);
        Assert.That(player.Hand.Contains(instanceId), Is.False);
        Assert.That(state.TrashedCards.Contains(instanceId), Is.True);
        Assert.That(state.CardInstances.Any(card => card.InstanceId == instanceId), Is.True);
        Assert.That(GameStateValidator.TryValidate(state, out string validationError), Is.True, validationError);
    }

    [Test]
    public void TrashFromHand_WithRegistryOwnerMismatch_IsRejectedWithoutMutation()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);
        Assert.That(CardInstanceRules.TryCreateOwnedCard(state, player, "base:cuivre", CardZone.Hand,
            out int instanceId, out string createError), Is.True, createError);
        state.CardInstances.Single(card => card.InstanceId == instanceId).OwnerPlayerId = "another-player";

        bool trashed = TrashRules.TryTrashFromHand(state, player, instanceId, 0, null, out string error);

        Assert.That(trashed, Is.False);
        Assert.That(string.IsNullOrEmpty(error), Is.False);
        Assert.That(player.Hand.Contains(instanceId), Is.True);
        Assert.That(state.TrashedCards.Contains(instanceId), Is.False);
    }

    [Test]
    public void DiscardSelected_InvalidSelectionDoesNotPartiallyMutateZones()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);
        Assert.That(CardInstanceRules.TryCreateOwnedCard(state, player, "base:cuivre", CardZone.Hand,
            out int firstId, out string firstError), Is.True, firstError);
        Assert.That(CardInstanceRules.TryCreateOwnedCard(state, player, "base:argent", CardZone.Hand,
            out int secondId, out string secondError), Is.True, secondError);
        Assert.That(ResolutionQueue.TryBegin(state, player.PlayerId, out ResolutionQueue queue, out string queueError),
            Is.True, queueError);

        bool discarded = DiscardRules.TryDiscardSelectedFromHand(
            state,
            player,
            new[] { firstId, 999 },
            0,
            queue.Events,
            out string error);

        Assert.That(discarded, Is.False);
        Assert.That(string.IsNullOrEmpty(error), Is.False);
        Assert.That(player.Hand.Contains(firstId), Is.True);
        Assert.That(player.Hand.Contains(secondId), Is.True);
        Assert.That(player.Discard, Is.Empty);
    }

    [Test]
    public void ProvinceEmpty_FinalisesMatchAtTurnBoundary()
    {
        GameStateSnapshot state = NewState(out _);
        state.TurnNumber = 9;
        state.SupplyPiles.Add(new SupplyPileSnapshot(GameEndRules.ProvinceDefinitionId, 0));

        bool ended = GameEndRules.TryFinaliseAtTurnBoundary(state);

        Assert.That(ended, Is.True);
        Assert.That(state.IsGameOver, Is.True);
        Assert.That(state.IsStarted, Is.False);
        Assert.That(state.GameEndReason, Is.EqualTo(GameEndRules.ProvinceEmptyReason));
        Assert.That(state.EndedTurnNumber, Is.EqualTo(9));
        Assert.That(state.Phase, Is.EqualTo(GameEndRules.GameOverPhase));
    }

    [Test]
    public void ThreeEmptySupplyPiles_FinaliseMatch()
    {
        GameStateSnapshot state = NewState(out _);
        state.SupplyPiles.Add(new SupplyPileSnapshot(GameEndRules.ProvinceDefinitionId, 5));
        state.SupplyPiles.Add(new SupplyPileSnapshot("base:village", 0));
        state.SupplyPiles.Add(new SupplyPileSnapshot("base:forge", 0));
        state.SupplyPiles.Add(new SupplyPileSnapshot("base:marche", 0));

        bool ended = GameEndRules.TryFinaliseAtTurnBoundary(state);

        Assert.That(ended, Is.True);
        Assert.That(state.GameEndReason, Is.EqualTo(GameEndRules.ThreePilesEmptyReason));
    }

    [Test]
    public void TwoEmptySupplyPiles_DoNotEndMatch()
    {
        GameStateSnapshot state = NewState(out _);
        state.SupplyPiles.Add(new SupplyPileSnapshot(GameEndRules.ProvinceDefinitionId, 5));
        state.SupplyPiles.Add(new SupplyPileSnapshot("base:village", 0));
        state.SupplyPiles.Add(new SupplyPileSnapshot("base:forge", 0));

        bool ended = GameEndRules.TryFinaliseAtTurnBoundary(state);

        Assert.That(ended, Is.False);
        Assert.That(state.IsGameOver, Is.False);
        Assert.That(state.IsStarted, Is.True);
    }

    private static GameStateSnapshot NewState(out PlayerStateSnapshot player)
    {
        GameStateSnapshot state = new GameStateSnapshot
        {
            MatchId = "core-rules-test",
            Version = 1,
            AuthorityEpoch = 1,
            IsStarted = true,
            IsInitialised = true,
            ActivePlayerId = "player-1",
            TurnNumber = 1,
            Phase = GameRules.ActionPhase,
            NextCardInstanceId = 1
        };

        player = NewPlayer();
        state.Players.Add(player);
        return state;
    }

    private static PlayerStateSnapshot NewPlayer()
    {
        return new PlayerStateSnapshot
        {
            PlayerId = "player-1",
            ActorNumber = 1,
            NickName = "Player 1",
            IsConnected = true,
            Actions = 1,
            Buys = 1,
            Coins = 0
        };
    }
}
#endif
