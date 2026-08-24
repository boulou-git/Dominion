using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Adds gameplay meaning to an existing hand-card visual without owning the hand layout.
/// Short left click requests the single PlayCard command when the current phase/type allows it.
/// Left-drag is reserved exclusively for reordering the local hand and never plays the card.
/// The Master remains authoritative; this local check is only an interaction affordance.
/// </summary>
public sealed class HandGameplayInteraction : MonoBehaviour, IBeginDragHandler, IEndDragHandler
{
    private int _instanceId;
    private RectTransform _playTarget;
    private Action<int> _playRequested;
    private CardPointerInteraction _pointer;
    private HandCardMotion _motion;
    private bool _playable;
    private bool _requestPending;
    private bool _dragging;
    private int _suppressPrimaryUntilFrame = -1;

    public void Bind(int instanceId, RectTransform playTarget, Action<int> playRequested, bool playable)
    {
        _instanceId = instanceId;
        _playTarget = playTarget;
        _playRequested = playRequested;
        _playable = playable;

        _motion = GetComponent<HandCardMotion>();
        _pointer = GetComponent<CardPointerInteraction>();
        if (_pointer == null)
            _pointer = gameObject.AddComponent<CardPointerInteraction>();

        _pointer.PrimaryActionRequested -= OnPrimaryAction;
        _pointer.PrimaryActionRequested += OnPrimaryAction;
    }

    public void SetPlayable(bool playable)
    {
        _playable = playable;
    }

    private void OnPrimaryAction()
    {
        if ((!_playable && !CanPlayFromCurrentState()) ||
            _requestPending ||
            _dragging ||
            Time.frameCount <= _suppressPrimaryUntilFrame)
            return;

        StartPlayAnimation();
    }

    private bool CanPlayFromCurrentState()
    {
        GameStateSnapshot state = NetworkGameState.State;
        if (state == null || !state.IsStarted || state.IsPaused ||
            state.ActivePlayerId != NetworkGameState.LocalPlayerId ||
            (state.Resolution != null && state.Resolution.IsActive))
            return false;

        CardInstance instance = NetworkGameState.FindCardInstance(state, _instanceId);
        if (instance == null)
            return false;

        ExtensionPackageData extension;
        ExtensionCardData definition;
        if (!RoomGameSetup.TryResolveCard(instance.DefinitionId, out extension, out definition))
            return false;

        if (string.Equals(state.Phase, NetworkGameState.ActionPhase, StringComparison.Ordinal))
        {
            PlayerStateSnapshot localPlayer = state.Players != null
                ? state.Players.Find(player => player != null && player.PlayerId == NetworkGameState.LocalPlayerId)
                : null;
            return localPlayer != null &&
                   localPlayer.Actions > 0 &&
                   CardDefinitionRules.HasType(definition, "Action");
        }

        return string.Equals(state.Phase, NetworkGameState.BuyPhase, StringComparison.Ordinal) &&
               CardDefinitionRules.HasType(definition, "Trésor");
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
            _dragging = true;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left)
            return;

        _dragging = false;
        _suppressPrimaryUntilFrame = Time.frameCount + 1;
    }

    private void StartPlayAnimation()
    {
        _requestPending = true;

        if (_motion != null && _playTarget != null)
        {
            _motion.PlayTo(_playTarget, SendPlayRequest);
            if (_motion.IsPlaying)
                return;
        }

        SendPlayRequest();
    }

    private void SendPlayRequest()
    {
        _playRequested?.Invoke(_instanceId);
    }

    private void OnDestroy()
    {
        if (_pointer != null)
            _pointer.PrimaryActionRequested -= OnPrimaryAction;
    }
}
