using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Generic UI for any blocking player choice. It does not know which card caused the
/// choice: it only reads GameStateSnapshot.PendingChoice, shows a prompt and applies
/// availability lighting to the affected hand cards.
/// </summary>
public sealed class PendingChoiceUiController : MonoBehaviour
{
    private static readonly Color NormalCardColor = Color.white;
    private static readonly Color InvalidCardColor = new Color(0.62f, 0.62f, 0.62f, 1f);
    private static readonly Color SelectedCardColor = new Color(1f, 0.88f, 0.52f, 1f);

    private GameObject _popup;
    private Text _promptText;
    private Text _counterText;
    private Button _finishButton;
    private RectTransform _handRoot;
    private string _lastChoiceId;

    private void Awake()
    {
        ResolveUi();
        EnsurePopup();
        NetworkGameState.StateChanged += Refresh;
        Refresh(NetworkGameState.State);
    }

    private void OnDestroy()
    {
        NetworkGameState.StateChanged -= Refresh;
    }

    private void ResolveUi()
    {
        Transform localHand = FindDeepChild(transform, "LocalHand");
        Transform cards = FindDirectChild(localHand, "Cards");
        _handRoot = cards as RectTransform;
    }

    private void EnsurePopup()
    {
        if (_popup != null)
            return;

        Canvas canvas = GetComponentInParent<Canvas>();
        Transform parent = canvas != null ? canvas.transform : transform;

        _popup = new GameObject("PendingChoicePopup", typeof(RectTransform), typeof(Image));
        _popup.transform.SetParent(parent, false);
        RectTransform rect = _popup.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(560f, 190f);
        rect.anchoredPosition = new Vector2(0f, 20f);

        Image background = _popup.GetComponent<Image>();
        background.color = new Color(0.055f, 0.052f, 0.047f, 0.97f);

        Text title = CreateText(_popup.transform, "Title", "CHOIX REQUIS", 22, FontStyle.Bold);
        SetAnchors(title.rectTransform, new Vector2(0.05f, 0.72f), new Vector2(0.95f, 0.94f));

        _promptText = CreateText(_popup.transform, "Prompt", string.Empty, 18, FontStyle.Normal);
        _promptText.alignment = TextAnchor.MiddleLeft;
        SetAnchors(_promptText.rectTransform, new Vector2(0.05f, 0.31f), new Vector2(0.95f, 0.72f));

        _counterText = CreateText(_popup.transform, "Counter", "0 / 0", 16, FontStyle.Normal);
        _counterText.alignment = TextAnchor.MiddleLeft;
        _counterText.color = new Color(0.78f, 0.75f, 0.67f, 1f);
        SetAnchors(_counterText.rectTransform, new Vector2(0.05f, 0.07f), new Vector2(0.50f, 0.28f));

        GameObject buttonObject = new GameObject("FinishButton", typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(_popup.transform, false);
        RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
        SetAnchors(buttonRect, new Vector2(0.64f, 0.07f), new Vector2(0.95f, 0.28f));
        Image buttonImage = buttonObject.GetComponent<Image>();
        buttonImage.color = new Color(0.38f, 0.30f, 0.14f, 1f);
        _finishButton = buttonObject.GetComponent<Button>();
        Text buttonText = CreateText(buttonObject.transform, "Text", "TERMINER", 16, FontStyle.Bold);
        buttonText.alignment = TextAnchor.MiddleCenter;
        SetAnchors(buttonText.rectTransform, Vector2.zero, Vector2.one);

        _popup.SetActive(false);
    }

    private void Refresh(GameStateSnapshot state)
    {
        ResolveUi();
        EnsurePopup();

        PendingChoiceSnapshot choice = state != null ? state.PendingChoice : null;
        bool mine = choice != null && choice.IsFor(NetworkGameState.LocalPlayerId);
        if (_popup != null)
            _popup.SetActive(mine);

        if (!mine)
        {
            _lastChoiceId = null;
            RestoreHandVisuals();
            return;
        }

        _lastChoiceId = choice.ChoiceId;
        if (_promptText != null)
            _promptText.text = string.IsNullOrWhiteSpace(choice.Prompt) ? "Choisissez une carte." : choice.Prompt;

        int selected = choice.SelectedInstanceIds != null ? choice.SelectedInstanceIds.Count : 0;
        int max = Mathf.Max(choice.MinSelections, choice.MaxSelections);
        if (_counterText != null)
            _counterText.text = max > 0 ? selected + " / " + max + " sélectionnée(s)" : selected + " sélectionnée(s)";

        int minimum = choice.Optional ? 0 : Mathf.Max(0, choice.MinSelections);
        if (_finishButton != null)
            _finishButton.interactable = selected >= minimum && (max <= 0 || selected <= max);

        RefreshHandVisuals(choice);
    }

    private void RefreshHandVisuals(PendingChoiceSnapshot choice)
    {
        if (_handRoot == null || choice == null)
            return;

        HashSet<int> valid = new HashSet<int>(choice.ValidInstanceIds ?? new List<int>());
        HashSet<int> selected = new HashSet<int>(choice.SelectedInstanceIds ?? new List<int>());

        for (int i = 0; i < _handRoot.childCount; i++)
        {
            Transform card = _handRoot.GetChild(i);
            HandCardMotion motion = card.GetComponent<HandCardMotion>();
            Image image = card.GetComponent<Image>();
            if (motion == null || image == null)
                continue;

            bool isValid = valid.Contains(motion.InstanceId);
            bool isSelected = selected.Contains(motion.InstanceId);
            image.color = isSelected ? SelectedCardColor : (isValid ? NormalCardColor : InvalidCardColor);

            HandGameplayInteraction gameplay = card.GetComponent<HandGameplayInteraction>();
            if (gameplay != null)
                gameplay.SetPlayable(false);
        }
    }

    private void RestoreHandVisuals()
    {
        if (_handRoot == null)
            return;

        for (int i = 0; i < _handRoot.childCount; i++)
        {
            Image image = _handRoot.GetChild(i).GetComponent<Image>();
            if (image != null)
                image.color = NormalCardColor;
        }
    }

    private static Text CreateText(Transform parent, string name, string value, int size, FontStyle style)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        Text text = go.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = value;
        text.fontSize = size;
        text.fontStyle = style;
        text.alignment = TextAnchor.MiddleLeft;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    private static void SetAnchors(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static Transform FindDirectChild(Transform parent, string childName)
    {
        if (parent == null)
            return null;
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (string.Equals(child.name, childName, StringComparison.Ordinal))
                return child;
        }
        return null;
    }

    private static Transform FindDeepChild(Transform parent, string childName)
    {
        if (parent == null)
            return null;
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (string.Equals(child.name, childName, StringComparison.Ordinal))
                return child;
            Transform nested = FindDeepChild(child, childName);
            if (nested != null)
                return nested;
        }
        return null;
    }
}
