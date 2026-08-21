using System;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.SceneManagement;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>
/// Persistent Photon session manager.
/// Handles connection, stable player identity, room lifetime, reconnect/rejoin,
/// Master Client migration and safe additive scene transitions.
/// </summary>
public class RoomConnectionHandler : MonoBehaviourPunCallbacks
{
    public static RoomConnectionHandler Instance { get; private set; }

    private const string RoomName = "Dominion";
    private const string PlayerIdPrefKey = "Dominion.PlayerId";
    private const string LastRoomPrefKey = "Dominion.LastRoom";
    private const string GameStartedPropertyKey = "dominion.gameStarted";

    private static readonly TypedLobby TypedLobby = new TypedLobby("Lobby", LobbyType.Default);

    private static readonly RoomOptions RoomOptions = new RoomOptions
    {
        MaxPlayers = 8,
        IsVisible = true,
        IsOpen = true,
        PlayerTtl = 300_000,
        EmptyRoomTtl = 300_000,
        PublishUserId = true
    };

    private bool _tryingToRejoin;
    private bool _resumeAttemptedThisConnection;
    private string _lastRoomName;
    private bool _gameSceneTransitionInProgress;
    private bool _lobbySceneTransitionInProgress;

    public string LocalPlayerId { get; private set; }
    public bool IsTryingToRejoin => _tryingToRejoin;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        LocalPlayerId = LoadOrCreatePlayerId();
        PhotonNetwork.AuthValues = new AuthenticationValues(LocalPlayerId);
        PhotonNetwork.AutomaticallySyncScene = false;

        _lastRoomName = PlayerPrefs.GetString(LastRoomPrefKey, string.Empty);

        EnsureLobbySceneLoaded();

