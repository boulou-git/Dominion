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
    private Transform _discard;
    private bool _destinations;
    private bool _ordered;
    private string _discardOption, _deckOption, _destination;
    private readonly List<int> _deck = new List<int>();
    public int[] DeckOrder => (_ordered || (_destinations && _destination == _deckOption)) &&
        _deck.Count == _decision.CandidateInstanceIds.Count ? _deck.ToArray() : null;
    public bool CanConfirm => _destinations
        ? _destination == _discardOption || (_destination == _deckOption && _deck.Count == _decision.CandidateInstanceIds.Count)
        : _ordered ? _deck.Count == _decision.CandidateInstanceIds.Count
        : _decision != null && DecisionPresentation.IsValid(_decision, _optionChoice ? _selectedOptions.Count : _selected.Count);

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
        _destinations = DeckChoiceRules.TryDescribe(decision, definition, out _discardOption, out _deckOption, out _);
        _ordered = decision.Operation == "move_all_ordered|deck";
        _discard = transform.Find("Panel/Discard/Scroll/Viewport/Content");
        if (_discard != null) transform.Find("Panel/Discard").gameObject.SetActive(_destinations);
        TextAt("Panel/Heading").text = _destinations ? "DÉFAUSSE OU DECK" : _ordered ? "ORDONNER LE DECK" : "CHOIX EN ATTENTE";
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
            drag.Bind(this, decision.DecisionId, id, () => {
                if (_destinations || _ordered) Drop(captured, true, _destinations ? _deckOption : null);
                else if (!_optionChoice) _toggleCard(captured);
            });
            _cards.Add(id, drag);
        }
        transform.Find("Panel/Chosen").gameObject.SetActive(!_optionChoice || _destinations);
        bool optionsOnly = _optionChoice && !_hasPreview;
        Transform optionRoot = transform.Find(optionsOnly
            ? "Panel/OptionsOnly/Scroll/Viewport/Content" : "Panel/Options/Scroll/Viewport/Content");
        transform.Find("Panel/Available").gameObject.SetActive(!optionsOnly);
        transform.Find("Panel/Options").gameObject.SetActive(_optionChoice && !optionsOnly && !_destinations);
        transform.Find("Panel/OptionsOnly").gameObject.SetActive(optionsOnly);
        if (_optionChoice && !_destinations)
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
        if (_destinations || _ordered)
        {
            TextAt("Panel/Chosen/Title").text = "DECK — gauche : plus bas · droite : dessus";
            TextAt("Panel/Hint").text = _destinations
                ? "Défausse : glissez une carte pour tout défausser. Deck : placez toutes les cartes et ordonnez-les avant de confirmer."
                : "Glissez toutes les cartes sur le deck, puis réordonnez-les. À droite : prochaine carte piochée.";
            TextAt("Panel/Prompt").text = _ordered ? "Replacez les cartes sur votre deck dans l’ordre de votre choix." : decision.Prompt;
            if (_destinations)
            {
                BindZone("Panel/Discard", true);
                transform.Find("Panel/Discard").GetComponent<DecisionDropZone>().OptionId = _discardOption;
                transform.Find("Panel/Chosen").GetComponent<DecisionDropZone>().OptionId = _deckOption;
                TextAt("Panel/Discard/Title").text = "DÉFAUSSE — tout le groupe";
            }
        }
        Button confirm = ButtonAt("Panel/Confirm");
        confirm.onClick.RemoveAllListeners();
        confirm.onClick.AddListener(() => { if (CanInteract(decision.DecisionId) && CanConfirm) submit(); });
        Button resetButton = ButtonAt("Panel/Reset");
        resetButton.onClick.RemoveAllListeners();
        resetButton.onClick.AddListener(() => { if (CanInteract(decision.DecisionId)) { _deck.Clear(); _destination = null; _reset(); RefreshSelection(false); } });
        RefreshSelection(false);
    }

    public void Drop(int id, bool select, string optionId, Vector2? screenPosition = null, Camera eventCamera = null)
    {
        if (!CanDrop(id, select, optionId)) return;
        if (_destinations || _ordered)
        {
            if (_destinations && optionId == _discardOption)
            {
                _deck.Clear(); _destination = _discardOption;
                _selectedOptions.Clear(); _selectedOptions.Add(_discardOption);
            }
            else
            {
                if (_destination == _discardOption) _destination = null;
                _deck.Remove(id);
                if (select)
                {
                    int index = _deck.Count;
                    if (screenPosition.HasValue)
                        for (int i = 0; i < _deck.Count; i++)
                            if (screenPosition.Value.x < RectTransformUtility.WorldToScreenPoint(eventCamera, _cards[_deck[i]].transform.position).x)
                            { index = i; break; }
                    _deck.Insert(index, id);
                }
                if (_destinations)
                {
                    _destination = _deck.Count > 0 ? _deckOption : null;
                    _selectedOptions.Clear();
                    if (_destination != null) _selectedOptions.Add(_destination);
                }
                else { _selected.Clear(); foreach (int cardId in _deck) _selected.Add(cardId); }
            }
            RefreshSelection(false);
            return;
        }
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
        if (_destinations) return optionId == null || optionId == _discardOption || optionId == _deckOption;
        if (_ordered) return true;
        if (_optionChoice)
            return _cards.Count == 1 && _decision.MaxSelections == 1 && optionId != null && _options.ContainsKey(optionId);
        return !select || _selected.Contains(id) || _decision.MaxSelections == 1 || _selected.Count < _decision.MaxSelections;
    }

    public void RefreshSelection(bool busy)
    {
        if (_decision == null) return;
        _busy = busy;
        int count = _optionChoice ? _selectedOptions.Count : _selected.Count;
        TextAt("Panel/Count").text = _destinations || _ordered
            ? (_destination == _discardOption && _destinations ? _cards.Count + " carte(s) à défausser" : _deck.Count + " / " + _cards.Count + " carte(s) sur le deck")
            : DecisionPresentation.CountLabel(_decision, count);
        ButtonAt("Panel/Confirm").interactable = !busy && CanConfirm;
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
            bool chosen = _destinations || _ordered ? _deck.Contains(pair.Key) : !_optionChoice && _selected.Contains(pair.Key);
            bool discarded = _destinations && _destination == _discardOption;
            if (!chosen && !discarded) available++;
            // A drag preview stays above the panel until OnEndDrag restores it.
            if (pair.Value.transform.parent != DragLayer)
                pair.Value.transform.SetParent(discarded ? _discard : chosen ? _chosen : _available, false);
            pair.Value.transform.Find("Selected").gameObject.SetActive(chosen);
        }
        for (int i = 0; i < _deck.Count; i++)
            if (_cards[_deck[i]].transform.parent == _chosen) _cards[_deck[i]].transform.SetSiblingIndex(i);
        if (_discard != null) transform.Find("Panel/Discard/Empty").gameObject.SetActive(_destination != _discardOption);
        transform.Find("Panel/Available/Empty").gameObject.SetActive(available == 0);
        transform.Find("Panel/Chosen/Empty").gameObject.SetActive((_destinations || _ordered) ? _deck.Count == 0 : _selected.Count == 0);
    }

    public void Clear()
    {
        foreach (DecisionDragCard card in _cards.Values)
            if (card != null) { card.gameObject.SetActive(false); Destroy(card.gameObject); }
        foreach (GameObject option in _options.Values)
            if (option != null) { option.SetActive(false); Destroy(option); }
        _cards.Clear(); _options.Clear(); _deck.Clear(); _destination = null; _destinations = false; _ordered = false; _decision = null;
    }

    private void BindZone(string path, bool select)
    {
        DecisionDropZone zone = transform.Find(path).GetComponent<DecisionDropZone>();
        zone.Owner = this; zone.Select = select; zone.OptionId = null;
    }
    private Text TextAt(string path) => transform.Find(path).GetComponent<Text>();
    private Button ButtonAt(string path) => transform.Find(path).GetComponent<Button>();
}
