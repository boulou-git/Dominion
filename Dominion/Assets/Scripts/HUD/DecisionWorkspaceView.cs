using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Prefab-backed decision workspace. Dragging edits a local draft only.</summary>
public sealed class DecisionWorkspaceView : MonoBehaviour
{
    [SerializeField] private GameObject _cardPrefab;
    [SerializeField] private GameObject _optionPrefab;
    private readonly Dictionary<int, DecisionDragCard> _cards = new Dictionary<int, DecisionDragCard>();
    private readonly Dictionary<string, GameObject> _options = new Dictionary<string, GameObject>();
    private PendingDecisionSnapshot _decision;
    private HashSet<int> _selected;
    private HashSet<string> _selectedOptions;
    private Action<int> _toggleCard;
    private Action<string> _toggleOption;
    private Action _reset;
    private bool _busy;
    private bool _optionChoice;
    private bool _hasPreview;
    private Transform _available;
    private Transform _chosen;
    public RectTransform DragLayer => (RectTransform)transform.Find("DragLayer");

    public bool CanInteract(string decisionId)
    {
        GameStateSnapshot state = NetworkGameState.State;
        return isActiveAndEnabled && !_busy && state != null && !state.IsPaused &&
            state.Resolution != null && state.Resolution.IsActive &&
            state.Resolution.PendingDecision != null &&
            state.Resolution.PendingDecision.IsPending &&
            state.Resolution.PendingDecision.DecisionId == decisionId &&
            state.Resolution.PendingDecision.PlayerId == NetworkGameState.LocalPlayerId;
    }

    public void Configure(GameStateSnapshot state, PendingDecisionSnapshot decision,
        HashSet<int> selected, HashSet<string> selectedOptions, Action<int> toggleCard,
        Action<string> toggleOption, Action reset, Action submit)
    {
        Clear();
        _decision = decision; _selected = selected; _selectedOptions = selectedOptions;
        _toggleCard = toggleCard; _toggleOption = toggleOption; _reset = reset; _busy = false;
        _optionChoice = string.Equals(decision.Zone, "options", StringComparison.OrdinalIgnoreCase);
        transform.Find("Panel/Source").GetComponent<DecisionSourceView>().Bind(state, decision);
        TextAt("Panel/Prompt").text = decision.Prompt;
        _available = transform.Find("Panel/Available/Scroll/Viewport/Content");
        _chosen = transform.Find("Panel/Chosen/Scroll/Viewport/Content");
        BindZone("Panel/Available", false);
        BindZone("Panel/Chosen", true);
        CardInstance source = NetworkGameState.FindCardInstance(state, DecisionPresentation.SourceId(decision));
        ExtensionCardData definition = null;
        if (source != null) RoomGameSetup.TryResolveCard(source.DefinitionId, out _, out definition);
        TextAt("Panel/Chosen/Title").text = DecisionPresentation.Destination(decision, definition);
        TextAt("Panel/Available/Title").text = _optionChoice ? "CARTES CONCERNÉES" : "CARTES DISPONIBLES";
        TextAt("Panel/Hint").text = _optionChoice
            ? "Choisissez une option, puis confirmez."
            : "Cliquez ou glissez une carte vers la destination. Ramenez-la pour annuler.";

        List<int> previews = new List<int>(decision.CandidateInstanceIds ?? new List<int>());
        // Only show the triggering card for the local player's own event; never reveal
        // another player's hidden hand while displaying a reaction prompt.
        if (_optionChoice && previews.Count == 0 && decision.TriggerEvent != null &&
            decision.TriggerEvent.PlayerId == decision.PlayerId && decision.TriggerEvent.CardInstanceId > 0 &&
            decision.TriggerEvent.CardInstanceId != DecisionPresentation.SourceId(decision))
            previews.Add(decision.TriggerEvent.CardInstanceId);
        _hasPreview = previews.Count > 0;
        foreach (int id in previews)
        {
            if (_cards.ContainsKey(id)) continue;
            CardInstance card = NetworkGameState.FindCardInstance(state, id);
            if (card == null || !RoomGameSetup.TryResolveCard(card.DefinitionId, out ExtensionPackageData extension, out ExtensionCardData cardDefinition)) continue;
            GameObject tile = Instantiate(_cardPrefab, _available, false);
            tile.name = "DecisionCard_" + id;
            Image art = tile.transform.Find("Artwork").GetComponent<Image>();
            art.sprite = ExtensionVisualLoader.LoadCardArtwork(extension, cardDefinition);
            art.enabled = art.sprite != null;
            tile.transform.Find("Label").GetComponent<Text>().text = cardDefinition.name;
            DecisionDragCard drag = tile.GetComponent<DecisionDragCard>();
            int captured = id;
            drag.Bind(this, decision.DecisionId, id, () => { if (!_optionChoice) _toggleCard(captured); });
            _cards.Add(id, drag);
        }
        transform.Find("Panel/Chosen").gameObject.SetActive(!_optionChoice);
        bool optionsOnly = _optionChoice && !_hasPreview;
        Transform optionRoot = transform.Find(optionsOnly
            ? "Panel/OptionsOnly/Scroll/Viewport/Content" : "Panel/Options/Scroll/Viewport/Content");
        transform.Find("Panel/Available").gameObject.SetActive(!optionsOnly);
        transform.Find("Panel/Options").gameObject.SetActive(_optionChoice && !optionsOnly);
        transform.Find("Panel/OptionsOnly").gameObject.SetActive(optionsOnly);
        if (_optionChoice)
        {
            List<string> ids = decision.CandidateDefinitionIds ?? new List<string>();
            for (int i = 0; i < ids.Count; i++)
            {
                string id = ids[i];
                GameObject option = Instantiate(_optionPrefab, optionRoot, false);
                string label = decision.CandidateOptionLabels != null && i < decision.CandidateOptionLabels.Count
                    ? decision.CandidateOptionLabels[i] : id;
                option.transform.Find("Label").GetComponent<Text>().text = label;
                option.GetComponent<Button>().onClick.AddListener(() => { if (CanInteract(decision.DecisionId)) _toggleOption(id); });
                DecisionDropZone zone = option.GetComponent<DecisionDropZone>();
                zone.Owner = this; zone.OptionId = id;
                _options.Add(id, option);
            }
            if (previews.Count == 1 && decision.MaxSelections == 1)
                TextAt("Panel/Hint").text = "Cliquez une option ou glissez la carte dessus, puis confirmez.";
            if (!_hasPreview) TextAt("Panel/Available/Empty").text = "Choisissez un effet ci-dessous.";
        }
        Button confirm = ButtonAt("Panel/Confirm");
        confirm.onClick.RemoveAllListeners();
        confirm.onClick.AddListener(() => { if (CanInteract(decision.DecisionId)) submit(); });
        Button resetButton = ButtonAt("Panel/Reset");
        resetButton.onClick.RemoveAllListeners();
        resetButton.onClick.AddListener(() => { if (CanInteract(decision.DecisionId)) _reset(); });
        RefreshSelection(false);
    }

