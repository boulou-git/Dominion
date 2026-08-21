using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Small reusable runtime binding for one Reserve pile: quantity, buy highlight,
/// left-click purchase and right-click inspection.
/// </summary>
public sealed class SupplyPileInteractionBinding : MonoBehaviour
{
    private string _definitionId;
    private Image _image;
    private Outline _outline;
    private Text _countText;
    private CardPointerInteraction _pointer;
    private Sprite _sprite;
    private Action<string> _buyRequested;
    private Action<Sprite> _inspectRequested;
    private bool _buyable;

    public string DefinitionId => _definitionId;

    public void Bind(
        string definitionId,
        Sprite sprite,
        Action<string> buyRequested,
        Action<Sprite> inspectRequested)
    {
        _definitionId = definitionId;
        _sprite = sprite;
        _buyRequested = buyRequested;
        _inspectRequested = inspectRequested;

        _image = GetComponent<Image>();
        if (_image != null)
        {
            _image.raycastTarget = true;
            if (_image.sprite == null)
                _image.sprite = sprite;
        }

        Button legacyButton = GetComponent<Button>();
        if (legacyButton != null)
        {
            legacyButton.onClick.RemoveAllListeners();
            legacyButton.enabled = false;
        }

        _pointer = GetComponent<CardPointerInteraction>();
        if (_pointer == null)
            _pointer = gameObject.AddComponent<CardPointerInteraction>();

        _pointer.InspectOnLongPress = false;
        _pointer.PrimaryActionRequested -= OnPrimaryAction;
        _pointer.InspectRequested -= OnInspect;
        _pointer.PrimaryActionRequested += OnPrimaryAction;
        _pointer.InspectRequested += OnInspect;

        _outline = GetComponent<Outline>();
        if (_outline == null)
            _outline = gameObject.AddComponent<Outline>();
        _outline.effectColor = new Color(1f, 0.78f, 0.18f, 0.90f);
        _outline.effectDistance = new Vector2(3f, -3f);
        _outline.useGraphicAlpha = false;
        _outline.enabled = false;

        _countText = FindOrCreateCountText(transform);
    }

    public void SetRemaining(int remaining)
    {
        int value = Mathf.Max(0, remaining);
        if (_countText != null)
            _countText.text = value.ToString();

        if (_image != null)
            _image.color = value > 0 ? Color.white : new Color(0.46f, 0.46f, 0.46f, 1f);
    }

    public void SetBuyable(bool buyable)
    {
        _buyable = buyable;
        if (_outline != null)
            _outline.enabled = buyable;
    }

    private void OnPrimaryAction()
    {
        if (_buyable && !string.IsNullOrEmpty(_definitionId))
            _buyRequested?.Invoke(_definitionId);
    }

    private void OnInspect()
    {
        if (_sprite != null)
            _inspectRequested?.Invoke(_sprite);
    }

    private void OnDestroy()
    {
        if (_pointer == null)
            return;

        _pointer.PrimaryActionRequested -= OnPrimaryAction;
        _pointer.InspectRequested -= OnInspect;
    }

    private static Text FindOrCreateCountText(Transform parent)
    {
        Transform existingBadge = parent.Find("RemainingCount");
        if (existingBadge != null)
        {
            Text existing = existingBadge.GetComponentInChildren<Text>();
            if (existing != null)
                return existing;
        }

        GameObject badgeObject = new GameObject("RemainingCount", typeof(RectTransform), typeof(Image));
        badgeObject.transform.SetParent(parent, false);
        RectTransform badgeRect = badgeObject.GetComponent<RectTransform>();
        badgeRect.anchorMin = new Vector2(0.64f, 0.79f);
        badgeRect.anchorMax = new Vector2(0.98f, 0.98f);
        badgeRect.offsetMin = Vector2.zero;
        badgeRect.offsetMax = Vector2.zero;

        Image badge = badgeObject.GetComponent<Image>();
        badge.color = new Color(0.03f, 0.03f, 0.03f, 0.88f);
        badge.raycastTarget = false;

        GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(Outline));
        textObject.transform.SetParent(badgeObject.transform, false);
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        Text text = textObject.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 20;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.raycastTarget = false;
        text.text = "—";

        Outline outline = textObject.GetComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
        outline.effectDistance = new Vector2(1f, -1f);
        return text;
    }
}
