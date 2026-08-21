using System;
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
    [SerializeField] private GameObject _revealScreen; // Legacy only; intentionally never displayed.

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

    private GameSetupConfig _config;
    private string _viewedExtensionId;
    private bool _lastInRoom;
    private bool _lastHost;
    private string _lastStage;
    private float _nextPhotonStateCheck;
    private Canvas _canvas;
    private GraphicRaycaster _raycaster;

    // Final 10-card screen. Each revealed card is ONE GameObject with ONE Image.
    // The sprite is assigned directly to the exact Image visible in the grid.
    private GameObject _revealOverlay;
    private RectTransform _revealCardsRoot;
    private Text _revealStatus;
    private Button _revealStartButton;

    private void Awake()
    {
        _canvas = GetComponent<Canvas>();
        _raycaster = GetComponent<GraphicRaycaster>();

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
            SetActive(_revealOverlay, false);
            return;
        }

        if (_config == null)
            _config = RoomGameSetup.ReadCurrent();

        bool reveal = string.Equals(_config.stage, RoomGameSetup.RevealStage, StringComparison.Ordinal);
        bool host = PhotonNetwork.IsMasterClient;

        SetActive(_hostSelectionScreen, !reveal && host);
        SetActive(_waitingScreen, !reveal && !host);
        SetActive(_revealScreen, false);

        if (reveal)
        {
            RebuildReveal();
            return;
        }

        SetActive(_revealOverlay, false);

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
            string extensionId = extension.id;

            ExtensionTileView tile = Instantiate(_extensionTilePrefab, _extensionsRoot);
            tile.Bind(
                extension,
                enabled,
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
                extensionEnabled,
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
        EnsureRevealOverlay();
        if (_revealOverlay == null || _revealCardsRoot == null)
            return;

        _revealOverlay.SetActive(true);
        _revealOverlay.transform.SetAsLastSibling();
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
                cardObject.transform.SetParent(_revealCardsRoot, false);
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
                ? shown + "/10 cartes — démarrez la partie quand vous êtes prêt."
                : shown + "/10 cartes — en attente de l’hôte.";
        }
    }

    private static GameObject CreateRevealCard(ExtensionCardData card, Sprite sprite)
    {
        string id = card != null && !string.IsNullOrEmpty(card.id) ? card.id : "unknown";
        GameObject cardObject = UiObject("RevealCard_" + id, typeof(Image));

        Image image = cardObject.GetComponent<Image>();
        image.sprite = sprite;
        image.color = sprite != null ? Color.white : new Color(0.15f, 0.05f, 0.05f, 1f);
        image.preserveAspect = true;
        image.raycastTarget = false;
        image.enabled = true;

        if (sprite == null)
        {
            Text missing = ChildText(
                cardObject.transform,
                "MissingArtwork",
                (card != null ? card.name : id) + "\nIMAGE MANQUANTE",
                16,
                TextAnchor.MiddleCenter,
                new Vector2(0.05f, 0.05f),
                new Vector2(0.95f, 0.95f));
            missing.color = new Color(1f, 0.75f, 0.65f, 1f);
        }

        return cardObject;
    }

    private void EnsureRevealOverlay()
    {
        if (_revealOverlay != null)
            return;

        _revealOverlay = UiObject("KingdomRevealOverlay", typeof(Image));
        _revealOverlay.transform.SetParent(transform, false);
        Stretch(_revealOverlay.GetComponent<RectTransform>());

        Image background = _revealOverlay.GetComponent<Image>();
        background.color = new Color(0.055f, 0.055f, 0.055f, 1f);
        background.raycastTarget = true;

        Text title = ChildText(
            _revealOverlay.transform,
            "Title",
            "LES 10 CARTES ROYAUME",
            36,
            TextAnchor.MiddleCenter,
            new Vector2(0.08f, 0.91f),
            new Vector2(0.92f, 0.985f));
        title.fontStyle = FontStyle.Bold;

        GameObject gridObject = UiObject("CardsGrid", typeof(GridLayoutGroup));
        gridObject.transform.SetParent(_revealOverlay.transform, false);
        _revealCardsRoot = gridObject.GetComponent<RectTransform>();
        SetAnchors(_revealCardsRoot, new Vector2(0.06f, 0.17f), new Vector2(0.94f, 0.89f));

        GridLayoutGroup grid = gridObject.GetComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(210f, 324f);
        grid.spacing = new Vector2(20f, 22f);
        grid.padding = new RectOffset(12, 12, 12, 12);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 5;
        grid.childAlignment = TextAnchor.MiddleCenter;
        grid.startAxis = GridLayoutGroup.Axis.Horizontal;
        grid.startCorner = GridLayoutGroup.Corner.UpperLeft;

        _revealStatus = ChildText(
            _revealOverlay.transform,
            "Status",
            string.Empty,
            19,
            TextAnchor.MiddleLeft,
            new Vector2(0.08f, 0.045f),
            new Vector2(0.64f, 0.135f));

        _revealStartButton = ChildButton(
            _revealOverlay.transform,
            "StartButton",
            "DÉMARRER LA PARTIE",
            new Vector2(0.68f, 0.04f),
            new Vector2(0.92f, 0.14f));
        _revealStartButton.onClick.AddListener(StartGame);
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

    private static GameObject UiObject(string name, params Type[] components)
    {
        Type[] all = new Type[components.Length + 1];
        all[0] = typeof(RectTransform);
        components.CopyTo(all, 1);
        return new GameObject(name, all);
    }

    private static Text ChildText(Transform parent, string name, string value, int size, TextAnchor alignment, Vector2 min, Vector2 max)
    {
        GameObject go = UiObject(name, typeof(Text));
        go.transform.SetParent(parent, false);
        SetAnchors(go.GetComponent<RectTransform>(), min, max);

        Text text = go.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = value;
        text.fontSize = size;
        text.alignment = alignment;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    private static Button ChildButton(Transform parent, string name, string label, Vector2 min, Vector2 max)
    {
        GameObject go = UiObject(name, typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        SetAnchors(go.GetComponent<RectTransform>(), min, max);

        Image image = go.GetComponent<Image>();
        image.color = new Color(0.30f, 0.25f, 0.14f, 1f);

        Button button = go.GetComponent<Button>();
        Text text = ChildText(go.transform, "Text", label, 21, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one);
        text.fontStyle = FontStyle.Bold;
        return button;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void SetAnchors(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
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
