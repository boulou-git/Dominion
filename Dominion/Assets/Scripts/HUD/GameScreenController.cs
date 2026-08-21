using System;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Binds the editable GameScreen prefab to the current replicated game state.
/// The prefab owns layout/visuals; this controller only fills dynamic content.
/// </summary>
public sealed class GameScreenController : MonoBehaviour
{
    [Header("Top bar")]
    [SerializeField] private RectTransform _playersRoot;
    [SerializeField] private Text _turnText;

    [Header("Supply")]
    [SerializeField] private RectTransform _baseSupplyRoot;
    [SerializeField] private RectTransform _kingdomSupplyRoot;

    [Header("Board")]
    [SerializeField] private RectTransform _inPlayRoot;
    [SerializeField] private Text _boardTitle;

    [Header("Right status")]
    [SerializeField] private Text _phaseText;
    [SerializeField] private Text _actionsText;
    [SerializeField] private Text _buysText;
    [SerializeField] private Text _coinsText;
    [SerializeField] private Text _deckText;
    [SerializeField] private Text _discardText;
    [SerializeField] private Text _handCountText;
    [SerializeField] private Text _statusText;
    [SerializeField] private Button _nextPhaseButton;
    [SerializeField] private Text _nextPhaseButtonText;

    [Header("Hand / journal")]
    [SerializeField] private RectTransform _handRoot;
    [SerializeField] private Text _journalText;

    [Header("Card zoom")]
    [SerializeField] private GameObject _zoomOverlay;
    [SerializeField] private Image _zoomImage;
    [SerializeField] private Button _zoomCloseButton;

    private readonly List<GameObject> _playerPills = new List<GameObject>();
    private readonly List<GameObject> _kingdomCards = new List<GameObject>();
    private readonly List<GameObject> _handCards = new List<GameObject>();
    private readonly List<int> _localHandOrder = new List<int>();
    private readonly List<int> _renderedHandIds = new List<int>();
    private bool _kingdomBuilt;
    private bool _handRootFailureLogged;

    private static readonly Color Panel = new Color(0.11f, 0.105f, 0.095f, 1f);
    private static readonly Color Active = new Color(0.40f, 0.32f, 0.17f, 1f);
    private static readonly Color Local = new Color(0.20f, 0.29f, 0.32f, 1f);

    private void Awake()
    {
        ResolveHandRoot();

        if (_nextPhaseButton != null)
            _nextPhaseButton.onClick.AddListener(RequestNextPhase);
        if (_zoomCloseButton != null)
            _zoomCloseButton.onClick.AddListener(HideZoom);

        if (_zoomOverlay != null)
            _zoomOverlay.SetActive(false);

        ExtensionCatalog.Reload();
        NetworkGameState.StateChanged += Refresh;
        NetworkGameState.HydrateFromRoom(true);

        BuildKingdomSupply();
        Refresh(NetworkGameState.State);
    }

    private void OnDestroy()
    {
        NetworkGameState.StateChanged -= Refresh;
    }

