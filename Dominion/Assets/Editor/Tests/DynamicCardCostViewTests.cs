#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

public sealed class DynamicCardCostViewTests
{
    [SetUp]
    public void ReloadCatalog() => ExtensionCatalog.Reload();

    [Test]
    public void CostView_ShowsPrintedAndBridgeReducedCosts()
    {
        GameObject card = new GameObject("Card", typeof(RectTransform), typeof(Image));
        try
        {
            ExtensionCardData province = ExtensionCatalog.FindCard("base", "province");
            Assert.That(province, Is.Not.Null);

            DynamicCardCostView view = DynamicCardCostView.Attach(card, province);
            GameStateSnapshot state = NewState(0);
            view.RefreshCost(state);

            Assert.That(view.DisplayedCost, Is.EqualTo(8));
            Assert.That(view.CostText.text, Is.EqualTo("8"));
            Assert.That(view.CostText.raycastTarget, Is.False);

            state.Players[0].CostReductionThisTurn = 2;
            view.RefreshCost(state);

            Assert.That(view.DisplayedCost, Is.EqualTo(6));
            Assert.That(view.CostText.text, Is.EqualTo("6"));
        }
        finally
        {
            Object.DestroyImmediate(card);
        }
    }

    [Test]
    public void CostView_ClampsDisplayedCostAtZero()
    {
        GameObject card = new GameObject("Card", typeof(RectTransform), typeof(Image));
        try
        {
            DynamicCardCostView view = DynamicCardCostView.Attach(card, ExtensionCatalog.FindCard("base", "cuivre"));
            view.RefreshCost(NewState(4));

            Assert.That(view.DisplayedCost, Is.Zero);
            Assert.That(view.CostText.text, Is.EqualTo("0"));
        }
        finally
        {
            Object.DestroyImmediate(card);
        }
    }

    private static GameStateSnapshot NewState(int reduction)
    {
        GameStateSnapshot state = new GameStateSnapshot { ActivePlayerId = "player-1" };
        state.Players.Add(new PlayerStateSnapshot
        {
            PlayerId = "player-1",
            CostReductionThisTurn = reduction
        });
        return state;
    }
}
#endif
