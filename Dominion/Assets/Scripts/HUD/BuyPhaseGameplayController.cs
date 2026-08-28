using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Gameplay behaviour layered onto the single editable GameScreen:
/// Reserve layout/purchases, card play during Buy, in-play stacks, discard top and cleanup animation.
/// It never owns rules; every mutation is routed to NetworkGameState through PlayersTurnsHandler.
/// </summary>
public sealed class BuyPhaseGameplayController : MonoBehaviour
{
    private readonly Dictionary<string, SupplyPileInteractionBinding> _supplyBindings =
        new Dictionary<string, SupplyPileInteractionBinding>(StringComparer.OrdinalIgnoreCase);
    private readonly List<GameObject> _inPlayObjects = new List<GameObject>();

    private RectTransform _baseSupplyRoot;
    private RectTransform _kingdomSupplyRoot;
    private RectTransform _handRoot;
    private RectTransform _inPlayRoot;
    private RectTransform _discardPanel;
    private GameObject _discardTopObject;
    private GameObject _zoomOverlay;
    private Image _zoomImage;
    private Button _nextPhaseButton;
    private GameScreenController _screenController;
    private GameObject _artifactTilePrefab;

    private bool _reserveLayoutReady;
    private bool _cleanupAnimating;
    private int _lastAutoCleanupVersion = -1;
    private bool _missingArtifactTileLogged;

    private void Awake()
    {
        ResolveUi();
        if (_screenController != null)
            _screenController.BoardPlayerChanged += HandleBoardPlayerChanged;
        EnsureReserveLayout();
        HookCleanupButton();

        NetworkGameState.StateChanged += Refresh;
        Refresh(NetworkGameState.State);
    }

    private IEnumerator Start()
    {
        // BaseSupplyController is attached at runtime too. One frame guarantees all
        // initial dynamic pile objects exist before bindings are decorated.
        yield return null;
        ResolveUi();
        EnsureReserveLayout();
        Refresh(NetworkGameState.State);
    }

    private void OnDestroy()
    {
        NetworkGameState.StateChanged -= Refresh;
        if (_screenController != null)
            _screenController.BoardPlayerChanged -= HandleBoardPlayerChanged;
    }

    private void Refresh(GameStateSnapshot state)
    {
        ResolveUi();
        EnsureReserveLayout();
        EnsureSupplyBindings();

        PlayerStateSnapshot localPlayer = ResolveLocalPlayer(state);
        PlayerStateSnapshot activePlayer = state != null && state.Players != null
            ? state.Players.Find(player => player != null && player.PlayerId == state.ActivePlayerId)
            : null;
        PlayerStateSnapshot viewedPlayer = _screenController != null
            ? _screenController.ResolveViewedPlayer(state)
            : activePlayer;

        RefreshSupplyStates(state, localPlayer);
        BindHandGameplay(state, localPlayer);
        RenderInPlay(state, viewedPlayer ?? activePlayer);
        RenderDiscardTop(state, viewedPlayer ?? activePlayer);

        if (state == null || localPlayer == null || state.IsPaused || _cleanupAnimating)
            return;

        bool localTurn = state.ActivePlayerId == localPlayer.PlayerId;
        if (!localTurn)
            return;

        bool explicitCleanup = string.Equals(state.Phase, NetworkGameState.CleanupPhase, StringComparison.Ordinal);
        bool noRemainingBuys = string.Equals(state.Phase, NetworkGameState.BuyPhase, StringComparison.Ordinal) &&
                               localPlayer.Buys <= 0;

        if ((explicitCleanup || noRemainingBuys) && _lastAutoCleanupVersion != state.Version)

        {
            _lastAutoCleanupVersion = state.Version;
            BeginCleanupAnimation();
        }
    }

