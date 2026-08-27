#if UNITY_INCLUDE_TESTS
using System;
using NUnit.Framework;

public sealed class FleauxSimpleCardRulesTests
{
    [SetUp]
    public void Reload() => ExtensionCatalog.Reload();

    [Test]
    public void Herboristerie_DrawsForEachMissingTonique()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);
        AddOwned(state, player, "base:cuivre", CardZone.Deck);
        AddOwned(state, player, "base:argent", CardZone.Deck);
        CardInstance herboristerie = AddOwned(state, player, "fleaux:herboristerie", CardZone.Hand);
        state.SpecialPiles.Add(new SpecialPileSnapshot("fleaux:toniques", "Toniques"));

        GameRuleResult result = GameRules.TryPlayCard(state, player.PlayerId, herboristerie.InstanceId, Resolve, new Random(1));

        Assert.That(result.Status, Is.EqualTo(GameRuleStatus.Applied), result.Error);
        Assert.That(player.Hand.Count, Is.EqualTo(2));
    }

    [Test]
    public void PaladinGain_TakesDivineBanner()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);
        state.SupplyPiles.Add(new SupplyPileSnapshot("fleaux:paladin", 10));
        CardInstance banner = new CardInstance(state.NextCardInstanceId++, "fleaux:etendard_divin", string.Empty);
        state.CardInstances.Add(banner);
        state.UnownedArtifacts.Add(banner.InstanceId);
        Assert.That(ResolutionQueue.TryBegin(state, player.PlayerId, out ResolutionQueue queue, out string beginError), Is.True, beginError);
        Assert.That(GainRules.TryGainFromSupply(state, player, "fleaux:paladin", CardZone.Discard,
            0, queue.Events, out _, out string gainError), Is.True, gainError);

        TriggerResolutionResult resolution = TriggerResolver.ResolvePending(queue, state, Resolve, new Random(1));

        Assert.That(resolution.Status, Is.EqualTo(EffectResolutionStatus.Applied), resolution.Error);
        Assert.That(player.Artifacts, Does.Contain(banner.InstanceId));
        Assert.That(banner.OwnerPlayerId, Is.EqualTo(player.PlayerId));
    }

    [Test]
    public void CabinetOfCuriosities_CountsDistinctTypesInPlay()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);
        state.Phase = GameRules.BuyPhase;
        AddOwned(state, player, "base:village", CardZone.InPlay);
        AddOwned(state, player, "base:domaine", CardZone.InPlay);
        CardInstance cabinet = AddOwned(state, player, "fleaux:cabinet_des_curiosites", CardZone.Hand);

        GameRuleResult result = GameRules.TryPlayCard(state, player.PlayerId, cabinet.InstanceId, Resolve, new Random(1));

        Assert.That(result.Status, Is.EqualTo(GameRuleStatus.Applied), result.Error);
        Assert.That(player.Buys, Is.EqualTo(2));
        Assert.That(player.Coins, Is.EqualTo(3));
    }

    [Test]
    public void Insomnie_AppliesNextCleanupPenalty()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);
        CardInstance insomnia = AddOwned(state, player, "fleaux:insomnie", CardZone.Hand);

        GameRuleResult result = GameRules.TryPlayCard(state, player.PlayerId, insomnia.InstanceId, Resolve, new Random(1));

        Assert.That(result.Status, Is.EqualTo(GameRuleStatus.Applied), result.Error);
        Assert.That(player.NextCleanupDrawModifier, Is.EqualTo(-1));
    }

    private static GameStateSnapshot NewState(out PlayerStateSnapshot player)
    {
        GameStateSnapshot state = new GameStateSnapshot
        {
            IsStarted = true,
            ActivePlayerId = "p1",
            Phase = GameRules.ActionPhase,
            TurnNumber = 1
        };
        player = new PlayerStateSnapshot { PlayerId = "p1", NickName = "P1", Actions = 1, Buys = 1 };
        state.Players.Add(player);
        return state;
    }

    private static CardInstance AddOwned(GameStateSnapshot state, PlayerStateSnapshot player, string definitionId, CardZone zone)
    {
        CardInstance instance = new CardInstance(state.NextCardInstanceId++, definitionId, player.PlayerId);
        state.CardInstances.Add(instance);
        CardZoneRules.ResolveZone(player, zone).Add(instance.InstanceId);
        return instance;
    }

    private static ExtensionCardData Resolve(string definitionId)
    {
        return RoomGameSetup.TryResolveCard(definitionId, out _, out ExtensionCardData card) ? card : null;
    }
}
#endif
