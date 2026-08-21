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
/// Second layer of the pre-game flow:
/// - during Selection, only the host sees the detailed setup UI; clients see a waiting screen;
/// - the host validates the pool, which chooses exactly 10 Kingdom cards;
/// - during Reveal, every player sees the same 10 cards before the host starts the match.
/// </summary>
public static class PreGameRevealBootstrap
{
    private const string LobbySceneName = "Lobby";
    private const string RootName = "DominionPreGameFlowUI";

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

        GameObject root = new GameObject(RootName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(PreGameRevealController));
        SceneManager.MoveGameObjectToScene(root, scene);

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 975;

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() == null)
        {
            GameObject eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            SceneManager.MoveGameObjectToScene(eventSystem, scene);
        }
    }
}

public sealed class PreGameRevealController : MonoBehaviourPunCallbacks
{
    private Canvas _canvas;
    private GraphicRaycaster _raycaster;
    private RectTransform _root;
    private GameObject _waitingScreen;
    private GameObject _revealScreen;
    private GameObject _hostValidateButtonRoot;
    private Text _waitingText;
    private Text _revealStatus;
    private Text _validationStatus;
    private RectTransform _kingdomGrid;
    private Button _validateButton;
    private Button _startButton;
    private readonly List<GameObject> _kingdomTiles = new List<GameObject>();

    private float _nextRefresh;
    private string _lastStage;
    private bool _lastHost;
    private bool _lastInRoom;

    private static readonly Color Background = new Color(0.065f, 0.065f, 0.065f, 1f);
    private static readonly Color Panel = new Color(0.13f, 0.13f, 0.13f, 1f);
    private static readonly Color Tile = new Color(0.18f, 0.18f, 0.18f, 1f);
    private static readonly Color ButtonColor = new Color(0.25f, 0.25f, 0.25f, 1f);

    private void Awake()
    {
        _canvas = GetComponent<Canvas>();
        _raycaster = GetComponent<GraphicRaycaster>();
        _root = GetComponent<RectTransform>();
        Stretch(_root);
        BuildWaitingScreen();
        BuildHostValidationOverlay();
        BuildRevealScreen();
        Refresh(true);
    }

    private void Update()
    {
        if (Time.unscaledTime < _nextRefresh)
            return;

        _nextRefresh = Time.unscaledTime + 0.2f;
        Refresh(false);
    }

    public override void OnJoinedRoom()
    {
        ExtensionCatalog.Reload();
        Refresh(true);
    }

    public override void OnLeftRoom()
    {
        Refresh(true);
    }

    public override void OnMasterClientSwitched(Player newMasterClient)
    {
        Refresh(true);
    }

    public override void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
    {
        if (propertiesThatChanged != null && propertiesThatChanged.ContainsKey(RoomGameSetup.RoomPropertyKey))
            Refresh(true);
    }

    private void Refresh(bool force)
    {
        bool inRoom = PhotonNetwork.InRoom && !NetworkGameState.IsStarted;
        bool isHost = inRoom && PhotonNetwork.IsMasterClient;
        GameSetupConfig config = inRoom ? RoomGameSetup.ReadCurrent() : null;
        string stage = config != null && !string.IsNullOrEmpty(config.stage) ? config.stage : RoomGameSetup.SelectionStage;

        if (!force && inRoom == _lastInRoom && isHost == _lastHost && string.Equals(stage, _lastStage, StringComparison.Ordinal))
        {
            if (stage == RoomGameSetup.SelectionStage && isHost)
                RefreshValidationState(config);
            return;
        }

        _lastInRoom = inRoom;
        _lastHost = isHost;
        _lastStage = stage;

        _canvas.enabled = inRoom;
        _raycaster.enabled = inRoom;
        if (!inRoom)
            return;

        bool selecting = stage == RoomGameSetup.SelectionStage;
        bool revealing = stage == RoomGameSetup.RevealStage;

        _waitingScreen.SetActive(selecting && !isHost);
        _hostValidateButtonRoot.SetActive(selecting && isHost);
        _revealScreen.SetActive(revealing);

        if (selecting && !isHost)
        {
            _waitingText.text = "EN ATTENTE DE L’HÔTE\n\nL’hôte choisit actuellement les extensions et les cartes disponibles pour cette partie.\n\nLes 10 cartes Royaume seront révélées ici avant le démarrage.";
        }

        if (selecting && isHost)
            RefreshValidationState(config);

        if (revealing)
            RebuildKingdom(config);
    }