        if (!PhotonNetwork.IsConnected)
            PhotonNetwork.ConnectUsingSettings();
    }

    public void JoinRoom(string pseudo)
    {
        if (string.IsNullOrWhiteSpace(pseudo))
            return;

        PhotonNetwork.NickName = pseudo.Trim();

        if (!PhotonNetwork.IsConnectedAndReady)
        {
            Debug.LogWarning("Photon is not ready yet. JoinRoom ignored.");
            return;
        }

        _tryingToRejoin = false;
        PhotonNetwork.JoinOrCreateRoom(RoomName, RoomOptions, TypedLobby);
    }

    public void StartGameMaster()
    {
        if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient)
            return;

        if (NetworkGameState.IsStarted)
            return;

        if (!NetworkGameState.InitialiseAuthoritativeState())
        {
            Debug.LogError("Could not initialise authoritative game state.");
            return;
        }

        PhotonNetwork.CurrentRoom.IsOpen = false;
        PhotonNetwork.CurrentRoom.IsVisible = false;

        Hashtable roomProperties = new Hashtable
        {
            { GameStartedPropertyKey, true }
        };
        PhotonNetwork.CurrentRoom.SetCustomProperties(roomProperties);
    }

    public override void OnJoinedRoom()
    {
        _lastRoomName = PhotonNetwork.CurrentRoom.Name;
        SaveLastRoom(_lastRoomName);
        _tryingToRejoin = false;
        _resumeAttemptedThisConnection = true;

        bool hasGameState = NetworkGameState.HydrateFromRoom(true);
        bool gameStarted = IsGameStartedInRoom() || (hasGameState && NetworkGameState.IsStarted);

        Debug.Log(gameStarted
            ? $"Joined/resumed match '{_lastRoomName}'."
            : $"Joined lobby '{_lastRoomName}'.");

        if (gameStarted)
            EnsureGameSceneLoaded();
        else
            EnsureLobbySceneLoaded();
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        Debug.LogWarning($"Photon disconnected: {cause}");

        if (string.IsNullOrEmpty(_lastRoomName))
            _lastRoomName = PlayerPrefs.GetString(LastRoomPrefKey, string.Empty);

        if (string.IsNullOrEmpty(_lastRoomName))
            return;

        _tryingToRejoin = true;
        _resumeAttemptedThisConnection = false;

        if (!PhotonNetwork.ReconnectAndRejoin())
            PhotonNetwork.Reconnect();
    }

    public override void OnConnectedToMaster()
    {
        if (_resumeAttemptedThisConnection)
            return;

        if (string.IsNullOrEmpty(_lastRoomName))
            _lastRoomName = PlayerPrefs.GetString(LastRoomPrefKey, string.Empty);

        if (string.IsNullOrEmpty(_lastRoomName))
            return;

        _resumeAttemptedThisConnection = true;
        _tryingToRejoin = true;
        Debug.Log($"Trying to resume previous room '{_lastRoomName}'.");
        PhotonNetwork.RejoinRoom(_lastRoomName);
    }

    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        if (!_tryingToRejoin)
        {
            Debug.LogError($"Could not join room: {returnCode} - {message}");
            return;
        }

        Debug.LogWarning($"Previous match could not be resumed: {returnCode} - {message}");
        _tryingToRejoin = false;
        _lastRoomName = null;
        ClearLastRoom();
        NetworkGameState.ResetLocalState();
        EnsureLobbySceneLoaded();
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        if (!PhotonNetwork.IsMasterClient || !NetworkGameState.IsStarted)
            return;

        NetworkGameState.SetPlayerConnectivity(newPlayer, true);
        LogPauseState();
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        if (!PhotonNetwork.IsMasterClient || !NetworkGameState.IsStarted)
            return;

        NetworkGameState.SetPlayerConnectivity(otherPlayer, false);
        LogPauseState();
    }

    public override void OnMasterClientSwitched(Player newMasterClient)
    {
        Debug.Log($"Master Client switched to {newMasterClient?.NickName}.");

        if (newMasterClient != PhotonNetwork.LocalPlayer)
            return;

        if (NetworkGameState.HandleMasterMigration())
        {
            Debug.Log($"Host migration complete. Authority epoch: {NetworkGameState.AuthorityEpoch}.");
            LogPauseState();
        }
    }

    public override void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
    {
        bool stateChanged = NetworkGameState.ApplyRoomProperties(propertiesThatChanged);

        if (stateChanged)
            LogPauseState();

        if (IsGameStartedInRoom())
            EnsureGameSceneLoaded();
    }

    public override void OnLeftRoom()
    {
        _lastRoomName = null;
        _tryingToRejoin = false;
        _resumeAttemptedThisConnection = true;
        ClearLastRoom();
        NetworkGameState.ResetLocalState();
        EnsureLobbySceneLoaded();
    }

    private void LogPauseState()
    {
        if (NetworkGameState.State == null || !NetworkGameState.IsStarted)
            return;

        if (NetworkGameState.IsPaused)
            Debug.LogWarning(NetworkGameState.State.PauseReason);
        else
            Debug.Log("All players are connected. Dominion match resumed.");
    }

    private bool IsGameStartedInRoom()
    {
        if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null)
            return false;

        Hashtable properties = PhotonNetwork.CurrentRoom.CustomProperties;
        if (properties == null || !properties.ContainsKey(GameStartedPropertyKey))
            return false;

        object value = properties[GameStartedPropertyKey];
        return value is bool && (bool)value;
    }

    private static string LoadOrCreatePlayerId()
    {
        if (PlayerPrefs.HasKey(PlayerIdPrefKey))
            return PlayerPrefs.GetString(PlayerIdPrefKey);

        string id = Guid.NewGuid().ToString("N");
        PlayerPrefs.SetString(PlayerIdPrefKey, id);
        PlayerPrefs.Save();
        return id;
    }

    private static void SaveLastRoom(string roomName)
    {
        if (string.IsNullOrEmpty(roomName))
            return;

        PlayerPrefs.SetString(LastRoomPrefKey, roomName);
        PlayerPrefs.Save();
    }

    private static void ClearLastRoom()
    {
        PlayerPrefs.DeleteKey(LastRoomPrefKey);
        PlayerPrefs.Save();
    }

    private void EnsureLobbySceneLoaded()
    {
        if (_lobbySceneTransitionInProgress)
            return;

        if (!SceneManager.GetSceneByName("Lobby").isLoaded)
        {
            _lobbySceneTransitionInProgress = true;
            AsyncOperation load = SceneManager.LoadSceneAsync("Lobby", LoadSceneMode.Additive);
            if (load != null)
                load.completed += delegate { _lobbySceneTransitionInProgress = false; };
            else
                _lobbySceneTransitionInProgress = false;
        }

        if (SceneManager.GetSceneByName("Game").isLoaded && !_gameSceneTransitionInProgress)
        {
            _gameSceneTransitionInProgress = true;
            AsyncOperation unload = SceneManager.UnloadSceneAsync("Game");
            if (unload != null)
                unload.completed += delegate { _gameSceneTransitionInProgress = false; };
            else
                _gameSceneTransitionInProgress = false;
        }
    }

    private void EnsureGameSceneLoaded()
    {
        if (_gameSceneTransitionInProgress)
            return;

        if (!SceneManager.GetSceneByName("Game").isLoaded)
        {
            _gameSceneTransitionInProgress = true;
            AsyncOperation load = SceneManager.LoadSceneAsync("Game", LoadSceneMode.Additive);
            if (load != null)
                load.completed += delegate { _gameSceneTransitionInProgress = false; };
            else
                _gameSceneTransitionInProgress = false;
        }

        if (SceneManager.GetSceneByName("Lobby").isLoaded && !_lobbySceneTransitionInProgress)
        {
            _lobbySceneTransitionInProgress = true;
            AsyncOperation unload = SceneManager.UnloadSceneAsync("Lobby");
            if (unload != null)
                unload.completed += delegate { _lobbySceneTransitionInProgress = false; };
            else
                _lobbySceneTransitionInProgress = false;
        }
    }
}
