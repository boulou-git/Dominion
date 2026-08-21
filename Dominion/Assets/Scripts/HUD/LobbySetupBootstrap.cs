using System;
using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>
/// Adds the host game-setup screen to the existing Lobby scene without replacing the
/// connection UI. It becomes visible only once the local client is inside a Photon room.
/// </summary>
public static class LobbySetupBootstrap
{
    private const string LobbySceneName = "Lobby";
    private const string RootName = "DominionLobbySetupUI";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!string.Equals(scene.name, LobbySceneName, StringComparison.Ordinal))
            return;

        if (GameObject.Find(RootName) != null)
            return;

        GameObject root = new GameObject(RootName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(LobbySetupController));
        SceneManager.MoveGameObjectToScene(root, scene);

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 950;

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        EnsureEventSystem(scene);
    }

    private static void EnsureEventSystem(Scene scene)
    {
        if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() != null)
            return;

        GameObject eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        SceneManager.MoveGameObjectToScene(eventSystem, scene);
    }
}

public sealed class LobbySetupController : MonoBehaviourPunCallbacks
{
    private readonly List<GameObject> _extensionRows = new List<GameObject>();
    private readonly List<GameObject> _cardRows = new List<GameObject>();

    private Canvas _canvas;
    private GraphicRaycaster _raycaster;
    private RectTransform _extensionsRoot;
    private RectTransform _cardsRoot;
    private Text _cardsTitle;
    private Text _summaryText;
    private Text _hostStatusText;
    private Button _selectAllButton;
    private Button _selectNoneButton;
    private Button _startButton;

    private GameSetupConfig _config;
    private string _viewedExtensionId;
    private bool _lastVisible;
    private bool _lastHostState;
    private float _nextPresenceRefresh;

    private static readonly Color Background = new Color(0.07f, 0.07f, 0.07f, 1f);
    private static readonly Color Panel = new Color(0.12f, 0.12f, 0.12f, 1f);
    private static readonly Color PanelAlt = new Color(0.17f, 0.17f, 0.17f, 1f);
    private static readonly Color ButtonColor = new Color(0.23f, 0.23f, 0.23f, 1f);
    private static readonly Color SelectedColor = new Color(0.30f, 0.36f, 0.40f, 1f);
    private static readonly Color EnabledColor = new Color(0.30f, 0.27f, 0.17f, 1f);

    private void Awake()
    {
        _canvas = GetComponent<Canvas>();
        _raycaster = GetComponent<GraphicRaycaster>();
        BuildLayout();
        ExtensionCatalog.Reload();
        _config = RoomGameSetup.ReadCurrent();
        PickInitialExtension();
        RefreshVisibility(true);
    }

    private void Update()
    {
        if (Time.unscaledTime < _nextPresenceRefresh)
            return;

        _nextPresenceRefresh = Time.unscaledTime + 0.25f;
        RefreshVisibility(false);
    }

    public override void OnJoinedRoom()
    {
        ExtensionCatalog.Reload();
        _config = RoomGameSetup.ReadCurrent();
        PickInitialExtension();

        if (PhotonNetwork.IsMasterClient && PhotonNetwork.CurrentRoom != null && !PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey(RoomGameSetup.RoomPropertyKey))
            RoomGameSetup.Publish(_config);

        RefreshVisibility(true);
    }

    public override void OnLeftRoom()
    {
        RefreshVisibility(true);
    }

    public override void OnMasterClientSwitched(Player newMasterClient)
    {
        _config = RoomGameSetup.ReadCurrent();
        if (PhotonNetwork.IsMasterClient)
            RoomGameSetup.Publish(_config);
        RefreshVisibility(true);
    }

    public override void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
    {
        if (propertiesThatChanged == null || !propertiesThatChanged.ContainsKey(RoomGameSetup.RoomPropertyKey))
            return;

        _config = RoomGameSetup.ReadCurrent();
        RefreshAll();
    }

