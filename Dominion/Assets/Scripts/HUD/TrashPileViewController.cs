using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Public, read-only view of the match-wide trash.
/// The visual hierarchy is a nested TrashPileUi instance authored directly in GameScreen.prefab.
/// </summary>
public sealed class TrashPileViewController : MonoBehaviour
{
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
    private readonly List<GameObject> _renderedCards = new List<GameObject>();
    private int _lastRenderedVersion = -1;
    private bool _uiBindingFailed;
    private Coroutine _openRefreshRoutine;
    private readonly HashSet<int> _decisionCandidates = new HashSet<int>();
    private Action _decisionButtonAction;
    private Action<int> _decisionCardAction;
    private Color _normalButtonColor;
    private bool _hasNormalButtonColor;
    private CardSelectionHalo _decisionButtonHalo;

    public Transform DecisionRoot => transform.Find("TrashPileUi");

    private const float CardWidth = 130f;
    private const float CardHeight = 200f;
    private const float CardSpacing = 18f;

    private void Awake()
    {
        _screen = GetComponent<GameScreenController>();
        BuildUi();
        NetworkGameState.StateChanged += Refresh;
        Refresh(NetworkGameState.State);
    }

    private void OnDestroy()
    {
        NetworkGameState.StateChanged -= Refresh;
        if (_openRefreshRoutine != null)
            StopCoroutine(_openRefreshRoutine);
    }

    private void OnRectTransformDimensionsChange()
    {
        if (_overlay != null && _overlay.activeSelf)
            RefreshGridColumns();
    }

    private void BuildUi()
    {
        if (_openButton != null || _screen == null || _uiBindingFailed) return;
        Transform ui = transform.Find("TrashPileUi");
        if (ui == null)
        {
            Debug.LogError("GameScreen.prefab contract is incomplete: nested TrashPileUi instance is missing.", this);
            _uiBindingFailed = true;
            return;
        }

        Transform buttonTransform = ui.Find("TrashPileButton");
        Transform overlayTransform = ui.Find("TrashPileOverlay");
        Transform panel = overlayTransform != null ? overlayTransform.Find("Panel") : null;
        Transform scroll = panel != null ? panel.Find("CardsScroll") : null;
        Transform viewport = scroll != null ? scroll.Find("Viewport") : null;
        Transform cards = viewport != null ? viewport.Find("Cards") : null;

        _openButton = buttonTransform != null ? buttonTransform.GetComponent<Button>() : null;
        _openButtonText = buttonTransform != null ? buttonTransform.Find("Label")?.GetComponent<Text>() : null;
        _overlay = overlayTransform != null ? overlayTransform.gameObject : null;
        _title = panel != null ? panel.Find("Title")?.GetComponent<Text>() : null;
        _emptyMessage = panel != null ? panel.Find("EmptyMessage")?.GetComponent<Text>() : null;
        _cardsScroll = scroll != null ? scroll.GetComponent<ScrollRect>() : null;
        _cardsViewport = viewport as RectTransform;
        _cardsRoot = cards as RectTransform;
        _cardsGrid = cards != null ? cards.GetComponent<GridLayoutGroup>() : null;
        Button backgroundClose = overlayTransform != null ? overlayTransform.GetComponent<Button>() : null;
        Button closeButton = panel != null ? panel.Find("Close")?.GetComponent<Button>() : null;

        if (_openButton == null || _openButtonText == null || _overlay == null || _title == null ||
            _emptyMessage == null || _cardsScroll == null || _cardsViewport == null || _cardsRoot == null ||
            _cardsGrid == null || backgroundClose == null || closeButton == null)
        {
            Debug.LogError("TrashPileUi prefab contract is incomplete.", ui);
            _openButton = null;
            _uiBindingFailed = true;
            return;
        }

        _openButton.onClick.AddListener(Open);
        _decisionButtonHalo = _openButton.GetComponent<CardSelectionHalo>();
        if (_decisionButtonHalo == null) _decisionButtonHalo = _openButton.gameObject.AddComponent<CardSelectionHalo>();
        _decisionButtonHalo.SetState(CardSelectionHalo.HighlightState.None);
        if (_openButton.targetGraphic != null)
        {
            _normalButtonColor = _openButton.targetGraphic.color;
            _hasNormalButtonColor = true;
        }
        backgroundClose.onClick.AddListener(Close);
        closeButton.onClick.AddListener(Close);
        _overlay.SetActive(false);
    }

    private void Refresh(GameStateSnapshot state)
    {
        BuildUi();
        int count = state != null && state.TrashedCards != null ? state.TrashedCards.Count : 0;
        if (_openButtonText != null) _openButtonText.text = "ÉCART (" + count + ")";
        bool decisionLocked = PendingDecisionInputLock.IsActive(state);
        bool decisionEnabled = _decisionButtonAction != null || _decisionCardAction != null;
        if (_openButton != null) _openButton.interactable = !decisionLocked || decisionEnabled;
        if (decisionLocked && !decisionEnabled && _overlay != null && _overlay.activeSelf)
            Close();

        if (_overlay == null || !_overlay.activeSelf) return;
        int version = state != null ? state.Version : -1;
        if (_lastRenderedVersion == version) return;
        RebuildCards(state);
    }

    private void Open()
    {
        if (_decisionButtonAction != null)
        {
            Action action = _decisionButtonAction;
            _decisionButtonAction = null;
            action();
            return;
        }
        if (PendingDecisionInputLock.IsActive(NetworkGameState.State) && _decisionCardAction == null) return;
        if (_overlay == null) BuildUi();
        if (_overlay == null) return;
        _overlay.SetActive(true);
        _overlay.transform.SetAsLastSibling();
        _lastRenderedVersion = -1;
        if (_openRefreshRoutine != null)
            StopCoroutine(_openRefreshRoutine);
        _openRefreshRoutine = StartCoroutine(RebuildAfterOverlayLayout());
    }

