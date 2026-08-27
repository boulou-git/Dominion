using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Prefab-backed control for choosing a position in a deck. A value of 1 is the top
/// of the deck and a value of 0 is the bottom; the submitted option remains the
/// integer position expected by the rules engine.
/// </summary>
public sealed class DeckPositionDecisionView : MonoBehaviour, IPointerDownHandler, IDragHandler
{
    private RectTransform _track;
    private RectTransform _handle;
    private Text _valueText;
    private IReadOnlyList<string> _optionIds;
    private IReadOnlyList<string> _optionLabels;
    private Action<string> _selectionChanged;
    private float _percentage = 1f;

    private void Awake()
    {
        BindPrefab();
    }

    public bool Configure(IReadOnlyList<string> optionIds, IReadOnlyList<string> optionLabels,
        Action<string> selectionChanged)
    {
        if (!BindPrefab() || optionIds == null || optionIds.Count == 0)
            return false;

        _optionIds = optionIds;
        _optionLabels = optionLabels;
        _selectionChanged = selectionChanged;
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(transform as RectTransform);
        SetPercentage(1f);
        return true;
    }

    public void ResetView()
    {
        _optionIds = null;
        _optionLabels = null;
        _selectionChanged = null;
    }

    public static int PositionFromPercentage(float percentage, int optionCount)
    {
        if (optionCount <= 1)
            return 0;
        return Mathf.FloorToInt(Mathf.Clamp01(percentage) * (optionCount - 1) + 0.5f);
    }

    public void OnPointerDown(PointerEventData eventData) => UpdateFromPointer(eventData);

    public void OnDrag(PointerEventData eventData) => UpdateFromPointer(eventData);

    private void UpdateFromPointer(PointerEventData eventData)
    {
        if (_track == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _track, eventData.position, eventData.pressEventCamera, out Vector2 local))
            return;
        SetPercentage(Mathf.InverseLerp(_track.rect.yMin, _track.rect.yMax, local.y));
    }

    private void SetPercentage(float value)
    {
        if (_optionIds == null || _optionIds.Count == 0)
            return;

        _percentage = Mathf.Clamp01(value);
        if (_handle != null && _track != null)
        {
            float halfHandle = _handle.rect.height * 0.5f;
            float y = Mathf.Lerp(_track.rect.yMin + halfHandle, _track.rect.yMax - halfHandle, _percentage);
            _handle.anchoredPosition = new Vector2(_handle.anchoredPosition.x, y);
        }
        int index = PositionFromPercentage(_percentage, _optionIds.Count);
        string label = _optionLabels != null && index < _optionLabels.Count
            ? _optionLabels[index]
            : "Position " + (index + 1);
        if (_valueText != null)
            _valueText.text = Mathf.RoundToInt(_percentage * 100f) + " %\n" + label;
        _selectionChanged?.Invoke(_optionIds[index]);
    }

    private bool BindPrefab()
    {
        if (_track == null)
            _track = transform.Find("Track") as RectTransform;
        if (_handle == null)
            _handle = transform.Find("Track/Handle") as RectTransform;
        if (_valueText == null)
            _valueText = transform.Find("Value")?.GetComponent<Text>();
        return _track != null && _handle != null && _valueText != null;
    }
}
