#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public sealed class PrefabUiContractTests
{
    [Test]
    public void JournalChatAndEmotes_ArePrefabAuthored()
    {
        GameObject gameScreen = Load("Board/GameScreen");
        Transform journal = gameScreen.transform.Find("JournalPanel");
        Transform social = journal?.Find("JournalSocialPanel");
        Assert.NotNull(social, "JournalSocialPanel must be instanced in GameScreen.prefab.");
        Assert.NotNull(social.GetComponent<JournalSocialPanel>());
        SerializedObject contract = new SerializedObject(social.GetComponent<JournalSocialPanel>());
        SerializedProperty icons = contract.FindProperty("_emoteIconPrefabs");
        Assert.AreEqual(4, icons.arraySize);
        for (int index = 0; index < icons.arraySize; index++)
        {
            GameObject icon = icons.GetArrayElementAtIndex(index).objectReferenceValue as GameObject;
            Assert.NotNull(icon, "Broken emote prefab reference at index " + index);
            Assert.NotNull(icon.GetComponent<CanvasGroup>());
            Assert.NotNull(icon.GetComponent<RectTransform>());
        }

        InputField input = social.Find("MessageInput")?.GetComponent<InputField>();
        Assert.NotNull(input);
        Assert.AreEqual(JournalRules.MaxChatLength, input.characterLimit);
        Assert.NotNull(social.Find("SendButton")?.GetComponent<Button>());
        Assert.NotNull(social.Find("EmoteButton")?.GetComponent<Button>());
        Transform wheel = social.Find("EmoteWheel");
        Assert.NotNull(wheel);
        Assert.AreEqual(4, wheel.GetComponentsInChildren<Button>(true).Length);
        Assert.NotNull(social.Find("EmoteAnimationRoot"));
        Assert.NotNull(social.GetComponent<AudioSource>());

        string[] iconNames = { "EmoteBlue", "EmoteGreen", "EmoteGold", "EmoteRose" };
        foreach (string iconName in iconNames)
        {
            GameObject icon = Load("Emotes/" + iconName);
            // Final artwork may replace the placeholder circle with an Image.
            Graphic[] graphics = icon.GetComponentsInChildren<Graphic>(true);
            Assert.IsNotEmpty(graphics, iconName + " needs a UI visual.");
            CanvasGroup group = icon.GetComponent<CanvasGroup>();
            Assert.NotNull(group);
            Assert.IsTrue(group.enabled);
            Assert.IsFalse(group.blocksRaycasts);

            // A parent CanvasGroup can reject raycasts even when an Image's
            // Raycast Target is checked. Test the effective filter on a live copy.
            GameObject instance = Object.Instantiate(icon);
            try
            {
                instance.SetActive(true);
                foreach (Graphic graphic in instance.GetComponentsInChildren<Graphic>(true))
                {
                    if (!graphic.isActiveAndEnabled || !graphic.raycastTarget) continue;
                    Vector2 point = RectTransformUtility.WorldToScreenPoint(null,
                        graphic.rectTransform.TransformPoint(graphic.rectTransform.rect.center));
                    Assert.IsFalse(graphic.Raycast(point, null),
                        iconName + "/" + graphic.name + " must not intercept clicks.");
                }
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }
    }

    [Test]
    public void RuntimeCard_IsTheSharedCompleteCardVisual()
    {
        GameObject prefab = Load("Cards/RuntimeCard");
        Assert.NotNull(prefab.GetComponent<Image>());
        Assert.NotNull(prefab.GetComponent<LayoutElement>());
        Assert.NotNull(prefab.GetComponent<CanvasGroup>());
        Assert.NotNull(prefab.GetComponent<CardPointerInteraction>());
        Assert.NotNull(prefab.GetComponent<DynamicCardCostView>());
        Assert.NotNull(prefab.GetComponent<RuntimeCardView>());
        RectTransform runtimeCost = prefab.transform.Find("DynamicCost") as RectTransform;
        Assert.NotNull(runtimeCost?.GetComponent<Text>());
        Assert.NotNull(prefab.transform.Find("RemainingCount/Text")?.GetComponent<Text>());

        RectTransform detachedCost = Load("Cards/CardCostOverlay").GetComponent<RectTransform>();
        Assert.NotNull(detachedCost?.GetComponent<Text>());
        Assert.AreEqual(runtimeCost.anchorMin, detachedCost.anchorMin);
        Assert.AreEqual(runtimeCost.anchorMax, detachedCost.anchorMax);
    }

    [Test]
    public void SupplyCard_IsASeparateSquareCroppedVisual()
    {
        GameObject prefab = Load("Cards/SupplyCard");
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
        GameObject gameScreen = Load("Board/GameScreen");
        Transform nestedTrash = gameScreen.transform.Find("TrashPileUi");
        Assert.NotNull(nestedTrash, "TrashPileUi must be instanced directly in GameScreen.prefab.");
        Assert.NotNull(nestedTrash.Find("TrashPileButton")?.GetComponent<Button>());

        GameObject trash = Load("Board/TrashPileUi");
        Assert.NotNull(trash.transform.Find("TrashPileButton")?.GetComponent<Button>());
        Transform cards = trash.transform.Find("TrashPileOverlay/Panel/CardsScroll/Viewport/Cards");
        Assert.NotNull(cards?.GetComponent<GridLayoutGroup>());
        Assert.NotNull(trash.transform.Find("TrashPileOverlay/Panel/CardsScroll")?.GetComponent<ScrollRect>());

        GameObject decision = Load("Choices/PendingDecisionPanel");
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
        Assert.NotNull(Load("Choices/DecisionOption").transform.Find("Label")?.GetComponent<Text>());

        GameObject instructionBar = Load("Choices/DecisionInstructionBar");
        RectTransform instructionRect = instructionBar.GetComponent<RectTransform>();
        Transform instructionPrompt = instructionBar.transform.Find("Prompt");
        Assert.NotNull(instructionPrompt?.GetComponent<Text>());
        Assert.NotNull(instructionPrompt?.GetComponent<DraggableDecisionPanel>());
        Assert.NotNull(instructionBar.transform.Find("Count")?.GetComponent<Text>());
        Assert.NotNull(instructionBar.transform.Find("ConfirmDecision")?.GetComponent<Button>());
        Assert.Less(instructionRect.anchorMax.y - instructionRect.anchorMin.y, 0.12f);

        GameObject cardDrawer = Load("Choices/DecisionCardDrawer");
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

        GameObject deckPosition = Load("Choices/DeckPositionDecision");
        Assert.NotNull(deckPosition.GetComponent<DeckPositionDecisionView>());
        Assert.NotNull(deckPosition.transform.Find("Track/Handle")?.GetComponent<Image>());
        Assert.NotNull(deckPosition.transform.Find("Value")?.GetComponent<Text>());

        GameObject cardName = Load("Choices/CardNameDecision");
        Assert.NotNull(cardName.GetComponent<CardNameDecisionView>());
        Assert.NotNull(cardName.transform.Find("SearchField")?.GetComponent<InputField>());
        Assert.NotNull(cardName.transform.Find("Suggestions")?.GetComponent<GridLayoutGroup>());
        Assert.AreEqual(1, cardName.transform.Find("Suggestions")?.GetComponent<GridLayoutGroup>()?.constraintCount);
    }

    [Test]
    public void PauseRevealAndEndGame_ArePrefabAuthored()
    {
        GameObject pause = Load("Board/GamePauseMenu");
        Assert.NotNull(pause.GetComponent<Canvas>());
        Assert.NotNull(pause.GetComponent<GamePauseMenu>());
        Assert.NotNull(pause.transform.Find("Backdrop/Window/ResumeButton")?.GetComponent<Button>());
        Assert.NotNull(pause.transform.Find("Backdrop/Window/CloseGameButton")?.GetComponent<Button>());

        GameObject lobby = Load("Lobby/LobbySetupScreen");
        GridLayoutGroup revealGrid = lobby.transform.Find("Reveal/RevealCards/Content")?.GetComponent<GridLayoutGroup>();
        Assert.NotNull(revealGrid);
        Assert.AreEqual(GridLayoutGroup.Constraint.FixedColumnCount, revealGrid.constraint);
        Assert.AreEqual(5, revealGrid.constraintCount);
        Assert.NotNull(lobby.transform.Find("HostSelection/CardsPanel/BackButton")?.GetComponent<Button>());

        GameObject revealControls = Load("Lobby/LobbyRevealControls");
        Assert.NotNull(revealControls.transform.Find("Players/Content")?.GetComponent<VerticalLayoutGroup>());
        Assert.NotNull(revealControls.transform.Find("ReadyButton")?.GetComponent<Button>());
        Assert.NotNull(revealControls.transform.Find("ResetButton")?.GetComponent<Button>());
        Assert.NotNull(revealControls.transform.Find("StartButton")?.GetComponent<Button>());
        GameObject readyRow = Load("Lobby/LobbyReadyPlayerRow");
        Assert.NotNull(readyRow.GetComponent<LayoutElement>());
        Assert.NotNull(readyRow.transform.Find("Name")?.GetComponent<Text>());
        Assert.NotNull(readyRow.transform.Find("Status")?.GetComponent<Text>());

        GameObject zoom = Load("Cards/CardZoomOverlay");
        AdaptiveCardZoomView adaptiveZoom = zoom.transform.Find("ZoomedCard")?.GetComponent<AdaptiveCardZoomView>();
        Assert.NotNull(adaptiveZoom);
        Assert.GreaterOrEqual(adaptiveZoom.MaximumSize.x / adaptiveZoom.MaximumSize.y, 1f);

        GameObject flow = Load("EndGame/EndGameFlow");
        Assert.NotNull(flow.GetComponent<Canvas>());
        Assert.NotNull(flow.transform.Find("EndGameSurface"));
        GameObject scoringStage = Load("EndGame/EndGameScoringStage");
        VerticalLayoutGroup scoreRows = scoringStage.transform.Find("Breakdown/ScoreRows")?.GetComponent<VerticalLayoutGroup>();
        Assert.NotNull(scoreRows);
        Assert.IsTrue(scoreRows.childControlHeight);

        GameObject rankingStage = Load("EndGame/EndGameRankingStage");
        VerticalLayoutGroup rankingRows = rankingStage.transform.Find("Ranking/RankingRows")?.GetComponent<VerticalLayoutGroup>();
        VerticalLayoutGroup detailRows = rankingStage.transform.Find("Detail/DetailRows")?.GetComponent<VerticalLayoutGroup>();
        Assert.NotNull(rankingRows);
        Assert.NotNull(detailRows);
        Assert.IsTrue(rankingRows.childControlHeight);
        Assert.IsTrue(detailRows.childControlHeight);
        GameObject scoreRow = Load("EndGame/EndGameScoreRow");
        LayoutElement scoreRowLayout = scoreRow.GetComponent<LayoutElement>();
        Assert.NotNull(scoreRowLayout);
        Assert.Greater(scoreRowLayout.preferredHeight, 0f);
        Assert.NotNull(scoreRow.transform.Find("Points/Value")?.GetComponent<Text>());
        Assert.NotNull(scoreRow.transform.Find("Points/Shield")?.GetComponent<Image>()?.sprite);
        GameObject rankingRow = Load("EndGame/EndGameRankingRow");
        LayoutElement rankingRowLayout = rankingRow.GetComponent<LayoutElement>();
        Assert.NotNull(rankingRowLayout);
        Assert.Greater(rankingRowLayout.preferredHeight, 0f);
        Assert.NotNull(rankingRow.transform.Find("Score/Value")?.GetComponent<Text>());
    }

    [Test]
    public void PlayerBoardControlsAndArtifacts_ArePrefabAuthored()
    {
        GameObject gameScreen = Load("Board/GameScreen");
        Transform topBarFollowToggle = gameScreen.transform.Find("TopBar/FollowActivePlayerToggle");
        Assert.NotNull(topBarFollowToggle?.GetComponent<Toggle>());
        Assert.NotNull(topBarFollowToggle?.Find("Box/Checkmark")?.GetComponent<Image>());
        Assert.NotNull(topBarFollowToggle?.Find("Label")?.GetComponent<Text>());

        // Runtime controllers locate these roots recursively by name.
        foreach (string rootName in new[] { "BaseSupply", "KingdomSupply" })
        {
            RectTransform root = null;
            foreach (RectTransform candidate in gameScreen.GetComponentsInChildren<RectTransform>(true))
            {
                if (candidate.name != rootName) continue;
                Assert.IsNull(root, "GameScreen contains duplicate " + rootName + " roots.");
                root = candidate;
            }
            Assert.NotNull(root, "GameScreen must contain a RectTransform named " + rootName + ".");
            LayoutGroup layout = root.GetComponent<LayoutGroup>();
            Assert.NotNull(layout, rootName + " needs a Grid, Horizontal or Vertical LayoutGroup.");
            Assert.IsTrue(layout.enabled, rootName + " layout must be enabled.");
            if (layout is GridLayoutGroup grid)
            {
                Assert.Greater(grid.cellSize.x, 0f);
                Assert.Greater(grid.cellSize.y, 0f);
            }
        }
        Assert.NotNull(gameScreen.GetComponent<ReserveExtrasController>());
        // Like BuyPhaseGameplayController.ResolveUi, find the board recursively
        // but require ArtifactStack to remain a direct child of InPlayPanel.
        RectTransform inPlayPanel = null;
        RectTransform artifactStack = null;
        foreach (RectTransform candidate in gameScreen.GetComponentsInChildren<RectTransform>(true))
        {
            if (candidate.name == "InPlayPanel")
            {
                Assert.IsNull(inPlayPanel, "GameScreen contains duplicate InPlayPanel roots.");
                inPlayPanel = candidate;
            }
            if (candidate.name == "ArtifactStack")
            {
                Assert.IsNull(artifactStack, "GameScreen contains duplicate ArtifactStack roots.");
                artifactStack = candidate;
            }
        }
        Assert.NotNull(inPlayPanel, "GameScreen must contain an InPlayPanel RectTransform.");
        Assert.NotNull(artifactStack, "The owned-Artifact stack must be authored in GameScreen.prefab.");
        Assert.AreEqual(inPlayPanel, artifactStack.parent,
            "ArtifactStack must be a direct child of InPlayPanel, as required by ResolveUi.");
        Assert.AreEqual(new Vector2(0f, 1f), artifactStack.anchorMin);
        Assert.AreEqual(new Vector2(0f, 1f), artifactStack.anchorMax);
        Assert.NotNull(gameScreen.transform.Find("CardZoomOverlay/Card")?.GetComponent<AdaptiveCardZoomView>());

        GameObject extras = Load("Board/ReserveExtrasUi");
        GridLayoutGroup specialPiles = extras.transform.Find("SpecialPiles")?.GetComponent<GridLayoutGroup>();
        GridLayoutGroup availableArtifacts = extras.transform.Find("AvailableArtifacts")?.GetComponent<GridLayoutGroup>();
        Assert.NotNull(specialPiles);
        Assert.NotNull(availableArtifacts);
        Assert.AreEqual(1, specialPiles.constraintCount);
        Assert.AreEqual(2, availableArtifacts.constraintCount);

        GameObject specialPile = Load("Board/SpecialPileTile");
        Assert.NotNull(specialPile.GetComponent<LayoutElement>());
        Assert.NotNull(specialPile.GetComponent<CardPointerInteraction>());
        Assert.NotNull(specialPile.transform.Find("Name")?.GetComponent<Text>());
        Assert.NotNull(specialPile.transform.Find("Count")?.GetComponent<Text>());

        GameObject playerTab = Load("Board/PlayerBoardTab");
        Assert.NotNull(playerTab.GetComponent<Button>());
        Assert.NotNull(playerTab.GetComponent<LayoutElement>());
        Assert.NotNull(playerTab.transform.Find("Label")?.GetComponent<Text>());
        Assert.NotNull(playerTab.transform.Find("PlayerColor")?.GetComponent<Image>());
        Assert.NotNull(playerTab.transform.Find("ActiveIndicator")?.GetComponent<Image>());
        Assert.NotNull(playerTab.transform.Find("ViewedIndicator")?.GetComponent<Image>());

        GameObject artifact = Load("Board/ArtifactTile");
        Assert.NotNull(artifact.GetComponent<Button>());
        Assert.NotNull(artifact.GetComponent<LayoutElement>());
        Assert.NotNull(artifact.GetComponent<CardPointerInteraction>());
        Assert.NotNull(artifact.transform.Find("Artwork")?.GetComponent<Image>());
        Assert.NotNull(artifact.transform.Find("Label")?.GetComponent<Text>());
        RectTransform artifactRect = artifact.GetComponent<RectTransform>();
        Assert.AreEqual(3f, artifactRect.sizeDelta.x / artifactRect.sizeDelta.y, 0.01f);
    }

    [Test]
    public void GameScreen_OwnsOneCardZoomAndNoDetachedInlineDuplicatesRemain()
    {
        GameObject gameScreen = Load("Board/GameScreen");
        Assert.AreEqual(1, gameScreen.GetComponents<GameScreenController>().Length);
        Assert.AreEqual(1, gameScreen.GetComponents<ReserveExtrasController>().Length);
        Assert.AreEqual(1, gameScreen.GetComponents<PublicJournalView>().Length);
        Assert.AreEqual(1, gameScreen.GetComponentsInChildren<AdaptiveCardZoomView>(true).Length);

        SerializedObject screen = new SerializedObject(gameScreen.GetComponent<GameScreenController>());
        Assert.IsNull(screen.FindProperty("_journalText"),
            "PublicJournalView must be the only component that owns journal rendering.");
        Assert.NotNull(screen.FindProperty("_zoomOverlay"));
        Assert.NotNull(screen.FindProperty("_zoomImage"));
        Assert.NotNull(screen.FindProperty("_otherPlayerDecisionPanel"));
        Assert.NotNull(screen.FindProperty("_otherPlayerDecisionText"));
        Transform waitingPanel = gameScreen.transform.Find("OtherPlayerDecisionPanel");
        Assert.NotNull(waitingPanel?.GetComponent<Image>());
        Assert.NotNull(waitingPanel?.Find("Message")?.GetComponent<Text>());

        Text journalText = gameScreen.transform.Find("JournalPanel/JournalText")?.GetComponent<Text>();
        Assert.NotNull(journalText);
        Assert.IsTrue(journalText.raycastTarget,
            "JournalText must receive pointer events for clickable card names.");
        SerializedObject journal = new SerializedObject(gameScreen.GetComponent<PublicJournalView>());
        Assert.AreEqual(journalText,
            journal.FindProperty("_journalText")?.objectReferenceValue);

        SerializedObject extras = new SerializedObject(gameScreen.GetComponent<ReserveExtrasController>());
        Assert.IsNull(extras.FindProperty("_zoomOverlay"));
        Assert.IsNull(extras.FindProperty("_zoomImage"));

        Assert.IsNull(AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Resources/UI/Board/FollowActivePlayerToggle.prefab"));
        Assert.IsNull(AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Resources/UI/Board/JournalEntry.prefab"));
        Assert.IsNull(AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Resources/UI/Lobby/KingdomRevealScreen.prefab"));
    }

    [Test]
    public void LobbyScrollSensitivity_IsAuthoredInThePrefab()
    {
        GameObject lobby = Load("Lobby/LobbySetupScreen");
        ScrollRect[] scrollRects = lobby.GetComponentsInChildren<ScrollRect>(true);
        Assert.Greater(scrollRects.Length, 0);
        foreach (ScrollRect scrollRect in scrollRects)
            Assert.AreEqual(45f, scrollRect.scrollSensitivity, 0.01f, scrollRect.name);
    }

    private static GameObject Load(string name)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/UI/" + name + ".prefab");
        Assert.NotNull(prefab, name + ".prefab is missing.");
        return prefab;
    }
}
#endif
