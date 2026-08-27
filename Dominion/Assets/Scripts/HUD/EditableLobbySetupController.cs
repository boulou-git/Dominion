using System;
using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.UI;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>
/// Binds the editable lobby UI to extension data and Photon room state.
/// Flow: host selects cards -> host draws 10 -> everybody sees the same 10 -> host starts.
/// </summary>
public sealed class EditableLobbySetupController : MonoBehaviourPunCallbacks
{
    [Header("Screens")]
    [SerializeField] private GameObject _hostSelectionScreen;
    [SerializeField] private GameObject _waitingScreen;
    [SerializeField] private GameObject _revealScreen;

    [Header("Host selection")]
    [SerializeField] private RectTransform _extensionsRoot;
    [SerializeField] private RectTransform _cardsRoot;
    [SerializeField] private Text _cardsTitle;
    [SerializeField] private Text _selectionSummary;
    [SerializeField] private Button _validateButton;
    [SerializeField] private ExtensionTileView _extensionTilePrefab;
    [SerializeField] private CardSelectionTileView _cardTilePrefab;

    [Header("Waiting")]
    [SerializeField] private Text _waitingText;

    private readonly List<GameObject> _spawnedExtensions = new List<GameObject>();
    private readonly List<GameObject> _spawnedCards = new List<GameObject>();
    private readonly List<GameObject> _spawnedRevealCards = new List<GameObject>();
    private readonly Dictionary<RectTransform, float> _extensionTileAspectRatios = new Dictionary<RectTransform, float>();

    private GameSetupConfig _config;
    private string _viewedExtensionId;
    private bool _lastInRoom;
    private bool _lastHost;
    private string _lastStage;
    private float _nextPhotonStateCheck;
    private Canvas _canvas;
    private GraphicRaycaster _raycaster;
    private RectTransform _extensionsPanel;
    private RectTransform _cardsPanel;
    private Button _cardsBackButton;
    private Coroutine _panelTransition;
    private bool _cardsPanelOpen;
    private bool _selectionFlowConfigured;

    private const float PanelSlideDuration = 0.24f;

    // Final 10-card screen. Each revealed card is ONE GameObject with ONE Image.
    // The sprite is assigned directly to the exact Image visible in the grid.
    private RectTransform _revealCardsRoot;
    private Text _revealStatus;
    private Button _revealStartButton;

    // Local-only card inspection for the reveal screen.
    private GameObject _revealZoomOverlay;
    private Image _revealZoomImage;

    private void Awake()
    {
        _canvas = GetComponent<Canvas>();
        _raycaster = GetComponent<GraphicRaycaster>();
        ConfigureSelectionFlow();
        BindRevealPrefab();

        ExtensionCatalog.Reload();
        _config = RoomGameSetup.ReadCurrent();
        PickInitialExtension();

        if (_validateButton != null)
            _validateButton.onClick.AddListener(ValidateSelection);

        CapturePhotonState();
        RefreshAll();
    }

    private void Update()
    {
        if (Time.unscaledTime < _nextPhotonStateCheck)
            return;

        _nextPhotonStateCheck = Time.unscaledTime + 0.20f;

        bool inRoom = PhotonNetwork.InRoom;
        bool host = inRoom && PhotonNetwork.IsMasterClient;
        string stage = _config != null ? _config.stage : null;

        if (inRoom == _lastInRoom && host == _lastHost && string.Equals(stage, _lastStage, StringComparison.Ordinal))
            return;

        if (inRoom)
        {
            _config = RoomGameSetup.ReadCurrent();
            PickInitialExtension();

            if (host && PhotonNetwork.CurrentRoom != null && !PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey(RoomGameSetup.RoomPropertyKey))
                RoomGameSetup.Publish(_config);
        }

        CapturePhotonState();
        RefreshAll();
    }

    public override void OnJoinedRoom()
    {
        _cardsPanelOpen = false;
        ApplySelectionFlowState(true);
        ExtensionCatalog.Reload();
        _config = RoomGameSetup.ReadCurrent();
        PickInitialExtension();

        if (PhotonNetwork.IsMasterClient && PhotonNetwork.CurrentRoom != null && !PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey(RoomGameSetup.RoomPropertyKey))
            RoomGameSetup.Publish(_config);

        CapturePhotonState();
        RefreshAll();
    }

