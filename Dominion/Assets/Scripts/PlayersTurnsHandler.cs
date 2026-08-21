using Photon.Pun;
using UnityEngine;

/// <summary>
/// Thin turn presentation/controller layer.
/// The authoritative turn and phase live in NetworkGameState instead of local Master-only memory.
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
    /// Requests the next phase for the local active player.
    /// Action -> Buy -> Cleanup -> next player's Action phase.
    /// </summary>
    public void AdvancePhase()
    {
        GameStateSnapshot state = NetworkGameState.State;
        if (state == null || state.IsPaused || !state.IsStarted)
            return;

        if (state.ActivePlayerId != NetworkGameState.LocalPlayerId)
            return;

        photonView.RPC(
            nameof(RpcRequestAdvancePhase),
            RpcTarget.MasterClient,
            NetworkGameState.LocalPlayerId,
            state.Version,
            state.AuthorityEpoch);
    }

    /// <summary>
    /// Compatibility alias for the old HUD button. It now advances one phase instead of
    /// skipping directly to the next player.
    /// </summary>
    public void FinishTurn()
    {
        AdvancePhase();
    }

    [PunRPC]
    private void RpcRequestAdvancePhase(
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
            Debug.LogWarning("Rejected phase command: Photon sender identity mismatch.");
            return;
        }

        if (!NetworkGameState.TryAdvancePhase(requesterPlayerId, expectedVersion, expectedAuthorityEpoch))
        {
            Debug.LogWarning("Rejected stale or invalid AdvancePhase command.");
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
