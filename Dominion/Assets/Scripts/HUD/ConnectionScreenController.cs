using Photon.Pun;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Presentation/controller for the very first Dominion screen.
/// Network ownership remains in RoomConnectionHandler; this class only collects a pseudo,
/// reports connection state and asks the existing session manager to join the room.
/// </summary>
public sealed class ConnectionScreenController : MonoBehaviourPunCallbacks
{
    private const string LastPseudoPrefKey = "Dominion.LastPseudo";

    [SerializeField] private GameObject _visualRoot;
    [SerializeField] private InputField _pseudoInput;
    [SerializeField] private Button _joinButton;
    [SerializeField] private Text _statusText;

    private bool _joinRequested;
    private string _lastError;

    private void Awake()
    {
        if (_pseudoInput != null)
        {
            _pseudoInput.characterLimit = 20;
            _pseudoInput.lineType = InputField.LineType.SingleLine;
            _pseudoInput.onValueChanged.AddListener(OnPseudoChanged);

            string previousPseudo = PlayerPrefs.GetString(LastPseudoPrefKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(previousPseudo))
                _pseudoInput.text = previousPseudo;
        }

        if (_joinButton != null)
        {
            _joinButton.onClick.RemoveAllListeners();
            _joinButton.onClick.AddListener(RequestJoin);
        }

        RefreshState();
    }

    private void OnDestroy()
    {
        if (_pseudoInput != null)
            _pseudoInput.onValueChanged.RemoveListener(OnPseudoChanged);

        if (_joinButton != null)
            _joinButton.onClick.RemoveListener(RequestJoin);
    }

    private void Update()
    {
        RefreshState();

        if (_visualRoot == null || !_visualRoot.activeSelf || _pseudoInput == null || !_pseudoInput.isFocused)
            return;

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.enterKey.wasPressedThisFrame)
            RequestJoin();
    }

    private void OnPseudoChanged(string value)
    {
        _lastError = string.Empty;
        RefreshState();
    }

    private void RequestJoin()
    {
        if (_pseudoInput == null || string.IsNullOrWhiteSpace(_pseudoInput.text))
            return;

        if (!PhotonNetwork.IsConnectedAndReady || PhotonNetwork.InRoom)
            return;

        RoomConnectionHandler handler = RoomConnectionHandler.Instance;
        if (handler == null)
        {
            _lastError = "Gestionnaire réseau indisponible.";
            RefreshState();
            return;
        }

        string pseudo = _pseudoInput.text.Trim();
        PlayerPrefs.SetString(LastPseudoPrefKey, pseudo);
        PlayerPrefs.Save();

        _lastError = string.Empty;
        _joinRequested = true;
        RefreshState();
        handler.JoinRoom(pseudo);
    }

    private void RefreshState()
    {
        bool inRoom = PhotonNetwork.InRoom;
        bool networkReady = PhotonNetwork.IsConnectedAndReady;

        if (_visualRoot != null)
            _visualRoot.SetActive(!inRoom);

        if (inRoom)
            return;

        RoomConnectionHandler handler = RoomConnectionHandler.Instance;
        bool reconnecting = handler != null && handler.IsTryingToRejoin;
        bool pseudoReady = _pseudoInput != null && !string.IsNullOrWhiteSpace(_pseudoInput.text);
        bool busy = _joinRequested || reconnecting;

        if (_joinButton != null)
            _joinButton.interactable = networkReady && pseudoReady && !busy;

        if (_statusText == null)
            return;

        if (!string.IsNullOrEmpty(_lastError) && !busy)
        {
            _statusText.text = _lastError;
            return;
        }

        if (reconnecting)
        {
            _statusText.text = "Reconnexion à votre partie précédente…";
            return;
        }

        if (_joinRequested)
        {
            _statusText.text = "Connexion à la partie…";
            return;
        }

        if (!PhotonNetwork.IsConnected)
        {
            _statusText.text = "Connexion au serveur…";
            return;
        }

        if (!networkReady)
        {
            _statusText.text = "Initialisation de la connexion…";
            return;
        }

        _statusText.text = pseudoReady ? "Prêt à rejoindre." : "Entrez votre pseudo.";
    }

    public override void OnConnectedToMaster()
    {
        RefreshState();
    }

    public override void OnJoinedRoom()
    {
        _joinRequested = false;
        _lastError = string.Empty;
        RefreshState();
    }

    public override void OnLeftRoom()
    {
        _joinRequested = false;
        _lastError = string.Empty;
        RefreshState();
    }

    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        // RoomConnectionHandler can decide to retry as RejoinRoom. Its reconnecting flag
        // takes priority in RefreshState; otherwise the user gets a concise retry message.
        _joinRequested = false;
        _lastError = "Impossible de rejoindre la partie. Réessayez.";
        RefreshState();
    }

    public override void OnDisconnected(Photon.Realtime.DisconnectCause cause)
    {
        _joinRequested = false;
        _lastError = string.Empty;
        RefreshState();
    }
}