    public override void OnLeftRoom()
    {
        _cardsPanelOpen = false;
        ApplySelectionFlowState(true);
        _config = RoomGameSetup.ReadCurrent();
        CapturePhotonState();
        RefreshAll();
    }

    public override void OnMasterClientSwitched(Player newMasterClient)
    {
        _config = RoomGameSetup.ReadCurrent();
        CapturePhotonState();
        RefreshAll();
    }

    public override void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
    {
        if (propertiesThatChanged == null || !propertiesThatChanged.ContainsKey(RoomGameSetup.RoomPropertyKey))
            return;

        _config = RoomGameSetup.ReadCurrent();
        CapturePhotonState();
        RefreshAll();
    }

    private void CapturePhotonState()
    {
        _lastInRoom = PhotonNetwork.InRoom;
        _lastHost = _lastInRoom && PhotonNetwork.IsMasterClient;
        _lastStage = _config != null ? _config.stage : null;
    }

    private void RefreshAll()
    {
        bool inRoom = PhotonNetwork.InRoom;

        if (_canvas != null)
            _canvas.enabled = inRoom;
        if (_raycaster != null)
            _raycaster.enabled = inRoom;

        if (!inRoom)
        {
            SetActive(_hostSelectionScreen, false);
            SetActive(_waitingScreen, false);
            SetActive(_revealScreen, false);
            SetActive(_revealZoomOverlay, false);
            return;
        }

        if (_config == null)
            _config = RoomGameSetup.ReadCurrent();

        bool reveal = string.Equals(_config.stage, RoomGameSetup.RevealStage, StringComparison.Ordinal);
        bool host = PhotonNetwork.IsMasterClient;

        SetActive(_hostSelectionScreen, !reveal && host);
        SetActive(_waitingScreen, !reveal && !host);
        SetActive(_revealScreen, reveal);

        if (reveal)
        {
            RebuildReveal();
            return;
        }

        SetActive(_revealZoomOverlay, false);

        if (!host)
        {
            if (_waitingText != null)
                _waitingText.text = "En attente de l’hôte…\n\nL’hôte choisit les extensions et prépare les 10 cartes Royaume.";
            return;
        }

        RebuildExtensions();
        RebuildCards();
        RefreshSummary();
        ApplySelectionFlowState(false);
    }

    private void RebuildExtensions()
    {
        Clear(_spawnedExtensions);
        _extensionTileAspectRatios.Clear();
        if (_extensionsRoot == null || _extensionTilePrefab == null)
            return;

        foreach (ExtensionPackageData extension in ExtensionCatalog.All)
        {
            if (extension == null)
                continue;

            ExtensionSetupSelection selection = RoomGameSetup.FindExtension(_config, extension.id);
            bool enabled = selection != null && selection.enabled;
            string extensionId = extension.id;

            ExtensionTileView tile = Instantiate(_extensionTilePrefab, _extensionsRoot);
            tile.Bind(
                extension,
                enabled,
                () =>
                {
                    _viewedExtensionId = extensionId;
                    RebuildCards();
                    ShowCardsPanel();
                },
                value => SetExtensionEnabled(extensionId, value));

            _spawnedExtensions.Add(tile.gameObject);
            RememberExtensionTileAspectRatio(tile);
        }

        ApplyExtensionTileAspectRatios();
    }

    private void RebuildCards()
    {
        Clear(_spawnedCards);
        if (_cardsRoot == null || _cardTilePrefab == null)
            return;

        ExtensionPackageData extension = ExtensionCatalog.Find(_viewedExtensionId);
        ExtensionSetupSelection selection = RoomGameSetup.FindExtension(_config, _viewedExtensionId);
        if (extension == null || selection == null || extension.cards == null)
            return;

        if (_cardsTitle != null)
            _cardsTitle.text = string.IsNullOrEmpty(extension.name) ? extension.id : extension.name;

        bool extensionEnabled = selection.enabled;

        foreach (ExtensionCardData card in extension.cards)
        {
            if (card == null || string.IsNullOrEmpty(card.id))
                continue;

            bool storedSelected = selection.selectedCardIds != null && selection.selectedCardIds.Contains(card.id);
            bool effectiveSelected = extensionEnabled && storedSelected;
            string cardId = card.id;

            CardSelectionTileView tile = Instantiate(_cardTilePrefab, _cardsRoot);
            tile.Bind(
                extension,
                card,
                effectiveSelected,
                true,
                value => SetCardSelected(extension.id, cardId, value));

            _spawnedCards.Add(tile.gameObject);
        }
    }

