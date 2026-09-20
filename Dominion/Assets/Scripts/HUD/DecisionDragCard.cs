using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Moves only a UI preview; the game state is untouched until confirmation.</summary>
public sealed class DecisionDragCard : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
{
    public int InstanceId { get; private set; }
    public string DecisionId { get; private set; }
    public DecisionWorkspaceView Owner { get; private set; }
    private RectTransform _rect;
    private Transform _home;
    private int _index;
    private Vector2 _size;
    private CanvasGroup _group;
    private bool _dragging;
    private Action _clicked;
    private ScrollRect _quickScroll;

    public void Bind(DecisionWorkspaceView owner, string decisionId, int instanceId, Action clicked)
    {
        Owner = owner; DecisionId = decisionId; InstanceId = instanceId; _clicked = clicked;
        _rect = (RectTransform)transform;
        _group = GetComponent<CanvasGroup>();
    }

    public void BindQuick(Action clicked) { Owner = null; _clicked = clicked; }

    public void OnPointerClick(PointerEventData e)
    {
        if (!_dragging && !e.dragging && e.button == PointerEventData.InputButton.Left && (Owner == null || Owner.CanInteract(DecisionId)))
            _clicked?.Invoke();
    }

    public void OnBeginDrag(PointerEventData e)
    {
        if (Owner == null)
        {
            _quickScroll = GetComponentInParent<ScrollRect>();
            if (_quickScroll != null) { e.eligibleForClick = false; _quickScroll.OnBeginDrag(e); }
            return;
        }
        if (e.button != PointerEventData.InputButton.Left || !Owner.CanInteract(DecisionId)) return;
        _home = transform.parent; _index = transform.GetSiblingIndex(); _size = _rect.sizeDelta;
        Vector2 visualSize = _rect.rect.size;
        _dragging = true; e.eligibleForClick = false;
        transform.SetParent(Owner.DragLayer, true);
        _rect.sizeDelta = visualSize;
        _group.blocksRaycasts = false;
        OnDrag(e);
    }

    public void OnDrag(PointerEventData e)
    {
        if (Owner == null) { _quickScroll?.OnDrag(e); return; }
        if (!_dragging) return;
        if (!Owner.CanInteract(DecisionId)) { Restore(); return; }
        if (RectTransformUtility.ScreenPointToWorldPointInRectangle(Owner.DragLayer, e.position, e.pressEventCamera, out Vector3 point))
            _rect.position = point;
    }

    public void OnEndDrag(PointerEventData e)
    {
        if (Owner == null) { _quickScroll?.OnEndDrag(e); _quickScroll = null; return; }
        Restore();
        Owner?.RefreshPlacement();
    }

    private void OnDisable() { Restore(); }

    private void Restore()
    {
        if (!_dragging) return;
        _dragging = false;
        if (_home != null)
        {
            transform.SetParent(_home, false);
            transform.SetSiblingIndex(_index);
            _rect.sizeDelta = _size;
        }
        if (_group != null) _group.blocksRaycasts = true;
    }
}
