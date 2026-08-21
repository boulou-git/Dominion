using System;
using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.UI;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>
/// Data/network controller for the editable Unity lobby prefab.
/// All layout and styling live in prefabs; this component only binds extension data and Photon state.
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

    [Header("Reveal")]
    [SerializeField] private RectTransform _revealCardsRoot;
    [SerializeField] private Text _revealStatus;
    [SerializeField] private Button _startButton;
    [SerializeField] private CardSelectionTileView _revealCardPrefab; // Legacy reference; reveal rendering no longer depends on it.

    private readonly List<GameObject> _spawnedExtensions = new List<GameObject>();
    private readonly List<GameObject> _spawnedCards = new List<GameObject>();
    private readonly List<GameObject> _spawnedRevealCards = new List<GameObject>();

    private GameSetupConfig _config;
    private string _viewedExtensionId;
    private bool _lastInRoom;
    private bool _lastHost;
    private string _lastStage;
    private float _nextPhotonStateCheck;
    private Canvas _canvas;
    private GraphicRaycaster _raycaster;

    private void Awake()
    {
        _canvas = GetComponent<Canvas>();
        _raycaster = GetComponent<GraphicRaycaster>();
        ResolveRevealReferences();

        ExtensionCatalog.Reload();
        _config = RoomGameSetup.ReadCurrent();
        PickInitialExtension();

        if (_validateButton != null)
            _validateButton.onClick.AddListener(ValidateSelection);
        if (_startButton != null)
            _startButton.onClick.AddListener(StartGame);

        CapturePhotonState();
        RefreshAll();
    }

    private void Update()
    {
        // This prefab can exist before the player has joined a room, and can also be
        // instantiated after Photon already fired OnJoinedRoom. Poll only the tiny
        // connection/authority state so the editable screen appears at the right moment.
        if (Time.unscaledTime < _nextPhotonStateCheck)
            return;

        _nextPhotonStateCheck = Time.unscaledTime + 0.20f;

        bool inRoom = PhotonNetwork.InRoom;
        bool host = inRoom && PhotonNetwork.IsMasterClient;
        string stage = _config != null ? _config.stage : null;

        if (inRoom == _lastInRoom && host == _lastHost && string.Equals(stage, _lastStage, StringComparison.Ordinal))
            return;

        _lastInRoom = inRoom;
        _lastHost = host;
        _lastStage = stage;

        if (inRoom)
        {
            _config = RoomGameSetup.ReadCurrent();
            PickInitialExtension();

            if (host && PhotonNetwork.CurrentRoom != null && !PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey(RoomGameSetup.RoomPropertyKey))
                RoomGameSetup.Publish(_config);
        }

        RefreshAll();
    }

    private void OnDestroy()
    {
        ExtensionVisualLoader.ClearCache();
    }

    public override void OnJoinedRoom()
    {
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

    private void ResolveRevealReferences()
    {
        if (_revealScreen == null)
        {
            Transform revealTransform = transform.Find("Reveal");
            if (revealTransform != null)
                _revealScreen = revealTransform.gameObject;
        }

        if (_revealScreen == null)
            return;

        KingdomRevealScreenView view = _revealScreen.GetComponent<KingdomRevealScreenView>();
        if (view == null)
            view = _revealScreen.GetComponentInChildren<KingdomRevealScreenView>(true);

        if (view == null)
            return;

        if (_revealCardsRoot == null)
            _revealCardsRoot = view.CardsRoot;
        if (_revealStatus == null)
            _revealStatus = view.StatusText;
        if (_startButton == null)
            _startButton = view.StartButton;
    }

    private void RefreshAll()
    {
        bool inRoom = PhotonNetwork.InRoom;

        // Before joining/creating a Photon room, keep this full-screen editable setup
        // completely hidden so the ordinary Lobby connection/pseudo UI remains usable.
        if (_canvas != null)
            _canvas.enabled = inRoom;
        if (_raycaster != null)
            _raycaster.enabled = inRoom;

        if (!inRoom)
        {
            SetActive(_hostSelectionScreen, false);
            SetActive(_waitingScreen, false);
            SetActive(_revealScreen, false);
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

        if (!host)
        {
            if (_waitingText != null)
                _waitingText.text = "En attente de l’hôte…\n\nL’hôte choisit les extensions et prépare les 10 cartes Royaume.";
            return;
        }

        RebuildExtensions();
        RebuildCards();
        RefreshSummary();
    }

    private void RebuildExtensions()
    {
        Clear(_spawnedExtensions);
        if (_extensionsRoot == null || _extensionTilePrefab == null)
            return;

        foreach (ExtensionPackageData extension in ExtensionCatalog.All)
        {
            if (extension == null)
                continue;

            ExtensionSetupSelection selection = RoomGameSetup.FindExtension(_config, extension.id);
            bool enabled = selection != null && selection.enabled;
            ExtensionTileView tile = Instantiate(_extensionTilePrefab, _extensionsRoot);
            string extensionId = extension.id;
            tile.Bind(extension, enabled,
                () =>
                {
                    _viewedExtensionId = extensionId;
                    RebuildCards();
                },
                value => SetExtensionEnabled(extensionId, value));
            _spawnedExtensions.Add(tile.gameObject);
        }
    }

    private void RebuildCards()
    {
        Clear(_spawnedCards);
        if (_cardsRoot == null || _cardTilePrefab == null)
            return;

        ExtensionPackageData extension = ExtensionCatalog.Find(_viewedExtensionId);
        ExtensionSetupSelection selection = RoomGameSetup.FindExtension(_config, _viewedExtensionId);
        if (extension == null || selection == null)
            return;

        if (_cardsTitle != null)
            _cardsTitle.text = string.IsNullOrEmpty(extension.name) ? extension.id : extension.name;

        bool extensionEnabled = selection.enabled;
        if (extension.cards == null)
            return;

        foreach (ExtensionCardData card in extension.cards)
        {
            if (card == null || string.IsNullOrEmpty(card.id))
                continue;

            bool storedSelected = selection.selectedCardIds != null && selection.selectedCardIds.Contains(card.id);
            bool effectiveSelected = extensionEnabled && storedSelected;
            string cardId = card.id;

            CardSelectionTileView tile = Instantiate(_cardTilePrefab, _cardsRoot);
            tile.Bind(extension, card, effectiveSelected, extensionEnabled,
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
        ResolveRevealReferences();
        Clear(_spawnedRevealCards);

        if (_revealCardsRoot == null)
        {
            Debug.LogError(
                "Kingdom reveal has no CardsRoot. Run Dominion/UI/Create or Reconnect Kingdom Reveal Prefab, then open Assets/Resources/UI/KingdomRevealScreen.prefab.");
            return;
        }

        int resolved = 0;
        int visualsLoaded = 0;

        if (_config.kingdomCardIds != null)
        {
            foreach (string cardRef in _config.kingdomCardIds)
            {
                ExtensionPackageData extension;
                ExtensionCardData card;
                if (!RoomGameSetup.TryResolveCard(cardRef, out extension, out card))
                {
                    Debug.LogWarning("Reveal could not resolve Kingdom card reference: " + cardRef);
                    continue;
                }

                bool artworkLoaded;
                GameObject tile = CreateRevealCard(extension, card, out artworkLoaded);
                tile.transform.SetParent(_revealCardsRoot, false);
                _spawnedRevealCards.Add(tile);
                resolved++;
                if (artworkLoaded)
                    visualsLoaded++;
            }
        }

        bool host = PhotonNetwork.IsMasterClient;
        if (_startButton != null)
            _startButton.gameObject.SetActive(host);

        if (_revealStatus != null)
        {
            string visualStatus = visualsLoaded == resolved
                ? resolved + "/10 cartes"
                : resolved + "/10 cartes — " + visualsLoaded + " visuels chargés";

            _revealStatus.text = host
                ? visualStatus + " — vérifiez le Royaume puis démarrez la partie."
                : visualStatus + " — en attente du démarrage par l’hôte.";
        }
    }

    private static GameObject CreateRevealCard(ExtensionPackageData extension, ExtensionCardData card, out bool artworkLoaded)
    {
        GameObject root = new GameObject(
            string.IsNullOrEmpty(card.id) ? "RevealCard" : "RevealCard_" + card.id,
            typeof(RectTransform),
            typeof(Image));

        Image image = root.GetComponent<Image>();
        Sprite sprite = ExtensionVisualLoader.LoadCardArtwork(extension, card);
        artworkLoaded = sprite != null;

        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
        image.color = sprite != null ? Color.white : new Color(0.11f, 0.11f, 0.11f, 1f);

        if (sprite != null)
            return root;

        GameObject fallbackObject = new GameObject("MissingArtwork", typeof(RectTransform), typeof(Text));
        fallbackObject.transform.SetParent(root.transform, false);
        RectTransform fallbackRect = fallbackObject.GetComponent<RectTransform>();
        fallbackRect.anchorMin = Vector2.zero;
        fallbackRect.anchorMax = Vector2.one;
        fallbackRect.offsetMin = new Vector2(8f, 8f);
        fallbackRect.offsetMax = new Vector2(-8f, -8f);

        Text fallback = fallbackObject.GetComponent<Text>();
        fallback.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        fallback.alignment = TextAnchor.MiddleCenter;
        fallback.fontSize = 17;
        fallback.color = new Color(0.92f, 0.72f, 0.55f, 1f);
        fallback.raycastTarget = false;
        fallback.text = (string.IsNullOrEmpty(card.name) ? card.id : card.name)
            + "\n\nIMAGE MANQUANTE\nimages/"
            + card.id
            + ".png";

        Debug.LogWarning(
            "No reveal artwork found for "
            + (extension != null ? extension.id : "?")
            + ":"
            + card.id
            + " (name: "
            + card.name
            + ", package: "
            + (extension != null ? extension.packageDirectory : "?")
            + ").");

        return root;
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
            if (!extension.selectedCardIds.Contains(cardId))
                extension.selectedCardIds.Add(cardId);
        }
        else
        {
            extension.selectedCardIds.Remove(cardId);
        }

        RoomGameSetup.Publish(_config);
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
