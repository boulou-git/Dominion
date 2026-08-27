using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Small reusable runtime binding for one Reserve pile: quantity, contextual availability,
/// left-click purchase, right-click inspection and durable supply-choice interaction.
/// </summary>
public sealed class SupplyPileInteractionBinding : MonoBehaviour
{
    private static readonly Color AvailableColor = Color.white;
    private static readonly Color UnavailableColor = new Color(0.68f, 0.68f, 0.68f, 1f);
    private static readonly Color EmptyColor = new Color(0.42f, 0.42f, 0.42f, 1f);

    private string _definitionId;
    private Image _image;
    private Outline _outline;
    private CardSelectionHalo _selectionHalo;
    private Text _countText;
    private CardPointerInteraction _pointer;
    private Sprite _sprite;
    private Action<string> _buyRequested;
    private Action<Sprite, ExtensionCardData> _inspectRequested;
    private ExtensionCardData _definition;
    private bool _buyable;
    private bool _hasCards = true;
    private bool _purchaseAnimationRunning;

    private bool _decisionActive;
    private bool _decisionCandidate;
    private bool _decisionSelected;
    private Action<string> _decisionRequested;

    public string DefinitionId => _definitionId;

    public void Bind(string definitionId, Sprite sprite, Action<string> buyRequested,
        Action<Sprite, ExtensionCardData> inspectRequested)
    {
        _definitionId = definitionId;
        _sprite = sprite;
        _buyRequested = buyRequested;
        _inspectRequested = inspectRequested;
        ExtensionPackageData extension;
        RoomGameSetup.TryResolveCard(definitionId, out extension, out _definition);
        DynamicCardCostView.Attach(gameObject, _definition);

        _image = GetComponent<Image>();
        if (_image != null)
        {
            _image.raycastTarget = true;
            if (_image.sprite == null) _image.sprite = sprite;
        }

        Button legacyButton = GetComponent<Button>();
        if (legacyButton != null)
        {
            legacyButton.onClick.RemoveAllListeners();
            legacyButton.enabled = false;
        }

        _pointer = GetComponent<CardPointerInteraction>();
        if (_pointer == null) _pointer = gameObject.AddComponent<CardPointerInteraction>();
        _pointer.InspectOnLongPress = false;
        _pointer.PrimaryActionRequested -= OnPrimaryAction;
        _pointer.InspectRequested -= OnInspect;
        _pointer.PrimaryActionRequested += OnPrimaryAction;
        _pointer.InspectRequested += OnInspect;

        _outline = GetComponent<Outline>();
        if (_outline == null) _outline = gameObject.AddComponent<Outline>();
        _outline.useGraphicAlpha = false;
        _outline.enabled = false;

        _selectionHalo = GetComponent<CardSelectionHalo>();
        if (_selectionHalo == null) _selectionHalo = gameObject.AddComponent<CardSelectionHalo>();
        _selectionHalo.SetVisible(false);

        _countText = FindOrCreateCountText(transform);
        EnsureCountBadgeVisible();
        RefreshAvailabilityVisual();
    }

    public void SetRemaining(int remaining)
    {
        int value = Mathf.Max(0, remaining);
        _hasCards = value > 0;
        if (_countText != null) _countText.text = value.ToString();
        EnsureCountBadgeVisible();
        RefreshAvailabilityVisual();
    }

    public void SetBuyable(bool buyable)
    {
        _buyable = buyable;
        RefreshAvailabilityVisual();
    }

    public void SetDecisionChoice(bool active, bool candidate, bool selected, Action<string> decisionRequested)
    {
        _decisionActive = active;
        _decisionCandidate = candidate;
        _decisionSelected = selected;
        _decisionRequested = active ? decisionRequested : null;
        RefreshAvailabilityVisual();
    }

    private void EnsureCountBadgeVisible()
    {
        if (_countText == null) return;
        _countText.enabled = true;
        Transform badgeTransform = _countText.transform.parent;
        if (badgeTransform == null) return;
        badgeTransform.gameObject.SetActive(true);
        Image badgeImage = badgeTransform.GetComponent<Image>();
        if (badgeImage != null) badgeImage.enabled = true;
        badgeTransform.SetAsLastSibling();
    }

