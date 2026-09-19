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

    public void Configure(GameStateSnapshot state, PendingDecisionSnapshot decision, ExtensionCardData source,
        Action<int[], string[]> answer)
    {
        Clear(); _decision = decision; _busy = false;
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
            tile.GetComponent<DecisionDragCard>().enabled = false;
            Image art = tile.transform.Find("Artwork").GetComponent<Image>();
            art.sprite = ExtensionVisualLoader.LoadCardArtwork(extension, definition); art.enabled = art.sprite != null;
            tile.transform.Find("Label").GetComponent<Text>().text = definition.name;
            tile.transform.Find("Selected").gameObject.SetActive(false);
            _items.Add(tile);
        }
        transform.Find("Panel/Available/Empty").gameObject.SetActive(false);
        transform.Find("Panel/Available").gameObject.SetActive(previews.Count > 0);
        transform.Find("Panel/Options/Title").GetComponent<Text>().text = "";
        if (decision.Zone == "options")
        {
            for (int i = 0; i < decision.CandidateDefinitionIds.Count; i++)
            {
                string id = decision.CandidateDefinitionIds[i];
                string label = decision.CandidateOptionLabels != null && i < decision.CandidateOptionLabels.Count
                    ? decision.CandidateOptionLabels[i] : id;
                AddButton(label, () => answer(new int[0], new[] { id }));
            }
        }
        else
            AddButton(DecisionPresentation.QuickCardAction(decision, source),
                () => answer(new[] { decision.CandidateInstanceIds[0] }, new string[0]));
        if (decision.AllowPass || decision.MinSelections == 0)
            AddButton(decision.Zone == "discard" ? "Laisser en défausse" : "Passer",
                () => answer(new int[0], new string[0]));
    }

    private void AddButton(string label, Action answer)
    {
        GameObject option = Instantiate(_optionPrefab, transform.Find("Panel/Options/Scroll/Viewport/Content"), false);
        option.transform.Find("Label").GetComponent<Text>().text = label;
        option.transform.Find("Selected").gameObject.SetActive(false);
        option.GetComponent<DecisionDropZone>().enabled = false;
        Button button = option.GetComponent<Button>();
        button.onClick.AddListener(() =>
        {
            GameStateSnapshot state = NetworkGameState.State;
            if (_busy || PlayersTurnsHandler.Instance == null || state == null || state.IsPaused || state.Resolution?.PendingDecision == null ||
                !state.Resolution.PendingDecision.IsPending || state.Resolution.PendingDecision.DecisionId != _decision.DecisionId ||
                state.Resolution.PendingDecision.PlayerId != NetworkGameState.LocalPlayerId) return;
            SetBusy(true); answer();
        });
        _buttons.Add(button); _items.Add(option);
    }

    public void SetBusy(bool busy)
    {
        _busy = busy;
        foreach (Button button in _buttons) if (button != null) button.interactable = !busy;
    }

    public void Clear()
    {
        foreach (GameObject item in _items) if (item != null) { item.SetActive(false); Destroy(item); }
        _items.Clear(); _buttons.Clear(); _decision = null;
    }
}
