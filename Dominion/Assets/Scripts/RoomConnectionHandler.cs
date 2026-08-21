using System;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.SceneManagement;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>
/// Persistent Photon session manager.
/// Handles connection, stable player identity, room lifetime, reconnect/rejoin,
/// Master Client migration and game/lobby scene recovery.
/// </summary>
public class RoomConnectionHandler : MonoBehaviourPunCallbacks
{
    public static RoomConnectionHandler Instance { get; private set; }

    private const string RoomName = "Dominion";
    private const string PlayerIdPrefKey = "Dominion.PlayerId";
    private const string GameStartedPropertyKey = "dominion.gameStarted";

    private static readonly TypedLobby TypedLobby = new TypedLobby("Lobby", LobbyType.Default);

    private static readonly RoomOptions RoomOptions = new RoomOptions
    {
        MaxPlayers = 8,
        IsVisible = true,
        IsOpen = true,

        // Keep an inactive player's actor in the room long enough to recover from
        // a transient internet loss or application hiccup.
        PlayerTtl = 60_000,

        // Keep the room alive briefly even if every client disconnects at once.
        EmptyRoomTtl = 60_000,

        // Makes Player.UserId available to the other clients. We use it as the
        // stable game identity instead of NickName or ActorNumber.
        PublishUserId = true
    };

    private bool _tryingToRejoin;
    private string _lastRoomName;

    public string LocalPlayerId { get; private set; }

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

        // No late joins after the immutable game player order has been created.
        PhotonNetwork.CurrentRoom.IsOpen = false;
        PhotonNetwork.CurrentRoom.IsVisible = false;

        Hashtable roomProperties = new Hashtable
        {
            { GameStartedPropertyKey, true }
        };
        PhotonNetwork.CurrentRoom.SetCustomProperties(roomProperties);

        PhotonView.Get(this).RPC(nameof(StartGameClients), RpcTarget.AllViaServer);
    }

    public override void OnJoinedRoom()
    {
        _lastRoomName = PhotonNetwork.CurrentRoom.Name;
        _tryingToRejoin = false;

        bool hasGameState = NetworkGameState.HydrateFromRoom(true);
        bool gameStarted = IsGameStartedInRoom() || (hasGameState && NetworkGameState.IsStarted);

        if (gameStarted)
            EnsureGameSceneLoaded();
        else
            EnsureLobbySceneLoaded();
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        Debug.LogWarning($"Photon disconnected: {cause}");

        // Keep Unity game state and scenes alive. ReconnectAndRejoin will restore the
        // same room actor while PlayerTtl is still valid.
        if (string.IsNullOrEmpty(_lastRoomName))
            return;

        _tryingToRejoin = true;

        if (!PhotonNetwork.ReconnectAndRejoin())
            PhotonNetwork.Reconnect();
    }

    public override void OnConnectedToMaster()
    {
        if (!_tryingToRejoin || string.IsNullOrEmpty(_lastRoomName))
            return;

        PhotonNetwork.RejoinRoom(_lastRoomName);
    }

    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        Debug.LogError($"Could not rejoin room '{_lastRoomName}': {returnCode} - {message}");
        _tryingToRejoin = false;
        _lastRoomName = null;
        NetworkGameState.ResetLocalState();
        EnsureLobbySceneLoaded();
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        if (!PhotonNetwork.IsMasterClient || !NetworkGameState.IsStarted)
            return;

        NetworkGameState.SetPlayerConnectivity(newPlayer, true);
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        if (!PhotonNetwork.IsMasterClient || !NetworkGameState.IsStarted)
            return;

        NetworkGameState.SetPlayerConnectivity(otherPlayer, false);
    }

    public override void OnMasterClientSwitched(Player newMasterClient)
    {
        Debug.Log($"Master Client switched to {newMasterClient?.NickName}.");

        if (newMasterClient != PhotonNetwork.LocalPlayer)
            return;

        if (NetworkGameState.HandleMasterMigration())
            Debug.Log($"Host migration complete. Authority epoch: {NetworkGameState.AuthorityEpoch}.");
    }

    public override void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
    {
        NetworkGameState.ApplyRoomProperties(propertiesThatChanged);

        if (IsGameStartedInRoom())
            EnsureGameSceneLoaded();
    }

    public override void OnLeftRoom()
    {
        _lastRoomName = null;
        _tryingToRejoin = false;
        NetworkGameState.ResetLocalState();
        EnsureLobbySceneLoaded();
    }

    [PunRPC]
    private void StartGameClients()
    {
        NetworkGameState.HydrateFromRoom(true);
        EnsureGameSceneLoaded();
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

    private static void EnsureLobbySceneLoaded()
    {
        if (!SceneManager.GetSceneByName("Lobby").isLoaded)
            SceneManager.LoadScene("Lobby", LoadSceneMode.Additive);

        if (SceneManager.GetSceneByName("Game").isLoaded)
            SceneManager.UnloadSceneAsync("Game");
    }

    private static void EnsureGameSceneLoaded()
    {
        if (!SceneManager.GetSceneByName("Game").isLoaded)
            SceneManager.LoadScene("Game", LoadSceneMode.Additive);

        if (SceneManager.GetSceneByName("Lobby").isLoaded)
            SceneManager.UnloadSceneAsync("Lobby");
    }
}
