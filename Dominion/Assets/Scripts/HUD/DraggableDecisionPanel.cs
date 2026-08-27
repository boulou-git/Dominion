using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>Lets the decision panel be moved by dragging its prefab-authored prompt.</summary>
public sealed class DraggableDecisionPanel : MonoBehaviour, IBeginDragHandler, IDragHandler
{
    private RectTransform _panel;
    private RectTransform _canvasRect;
    private Vector2 _pointerOffset;

    private void Awake()
    {
        _panel = transform.parent as RectTransform;
        Canvas canvas = GetComponentInParent<Canvas>();
        _canvasRect = canvas != null ? canvas.transform as RectTransform : null;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (_panel == null || _canvasRect == null)
            return;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _canvasRect, eventData.position, eventData.pressEventCamera, out Vector2 pointer);
        _pointerOffset = _panel.anchoredPosition - pointer;
        _panel.SetAsLastSibling();
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (_panel == null || _canvasRect == null)
            return;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvasRect, eventData.position, eventData.pressEventCamera, out Vector2 pointer))
            return;

        _panel.anchoredPosition = pointer + _pointerOffset;
    }
}
