using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Public, read-only view of the match-wide trash.
/// The UI is built at runtime so it remains independent from the editable GameScreen prefab.
/// </summary>
public sealed class TrashPileViewController : MonoBehaviour
{
    private static readonly Color ButtonColor = new Color(0.29f, 0.24f, 0.16f, 0.98f);
    private static readonly Color ButtonHighlightedColor = new Color(0.40f, 0.33f, 0.20f, 1f);
    private static readonly Color PanelColor = new Color(0.105f, 0.095f, 0.08f, 0.99f);
    private static readonly Color BorderColor = new Color(0.48f, 0.39f, 0.23f, 1f);
    private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    private GameScreenController _screen;
    private Button _openButton;
    private Text _openButtonText;
    private GameObject _overlay;
    private RectTransform _cardsRoot;
    private RectTransform _cardsViewport;
    private GridLayoutGroup _cardsGrid;
    private ScrollRect _cardsScroll;
    private Text _title;
    private Text _emptyMessage;
    private GameObject _zoomOverlay;
    private Image _zoomImage;
    private readonly List<GameObject> _renderedCards = new List<GameObject>();
    private int _lastRenderedVersion = -1;

    private const float CardWidth = 130f;
    private const float CardHeight = 200f;
    private const float CardSpacing = 18f;

    private void Awake()
    {
        _screen = GetComponent<GameScreenController>();
        BuildUi();
        BindZoomUi();
        NetworkGameState.StateChanged += Refresh;
        Refresh(NetworkGameState.State);
    }

    private void OnDestroy()
    {
        NetworkGameState.StateChanged -= Refresh;
    }

    private void OnRectTransformDimensionsChange()
    {
        if (_overlay != null && _overlay.activeSelf)
            RefreshGridColumns();
    }

    private void BuildUi()
    {
        if (_openButton != null || _screen == null) return;

        BuildOpenButton();
        BuildOverlay();
        _overlay.SetActive(false);
    }