    public void Drop(int id, bool select, string optionId)
    {
        if (!CanDrop(id, select, optionId)) return;
        if (_optionChoice)
        {
            if (_cards.Count == 1 && _decision.MaxSelections == 1 && optionId != null && _options.ContainsKey(optionId) && !_selectedOptions.Contains(optionId))
                _toggleOption(optionId);
        }
        else if (_selected.Contains(id) != select) _toggleCard(id);
    }

    public bool CanDrop(int id, bool select, string optionId)
    {
        if (_decision == null || !CanInteract(_decision.DecisionId) || !_cards.ContainsKey(id)) return false;
        if (_optionChoice)
            return _cards.Count == 1 && _decision.MaxSelections == 1 && optionId != null && _options.ContainsKey(optionId);
        return !select || _selected.Contains(id) || _decision.MaxSelections == 1 || _selected.Count < _decision.MaxSelections;
    }

    public void RefreshSelection(bool busy)
    {
        if (_decision == null) return;
        _busy = busy;
        int count = _optionChoice ? _selectedOptions.Count : _selected.Count;
        TextAt("Panel/Count").text = DecisionPresentation.CountLabel(_decision, count);
        ButtonAt("Panel/Confirm").interactable = !busy && DecisionPresentation.IsValid(_decision, count);
        TextAt("Panel/Confirm/Label").text = busy ? "ENVOI…" : count == 0 && DecisionPresentation.IsValid(_decision, 0) ? "PASSER" : "CONFIRMER";
        ButtonAt("Panel/Reset").interactable = !busy && count > 0;
        foreach (KeyValuePair<string, GameObject> option in _options)
        {
            option.Value.transform.Find("Selected").gameObject.SetActive(_selectedOptions.Contains(option.Key));
            option.Value.GetComponent<Button>().interactable = !busy;
        }
        RefreshPlacement();
    }

    public void RefreshPlacement()
    {
        if (_decision == null) return;
        int available = 0;
        foreach (KeyValuePair<int, DecisionDragCard> pair in _cards)
        {
            bool chosen = !_optionChoice && _selected.Contains(pair.Key);
            if (!chosen) available++;
            // A drag preview stays above the panel until OnEndDrag restores it.
            if (pair.Value.transform.parent != DragLayer)
                pair.Value.transform.SetParent(chosen ? _chosen : _available, false);
            pair.Value.transform.Find("Selected").gameObject.SetActive(chosen);
        }
        transform.Find("Panel/Available/Empty").gameObject.SetActive(available == 0);
        transform.Find("Panel/Chosen/Empty").gameObject.SetActive(_selected.Count == 0);
    }

    public void Clear()
    {
        foreach (DecisionDragCard card in _cards.Values)
            if (card != null) { card.gameObject.SetActive(false); Destroy(card.gameObject); }
        foreach (GameObject option in _options.Values)
            if (option != null) { option.SetActive(false); Destroy(option); }
        _cards.Clear(); _options.Clear(); _decision = null;
    }

    private void BindZone(string path, bool select)
    {
        DecisionDropZone zone = transform.Find(path).GetComponent<DecisionDropZone>();
        zone.Owner = this; zone.Select = select; zone.OptionId = null;
    }
    private Text TextAt(string path) => transform.Find(path).GetComponent<Text>();
    private Button ButtonAt(string path) => transform.Find(path).GetComponent<Button>();
}
