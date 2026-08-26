#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using System;
using System.Linq;

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

    [Test]
    public void TrashFromSupply_CreatesPhysicalInstanceAndDecrementsPile()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);
        state.SupplyPiles.Add(new SupplyPileSnapshot("base:village", 2));

        bool trashed = TrashRules.TryTrashFromSupply(
            state,
            player,
            "base:village",
            0,
            null,
            out int instanceId,
            out string error);

        Assert.That(trashed, Is.True, error);
        Assert.That(state.SupplyPiles.Single().RemainingCount, Is.EqualTo(1));
        CollectionAssert.AreEqual(new[] { instanceId }, state.TrashedCards);
        CardInstance instance = state.CardInstances.Single(card => card.InstanceId == instanceId);
        Assert.That(instance.DefinitionId, Is.EqualTo("base:village"));
        Assert.That(instance.OwnerPlayerId, Is.EqualTo(player.PlayerId));
        Assert.That(GameStateValidator.TryValidate(state, out string validationError), Is.True, validationError);
    }

    [Test]
    public void Rodeuse_CanGainActionFromPublicTrashThroughDeclarativeEffects()
    {
        ExtensionCatalog.Reload();
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);
        Assert.That(CardInstanceRules.TryCreateOwnedCard(state, player, "intrigue:rodeuse", CardZone.Hand,
            out int rodeuseId, out string rodeuseError), Is.True, rodeuseError);
        Assert.That(CardInstanceRules.TryCreateOwnedCard(state, player, "base:village", CardZone.Hand,
            out int villageId, out string villageError), Is.True, villageError);
        Assert.That(TrashRules.TryTrashFromHand(state, player, villageId, 0, null, out string trashError), Is.True, trashError);
        state.SupplyPiles.Add(new SupplyPileSnapshot("base:village", 10));

        GameRuleResult played = GameRules.TryPlayCard(state, player.PlayerId, rodeuseId, ResolveDefinition, new Random(1));

        Assert.That(played.Status, Is.EqualTo(GameRuleStatus.WaitingForChoice), played.Error);
        Assert.That(state.Resolution.PendingDecision.Zone, Is.EqualTo("supply"));
        string supplyDecisionId = state.Resolution.PendingDecision.DecisionId;

        GameRuleResult choseTrashGain = GameRules.TrySubmitSupplyDecision(
            state, player.PlayerId, supplyDecisionId, Array.Empty<string>(), ResolveDefinition, new Random(1));

        Assert.That(choseTrashGain.Status, Is.EqualTo(GameRuleStatus.WaitingForChoice), choseTrashGain.Error);
        Assert.That(state.Resolution.PendingDecision.Zone, Is.EqualTo("trash"));
        CollectionAssert.AreEqual(new[] { villageId }, state.Resolution.PendingDecision.CandidateInstanceIds);
        string trashDecisionId = state.Resolution.PendingDecision.DecisionId;

        GameRuleResult gained = GameRules.TrySubmitDecision(
            state, player.PlayerId, trashDecisionId, new[] { villageId }, ResolveDefinition, new Random(1));

        Assert.That(gained.Status, Is.EqualTo(GameRuleStatus.Applied), gained.Error);
        Assert.That(state.TrashedCards, Is.Empty);
        CollectionAssert.AreEqual(new[] { villageId }, player.Discard);
        Assert.That(state.CardInstances.Single(card => card.InstanceId == villageId).OwnerPlayerId, Is.EqualTo(player.PlayerId));
        Assert.That(player.Actions, Is.EqualTo(1));
        Assert.That(state.Resolution.IsActive, Is.False);
        Assert.That(GameStateValidator.TryValidate(state, out string validationError), Is.True, validationError);
    }

    [Test]
    public void Rodeuse_CanTrashActionFromSupplyThroughDeclarativeEffects()
    {
        ExtensionCatalog.Reload();
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);
        Assert.That(CardInstanceRules.TryCreateOwnedCard(state, player, "intrigue:rodeuse", CardZone.Hand,
            out int rodeuseId, out string rodeuseError), Is.True, rodeuseError);
        state.SupplyPiles.Add(new SupplyPileSnapshot("base:village", 2));

        GameRuleResult played = GameRules.TryPlayCard(state, player.PlayerId, rodeuseId, ResolveDefinition, new Random(1));

        Assert.That(played.Status, Is.EqualTo(GameRuleStatus.WaitingForChoice), played.Error);
        CollectionAssert.Contains(state.Resolution.PendingDecision.CandidateDefinitionIds, "base:village");
        string decisionId = state.Resolution.PendingDecision.DecisionId;

        GameRuleResult trashed = GameRules.TrySubmitSupplyDecision(
            state, player.PlayerId, decisionId, new[] { "base:village" }, ResolveDefinition, new Random(1));

        Assert.That(trashed.Status, Is.EqualTo(GameRuleStatus.Applied), trashed.Error);
        Assert.That(state.SupplyPiles.Single().RemainingCount, Is.EqualTo(1));
        Assert.That(state.TrashedCards.Count, Is.EqualTo(1));
        int trashedId = state.TrashedCards.Single();
        Assert.That(state.CardInstances.Single(card => card.InstanceId == trashedId).DefinitionId, Is.EqualTo("base:village"));
        Assert.That(player.Discard, Is.Empty);
        Assert.That(player.Actions, Is.EqualTo(1));
        Assert.That(state.Resolution.IsActive, Is.False);
        Assert.That(GameStateValidator.TryValidate(state, out string validationError), Is.True, validationError);
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