    private void Refresh(GameStateSnapshot state)
    {
        ResolveHandRoot();
        RefreshPlayerPills(state);

        if (!_kingdomBuilt)
            BuildKingdomSupply();

        if (state == null || state.Players == null || state.Players.Count == 0)
        {
            // Do not destroy an already rendered hand during a transient Photon hydrate.
            // The Game scene will disappear naturally when the player actually leaves the room.
            if (_turnText != null) _turnText.text = "En attente de la partie";
            if (_boardTitle != null) _boardTitle.text = "PLATEAU";
            if (_phaseText != null) _phaseText.text = "PHASE  —";
            if (_actionsText != null) _actionsText.text = "Actions  —";
            if (_buysText != null) _buysText.text = "Achats  —";
            if (_coinsText != null) _coinsText.text = "Pièces  —";
            if (_deckText != null) _deckText.text = "Pioche\n—";
            if (_discardText != null) _discardText.text = "Défausse\n—";
            if (_handCountText != null) _handCountText.text = "Main  —";
            if (_statusText != null) _statusText.text = "Synchronisation du GameState…";
            if (_nextPhaseButton != null) _nextPhaseButton.interactable = false;
            return;
        }

        PlayerStateSnapshot activePlayer = state.Players.Find(p => p != null && p.PlayerId == state.ActivePlayerId);
        PlayerStateSnapshot localPlayer = ResolveLocalPlayer(state);

        if (_turnText != null)
        {
            string activeName = activePlayer != null && !string.IsNullOrEmpty(activePlayer.NickName) ? activePlayer.NickName : "Joueur";
            _turnText.text = "TOUR " + state.TurnNumber + "  •  " + activeName;
        }

        if (_boardTitle != null)
            _boardTitle.text = activePlayer != null ? "PLATEAU — " + activePlayer.NickName : "PLATEAU";

        if (_phaseText != null) _phaseText.text = "PHASE  " + PhaseLabel(state.Phase);

        PlayerStateSnapshot counters = localPlayer ?? activePlayer;
        if (counters != null)
        {
            if (_actionsText != null) _actionsText.text = "Actions  " + counters.Actions;
            if (_buysText != null) _buysText.text = "Achats  " + counters.Buys;
            if (_coinsText != null) _coinsText.text = "Pièces  " + counters.Coins;
            if (_deckText != null) _deckText.text = "PIOCHE\n" + SafeCount(counters.Deck);
            if (_discardText != null) _discardText.text = "DÉFAUSSE\n" + SafeCount(counters.Discard);
            if (_handCountText != null) _handCountText.text = "Main  " + SafeCount(counters.Hand);
        }

        RefreshLocalHand(state, localPlayer);

        bool localTurn = state.ActivePlayerId == NetworkGameState.LocalPlayerId;
        if (_nextPhaseButton != null)
            _nextPhaseButton.interactable = localTurn && state.IsStarted && !state.IsPaused;
        if (_nextPhaseButtonText != null)
            _nextPhaseButtonText.text = NextPhaseLabel(state.Phase, localTurn);

        if (_statusText != null)
        {
            if (state.IsPaused)
                _statusText.text = state.PauseReason;
            else if (localTurn)
                _statusText.text = "À vous de jouer";
            else
                _statusText.text = "En attente du joueur actif";
        }

        if (_journalText != null && activePlayer != null)
            _journalText.text = "Tour " + state.TurnNumber + "\n\n" + activePlayer.NickName + " — phase " + PhaseLabel(state.Phase) + ".";
    }

    private PlayerStateSnapshot ResolveLocalPlayer(GameStateSnapshot state)
    {
        if (state == null || state.Players == null)
            return null;

        string localId = NetworkGameState.LocalPlayerId;
        PlayerStateSnapshot localPlayer = state.Players.Find(p => p != null && p.PlayerId == localId);
        if (localPlayer != null)
            return localPlayer;

        // UserId can briefly be unavailable during scene startup. ActorNumber is a safe
        // local fallback and avoids hiding the hand during that short Photon window.
        if (PhotonNetwork.LocalPlayer != null)
        {
            int actorNumber = PhotonNetwork.LocalPlayer.ActorNumber;
            localPlayer = state.Players.Find(p => p != null && p.ActorNumber == actorNumber);
            if (localPlayer != null)
                return localPlayer;
        }

        // Explicit solo-test fallback. With one player there is no privacy ambiguity.
        return state.Players.Count == 1 ? state.Players[0] : null;
    }

