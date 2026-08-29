using System;
using ExitGames.Client.Photon;
using Photon.Pun;
using UnityEngine;
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
    private const string PrefabResourcePath = "UI/GamePauseMenu";
    private const string StatePropertyKey = "dominion.gameState.v1";

    private GameObject _panel;
    private Button _pauseButton;
    private Text _pauseButtonText;
    private Button _finishTestButton;
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

        GameObject prefab = Resources.Load<GameObject>(PrefabResourcePath);
        if (prefab == null)
        {
            Debug.LogError("GamePauseMenu prefab missing at Resources/UI/GamePauseMenu.");
            return;
        }

        GameObject root = Instantiate(prefab);
        root.name = RootName;
        SceneManager.MoveGameObjectToScene(root, scene);

        SingleEventSystemGuard.EnsureExactlyOne();
    }

    private void Awake()
    {
        if (!BindPrefab())
        {
            enabled = false;
            return;
        }
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

    private bool BindPrefab()
    {
        Transform backdrop = transform.Find("Backdrop");
        Transform window = backdrop != null ? backdrop.Find("Window") : null;
        _panel = backdrop != null ? backdrop.gameObject : null;
        _statusText = window != null ? window.Find("Status")?.GetComponent<Text>() : null;
        Button resume = window != null ? window.Find("ResumeButton")?.GetComponent<Button>() : null;
        _pauseButton = window != null ? window.Find("PauseButton")?.GetComponent<Button>() : null;
        _pauseButtonText = _pauseButton != null ? _pauseButton.GetComponentInChildren<Text>() : null;
        _finishTestButton = window != null ? window.Find("FinishGameTestButton")?.GetComponent<Button>() : null;
        Button leave = window != null ? window.Find("LeaveGameButton")?.GetComponent<Button>() : null;
        Button close = window != null ? window.Find("CloseGameButton")?.GetComponent<Button>() : null;

        if (_panel == null || _statusText == null || resume == null || _pauseButton == null ||
            _finishTestButton == null || leave == null || close == null)
        {
            Debug.LogError("GamePauseMenu prefab contract is incomplete. Expected Backdrop/Window and all named controls.", this);
            return false;
        }

        resume.onClick.AddListener(ResumeMenu);
        _pauseButton.onClick.AddListener(ToggleHostPause);
        _finishTestButton.onClick.AddListener(ForceEndGameForTest);
        leave.onClick.AddListener(LeaveGame);
        close.onClick.AddListener(CloseGameAsHost);
        return true;
    }

    private void Refresh()
    {
        bool isHost = PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient;
        bool gameOver = NetworkGameState.State != null && NetworkGameState.State.IsGameOver;
        bool manuallyPaused = NetworkGameState.State != null && NetworkGameState.State.ManualPauseRequested;

        if (_pauseButton != null)
        {
            _pauseButton.gameObject.SetActive(isHost && !gameOver);
            if (_pauseButtonText != null)
                _pauseButtonText.text = manuallyPaused ? "Reprendre la partie" : "Mettre en pause";
        }

        if (_finishTestButton != null)
        {
            _finishTestButton.gameObject.SetActive(isHost && !gameOver);
            _finishTestButton.interactable = isHost && !gameOver && NetworkGameState.State != null;
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
            else if (gameOver)
                _statusText.text = "Partie terminée";
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

    /// <summary>
    /// Host-only developer shortcut used to exercise the real replicated end-game UI
    /// without emptying Province/three Supply piles. This deliberately bypasses the
    /// normal GameEndRules condition check but publishes the same durable end-state.
    /// </summary>
    private void ForceEndGameForTest()
    {
        if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient || PhotonNetwork.CurrentRoom == null)
            return;

        GameStateSnapshot current = NetworkGameState.State;
        if (current == null || current.IsGameOver)
            return;

        GameStateSnapshot next = JsonUtility.FromJson<GameStateSnapshot>(JsonUtility.ToJson(current));
        if (next == null)
            return;

        next.Version = current.Version + 1;
        next.IsGameOver = true;
        next.GameEndReason = "Fin forcée par l’hôte (test).";
        next.EndedTurnNumber = current.TurnNumber;
        next.IsStarted = false;
        next.IsPaused = false;
        next.ManualPauseRequested = false;
        next.PauseReason = string.Empty;

        Hashtable properties = new Hashtable
        {
            { StatePropertyKey, JsonUtility.ToJson(next) }
        };

        if (PhotonNetwork.CurrentRoom.SetCustomProperties(properties))
            _panel.SetActive(false);
    }

    private void LeaveGame()
    {
        if (RoomConnectionHandler.Instance != null)
            RoomConnectionHandler.Instance.LeaveCurrentRoomPermanently();
        else if (PhotonNetwork.InRoom)
            PhotonNetwork.LeaveRoom(false);
    }

    private void CloseGameAsHost()
    {
        if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient)
            return;

        if (RoomConnectionHandler.Instance != null)
            RoomConnectionHandler.Instance.CloseCurrentRoomAsHost();
        else
            PhotonNetwork.LeaveRoom(false);
    }

}
