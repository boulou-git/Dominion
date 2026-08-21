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
    public string Pseudo => _pseudoInput.text;

    [SerializeField]
    private Button _joinButton, _launchGame;

    [SerializeField]
    private TextMeshProUGUI _playersList;

    private const string PlayerNamePrefKey = "PlayerName";

    private void Awake()
    {
        Instance = this;

        _pseudoInput.onValueChanged.AddListener(delegate
        {
            _joinButton.interactable = !string.IsNullOrWhiteSpace(_pseudoInput.text);
        });

        _joinButton.onClick.AddListener(JoinRoom);
        _launchGame.onClick.AddListener(delegate
        {
            RoomConnectionHandler.Instance.StartGameMaster();
        });
    }

    public override void OnConnectedToMaster()
    {
        _waitingPanel.SetActive(false);

        // During automatic reconnect, the session manager will rejoin the room itself.
        if (PhotonNetwork.InRoom)
            return;

        _joinLobbyPanel.SetActive(true);

        if (PlayerPrefs.HasKey(PlayerNamePrefKey))
        {
            _pseudoInput.text = PlayerPrefs.GetString(PlayerNamePrefKey);
            _joinButton.interactable = !string.IsNullOrWhiteSpace(_pseudoInput.text);
        }
    }

    private void JoinRoom()
    {
        if (RoomConnectionHandler.Instance == null)
            return;

        PlayerPrefs.SetString(PlayerNamePrefKey, _pseudoInput.text.Trim());
        PlayerPrefs.Save();
        RoomConnectionHandler.Instance.JoinRoom(_pseudoInput.text);
    }

    public override void OnJoinedRoom()
    {
        _joinLobbyPanel.SetActive(false);
        _listPanel.SetActive(true);
        RefreshHostControls();
        RefreshPlayersInRoom();
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        RefreshPlayersInRoom();
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        RefreshPlayersInRoom();
    }

    public override void OnMasterClientSwitched(Player newMasterClient)
    {
        RefreshHostControls();
        RefreshPlayersInRoom();
    }

    private void RefreshHostControls()
    {
        bool isMaster = PhotonNetwork.IsMasterClient;
        _masterPanel.SetActive(isMaster);
        _clientPanel.SetActive(!isMaster);

        if (_launchGame != null)
            _launchGame.interactable = isMaster && !NetworkGameState.IsStarted;
    }

    private void RefreshPlayersInRoom()
    {
        if (_playersList == null || !PhotonNetwork.InRoom)
            return;

        _playersList.text = string.Empty;
        foreach (Player player in PhotonNetwork.CurrentRoom.Players.Values)
        {
            string inactive = player.IsInactive ? " (déconnecté)" : string.Empty;
            string host = player.IsMasterClient ? " (host)" : string.Empty;
            _playersList.text += player.NickName + host + inactive + "\n";
        }
    }
}
