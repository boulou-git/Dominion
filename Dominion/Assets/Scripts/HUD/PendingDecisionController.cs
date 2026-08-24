using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Generic presentation for durable card-selection decisions.
/// It never decides game rules: candidates/bounds/prompt come from GameStateSnapshot and
/// the validated selection is sent back through PlayersTurnsHandler.
/// </summary>
public sealed class PendingDecisionController : MonoBehaviour
{
    private readonly HashSet<int> _selected = new HashSet<int>();
    private readonly Dictionary<CardPointerInteraction, Action> _selectionHandlers =
        new Dictionary<CardPointerInteraction, Action>();
    private readonly Dictionary<Image, Color> _originalImageColors = new Dictionary<Image, Color>();
    private readonly List<Outline> _selectionOutlines = new List<Outline>();

    private RectTransform _panel;
    private Text _promptText;
    private Text _countText;
    private Button _confirmButton;
    private Text _confirmText;
    private string _boundDecisionId;
    private bool _submitPending;

    private void Awake()
    {
        EnsurePanel();
        NetworkGameState.StateChanged += Refresh;
        Refresh(NetworkGameState.State);
    }

    private void OnDestroy()
    {
        NetworkGameState.StateChanged -= Refresh;
        ClearCardBindings();
    }

    private void Refresh(GameStateSnapshot state)
    {
        PendingDecisionSnapshot decision = ResolveLocalDecision(state);
        if (decision == null)
        {
            HideDecision();
            return;
        }

        EnsurePanel();

        if (!string.Equals(_boundDecisionId, decision.DecisionId, StringComparison.Ordinal))
        {
            ClearCardBindings();
            _selected.Clear();
            _submitPending = false;
            _boundDecisionId = decision.DecisionId;
        }

        if (_panel != null)
            _panel.gameObject.SetActive(true);

        if (_promptText != null)
            _promptText.text = string.IsNullOrWhiteSpace(decision.Prompt)
                ? "Choisissez des cartes de votre main."
                : decision.Prompt;

        BindHandCards(decision);
        RefreshSelectionUi(decision);
    }

    private PendingDecisionSnapshot ResolveLocalDecision(GameStateSnapshot state)
    {
        if (state == null ||
            !state.IsStarted ||
            state.IsPaused ||
            state.Resolution == null ||
            !state.Resolution.IsActive)
            return null;

        PendingDecisionSnapshot decision = state.Resolution.PendingDecision;
        if (decision == null ||
            !decision.IsPending ||
            !string.Equals(decision.PlayerId, NetworkGameState.LocalPlayerId, StringComparison.Ordinal))
            return null;

        return decision;
    }

    private void BindHandCards(PendingDecisionSnapshot decision)
    {
        Transform handRoot = FindHandCardsRoot();
        if (handRoot == null)
            return;

        HashSet<int> candidates = new HashSet<int>(decision.CandidateInstanceIds ?? new List<int>());

        for (int i = 0; i < handRoot.childCount; i++)
        {
            Transform child = handRoot.GetChild(i);
            HandCardMotion motion = child.GetComponent<HandCardMotion>();
            if (motion == null || motion.InstanceId <= 0)
                continue;

            int instanceId = motion.InstanceId;
            bool candidate = candidates.Contains(instanceId);

            HandGameplayInteraction gameplay = child.GetComponent<HandGameplayInteraction>();
            if (gameplay != null)
                gameplay.SetPlayable(false);

            Image image = child.GetComponent<Image>();
            if (image != null && !_originalImageColors.ContainsKey(image))
                _originalImageColors.Add(image, image.color);

            if (image != null)
                image.color = candidate
                    ? _originalImageColors[image]
                    : MultiplyRgb(_originalImageColors[image], 0.55f);

            CardPointerInteraction pointer = child.GetComponent<CardPointerInteraction>();
            if (!candidate || pointer == null || _selectionHandlers.ContainsKey(pointer))
                continue;

            int capturedId = instanceId;
            Action handler = () => ToggleSelection(capturedId);
            pointer.PrimaryActionRequested += handler;
            _selectionHandlers.Add(pointer, handler);
        }
    }

