using System.Collections.Generic;
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
        if (Instance == this)
            Instance = null;
    }

    public void AdvancePhase()
    {
        GameStateSnapshot state = NetworkGameState.State;
        if (!CanSendActivePlayerCommand(state) || state.PendingChoice != null)
            return;

        photonView.RPC(
            nameof(RpcRequestAdvancePhase),
            RpcTarget.MasterClient,
            NetworkGameState.LocalPlayerId,
            state.Version,
            state.AuthorityEpoch);
    }

    public void PlayTreasure(int instanceId)
    {
        GameStateSnapshot state = NetworkGameState.State;
        if (!CanSendActivePlayerCommand(state) || state.PendingChoice != null || instanceId <= 0)
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
        if (!CanSendActivePlayerCommand(state) || state.PendingChoice != null || string.IsNullOrEmpty(definitionId))
            return;

        photonView.RPC(
            nameof(RpcRequestBuyCard),
            RpcTarget.MasterClient,
            NetworkGameState.LocalPlayerId,
            definitionId,
            state.Version,
            state.AuthorityEpoch);
    }

    /// <summary>
    /// Generic card resolution entry point. Photon transports each effect as JSON so the
    /// networking layer does not need a custom Photon serializer for every new effect type.
    /// </summary>
    public void ApplyGenericEffects(List<GenericCardEffect> effects, int sourceCardInstanceId = 0)
    {
        GameStateSnapshot state = NetworkGameState.State;
        if (!CanSendActivePlayerCommand(state) || state.PendingChoice != null || effects == null || effects.Count == 0)
            return;

        string[] effectJson = new string[effects.Count];
        for (int i = 0; i < effects.Count; i++)
            effectJson[i] = effects[i] != null ? JsonUtility.ToJson(effects[i]) : string.Empty;

        photonView.RPC(
            nameof(RpcRequestGenericEffects),
            RpcTarget.MasterClient,
            NetworkGameState.LocalPlayerId,
            effectJson,
            sourceCardInstanceId,
            state.Version,
            state.AuthorityEpoch);
    }

    public void TogglePendingChoiceCard(int instanceId)
    {
        GameStateSnapshot state = NetworkGameState.State;
        if (!CanSendChoiceCommand(state) || instanceId <= 0)
            return;

        photonView.RPC(
            nameof(RpcTogglePendingChoiceCard),
            RpcTarget.MasterClient,
            NetworkGameState.LocalPlayerId,
            instanceId,
            state.Version,
            state.AuthorityEpoch);
    }

    public void ResolvePendingChoice()
    {
        GameStateSnapshot state = NetworkGameState.State;
        if (!CanSendChoiceCommand(state))
            return;

        photonView.RPC(
            nameof(RpcResolvePendingChoice),
            RpcTarget.MasterClient,
            NetworkGameState.LocalPlayerId,
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
        int expectedVersion,
        int expectedAuthorityEpoch,
        PhotonMessageInfo info)
    {
        if (!ValidateSender(requesterPlayerId, info))
            return;

        if (!NetworkGameState.TryAdvancePhase(requesterPlayerId, expectedVersion, expectedAuthorityEpoch))
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

    [PunRPC]
    private void RpcRequestGenericEffects(
        string requesterPlayerId,
        string[] effectJson,
        int sourceCardInstanceId,
        int expectedVersion,
        int expectedAuthorityEpoch,
        PhotonMessageInfo info)
    {
        if (!ValidateSender(requesterPlayerId, info) || effectJson == null)
            return;

        List<GenericCardEffect> effects = new List<GenericCardEffect>();
        foreach (string json in effectJson)
        {
            if (string.IsNullOrEmpty(json))
                continue;

            GenericCardEffect effect = JsonUtility.FromJson<GenericCardEffect>(json);
            if (effect != null)
                effects.Add(effect);
        }

        if (!NetworkGameState.TryApplyGenericEffects(
                requesterPlayerId,
                effects,
                sourceCardInstanceId,
                expectedVersion,
                expectedAuthorityEpoch))
            Debug.LogWarning("Rejected stale or invalid generic effect command.");
    }

    [PunRPC]
    private void RpcTogglePendingChoiceCard(
        string requesterPlayerId,
        int instanceId,
        int expectedVersion,
        int expectedAuthorityEpoch,
        PhotonMessageInfo info)
    {
        if (!ValidateSender(requesterPlayerId, info))
            return;

        if (!NetworkGameState.TryTogglePendingChoiceSelection(
                requesterPlayerId,
                instanceId,
                expectedVersion,
                expectedAuthorityEpoch))
            Debug.LogWarning("Rejected pending-choice card selection.");
    }

    [PunRPC]
    private void RpcResolvePendingChoice(
        string requesterPlayerId,
        int expectedVersion,
        int expectedAuthorityEpoch,
        PhotonMessageInfo info)
    {
        if (!ValidateSender(requesterPlayerId, info))
            return;

        if (!NetworkGameState.TryResolvePendingChoice(
                requesterPlayerId,
                expectedVersion,
                expectedAuthorityEpoch))
            Debug.LogWarning("Rejected pending-choice resolution.");
    }

    private static bool CanSendActivePlayerCommand(GameStateSnapshot state)
    {
        return state != null &&
               state.IsStarted &&
               !state.IsPaused &&
               state.ActivePlayerId == NetworkGameState.LocalPlayerId;
    }

    private static bool CanSendChoiceCommand(GameStateSnapshot state)
    {
        return state != null &&
               state.IsStarted &&
               !state.IsPaused &&
               state.PendingChoice != null &&
               state.PendingChoice.IsFor(NetworkGameState.LocalPlayerId);
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

        if (state.ActivePlayerId == NetworkGameState.LocalPlayerId && _playerHandler != null)
            _playerHandler.BeginTurn();
    }
}