    private void RefreshVisibility(bool force)
    {
        bool visible = PhotonNetwork.InRoom && !NetworkGameState.IsStarted;
        bool isHost = visible && PhotonNetwork.IsMasterClient;

        if (!force && visible == _lastVisible && isHost == _lastHostState)
            return;

        _lastVisible = visible;
        _lastHostState = isHost;
        _canvas.enabled = visible;
        _raycaster.enabled = visible;

        if (!visible)
            return;

        if (_config == null)
            _config = RoomGameSetup.ReadCurrent();

        PickInitialExtension();
        RefreshAll();
    }

    private void BuildLayout()
    {
        RectTransform root = GetComponent<RectTransform>();
        Stretch(root);
        AddImage(gameObject, Background);

        Text title = CreateText("Title", root, "PRÉPARATION DE LA PARTIE", 34, TextAnchor.MiddleCenter);
        SetAnchors(title.rectTransform, new Vector2(0f, 0.91f), new Vector2(1f, 0.99f), 8f);

        _hostStatusText = CreateText("HostStatus", root, string.Empty, 18, TextAnchor.MiddleCenter);
        SetAnchors(_hostStatusText.rectTransform, new Vector2(0.02f, 0.865f), new Vector2(0.98f, 0.91f), 4f);

        RectTransform left = CreatePanel("ExtensionsPanel", root, new Vector2(0.025f, 0.10f), new Vector2(0.27f, 0.855f), Panel);
        RectTransform center = CreatePanel("CardsPanel", root, new Vector2(0.285f, 0.10f), new Vector2(0.73f, 0.855f), Panel);
        RectTransform right = CreatePanel("SummaryPanel", root, new Vector2(0.745f, 0.10f), new Vector2(0.975f, 0.855f), Panel);

        BuildExtensions(left);
        BuildCards(center);
        BuildSummary(right);
    }

    private void BuildExtensions(RectTransform panel)
    {
        Text header = CreateText("Header", panel, "EXTENSIONS", 24, TextAnchor.MiddleCenter);
        SetAnchors(header.rectTransform, new Vector2(0f, 0.90f), new Vector2(1f, 1f), 8f);

        _extensionsRoot = CreateScrollContent("ExtensionsScroll", panel, new Vector2(0.04f, 0.04f), new Vector2(0.96f, 0.89f));
    }

    private void BuildCards(RectTransform panel)
    {
        _cardsTitle = CreateText("Header", panel, "CARTES", 24, TextAnchor.MiddleCenter);
        SetAnchors(_cardsTitle.rectTransform, new Vector2(0f, 0.90f), new Vector2(1f, 1f), 8f);

        RectTransform actions = CreatePanel("Actions", panel, new Vector2(0.04f, 0.82f), new Vector2(0.96f, 0.90f), Panel);
        HorizontalLayoutGroup actionLayout = actions.gameObject.AddComponent<HorizontalLayoutGroup>();
        actionLayout.spacing = 8f;
        actionLayout.childAlignment = TextAnchor.MiddleCenter;
        actionLayout.childControlHeight = true;
        actionLayout.childControlWidth = false;
        actionLayout.childForceExpandWidth = false;

        _selectAllButton = CreateButton("Tout sélectionner", actions, 190f, SelectAllCards);
        _selectNoneButton = CreateButton("Tout désélectionner", actions, 210f, SelectNoCards);

        _cardsRoot = CreateScrollContent("CardsScroll", panel, new Vector2(0.04f, 0.04f), new Vector2(0.96f, 0.80f));
    }