    private void RefreshPlayerPills(GameStateSnapshot state)
    {
        Clear(_playerPills);
        if (_playersRoot == null || state == null || state.Players == null)
            return;

        foreach (PlayerStateSnapshot player in state.Players)
        {
            if (player == null)
                continue;

            bool active = player.PlayerId == state.ActivePlayerId;
            bool local = player.PlayerId == NetworkGameState.LocalPlayerId;
            GameObject pill = new GameObject("Player_" + player.ActorNumber, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            pill.transform.SetParent(_playersRoot, false);

            Image background = pill.GetComponent<Image>();
            background.color = active ? Active : local ? Local : Panel;

            LayoutElement layout = pill.GetComponent<LayoutElement>();
            layout.preferredWidth = 180f;
            layout.minWidth = 150f;

            GameObject labelObject = new GameObject("Name", typeof(RectTransform), typeof(Text));
            labelObject.transform.SetParent(pill.transform, false);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            Stretch(labelRect, 8f);

            Text label = labelObject.GetComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 18;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.raycastTarget = false;
            label.text = player.NickName + (active ? "  ★" : string.Empty) + (!player.IsConnected ? "  • hors ligne" : string.Empty);

            _playerPills.Add(pill);
        }
    }

    /// <summary>
    /// Keeps the same card GameObjects alive while the authoritative hand contents are
    /// unchanged. This prevents Photon state refreshes from visually deleting/recreating
    /// the hand and also preserves the player's local drag ordering.
    /// </summary>
    private void RefreshLocalHand(GameStateSnapshot state, PlayerStateSnapshot localPlayer)
    {
        ResolveHandRoot();
        if (_handRoot == null || localPlayer == null)
            return;

        SynchroniseLocalHandOrder(localPlayer.Hand);

        if (RenderedHandMatchesLocalOrder())
            return;

        RebuildLocalHand(state);
    }

    /// <summary>
    /// The game UI prefab is deliberately editable and may exist locally in an older
    /// version. Never rely exclusively on a serialized reference for the hand: recover
    /// LocalHand/Cards by hierarchy name, and create the Cards container if necessary.
    /// </summary>
    private void ResolveHandRoot()
    {
        if (_handRoot != null)
        {
            _handRoot.gameObject.SetActive(true);
            return;
        }

        Transform localHand = FindDeepChild(transform, "LocalHand");
        if (localHand == null)
        {
            // Last-resort runtime fallback for an old local GameScreen prefab.
            GameObject localHandObject = new GameObject("LocalHand", typeof(RectTransform), typeof(Image));
            localHandObject.transform.SetParent(transform, false);
            RectTransform localHandRect = localHandObject.GetComponent<RectTransform>();
            SetAnchors(localHandRect, new Vector2(0.015f, 0.015f), new Vector2(0.985f, 0.277f));
            localHandObject.GetComponent<Image>().color = new Color(0.115f, 0.108f, 0.095f, 0.98f);

            GameObject headerObject = new GameObject("Header", typeof(RectTransform), typeof(Text));
            headerObject.transform.SetParent(localHandObject.transform, false);
            RectTransform headerRect = headerObject.GetComponent<RectTransform>();
            SetAnchors(headerRect, new Vector2(0.018f, 0.82f), new Vector2(0.982f, 0.98f));
            Text header = headerObject.GetComponent<Text>();
            header.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            header.text = "VOTRE MAIN";
            header.fontSize = 18;
            header.fontStyle = FontStyle.Bold;
            header.alignment = TextAnchor.MiddleLeft;
            header.color = Color.white;
            header.raycastTarget = false;

            localHand = localHandObject.transform;
            Debug.LogWarning("GameScreen had no LocalHand object. Created a runtime fallback hand panel.");
        }

        Transform cards = FindDirectChild(localHand, "Cards");
        if (cards == null)
        {
            GameObject cardsObject = new GameObject("Cards", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            cardsObject.transform.SetParent(localHand, false);
            RectTransform cardsRect = cardsObject.GetComponent<RectTransform>();
            SetAnchors(cardsRect, new Vector2(0.018f, 0.055f), new Vector2(0.982f, 0.81f));

            HorizontalLayoutGroup layout = cardsObject.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            cards = cardsObject.transform;
            Debug.LogWarning("GameScreen LocalHand had no Cards container. Created it at runtime.");
        }

        _handRoot = cards as RectTransform;
        if (_handRoot != null)
        {
            _handRoot.gameObject.SetActive(true);
            _handRootFailureLogged = false;
            return;
        }

        if (!_handRootFailureLogged)
        {
            _handRootFailureLogged = true;
            Debug.LogError("Could not resolve GameScreen LocalHand/Cards RectTransform.");
        }
    }

    private void SynchroniseLocalHandOrder(List<int> authoritativeHand)
    {
        if (authoritativeHand == null)
            authoritativeHand = new List<int>();

        for (int i = _localHandOrder.Count - 1; i >= 0; i--)
        {
            if (!authoritativeHand.Contains(_localHandOrder[i]))
                _localHandOrder.RemoveAt(i);
        }

        foreach (int instanceId in authoritativeHand)
        {
            if (!_localHandOrder.Contains(instanceId))
                _localHandOrder.Add(instanceId);
        }
    }

    private bool RenderedHandMatchesLocalOrder()
    {
        if (_localHandOrder.Count == 0)
        {
            return _handCards.Count == 1 &&
                   _handCards[0] != null &&
                   _handCards[0].name == "HandMessage";
        }

        if (_handCards.Count != _localHandOrder.Count || _renderedHandIds.Count != _localHandOrder.Count)
            return false;

        for (int i = 0; i < _localHandOrder.Count; i++)
        {
            if (_handCards[i] == null || _renderedHandIds[i] != _localHandOrder[i])
                return false;
        }

        return true;
    }

    private void RebuildLocalHand(GameStateSnapshot state)
    {
        Clear(_handCards);
        _renderedHandIds.Clear();

        if (_localHandOrder.Count == 0)
        {
            CreateHandMessage("Main vide");
            return;
        }

        foreach (int instanceId in _localHandOrder)
        {
            CardInstance instance = NetworkGameState.FindCardInstance(state, instanceId);
            if (instance == null || string.IsNullOrEmpty(instance.DefinitionId))
            {
                Debug.LogWarning("Hand contains unknown card instance #" + instanceId + ".");
                continue;
            }

            ExtensionPackageData extension;
            ExtensionCardData definition;
            if (!RoomGameSetup.TryResolveCard(instance.DefinitionId, out extension, out definition))
            {
                Debug.LogWarning("Could not resolve hand card definition: " + instance.DefinitionId);
                continue;
            }

            Sprite sprite = ExtensionVisualLoader.LoadCardArtwork(extension, definition);
            GameObject cardObject = new GameObject(
                "Hand_" + instanceId + "_" + definition.id,
                typeof(RectTransform),
                typeof(Image),
                typeof(LayoutElement),
                typeof(CardPointerInteraction));
            cardObject.transform.SetParent(_handRoot, false);

            Image image = cardObject.GetComponent<Image>();
            image.sprite = sprite;
            image.color = sprite != null ? Color.white : new Color(0.16f, 0.08f, 0.08f, 1f);
            image.preserveAspect = true;
            image.raycastTarget = true;

            LayoutElement layout = cardObject.GetComponent<LayoutElement>();
            layout.preferredWidth = 130f;
            layout.minWidth = 130f;
            layout.preferredHeight = 200f;
            layout.minHeight = 200f;
            layout.flexibleWidth = 0f;
            layout.flexibleHeight = 0f;

            // Hand convention: right click = inspect. Left click is intentionally left free
            // for contextual play later; holding left and moving is handled by HandCardMotion.
            CardPointerInteraction pointer = cardObject.GetComponent<CardPointerInteraction>();
            pointer.InspectOnLongPress = false;
            Sprite capturedSprite = sprite;
            if (capturedSprite != null)
                pointer.InspectRequested += () => ShowZoom(capturedSprite);

            HandCardMotion motion = cardObject.AddComponent<HandCardMotion>();
            motion.BindInstance(instanceId, HandleHandOrderChanged);

            _handCards.Add(cardObject);
            _renderedHandIds.Add(instanceId);
        }

        if (_handCards.Count == 0)
            CreateHandMessage("Aucune carte affichable");

        Canvas.ForceUpdateCanvases();
        Debug.Log("Local hand rendered: " + _renderedHandIds.Count + " card(s) in " + _handRoot.name + ".");
    }

    private void HandleHandOrderChanged()
    {
        if (_handRoot == null)
            return;

        List<int> order = new List<int>();
        for (int i = 0; i < _handRoot.childCount; i++)
        {
            HandCardMotion motion = _handRoot.GetChild(i).GetComponent<HandCardMotion>();
            if (motion != null && motion.InstanceId > 0)
                order.Add(motion.InstanceId);
        }

        if (order.Count == 0)
            return;

        _localHandOrder.Clear();
        _localHandOrder.AddRange(order);
        _renderedHandIds.Clear();
        _renderedHandIds.AddRange(order);

        _handCards.Sort((left, right) =>
        {
            if (left == null || right == null)
                return 0;
            return left.transform.GetSiblingIndex().CompareTo(right.transform.GetSiblingIndex());
        });
    }

    private void CreateHandMessage(string message)
    {
        GameObject messageObject = new GameObject("HandMessage", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
        messageObject.transform.SetParent(_handRoot, false);

        Text text = messageObject.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 18;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.raycastTarget = false;
        text.text = message;

        LayoutElement layout = messageObject.GetComponent<LayoutElement>();
        layout.preferredWidth = 260f;
        layout.minWidth = 260f;
        _handCards.Add(messageObject);
    }

    private void BuildKingdomSupply()
    {
        Clear(_kingdomCards);
        _kingdomBuilt = false;
        if (_kingdomSupplyRoot == null)
            return;

        GameSetupConfig setup = RoomGameSetup.ReadCurrent();
        if (setup == null || setup.kingdomCardIds == null || setup.kingdomCardIds.Count == 0)
            return;

        foreach (string cardRef in setup.kingdomCardIds)
        {
            ExtensionPackageData extension;
            ExtensionCardData card;
            if (!RoomGameSetup.TryResolveCard(cardRef, out extension, out card))
                continue;

            Sprite sprite = ExtensionVisualLoader.LoadCardArtwork(extension, card);
            GameObject cardObject = new GameObject(
                "Supply_" + card.id,
                typeof(RectTransform),
                typeof(Image),
                typeof(Button),
                typeof(CardPointerInteraction));
            cardObject.transform.SetParent(_kingdomSupplyRoot, false);

            Image image = cardObject.GetComponent<Image>();
            image.sprite = sprite;
            image.color = sprite != null ? Color.white : new Color(0.16f, 0.08f, 0.08f, 1f);
            image.preserveAspect = true;
            image.raycastTarget = true;

            Button button = cardObject.GetComponent<Button>();
            button.targetGraphic = image;
            Sprite captured = sprite;
            if (captured != null)
                button.onClick.AddListener(() => ShowZoom(captured));

            CardPointerInteraction pointer = cardObject.GetComponent<CardPointerInteraction>();
            if (captured != null)
                pointer.InspectRequested += () => ShowZoom(captured);

            _kingdomCards.Add(cardObject);
        }

        _kingdomBuilt = _kingdomCards.Count > 0;
    }

    private void ShowZoom(Sprite sprite)
    {
        if (_zoomOverlay == null || _zoomImage == null || sprite == null)
            return;

        _zoomImage.sprite = sprite;
        _zoomImage.preserveAspect = true;
        _zoomOverlay.SetActive(true);
        _zoomOverlay.transform.SetAsLastSibling();
    }

    private void HideZoom()
    {
        if (_zoomOverlay != null)
            _zoomOverlay.SetActive(false);
    }

    private void RequestNextPhase()
    {
        if (PlayersTurnsHandler.Instance != null)
            PlayersTurnsHandler.Instance.AdvancePhase();
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

    private static string PhaseLabel(string phase)
    {
        switch (phase)
        {
            case NetworkGameState.ActionPhase: return "ACTION";
            case NetworkGameState.BuyPhase: return "ACHAT";
            case NetworkGameState.CleanupPhase: return "AJUSTEMENT";
            default: return string.IsNullOrEmpty(phase) ? "—" : phase.ToUpperInvariant();
        }
    }

    private static string NextPhaseLabel(string phase, bool localTurn)
    {
        if (!localTurn)
            return "EN ATTENTE";

        switch (phase)
        {
            case NetworkGameState.ActionPhase: return "PASSER À L’ACHAT";
            case NetworkGameState.BuyPhase: return "PASSER À L’AJUSTEMENT";
            case NetworkGameState.CleanupPhase: return "TERMINER LE TOUR";
            default: return "PHASE SUIVANTE";
        }
    }

    private static int SafeCount<T>(List<T> list)
    {
        return list != null ? list.Count : 0;
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

    private static void Stretch(RectTransform rect, float inset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }
}
