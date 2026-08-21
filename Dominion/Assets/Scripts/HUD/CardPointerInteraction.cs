using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Shared pointer convention for card UI:
/// - short left click: primary/contextual action
/// - short right click: inspect
/// - optional long press: inspect
/// </summary>
public sealed class CardPointerInteraction : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
{
    public float LongPressSeconds = 0.45f;
    public bool InspectOnLongPress = true;

    public event Action PrimaryActionRequested;
    public event Action InspectRequested;

    private bool _pointerIsDown;
    private bool _longPressTriggered;
    private float _pointerDownTime;
    private PointerEventData.InputButton _button;

    private void Update()
    {
        if (!_pointerIsDown || _longPressTriggered || !InspectOnLongPress)
            return;

        if (Time.unscaledTime - _pointerDownTime < LongPressSeconds)
            return;

        _longPressTriggered = true;
        InspectRequested?.Invoke();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        _pointerIsDown = true;
        _longPressTriggered = false;
        _pointerDownTime = Time.unscaledTime;
        _button = eventData.button;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        _pointerIsDown = false;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (_longPressTriggered)
        {
            _longPressTriggered = false;
            return;
        }

        if (eventData.button == PointerEventData.InputButton.Right)
        {
            InspectRequested?.Invoke();
            return;
        }

        if (eventData.button == PointerEventData.InputButton.Left)
            PrimaryActionRequested?.Invoke();
    }

    private void OnDisable()
    {
        _pointerIsDown = false;
        _longPressTriggered = false;
    }
}
