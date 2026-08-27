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
    private const string CostPrefabResourcePath = "UI/CardCostOverlay";

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
        if (_costText == null)
            return;
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
            GameObject prefab = Resources.Load<GameObject>(CostPrefabResourcePath);
            if (prefab == null || prefab.GetComponent<Text>() == null)
            {
                Debug.LogError("Missing Resources/UI/CardCostOverlay prefab.", this);
                return;
            }

            GameObject instance = Instantiate(prefab, transform, false);
            instance.name = CostTextName;
            _costText = instance.GetComponent<Text>();
        }

        _costText.raycastTarget = false;
        _costText.transform.SetAsLastSibling();
    }
}