    private void RefreshValidationState(GameSetupConfig config)
    {
        int count = RoomGameSetup.CountSelectedCards(config);
        bool enough = count >= RoomGameSetup.KingdomCardCount;
        _validateButton.interactable = enough;
        _validationStatus.text = enough
            ? count + " cartes dans le pool — 10 seront tirées au hasard."
            : count + "/10 cartes minimum sélectionnées.";
    }

    private void ValidateSelection()
    {
        if (!PhotonNetwork.IsMasterClient)
            return;

        GameSetupConfig config = RoomGameSetup.ReadCurrent();
        if (!RoomGameSetup.FinaliseKingdom(config))
            RefreshValidationState(config);
    }

    private void StartGame()
    {
        if (!PhotonNetwork.IsMasterClient || RoomConnectionHandler.Instance == null)
            return;

        GameSetupConfig config = RoomGameSetup.ReadCurrent();
        if (config.stage != RoomGameSetup.RevealStage || config.kingdomCardIds == null || config.kingdomCardIds.Count != RoomGameSetup.KingdomCardCount)
            return;

        RoomConnectionHandler.Instance.StartGameMaster();
    }

    private void RebuildKingdom(GameSetupConfig config)
    {
        ClearObjects(_kingdomTiles);
        int count = config != null && config.kingdomCardIds != null ? config.kingdomCardIds.Count : 0;
        _revealStatus.text = count == RoomGameSetup.KingdomCardCount
            ? "Voici les 10 cartes Royaume de cette partie."
            : "Configuration incomplète : " + count + "/10 cartes.";

        if (config == null || config.kingdomCardIds == null)
            return;

        for (int i = 0; i < config.kingdomCardIds.Count; i++)
        {
            string cardRef = config.kingdomCardIds[i];
            ExtensionPackageData extension;
            ExtensionCardData card;
            bool resolved = RoomGameSetup.TryResolveCard(cardRef, out extension, out card);

            RectTransform tile = CreatePanel("KingdomCard_" + i, _kingdomGrid, Tile);
            LayoutElement layoutElement = tile.gameObject.AddComponent<LayoutElement>();
            layoutElement.preferredWidth = 290f;
            layoutElement.preferredHeight = 265f;

            string title = resolved ? card.name : cardRef;
            string types = resolved && card.types != null ? string.Join(" – ", card.types) : string.Empty;
            string extensionName = resolved && extension != null ? extension.name : string.Empty;
            string text = resolved
                ? title + "\n\nCoût : " + card.cost + "\n" + types + "\n\n" + extensionName
                : title;

            Text label = CreateText("Label", tile, text, 19, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform, 12f);
            _kingdomTiles.Add(tile.gameObject);
        }

        bool host = PhotonNetwork.IsMasterClient;
        _startButton.gameObject.SetActive(host);
        _startButton.interactable = host && count == RoomGameSetup.KingdomCardCount;
    }

    private void BuildWaitingScreen()
    {
        _waitingScreen = CreatePanel("WaitingForHost", _root, Background).gameObject;
        Stretch(_waitingScreen.GetComponent<RectTransform>());

        _waitingText = CreateText("WaitingText", _waitingScreen.GetComponent<RectTransform>(), string.Empty, 30, TextAnchor.MiddleCenter);
        SetAnchors(_waitingText.rectTransform, new Vector2(0.18f, 0.25f), new Vector2(0.82f, 0.75f));
    }