    private void RefreshSummary()
    {
        int selected = RoomGameSetup.CountSelectedCards(_config);

        if (_selectionSummary != null)
            _selectionSummary.text = selected + " cartes dans le pool — 10 seront tirées pour la partie.";

        if (_validateButton != null)
            _validateButton.interactable = selected >= RoomGameSetup.KingdomCardCount;
    }

    private void RebuildReveal()
    {
        if (_revealScreen == null || _revealCardsRoot == null)
            return;

        _revealScreen.SetActive(true);
        _revealScreen.transform.SetAsLastSibling();
        Clear(_spawnedRevealCards);

        int shown = 0;

        if (_config.kingdomCardIds != null)
        {
            foreach (string cardRef in _config.kingdomCardIds)
            {
                ExtensionPackageData extension;
                ExtensionCardData card;
                if (!RoomGameSetup.TryResolveCard(cardRef, out extension, out card))
                {
                    Debug.LogWarning("Could not resolve revealed Kingdom card: " + cardRef);
                    continue;
                }

                Sprite sprite = ExtensionVisualLoader.LoadCardArtwork(extension, card);
                GameObject cardObject = CreateRevealCard(card, sprite);
                if (cardObject == null) continue;
                _spawnedRevealCards.Add(cardObject);
                shown++;

                Image visibleImage = cardObject.GetComponent<Image>();
                Debug.Log(
                    "Reveal card " + cardRef
                    + " -> object=" + cardObject.name
                    + ", imageInstance=" + visibleImage.GetInstanceID()
                    + ", sprite=" + (visibleImage.sprite != null ? visibleImage.sprite.name : "NULL"));
            }
        }

        bool host = PhotonNetwork.IsMasterClient;
        if (_revealStartButton != null)
            _revealStartButton.gameObject.SetActive(host);

        if (_revealStatus != null)
        {
            _revealStatus.text = host
                ? shown + "/10 cartes — cliquez sur une carte pour l’agrandir."
                : shown + "/10 cartes — cliquez sur une carte pour l’agrandir. En attente de l’hôte.";
        }
    }

    private GameObject CreateRevealCard(ExtensionCardData card, Sprite sprite)
    {
        string id = card != null && !string.IsNullOrEmpty(card.id) ? card.id : "unknown";
        RuntimeCardView cardView = RuntimeCardView.Create(_revealCardsRoot, "RevealCard_" + id, card, sprite, sprite != null);
        if (cardView == null) return null;
        GameObject cardObject = cardView.gameObject;

        if (sprite != null)
        {
            CardPointerInteraction pointer = cardView.Pointer;
            pointer.InspectOnLongPress = false;
            ExtensionCardData capturedDefinition = card;
            pointer.PrimaryActionRequested += () => ShowRevealZoom(sprite, capturedDefinition);
            pointer.InspectRequested += () => ShowRevealZoom(sprite, capturedDefinition);
        }

        return cardObject;
    }

    private void BindRevealPrefab()
    {
        if (_revealScreen == null)
        {
            Debug.LogError("LobbySetupScreen prefab contract is incomplete: Reveal is missing.", this);
            return;
        }

        Transform revealCards = _revealScreen.transform.Find("RevealCards");
        _revealCardsRoot = revealCards != null ? revealCards.Find("Content") as RectTransform : null;
        _revealStatus = _revealScreen.transform.Find("Status")?.GetComponent<Text>();
        _revealStartButton = _revealScreen.transform.Find("StartButton")?.GetComponent<Button>();
        if (_revealCardsRoot == null || _revealStatus == null || _revealStartButton == null)
        {
            Debug.LogError("LobbySetupScreen Reveal contract is incomplete.", _revealScreen);
            return;
        }
        _revealStartButton.onClick.AddListener(StartGame);
        GameObject zoomPrefab = Resources.Load<GameObject>("UI/CardZoomOverlay");
        if (zoomPrefab == null)
        {
            Debug.LogError("CardZoomOverlay prefab missing at Resources/UI/CardZoomOverlay.", this);
            return;
        }
        _revealZoomOverlay = Instantiate(zoomPrefab, _revealScreen.transform);
        _revealZoomOverlay.name = "CardZoomOverlay";
        _revealZoomImage = _revealZoomOverlay.transform.Find("ZoomedCard")?.GetComponent<Image>();
        Button closeButton = _revealZoomOverlay.GetComponent<Button>();
        if (_revealZoomImage == null || closeButton == null)
        {
            Debug.LogError("CardZoomOverlay prefab contract is incomplete.", _revealZoomOverlay);
            return;
        }
        closeButton.onClick.AddListener(HideRevealZoom);
        _revealZoomOverlay.SetActive(false);
    }

