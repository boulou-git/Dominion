using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine.SceneManagement;

public class RoomConnectionHandler : MonoBehaviourPunCallbacks
{
    public static RoomConnectionHandler Instance { get; private set; }

    private static string _roomName = "Dominion";

    private static RoomOptions _roomOptions = new RoomOptions()
    {
        MaxPlayers = 8,
        IsVisible = true,
        IsOpen = true
    };

    private static TypedLobby _typedLobby = new TypedLobby("Lobby", LobbyType.Default);

    private void Awake()
    {
        Instance = this;
        PhotonNetwork.ConnectUsingSettings();
        SceneManager.LoadScene("Lobby", LoadSceneMode.Additive);
    }

    public void JoinRoom(string pseudo)
    {
        PhotonNetwork.NickName = pseudo;
        PhotonNetwork.JoinOrCreateRoom(_roomName, _roomOptions, _typedLobby);
    }

    public void StartGameMaster()
    {
        PhotonView.Get(this).RPC("StartGameClients", RpcTarget.All);
    }

    [PunRPC]
    private void StartGameClients()
    {
        SceneManager.UnloadSceneAsync("Lobby");
        SceneManager.LoadScene("Game", LoadSceneMode.Additive);
    }
}
