using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Draws the current effective coin cost over the coin already present in a card artwork.
/// The displayed value follows the replicated game state, including turn-scoped modifiers.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public sealed class DynamicCardCostView : MonoBehaviour
{
    private const string CostTextName = "DynamicCost";

    private ExtensionCardData _definition;
    private Text _costText;
    private int _displayedCost = -1;

    public int DisplayedCost => _displayedCost;
    public Text CostText => _costText;

    public static DynamicCardCostView Attach(GameObject cardObject, ExtensionCardData definition)
    {
        if (cardObject == null)
            return null;

        DynamicCardCostView view = cardObject.GetComponent<DynamicCardCostView>();
        if (view == null)
            view = cardObject.AddComponent<DynamicCardCostView>();
        view.Bind(definition);
        return view;
    }

    public static DynamicCardCostView Attach(GameObject cardObject, string definitionId)
    {
        ExtensionPackageData extension;
        ExtensionCardData definition;
        if (!RoomGameSetup.TryResolveCard(definitionId, out extension, out definition))
            definition = null;
        return Attach(cardObject, definition);
    }

    private void Awake()
    {
        EnsureText();
    }

    private void OnEnable()
    {
        NetworkGameState.StateChanged -= RefreshCost;
        NetworkGameState.StateChanged += RefreshCost;
        RefreshCost(NetworkGameState.State);
    }

    private void OnDisable()
    {
        NetworkGameState.StateChanged -= RefreshCost;
    }

    private void OnDestroy()
    {
        NetworkGameState.StateChanged -= RefreshCost;
    }

    public void Bind(ExtensionCardData definition)
    {
        _definition = definition;
        RefreshCost(NetworkGameState.State);
    }

    public void RefreshCost(GameStateSnapshot state)
    {
        EnsureText();
        int cost = CostRules.GetEffectiveCost(state, _definition);
        _displayedCost = cost;
        _costText.gameObject.SetActive(cost >= 0);
        if (cost >= 0)
            _costText.text = cost.ToString();
        _costText.transform.SetAsLastSibling();
    }

    private void EnsureText()
    {
        if (_costText != null)
            return;

        Transform existing = transform.Find(CostTextName);
        if (existing != null)
            _costText = existing.GetComponent<Text>();

        if (_costText == null)
        {
            GameObject textObject = new GameObject(CostTextName, typeof(RectTransform), typeof(Text), typeof(Outline));
            textObject.transform.SetParent(transform, false);

            RectTransform rect = textObject.GetComponent<RectTransform>();
            // Matches the upper-left coin printed in the locked 59:91 card template.
            rect.anchorMin = new Vector2(0.035f, 0.815f);
            rect.anchorMax = new Vector2(0.285f, 0.978f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            _costText = textObject.GetComponent<Text>();
            _costText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _costText.alignment = TextAnchor.MiddleCenter;
            _costText.fontStyle = FontStyle.Bold;
            _costText.resizeTextForBestFit = true;
            _costText.resizeTextMinSize = 6;
            _costText.resizeTextMaxSize = 48;
            _costText.color = new Color(0.12f, 0.075f, 0.025f, 1f);
            _costText.raycastTarget = false;

            Outline outline = textObject.GetComponent<Outline>();
            outline.effectColor = new Color(1f, 0.86f, 0.48f, 0.58f);
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = false;
        }

        _costText.raycastTarget = false;
        _costText.transform.SetAsLastSibling();
    }
}