    private void BuildOpenButton()
    {
        GameObject buttonObject = new GameObject("TrashPileButton", typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(transform, false);
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.one;
        rect.anchorMax = Vector2.one;
        rect.pivot = Vector2.one;
        rect.anchoredPosition = new Vector2(-500f, -80f);
        rect.sizeDelta = new Vector2(158f, 46f);

        Image image = buttonObject.GetComponent<Image>();
        image.color = Color.white;
        image.raycastTarget = true;

        _openButton = buttonObject.GetComponent<Button>();
        _openButton.targetGraphic = image;
        ColorBlock colors = _openButton.colors;
        colors.normalColor = ButtonColor;
        colors.highlightedColor = ButtonHighlightedColor;
        colors.pressedColor = new Color(0.23f, 0.18f, 0.11f, 1f);
        colors.selectedColor = colors.highlightedColor;
        _openButton.colors = colors;
        _openButton.onClick.AddListener(Open);

        _openButtonText = CreateText(buttonObject.transform, "Label", 18, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
        Stretch(_openButtonText.rectTransform, 5f);
        _openButtonText.raycastTarget = false;
        _openButtonText.text = "ÉCART (0)";
    }

    private void BuildOverlay()
    {
        _overlay = new GameObject("TrashPileOverlay", typeof(RectTransform), typeof(Image), typeof(Button));
        _overlay.transform.SetParent(transform, false);
        Stretch(_overlay.GetComponent<RectTransform>(), 0f);
        Image shade = _overlay.GetComponent<Image>();
        shade.color = new Color(0f, 0f, 0f, 0.80f);
        shade.raycastTarget = true;
        Button backgroundClose = _overlay.GetComponent<Button>();
        backgroundClose.targetGraphic = shade;
        backgroundClose.onClick.AddListener(Close);

        GameObject panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(Outline));
        panel.transform.SetParent(_overlay.transform, false);
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.11f, 0.11f);
        panelRect.anchorMax = new Vector2(0.89f, 0.89f);
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        Image panelImage = panel.GetComponent<Image>();
        panelImage.color = PanelColor;
        panelImage.raycastTarget = true;
        Outline outline = panel.GetComponent<Outline>();
        outline.effectColor = BorderColor;
        outline.effectDistance = new Vector2(2f, -2f);

        _title = CreateText(panel.transform, "Title", 28, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
        RectTransform titleRect = _title.rectTransform;
        titleRect.anchorMin = new Vector2(0.035f, 0.865f);
        titleRect.anchorMax = new Vector2(0.82f, 0.965f);
        titleRect.offsetMin = Vector2.zero;
        titleRect.offsetMax = Vector2.zero;
        _title.text = "ÉCART — 0 CARTE";

        Button closeButton = CreateButton(panel.transform, "Close", "FERMER", 18);
        RectTransform closeRect = closeButton.GetComponent<RectTransform>();
        closeRect.anchorMin = new Vector2(0.84f, 0.885f);
        closeRect.anchorMax = new Vector2(0.965f, 0.955f);
        closeRect.offsetMin = Vector2.zero;
        closeRect.offsetMax = Vector2.zero;
        closeButton.onClick.AddListener(Close);

        BuildScrollView(panel.transform);

        _emptyMessage = CreateText(panel.transform, "EmptyMessage", 22, FontStyle.Italic, TextAnchor.MiddleCenter,
            new Color(0.76f, 0.72f, 0.63f, 1f));
        RectTransform emptyRect = _emptyMessage.rectTransform;
        emptyRect.anchorMin = new Vector2(0.08f, 0.20f);
        emptyRect.anchorMax = new Vector2(0.92f, 0.78f);
        emptyRect.offsetMin = Vector2.zero;
        emptyRect.offsetMax = Vector2.zero;
        _emptyMessage.text = "Aucune carte n’a encore été écartée.";
    }

    private void BuildScrollView(Transform panel)
    {
        GameObject scrollObject = new GameObject("CardsScroll", typeof(RectTransform), typeof(ScrollRect));
        scrollObject.transform.SetParent(panel, false);
        RectTransform scrollRectTransform = scrollObject.GetComponent<RectTransform>();
        scrollRectTransform.anchorMin = new Vector2(0.035f, 0.055f);
        scrollRectTransform.anchorMax = new Vector2(0.965f, 0.85f);
        scrollRectTransform.offsetMin = Vector2.zero;
        scrollRectTransform.offsetMax = Vector2.zero;

        GameObject viewportObject = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        viewportObject.transform.SetParent(scrollObject.transform, false);
        _cardsViewport = viewportObject.GetComponent<RectTransform>();
        Stretch(_cardsViewport, 0f);
        Image viewportImage = viewportObject.GetComponent<Image>();
        viewportImage.color = new Color(0f, 0f, 0f, 0.001f);
        viewportImage.raycastTarget = true;
        viewportObject.GetComponent<Mask>().showMaskGraphic = false;

        GameObject contentObject = new GameObject("Cards", typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
        contentObject.transform.SetParent(viewportObject.transform, false);
        _cardsRoot = contentObject.GetComponent<RectTransform>();
        _cardsRoot.anchorMin = new Vector2(0f, 1f);
        _cardsRoot.anchorMax = new Vector2(1f, 1f);
        _cardsRoot.pivot = new Vector2(0.5f, 1f);
        _cardsRoot.anchoredPosition = Vector2.zero;
        _cardsRoot.sizeDelta = Vector2.zero;

        _cardsGrid = contentObject.GetComponent<GridLayoutGroup>();
        _cardsGrid.padding = new RectOffset(14, 14, 12, 12);
        _cardsGrid.cellSize = new Vector2(CardWidth, CardHeight);
        _cardsGrid.spacing = new Vector2(CardSpacing, CardSpacing);
        _cardsGrid.startCorner = GridLayoutGroup.Corner.UpperLeft;
        _cardsGrid.startAxis = GridLayoutGroup.Axis.Horizontal;
        _cardsGrid.childAlignment = TextAnchor.UpperCenter;
        _cardsGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        _cardsGrid.constraintCount = 1;

        ContentSizeFitter fitter = contentObject.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _cardsScroll = scrollObject.GetComponent<ScrollRect>();
        _cardsScroll.viewport = _cardsViewport;
        _cardsScroll.content = _cardsRoot;
        _cardsScroll.horizontal = false;
        _cardsScroll.vertical = true;
        _cardsScroll.movementType = ScrollRect.MovementType.Clamped;
        _cardsScroll.scrollSensitivity = 30f;
    }

    private void Refresh(GameStateSnapshot state)
    {
        BuildUi();
        int count = state != null && state.TrashedCards != null ? state.TrashedCards.Count : 0;
        if (_openButtonText != null) _openButtonText.text = "ÉCART (" + count + ")";

        if (_overlay == null || !_overlay.activeSelf) return;
        int version = state != null ? state.Version : -1;
        if (_lastRenderedVersion == version) return;
        RebuildCards(state);
    }

    private void Open()
    {
        if (_overlay == null) BuildUi();
        if (_overlay == null) return;
        _overlay.SetActive(true);
        _overlay.transform.SetAsLastSibling();
        _lastRenderedVersion = -1;
        RebuildCards(NetworkGameState.State);
    }

    private void Close()
    {
        if (_overlay != null) _overlay.SetActive(false);
    }

    private void RebuildCards(GameStateSnapshot state)
    {
        ClearCards();
        _lastRenderedVersion = state != null ? state.Version : -1;
        List<int> trash = state != null ? state.TrashedCards : null;
        int count = trash != null ? trash.Count : 0;
        if (_title != null) _title.text = "ÉCART — " + count + (count > 1 ? " CARTES" : " CARTE");
        if (_emptyMessage != null) _emptyMessage.gameObject.SetActive(count == 0);
        if (_cardsRoot != null) _cardsRoot.gameObject.SetActive(count > 0);
        if (count == 0 || _cardsRoot == null) return;

        foreach (int instanceId in trash)
        {
            CardInstance instance = NetworkGameState.FindCardInstance(state, instanceId);
            ExtensionPackageData extension = null;
            ExtensionCardData definition = null;
            bool resolved = instance != null && !string.IsNullOrEmpty(instance.DefinitionId) &&
                            RoomGameSetup.TryResolveCard(instance.DefinitionId, out extension, out definition) &&
                            definition != null;

            Sprite sprite = resolved ? ExtensionVisualLoader.LoadCardArtwork(extension, definition) : null;
            GameObject cardObject = new GameObject(
                "Trash_" + instanceId + (resolved ? "_" + definition.id : "_Unresolved"),
                typeof(RectTransform),
                typeof(Image),
                typeof(CardPointerInteraction));
            cardObject.transform.SetParent(_cardsRoot, false);
            Image image = cardObject.GetComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = true;
            image.color = sprite != null ? Color.white : new Color(0.55f, 0.12f, 0.12f, 1f);
            if (resolved)
                DynamicCardCostView.Attach(cardObject, definition);

            if (!resolved)
            {
                Text fallback = CreateText(cardObject.transform, "MissingCard", 14, FontStyle.Bold,
                    TextAnchor.MiddleCenter, Color.white);
                Stretch(fallback.rectTransform, 8f);
                fallback.text = "CARTE INTROUVABLE\n#" + instanceId;
            }

            if (sprite != null)
            {
                CardPointerInteraction pointer = cardObject.GetComponent<CardPointerInteraction>();
                pointer.InspectOnLongPress = false;
                Sprite capturedSprite = sprite;
                ExtensionCardData capturedDefinition = definition;
                pointer.PrimaryActionRequested += () => ShowZoom(capturedSprite, capturedDefinition);
                pointer.InspectRequested += () => ShowZoom(capturedSprite, capturedDefinition);
            }

            _renderedCards.Add(cardObject);
        }

        Canvas.ForceUpdateCanvases();
        RefreshGridColumns();
        LayoutRebuilder.ForceRebuildLayoutImmediate(_cardsRoot);
        Canvas.ForceUpdateCanvases();
        if (_cardsScroll != null)
            _cardsScroll.verticalNormalizedPosition = 1f;
    }

    private void RefreshGridColumns()
    {
        if (_cardsGrid == null || _cardsViewport == null)
            return;

        float availableWidth = _cardsViewport.rect.width -
                               _cardsGrid.padding.left - _cardsGrid.padding.right;
        int columns = Mathf.Max(1, Mathf.FloorToInt((availableWidth + CardSpacing) / (CardWidth + CardSpacing)));
        if (_cardsGrid.constraintCount == columns)
            return;

        _cardsGrid.constraintCount = columns;
        if (_cardsRoot != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(_cardsRoot);
    }

    private void BindZoomUi()
    {
        if (_screen == null) return;
        FieldInfo overlayField = typeof(GameScreenController).GetField("_zoomOverlay", PrivateInstance);
        FieldInfo imageField = typeof(GameScreenController).GetField("_zoomImage", PrivateInstance);
        _zoomOverlay = overlayField != null ? overlayField.GetValue(_screen) as GameObject : null;
        _zoomImage = imageField != null ? imageField.GetValue(_screen) as Image : null;
    }

    private void ShowZoom(Sprite sprite, ExtensionCardData definition)
    {
        if (sprite == null) return;
        if (_zoomOverlay == null || _zoomImage == null) BindZoomUi();
        if (_zoomOverlay == null || _zoomImage == null) return;
        _zoomImage.sprite = sprite;
        _zoomImage.preserveAspect = true;
        DynamicCardCostView.Attach(_zoomImage.gameObject, definition);
        _zoomOverlay.SetActive(true);
        _zoomOverlay.transform.SetAsLastSibling();
    }

    private void ClearCards()
    {
        foreach (GameObject card in _renderedCards)
            if (card != null) Destroy(card);
        _renderedCards.Clear();
    }

    private static Button CreateButton(Transform parent, string name, string label, int fontSize)
    {
        GameObject buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);
        Image image = buttonObject.GetComponent<Image>();
        image.color = ButtonColor;
        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = image;
        Text text = CreateText(buttonObject.transform, "Label", fontSize, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
        Stretch(text.rectTransform, 4f);
        text.raycastTarget = false;
        text.text = label;
        return button;
    }

    private static Text CreateText(Transform parent, string name, int fontSize, FontStyle style, TextAnchor alignment, Color color)
    {
        GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(parent, false);
        Text text = textObject.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;
        return text;
    }

    private static void Stretch(RectTransform rect, float inset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }
}