    private void BuildHostValidationOverlay()
    {
        _hostValidateButtonRoot = new GameObject("HostValidateOverlay", typeof(RectTransform));
        RectTransform rect = _hostValidateButtonRoot.GetComponent<RectTransform>();
        rect.SetParent(_root, false);
        rect.anchorMin = new Vector2(0.755f, 0.11f);
        rect.anchorMax = new Vector2(0.965f, 0.255f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image blocker = _hostValidateButtonRoot.AddComponent<Image>();
        blocker.color = Panel;
        blocker.raycastTarget = true;

        _validationStatus = CreateText("Status", rect, string.Empty, 16, TextAnchor.MiddleCenter);
        SetAnchors(_validationStatus.rectTransform, new Vector2(0.04f, 0.52f), new Vector2(0.96f, 0.96f));

        _validateButton = CreateButton("Valider la sélection", rect, ValidateSelection);
        RectTransform buttonRect = _validateButton.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0.08f, 0.08f);
        buttonRect.anchorMax = new Vector2(0.92f, 0.50f);
        buttonRect.offsetMin = Vector2.zero;
        buttonRect.offsetMax = Vector2.zero;
    }

    private void BuildRevealScreen()
    {
        _revealScreen = CreatePanel("KingdomReveal", _root, Background).gameObject;
        Stretch(_revealScreen.GetComponent<RectTransform>());
        RectTransform reveal = _revealScreen.GetComponent<RectTransform>();

        Text title = CreateText("Title", reveal, "CARTES DE LA PARTIE", 38, TextAnchor.MiddleCenter);
        SetAnchors(title.rectTransform, new Vector2(0.05f, 0.90f), new Vector2(0.95f, 0.98f));

        _revealStatus = CreateText("Status", reveal, string.Empty, 21, TextAnchor.MiddleCenter);
        SetAnchors(_revealStatus.rectTransform, new Vector2(0.08f, 0.845f), new Vector2(0.92f, 0.90f));

        RectTransform gridPanel = CreatePanel("GridPanel", reveal, Panel);
        gridPanel.anchorMin = new Vector2(0.06f, 0.15f);
        gridPanel.anchorMax = new Vector2(0.94f, 0.83f);
        gridPanel.offsetMin = Vector2.zero;
        gridPanel.offsetMax = Vector2.zero;

        _kingdomGrid = new GameObject("KingdomGrid", typeof(RectTransform), typeof(GridLayoutGroup)).GetComponent<RectTransform>();
        _kingdomGrid.SetParent(gridPanel, false);
        Stretch(_kingdomGrid, 20f);
        GridLayoutGroup grid = _kingdomGrid.GetComponent<GridLayoutGroup>();
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 5;
        grid.cellSize = new Vector2(290f, 265f);
        grid.spacing = new Vector2(20f, 18f);
        grid.childAlignment = TextAnchor.MiddleCenter;

        _startButton = CreateButton("Démarrer la partie", reveal, StartGame);
        RectTransform startRect = _startButton.GetComponent<RectTransform>();
        startRect.anchorMin = new Vector2(0.39f, 0.045f);
        startRect.anchorMax = new Vector2(0.61f, 0.115f);
        startRect.offsetMin = Vector2.zero;
        startRect.offsetMax = Vector2.zero;
    }

    private static RectTransform CreatePanel(string name, RectTransform parent, Color color)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Image));
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        Image image = obj.GetComponent<Image>();
        image.color = color;
        return rect;
    }

    private static Text CreateText(string name, RectTransform parent, string text, int fontSize, TextAnchor anchor)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Text));
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        Text label = obj.GetComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.text = text;
        label.fontSize = fontSize;
        label.alignment = anchor;
        label.color = Color.white;
        label.raycastTarget = false;
        return label;
    }

    private static Button CreateButton(string label, RectTransform parent, Action action)
    {
        RectTransform panel = CreatePanel("Button_" + label, parent, ButtonColor);
        Button button = panel.gameObject.AddComponent<Button>();
        button.targetGraphic = panel.GetComponent<Image>();
        if (action != null)
            button.onClick.AddListener(() => action());

        Text text = CreateText("Text", panel, label, 20, TextAnchor.MiddleCenter);
        Stretch(text.rectTransform, 4f);
        return button;
    }

    private static void ClearObjects(List<GameObject> objects)
    {
        foreach (GameObject obj in objects)
        {
            if (obj != null)
                UnityEngine.Object.Destroy(obj);
        }
        objects.Clear();
    }

    private static void Stretch(RectTransform rect, float inset = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }

    private static void SetAnchors(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