    private void BuildSummary(RectTransform panel)
    {
        Text header = CreateText("Header", panel, "RÉSUMÉ", 24, TextAnchor.MiddleCenter);
        SetAnchors(header.rectTransform, new Vector2(0f, 0.90f), new Vector2(1f, 1f), 8f);

        _summaryText = CreateText("Summary", panel, string.Empty, 19, TextAnchor.UpperLeft);
        SetAnchors(_summaryText.rectTransform, new Vector2(0.06f, 0.24f), new Vector2(0.94f, 0.88f), 8f);

        _startButton = CreateButton("Lancer la partie", panel, 260f, StartGame);
        RectTransform rect = _startButton.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.10f, 0.07f);
        rect.anchorMax = new Vector2(0.90f, 0.19f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private void RefreshAll()
    {
        RebuildExtensions();
        RebuildCards();
        RefreshSummary();
    }

    private void RebuildExtensions()
    {
        ClearObjects(_extensionRows);
        bool isHost = PhotonNetwork.IsMasterClient;

        foreach (ExtensionPackageData extension in ExtensionCatalog.All)
        {
            if (extension == null)
                continue;

            ExtensionSetupSelection selection = RoomGameSetup.FindExtension(_config, extension.id);
            bool enabled = selection != null && selection.enabled;
            bool viewed = string.Equals(_viewedExtensionId, extension.id, StringComparison.OrdinalIgnoreCase);

            RectTransform row = CreatePanel("Extension_" + extension.id, _extensionsRoot, Vector2.zero, Vector2.one, viewed ? SelectedColor : enabled ? EnabledColor : PanelAlt);
            LayoutElement rowLayout = row.gameObject.AddComponent<LayoutElement>();
            rowLayout.preferredHeight = 58f;
            rowLayout.minHeight = 58f;

            HorizontalLayoutGroup layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 7, 7);
            layout.spacing = 8f;
            layout.childControlHeight = true;
            layout.childControlWidth = false;
            layout.childForceExpandWidth = false;

            string extensionId = extension.id;
            Toggle toggle = CreateToggle(row, 38f, enabled, value => SetExtensionEnabled(extensionId, value));
            toggle.interactable = isHost;

            Button select = CreateButton(string.IsNullOrEmpty(extension.name) ? extension.id : extension.name, row, 300f, () => ViewExtension(extensionId));
            _extensionRows.Add(row.gameObject);
        }
    }

