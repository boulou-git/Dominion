using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Generic presentation for durable decisions. Owned-card choices reuse the hand or a temporary
/// zone grid; supply choices reuse the existing Reserve piles directly.
/// </summary>
public sealed class PendingDecisionController : MonoBehaviour
{
    private readonly HashSet<int> _selected = new HashSet<int>();
    private readonly HashSet<string> _selectedSupply = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<CardPointerInteraction, Action> _selectionHandlers = new Dictionary<CardPointerInteraction, Action>();
    private readonly Dictionary<Image, Color> _originalImageColors = new Dictionary<Image, Color>();
    private readonly List<Outline> _selectionOutlines = new List<Outline>();
    private readonly List<GameObject> _externalCards = new List<GameObject>();

    private RectTransform _panel;
    private RectTransform _externalCardsRoot;
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
        ClearExternalCards();
        ClearSupplyDecisionVisuals();
    }

    private void Refresh(GameStateSnapshot state)
    {
        PendingDecisionSnapshot decision = ResolveLocalDecision(state);
        if (decision == null) { HideDecision(); return; }
        EnsurePanel();

        bool newDecision = !string.Equals(_boundDecisionId, decision.DecisionId, StringComparison.Ordinal);
        if (newDecision)
        {
            ClearCardBindings();
            ClearExternalCards();
            ClearSupplyDecisionVisuals();
            _selected.Clear();
            _selectedSupply.Clear();
            _submitPending = false;
            _boundDecisionId = decision.DecisionId;
        }

        if (_panel != null) _panel.gameObject.SetActive(true);
        if (_promptText != null) _promptText.text = string.IsNullOrWhiteSpace(decision.Prompt) ? "Faites un choix." : decision.Prompt;

        bool supplyChoice = IsSupplyDecision(decision);
        CardZone choiceZone = ResolveDecisionZone(decision);
        ConfigurePanel(choiceZone, supplyChoice);

        if (supplyChoice)
        {
            if (_externalCardsRoot != null) _externalCardsRoot.gameObject.SetActive(false);
            BindSupplyPiles(decision);
        }
        else if (choiceZone == CardZone.Hand)
        {
            if (_externalCardsRoot != null) _externalCardsRoot.gameObject.SetActive(false);
            BindHandCards(decision);
        }
        else if (newDecision)
        {
            BuildExternalCards(state, decision);
        }

        RefreshSelectionUi(decision);
    }

    private static bool IsSupplyDecision(PendingDecisionSnapshot decision) =>
        decision != null && string.Equals(decision.Zone, "supply", StringComparison.OrdinalIgnoreCase);

    private static CardZone ResolveDecisionZone(PendingDecisionSnapshot decision)
    {
        if (decision == null || string.IsNullOrWhiteSpace(decision.Zone) || IsSupplyDecision(decision)) return CardZone.Hand;
        return CardZoneRules.TryParseZone(decision.Zone, out CardZone zone) ? zone : CardZone.Hand;
    }

    private PendingDecisionSnapshot ResolveLocalDecision(GameStateSnapshot state)
    {
        if (state == null || !state.IsStarted || state.IsPaused || state.Resolution == null || !state.Resolution.IsActive) return null;
        PendingDecisionSnapshot decision = state.Resolution.PendingDecision;
        if (decision == null || !decision.IsPending || !string.Equals(decision.PlayerId, NetworkGameState.LocalPlayerId, StringComparison.Ordinal)) return null;
        return decision;
    }

    private void BindSupplyPiles(PendingDecisionSnapshot decision)
    {
        HashSet<string> candidates = new HashSet<string>(decision.CandidateDefinitionIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
        SupplyPileInteractionBinding[] bindings = GetComponentsInChildren<SupplyPileInteractionBinding>(true);
        foreach (SupplyPileInteractionBinding binding in bindings)
        {
            if (binding == null || string.IsNullOrEmpty(binding.DefinitionId)) continue;
            bool candidate = candidates.Contains(binding.DefinitionId);
            bool selected = _selectedSupply.Contains(binding.DefinitionId);
            binding.SetDecisionChoice(true, candidate, selected, ToggleSupplySelection);
        }
    }

    private void ToggleSupplySelection(string definitionId)
    {
        PendingDecisionSnapshot decision = ResolveLocalDecision(NetworkGameState.State);
        if (decision == null || _submitPending || !IsSupplyDecision(decision) ||
            decision.CandidateDefinitionIds == null || !decision.CandidateDefinitionIds.Contains(definitionId)) return;

        if (_selectedSupply.Contains(definitionId)) _selectedSupply.Remove(definitionId);
        else
        {
            int max = Math.Max(decision.MinSelections, decision.MaxSelections);
            if (_selectedSupply.Count >= max) return;
            _selectedSupply.Add(definitionId);
        }

        BindSupplyPiles(decision);
        RefreshSelectionUi(decision);
    }

    private void BindHandCards(PendingDecisionSnapshot decision)
    {
        Transform handRoot = FindHandCardsRoot();
        if (handRoot == null) return;
        HashSet<int> candidates = new HashSet<int>(decision.CandidateInstanceIds ?? new List<int>());

        for (int i = 0; i < handRoot.childCount; i++)
        {
            Transform child = handRoot.GetChild(i);
            HandCardMotion motion = child.GetComponent<HandCardMotion>();
            if (motion == null || motion.InstanceId <= 0) continue;
            int instanceId = motion.InstanceId;
            bool candidate = candidates.Contains(instanceId);

            HandGameplayInteraction gameplay = child.GetComponent<HandGameplayInteraction>();
            if (gameplay != null) gameplay.SetPlayable(false);
            Image image = child.GetComponent<Image>();
            if (image != null && !_originalImageColors.ContainsKey(image)) _originalImageColors.Add(image, image.color);
            if (image != null) image.color = candidate ? _originalImageColors[image] : MultiplyRgb(_originalImageColors[image], 0.55f);

            CardPointerInteraction pointer = child.GetComponent<CardPointerInteraction>();
            if (!candidate || pointer == null || _selectionHandlers.ContainsKey(pointer)) continue;
            int capturedId = instanceId;
            Action handler = () => ToggleSelection(capturedId);
            pointer.PrimaryActionRequested += handler;
            _selectionHandlers.Add(pointer, handler);
        }
    }

    private void BuildExternalCards(GameStateSnapshot state, PendingDecisionSnapshot decision)
    {
        ClearExternalCards();
        if (_externalCardsRoot == null || state == null || decision == null) return;
        _externalCardsRoot.gameObject.SetActive(true);

        foreach (int instanceId in decision.CandidateInstanceIds ?? new List<int>())
        {
            CardInstance instance = NetworkGameState.FindCardInstance(state, instanceId);
            if (instance == null) continue;
            ExtensionPackageData extension; ExtensionCardData definition;
            if (!RoomGameSetup.TryResolveCard(instance.DefinitionId, out extension, out definition)) continue;

            GameObject card = new GameObject("DecisionCard_" + instanceId, typeof(RectTransform), typeof(Image), typeof(LayoutElement), typeof(CardPointerInteraction));
            card.transform.SetParent(_externalCardsRoot, false);
            RectTransform rect = card.GetComponent<RectTransform>(); rect.sizeDelta = new Vector2(82f, 127f);
            Image image = card.GetComponent<Image>();
            image.sprite = ExtensionVisualLoader.LoadCardArtwork(extension, definition);
            image.preserveAspect = true; image.color = image.sprite != null ? Color.white : new Color(0.55f, 0.12f, 0.12f, 1f); image.raycastTarget = true;
            LayoutElement layout = card.GetComponent<LayoutElement>(); layout.preferredWidth = 82f; layout.preferredHeight = 127f;

            CardPointerInteraction pointer = card.GetComponent<CardPointerInteraction>(); pointer.InspectOnLongPress = false;
            int capturedId = instanceId; Action handler = () => ToggleSelection(capturedId);
            pointer.PrimaryActionRequested += handler; _selectionHandlers.Add(pointer, handler); _externalCards.Add(card);
        }
        LayoutRebuilder.ForceRebuildLayoutImmediate(_externalCardsRoot);
    }

    private void ToggleSelection(int instanceId)
    {
        PendingDecisionSnapshot decision = ResolveLocalDecision(NetworkGameState.State);
        if (decision == null || _submitPending || decision.CandidateInstanceIds == null || !decision.CandidateInstanceIds.Contains(instanceId)) return;
        if (_selected.Contains(instanceId)) _selected.Remove(instanceId);
        else
        {
            int max = Math.Max(decision.MinSelections, decision.MaxSelections);
            if (_selected.Count >= max) return;
            _selected.Add(instanceId);
        }
        RefreshSelectionUi(decision);
    }

    private void RefreshSelectionUi(PendingDecisionSnapshot decision)
    {
        if (decision == null) return;
        int selectedCount = IsSupplyDecision(decision) ? _selectedSupply.Count : _selected.Count;
        if (_countText != null)
            _countText.text = decision.MinSelections == decision.MaxSelections
                ? selectedCount + " / " + decision.MaxSelections
                : selectedCount + " sélectionnée(s) — " + decision.MinSelections + " à " + decision.MaxSelections;
        if (_confirmButton != null)
            _confirmButton.interactable = selectedCount >= decision.MinSelections && selectedCount <= decision.MaxSelections && !_submitPending;
        if (!IsSupplyDecision(decision)) RefreshSelectionMarkers();
    }

    private void RefreshSelectionMarkers()
    {
        foreach (Outline outline in _selectionOutlines) if (outline != null) Destroy(outline);
        _selectionOutlines.Clear();

        Transform handRoot = FindHandCardsRoot();
        if (handRoot != null)
            for (int i = 0; i < handRoot.childCount; i++)
            {
                Transform child = handRoot.GetChild(i);
                HandCardMotion motion = child.GetComponent<HandCardMotion>();
                if (motion != null && _selected.Contains(motion.InstanceId)) AddSelectionOutline(child.gameObject);
            }

        foreach (GameObject card in _externalCards)
        {
            if (card == null) continue;
            int id = ResolveExternalInstanceId(card.name);
            if (_selected.Contains(id)) AddSelectionOutline(card);
        }
    }

    private void AddSelectionOutline(GameObject target)
    {
        Outline outline = target.AddComponent<Outline>();
        outline.effectColor = new Color(1f, 1f, 1f, 0.95f);
        outline.effectDistance = new Vector2(2f, -2f);
        outline.useGraphicAlpha = true;
        _selectionOutlines.Add(outline);
    }

    private static int ResolveExternalInstanceId(string objectName)
    {
        const string prefix = "DecisionCard_";
        if (string.IsNullOrEmpty(objectName) || !objectName.StartsWith(prefix, StringComparison.Ordinal)) return 0;
        return int.TryParse(objectName.Substring(prefix.Length), out int id) ? id : 0;
    }

    private void Submit()
    {
        PendingDecisionSnapshot decision = ResolveLocalDecision(NetworkGameState.State);
        if (decision == null || _submitPending) return;
        int selectedCount = IsSupplyDecision(decision) ? _selectedSupply.Count : _selected.Count;
        if (selectedCount < decision.MinSelections || selectedCount > decision.MaxSelections) return;

        PlayersTurnsHandler handler = PlayersTurnsHandler.Instance;
        if (handler == null) return;
        _submitPending = true;
        if (_confirmButton != null) _confirmButton.interactable = false;

        if (IsSupplyDecision(decision))
        {
            string[] selected = new string[_selectedSupply.Count]; _selectedSupply.CopyTo(selected);
            handler.SubmitSupplyDecision(decision.DecisionId, selected);
        }
        else
        {
            int[] selected = new int[_selected.Count]; _selected.CopyTo(selected);
            handler.SubmitDecision(decision.DecisionId, selected);
        }
    }

    private void HideDecision()
    {
        ClearCardBindings(); ClearExternalCards(); ClearSupplyDecisionVisuals();
        _selected.Clear(); _selectedSupply.Clear(); _boundDecisionId = string.Empty; _submitPending = false;
        if (_panel != null) _panel.gameObject.SetActive(false);
    }

    private void ClearSupplyDecisionVisuals()
    {
        SupplyPileInteractionBinding[] bindings = GetComponentsInChildren<SupplyPileInteractionBinding>(true);
        foreach (SupplyPileInteractionBinding binding in bindings)
            if (binding != null) binding.SetDecisionChoice(false, false, false, null);
    }

    private void ClearCardBindings()
    {
        foreach (KeyValuePair<CardPointerInteraction, Action> pair in _selectionHandlers)
            if (pair.Key != null) pair.Key.PrimaryActionRequested -= pair.Value;
        _selectionHandlers.Clear();
        foreach (KeyValuePair<Image, Color> pair in _originalImageColors) if (pair.Key != null) pair.Key.color = pair.Value;
        _originalImageColors.Clear();
        foreach (Outline outline in _selectionOutlines) if (outline != null) Destroy(outline);
        _selectionOutlines.Clear();
    }

    private void ClearExternalCards()
    {
        foreach (GameObject card in _externalCards) if (card != null) Destroy(card);
        _externalCards.Clear();
    }

    private void EnsurePanel()
    {
        if (_panel != null) return;
        GameObject panelObject = new GameObject("PendingDecisionPanel", typeof(RectTransform), typeof(Image));
        panelObject.transform.SetParent(transform, false);
        _panel = panelObject.GetComponent<RectTransform>();
        panelObject.GetComponent<Image>().color = new Color(0.08f, 0.075f, 0.065f, 0.97f);

        GameObject promptObject = new GameObject("Prompt", typeof(RectTransform), typeof(Text));
        promptObject.transform.SetParent(_panel, false); _promptText = promptObject.GetComponent<Text>(); ConfigureText(_promptText, 17, TextAnchor.MiddleLeft);
        GameObject countObject = new GameObject("Count", typeof(RectTransform), typeof(Text));
        countObject.transform.SetParent(_panel, false); _countText = countObject.GetComponent<Text>(); ConfigureText(_countText, 14, TextAnchor.MiddleLeft);

        GameObject cardsObject = new GameObject("DecisionCards", typeof(RectTransform), typeof(GridLayoutGroup));
        cardsObject.transform.SetParent(_panel, false); _externalCardsRoot = cardsObject.GetComponent<RectTransform>();
        GridLayoutGroup grid = cardsObject.GetComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(82f, 127f); grid.spacing = new Vector2(8f, 8f); grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 7; grid.childAlignment = TextAnchor.MiddleCenter;

        GameObject buttonObject = new GameObject("ConfirmDecision", typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(_panel, false); Image buttonImage = buttonObject.GetComponent<Image>(); buttonImage.color = new Color(0.30f, 0.26f, 0.16f, 1f);
        _confirmButton = buttonObject.GetComponent<Button>(); _confirmButton.targetGraphic = buttonImage; _confirmButton.onClick.AddListener(Submit);
        GameObject buttonTextObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
        buttonTextObject.transform.SetParent(buttonObject.transform, false); _confirmText = buttonTextObject.GetComponent<Text>();
        ConfigureText(_confirmText, 16, TextAnchor.MiddleCenter); _confirmText.fontStyle = FontStyle.Bold; _confirmText.text = "VALIDER"; _confirmText.raycastTarget = false;
        SetAnchors(buttonTextObject.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);
        _panel.gameObject.SetActive(false);
        ConfigurePanel(CardZone.Hand, false);
    }

    private void ConfigurePanel(CardZone zone, bool supplyChoice)
    {
        if (_panel == null) return;
        bool compact = zone == CardZone.Hand || supplyChoice;
        SetAnchors(_panel, compact ? new Vector2(0.30f, 0.245f) : new Vector2(0.12f, 0.20f), compact ? new Vector2(0.70f, 0.34f) : new Vector2(0.88f, 0.66f));
        SetAnchors(_promptText.rectTransform, compact ? new Vector2(0.03f, 0.40f) : new Vector2(0.03f, 0.82f), compact ? new Vector2(0.70f, 0.94f) : new Vector2(0.78f, 0.97f));
        SetAnchors(_countText.rectTransform, new Vector2(0.03f, 0.06f), compact ? new Vector2(0.70f, 0.42f) : new Vector2(0.62f, 0.16f));
        SetAnchors(_confirmButton.GetComponent<RectTransform>(), compact ? new Vector2(0.73f, 0.15f) : new Vector2(0.80f, 0.82f), compact ? new Vector2(0.97f, 0.85f) : new Vector2(0.97f, 0.96f));
        if (_externalCardsRoot != null)
        {
            SetAnchors(_externalCardsRoot, new Vector2(0.03f, 0.18f), new Vector2(0.97f, 0.79f));
            _externalCardsRoot.gameObject.SetActive(!compact);
        }
    }

    private Transform FindHandCardsRoot()
    {
        Transform localHand = FindDeepChild(transform, "LocalHand"); return FindDirectChild(localHand, "Cards");
    }

    private static Color MultiplyRgb(Color color, float multiplier) => new Color(color.r * multiplier, color.g * multiplier, color.b * multiplier, color.a);
    private static void ConfigureText(Text text, int fontSize, TextAnchor alignment)
    { text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); text.fontSize = fontSize; text.alignment = alignment; text.color = Color.white; text.raycastTarget = false; }
    private static void SetAnchors(RectTransform rect, Vector2 min, Vector2 max)
    { if (rect == null) return; rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero; }
    private static Transform FindDirectChild(Transform parent, string name)
    { if (parent == null) return null; for (int i = 0; i < parent.childCount; i++) { Transform child = parent.GetChild(i); if (string.Equals(child.name, name, StringComparison.Ordinal)) return child; } return null; }
    private static Transform FindDeepChild(Transform parent, string name)
    { if (parent == null) return null; for (int i = 0; i < parent.childCount; i++) { Transform child = parent.GetChild(i); if (string.Equals(child.name, name, StringComparison.Ordinal)) return child; Transform nested = FindDeepChild(child, name); if (nested != null) return nested; } return null; }
}