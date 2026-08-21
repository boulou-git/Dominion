using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Local-hand visual behaviour: hover, drag-to-reorder and optional play animation.
/// Reordering is presentation-only and never mutates authoritative game state.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public sealed class HandCardMotion : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IBeginDragHandler,
    IDragHandler,
    IEndDragHandler
{
    [SerializeField] private float _hoverLift = 28f;
    [SerializeField] private float _hoverScale = 1.12f;
    [SerializeField] private float _hoverSpeed = 14f;
    [SerializeField] private float _playDuration = 0.28f;

    private RectTransform _rect;
    private LayoutElement _layoutElement;
    private Vector3 _visualOffset;
    private Vector3 _lastAppliedOffset;
    private Vector3 _targetOffset;
    private Vector3 _targetScale = Vector3.one;
    private bool _playing;
    private bool _dragging;
    private RectTransform _dragPlaceholder;
    private Vector2 _dragPointerOffset;
    private int _instanceId;

    public bool IsPlaying => _playing;
    public bool IsDragging => _dragging;
    public int InstanceId => _instanceId;

    public event Action OrderChanged;

    private void Awake()
    {
        _rect = GetComponent<RectTransform>();
        _layoutElement = GetComponent<LayoutElement>();
    }

    public void BindInstance(int instanceId, Action orderChanged)
    {
        _instanceId = instanceId;
        if (orderChanged != null)
            OrderChanged += orderChanged;
    }

    private void OnDisable()
    {
        if (_rect != null && !_dragging)
            _rect.anchoredPosition -= (Vector2)_lastAppliedOffset;

        if (_layoutElement != null)
            _layoutElement.ignoreLayout = false;

        if (_dragPlaceholder != null)
            Destroy(_dragPlaceholder.gameObject);

        _dragPlaceholder = null;
        _playing = false;
        _dragging = false;
        _visualOffset = Vector3.zero;
        _lastAppliedOffset = Vector3.zero;
        _targetOffset = Vector3.zero;
        _targetScale = Vector3.one;
        transform.localScale = Vector3.one;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_playing || _dragging)
            return;

        _targetOffset = new Vector3(0f, _hoverLift, 0f);
        _targetScale = Vector3.one * _hoverScale;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (_playing || _dragging)
            return;

        _targetOffset = Vector3.zero;
        _targetScale = Vector3.one;
    }

    private void LateUpdate()
    {
        if (_rect == null || _playing || _dragging)
            return;

        // The layout owns the base position; this component adds only a reversible visual offset.
        _rect.anchoredPosition -= (Vector2)_lastAppliedOffset;

        float t = 1f - Mathf.Exp(-_hoverSpeed * Time.unscaledDeltaTime);
        _visualOffset = Vector3.Lerp(_visualOffset, _targetOffset, t);
        transform.localScale = Vector3.Lerp(transform.localScale, _targetScale, t);

        _rect.anchoredPosition += (Vector2)_visualOffset;
        _lastAppliedOffset = _visualOffset;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left || _playing || _dragging)
            return;

        RectTransform parentRect = transform.parent as RectTransform;
        if (parentRect == null)
            return;

        _dragging = true;
        _targetOffset = Vector3.zero;
        _targetScale = Vector3.one;

        _rect.anchoredPosition -= (Vector2)_lastAppliedOffset;
        _lastAppliedOffset = Vector3.zero;
        _visualOffset = Vector3.zero;

        GameObject placeholderObject = new GameObject("HandDragPlaceholder", typeof(RectTransform), typeof(LayoutElement));
        placeholderObject.transform.SetParent(parentRect, false);
        _dragPlaceholder = placeholderObject.GetComponent<RectTransform>();

        LayoutElement placeholderLayout = placeholderObject.GetComponent<LayoutElement>();
        float width = _layoutElement != null && _layoutElement.preferredWidth > 0f
            ? _layoutElement.preferredWidth
            : Mathf.Max(1f, _rect.rect.width);
        float height = _layoutElement != null && _layoutElement.preferredHeight > 0f
            ? _layoutElement.preferredHeight
            : Mathf.Max(1f, _rect.rect.height);
        placeholderLayout.preferredWidth = width;
        placeholderLayout.minWidth = width;
        placeholderLayout.preferredHeight = height;
        placeholderLayout.minHeight = height;

        int originalIndex = transform.GetSiblingIndex();
        _dragPlaceholder.SetSiblingIndex(originalIndex);

        if (_layoutElement != null)
            _layoutElement.ignoreLayout = true;

        Vector2 pointerLocal;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, eventData.position, eventData.pressEventCamera, out pointerLocal);
        _dragPointerOffset = (Vector2)_rect.localPosition - pointerLocal;

        transform.SetAsLastSibling();
        transform.localScale = Vector3.one * 1.08f;
        UpdateDraggedPosition(eventData);
        UpdatePlaceholderIndex(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!_dragging || eventData.button != PointerEventData.InputButton.Left)
            return;

        UpdateDraggedPosition(eventData);
        UpdatePlaceholderIndex(eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!_dragging)
            return;

        RectTransform parentRect = transform.parent as RectTransform;
        int targetIndex = _dragPlaceholder != null
            ? _dragPlaceholder.GetSiblingIndex()
            : transform.GetSiblingIndex();

        if (_layoutElement != null)
            _layoutElement.ignoreLayout = false;

        transform.SetSiblingIndex(targetIndex);

        if (_dragPlaceholder != null)
            Destroy(_dragPlaceholder.gameObject);
        _dragPlaceholder = null;

        _dragging = false;
        transform.localScale = Vector3.one;
        _targetOffset = Vector3.zero;
        _targetScale = Vector3.one;

        if (parentRect != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(parentRect);

        OrderChanged?.Invoke();
    }

    private void UpdateDraggedPosition(PointerEventData eventData)
    {
        RectTransform parentRect = transform.parent as RectTransform;
        if (parentRect == null)
            return;

        Vector2 pointerLocal;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, eventData.position, eventData.pressEventCamera, out pointerLocal))
            return;

        Vector2 target = pointerLocal + _dragPointerOffset;
        _rect.localPosition = new Vector3(target.x, target.y, _rect.localPosition.z);
    }

    private void UpdatePlaceholderIndex(PointerEventData eventData)
    {
        if (_dragPlaceholder == null || transform.parent == null)
            return;

        int targetCardPosition = 0;
        Transform parent = transform.parent;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform sibling = parent.GetChild(i);
            if (sibling == transform || sibling == _dragPlaceholder)
                continue;

            HandCardMotion siblingCard = sibling.GetComponent<HandCardMotion>();
            RectTransform siblingRect = sibling as RectTransform;
            if (siblingCard == null || siblingRect == null)
                continue;

            Vector2 siblingCenter = RectTransformUtility.WorldToScreenPoint(
                eventData.pressEventCamera,
                siblingRect.TransformPoint(siblingRect.rect.center));

            if (eventData.position.x > siblingCenter.x)
                targetCardPosition++;
        }

        _dragPlaceholder.SetSiblingIndex(Mathf.Clamp(targetCardPosition, 0, parent.childCount - 1));
    }

    /// <summary>
    /// Visually moves this card to the centre of the target area. The caller remains
    /// responsible for committing/rebuilding authoritative zones afterwards.
    /// </summary>
    public void PlayTo(RectTransform targetArea, Action completed = null)
    {
        if (_playing || _dragging || targetArea == null || !isActiveAndEnabled)
            return;

        StartCoroutine(PlayRoutine(targetArea, completed));
    }

    private IEnumerator PlayRoutine(RectTransform targetArea, Action completed)
    {
        _playing = true;
        _targetOffset = Vector3.zero;
        _targetScale = Vector3.one;

        _rect.anchoredPosition -= (Vector2)_lastAppliedOffset;
        _lastAppliedOffset = Vector3.zero;
        _visualOffset = Vector3.zero;

        Canvas canvas = GetComponentInParent<Canvas>();
        RectTransform animationParent = canvas != null ? canvas.transform as RectTransform : _rect.parent as RectTransform;
        if (animationParent == null)
        {
            _playing = false;
            completed?.Invoke();
            yield break;
        }

        Vector3 startWorld = _rect.position;
        Vector3 targetWorld = targetArea.TransformPoint(targetArea.rect.center);

        transform.SetParent(animationParent, true);
        transform.SetAsLastSibling();

        float elapsed = 0f;
        while (elapsed < _playDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float linear = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, _playDuration));
            float eased = 1f - Mathf.Pow(1f - linear, 3f);

            _rect.position = Vector3.Lerp(startWorld, targetWorld, eased);
            transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one * 0.92f, eased);
            yield return null;
        }

        _rect.position = targetWorld;
        _playing = false;
        completed?.Invoke();
    }
}
