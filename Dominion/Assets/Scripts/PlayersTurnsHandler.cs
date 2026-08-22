using Photon.Pun;
using UnityEngine;

/// <summary>
/// Thin turn command/presentation layer. Authoritative mutations live in NetworkGameState;
/// this component only routes validated local requests to the Master Client.
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

    public void AdvancePhase()
    {
        GameStateSnapshot state = NetworkGameState.State;
        if (!CanSendActivePlayerCommand(state))
            return;

        PlayerStateSnapshot localPlayer = state.Players != null
            ? state.Players.Find(player => player != null && player.PlayerId == NetworkGameState.LocalPlayerId)
            : null;
        int[] visualHandOrder = LocalHandOrderTracker.ResolveForAuthoritativeHand(
            localPlayer != null ? localPlayer.Hand : null);

        photonView.RPC(
            nameof(RpcRequestAdvancePhase),
            RpcTarget.MasterClient,
            NetworkGameState.LocalPlayerId,
            visualHandOrder,
            state.Version,
            state.AuthorityEpoch);
    }

    public void PlayTreasure(int instanceId)
    {
        GameStateSnapshot state = NetworkGameState.State;
        if (!CanSendActivePlayerCommand(state) || instanceId <= 0)
            return;

        photonView.RPC(
            nameof(RpcRequestPlayTreasure),
            RpcTarget.MasterClient,
            NetworkGameState.LocalPlayerId,
            instanceId,
            state.Version,
            state.AuthorityEpoch);
    }

    public void BuyCard(string definitionId)
    {
        GameStateSnapshot state = NetworkGameState.State;
        if (!CanSendActivePlayerCommand(state) || string.IsNullOrEmpty(definitionId))
            return;

        photonView.RPC(
            nameof(RpcRequestBuyCard),
            RpcTarget.MasterClient,
            NetworkGameState.LocalPlayerId,
            definitionId,
            state.Version,
            state.AuthorityEpoch);
    }

    public void FinishTurn()
    {
        AdvancePhase();
    }

    [PunRPC]
    private void RpcRequestAdvancePhase(
        string requesterPlayerId,
        int[] visualHandOrder,
        int expectedVersion,
        int expectedAuthorityEpoch,
        PhotonMessageInfo info)
    {
        if (!ValidateSender(requesterPlayerId, info))
            return;

        if (!NetworkGameState.TryAdvancePhase(
                requesterPlayerId,
                expectedVersion,
                expectedAuthorityEpoch,
                visualHandOrder))
            Debug.LogWarning("Rejected stale or invalid AdvancePhase command.");
    }

    [PunRPC]
    private void RpcRequestPlayTreasure(
        string requesterPlayerId,
        int instanceId,
        int expectedVersion,
        int expectedAuthorityEpoch,
        PhotonMessageInfo info)
    {
        if (!ValidateSender(requesterPlayerId, info))
            return;

        if (!NetworkGameState.TryPlayTreasure(requesterPlayerId, instanceId, expectedVersion, expectedAuthorityEpoch))
            Debug.LogWarning("Rejected stale or invalid PlayTreasure command.");
    }

    [PunRPC]
    private void RpcRequestBuyCard(
        string requesterPlayerId,
        string definitionId,
        int expectedVersion,
        int expectedAuthorityEpoch,
        PhotonMessageInfo info)
    {
        if (!ValidateSender(requesterPlayerId, info))
            return;

        if (!NetworkGameState.TryBuyCard(requesterPlayerId, definitionId, expectedVersion, expectedAuthorityEpoch))
            Debug.LogWarning("Rejected stale or invalid BuyCard command for " + definitionId + ".");
    }

    private static bool CanSendActivePlayerCommand(GameStateSnapshot state)
    {
        return state != null &&
               state.IsStarted &&
               !state.IsPaused &&
               state.ActivePlayerId == NetworkGameState.LocalPlayerId;
    }

    private static bool ValidateSender(string requesterPlayerId, PhotonMessageInfo info)
    {
        if (!PhotonNetwork.IsMasterClient || info.Sender == null)
            return false;

        string senderPlayerId = NetworkGameState.GetPlayerId(info.Sender);
        if (senderPlayerId == requesterPlayerId)
            return true;

        Debug.LogWarning("Rejected gameplay command: Photon sender identity mismatch.");
        return false;
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

        LocalHandOrderTracker.Clear();

        if (state.ActivePlayerId == NetworkGameState.LocalPlayerId && _playerHandler != null)
            _playerHandler.BeginTurn();
    }
}
