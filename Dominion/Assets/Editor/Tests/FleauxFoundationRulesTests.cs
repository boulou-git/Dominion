#if UNITY_INCLUDE_TESTS
using System;
using NUnit.Framework;

public sealed class FleauxFoundationRulesTests
{
    [SetUp]
    public void ReloadCatalogs()
    {
        ExtensionCatalog.Reload();
        ScoringRules.Reload();
    }

    [Test]
    public void FleauxCatalog_ContainsFinalRosterAndComponents()
    {
        ExtensionPackageData extension = ExtensionCatalog.Find("fleaux");
        Assert.That(extension, Is.Not.Null);
        Assert.That(extension.cards.Count, Is.EqualTo(27));
        Assert.That(extension.baseCards.Count, Is.EqualTo(7));
        Assert.That(extension.artifacts.Count, Is.EqualTo(7));
        Assert.That(extension.specialPiles.Count, Is.EqualTo(4));
        Assert.That(ExtensionCatalog.FindCard(extension, "confesseur").name, Is.EqualTo("Confesseur"));
        Assert.That(ExtensionCatalog.FindCard(extension, "rats").pileSize, Is.EqualTo(20));
        Assert.That(ExtensionCatalog.FindCard(extension, "rats").abilities[0].effects[3].excludedCardId,
            Is.EqualTo("fleaux:rats"));
    }

    [Test]
    public void SchemaOneSnapshot_MigratesNewCollections()
    {
        GameStateSnapshot state = new GameStateSnapshot { SchemaVersion = 1 };
        state.SpecialPiles = null;
        state.UnownedArtifacts = null;
        state.SetAsideCards = null;
        state.Players.Add(new PlayerStateSnapshot { PlayerId = "p1", Artifacts = null });

        Assert.That(GameStateSnapshotMigration.TryUpgradeToCurrent(state, out string error), Is.True, error);
        Assert.That(state.SchemaVersion, Is.EqualTo(GameStateSnapshot.CurrentSchemaVersion));
        Assert.That(state.SpecialPiles, Is.Not.Null);
        Assert.That(state.UnownedArtifacts, Is.Not.Null);
        Assert.That(state.SetAsideCards, Is.Not.Null);
        Assert.That(state.Players[0].Artifacts, Is.Not.Null);
        Assert.That(state.Players[0].ResolvedDurationCards, Is.Not.Null);
    }

    [Test]
    public void DiseaseGain_MovesPhysicalCardAndPublishesSemanticEvent()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);
        CardInstance disease = new CardInstance(state.NextCardInstanceId++, "fleaux:fievre", string.Empty);
        state.CardInstances.Add(disease);
        state.SpecialPiles.Add(new SpecialPileSnapshot("fleaux:maladies", "Maladies"));
        state.SpecialPiles[0].CardInstanceIds.Add(disease.InstanceId);
        Assert.That(ResolutionQueue.TryBegin(state, player.PlayerId, out ResolutionQueue queue, out string beginError), Is.True, beginError);

        bool gained = SpecialPileRules.TryGainTop(state, player, "fleaux:maladies", CardZone.Discard,
            0, queue.Events, Resolve, out int gainedId, out string error);

        Assert.That(gained, Is.True, error);
        Assert.That(gainedId, Is.EqualTo(disease.InstanceId));
        Assert.That(player.Discard.Contains(disease.InstanceId), Is.True);
        Assert.That(disease.OwnerPlayerId, Is.EqualTo(player.PlayerId));
        Assert.That(player.CardsGainedThisTurn, Is.EqualTo(1));
        Assert.That(queue.Events.SnapshotHistory().Exists(e => e.Type == GameEventType.DiseaseGained), Is.True);
    }

    [Test]
    public void TakingArtifact_TransfersUniqueInstance()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot first);
        PlayerStateSnapshot second = new PlayerStateSnapshot { PlayerId = "p2", NickName = "P2" };
        state.Players.Add(second);
        CardInstance artifact = new CardInstance(state.NextCardInstanceId++, "fleaux:dent_en_or", string.Empty);
        state.CardInstances.Add(artifact);
        state.UnownedArtifacts.Add(artifact.InstanceId);

        Assert.That(ArtifactRules.TryTake(state, first, artifact.DefinitionId, 0, null, out _, out string firstError), Is.True, firstError);
        Assert.That(ArtifactRules.TryTake(state, second, artifact.DefinitionId, 0, null, out _, out string secondError), Is.True, secondError);

        Assert.That(first.Artifacts.Contains(artifact.InstanceId), Is.False);
        Assert.That(second.Artifacts.Contains(artifact.InstanceId), Is.True);
        Assert.That(artifact.OwnerPlayerId, Is.EqualTo(second.PlayerId));
    }

    [Test]
    public void Cemetery_ScoresFromGlobalTrashInGroupsOfSix()
    {
        GameStateSnapshot state = NewState(out PlayerStateSnapshot player);
        CardInstance cemetery = AddOwned(state, player, "fleaux:cimetiere", CardZone.Deck);
        for (int index = 0; index < 12; index++)
        {
            CardInstance trashed = new CardInstance(state.NextCardInstanceId++, "base:cuivre", player.PlayerId);
            state.CardInstances.Add(trashed);
            state.TrashedCards.Add(trashed.InstanceId);
        }

        PlayerScoreResult score = ScoringRules.CalculatePlayerScore(state, player);
        CardScoreBreakdown row = new System.Collections.Generic.List<CardScoreBreakdown>(score.Breakdown)
            .Find(candidate => candidate.DefinitionId == cemetery.DefinitionId);
        Assert.That(row, Is.Not.Null);
        Assert.That(row.TotalPoints, Is.EqualTo(2));
    }

    private static GameStateSnapshot NewState(out PlayerStateSnapshot player)
    {
        GameStateSnapshot state = new GameStateSnapshot { ActivePlayerId = "p1", IsStarted = true };
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
