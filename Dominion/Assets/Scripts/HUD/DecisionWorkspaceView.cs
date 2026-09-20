using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Prefab-backed destinations. Cards start on the deck; editing never mutates game state.</summary>
public sealed class DecisionWorkspaceView : MonoBehaviour
{
    [SerializeField] private GameObject _cardPrefab;
    private readonly Dictionary<int, DecisionDragCard> _cards = new Dictionary<int, DecisionDragCard>();
    private readonly List<int> _deck = new List<int>();
    private readonly List<int> _trash = new List<int>();
    private readonly List<int> _discarded = new List<int>();
    private PendingDecisionSnapshot _decision;
    private HashSet<int> _selected;
    private HashSet<string> _selectedOptions;
    private bool _busy, _sort, _group;
    private string _discardOption, _deckOption;
    private Transform _deckRoot, _trashRoot, _discardRoot;
    public bool IsSort => _sort;
    public int[] TrashCards => _trash.ToArray();
    public int[] DiscardCards => _discarded.ToArray();
    public int[] DeckOrder => !_group || _discarded.Count == 0 ? _deck.ToArray() : null;
    public bool CanConfirm
    {
        get
        {
            if (_decision == null || _cards.Count != _decision.CandidateInstanceIds.Count) return false;
            var all = new List<int>(_deck); all.AddRange(_trash); all.AddRange(_discarded);
            return DeckChoiceRules.IsPermutation(_decision.CandidateInstanceIds, all) &&
                (_sort || !_group || _deck.Count == 0 || _discarded.Count == 0);
        }
    }
    public RectTransform DragLayer => (RectTransform)transform.Find("DragLayer");

    public bool CanInteract(string decisionId)
    {
        var state = NetworkGameState.State;
        return isActiveAndEnabled && !_busy && state != null && !state.IsPaused &&
            state.Resolution != null && state.Resolution.IsActive && state.Resolution.PendingDecision != null &&
            state.Resolution.PendingDecision.IsPending && state.Resolution.PendingDecision.DecisionId == decisionId &&
            state.Resolution.PendingDecision.PlayerId == NetworkGameState.LocalPlayerId;
    }

    public void Configure(GameStateSnapshot state, PendingDecisionSnapshot decision,
        HashSet<int> selected, HashSet<string> selectedOptions, Action<int> toggleCard,
        Action<string> toggleOption, Action reset, Action submit)
    {
        Clear(); _decision = decision; _selected = selected; _selectedOptions = selectedOptions; _busy = false;
        CardInstance source = NetworkGameState.FindCardInstance(state, DecisionPresentation.SourceId(decision));
        ExtensionCardData definition = null;
        if (source != null) RoomGameSetup.TryResolveCard(source.DefinitionId, out _, out definition);
        _sort = InspectedSortRules.CanSort(decision, definition);
        _group = DeckChoiceRules.TryDescribe(decision, definition, out _discardOption, out _deckOption, out _);
        transform.Find("Panel/Source").GetComponent<DecisionSourceView>().Bind(state, decision);
        TextAt("Panel/Heading").text = _sort ? "RÉPARTISSEZ LES CARTES" : _group ? "DÉFAUSSE OU DECK" : "ORDONNER LE DECK";
        TextAt("Panel/Prompt").text = _sort
            ? "Déplacez les cartes entre Défausse, Deck et Écart, puis validez une seule fois."
            : _group ? decision.Prompt : "Replacez les cartes sur votre deck dans l’ordre de votre choix.";
        TextAt("Panel/Hint").text = "Deck : gauche = plus bas ; droite = dessus (prochaine carte piochée).";
        BindZone("Discard", "discard", _sort || _group);
        BindZone("Chosen", "deck", true);
        BindZone("Available", "trash", _sort);
        _deckRoot = Zone("Chosen").Find("Scroll/Viewport/Content");
        _discardRoot = Zone("Discard").Find("Scroll/Viewport/Content");
        _trashRoot = Zone("Available").Find("Scroll/Viewport/Content");
        Zone("Discard").Find("Title").GetComponent<Text>().text = _group ? "DÉFAUSSE — tout le groupe" : "DÉFAUSSE";
        Zone("Chosen").Find("Title").GetComponent<Text>().text = "DECK";
        Zone("Available").Find("Title").GetComponent<Text>().text = "ÉCART";
        foreach (int id in decision.CandidateInstanceIds)
        {
            if (_cards.ContainsKey(id)) continue;
            var card = NetworkGameState.FindCardInstance(state, id);
            if (card == null || !RoomGameSetup.TryResolveCard(card.DefinitionId, out ExtensionPackageData extension, out ExtensionCardData def)) continue;
            var tile = Instantiate(_cardPrefab, _deckRoot, false);
            tile.name = "DecisionCard_" + id;
            var art = tile.transform.Find("Artwork").GetComponent<Image>();
            art.sprite = ExtensionVisualLoader.LoadCardArtwork(extension, def); art.enabled = art.sprite != null;
            tile.transform.Find("Label").GetComponent<Text>().text = def.name;
            var drag = tile.GetComponent<DecisionDragCard>();
            drag.Bind(this, decision.DecisionId, id, () => { });
            _cards.Add(id, drag);
        }
        ResetDraft();
        var confirm = ButtonAt("Panel/Confirm"); confirm.onClick.RemoveAllListeners();
        confirm.onClick.AddListener(() => { if (CanInteract(decision.DecisionId) && CanConfirm) submit(); });
        var resetButton = ButtonAt("Panel/Reset"); resetButton.onClick.RemoveAllListeners();
        resetButton.onClick.AddListener(() => { if (CanInteract(decision.DecisionId)) ResetDraft(); });
    }