    private void ResolveUi()
    {
        if (_screenController == null)
            _screenController = GetComponent<GameScreenController>();
        if (_artifactTilePrefab == null)
            _artifactTilePrefab = Resources.Load<GameObject>("UI/ArtifactTile");

        Transform baseSupply = FindDeepChild(transform, "BaseSupply");
        if (baseSupply is RectTransform baseRect)
            _baseSupplyRoot = baseRect;

        Transform kingdomSupply = FindDeepChild(transform, "KingdomSupply");
        if (kingdomSupply is RectTransform kingdomRect)
            _kingdomSupplyRoot = kingdomRect;

        Transform localHand = FindDeepChild(transform, "LocalHand");
        Transform handCards = FindDirectChild(localHand, "Cards");
        if (handCards is RectTransform handRect)
            _handRoot = handRect;

        Transform inPlayPanel = FindDeepChild(transform, "InPlayPanel");
        Transform inPlayCards = FindDirectChild(inPlayPanel, "Cards");
        if (inPlayCards is RectTransform inPlayRect)
            _inPlayRoot = inPlayRect;

        Transform discard = FindDeepChild(transform, "Discard");
        if (discard is RectTransform discardRect)
            _discardPanel = discardRect;

        Transform zoom = FindDeepChild(transform, "CardZoomOverlay");
        if (zoom != null)
        {
            _zoomOverlay = zoom.gameObject;
            Transform card = FindDirectChild(zoom, "Card");
            _zoomImage = card != null ? card.GetComponent<Image>() : null;
        }

        Transform nextPhase = FindDeepChild(transform, "NextPhaseButton");
        if (nextPhase != null)
            _nextPhaseButton = nextPhase.GetComponent<Button>();
    }

    /// <summary>
    /// Shows the whole Reserve at once: the seven permanent base piles on the left and
    /// the ten selected Kingdom piles on the right. No scrolling and no overlapping.
    /// Existing BaseSupply/KingdomSupply objects are reused so local prefab edits survive.
    /// </summary>
    private void EnsureReserveLayout()
    {
        if (_reserveLayoutReady)
            return;

        Transform supplyPanelTransform = FindDeepChild(transform, "SupplyPanel");
        if (!(supplyPanelTransform is RectTransform supplyPanel) ||
            _baseSupplyRoot == null || _kingdomSupplyRoot == null)
            return;

        Transform baseLabel = FindDeepChild(supplyPanel, "BaseSupplyLabel");
        Transform kingdomLabel = FindDeepChild(supplyPanel, "KingdomLabel");

        // Clean up a ReserveScrollViewport created by the previous layout if scripts
        // are hot-reloaded while Play Mode is already running.
        Transform oldViewport = FindDirectChild(supplyPanel, "ReserveScrollViewport");

        if (baseLabel != null)
            baseLabel.SetParent(supplyPanel, false);
        if (kingdomLabel != null)
            kingdomLabel.SetParent(supplyPanel, false);
        _baseSupplyRoot.SetParent(supplyPanel, false);
        _kingdomSupplyRoot.SetParent(supplyPanel, false);

        if (oldViewport != null)
            Destroy(oldViewport.gameObject);

        // Layout is authored entirely in GameScreen.prefab. Runtime code only checks
        // that the required components exist and never overwrites Inspector values.
        if (_baseSupplyRoot.GetComponent<GridLayoutGroup>() == null ||
            _kingdomSupplyRoot.GetComponent<GridLayoutGroup>() == null)
        {
            Debug.LogError("BaseSupply and KingdomSupply must each define a GridLayoutGroup in GameScreen.prefab.");
            return;
        }

        _reserveLayoutReady = true;
        LayoutRebuilder.ForceRebuildLayoutImmediate(_baseSupplyRoot);
        LayoutRebuilder.ForceRebuildLayoutImmediate(_kingdomSupplyRoot);
        LayoutRebuilder.ForceRebuildLayoutImmediate(supplyPanel);
        Canvas.ForceUpdateCanvases();
    }

    private void HookCleanupButton()
    {
        if (_nextPhaseButton == null)
            return;

        _nextPhaseButton.onClick.RemoveAllListeners();
        _nextPhaseButton.onClick.AddListener(BeginCleanupAnimation);
    }

    private void EnsureSupplyBindings()
    {
        BindSupplyRoot(_baseSupplyRoot);
        BindSupplyRoot(_kingdomSupplyRoot);
    }

