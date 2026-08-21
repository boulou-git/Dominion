using System;
using UnityEngine;

/// <summary>
/// Temporarily gives one hand card the meaning "select this for the current PendingChoice".
/// The normal HandGameplayInteraction stays attached but is disabled via SetPlayable(false).
/// </summary>
public sealed class PendingChoiceCardBinding : MonoBehaviour
{
    private int _instanceId;
    private bool _valid;
    private CardPointerInteraction _pointer;
    private Action<int> _selectionRequested;

    public void Bind(int instanceId, bool valid, Action<int> selectionRequested)
    {
        _instanceId = instanceId;
        _valid = valid;
        _selectionRequested = selectionRequested;

        _pointer = GetComponent<CardPointerInteraction>();
        if (_pointer == null)
            _pointer = gameObject.AddComponent<CardPointerInteraction>();

        _pointer.PrimaryActionRequested -= OnPrimaryAction;
        _pointer.PrimaryActionRequested += OnPrimaryAction;
    }

    public void Clear()
    {
        _valid = false;
        _selectionRequested = null;
        if (_pointer != null)
            _pointer.PrimaryActionRequested -= OnPrimaryAction;
    }

    private void OnPrimaryAction()
    {
        if (_valid && _instanceId > 0)
            _selectionRequested?.Invoke(_instanceId);
    }

    private void OnDestroy()
    {
        if (_pointer != null)
            _pointer.PrimaryActionRequested -= OnPrimaryAction;
    }
}