    private void ResetDraft()
    {
        _deck.Clear(); _trash.Clear(); _discarded.Clear();
        // inspected is top-first, while the visual deck is bottom-left to top-right.
        _deck.AddRange(_decision.CandidateInstanceIds); _deck.Reverse();
        RefreshSelection(false);
    }

    public bool CanDrop(int id, bool select, string destination)
    {
        return _decision != null && CanInteract(_decision.DecisionId) && _cards.ContainsKey(id) &&
            (destination == "deck" || destination == "discard" && (_group || _sort) || destination == "trash" && _sort);
    }

    public void Drop(int id, bool select, string destination, Vector2? screenPosition = null, Camera eventCamera = null)
    {
        if (!CanDrop(id, select, destination)) return;
        if (_group && destination == "discard")
        {
            _deck.Clear(); _discarded.Clear(); _discarded.AddRange(_decision.CandidateInstanceIds);
        }
        else
        {
            if (_group && _discarded.Count > 0)
            {
                _discarded.Clear(); _deck.Clear(); _deck.AddRange(_decision.CandidateInstanceIds); _deck.Reverse();
            }
            _deck.Remove(id); _discarded.Remove(id); _trash.Remove(id);
            if (destination == "deck")
            {
                int index = _deck.Count;
                if (screenPosition.HasValue)
                    for (int i = 0; i < _deck.Count; i++)
                        if (_cards.TryGetValue(_deck[i], out DecisionDragCard neighbour) &&
                            screenPosition.Value.x < RectTransformUtility.WorldToScreenPoint(eventCamera, neighbour.transform.position).x)
                        { index = i; break; }
                _deck.Insert(index, id);
            }
            else if (destination == "discard") _discarded.Add(id);
            else _trash.Add(id);
        }
        RefreshSelection(false);
    }

    public void RefreshSelection(bool busy)
    {
        if (_decision == null) return;
        _busy = busy;
        _selected.Clear(); _selectedOptions.Clear();
        if (_group) _selectedOptions.Add(_discarded.Count > 0 ? _discardOption : _deckOption);
        else foreach (int id in _sort ? _trash : _deck) _selected.Add(id);
        TextAt("Panel/Count").text = "Défausse : " + _discarded.Count + " · Deck : " + _deck.Count + (_sort ? " · Écart : " + _trash.Count : "");
        ButtonAt("Panel/Confirm").interactable = !busy && CanConfirm;
        TextAt("Panel/Confirm/Label").text = busy ? "ENVOI…" : "VALIDER";
        ButtonAt("Panel/Reset").interactable = !busy;
        RefreshPlacement();
    }

    public void RefreshPlacement()
    {
        if (_decision == null) return;
        foreach (var pair in _cards)
        {
            if (pair.Value.transform.parent != DragLayer)
                pair.Value.transform.SetParent(_trash.Contains(pair.Key) ? _trashRoot : _discarded.Contains(pair.Key) ? _discardRoot : _deckRoot, false);
            pair.Value.transform.Find("Selected").gameObject.SetActive(false);
        }
        for (int i = 0; i < _deck.Count; i++)
            if (_cards.TryGetValue(_deck[i], out DecisionDragCard card) && card.transform.parent == _deckRoot) card.transform.SetSiblingIndex(i);
        Zone("Discard").Find("Empty").gameObject.SetActive(_discarded.Count == 0);
        Zone("Available").Find("Empty").gameObject.SetActive(_trash.Count == 0);
        Zone("Chosen").Find("Empty").gameObject.SetActive(_deck.Count == 0);
    }

    public void Clear()
    {
        foreach (var card in _cards.Values) if (card != null) { card.gameObject.SetActive(false); Destroy(card.gameObject); }
        _cards.Clear(); _deck.Clear(); _trash.Clear(); _discarded.Clear(); _decision = null; _sort = false; _group = false;
    }
    private Transform Zone(string name) => transform.Find("Panel/Zones/" + name);
    private void BindZone(string name, string destination, bool visible)
    {
        var zone = Zone(name); zone.gameObject.SetActive(visible);
        var drop = zone.GetComponent<DecisionDropZone>(); drop.Owner = this; drop.OptionId = destination;
    }
    private Text TextAt(string path) => transform.Find(path).GetComponent<Text>();
    private Button ButtonAt(string path) => transform.Find(path).GetComponent<Button>();
}
