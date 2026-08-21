using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

/// <summary>
/// Handles transient Photon disconnects. Rejoin requires the room to use PlayerTtl > 0.
/// </summary>
public class PhotonReconnectHandler : MonoBehaviourPunCallbacks
{
    [SerializeField] private bool _autoReconnect = true;

    private bool _shouldRejoin;
    private string _roomName;

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }

    public override void OnJoinedRoom()
    {
        _roomName = PhotonNetwork.CurrentRoom.Name;
        _shouldRejoin = false;
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        Debug.LogWarning($"Photon disconnected: {cause}");

        if (!_autoReconnect || string.IsNullOrEmpty(_roomName))
            return;

        _shouldRejoin = true;

        if (!PhotonNetwork.ReconnectAndRejoin())
            PhotonNetwork.Reconnect();
    }

    public override void OnConnectedToMaster()
    {
        if (!_shouldRejoin || string.IsNullOrEmpty(_roomName))
            return;

        PhotonNetwork.RejoinRoom(_roomName);
    }

    public override void OnJoinedLobby()
    {
        if (_shouldRejoin && !string.IsNullOrEmpty(_roomName))
            PhotonNetwork.RejoinRoom(_roomName);
    }

    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        Debug.LogError($"Could not rejoin room '{_roomName}': {returnCode} - {message}");
        _shouldRejoin = false;
    }
}
