using Photon.Pun;
using UnityEngine;

/// <summary>
/// Thin turn command/presentation layer. Authoritative mutations live in NetworkGameState;
/// this component only routes validated local requests to the Master Client.
/// </summary>
public class PlayersTurnsHandler : MonoBehaviourPunCallbacks
{
    public static PlayersTurnsHandler Instance { get; private set; }

    [SerializeField] private PlayerHandler _playerHandler;
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
        if (!CanSendActivePlayerCommand(state)) return;
        PlayerStateSnapshot localPlayer = state.Players != null
            ? state.Players.Find(player => player != null && player.PlayerId == NetworkGameState.LocalPlayerId)
            : null;
        int[] visualHandOrder = LocalHandOrderTracker.ResolveForAuthoritativeHand(localPlayer != null ? localPlayer.Hand : null);
        photonView.RPC(nameof(RpcRequestAdvancePhase), RpcTarget.MasterClient,
            NetworkGameState.LocalPlayerId, visualHandOrder, state.Version, state.AuthorityEpoch);
    }

    public void PlayCard(int instanceId)
    {
        GameStateSnapshot state = NetworkGameState.State;
        if (!CanSendActivePlayerCommand(state) || instanceId <= 0) return;
        photonView.RPC(nameof(RpcRequestPlayCard), RpcTarget.MasterClient,
            NetworkGameState.LocalPlayerId, instanceId, state.Version, state.AuthorityEpoch);
    }

    public void BuyCard(string definitionId)
    {
        GameStateSnapshot state = NetworkGameState.State;
        if (!CanSendActivePlayerCommand(state) || string.IsNullOrEmpty(definitionId)) return;
        photonView.RPC(nameof(RpcRequestBuyCard), RpcTarget.MasterClient,
            NetworkGameState.LocalPlayerId, definitionId, state.Version, state.AuthorityEpoch);
    }

    public void SubmitDecision(string decisionId, int[] selectedInstanceIds)
    {
        GameStateSnapshot state = NetworkGameState.State;
        if (!CanSendPendingDecision(state, decisionId)) return;
        photonView.RPC(nameof(RpcRequestSubmitDecision), RpcTarget.MasterClient,
            NetworkGameState.LocalPlayerId, decisionId, selectedInstanceIds ?? new int[0], state.Version, state.AuthorityEpoch);
    }

    public void SubmitSupplyDecision(string decisionId, string[] selectedDefinitionIds)
    {
        GameStateSnapshot state = NetworkGameState.State;
        if (!CanSendPendingDecision(state, decisionId)) return;
        photonView.RPC(nameof(RpcRequestSubmitSupplyDecision), RpcTarget.MasterClient,
            NetworkGameState.LocalPlayerId, decisionId, selectedDefinitionIds ?? new string[0], state.Version, state.AuthorityEpoch);
    }

    public void SubmitOptionDecision(string decisionId, string[] selectedOptionIds)
    {
        GameStateSnapshot state = NetworkGameState.State;
        if (!CanSendPendingDecision(state, decisionId)) return;
        photonView.RPC(nameof(RpcRequestSubmitOptionDecision), RpcTarget.MasterClient,
            NetworkGameState.LocalPlayerId, decisionId, selectedOptionIds ?? new string[0], state.Version, state.AuthorityEpoch);
    }

    public void FinishTurn() => AdvancePhase();

    public void SendChatMessage(string message)
    {
        GameStateSnapshot state = NetworkGameState.State;
        if (state == null || !state.IsStarted || string.IsNullOrWhiteSpace(message)) return;
        message = message.Trim();
        if (message.Length > JournalRules.MaxChatLength)
            message = message.Substring(0, JournalRules.MaxChatLength);
        photonView.RPC(nameof(RpcRequestChatMessage), RpcTarget.MasterClient,
            NetworkGameState.LocalPlayerId, message, state.AuthorityEpoch);
    }

    [PunRPC]
    private void RpcRequestAdvancePhase(string requesterPlayerId, int[] visualHandOrder, int expectedVersion, int expectedAuthorityEpoch, PhotonMessageInfo info)
    {
        if (!ValidateSender(requesterPlayerId, info)) return;
        if (!NetworkGameState.TryAdvancePhase(requesterPlayerId, expectedVersion, expectedAuthorityEpoch, visualHandOrder))
            Debug.LogWarning("Rejected stale or invalid AdvancePhase command.");
    }

    [PunRPC]
    private void RpcRequestPlayCard(string requesterPlayerId, int instanceId, int expectedVersion, int expectedAuthorityEpoch, PhotonMessageInfo info)
    {
        if (!ValidateSender(requesterPlayerId, info)) return;
        if (!NetworkGameState.TryPlayCard(requesterPlayerId, instanceId, expectedVersion, expectedAuthorityEpoch))
            Debug.LogWarning("Rejected stale or invalid PlayCard command.");
    }

    [PunRPC]
    private void RpcRequestBuyCard(string requesterPlayerId, string definitionId, int expectedVersion, int expectedAuthorityEpoch, PhotonMessageInfo info)
    {
        if (!ValidateSender(requesterPlayerId, info)) return;
        if (!NetworkGameState.TryBuyCard(requesterPlayerId, definitionId, expectedVersion, expectedAuthorityEpoch))
            Debug.LogWarning("Rejected stale or invalid BuyCard command for " + definitionId + ".");
    }

    [PunRPC]
    private void RpcRequestSubmitDecision(string requesterPlayerId, string decisionId, int[] selectedInstanceIds,
        int expectedVersion, int expectedAuthorityEpoch, PhotonMessageInfo info)
    {
        if (!ValidateSender(requesterPlayerId, info)) return;
        if (!NetworkGameState.TrySubmitDecision(requesterPlayerId, decisionId, selectedInstanceIds, expectedVersion, expectedAuthorityEpoch))
            Debug.LogWarning("Rejected stale or invalid SubmitDecision command.");
    }

    [PunRPC]
    private void RpcRequestSubmitSupplyDecision(string requesterPlayerId, string decisionId, string[] selectedDefinitionIds,
        int expectedVersion, int expectedAuthorityEpoch, PhotonMessageInfo info)
    {
        if (!ValidateSender(requesterPlayerId, info)) return;
        if (!NetworkGameState.TrySubmitSupplyDecision(requesterPlayerId, decisionId, selectedDefinitionIds, expectedVersion, expectedAuthorityEpoch))
            Debug.LogWarning("Rejected stale or invalid SubmitSupplyDecision command.");
    }

    [PunRPC]
    private void RpcRequestSubmitOptionDecision(string requesterPlayerId, string decisionId, string[] selectedOptionIds,
        int expectedVersion, int expectedAuthorityEpoch, PhotonMessageInfo info)
    {
        if (!ValidateSender(requesterPlayerId, info)) return;
        if (!NetworkGameState.TrySubmitOptionDecision(requesterPlayerId, decisionId, selectedOptionIds, expectedVersion, expectedAuthorityEpoch))
            Debug.LogWarning("Rejected stale or invalid SubmitOptionDecision command.");
    }

    [PunRPC]
    private void RpcRequestChatMessage(string requesterPlayerId, string message, int expectedAuthorityEpoch,
        PhotonMessageInfo info)
    {
        if (!ValidateSender(requesterPlayerId, info)) return;
        NetworkGameState.TrySendChatMessage(requesterPlayerId, message, expectedAuthorityEpoch);
    }

    private static bool CanSendActivePlayerCommand(GameStateSnapshot state)
    {
        return state != null && state.IsStarted && !state.IsPaused &&
               (state.Resolution == null || !state.Resolution.IsActive) &&
               state.ActivePlayerId == NetworkGameState.LocalPlayerId;
    }

    private static bool CanSendPendingDecision(GameStateSnapshot state, string decisionId)
    {
        if (state == null || !state.IsStarted || state.IsPaused || state.Resolution == null || !state.Resolution.IsActive)
            return false;
        PendingDecisionSnapshot decision = state.Resolution.PendingDecision;
        return decision != null && decision.IsPending &&
               decision.PlayerId == NetworkGameState.LocalPlayerId && decision.DecisionId == decisionId;
    }

    private static bool ValidateSender(string requesterPlayerId, PhotonMessageInfo info)
    {
        if (!PhotonNetwork.IsMasterClient || info.Sender == null) return false;
        string senderPlayerId = NetworkGameState.GetPlayerId(info.Sender);
        if (senderPlayerId == requesterPlayerId) return true;
        Debug.LogWarning("Rejected gameplay command: Photon sender identity mismatch.");
        return false;
    }

    private void OnGameStateChanged(GameStateSnapshot state)
    {
        if (state == null || !state.IsStarted) return;
        bool turnChanged = state.TurnNumber != _lastObservedTurnNumber || state.ActivePlayerId != _lastObservedActivePlayerId;
        _lastObservedTurnNumber = state.TurnNumber;
        _lastObservedActivePlayerId = state.ActivePlayerId;
        if (!turnChanged || state.IsPaused) return;
        LocalHandOrderTracker.Clear();
        if (state.ActivePlayerId == NetworkGameState.LocalPlayerId && _playerHandler != null)
            _playerHandler.BeginTurn();
    }
}