    private void ShowRevealZoom(Sprite sprite, ExtensionCardData definition)
    {
        if (sprite == null)
            return;

        if (_revealZoomOverlay == null || _revealZoomImage == null)
            return;

        _revealZoomImage.sprite = sprite;
        _revealZoomImage.enabled = true;
        DynamicCardCostView.Attach(_revealZoomImage.gameObject, definition);
        _revealZoomOverlay.SetActive(true);
        _revealZoomOverlay.transform.SetAsLastSibling();
    }

    private void HideRevealZoom()
    {
        SetActive(_revealZoomOverlay, false);
    }

    private void SetExtensionEnabled(string extensionId, bool enabled)
    {
        if (!PhotonNetwork.IsMasterClient)
            return;

        ExtensionSetupSelection selection = RoomGameSetup.FindExtension(_config, extensionId);
        if (selection == null)
            return;

        selection.enabled = enabled;
        RoomGameSetup.Publish(_config);
        RefreshAll();
    }

    private void SetCardSelected(string extensionId, string cardId, bool selected)
    {
        if (!PhotonNetwork.IsMasterClient)
            return;

        ExtensionSetupSelection extension = RoomGameSetup.FindExtension(_config, extensionId);
        if (extension == null)
            return;

        if (extension.selectedCardIds == null)
            extension.selectedCardIds = new List<string>();

        if (selected)
        {
            // A click in a disabled extension starts a fresh partial selection. Default
            // configs retain every card id even while disabled, so clear those defaults.
            if (!extension.enabled)
            {
                extension.selectedCardIds.Clear();
                extension.enabled = true;
            }
            if (!extension.selectedCardIds.Contains(cardId))
                extension.selectedCardIds.Add(cardId);
        }
        else
        {
            extension.selectedCardIds.Remove(cardId);
            if (extension.selectedCardIds.Count == 0)
                extension.enabled = false;
        }

        RoomGameSetup.Publish(_config);
        RebuildExtensions();
        RebuildCards();
        RefreshSummary();
    }

    private void ValidateSelection()
    {
        if (PhotonNetwork.IsMasterClient)
            RoomGameSetup.FinaliseKingdom(_config);
    }

    private void StartGame()
    {
        if (PhotonNetwork.IsMasterClient && RoomConnectionHandler.Instance != null)
            RoomConnectionHandler.Instance.StartGameMaster();
    }

    private void PickInitialExtension()
    {
        if (!string.IsNullOrEmpty(_viewedExtensionId) && ExtensionCatalog.Find(_viewedExtensionId) != null)
            return;

        if (ExtensionCatalog.All.Count > 0)
            _viewedExtensionId = ExtensionCatalog.All[0].id;
    }

    private void OnRectTransformDimensionsChange()
    {
        ApplyExtensionTileAspectRatios();
    }

    // Width is assigned by the scroll view's VerticalLayoutGroup. Height follows the
    // aspect ratio authored on ExtensionTile.prefab's LayoutElement, so visual sizing
    // remains editable in one place in the Inspector.
    private void RememberExtensionTileAspectRatio(ExtensionTileView tile)
    {
        if (tile == null)
            return;

        LayoutElement layout = tile.GetComponent<LayoutElement>();
        RectTransform rect = tile.GetComponent<RectTransform>();
        if (layout == null || rect == null || layout.preferredWidth <= 0f || layout.preferredHeight <= 0f)
            return;

        _extensionTileAspectRatios[rect] = layout.preferredWidth / layout.preferredHeight;
    }

