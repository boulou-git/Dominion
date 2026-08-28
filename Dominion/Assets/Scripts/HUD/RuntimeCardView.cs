using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Prefab-backed visual for one complete card artwork. Layout belongs to RuntimeCard.prefab;
/// callers only bind data and add context-specific gameplay behaviour.
/// </summary>
[RequireComponent(typeof(RectTransform), typeof(Image))]
public sealed class RuntimeCardView : MonoBehaviour
{
    private const string PrefabResourcePath = "UI/RuntimeCard";
    private const string SupplyPrefabResourcePath = "UI/SupplyCard";
    private static GameObject _prefab;
    private static GameObject _supplyPrefab;

    [SerializeField] private Image _artwork;
    [SerializeField] private DynamicCardCostView _costView;
    [SerializeField] private CardPointerInteraction _pointer;
    [SerializeField] private Text _remainingCountText;

    public Image Artwork => _artwork;
    public CardPointerInteraction Pointer => _pointer;
    public Text RemainingCountText
    {
        get
        {
            if (_remainingCountText == null)
                _remainingCountText = transform.Find("RemainingCount/Text")?.GetComponent<Text>();
            return _remainingCountText;
        }
    }

    public static RuntimeCardView Create(Transform parent, string objectName,
        ExtensionCardData definition, Sprite artwork, bool raycastTarget)
    {
        return CreateFromPrefab(ref _prefab, PrefabResourcePath, parent, objectName,
            definition, artwork, raycastTarget);
    }

    public static RuntimeCardView CreateSupply(Transform parent, string objectName,
        ExtensionCardData definition, Sprite artwork, bool raycastTarget)
    {
        return CreateFromPrefab(ref _supplyPrefab, SupplyPrefabResourcePath, parent, objectName,
            definition, artwork, raycastTarget);
    }

    private static RuntimeCardView CreateFromPrefab(ref GameObject cachedPrefab, string resourcePath,
        Transform parent, string objectName, ExtensionCardData definition, Sprite artwork, bool raycastTarget)
    {
        if (cachedPrefab == null)
            cachedPrefab = Resources.Load<GameObject>(resourcePath);
        if (cachedPrefab == null || cachedPrefab.GetComponent<RuntimeCardView>() == null)
        {
            Debug.LogError("Missing Resources/" + resourcePath + " prefab.");
            return null;
        }

        RuntimeCardView view = Instantiate(cachedPrefab, parent, false).GetComponent<RuntimeCardView>();
        view.gameObject.name = objectName;
        view.Bind(definition, artwork, raycastTarget);
        return view;
    }

    public void Bind(ExtensionCardData definition, Sprite artwork, bool raycastTarget)
    {
        if (_artwork == null)
            _artwork = GetComponent<Image>();
        if (_costView == null)
            _costView = GetComponent<DynamicCardCostView>();
        if (_pointer == null)
            _pointer = GetComponent<CardPointerInteraction>();

        if (_artwork == null || _costView == null || _pointer == null)
        {
            Debug.LogError("RuntimeCard.prefab is missing one or more required references.", this);
            return;
        }

        _artwork.sprite = artwork;
        _artwork.color = artwork != null ? Color.white : new Color(0.55f, 0.12f, 0.12f, 1f);
        _artwork.raycastTarget = raycastTarget;
        _costView.Bind(definition);
        SetRemainingCount(null);
    }

    public void SetRemainingCount(int? count)
    {
        Text text = RemainingCountText;
        if (text == null)
        {
            Debug.LogError("RuntimeCard.prefab is missing RemainingCount/Text.", this);
            return;
        }

        text.transform.parent.gameObject.SetActive(count.HasValue);
        if (count.HasValue)
            text.text = Mathf.Max(0, count.Value).ToString();
    }
}