    private void RebuildCards()
    {
        ClearObjects(_cardRows);
        ExtensionPackageData extension = ExtensionCatalog.Find(_viewedExtensionId);
        ExtensionSetupSelection selection = RoomGameSetup.FindExtension(_config, _viewedExtensionId);

        if (extension == null)
        {
            _cardsTitle.text = "CARTES";
            return;
        }

        _cardsTitle.text = "CARTES — " + (string.IsNullOrEmpty(extension.name) ? extension.id : extension.name);
        bool extensionEnabled = selection != null && selection.enabled;
        bool canEdit = PhotonNetwork.IsMasterClient && extensionEnabled;
        _selectAllButton.interactable = canEdit;
        _selectNoneButton.interactable = canEdit;

        if (extension.cards == null)
            return;

        foreach (ExtensionCardData card in extension.cards)
        {
            if (card == null || string.IsNullOrEmpty(card.id))
                continue;

            bool selected = selection != null && selection.selectedCardIds != null && selection.selectedCardIds.Contains(card.id);
            RectTransform row = CreatePanel("Card_" + card.id, _cardsRoot, Vector2.zero, Vector2.one, PanelAlt);
            LayoutElement rowLayout = row.gameObject.AddComponent<LayoutElement>();
            rowLayout.preferredHeight = 52f;
            rowLayout.minHeight = 52f;

            HorizontalLayoutGroup layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 6, 6);
            layout.spacing = 10f;
            layout.childControlHeight = true;
            layout.childControlWidth = false;
            layout.childForceExpandWidth = false;

            string cardId = card.id;
            Toggle toggle = CreateToggle(row, 38f, selected, value => SetCardSelected(cardId, value));
            toggle.interactable = canEdit;

            string types = card.types == null ? string.Empty : string.Join(" – ", card.types);
            Text label = CreateText("Label", row, card.name + "    " + card.cost + "    " + types, 18, TextAnchor.MiddleLeft);
            LayoutElement labelLayout = label.gameObject.AddComponent<LayoutElement>();
            labelLayout.preferredWidth = 560f;
            labelLayout.flexibleWidth = 1f;
            _cardRows.Add(row.gameObject);
        }
    }

    private void RefreshSummary()
    {
        bool isHost = PhotonNetwork.IsMasterClient;
        int enabledExtensions = 0;
        int selectedCards = 0;
        List<string> lines = new List<string>();

        if (_config != null && _config.extensions != null)
        {
            foreach (ExtensionSetupSelection selection in _config.extensions)
            {
                if (selection == null || !selection.enabled)
                    continue;

                ExtensionPackageData extension = ExtensionCatalog.Find(selection.extensionId);
                int count = selection.selectedCardIds == null ? 0 : selection.selectedCardIds.Count;
                enabledExtensions++;
                selectedCards += count;
                lines.Add("• " + (extension != null ? extension.name : selection.extensionId) + " : " + count + " cartes");
            }
        }

        _hostStatusText.text = isHost
            ? "Vous êtes l’host : choisissez les extensions et les cartes autorisées pour cette partie."
            : "Configuration définie par l’host — vous pouvez consulter les extensions et les cartes.";

        string detail = lines.Count > 0 ? string.Join("\n", lines) : "Aucune extension activée.";
        _summaryText.text = detail + "\n\nExtensions actives : " + enabledExtensions + "\nCartes autorisées : " + selectedCards + "\n\nLa sélection représente le pool disponible. Le choix des piles Royaume sera géré ensuite par le setup de partie.";

        _startButton.gameObject.SetActive(isHost);
        _startButton.interactable = isHost && selectedCards > 0;
    }

    private void PickInitialExtension()
    {
        if (ExtensionCatalog.All.Count == 0)
        {
            _viewedExtensionId = null;
            return;
        }

        if (!string.IsNullOrEmpty(_viewedExtensionId) && ExtensionCatalog.Find(_viewedExtensionId) != null)
            return;

        ExtensionSetupSelection enabled = null;
        if (_config != null && _config.extensions != null)
            enabled = _config.extensions.Find(e => e != null && e.enabled && ExtensionCatalog.Find(e.extensionId) != null);

        _viewedExtensionId = enabled != null ? enabled.extensionId : ExtensionCatalog.All[0].id;
    }

    private void ViewExtension(string extensionId)
    {
        _viewedExtensionId = extensionId;
        RefreshAll();
    }

    private void SetExtensionEnabled(string extensionId, bool enabled)
    {
        if (!PhotonNetwork.IsMasterClient)
            return;

        ExtensionSetupSelection selection = RoomGameSetup.FindExtension(_config, extensionId);
        if (selection == null)
            return;

        selection.enabled = enabled;
        PublishAndRefresh();
    }

    private void SetCardSelected(string cardId, bool selected)
    {
        if (!PhotonNetwork.IsMasterClient)
            return;

        ExtensionSetupSelection extension = RoomGameSetup.FindExtension(_config, _viewedExtensionId);
        if (extension == null || !extension.enabled)
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

        PublishAndRefresh();
    }

    private void SelectAllCards()
    {
        SetAllCards(true);
    }

    private void SelectNoCards()
    {
        SetAllCards(false);
    }

    private void SetAllCards(bool selected)
    {
        if (!PhotonNetwork.IsMasterClient)
            return;

        ExtensionPackageData package = ExtensionCatalog.Find(_viewedExtensionId);
        ExtensionSetupSelection extension = RoomGameSetup.FindExtension(_config, _viewedExtensionId);
        if (package == null || extension == null || !extension.enabled)
            return;

        extension.selectedCardIds.Clear();
        if (selected && package.cards != null)
        {
            foreach (ExtensionCardData card in package.cards)
            {
                if (card != null && !string.IsNullOrEmpty(card.id))
                    extension.selectedCardIds.Add(card.id);
            }
        }

        // Publish directly: an intentionally empty list must stay empty.
        PublishCurrentWithoutNormalisingSelection();
        RefreshAll();
    }

    private void PublishAndRefresh()
    {
        RoomGameSetup.Publish(_config);
        RefreshAll();
    }

    private void PublishCurrentWithoutNormalisingSelection()
    {
        // RoomGameSetup.Publish keeps the configuration shape stable. Empty card lists are
        // meaningful after the host explicitly clicks "Tout désélectionner".
        RoomGameSetup.Publish(_config);
    }

    private void StartGame()
    {
        if (!PhotonNetwork.IsMasterClient || RoomGameSetup.CountSelectedCards(_config) <= 0)
            return;

        RoomGameSetup.Publish(_config);
        if (RoomConnectionHandler.Instance != null)
            RoomConnectionHandler.Instance.StartGameMaster();
    }

    private static RectTransform CreateScrollContent(string name, RectTransform parent, Vector2 anchorMin, Vector2 anchorMax)
    {
        RectTransform viewport = CreatePanel(name + "Viewport", parent, anchorMin, anchorMax, PanelAlt);
        viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;

        GameObject contentObject = new GameObject(name + "Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        RectTransform content = contentObject.GetComponent<RectTransform>();
        content.SetParent(viewport, false);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.offsetMin = Vector2.zero;
        content.offsetMax = Vector2.zero;

        VerticalLayoutGroup layout = contentObject.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(6, 6, 6, 6);
        layout.spacing = 5f;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;

        ContentSizeFitter fitter = contentObject.GetComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.content = content;
        scroll.viewport = viewport;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 28f;

        return content;
    }

    private static Toggle CreateToggle(RectTransform parent, float width, bool value, Action<bool> onChanged)
    {
        GameObject go = new GameObject("Toggle", typeof(RectTransform), typeof(Image), typeof(Toggle), typeof(LayoutElement));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);

        LayoutElement layout = go.GetComponent<LayoutElement>();
        layout.preferredWidth = width;
        layout.minWidth = width;
        layout.preferredHeight = 36f;

        Image background = go.GetComponent<Image>();
        background.color = new Color(0.08f, 0.08f, 0.08f, 1f);

        Text mark = CreateText("Mark", rect, "X", 23, TextAnchor.MiddleCenter);
        Stretch(mark.rectTransform, 3f);

        Toggle toggle = go.GetComponent<Toggle>();
        toggle.targetGraphic = background;
        toggle.graphic = mark;
        toggle.SetIsOnWithoutNotify(value);
        if (onChanged != null)
            toggle.onValueChanged.AddListener(v => onChanged(v));
        return toggle;
    }

    private static Button CreateButton(string label, RectTransform parent, float preferredWidth, Action onClick)
    {
        GameObject go = new GameObject(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        go.GetComponent<Image>().color = ButtonColor;

        LayoutElement layout = go.GetComponent<LayoutElement>();
        layout.preferredWidth = preferredWidth;
        layout.minWidth = preferredWidth;

        Button button = go.GetComponent<Button>();
        if (onClick != null)
            button.onClick.AddListener(() => onClick());

        Text text = CreateText("Label", rect, label, 18, TextAnchor.MiddleCenter);
        Stretch(text.rectTransform, 6f);
        return button;
    }

    private static RectTransform CreatePanel(string name, RectTransform parent, Vector2 anchorMin, Vector2 anchorMax, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        go.GetComponent<Image>().color = color;
        return rect;
    }

    private static Text CreateText(string name, RectTransform parent, string value, int size, TextAnchor alignment)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        Text text = go.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = value;
        text.fontSize = size;
        text.alignment = alignment;
        text.color = Color.white;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }

    private static void AddImage(GameObject go, Color color)
    {
        Image image = go.GetComponent<Image>();
        if (image == null)
            image = go.AddComponent<Image>();
        image.color = color;
    }

    private static void Stretch(RectTransform rect, float inset = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }

    private static void SetAnchors(RectTransform rect, Vector2 min, Vector2 max, float inset)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }

    private static void ClearObjects(List<GameObject> objects)
    {
        foreach (GameObject go in objects)
        {
            if (go != null)
                UnityEngine.Object.Destroy(go);
        }
        objects.Clear();
    }
}
