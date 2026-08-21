using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Adds gameplay meaning to an existing hand-card visual without owning the hand layout.
/// Left click plays an allowed card; dragging it onto the play area does the same.
/// </summary>
public sealed class HandGameplayInteraction : MonoBehaviour, IEndDragHandler
{
    private int _instanceId;
    private RectTransform _playTarget;
    private Action<int> _playRequested;
    private CardPointerInteraction _pointer;
    private HandCardMotion _motion;
    private bool _playable;
    private bool _requestPending;

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
        if (!_playable || _requestPending)
            return;

        StartPlayAnimation();
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!_playable || _requestPending || _playTarget == null || eventData.button != PointerEventData.InputButton.Left)
            return;

        if (!RectTransformUtility.RectangleContainsScreenPoint(_playTarget, eventData.position, eventData.pressEventCamera))
            return;

        // HandCardMotion also receives OnEndDrag. Waiting one frame guarantees its
        // local reorder cleanup is finished before the play animation starts.
        StartCoroutine(PlayAfterDragFrame());
    }

    private IEnumerator PlayAfterDragFrame()
    {
        yield return null;
        if (_playable && !_requestPending)
            StartPlayAnimation();
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
