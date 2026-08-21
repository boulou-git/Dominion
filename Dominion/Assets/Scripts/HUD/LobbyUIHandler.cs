using Photon.Pun;
using Photon.Realtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LobbyUIHandler : MonoBehaviourPunCallbacks
{
    public static LobbyUIHandler Instance;

    [SerializeField]
    private GameObject _waitingPanel, _joinLobbyPanel, _listPanel, _masterPanel, _clientPanel;

    [SerializeField]
    private TMP_InputField _pseudoInput;
    public string Pseudo { get { return _pseudoInput.text; } }

    [SerializeField]
    private Button _joinButton, _launchGame;

    [SerializeField]
    private TextMeshProUGUI _playersList;

    const string playerNamePrefKey = "PlayerName";

    private void Awake()
    {
        Instance = this;

        _pseudoInput.onValueChanged.AddListener(delegate { _joinButton.interactable = _pseudoInput.text != string.Empty; });
        _joinButton.onClick.AddListener(delegate { JoinRoom(); });
        _launchGame.onClick.AddListener(delegate { RoomConnectionHandler.Instance.StartGameMaster(); });
    }

    public override void OnConnectedToMaster()
    {
        _waitingPanel.SetActive(false);
        _joinLobbyPanel.SetActive(true);

        if (PlayerPrefs.HasKey(playerNamePrefKey))
        {
            _pseudoInput.text = PlayerPrefs.GetString(playerNamePrefKey);
            _joinButton.interactable = true;
        }
    }

    private void JoinRoom()
    {
        PlayerPrefs.SetString(playerNamePrefKey, _pseudoInput.text);
        RoomConnectionHandler.Instance.JoinRoom(_pseudoInput.text);
    }

    public override void OnJoinedRoom()
    {
        _joinLobbyPanel.SetActive(false);
        _listPanel.SetActive(true);
        (PhotonNetwork.IsMasterClient ? _masterPanel : _clientPanel).SetActive(true);

        PhotonView photonView = PhotonView.Get(this);
        photonView.RPC("RefreshPlayersInRoom", RpcTarget.All);
    }

    [PunRPC]
    private void RefreshPlayersInRoom()
    {
        _playersList.text = string.Empty;
        foreach (Player player in PhotonNetwork.CurrentRoom.Players.Values)
        {
            _playersList.text += player.NickName + (player.IsMasterClient ? " (host)" : "") + "\n";
        }
    }
}
