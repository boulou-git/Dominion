using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

/// <summary>
/// Owns the synchronized game-state snapshot.
/// The Master Client is authoritative; other clients only apply snapshots received from it.
/// </summary>
[RequireComponent(typeof(PhotonView))]
public class NetworkGameState : MonoBehaviourPunCallbacks
{
    public static NetworkGameState Instance { get; private set; }

    public GameStateSnapshot State { get; private set; }

    public bool IsAuthoritative => PhotonNetwork.IsMasterClient;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    public void InitialiseAuthoritativeState()
    {
        if (!IsAuthoritative)
            return;

        State = new GameStateSnapshot();

        foreach (Player player in PhotonNetwork.PlayerList)
        {
            State.Players.Add(new PlayerStateSnapshot
            {
                ActorNumber = player.ActorNumber,
                NickName = player.NickName,
                IsConnected = !player.IsInactive
            });
        }

        if (PhotonNetwork.PlayerList.Length > 0)
            State.ActivePlayerActorNumber = PhotonNetwork.PlayerList[0].ActorNumber;

        State.Phase = "Action";
        CommitStateChange();
    }

    /// <summary>
    /// Call this after an authoritative rules change. Later, GameEngine will be the only caller.
    /// </summary>
    public void CommitStateChange()
    {
        if (!IsAuthoritative || State == null)
            return;

        State.IncrementVersion();
        BroadcastFullState();
    }

    public void RequestFullState()
    {
        if (!PhotonNetwork.InRoom)
            return;

        if (IsAuthoritative)
        {
            BroadcastFullState();
            return;
        }

        photonView.RPC(nameof(RpcRequestFullState), RpcTarget.MasterClient, PhotonNetwork.LocalPlayer.ActorNumber);
    }

    private void BroadcastFullState()
    {
        if (!IsAuthoritative || State == null)
            return;

        string json = JsonUtility.ToJson(State);
        photonView.RPC(nameof(RpcReceiveFullState), RpcTarget.Others, json);
    }

    private void SendFullStateTo(Player player)
    {
        if (!IsAuthoritative || State == null || player == null)
            return;

        string json = JsonUtility.ToJson(State);
        photonView.RPC(nameof(RpcReceiveFullState), player, json);
    }

    [PunRPC]
    private void RpcRequestFullState(int requesterActorNumber, PhotonMessageInfo info)
    {
        if (!IsAuthoritative || info.Sender == null || info.Sender.ActorNumber != requesterActorNumber)
            return;

        SendFullStateTo(info.Sender);
    }

    [PunRPC]
    private void RpcReceiveFullState(string json)
    {
        if (PhotonNetwork.IsMasterClient)
            return;

        GameStateSnapshot incoming = JsonUtility.FromJson<GameStateSnapshot>(json);
        if (incoming == null)
            return;

        if (State != null && incoming.Version < State.Version)
            return;

        State = incoming;
        Debug.Log($"GameState synchronized at version {State.Version}.");
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        if (IsAuthoritative)
            SendFullStateTo(newPlayer);
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        if (!IsAuthoritative || State == null)
            return;

        PlayerStateSnapshot playerState = State.Players.Find(p => p.ActorNumber == otherPlayer.ActorNumber);
        if (playerState != null)
        {
            playerState.IsConnected = false;
            CommitStateChange();
        }
    }

    public override void OnMasterClientSwitched(Player newMasterClient)
    {
        if (newMasterClient != PhotonNetwork.LocalPlayer)
            return;

        Debug.Log("Local player became Master Client. Continuing from local synchronized GameState.");

        if (State == null)
        {
            Debug.LogWarning("No local GameState is available after host migration.");
            return;
        }

        CommitStateChange();
    }
}
