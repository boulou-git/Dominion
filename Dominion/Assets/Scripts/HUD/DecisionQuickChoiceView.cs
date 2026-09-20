using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Small prefab-backed dialog: each button sends one explicit answer.</summary>
public sealed class DecisionQuickChoiceView : MonoBehaviour
{
    [SerializeField] private GameObject _cardPrefab;
    [SerializeField] private GameObject _optionPrefab;
    private readonly List<GameObject> _items = new List<GameObject>();
    private readonly List<Button> _buttons = new List<Button>();
    private PendingDecisionSnapshot _decision;
    private bool _busy;
    private bool _draft;
    private Transform _optionRoot;
    private Text _optionTitle;
    private Button _confirm;
    private readonly HashSet<int> _selectedCards = new HashSet<int>();
    private readonly HashSet<string> _selectedOptions = new HashSet<string>();
    private readonly Dictionary<int, GameObject> _cardTiles = new Dictionary<int, GameObject>();
    private readonly Dictionary<string, GameObject> _optionTiles = new Dictionary<string, GameObject>();
    private readonly Dictionary<string, string> _optionLabels = new Dictionary<string, string>();
    private bool _destructive;

    private bool CanAnswer()
    {
        var state = NetworkGameState.State;
        return !_busy && PlayersTurnsHandler.Instance != null && state != null && !state.IsPaused &&
            state.Resolution?.PendingDecision != null && state.Resolution.PendingDecision.IsPending &&
            state.Resolution.PendingDecision.DecisionId == _decision?.DecisionId &&
            state.Resolution.PendingDecision.PlayerId == NetworkGameState.LocalPlayerId;
    }


    public void Configure(GameStateSnapshot state, PendingDecisionSnapshot decision, ExtensionCardData source,
        Action<int[], string[]> answer)
    {
        Clear(); _decision = decision; _busy = false;
        _destructive = DecisionPresentation.IsDestructiveSelection(decision, source);
        bool optionChoice = decision.Zone == "options";
        string action = DecisionPresentation.QuickCardAction(decision, source);
        _draft = decision.MaxSelections > 1;
        bool directCardPick = !optionChoice && action == null && decision.MaxSelections == 1;
        transform.Find("Panel/Source").GetComponent<DecisionSourceView>().Bind(state, decision);
        transform.Find("Panel/Prompt").GetComponent<Text>().text = decision.Prompt;
        transform.Find("Panel/Heading").GetComponent<Text>().text = "À VOUS DE CHOISIR";
        transform.Find("Panel/Available/Title").GetComponent<Text>().text = "CARTE CONCERNÉE";
        var previews = new List<int>(decision.CandidateInstanceIds ?? new List<int>());
        if (previews.Count == 0 && decision.TriggerEvent != null && decision.TriggerEvent.PlayerId == decision.PlayerId &&
            decision.TriggerEvent.CardInstanceId > 0 && decision.TriggerEvent.CardInstanceId != DecisionPresentation.SourceId(decision))
            previews.Add(decision.TriggerEvent.CardInstanceId);
        foreach (int id in previews)
        {
            CardInstance card = NetworkGameState.FindCardInstance(state, id);
            if (card == null || !RoomGameSetup.TryResolveCard(card.DefinitionId, out ExtensionPackageData extension, out ExtensionCardData definition)) continue;
            GameObject tile = Instantiate(_cardPrefab, transform.Find("Panel/Available/Scroll/Viewport/Content"), false);
            tile.GetComponent<DecisionDragCard>().BindQuick(() =>
            {
                if (!CanAnswer() || optionChoice) return;
                if (directCardPick) { SetBusy(true); answer(new[] { id }, new string[0]); return; }
                if (!_draft) return;
                PendingDecisionSelectionRules.Toggle(_selectedCards, id, decision.MaxSelections);
                RefreshDraft();
            });
            _cardTiles[id] = tile;
            Image art = tile.transform.Find("Artwork").GetComponent<Image>();
            art.sprite = ExtensionVisualLoader.LoadCardArtwork(extension, definition); art.enabled = art.sprite != null;
            tile.transform.Find("Label").GetComponent<Text>().text = definition.name;
            tile.transform.Find("Selected").gameObject.SetActive(false);
            SetHalo(tile, CardSelectionHalo.HighlightState.Candidate);
            _items.Add(tile);
        }
        transform.Find("Panel/Available/Title").GetComponent<Text>().text = previews.Count > 1 ? "CARTES CONCERNÉES" : "CARTE CONCERNÉE";
        transform.Find("Panel/Available/Empty").gameObject.SetActive(false);
        transform.Find("Panel/Available").gameObject.SetActive(previews.Count > 0);
        bool hasPreview = previews.Count > 0;
        transform.Find("Panel/Options").gameObject.SetActive(hasPreview);
        transform.Find("Panel/OptionsOnly").gameObject.SetActive(!hasPreview);
        string optionsPath = hasPreview ? "Panel/Options" : "Panel/OptionsOnly";
        _optionRoot = transform.Find(optionsPath + "/Scroll/Viewport/Content");
        _optionTitle = transform.Find(optionsPath + "/Title").GetComponent<Text>();
        _optionTitle.text = directCardPick ? "Cliquez sur une carte pour répondre." : "";
        if (decision.Zone == "options")
        {
            for (int i = 0; i < decision.CandidateDefinitionIds.Count; i++)
            {
                string id = decision.CandidateDefinitionIds[i];
                string label = decision.CandidateOptionLabels != null && i < decision.CandidateOptionLabels.Count
                    ? decision.CandidateOptionLabels[i] : id;
                if (_draft)
                {
                    Button button = AddButton(label, () => {
                        PendingDecisionSelectionRules.Toggle(_selectedOptions, id, decision.MaxSelections); RefreshDraft();
                    }, false);
                    _optionTiles[id] = button.gameObject;
                    _optionLabels[id] = label;
                }
                else AddButton(label, () => answer(new int[0], new[] { id }));
            }
        }
        else if (!_draft && action != null)
            AddButton(action,
                () => answer(new[] { decision.CandidateInstanceIds[0] }, new string[0]));
        if (_draft)
        {
            _confirm = AddButton("Valider", () => {
                int[] cards = new int[_selectedCards.Count]; _selectedCards.CopyTo(cards);
                string[] options = new string[_selectedOptions.Count]; _selectedOptions.CopyTo(options);
                answer(cards, options);
            });
            RefreshDraft();
        }
        else if (decision.AllowPass || decision.MinSelections == 0)
            AddButton(decision.Zone == "discard" ? "Laisser en défausse" : "Passer",
                () => answer(new int[0], new string[0]));
    }