    private void ApplyExtensionTileAspectRatios()
    {
        if (_extensionsRoot == null || _extensionTileAspectRatios.Count == 0)
            return;

        VerticalLayoutGroup listLayout = _extensionsRoot.GetComponent<VerticalLayoutGroup>();
        float width = _extensionsRoot.rect.width;
        if (listLayout != null)
            width -= listLayout.padding.left + listLayout.padding.right;
        if (width <= 0f)
            return;

        foreach (KeyValuePair<RectTransform, float> pair in _extensionTileAspectRatios)
        {
            if (pair.Key == null || pair.Value <= 0f)
                continue;
            LayoutElement layout = pair.Key.GetComponent<LayoutElement>();
            if (layout != null)
                layout.preferredHeight = width / pair.Value;
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(_extensionsRoot);
    }

    /// <summary>Binds the prefab-authored two-step selection flow.</summary>
    private void ConfigureSelectionFlow()
    {
        if (_selectionFlowConfigured || _hostSelectionScreen == null)
            return;

        _extensionsPanel = FindDeepChild(_hostSelectionScreen.transform, "ExtensionsPanel") as RectTransform;
        _cardsPanel = FindDeepChild(_hostSelectionScreen.transform, "CardsPanel") as RectTransform;
        Transform existingBack = _cardsPanel != null ? FindDeepChild(_cardsPanel, "BackButton") : null;
        _cardsBackButton = existingBack != null ? existingBack.GetComponent<Button>() : null;
        if (_extensionsPanel == null || _cardsPanel == null || _cardsBackButton == null)
        {
            Debug.LogError("LobbySetupScreen prefab contract is incomplete: ExtensionsPanel, CardsPanel or BackButton is missing.", this);
            return;
        }

        _selectionFlowConfigured = true;
        _cardsBackButton.onClick.RemoveAllListeners();
        _cardsBackButton.onClick.AddListener(HideCardsPanel);

        ApplySelectionFlowState(true);
    }

    private void ShowCardsPanel()
    {
        ConfigureSelectionFlow();
        if (_cardsPanel == null)
            return;

        _cardsPanelOpen = true;
        _cardsPanel.gameObject.SetActive(true);
        _cardsPanel.SetAsLastSibling();
        StartPanelTransition(true);
    }

    private void HideCardsPanel()
    {
        if (_cardsPanel == null || !_cardsPanel.gameObject.activeSelf)
            return;

        _cardsPanelOpen = false;
        StartPanelTransition(false);
    }

    private void ApplySelectionFlowState(bool immediate)
    {
        if (!_selectionFlowConfigured || _cardsPanel == null)
            return;

        if (_cardsPanelOpen)
        {
            _cardsPanel.gameObject.SetActive(true);
            _cardsPanel.SetAsLastSibling();
            if (immediate) _cardsPanel.anchoredPosition = Vector2.zero;
        }
        else if (immediate)
        {
            _cardsPanel.anchoredPosition = new Vector2(PanelSlideDistance(), 0f);
            _cardsPanel.gameObject.SetActive(false);
        }
    }

    private void StartPanelTransition(bool opening)
    {
        if (_panelTransition != null)
            StopCoroutine(_panelTransition);
        _panelTransition = StartCoroutine(AnimateCardsPanel(opening));
    }

    private IEnumerator AnimateCardsPanel(bool opening)
    {
        Vector2 visible = Vector2.zero;
        Vector2 hidden = new Vector2(PanelSlideDistance(), 0f);
        Vector2 start = opening ? hidden : _cardsPanel.anchoredPosition;
        Vector2 end = opening ? visible : hidden;
        if (opening) _cardsPanel.anchoredPosition = start;

        float elapsed = 0f;
        while (elapsed < PanelSlideDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / PanelSlideDuration);
            t = 1f - Mathf.Pow(1f - t, 3f);
            _cardsPanel.anchoredPosition = Vector2.LerpUnclamped(start, end, t);
            yield return null;
        }

        _cardsPanel.anchoredPosition = end;
        if (!opening) _cardsPanel.gameObject.SetActive(false);
        _panelTransition = null;
    }

    private float PanelSlideDistance()
    {
        RectTransform hostRect = _hostSelectionScreen != null ? _hostSelectionScreen.GetComponent<RectTransform>() : null;
        float width = hostRect != null ? hostRect.rect.width : 0f;
        return width > 0f ? width : 1920f;
    }

    private static Transform FindDeepChild(Transform parent, string childName)
    {
        if (parent == null)
            return null;
        foreach (Transform child in parent)
        {
            if (child.name == childName)
                return child;
            Transform nested = FindDeepChild(child, childName);
            if (nested != null)
                return nested;
        }
        return null;
    }

    private static void SetActive(GameObject target, bool active)
    {
        if (target != null)
            target.SetActive(active);
    }

    private static void Clear(List<GameObject> objects)
    {
        foreach (GameObject item in objects)
        {
            if (item != null)
                Destroy(item);
        }

        objects.Clear();
    }
}
