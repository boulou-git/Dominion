using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Purely visual motion for cards displayed in the local hand.
/// Hover motion never changes game state. PlayTo is called only after the rules/network
/// layer has accepted that the card is actually being played.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public sealed class HandCardMotion : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private float _hoverLift = 28f;
    [SerializeField] private float _hoverScale = 1.12f;
    [SerializeField] private float _hoverSpeed = 14f;
    [SerializeField] private float _playDuration = 0.28f;

    private RectTransform _rect;
    private Vector3 _visualOffset;
    private Vector3 _targetOffset;
    private Vector3 _targetScale = Vector3.one;
    private bool _hovered;
    private bool _playing;
    private int _originalSiblingIndex;

    public bool IsPlaying => _playing;

    private void Awake()
    {
        _rect = GetComponent<RectTransform>();
        _originalSiblingIndex = transform.GetSiblingIndex();
    }

    private void OnDisable()
    {
        _hovered = false;
        _playing = false;
        _visualOffset = Vector3.zero;
        _targetOffset = Vector3.zero;
        _targetScale = Vector3.one;
        transform.localScale = Vector3.one;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_playing)
            return;

        _hovered = true;
        _targetOffset = new Vector3(0f, _hoverLift, 0f);
        _targetScale = Vector3.one * _hoverScale;
        transform.SetAsLastSibling();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (_playing)
            return;

        _hovered = false;
        _targetOffset = Vector3.zero;
        _targetScale = Vector3.one;
        RestoreSiblingOrder();
    }

    private void LateUpdate()
    {
        if (_rect == null || _playing)
            return;

        float t = 1f - Mathf.Exp(-_hoverSpeed * Time.unscaledDeltaTime);
        _visualOffset = Vector3.Lerp(_visualOffset, _targetOffset, t);
        transform.localScale = Vector3.Lerp(transform.localScale, _targetScale, t);

        // HorizontalLayoutGroup owns the base position; this offset is applied after layout.
        _rect.anchoredPosition += (Vector2)_visualOffset;
    }

    /// <summary>
    /// Visually moves this card to the centre of the target area. The caller remains
    /// responsible for committing/rebuilding the authoritative zones afterwards.
    /// </summary>
    public void PlayTo(RectTransform targetArea, Action completed = null)
    {
        if (_playing || targetArea == null || !isActiveAndEnabled)
            return;

        StartCoroutine(PlayRoutine(targetArea, completed));
    }

    private IEnumerator PlayRoutine(RectTransform targetArea, Action completed)
    {
        _playing = true;
        _hovered = false;

        Canvas canvas = GetComponentInParent<Canvas>();
        RectTransform animationParent = canvas != null ? canvas.transform as RectTransform : _rect.parent as RectTransform;
        if (animationParent == null)
        {
            _playing = false;
            completed?.Invoke();
            yield break;
        }

        Vector3 startWorld = _rect.position;
        Vector3 startScale = _rect.lossyScale;
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

    private void RestoreSiblingOrder()
    {
        if (transform.parent == null)
            return;

        int maxIndex = transform.parent.childCount - 1;
        transform.SetSiblingIndex(Mathf.Clamp(_originalSiblingIndex, 0, maxIndex));
    }
}
