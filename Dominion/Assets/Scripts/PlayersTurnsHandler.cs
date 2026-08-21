using Photon.Pun;
using UnityEngine;

/// <summary>
/// Thin turn presentation/controller layer.
/// The authoritative turn now lives in NetworkGameState instead of local Master-only memory.
/// </summary>
public class PlayersTurnsHandler : MonoBehaviourPunCallbacks
{
    public static PlayersTurnsHandler Instance { get; private set; }

    [SerializeField]
    private PlayerHandler _playerHandler;

    private string _lastObservedActivePlayerId;
    private int _lastObservedTurnNumber = -1;

    public void Initialise()
    {
        Instance = this;
        NetworkGameState.StateChanged -= OnGameStateChanged;
        NetworkGameState.StateChanged += OnGameStateChanged;

        NetworkGameState.HydrateFromRoom(true);
        OnGameStateChanged(NetworkGameState.State);
    }

    private void OnDestroy()
    {
        NetworkGameState.StateChanged -= OnGameStateChanged;
    }

    /// <summary>
    /// Called by the local player's UI when their turn is complete.
    /// The request is sent to the Master Client with the state version/epoch the player saw.
    /// </summary>
    public void FinishTurn()
    {
        GameStateSnapshot state = NetworkGameState.State;
        if (state == null || state.IsPaused || !state.IsStarted)
            return;

        if (state.ActivePlayerId != NetworkGameState.LocalPlayerId)
            return;

        photonView.RPC(
            nameof(RpcRequestFinishTurn),
            RpcTarget.MasterClient,
            NetworkGameState.LocalPlayerId,
            state.Version,
            state.AuthorityEpoch);
    }

    [PunRPC]
    private void RpcRequestFinishTurn(
        string requesterPlayerId,
        int expectedVersion,
        int expectedAuthorityEpoch,
        PhotonMessageInfo info)
    {
        if (!PhotonNetwork.IsMasterClient || info.Sender == null)
            return;

        string senderPlayerId = NetworkGameState.GetPlayerId(info.Sender);
        if (senderPlayerId != requesterPlayerId)
        {
            Debug.LogWarning("Rejected turn command: Photon sender identity mismatch.");
            return;
        }

        if (!NetworkGameState.TryAdvanceTurn(requesterPlayerId, expectedVersion, expectedAuthorityEpoch))
        {
            Debug.LogWarning("Rejected stale or invalid FinishTurn command.");
        }
    }

    private void OnGameStateChanged(GameStateSnapshot state)
    {
        if (state == null || !state.IsStarted)
            return;

        bool turnChanged = state.TurnNumber != _lastObservedTurnNumber ||
                           state.ActivePlayerId != _lastObservedActivePlayerId;

        _lastObservedTurnNumber = state.TurnNumber;
        _lastObservedActivePlayerId = state.ActivePlayerId;

        if (!turnChanged || state.IsPaused)
            return;

        if (state.ActivePlayerId == NetworkGameState.LocalPlayerId)
            _playerHandler.BeginTurn();
    }
}
