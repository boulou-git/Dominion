using System;
using System.Linq;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Runtime Escape menu for the Game scene.
/// Reprendre closes the local menu. The host also controls the authoritative match pause
/// and can close the room for everybody.
/// </summary>
public sealed class GamePauseMenu : MonoBehaviour
{
    private const string RootName = "DominionPauseMenu";
    private GameObject _panel;
    private Button _pauseButton;
    private Text _pauseButtonText;
    private Text _statusText;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!string.Equals(scene.name, "Game", StringComparison.Ordinal))
            return;

        if (GameObject.Find(RootName) != null)
            return;

        GameObject root = new GameObject(RootName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(GamePauseMenu));
        SceneManager.MoveGameObjectToScene(root, scene);

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 5000;

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        if (FindFirstObjectByType<EventSystem>() == null)
        {
            GameObject eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            SceneManager.MoveGameObjectToScene(eventSystem, scene);
        }
    }

    private void Awake()
    {
        Build();
        NetworkGameState.StateChanged += OnGameStateChanged;
        Refresh();
        _panel.SetActive(false);
    }

    private void OnDestroy()
    {
        NetworkGameState.StateChanged -= OnGameStateChanged;
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            _panel.SetActive(!_panel.activeSelf);
            if (_panel.activeSelf)
                Refresh();
        }
    }

    private void OnGameStateChanged(GameStateSnapshot state)
    {
        if (_panel != null && _panel.activeSelf)
            Refresh();
    }

    private void Build()
    {
        RectTransform root = GetComponent<RectTransform>();
        Stretch(root);

        _panel = CreatePanel("Backdrop", root, Vector2.zero, Vector2.one, new Color(0f, 0f, 0f, 0.72f)).gameObject;
        RectTransform window = CreatePanel("Window", _panel.GetComponent<RectTransform>(), new Vector2(0.34f, 0.20f), new Vector2(0.66f, 0.80f), new Color(0.12f, 0.12f, 0.12f, 1f));

        Text title = CreateText("Title", window, "MENU", 38, TextAnchor.MiddleCenter);
        SetAnchors(title.rectTransform, new Vector2(0.08f, 0.82f), new Vector2(0.92f, 0.96f));

        _statusText = CreateText("Status", window, string.Empty, 20, TextAnchor.MiddleCenter);
        SetAnchors(_statusText.rectTransform, new Vector2(0.08f, 0.69f), new Vector2(0.92f, 0.81f));

        CreateButton("Reprendre", window, 0.54f, ResumeMenu);

        _pauseButton = CreateButton("Mettre en pause", window, 0.40f, ToggleHostPause);
        _pauseButtonText = _pauseButton.GetComponentInChildren<Text>();

        CreateButton("Quitter la partie", window, 0.26f, LeaveGame).gameObject.name = "LeaveGameButton";
        CreateButton("Quitter et fermer la partie", window, 0.12f, CloseGameAsHost).gameObject.name = "CloseGameButton";
    }

    private void Refresh()
    {
        bool isHost = PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient;
        bool manuallyPaused = NetworkGameState.State != null && NetworkGameState.State.ManualPauseRequested;

        if (_pauseButton != null)
        {
            _pauseButton.gameObject.SetActive(isHost);
            if (_pauseButtonText != null)
                _pauseButtonText.text = manuallyPaused ? "Reprendre la partie" : "Mettre en pause";
        }

        Transform leave = _panel != null ? _panel.transform.Find("Window/LeaveGameButton") : null;
        if (leave != null)
            leave.gameObject.SetActive(!isHost);

        Transform close = _panel != null ? _panel.transform.Find("Window/CloseGameButton") : null;
        if (close != null)
            close.gameObject.SetActive(isHost);

        if (_statusText != null)
        {
            if (!PhotonNetwork.InRoom)
                _statusText.text = "Hors ligne";
            else if (NetworkGameState.IsPaused)
                _statusText.text = NetworkGameState.State != null ? NetworkGameState.State.PauseReason : "Partie en pause";
            else
                _statusText.text = isHost ? "Vous êtes l’hôte" : "Partie en cours";
        }
    }

    private void ResumeMenu()
    {
        _panel.SetActive(false);
    }

    private void ToggleHostPause()
    {
        if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient || NetworkGameState.State == null)
            return;

        NetworkGameState.SetManualPause(!NetworkGameState.State.ManualPauseRequested);
        Refresh();
    }

    private void LeaveGame()
    {
        if (!PhotonNetwork.InRoom)
            return;

        PhotonNetwork.LeaveRoom();
    }

    private void CloseGameAsHost()
    {
        if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient || PhotonNetwork.CurrentRoom == null)
            return;

        PhotonNetwork.CurrentRoom.IsOpen = false;
        PhotonNetwork.CurrentRoom.IsVisible = false;

        Player[] others = PhotonNetwork.PlayerListOthers.ToArray();
        foreach (Player player in others)
            PhotonNetwork.CloseConnection(player);

        PhotonNetwork.LeaveRoom();
    }

    private static RectTransform CreatePanel(string name, RectTransform parent, Vector2 min, Vector2 max, Color color)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Image));
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        obj.GetComponent<Image>().color = color;
        return rect;
    }

    private static Text CreateText(string name, RectTransform parent, string value, int size, TextAnchor alignment)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Text));
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        Text text = obj.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = value;
        text.fontSize = size;
        text.alignment = alignment;
        text.color = Color.white;
        return text;
    }

    private static Button CreateButton(string label, RectTransform parent, float centerY, Action action)
    {
        GameObject obj = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0.18f, centerY - 0.055f);
        rect.anchorMax = new Vector2(0.82f, centerY + 0.055f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image image = obj.GetComponent<Image>();
        image.color = new Color(0.24f, 0.24f, 0.24f, 1f);

        Button button = obj.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => action());

        Text text = CreateText("Text", rect, label, 23, TextAnchor.MiddleCenter);
        Stretch(text.rectTransform, 8f);
        return button;
    }

    private static void SetAnchors(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void Stretch(RectTransform rect, float inset = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }
}