    private IEnumerator RebuildAfterOverlayLayout()
    {
        // The prefab overlay is inactive until Open. Its ScrollRect viewport therefore
        // has no reliable dimensions during the click frame. Wait for Unity's layout
        // pass before creating and positioning the cards inside the masked viewport.
        yield return null;
        Canvas.ForceUpdateCanvases();
        RebuildCards(NetworkGameState.State);
        _openRefreshRoutine = null;
    }

    private void Close()
    {
        if (_openRefreshRoutine != null)
        {
            StopCoroutine(_openRefreshRoutine);
            _openRefreshRoutine = null;
        }
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
            if (!resolved)
                Debug.LogWarning("Could not resolve trashed card instance #" + instanceId + "; keeping a placeholder card visible.", this);

            RuntimeCardView cardView = RuntimeCardView.Create(
                _cardsRoot,
                "Trash_" + instanceId + (resolved ? "_" + definition.id : "_Unresolved"),
                definition,
                sprite,
                resolved);
            if (cardView == null) continue;
            GameObject cardObject = cardView.gameObject;
            cardObject.SetActive(true);
            cardObject.transform.localScale = Vector3.one;

            if (sprite != null)
            {
                CardPointerInteraction pointer = cardView.Pointer;
                pointer.InspectOnLongPress = false;
                bool candidate = _decisionCardAction != null && _decisionCandidates.Contains(instanceId);
                pointer.SetDecisionCandidate(candidate);
                if (candidate)
                {
                    CardSelectionHalo halo = cardObject.GetComponent<CardSelectionHalo>();
                    if (halo == null) halo = cardObject.AddComponent<CardSelectionHalo>();
                    halo.SetState(CardSelectionHalo.HighlightState.Candidate);
                    int capturedId = instanceId;
                    pointer.PrimaryActionRequested += () => _decisionCardAction?.Invoke(capturedId);
                }
                else if (_decisionCardAction == null)
                {
                    Sprite capturedSprite = sprite;
                    ExtensionCardData capturedDefinition = definition;
                    pointer.PrimaryActionRequested += () => ShowZoom(capturedSprite, capturedDefinition);
                    pointer.InspectRequested += () => ShowZoom(capturedSprite, capturedDefinition);
                }
                else
                {
                    CanvasGroup dim = cardObject.AddComponent<CanvasGroup>();
                    dim.alpha = 0.42f;
                }
            }

            _renderedCards.Add(cardObject);
        }

        Canvas.ForceUpdateCanvases();
        RefreshGridColumns();
        ResizeCardsContent();
        _cardsGrid.CalculateLayoutInputHorizontal();
        _cardsGrid.SetLayoutHorizontal();
        _cardsGrid.CalculateLayoutInputVertical();
        _cardsGrid.SetLayoutVertical();
        LayoutRebuilder.ForceRebuildLayoutImmediate(_cardsRoot);
        Canvas.ForceUpdateCanvases();
        if (_cardsScroll != null)
            _cardsScroll.verticalNormalizedPosition = 1f;
    }

    private void ResizeCardsContent()
    {
        if (_cardsRoot == null || _cardsGrid == null)
            return;

        int columns = Mathf.Max(1, _cardsGrid.constraintCount);
        int rows = Mathf.CeilToInt(_renderedCards.Count / (float)columns);
        float height = _cardsGrid.padding.top + _cardsGrid.padding.bottom;
        if (rows > 0)
            height += rows * _cardsGrid.cellSize.y + (rows - 1) * _cardsGrid.spacing.y;

        _cardsRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(1f, height));
        _cardsRoot.anchoredPosition = Vector2.zero;
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
        ResizeCardsContent();
        if (_cardsRoot != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(_cardsRoot);
    }

    private void ShowZoom(Sprite sprite, ExtensionCardData definition)
    {
        _screen?.ShowCardZoom(sprite, definition);
    }

    private void ClearCards()
    {
        foreach (GameObject card in _renderedCards)
            if (card != null) Destroy(card);
        _renderedCards.Clear();
    }

    public void SetAlternativeButton(Action chooseTrash)
    {
        _decisionButtonAction = chooseTrash;
        _decisionCardAction = null;
        _decisionCandidates.Clear();
        RefreshDecisionButton();
    }

    public void SetDecisionCards(IEnumerable<int> candidates, Action<int> chooseCard, bool openImmediately)
    {
        _decisionButtonAction = null;
        _decisionCardAction = chooseCard;
        _decisionCandidates.Clear();
        if (candidates != null) foreach (int id in candidates) _decisionCandidates.Add(id);
        RefreshDecisionButton();
        _lastRenderedVersion = -1;
        if (openImmediately) Open();
        else if (_overlay != null && _overlay.activeSelf) RebuildCards(NetworkGameState.State);
    }

    public void ClearDecisionChoice()
    {
        bool wasDecision = _decisionButtonAction != null || _decisionCardAction != null;
        _decisionButtonAction = null;
        _decisionCardAction = null;
        _decisionCandidates.Clear();
        RefreshDecisionButton();
        if (wasDecision) Close();
    }

    private void RefreshDecisionButton()
    {
        if (_openButton == null) return;
        bool active = _decisionButtonAction != null || _decisionCardAction != null;
        _openButton.interactable = active || !PendingDecisionInputLock.IsActive(NetworkGameState.State);
        if (_openButton.targetGraphic != null && _hasNormalButtonColor)
            _openButton.targetGraphic.color = _normalButtonColor;
        _decisionButtonHalo?.SetState(active ? CardSelectionHalo.HighlightState.Candidate
            : CardSelectionHalo.HighlightState.None);
    }

}