    private void ToggleSelection(int instanceId)
    {
        GameStateSnapshot state = NetworkGameState.State;
        PendingDecisionSnapshot decision = ResolveLocalDecision(state);
        if (decision == null || _submitPending)
            return;

        if (decision.CandidateInstanceIds == null || !decision.CandidateInstanceIds.Contains(instanceId))
            return;

        if (_selected.Contains(instanceId))
        {
            _selected.Remove(instanceId);
        }
        else
        {
            int max = Math.Max(decision.MinSelections, decision.MaxSelections);
            if (_selected.Count >= max)
                return;
            _selected.Add(instanceId);
        }

        RefreshSelectionUi(decision);
    }

    private void RefreshSelectionUi(PendingDecisionSnapshot decision)
    {
        if (decision == null)
            return;

        if (_countText != null)
        {
            if (decision.MinSelections == decision.MaxSelections)
                _countText.text = _selected.Count + " / " + decision.MaxSelections;
            else
                _countText.text = _selected.Count + " sélectionnée(s) — " +
                                  decision.MinSelections + " à " + decision.MaxSelections;
        }

        if (_confirmButton != null)
        {
            bool validCount = _selected.Count >= decision.MinSelections &&
                              _selected.Count <= decision.MaxSelections;
            _confirmButton.interactable = validCount && !_submitPending;
        }

        RefreshSelectionMarkers();
    }

    private void RefreshSelectionMarkers()
    {
        foreach (Outline outline in _selectionOutlines)
        {
            if (outline != null)
                Destroy(outline);
        }
        _selectionOutlines.Clear();

        Transform handRoot = FindHandCardsRoot();
        if (handRoot == null)
            return;

        for (int i = 0; i < handRoot.childCount; i++)
        {
            Transform child = handRoot.GetChild(i);
            HandCardMotion motion = child.GetComponent<HandCardMotion>();
            if (motion == null || !_selected.Contains(motion.InstanceId))
                continue;

            Outline outline = child.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(1f, 1f, 1f, 0.95f);
            outline.effectDistance = new Vector2(6f, -6f);
            outline.useGraphicAlpha = true;
            _selectionOutlines.Add(outline);
        }
    }

    private void Submit()
    {
        GameStateSnapshot state = NetworkGameState.State;
        PendingDecisionSnapshot decision = ResolveLocalDecision(state);
        if (decision == null || _submitPending)
            return;

        if (_selected.Count < decision.MinSelections || _selected.Count > decision.MaxSelections)
            return;

        PlayersTurnsHandler handler = PlayersTurnsHandler.Instance;
        if (handler == null)
            return;

        _submitPending = true;
        if (_confirmButton != null)
            _confirmButton.interactable = false;

        int[] selected = new int[_selected.Count];
        _selected.CopyTo(selected);
        handler.SubmitDecision(decision.DecisionId, selected);
    }

    private void HideDecision()
    {
        ClearCardBindings();
        _selected.Clear();
        _boundDecisionId = string.Empty;
        _submitPending = false;

        if (_panel != null)
            _panel.gameObject.SetActive(false);
    }

    private void ClearCardBindings()
    {
        foreach (KeyValuePair<CardPointerInteraction, Action> pair in _selectionHandlers)
        {
            if (pair.Key != null)
                pair.Key.PrimaryActionRequested -= pair.Value;
        }
        _selectionHandlers.Clear();

        foreach (KeyValuePair<Image, Color> pair in _originalImageColors)
        {
            if (pair.Key != null)
                pair.Key.color = pair.Value;
        }
        _originalImageColors.Clear();

        foreach (Outline outline in _selectionOutlines)
        {
            if (outline != null)
                Destroy(outline);
        }
        _selectionOutlines.Clear();
    }

