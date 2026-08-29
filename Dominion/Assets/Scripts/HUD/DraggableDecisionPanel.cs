using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Moves a decision surface from its prefab-authored title bar, keeps it
/// inside the Canvas, and restores the last position used during the session.
/// Double-clicking the title bar restores the prefab position.
/// </summary>
public sealed class DraggableDecisionPanel : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
{
    [SerializeField] private float _edgePadding = 12f;

    private static readonly Dictionary<string, Vector2> RememberedPositions = new Dictionary<string, Vector2>();
    private RectTransform _panel;
    private RectTransform _canvasRect;
    private Vector2 _pointerOffset;
    private Vector2 _defaultPosition;
    private string _positionKey;

    private void Awake()
    {
        _panel = transform.parent as RectTransform;
        Canvas canvas = GetComponentInParent<Canvas>();
        _canvasRect = canvas != null ? canvas.transform as RectTransform : null;
        if (_panel != null)
        {
            _defaultPosition = _panel.anchoredPosition;
            _positionKey = _panel.gameObject.name;
            if (RememberedPositions.TryGetValue(_positionKey, out Vector2 rememberedPosition))
                _panel.anchoredPosition = rememberedPosition;
        }
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
        ClampToCanvas();
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (_panel == null)
            return;
        ClampToCanvas();
        RememberPosition();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (_panel == null || eventData.clickCount < 2)
            return;
        _panel.anchoredPosition = _defaultPosition;
        ClampToCanvas();
        RememberPosition();
    }

    private void RememberPosition()
    {
        if (_panel != null && !string.IsNullOrEmpty(_positionKey))
            RememberedPositions[_positionKey] = _panel.anchoredPosition;
    }

    private void ClampToCanvas()
    {
        if (_panel == null || _canvasRect == null)
            return;

        Vector3[] worldCorners = new Vector3[4];
        _panel.GetWorldCorners(worldCorners);
        Vector3 bottomLeft = _canvasRect.InverseTransformPoint(worldCorners[0]);
        Vector3 topRight = _canvasRect.InverseTransformPoint(worldCorners[2]);
        Rect canvasBounds = _canvasRect.rect;
        float deltaX = 0f;
        float deltaY = 0f;
        if (bottomLeft.x < canvasBounds.xMin + _edgePadding)
            deltaX = canvasBounds.xMin + _edgePadding - bottomLeft.x;
        else if (topRight.x > canvasBounds.xMax - _edgePadding)
            deltaX = canvasBounds.xMax - _edgePadding - topRight.x;
        if (bottomLeft.y < canvasBounds.yMin + _edgePadding)
            deltaY = canvasBounds.yMin + _edgePadding - bottomLeft.y;
        else if (topRight.y > canvasBounds.yMax - _edgePadding)
            deltaY = canvasBounds.yMax - _edgePadding - topRight.y;

        if (!Mathf.Approximately(deltaX, 0f) || !Mathf.Approximately(deltaY, 0f))
            _panel.position += _canvasRect.TransformVector(new Vector3(deltaX, deltaY, 0f));
    }
}
