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
    private const string PanelPrefabResourcePath = "UI/PendingDecisionPanel";
    private const string InstructionBarPrefabResourcePath = "UI/DecisionInstructionBar";
    private const string CardDrawerPrefabResourcePath = "UI/DecisionCardDrawer";
    private const string OptionPrefabResourcePath = "UI/DecisionOption";
    private const string DeckPositionPrefabResourcePath = "UI/DeckPositionDecision";
    private const string CardNamePrefabResourcePath = "UI/CardNameDecision";
    private const int MaximumGenericOptions = 4;
    private readonly HashSet<int> _selected = new HashSet<int>();
    private readonly HashSet<string> _selectedSupply = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _selectedOptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Image> _optionButtons = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<CardPointerInteraction, Action> _selectionHandlers = new Dictionary<CardPointerInteraction, Action>();
    private readonly Dictionary<Image, Color> _originalImageColors = new Dictionary<Image, Color>();
    private readonly List<CardSelectionHalo> _selectionHalos = new List<CardSelectionHalo>();
    private readonly List<GameObject> _externalCards = new List<GameObject>();

    private RectTransform _panel;
    private GameObject _instructionBar;
    private GameObject _cardDrawer;
    private GameObject _drawerExpanded;
    private GameObject _drawerCollapsedTab;
    private Text _barPromptText;
    private Text _barCountText;
    private Button _barConfirmButton;
    private Text _panelPromptText;
    private Text _panelCountText;
    private Button _panelConfirmButton;
    private Text _drawerPromptText;
    private Text _drawerCountText;
    private Button _drawerConfirmButton;
    private RectTransform _externalCardsRoot;
    private RectTransform _optionsRoot;
    private RectTransform _optionPreviewCardsRoot;
    private RectTransform _optionPreviewOptionsRoot;
    private DecisionScrollGrid _cardsScrollGrid;
    private DecisionScrollGrid _optionsScrollGrid;
    private DecisionScrollGrid _optionPreviewCardsScrollGrid;
    private DecisionScrollGrid _optionPreviewOptionsScrollGrid;
    private DeckPositionDecisionView _deckPositionView;
    private CardNameDecisionView _cardNameView;
    private Text _promptText;
    private Text _countText;
    private Button _confirmButton;
    private string _boundDecisionId;
    private bool _submitPending;
    private bool _panelBindingFailed;

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
            _selectedOptions.Clear();
            _submitPending = false;
            _boundDecisionId = decision.DecisionId;
        }

        bool supplyChoice = IsSupplyDecision(decision);
        bool optionChoice = IsOptionDecision(decision);
        bool deckPositionChoice = IsDeckPositionDecision(decision);
        bool cardNameChoice = IsCardNameDecision(decision);
        bool hasCardPreview = optionChoice && decision.CandidateInstanceIds != null && decision.CandidateInstanceIds.Count > 0;
        CardZone choiceZone = ResolveDecisionZone(decision);
        ConfigurePanel(choiceZone, supplyChoice, optionChoice, deckPositionChoice, cardNameChoice, hasCardPreview, newDecision);
        if (_promptText != null)
            _promptText.text = string.IsNullOrWhiteSpace(decision.Prompt) ? "Faites un choix." : decision.Prompt;

        if (optionChoice)
        {
            ClearSupplyDecisionVisuals();
            if (newDecision)
            {
                if (deckPositionChoice) BuildDeckPositionChoice(decision);
                else if (cardNameChoice) BuildCardNameChoice(decision);
                else
                {
                    if (hasCardPreview) BuildOptionPreviewCards(state, decision);
                    BuildOptionButtons(decision,
                        hasCardPreview ? _optionPreviewOptionsRoot : _optionsRoot,
                        hasCardPreview ? _optionPreviewOptionsScrollGrid : _optionsScrollGrid);
                }
            }
            else if (!deckPositionChoice && !cardNameChoice) RefreshOptionButtons();
        }
        else if (supplyChoice)
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

    private static bool IsOptionDecision(PendingDecisionSnapshot decision) =>
        decision != null && string.Equals(decision.Zone, "options", StringComparison.OrdinalIgnoreCase);

    private static bool IsDeckPositionDecision(PendingDecisionSnapshot decision) =>
        IsOptionDecision(decision) && !string.IsNullOrEmpty(decision.Operation) &&
        decision.Operation.StartsWith("insert_selected_into_deck|", StringComparison.OrdinalIgnoreCase);

    private static bool IsCardNameDecision(PendingDecisionSnapshot decision) =>
        IsOptionDecision(decision) && string.Equals(decision.Operation, "name_card", StringComparison.OrdinalIgnoreCase);

    private static CardZone ResolveDecisionZone(PendingDecisionSnapshot decision)
    {
        if (decision == null || string.IsNullOrWhiteSpace(decision.Zone) || IsSupplyDecision(decision) || IsOptionDecision(decision)) return CardZone.Hand;
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
        BuildCards(state, decision != null ? decision.CandidateInstanceIds : null,
            _externalCardsRoot, _cardsScrollGrid, true);
    }

    private void BuildOptionPreviewCards(GameStateSnapshot state, PendingDecisionSnapshot decision)
    {
        BuildCards(state, decision != null ? decision.CandidateInstanceIds : null,
            _optionPreviewCardsRoot, _optionPreviewCardsScrollGrid, false);
    }

    private void BuildCards(GameStateSnapshot state, IEnumerable<int> instanceIds,
        RectTransform cardsRoot, DecisionScrollGrid scrollGrid, bool selectable)
    {
        if (cardsRoot == null || state == null || instanceIds == null) return;
        cardsRoot.gameObject.SetActive(true);

        foreach (int instanceId in instanceIds)
        {
            CardInstance instance = NetworkGameState.FindCardInstance(state, instanceId);
            if (instance == null) continue;
            ExtensionPackageData extension; ExtensionCardData definition;
            if (!RoomGameSetup.TryResolveCard(instance.DefinitionId, out extension, out definition)) continue;

            RuntimeCardView cardView = RuntimeCardView.Create(
                cardsRoot,
                (selectable ? "DecisionCard_" : "DecisionPreviewCard_") + instanceId,
                definition,
                ExtensionVisualLoader.LoadCardArtwork(extension, definition),
                true);
            if (cardView == null) continue;
            GameObject card = cardView.gameObject;
            CardPointerInteraction pointer = cardView.Pointer;
            pointer.InspectOnLongPress = false;
            if (selectable)
            {
                int capturedId = instanceId;
                Action handler = () => ToggleSelection(capturedId);
                pointer.PrimaryActionRequested += handler;
                _selectionHandlers.Add(pointer, handler);
            }
            _externalCards.Add(card);
        }
        scrollGrid?.RefreshLayout(true);
    }

    private void BuildOptionButtons(PendingDecisionSnapshot decision, RectTransform optionsRoot, DecisionScrollGrid scrollGrid)
    {
        if (optionsRoot == null || decision == null) return;
        optionsRoot.gameObject.SetActive(true);
        GameObject optionPrefab = Resources.Load<GameObject>(OptionPrefabResourcePath);
        if (optionPrefab == null)
        {
            Debug.LogError("DecisionOption prefab missing at Resources/UI/DecisionOption.", this);
            return;
        }
        List<string> ids = decision.CandidateDefinitionIds ?? new List<string>();
        List<string> labels = decision.CandidateOptionLabels ?? new List<string>();
        if (ids.Count > MaximumGenericOptions)
        {
            Debug.LogError("Generic decisions support at most " + MaximumGenericOptions +
                " options. Operation '" + decision.Operation + "' needs a dedicated prefab-backed control.", this);
            return;
        }
        for (int index = 0; index < ids.Count; index++)
        {
            string optionId = ids[index];
            if (string.IsNullOrWhiteSpace(optionId)) continue;
            string label = index < labels.Count && !string.IsNullOrWhiteSpace(labels[index]) ? labels[index] : optionId;
            GameObject optionObject = Instantiate(optionPrefab, optionsRoot);
            optionObject.name = "DecisionOption_" + optionId;
            Image image = optionObject.GetComponent<Image>();
            Button button = optionObject.GetComponent<Button>();
            Text text = optionObject.transform.Find("Label")?.GetComponent<Text>();
            if (image == null || button == null || text == null)
            {
                Debug.LogError("DecisionOption prefab contract is incomplete.", optionObject);
                Destroy(optionObject);
                continue;
            }
            string capturedId = optionId;
            button.onClick.AddListener(() => ToggleOptionSelection(capturedId));
            text.text = label;

            _optionButtons[optionId] = image;
            _externalCards.Add(optionObject);
        }
        RefreshOptionButtons();
        scrollGrid?.RefreshLayout(true);
    }

    private void BuildDeckPositionChoice(PendingDecisionSnapshot decision)
    {
        ClearExternalCards();
        _deckPositionView = EnsureSpecialDecisionView(_deckPositionView, DeckPositionPrefabResourcePath);
        if (_deckPositionView == null || !_deckPositionView.Configure(
                decision.CandidateDefinitionIds, decision.CandidateOptionLabels, SelectSingleOption))
            Debug.LogError("DeckPositionDecision prefab contract is incomplete.", this);
    }

    private void BuildCardNameChoice(PendingDecisionSnapshot decision)
    {
        ClearExternalCards();
        _cardNameView = EnsureSpecialDecisionView(_cardNameView, CardNamePrefabResourcePath);
        GameObject optionPrefab = Resources.Load<GameObject>(OptionPrefabResourcePath);
        if (_cardNameView == null || !_cardNameView.Configure(
                decision.CandidateDefinitionIds, decision.CandidateOptionLabels, optionPrefab, SelectSingleOption))
            Debug.LogError("CardNameDecision prefab contract is incomplete.", this);
    }

    private T EnsureSpecialDecisionView<T>(T existing, string resourcePath) where T : Component
    {
        if (existing != null)
            return existing;
        GameObject prefab = Resources.Load<GameObject>(resourcePath);
        if (prefab == null)
        {
            Debug.LogError(resourcePath + " prefab is missing.", this);
            return null;
        }
        GameObject instance = Instantiate(prefab, _panel);
        instance.name = prefab.name;
        T view = instance.GetComponent<T>();
        if (view == null)
        {
            Debug.LogError(resourcePath + " prefab has no " + typeof(T).Name + ".", instance);
            Destroy(instance);
        }
        return view;
    }

    private void SelectSingleOption(string optionId)
    {
        PendingDecisionSnapshot decision = ResolveLocalDecision(NetworkGameState.State);
        if (decision == null || _submitPending || !IsOptionDecision(decision))
            return;
        _selectedOptions.Clear();
        if (!string.IsNullOrEmpty(optionId) && decision.CandidateDefinitionIds != null &&
            decision.CandidateDefinitionIds.Contains(optionId))
            _selectedOptions.Add(optionId);
        RefreshSelectionUi(decision);
    }

    private void ToggleOptionSelection(string optionId)
    {
        PendingDecisionSnapshot decision = ResolveLocalDecision(NetworkGameState.State);
        if (decision == null || _submitPending || !IsOptionDecision(decision) ||
            decision.CandidateDefinitionIds == null || !decision.CandidateDefinitionIds.Contains(optionId)) return;
        if (_selectedOptions.Contains(optionId)) _selectedOptions.Remove(optionId);
        else
        {
            int max = Math.Max(decision.MinSelections, decision.MaxSelections);
            if (_selectedOptions.Count >= max) return;
            _selectedOptions.Add(optionId);
        }
        RefreshOptionButtons();
        RefreshSelectionUi(decision);
    }

    private void RefreshOptionButtons()
    {
        foreach (KeyValuePair<string, Image> pair in _optionButtons)
            if (pair.Value != null) pair.Value.color = _selectedOptions.Contains(pair.Key)
                ? new Color(0.51f, 0.40f, 0.18f, 1f)
                : new Color(0.25f, 0.22f, 0.15f, 1f);
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
        int selectedCount = IsOptionDecision(decision) ? _selectedOptions.Count : IsSupplyDecision(decision) ? _selectedSupply.Count : _selected.Count;
        if (_countText != null)
            _countText.text = decision.MinSelections == decision.MaxSelections
                ? selectedCount + " / " + decision.MaxSelections
                : selectedCount + " sélectionnée(s) — " + decision.MinSelections + " à " + decision.MaxSelections;
        if (_confirmButton != null)
            _confirmButton.interactable = ((selectedCount == 0 && decision.AllowPass) ||
                (selectedCount >= decision.MinSelections && selectedCount <= decision.MaxSelections)) && !_submitPending;
        if (!IsSupplyDecision(decision) && !IsOptionDecision(decision)) RefreshSelectionMarkers();
    }

    private void RefreshSelectionMarkers()
    {
        foreach (CardSelectionHalo halo in _selectionHalos)
            if (halo != null) halo.SetVisible(false);
        _selectionHalos.Clear();

        Transform handRoot = FindHandCardsRoot();
        if (handRoot != null)
            for (int i = 0; i < handRoot.childCount; i++)
            {
                Transform child = handRoot.GetChild(i);
                HandCardMotion motion = child.GetComponent<HandCardMotion>();
                if (motion != null && _selected.Contains(motion.InstanceId)) AddSelectionHalo(child.gameObject);
            }

        foreach (GameObject card in _externalCards)
        {
            if (card == null) continue;
            int id = ResolveExternalInstanceId(card.name);
            if (_selected.Contains(id)) AddSelectionHalo(card);
        }
    }

    private void AddSelectionHalo(GameObject target)
    {
        CardSelectionHalo halo = target.GetComponent<CardSelectionHalo>();
        if (halo == null) halo = target.AddComponent<CardSelectionHalo>();
        halo.SetVisible(true);
        if (!_selectionHalos.Contains(halo)) _selectionHalos.Add(halo);
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
        int selectedCount = IsOptionDecision(decision) ? _selectedOptions.Count : IsSupplyDecision(decision) ? _selectedSupply.Count : _selected.Count;
        if (!(selectedCount == 0 && decision.AllowPass) &&
            (selectedCount < decision.MinSelections || selectedCount > decision.MaxSelections)) return;

        PlayersTurnsHandler handler = PlayersTurnsHandler.Instance;
        if (handler == null) return;
        _submitPending = true;
        if (_confirmButton != null) _confirmButton.interactable = false;

        if (IsOptionDecision(decision))
        {
            string[] selected = new string[_selectedOptions.Count]; _selectedOptions.CopyTo(selected);
            handler.SubmitOptionDecision(decision.DecisionId, selected);
        }
        else if (IsSupplyDecision(decision))
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
        _selected.Clear(); _selectedSupply.Clear(); _selectedOptions.Clear(); _boundDecisionId = string.Empty; _submitPending = false;
        if (_panel != null) _panel.gameObject.SetActive(false);
        if (_instructionBar != null) _instructionBar.SetActive(false);
        if (_cardDrawer != null) _cardDrawer.SetActive(false);
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
        foreach (CardSelectionHalo halo in _selectionHalos)
            if (halo != null) halo.SetVisible(false);
        _selectionHalos.Clear();
    }

    private void ClearExternalCards()
    {
        foreach (GameObject card in _externalCards) if (card != null) Destroy(card);
        _externalCards.Clear();
        _optionButtons.Clear();
        _deckPositionView?.ResetView();
        _cardNameView?.ResetView();
    }

    private void EnsurePanel()
    {
        if (_panel != null || _panelBindingFailed) return;
        GameObject prefab = Resources.Load<GameObject>(PanelPrefabResourcePath);
        if (prefab == null)
        {
            Debug.LogError("PendingDecisionPanel prefab missing at Resources/UI/PendingDecisionPanel.", this);
            _panelBindingFailed = true;
            return;
        }

        GameObject panelObject = Instantiate(prefab, transform);
        panelObject.name = "PendingDecisionPanel";
        _panel = panelObject.GetComponent<RectTransform>();
        _panelPromptText = panelObject.transform.Find("Prompt")?.GetComponent<Text>();
        _panelCountText = panelObject.transform.Find("Count")?.GetComponent<Text>();
        Transform optionsViewport = panelObject.transform.Find("DecisionOptions");
        Transform optionPreviewCardsViewport = panelObject.transform.Find("OptionPreviewCards");
        Transform optionPreviewOptionsViewport = panelObject.transform.Find("OptionPreviewOptions");
        _optionsScrollGrid = optionsViewport != null ? optionsViewport.GetComponent<DecisionScrollGrid>() : null;
        _optionPreviewCardsScrollGrid = optionPreviewCardsViewport != null ? optionPreviewCardsViewport.GetComponent<DecisionScrollGrid>() : null;
        _optionPreviewOptionsScrollGrid = optionPreviewOptionsViewport != null ? optionPreviewOptionsViewport.GetComponent<DecisionScrollGrid>() : null;
        _optionsRoot = _optionsScrollGrid != null ? _optionsScrollGrid.Content : null;
        _optionPreviewCardsRoot = _optionPreviewCardsScrollGrid != null ? _optionPreviewCardsScrollGrid.Content : null;
        _optionPreviewOptionsRoot = _optionPreviewOptionsScrollGrid != null ? _optionPreviewOptionsScrollGrid.Content : null;
        _panelConfirmButton = panelObject.transform.Find("ConfirmDecision")?.GetComponent<Button>();
        if (_panel == null || _panelPromptText == null || _panelCountText == null ||
            _optionsScrollGrid == null || _optionPreviewCardsScrollGrid == null || _optionPreviewOptionsScrollGrid == null ||
            _optionsRoot == null || _optionPreviewCardsRoot == null || _optionPreviewOptionsRoot == null ||
            _panelConfirmButton == null)
        {
            Debug.LogError("PendingDecisionPanel prefab contract is incomplete.", panelObject);
            Destroy(panelObject);
            _panel = null;
            _panelBindingFailed = true;
            return;
        }

        GameObject barPrefab = Resources.Load<GameObject>(InstructionBarPrefabResourcePath);
        GameObject drawerPrefab = Resources.Load<GameObject>(CardDrawerPrefabResourcePath);
        if (barPrefab == null || drawerPrefab == null)
        {
            Debug.LogError("Adaptive decision prefabs are missing from Resources/UI.", this);
            Destroy(panelObject);
            _panel = null;
            _panelBindingFailed = true;
            return;
        }

        _instructionBar = Instantiate(barPrefab, transform);
        _instructionBar.name = "DecisionInstructionBar";
        _barPromptText = _instructionBar.transform.Find("Prompt")?.GetComponent<Text>();
        _barCountText = _instructionBar.transform.Find("Count")?.GetComponent<Text>();
        _barConfirmButton = _instructionBar.transform.Find("ConfirmDecision")?.GetComponent<Button>();

        _cardDrawer = Instantiate(drawerPrefab, transform);
        _cardDrawer.name = "DecisionCardDrawer";
        Transform expanded = _cardDrawer.transform.Find("Expanded");
        _drawerExpanded = expanded != null ? expanded.gameObject : null;
        Transform collapsedTab = _cardDrawer.transform.Find("CollapsedTab");
        _drawerCollapsedTab = collapsedTab != null ? collapsedTab.gameObject : null;
        _drawerPromptText = expanded?.Find("Prompt")?.GetComponent<Text>();
        _drawerCountText = expanded?.Find("Count")?.GetComponent<Text>();
        _drawerConfirmButton = expanded?.Find("ConfirmDecision")?.GetComponent<Button>();
        Transform cardsViewport = expanded?.Find("DecisionCards");
        _cardsScrollGrid = cardsViewport != null ? cardsViewport.GetComponent<DecisionScrollGrid>() : null;
        _externalCardsRoot = _cardsScrollGrid != null ? _cardsScrollGrid.Content : null;
        Button collapseButton = expanded?.Find("CollapseButton")?.GetComponent<Button>();
        Button expandButton = collapsedTab?.GetComponent<Button>();

        if (_barPromptText == null || _barCountText == null || _barConfirmButton == null ||
            _drawerExpanded == null || _drawerCollapsedTab == null || _drawerPromptText == null ||
            _drawerCountText == null || _drawerConfirmButton == null || _cardsScrollGrid == null ||
            _externalCardsRoot == null || collapseButton == null || expandButton == null)
        {
            Debug.LogError("Adaptive decision prefab contract is incomplete.", this);
            Destroy(panelObject);
            Destroy(_instructionBar);
            Destroy(_cardDrawer);
            _panel = null;
            _panelBindingFailed = true;
            return;
        }

        _panelConfirmButton.onClick.AddListener(Submit);
        _barConfirmButton.onClick.AddListener(Submit);
        _drawerConfirmButton.onClick.AddListener(Submit);
        collapseButton.onClick.AddListener(() => SetDrawerExpanded(false));
        expandButton.onClick.AddListener(() => SetDrawerExpanded(true));
        _panel.gameObject.SetActive(false);
        _instructionBar.SetActive(false);
        _cardDrawer.SetActive(false);
    }

    private void ConfigurePanel(CardZone zone, bool supplyChoice, bool optionChoice,
        bool deckPositionChoice, bool cardNameChoice, bool hasCardPreview, bool newDecision)
    {
        if (_panel == null || _instructionBar == null || _cardDrawer == null) return;
        bool cardsVisible = zone != CardZone.Hand && !supplyChoice && !optionChoice;
        bool compactVisible = !cardsVisible && !optionChoice;
        _panel.gameObject.SetActive(optionChoice);
        _instructionBar.SetActive(compactVisible);
        _cardDrawer.SetActive(cardsVisible);
        if (cardsVisible && newDecision) SetDrawerExpanded(true);

        if (optionChoice)
        {
            _promptText = _panelPromptText;
            _countText = _panelCountText;
            _confirmButton = _panelConfirmButton;
            _panel.SetAsLastSibling();
        }
        else if (cardsVisible)
        {
            _promptText = _drawerPromptText;
            _countText = _drawerCountText;
            _confirmButton = _drawerConfirmButton;
            _cardDrawer.transform.SetAsLastSibling();
        }
        else
        {
            _promptText = _barPromptText;
            _countText = _barCountText;
            _confirmButton = _barConfirmButton;
            _instructionBar.transform.SetAsLastSibling();
        }

        bool genericOptions = optionChoice && !deckPositionChoice && !cardNameChoice;
        if (_optionsScrollGrid != null) _optionsScrollGrid.gameObject.SetActive(genericOptions && !hasCardPreview);
        if (_optionPreviewCardsScrollGrid != null) _optionPreviewCardsScrollGrid.gameObject.SetActive(genericOptions && hasCardPreview);
        if (_optionPreviewOptionsScrollGrid != null) _optionPreviewOptionsScrollGrid.gameObject.SetActive(genericOptions && hasCardPreview);
        if (_deckPositionView != null) _deckPositionView.gameObject.SetActive(deckPositionChoice);
        if (_cardNameView != null) _cardNameView.gameObject.SetActive(cardNameChoice);
    }

    private void SetDrawerExpanded(bool expanded)
    {
        if (_drawerExpanded != null) _drawerExpanded.SetActive(expanded);
        if (_drawerCollapsedTab != null) _drawerCollapsedTab.SetActive(!expanded);
        if (expanded) _cardsScrollGrid?.RefreshLayout(false);
    }

    private Transform FindHandCardsRoot()
    {
        Transform localHand = FindDeepChild(transform, "LocalHand"); return FindDirectChild(localHand, "Cards");
    }

    private static Color MultiplyRgb(Color color, float multiplier) => new Color(color.r * multiplier, color.g * multiplier, color.b * multiplier, color.a);
    private static Transform FindDirectChild(Transform parent, string name)
    { if (parent == null) return null; for (int i = 0; i < parent.childCount; i++) { Transform child = parent.GetChild(i); if (string.Equals(child.name, name, StringComparison.Ordinal)) return child; } return null; }
    private static Transform FindDeepChild(Transform parent, string name)
    { if (parent == null) return null; for (int i = 0; i < parent.childCount; i++) { Transform child = parent.GetChild(i); if (string.Equals(child.name, name, StringComparison.Ordinal)) return child; Transform nested = FindDeepChild(child, name); if (nested != null) return nested; } return null; }
}