    private void EnsurePanel()
    {
        if (_panel != null)
            return;

        Transform existing = FindDeepChild(transform, "PendingDecisionPanel");
        if (existing is RectTransform existingRect)
        {
            _panel = existingRect;
            return;
        }

        GameObject panelObject = new GameObject(
            "PendingDecisionPanel",
            typeof(RectTransform),
            typeof(Image));
        panelObject.transform.SetParent(transform, false);
        _panel = panelObject.GetComponent<RectTransform>();
        _panel.anchorMin = new Vector2(0.30f, 0.245f);
        _panel.anchorMax = new Vector2(0.70f, 0.34f);
        _panel.offsetMin = Vector2.zero;
        _panel.offsetMax = Vector2.zero;
        panelObject.GetComponent<Image>().color = new Color(0.08f, 0.075f, 0.065f, 0.97f);

        GameObject promptObject = new GameObject("Prompt", typeof(RectTransform), typeof(Text));
        promptObject.transform.SetParent(_panel, false);
        RectTransform promptRect = promptObject.GetComponent<RectTransform>();
        SetAnchors(promptRect, new Vector2(0.03f, 0.40f), new Vector2(0.70f, 0.94f));
        _promptText = promptObject.GetComponent<Text>();
        ConfigureText(_promptText, 17, TextAnchor.MiddleLeft);

        GameObject countObject = new GameObject("Count", typeof(RectTransform), typeof(Text));
        countObject.transform.SetParent(_panel, false);
        RectTransform countRect = countObject.GetComponent<RectTransform>();
        SetAnchors(countRect, new Vector2(0.03f, 0.06f), new Vector2(0.70f, 0.42f));
        _countText = countObject.GetComponent<Text>();
        ConfigureText(_countText, 14, TextAnchor.MiddleLeft);

        GameObject buttonObject = new GameObject(
            "ConfirmDecision",
            typeof(RectTransform),
            typeof(Image),
            typeof(Button));
        buttonObject.transform.SetParent(_panel, false);
        RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
        SetAnchors(buttonRect, new Vector2(0.73f, 0.15f), new Vector2(0.97f, 0.85f));
        Image buttonImage = buttonObject.GetComponent<Image>();
        buttonImage.color = new Color(0.30f, 0.26f, 0.16f, 1f);
        _confirmButton = buttonObject.GetComponent<Button>();
        _confirmButton.targetGraphic = buttonImage;
        _confirmButton.onClick.AddListener(Submit);

        GameObject buttonTextObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
        buttonTextObject.transform.SetParent(buttonObject.transform, false);
        RectTransform buttonTextRect = buttonTextObject.GetComponent<RectTransform>();
        SetAnchors(buttonTextRect, Vector2.zero, Vector2.one);
        _confirmText = buttonTextObject.GetComponent<Text>();
        ConfigureText(_confirmText, 16, TextAnchor.MiddleCenter);
        _confirmText.fontStyle = FontStyle.Bold;
        _confirmText.text = "VALIDER";
        _confirmText.raycastTarget = false;

        _panel.gameObject.SetActive(false);
    }

    private Transform FindHandCardsRoot()
    {
        Transform localHand = FindDeepChild(transform, "LocalHand");
        return FindDirectChild(localHand, "Cards");
    }

    private static Color MultiplyRgb(Color color, float multiplier)
    {
        return new Color(
            color.r * multiplier,
            color.g * multiplier,
            color.b * multiplier,
            color.a);
    }

    private static void ConfigureText(Text text, int fontSize, TextAnchor alignment)
    {
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = Color.white;
        text.raycastTarget = false;
    }

    private static void SetAnchors(RectTransform rect, Vector2 min, Vector2 max)
    {
        if (rect == null)
            return;
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static Transform FindDirectChild(Transform parent, string name)
    {
        if (parent == null)
            return null;
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (string.Equals(child.name, name, StringComparison.Ordinal))
                return child;
        }
        return null;
    }

    private static Transform FindDeepChild(Transform parent, string name)
    {
        if (parent == null)
            return null;
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (string.Equals(child.name, name, StringComparison.Ordinal))
                return child;
            Transform nested = FindDeepChild(child, name);
            if (nested != null)
                return nested;
        }
        return null;
    }
}