    private Button AddButton(string label, Action answer, bool submit = true)
    {
        GameObject option = Instantiate(_optionPrefab, _optionRoot, false);
        option.transform.Find("Label").GetComponent<Text>().text = label;
        option.transform.Find("Selected").gameObject.SetActive(false);
        SetHalo(option, CardSelectionHalo.HighlightState.Candidate);
        option.GetComponent<DecisionDropZone>().enabled = false;
        Button button = option.GetComponent<Button>();
        button.onClick.AddListener(() =>
        {
            if (!CanAnswer()) return;
            if (submit) SetBusy(true);
            answer();
        });
        _buttons.Add(button); _items.Add(option);
        return button;
    }

    private void RefreshDraft()
    {
        if (!_draft || _decision == null) return;
        int count = _decision.Zone == "options" ? _selectedOptions.Count : _selectedCards.Count;
        foreach (var pair in _cardTiles)
        {
            bool selected = _selectedCards.Contains(pair.Key);
            pair.Value.transform.Find("Selected").gameObject.SetActive(selected);
            SetHalo(pair.Value, selected
                ? _destructive ? CardSelectionHalo.HighlightState.Destructive : CardSelectionHalo.HighlightState.Selected
                : CardSelectionHalo.HighlightState.Candidate);
        }
        foreach (var pair in _optionTiles)
        {
            bool selected = _selectedOptions.Contains(pair.Key);
            pair.Value.transform.Find("Selected").gameObject.SetActive(selected);
            Image background = pair.Value.GetComponent<Image>();
            if (background != null) background.color = selected
                ? new Color(0.25f, 0.48f, 0.18f, 1f)
                : new Color(0.27f, 0.24f, 0.16f, 1f);
            Text label = pair.Value.transform.Find("Label").GetComponent<Text>();
            label.text = (selected ? "✓  " : string.Empty) + _optionLabels[pair.Key];
            label.fontStyle = selected ? FontStyle.Bold : FontStyle.Normal;
            SetHalo(pair.Value, selected ? CardSelectionHalo.HighlightState.Selected
                : CardSelectionHalo.HighlightState.Candidate);
        }
        if (_confirm != null)
        {
            _confirm.interactable = !_busy && DecisionPresentation.IsValid(_decision, count);
            _confirm.transform.Find("Label").GetComponent<Text>().text = count == 0 && DecisionPresentation.IsValid(_decision, 0)
                ? "Passer" : "Valider (" + count + ")";
        }
        _optionTitle.text = DecisionPresentation.CountLabel(_decision, count);
    }

    public void SetBusy(bool busy)
    {
        _busy = busy;
        foreach (Button button in _buttons) if (button != null) button.interactable = !busy;
        RefreshDraft();
    }

    public void Clear()
    {
        foreach (GameObject item in _items) if (item != null) { item.SetActive(false); Destroy(item); }
        _items.Clear(); _buttons.Clear(); _cardTiles.Clear(); _optionTiles.Clear(); _optionLabels.Clear();
        _selectedCards.Clear(); _selectedOptions.Clear(); _confirm = null; _decision = null;
    }

    private static void SetHalo(GameObject target, CardSelectionHalo.HighlightState state)
    {
        CardSelectionHalo halo = target.GetComponent<CardSelectionHalo>();
        if (halo == null) halo = target.AddComponent<CardSelectionHalo>();
        halo.SetState(state);
    }
}
