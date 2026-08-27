#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public sealed class PrefabUiContractTests
{
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
        Assert.NotNull(decisionCards?.GetComponent<GridLayoutGroup>());
        Assert.NotNull(decisionCards?.GetComponent<DecisionScrollGrid>());
        Assert.NotNull(decisionOptions?.GetComponent<GridLayoutGroup>());
        Assert.NotNull(decisionOptions?.GetComponent<DecisionScrollGrid>());
        Assert.NotNull(decision.transform.Find("ConfirmDecision")?.GetComponent<Button>());
        Assert.NotNull(Load("DecisionOption").transform.Find("Label")?.GetComponent<Text>());

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

    private static GameObject Load(string name)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/UI/" + name + ".prefab");
        Assert.NotNull(prefab, name + ".prefab is missing.");
        return prefab;
    }
}
#endif