    private void RefreshAvailabilityVisual()
    {
        if (_image != null)
        {
            if (_decisionActive)
                _image.color = !_hasCards ? EmptyColor : (_decisionCandidate ? AvailableColor : UnavailableColor);
            else
                _image.color = !_hasCards ? EmptyColor : (_buyable ? AvailableColor : UnavailableColor);
        }

        if (_selectionHalo != null)
            _selectionHalo.SetVisible(_decisionActive && _hasCards && _decisionCandidate && _decisionSelected);

        if (_outline != null)
        {
            if (_decisionActive)
            {
                // Decision selection uses CardSelectionHalo so the card artwork is never duplicated.
                _outline.enabled = false;
            }
            else
            {
                _outline.effectColor = new Color(1f, 0.78f, 0.18f, 0.90f);
                _outline.effectDistance = new Vector2(3f, -3f);
                _outline.enabled = _hasCards && _buyable && !_purchaseAnimationRunning;
            }
        }
    }

    private void OnPrimaryAction()
    {
        if (_decisionActive)
        {
            if (_decisionCandidate && _hasCards && !string.IsNullOrEmpty(_definitionId))
                _decisionRequested?.Invoke(_definitionId);
            return;
        }

        if (!_buyable || _purchaseAnimationRunning || string.IsNullOrEmpty(_definitionId)) return;
        if (_sprite != null) StartCoroutine(PurchaseAnimationRoutine());
        _buyRequested?.Invoke(_definitionId);
    }

    private IEnumerator PurchaseAnimationRoutine()
    {
        _purchaseAnimationRunning = true;
        RefreshAvailabilityVisual();

        RectTransform source = transform as RectTransform;
        RectTransform discard = FindDeepChild(transform.root, "Discard") as RectTransform;
        Canvas canvas = GetComponentInParent<Canvas>();
        if (source == null || discard == null || canvas == null || _sprite == null)
        {
            _purchaseAnimationRunning = false;
            RefreshAvailabilityVisual();
            yield break;
        }

        GameObject flyingObject = new GameObject("PurchasedCardAnimation_" + _definitionId.Replace(':', '_'), typeof(RectTransform), typeof(Image));
        flyingObject.transform.SetParent(canvas.transform, false);
        flyingObject.transform.SetAsLastSibling();
        RectTransform flying = flyingObject.GetComponent<RectTransform>();
        Vector2 sourceSize = source.rect.size;
        if (sourceSize.x <= 1f || sourceSize.y <= 1f) sourceSize = new Vector2(96f, 148f);
        flying.sizeDelta = sourceSize;
        flying.pivot = new Vector2(0.5f, 0.5f);

        Image flyingImage = flyingObject.GetComponent<Image>();
        flyingImage.sprite = _sprite;
        flyingImage.preserveAspect = true;
        flyingImage.raycastTarget = false;
        flyingImage.color = Color.white;
        DynamicCardCostView.Attach(flyingObject, _definition);

        Vector3 startWorld = source.TransformPoint(source.rect.center);
        Vector3 targetWorld = discard.TransformPoint(discard.rect.center);
        flying.position = startWorld;

        const float duration = 0.34f;
        float elapsed = 0f;
        while (elapsed < duration && flying != null)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            Vector3 position = Vector3.Lerp(startWorld, targetWorld, eased);
            position.y += Mathf.Sin(t * Mathf.PI) * 24f;
            flying.position = position;
            flying.localScale = Vector3.Lerp(Vector3.one, Vector3.one * 0.58f, eased);
            yield return null;
        }

        if (flyingObject != null) Destroy(flyingObject);
        _purchaseAnimationRunning = false;
        RefreshAvailabilityVisual();
    }

    private void OnInspect()
    {
        if (_sprite != null) _inspectRequested?.Invoke(_sprite, _definition);
    }

    private void OnDestroy()
    {
        if (_pointer == null) return;
        _pointer.PrimaryActionRequested -= OnPrimaryAction;
        _pointer.InspectRequested -= OnInspect;
    }

    private static Text FindOrCreateCountText(Transform parent)
    {
        Transform existingBadge = parent.Find("RemainingCount");
        if (existingBadge != null)
        {
            existingBadge.gameObject.SetActive(true);
            existingBadge.SetAsLastSibling();
            Text existing = existingBadge.GetComponentInChildren<Text>(true);
            if (existing != null) return existing;
        }

        GameObject badgeObject = new GameObject("RemainingCount", typeof(RectTransform), typeof(Image));
        badgeObject.transform.SetParent(parent, false);
        badgeObject.transform.SetAsLastSibling();
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

    private static Transform FindDeepChild(Transform parent, string childName)
    {
        if (parent == null) return null;
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (string.Equals(child.name, childName, StringComparison.Ordinal)) return child;
            Transform nested = FindDeepChild(child, childName);
            if (nested != null) return nested;
        }
        return null;
    }
}
