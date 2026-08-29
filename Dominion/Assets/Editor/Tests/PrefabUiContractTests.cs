#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public sealed class PrefabUiContractTests
{
    [Test]
    public void JournalChatAndEntries_ArePrefabAuthored()
    {
        GameObject surface = Load("JournalSurface");
        Transform scroll = surface.transform.Find("EntriesScroll");
        Transform viewport = scroll?.Find("Viewport");
        Transform content = viewport?.Find("Content");
        Assert.NotNull(scroll?.GetComponent<ScrollRect>());
        Assert.NotNull(viewport?.GetComponent<Mask>());
        Assert.NotNull(content?.GetComponent<VerticalLayoutGroup>());
        Assert.NotNull(content?.GetComponent<ContentSizeFitter>());
        InputField input = surface.transform.Find("Composer/MessageInput")?.GetComponent<InputField>();
        Assert.NotNull(input);
        Assert.AreEqual(JournalRules.MaxChatLength, input.characterLimit);
        Assert.NotNull(surface.transform.Find("Composer/SendButton")?.GetComponent<Button>());

        GameObject entry = Load("JournalEntry");
        Assert.NotNull(entry.GetComponent<Text>());
        Assert.NotNull(entry.GetComponent<Button>());
        Assert.NotNull(entry.GetComponent<ContentSizeFitter>());
    }

    [Test]
    public void RuntimeCard_IsTheSharedCompleteCardVisual()
    {
        GameObject prefab = Load("RuntimeCard");
        Assert.NotNull(prefab.GetComponent<Image>());
        Assert.NotNull(prefab.GetComponent<LayoutElement>());
        Assert.NotNull(prefab.GetComponent<CanvasGroup>());
        Assert.NotNull(prefab.GetComponent<CardPointerInteraction>());
        Assert.NotNull(prefab.GetComponent<DynamicCardCostView>());
        Assert.NotNull(prefab.GetComponent<RuntimeCardView>());
        RectTransform runtimeCost = prefab.transform.Find("DynamicCost") as RectTransform;
        Assert.NotNull(runtimeCost?.GetComponent<Text>());
        Assert.NotNull(prefab.transform.Find("RemainingCount/Text")?.GetComponent<Text>());

        RectTransform detachedCost = Load("CardCostOverlay").GetComponent<RectTransform>();
        Assert.NotNull(detachedCost?.GetComponent<Text>());
        Assert.AreEqual(runtimeCost.anchorMin, detachedCost.anchorMin);
        Assert.AreEqual(runtimeCost.anchorMax, detachedCost.anchorMax);
    }

    [Test]
    public void SupplyCard_IsASeparateSquareCroppedVisual()
    {
        GameObject prefab = Load("SupplyCard");
        Assert.NotNull(prefab.transform.Find("CropViewport")?.GetComponent<Mask>());
        Assert.NotNull(prefab.GetComponent<RuntimeCardView>());
        Assert.NotNull(prefab.GetComponent<CardPointerInteraction>());
        Assert.NotNull(prefab.GetComponent<DynamicCardCostView>());
        TopTwoThirdsCardCrop crop = prefab.GetComponent<TopTwoThirdsCardCrop>();
        Assert.NotNull(crop);
        Assert.AreEqual(1.5f, crop.FullArtworkHeightToWidth, 0.001f);
        RectTransform root = prefab.GetComponent<RectTransform>();
        Assert.AreEqual(root.sizeDelta.x, root.sizeDelta.y, 0.01f);
        RectTransform artwork = prefab.transform.Find("CropViewport/Artwork") as RectTransform;
        Assert.NotNull(artwork?.GetComponent<Image>());
        Assert.AreEqual(root.sizeDelta.x * 1.5f, artwork.sizeDelta.y, 0.01f);
        Assert.NotNull(prefab.transform.Find("DynamicCost")?.GetComponent<Text>());
        Assert.NotNull(prefab.transform.Find("RemainingCount/Text")?.GetComponent<Text>());
    }

    [Test]
    public void TrashAndDecisionPrefabs_ExposeTheirRuntimeContracts()
    {
        GameObject trash = Load("TrashPileUi");
        Assert.NotNull(trash.transform.Find("TrashPileButton")?.GetComponent<Button>());
        Transform cards = trash.transform.Find("TrashPileOverlay/Panel/CardsScroll/Viewport/Cards");
        Assert.NotNull(cards?.GetComponent<GridLayoutGroup>());
        Assert.NotNull(trash.transform.Find("TrashPileOverlay/Panel/CardsScroll")?.GetComponent<ScrollRect>());

        GameObject decision = Load("PendingDecisionPanel");
        Transform prompt = decision.transform.Find("Prompt");
        Assert.NotNull(prompt?.GetComponent<Text>());
        Assert.NotNull(prompt?.GetComponent<DraggableDecisionPanel>());
        Transform decisionCards = decision.transform.Find("DecisionCards");
        Transform decisionOptions = decision.transform.Find("DecisionOptions");
        Transform optionPreviewCards = decision.transform.Find("OptionPreviewCards");
        Transform optionPreviewOptions = decision.transform.Find("OptionPreviewOptions");
        Assert.NotNull(decisionCards?.GetComponent<GridLayoutGroup>());
        Assert.NotNull(decisionCards?.GetComponent<DecisionScrollGrid>());
        Assert.NotNull(decisionOptions?.GetComponent<GridLayoutGroup>());
        Assert.NotNull(decisionOptions?.GetComponent<DecisionScrollGrid>());
        Assert.NotNull(optionPreviewCards?.GetComponent<GridLayoutGroup>());
        Assert.NotNull(optionPreviewCards?.GetComponent<DecisionScrollGrid>());
        Assert.NotNull(optionPreviewOptions?.GetComponent<GridLayoutGroup>());
        Assert.NotNull(optionPreviewOptions?.GetComponent<DecisionScrollGrid>());
        Assert.NotNull(decision.transform.Find("ConfirmDecision")?.GetComponent<Button>());
        Assert.NotNull(Load("DecisionOption").transform.Find("Label")?.GetComponent<Text>());

        GameObject instructionBar = Load("DecisionInstructionBar");
        RectTransform instructionRect = instructionBar.GetComponent<RectTransform>();
        Transform instructionPrompt = instructionBar.transform.Find("Prompt");
        Assert.NotNull(instructionPrompt?.GetComponent<Text>());
        Assert.NotNull(instructionPrompt?.GetComponent<DraggableDecisionPanel>());
        Assert.NotNull(instructionBar.transform.Find("Count")?.GetComponent<Text>());
        Assert.NotNull(instructionBar.transform.Find("ConfirmDecision")?.GetComponent<Button>());
        Assert.Less(instructionRect.anchorMax.y - instructionRect.anchorMin.y, 0.12f);

        GameObject cardDrawer = Load("DecisionCardDrawer");
        Assert.IsNull(cardDrawer.GetComponent<Graphic>(), "The full-screen drawer root must not block board clicks.");
        Transform expandedDrawer = cardDrawer.transform.Find("Expanded");
        Transform drawerCards = expandedDrawer?.Find("DecisionCards");
        Assert.NotNull(expandedDrawer?.Find("Prompt")?.GetComponent<Text>());
        Assert.NotNull(expandedDrawer?.Find("Count")?.GetComponent<Text>());
        Assert.NotNull(expandedDrawer?.Find("ConfirmDecision")?.GetComponent<Button>());
        Assert.NotNull(expandedDrawer?.Find("CollapseButton")?.GetComponent<Button>());
        Assert.NotNull(drawerCards?.GetComponent<GridLayoutGroup>());
        Assert.NotNull(drawerCards?.GetComponent<DecisionScrollGrid>());
        Assert.NotNull(cardDrawer.transform.Find("CollapsedTab")?.GetComponent<Button>());

        GameObject deckPosition = Load("DeckPositionDecision");
        Assert.NotNull(deckPosition.GetComponent<DeckPositionDecisionView>());
        Assert.NotNull(deckPosition.transform.Find("Track/Handle")?.GetComponent<Image>());
        Assert.NotNull(deckPosition.transform.Find("Value")?.GetComponent<Text>());

        GameObject cardName = Load("CardNameDecision");
        Assert.NotNull(cardName.GetComponent<CardNameDecisionView>());
        Assert.NotNull(cardName.transform.Find("SearchField")?.GetComponent<InputField>());
        Assert.NotNull(cardName.transform.Find("Suggestions")?.GetComponent<GridLayoutGroup>());
        Assert.AreEqual(1, cardName.transform.Find("Suggestions")?.GetComponent<GridLayoutGroup>()?.constraintCount);
    }

    [Test]
    public void PauseRevealAndEndGame_ArePrefabAuthored()
    {
        GameObject pause = Load("GamePauseMenu");
        Assert.NotNull(pause.GetComponent<Canvas>());
        Assert.NotNull(pause.GetComponent<GamePauseMenu>());
        Assert.NotNull(pause.transform.Find("Backdrop/Window/ResumeButton")?.GetComponent<Button>());
        Assert.NotNull(pause.transform.Find("Backdrop/Window/CloseGameButton")?.GetComponent<Button>());

        GameObject lobby = Load("LobbySetupScreen");
        GridLayoutGroup revealGrid = lobby.transform.Find("Reveal/RevealCards/Content")?.GetComponent<GridLayoutGroup>();
        Assert.NotNull(revealGrid);
        Assert.AreEqual(GridLayoutGroup.Constraint.FixedColumnCount, revealGrid.constraint);
        Assert.AreEqual(5, revealGrid.constraintCount);
        Assert.NotNull(lobby.transform.Find("HostSelection/CardsPanel/BackButton")?.GetComponent<Button>());

        GameObject revealControls = Load("LobbyRevealControls");
        Assert.NotNull(revealControls.transform.Find("Players/Content")?.GetComponent<VerticalLayoutGroup>());
        Assert.NotNull(revealControls.transform.Find("ReadyButton")?.GetComponent<Button>());
        Assert.NotNull(revealControls.transform.Find("ResetButton")?.GetComponent<Button>());
        Assert.NotNull(revealControls.transform.Find("StartButton")?.GetComponent<Button>());
        GameObject readyRow = Load("LobbyReadyPlayerRow");
        Assert.NotNull(readyRow.GetComponent<LayoutElement>());
        Assert.NotNull(readyRow.transform.Find("Name")?.GetComponent<Text>());
        Assert.NotNull(readyRow.transform.Find("Status")?.GetComponent<Text>());

        GameObject zoom = Load("CardZoomOverlay");
        AdaptiveCardZoomView adaptiveZoom = zoom.transform.Find("ZoomedCard")?.GetComponent<AdaptiveCardZoomView>();
        Assert.NotNull(adaptiveZoom);
        Assert.GreaterOrEqual(adaptiveZoom.MaximumSize.x / adaptiveZoom.MaximumSize.y, 1f);

        GameObject flow = Load("EndGameFlow");
        Assert.NotNull(flow.GetComponent<Canvas>());
        Assert.NotNull(flow.transform.Find("EndGameSurface"));
        GameObject scoringStage = Load("EndGameScoringStage");
        VerticalLayoutGroup scoreRows = scoringStage.transform.Find("Breakdown/ScoreRows")?.GetComponent<VerticalLayoutGroup>();
        Assert.NotNull(scoreRows);
        Assert.IsTrue(scoreRows.childControlHeight);

        GameObject rankingStage = Load("EndGameRankingStage");
        VerticalLayoutGroup rankingRows = rankingStage.transform.Find("Ranking/RankingRows")?.GetComponent<VerticalLayoutGroup>();
        VerticalLayoutGroup detailRows = rankingStage.transform.Find("Detail/DetailRows")?.GetComponent<VerticalLayoutGroup>();
        Assert.NotNull(rankingRows);
        Assert.NotNull(detailRows);
        Assert.IsTrue(rankingRows.childControlHeight);
        Assert.IsTrue(detailRows.childControlHeight);
        GameObject scoreRow = Load("EndGameScoreRow");
        LayoutElement scoreRowLayout = scoreRow.GetComponent<LayoutElement>();
        Assert.NotNull(scoreRowLayout);
        Assert.Greater(scoreRowLayout.preferredHeight, 0f);
        Assert.NotNull(scoreRow.transform.Find("Points/Value")?.GetComponent<Text>());
        Assert.NotNull(scoreRow.transform.Find("Points/Shield")?.GetComponent<Image>()?.sprite);
        GameObject rankingRow = Load("EndGameRankingRow");
        LayoutElement rankingRowLayout = rankingRow.GetComponent<LayoutElement>();
        Assert.NotNull(rankingRowLayout);
        Assert.Greater(rankingRowLayout.preferredHeight, 0f);
        Assert.NotNull(rankingRow.transform.Find("Score/Value")?.GetComponent<Text>());
    }

    [Test]
    public void PlayerBoardControlsAndArtifacts_ArePrefabAuthored()
    {
        GameObject gameScreen = Load("GameScreen");
        Transform topBarFollowToggle = gameScreen.transform.Find("TopBar/FollowActivePlayerToggle");
        Assert.NotNull(topBarFollowToggle?.GetComponent<Toggle>());
        Assert.NotNull(topBarFollowToggle?.Find("Box/Checkmark")?.GetComponent<Image>());
        Assert.NotNull(topBarFollowToggle?.Find("Label")?.GetComponent<Text>());

        GridLayoutGroup kingdomGrid = gameScreen.transform
            .Find("SupplyPanel/KingdomSupply")?.GetComponent<GridLayoutGroup>();
        GridLayoutGroup baseGrid = gameScreen.transform
            .Find("SupplyPanel/BaseSupply")?.GetComponent<GridLayoutGroup>();
        Assert.NotNull(baseGrid);
        Assert.NotNull(kingdomGrid);
        Assert.AreEqual(kingdomGrid.cellSize.x, kingdomGrid.cellSize.y, 0.01f);
        Assert.Less(baseGrid.cellSize.x, kingdomGrid.cellSize.x);
        Assert.Less(baseGrid.cellSize.y, kingdomGrid.cellSize.y);
        Assert.NotNull(gameScreen.GetComponent<ReserveExtrasController>());
        Assert.NotNull(gameScreen.transform.Find("CardZoomOverlay/Card")?.GetComponent<AdaptiveCardZoomView>());

        GameObject extras = Load("ReserveExtrasUi");
        GridLayoutGroup specialPiles = extras.transform.Find("SpecialPiles")?.GetComponent<GridLayoutGroup>();
        GridLayoutGroup availableArtifacts = extras.transform.Find("AvailableArtifacts")?.GetComponent<GridLayoutGroup>();
        Assert.NotNull(specialPiles);
        Assert.NotNull(availableArtifacts);
        Assert.AreEqual(1, specialPiles.constraintCount);
        Assert.AreEqual(2, availableArtifacts.constraintCount);

        GameObject specialPile = Load("SpecialPileTile");
        Assert.NotNull(specialPile.GetComponent<LayoutElement>());
        Assert.NotNull(specialPile.GetComponent<CardPointerInteraction>());
        Assert.NotNull(specialPile.transform.Find("Name")?.GetComponent<Text>());
        Assert.NotNull(specialPile.transform.Find("Count")?.GetComponent<Text>());

        GameObject playerTab = Load("PlayerBoardTab");
        Assert.NotNull(playerTab.GetComponent<Button>());
        Assert.NotNull(playerTab.GetComponent<LayoutElement>());
        Assert.NotNull(playerTab.transform.Find("Label")?.GetComponent<Text>());
        Assert.NotNull(playerTab.transform.Find("PlayerColor")?.GetComponent<Image>());
        Assert.NotNull(playerTab.transform.Find("ActiveIndicator")?.GetComponent<Image>());
        Assert.NotNull(playerTab.transform.Find("ViewedIndicator")?.GetComponent<Image>());

        GameObject followToggle = Load("FollowActivePlayerToggle");
        Assert.NotNull(followToggle.GetComponent<Toggle>());
        Assert.NotNull(followToggle.transform.Find("Box/Checkmark")?.GetComponent<Image>());
        Assert.NotNull(followToggle.transform.Find("Label")?.GetComponent<Text>());

        GameObject artifact = Load("ArtifactTile");
        Assert.NotNull(artifact.GetComponent<Button>());
        Assert.NotNull(artifact.GetComponent<LayoutElement>());
        Assert.NotNull(artifact.GetComponent<CardPointerInteraction>());
        Assert.NotNull(artifact.transform.Find("Artwork")?.GetComponent<Image>());
        Assert.NotNull(artifact.transform.Find("Label")?.GetComponent<Text>());
        RectTransform artifactRect = artifact.GetComponent<RectTransform>();
        Assert.AreEqual(3f, artifactRect.sizeDelta.x / artifactRect.sizeDelta.y, 0.01f);
    }

    private static GameObject Load(string name)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/UI/" + name + ".prefab");
        Assert.NotNull(prefab, name + ".prefab is missing.");
        return prefab;
    }
}
#endif