    private void BindSupplyRoot(RectTransform root)
    {
        if (root == null)
            return;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            string definitionId = ResolveSupplyDefinitionId(child.name);
            if (string.IsNullOrEmpty(definitionId))
                continue;

            Image image = child.GetComponent<Image>();
            if (image == null)
                continue;

            SupplyPileInteractionBinding binding = child.GetComponent<SupplyPileInteractionBinding>();
            if (binding == null)
                binding = child.gameObject.AddComponent<SupplyPileInteractionBinding>();

            binding.Bind(definitionId, image.sprite, RequestBuyCard, ShowZoom);
            _supplyBindings[definitionId] = binding;
        }
    }

    private string ResolveSupplyDefinitionId(string objectName)
    {
        const string basePrefix = "BaseSupply_";
        const string kingdomPrefix = "Supply_";

        if (objectName.StartsWith(basePrefix, StringComparison.Ordinal))
            return "base:" + objectName.Substring(basePrefix.Length);

        if (!objectName.StartsWith(kingdomPrefix, StringComparison.Ordinal))
            return null;

        string cardId = objectName.Substring(kingdomPrefix.Length);
        GameSetupConfig setup = RoomGameSetup.ReadCurrent();
        if (setup == null || setup.kingdomCardIds == null)
            return null;

        foreach (string cardRef in setup.kingdomCardIds)
        {
            int separator = cardRef != null ? cardRef.IndexOf(':') : -1;
            if (separator > 0 && separator < cardRef.Length - 1 &&
                string.Equals(cardRef.Substring(separator + 1), cardId, StringComparison.OrdinalIgnoreCase))
                return cardRef;
        }

        return null;
    }

    private void RefreshSupplyStates(GameStateSnapshot state, PlayerStateSnapshot localPlayer)
    {
        // A missing snapshot is a network synchronisation state, not an empty
        // Reserve. In particular this happens briefly when the game scene is opened.
        // Keep the piles visually neutral until their authoritative quantities arrive.
        bool supplyKnown = state != null && state.SupplyPiles != null && state.SupplyPiles.Count > 0;
        bool localTurn = state != null && localPlayer != null && state.ActivePlayerId == localPlayer.PlayerId;
        bool buyPhase = state != null && string.Equals(state.Phase, NetworkGameState.BuyPhase, StringComparison.Ordinal);

        foreach (KeyValuePair<string, SupplyPileInteractionBinding> pair in _supplyBindings.ToList())
        {
            SupplyPileInteractionBinding binding = pair.Value;
            if (binding == null)
            {
                _supplyBindings.Remove(pair.Key);
                continue;
            }

            SupplyPileSnapshot pile = supplyKnown ? NetworkGameState.FindSupplyPile(state, pair.Key) : null;
            int? remaining = pile != null ? Mathf.Max(0, pile.RemainingCount) : (int?)null;
            binding.SetRemaining(remaining);

            ExtensionPackageData extension;
            ExtensionCardData definition;
            bool resolved = RoomGameSetup.TryResolveCard(pair.Key, out extension, out definition);
            int effectiveCost = resolved ? CostRules.GetEffectiveCost(state, definition) : -1;
            bool buyable = resolved &&
                           remaining.HasValue && remaining.Value > 0 &&
                           localTurn &&
                           buyPhase &&
                           state != null && !state.IsPaused &&
                           localPlayer.Buys > 0 &&
                           effectiveCost >= 0 && effectiveCost <= localPlayer.Coins;
            binding.SetBuyable(buyable);
        }
    }

    private void BindHandGameplay(GameStateSnapshot state, PlayerStateSnapshot localPlayer)
    {
        if (_handRoot == null)
            return;

        bool localTurn = state != null && localPlayer != null && state.ActivePlayerId == localPlayer.PlayerId;
        bool buyPhase = state != null && string.Equals(state.Phase, NetworkGameState.BuyPhase, StringComparison.Ordinal);

        for (int i = 0; i < _handRoot.childCount; i++)
        {
            Transform child = _handRoot.GetChild(i);
            HandCardMotion motion = child.GetComponent<HandCardMotion>();
            if (motion == null || motion.InstanceId <= 0)
                continue;

            CardInstance instance = NetworkGameState.FindCardInstance(state, motion.InstanceId);
            ExtensionPackageData extension;
            ExtensionCardData definition;
            bool treasure = instance != null &&
                            RoomGameSetup.TryResolveCard(instance.DefinitionId, out extension, out definition) &&
                            IsTreasure(definition);

            bool playable = treasure && localTurn && buyPhase && state != null && !state.IsPaused;
            HandGameplayInteraction gameplay = child.GetComponent<HandGameplayInteraction>();
            if (gameplay == null)
                gameplay = child.gameObject.AddComponent<HandGameplayInteraction>();
            gameplay.Bind(motion.InstanceId, _inPlayRoot, RequestPlayCard, playable);
        }
    }

    private void RenderInPlay(GameStateSnapshot state, PlayerStateSnapshot viewedPlayer)
    {
        Clear(_inPlayObjects);
        if (_inPlayRoot == null || state == null || viewedPlayer == null)
            return;

        Dictionary<string, List<CardInstance>> groups = new Dictionary<string, List<CardInstance>>(StringComparer.OrdinalIgnoreCase);
        List<string> order = new List<string>();

        if (viewedPlayer.InPlay != null)
        {
            foreach (int instanceId in viewedPlayer.InPlay)
            {
                CardInstance instance = NetworkGameState.FindCardInstance(state, instanceId);
                if (instance == null || string.IsNullOrEmpty(instance.DefinitionId))
                    continue;

                if (!groups.ContainsKey(instance.DefinitionId))
                {
                    groups[instance.DefinitionId] = new List<CardInstance>();
                    order.Add(instance.DefinitionId);
                }
                groups[instance.DefinitionId].Add(instance);
            }
        }

        foreach (string definitionId in order)
        {
            ExtensionPackageData extension;
            ExtensionCardData definition;
            if (!RoomGameSetup.TryResolveCard(definitionId, out extension, out definition))
                continue;

            Sprite sprite = ExtensionVisualLoader.LoadCardArtwork(extension, definition);
            GameObject stack = CreateInPlayStack(definition.id, sprite, definition, groups[definitionId].Count);
            stack.transform.SetParent(_inPlayRoot, false);
            _inPlayObjects.Add(stack);
        }

        RenderArtifacts(state, viewedPlayer);

        LayoutRebuilder.ForceRebuildLayoutImmediate(_inPlayRoot);
    }

    private void RenderArtifacts(GameStateSnapshot state, PlayerStateSnapshot viewedPlayer)
    {
        if (viewedPlayer == null || viewedPlayer.Artifacts == null || viewedPlayer.Artifacts.Count == 0)
            return;
        if (_artifactTilePrefab == null)
        {
            if (!_missingArtifactTileLogged)
            {
                _missingArtifactTileLogged = true;
                Debug.LogError("Required UI prefab Resources/UI/ArtifactTile.prefab is missing.", this);
            }
            return;
        }

        foreach (int instanceId in viewedPlayer.Artifacts)
        {
            CardInstance instance = NetworkGameState.FindCardInstance(state, instanceId);
            if (instance == null || !RoomGameSetup.TryResolveCard(instance.DefinitionId,
                    out ExtensionPackageData extension, out ExtensionCardData definition))
                continue;

            GameObject tile = Instantiate(_artifactTilePrefab, _inPlayRoot, false);
            tile.name = "Artifact_" + definition.id;
            Transform labelTransform = FindDeepChild(tile.transform, "Label");
            Text label = labelTransform != null ? labelTransform.GetComponent<Text>() : null;
            if (label != null) label.text = definition.name;
            Transform artworkTransform = FindDeepChild(tile.transform, "Artwork");
            Image artwork = artworkTransform != null ? artworkTransform.GetComponent<Image>() : null;
            Sprite sprite = ExtensionVisualLoader.LoadCardArtwork(extension, definition);
            if (artwork != null)
            {
                artwork.sprite = sprite;
                artwork.gameObject.SetActive(sprite != null);
            }
            Button button = tile.GetComponent<Button>();
            if (button != null)
            {
                button.interactable = sprite != null;
                if (sprite != null)
                {
                    Sprite capturedSprite = sprite;
                    ExtensionCardData capturedDefinition = definition;
                    button.onClick.AddListener(() => ShowZoom(capturedSprite, capturedDefinition));
                }
            }
            _inPlayObjects.Add(tile);
        }
    }

    private GameObject CreateInPlayStack(string cardId, Sprite sprite, ExtensionCardData definition, int count)
    {
        GameObject stack = new GameObject("InPlay_" + cardId, typeof(RectTransform), typeof(LayoutElement));
        RectTransform stackRect = stack.GetComponent<RectTransform>();
        stackRect.sizeDelta = new Vector2(116f, 168f);

        LayoutElement layout = stack.GetComponent<LayoutElement>();
        layout.preferredWidth = 116f;
        layout.minWidth = 116f;
        layout.preferredHeight = 168f;
        layout.minHeight = 168f;

        int layers = Mathf.Clamp(count, 1, 3);
        for (int i = 0; i < layers; i++)
        {
            RuntimeCardView layer = RuntimeCardView.Create(stack.transform, "CardLayer" + i, definition, sprite, i == layers - 1);
            if (layer == null) continue;
            GameObject layerObject = layer.gameObject;
            RectTransform layerRect = layerObject.GetComponent<RectTransform>();
            layerRect.anchorMin = new Vector2(0.5f, 0.5f);
            layerRect.anchorMax = new Vector2(0.5f, 0.5f);
            layerRect.pivot = new Vector2(0.5f, 0.5f);
            layerRect.sizeDelta = new Vector2(104f, 160f);
            layerRect.anchoredPosition = new Vector2(i * 5f, -i * 3f);

            if (i == layers - 1 && sprite != null)
            {
                CardPointerInteraction pointer = layer.Pointer;
                pointer.InspectOnLongPress = false;
                Sprite captured = sprite;
                ExtensionCardData capturedDefinition = definition;
                pointer.InspectRequested += () => ShowZoom(captured, capturedDefinition);
            }
        }

        if (count > 1)
            CreateCountBadge(stack.transform, count);

        return stack;
    }

    private void RenderDiscardTop(GameStateSnapshot state, PlayerStateSnapshot viewedPlayer)
    {
        if (_discardTopObject != null)
            Destroy(_discardTopObject);
        _discardTopObject = null;

        if (_discardPanel == null || state == null || viewedPlayer == null || viewedPlayer.Discard == null || viewedPlayer.Discard.Count == 0)
            return;

        int topInstanceId = viewedPlayer.Discard[viewedPlayer.Discard.Count - 1];
        CardInstance instance = NetworkGameState.FindCardInstance(state, topInstanceId);
        if (instance == null)
            return;

        ExtensionPackageData extension;
        ExtensionCardData definition;
        if (!RoomGameSetup.TryResolveCard(instance.DefinitionId, out extension, out definition))
            return;

        Sprite sprite = ExtensionVisualLoader.LoadCardArtwork(extension, definition);
        RuntimeCardView discardView = RuntimeCardView.Create(_discardPanel, "DiscardTopCard", definition, sprite, sprite != null);
        if (discardView == null) return;
        _discardTopObject = discardView.gameObject;
        _discardTopObject.transform.SetSiblingIndex(0);

        RectTransform rect = _discardTopObject.GetComponent<RectTransform>();
        SetAnchors(rect, new Vector2(0.06f, 0.04f), new Vector2(0.94f, 0.96f));

        if (sprite != null)
        {
            CardPointerInteraction pointer = discardView.Pointer;
            pointer.InspectOnLongPress = false;
            Sprite captured = sprite;
            ExtensionCardData capturedDefinition = definition;
            pointer.InspectRequested += () => ShowZoom(captured, capturedDefinition);
        }
    }

    private void RequestPlayCard(int instanceId)
    {
        if (PlayersTurnsHandler.Instance != null)
            PlayersTurnsHandler.Instance.PlayCard(instanceId);
    }

    private void RequestBuyCard(string definitionId)
    {
        if (PlayersTurnsHandler.Instance != null)
            PlayersTurnsHandler.Instance.BuyCard(definitionId);
    }

    private void BeginCleanupAnimation()
    {
        if (_cleanupAnimating)
            return;

        GameStateSnapshot state = NetworkGameState.State;
        PlayerStateSnapshot localPlayer = ResolveLocalPlayer(state);
        if (state == null || localPlayer == null || state.IsPaused || state.ActivePlayerId != localPlayer.PlayerId)
            return;

        // The same existing button owns both transitions. Action -> Buy is immediate and
        // does not touch the hand; Buy/Cleanup keeps the established cleanup animation.
        if (string.Equals(state.Phase, NetworkGameState.ActionPhase, StringComparison.Ordinal))
        {
            if (PlayersTurnsHandler.Instance != null)
                PlayersTurnsHandler.Instance.AdvancePhase();
            return;
        }

        if (!string.Equals(state.Phase, NetworkGameState.BuyPhase, StringComparison.Ordinal) &&
            !string.Equals(state.Phase, NetworkGameState.CleanupPhase, StringComparison.Ordinal))
            return;

        StartCoroutine(CleanupAnimationRoutine(localPlayer.PlayerId));
    }

    private IEnumerator CleanupAnimationRoutine(string localPlayerId)
    {
        _cleanupAnimating = true;

        if (_discardPanel == null)
        {
            if (PlayersTurnsHandler.Instance != null)
                PlayersTurnsHandler.Instance.AdvancePhase();
            _cleanupAnimating = false;
            yield break;
        }

        List<RectTransform> visuals = new List<RectTransform>();
        if (_handRoot != null)
        {
            for (int i = 0; i < _handRoot.childCount; i++)
            {
                RectTransform rect = _handRoot.GetChild(i) as RectTransform;
                if (rect != null && rect.GetComponent<HandCardMotion>() != null)
                    visuals.Add(rect);
            }
        }

        if (_screenController == null || _screenController.ViewedPlayerId == localPlayerId)
            foreach (GameObject inPlay in _inPlayObjects)
            {
                if (inPlay != null && inPlay.transform is RectTransform rect)
                    visuals.Add(rect);
            }

        RectTransform animationRoot = transform as RectTransform;
        Vector3 targetWorld = _discardPanel.TransformPoint(_discardPanel.rect.center);
        List<Vector3> starts = new List<Vector3>();
        List<Vector3> startScales = new List<Vector3>();

        foreach (RectTransform rect in visuals)
        {
            starts.Add(rect.position);
            startScales.Add(rect.localScale);

            HandCardMotion motion = rect.GetComponent<HandCardMotion>();
            if (motion != null)
                motion.enabled = false;
            CardPointerInteraction pointer = rect.GetComponent<CardPointerInteraction>();
            if (pointer != null)
                pointer.enabled = false;
            LayoutElement element = rect.GetComponent<LayoutElement>();
            if (element != null)
                element.ignoreLayout = true;

            if (animationRoot != null)
                rect.SetParent(animationRoot, true);
            rect.SetAsLastSibling();
        }

        const float duration = 0.32f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);

            for (int i = 0; i < visuals.Count; i++)
            {
                RectTransform rect = visuals[i];
                if (rect == null)
                    continue;

                rect.position = Vector3.Lerp(starts[i], targetWorld, eased);
                rect.localScale = Vector3.Lerp(startScales[i], Vector3.one * 0.32f, eased);
            }

            yield return null;
        }

        if (PlayersTurnsHandler.Instance != null)
            PlayersTurnsHandler.Instance.AdvancePhase();

        yield return new WaitForSecondsRealtime(0.08f);
        _cleanupAnimating = false;
    }

    private void ShowZoom(Sprite sprite, ExtensionCardData definition)
    {
        if (_zoomOverlay == null || _zoomImage == null || sprite == null)
            return;

        _zoomImage.sprite = sprite;
        _zoomImage.preserveAspect = true;
        DynamicCardCostView.Attach(_zoomImage.gameObject, definition);
        _zoomOverlay.SetActive(true);
        _zoomOverlay.transform.SetAsLastSibling();
    }

    private void HandleBoardPlayerChanged()
    {
        Refresh(NetworkGameState.State);
    }

    private static PlayerStateSnapshot ResolveLocalPlayer(GameStateSnapshot state)
    {
        if (state == null || state.Players == null)
            return null;

        string localId = NetworkGameState.LocalPlayerId;
        PlayerStateSnapshot local = state.Players.Find(player => player != null && player.PlayerId == localId);
        if (local != null)
            return local;

        if (PhotonNetwork.LocalPlayer != null)
        {
            int actor = PhotonNetwork.LocalPlayer.ActorNumber;
            local = state.Players.Find(player => player != null && player.ActorNumber == actor);
            if (local != null)
                return local;
        }

        return state.Players.Count == 1 ? state.Players[0] : null;
    }

    private static bool IsTreasure(ExtensionCardData definition)
    {
        return definition != null && definition.types != null && definition.types.Any(type =>
            string.Equals(type, "Trésor", StringComparison.OrdinalIgnoreCase));
    }

    private static void CreateCountBadge(Transform parent, int count)
    {
        GameObject badgeObject = new GameObject("Count", typeof(RectTransform), typeof(Image));
        badgeObject.transform.SetParent(parent, false);
        RectTransform badgeRect = badgeObject.GetComponent<RectTransform>();
        badgeRect.anchorMin = new Vector2(0.68f, 0.79f);
        badgeRect.anchorMax = new Vector2(0.98f, 0.98f);
        badgeRect.offsetMin = Vector2.zero;
        badgeRect.offsetMax = Vector2.zero;

        Image badge = badgeObject.GetComponent<Image>();
        badge.color = new Color(0.03f, 0.03f, 0.03f, 0.9f);
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
        text.text = count.ToString();
        text.fontSize = 20;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.raycastTarget = false;
    }

    private static void Clear(List<GameObject> objects)
    {
        foreach (GameObject item in objects)
        {
            if (item != null)
                Destroy(item);
        }
        objects.Clear();
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

    private static void SetAnchors(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
