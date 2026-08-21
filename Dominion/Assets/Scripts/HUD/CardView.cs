using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Reusable visual representation of one Dominion card.
/// The source PNG/Sprite uses the locked 59:91 portrait ratio and already contains
/// artwork, card name and rules text. Unity only overlays the dynamic cost.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public sealed class CardView : MonoBehaviour
{
    public const float CardAspectRatio = 59f / 91f;

    private Image _cardImage;
    private Text _costText;
    private Image _costBackground;
    private AspectRatioFitter _aspectRatioFitter;
    private CardPointerInteraction _pointerInteraction;

    private CardDefinition _definition;
    private int _instanceId;
    private int _displayedCoinCost;

    public CardDefinition Definition => _definition;
    public int InstanceId => _instanceId;
    public int DisplayedCoinCost => _displayedCoinCost;

    public event Action<CardView> PrimaryActionRequested;
    public event Action<CardView> InspectRequested;

    /// <summary>
    /// Creates the complete CardView hierarchy in code so the prototype can use the
    /// component before a final prefab/style is locked.
    /// </summary>
    public static CardView Create(RectTransform parent, float preferredHeight = 180f)
    {
        GameObject root = new GameObject("CardView", typeof(RectTransform), typeof(LayoutElement), typeof(AspectRatioFitter), typeof(CardView));
        RectTransform rect = root.GetComponent<RectTransform>();
        rect.SetParent(parent, false);

        LayoutElement layout = root.GetComponent<LayoutElement>();
        layout.preferredHeight = preferredHeight;
        layout.minHeight = preferredHeight;
        layout.preferredWidth = preferredHeight * CardAspectRatio;
        layout.minWidth = layout.preferredWidth;

        return root.GetComponent<CardView>();
    }

    private void Awake()
    {
        EnsureVisualHierarchy();
    }

    public void Bind(CardDefinition definition, int instanceId, int currentCoinCost)
    {
        EnsureVisualHierarchy();

        _definition = definition;
        _instanceId = instanceId;

        gameObject.name = definition != null && !string.IsNullOrEmpty(definition.DisplayName)
            ? "CardView_" + definition.DisplayName
            : "CardView_" + instanceId;

        _cardImage.sprite = definition != null ? definition.CardSprite : null;
        _cardImage.color = _cardImage.sprite != null ? Color.white : new Color(0.18f, 0.18f, 0.18f, 1f);
        _cardImage.preserveAspect = true;

        SetCoinCost(currentCoinCost);
    }

    public void BindFallback(int instanceId, int currentCoinCost = 0)
    {
        Bind(null, instanceId, currentCoinCost);
    }

    public void SetCoinCost(int currentCoinCost)
    {
        EnsureVisualHierarchy();
        _displayedCoinCost = Mathf.Max(0, currentCoinCost);
        _costText.text = _displayedCoinCost.ToString();
    }

    private void EnsureVisualHierarchy()
    {
        if (_aspectRatioFitter == null)
        {
            _aspectRatioFitter = GetComponent<AspectRatioFitter>();
            if (_aspectRatioFitter == null)
                _aspectRatioFitter = gameObject.AddComponent<AspectRatioFitter>();

            _aspectRatioFitter.aspectMode = AspectRatioFitter.AspectMode.WidthControlsHeight;
            _aspectRatioFitter.aspectRatio = CardAspectRatio;
        }

        if (_cardImage == null)
        {
            Transform existing = transform.Find("CardImage");
            if (existing != null)
                _cardImage = existing.GetComponent<Image>();

            if (_cardImage == null)
            {
                GameObject imageObject = new GameObject("CardImage", typeof(RectTransform), typeof(Image));
                RectTransform imageRect = imageObject.GetComponent<RectTransform>();
                imageRect.SetParent(transform, false);
                Stretch(imageRect);
                _cardImage = imageObject.GetComponent<Image>();
                _cardImage.raycastTarget = false;
            }
        }

        if (_costBackground == null)
        {
            Transform existing = transform.Find("CostOverlay");
            if (existing != null)
                _costBackground = existing.GetComponent<Image>();

            if (_costBackground == null)
            {
                GameObject costObject = new GameObject("CostOverlay", typeof(RectTransform), typeof(Image));
                RectTransform costRect = costObject.GetComponent<RectTransform>();
                costRect.SetParent(transform, false);

                // Relative anchors keep the cost in the same physical place at every card size.
                costRect.anchorMin = new Vector2(0.035f, 0.815f);
                costRect.anchorMax = new Vector2(0.285f, 0.978f);
                costRect.offsetMin = Vector2.zero;
                costRect.offsetMax = Vector2.zero;

                _costBackground = costObject.GetComponent<Image>();
                _costBackground.color = new Color(0.82f, 0.66f, 0.23f, 0.96f);
                _costBackground.raycastTarget = false;
            }
        }

        if (_costText == null)
        {
            Transform existing = _costBackground.transform.Find("CostText");
            if (existing != null)
                _costText = existing.GetComponent<Text>();

            if (_costText == null)
            {
                GameObject textObject = new GameObject("CostText", typeof(RectTransform), typeof(Text));
                RectTransform textRect = textObject.GetComponent<RectTransform>();
                textRect.SetParent(_costBackground.transform, false);
                Stretch(textRect, 2f);

                _costText = textObject.GetComponent<Text>();
                _costText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                _costText.alignment = TextAnchor.MiddleCenter;
                _costText.fontStyle = FontStyle.Bold;
                _costText.resizeTextForBestFit = true;
                _costText.resizeTextMinSize = 10;
                _costText.resizeTextMaxSize = 42;
                _costText.color = new Color(0.16f, 0.10f, 0.03f, 1f);
                _costText.raycastTarget = false;
            }
        }

        if (_pointerInteraction == null)
        {
            _pointerInteraction = GetComponent<CardPointerInteraction>();
            if (_pointerInteraction == null)
                _pointerInteraction = gameObject.AddComponent<CardPointerInteraction>();

            _pointerInteraction.LongPressSeconds = 0.45f;
            _pointerInteraction.InspectRequested -= HandleInspectRequested;
            _pointerInteraction.InspectRequested += HandleInspectRequested;
        }

        // The root receives pointer events even when the source PNG has transparent rounded corners.
        Image hitTarget = GetComponent<Image>();
        if (hitTarget == null)
            hitTarget = gameObject.AddComponent<Image>();
        hitTarget.color = new Color(1f, 1f, 1f, 0.001f);
        hitTarget.raycastTarget = true;

        EventTrigger trigger = GetComponent<EventTrigger>();
        if (trigger == null)
            trigger = gameObject.AddComponent<EventTrigger>();

        if (trigger.triggers == null || trigger.triggers.Count == 0)
        {
            EventTrigger.Entry clickEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            clickEntry.callback.AddListener(data =>
            {
                PointerEventData pointer = data as PointerEventData;
                if (pointer != null && pointer.button == PointerEventData.InputButton.Left)
                    PrimaryActionRequested?.Invoke(this);
            });
            trigger.triggers.Add(clickEntry);
        }
    }

    private void HandleInspectRequested()
    {
        InspectRequested?.Invoke(this);
    }

    private static void Stretch(RectTransform rect, float inset = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }
}
