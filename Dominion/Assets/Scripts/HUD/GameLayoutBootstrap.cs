using System;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Builds the first functional Dominion game layout at runtime.
/// This is intentionally a prototype shell: it validates navigation and information hierarchy
/// without coupling the final visual design to the current scene hierarchy.
/// </summary>
public static class GameLayoutBootstrap
{
    private const string GameSceneName = "Game";
    private const string RootName = "DominionGameUI";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!string.Equals(scene.name, GameSceneName, StringComparison.Ordinal))
            return;

        if (GameObject.Find(RootName) != null)
            return;

        GameObject root = new GameObject(RootName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(GameLayoutController));
        SceneManager.MoveGameObjectToScene(root, scene);

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 1000;

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        EnsureEventSystem(scene);
    }

    private static void EnsureEventSystem(Scene scene)
    {
        if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() != null)
            return;

        GameObject eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        SceneManager.MoveGameObjectToScene(eventSystem, scene);
    }
}

/// <summary>
/// Temporary functional implementation of the global game screen.
/// The board being viewed is local UI state and is never synchronized over Photon.
/// </summary>
public sealed class GameLayoutController : MonoBehaviour
{
    private readonly List<GameObject> _dynamicPlayerTabs = new List<GameObject>();
    private readonly List<GameObject> _dynamicInPlayCards = new List<GameObject>();
    private readonly List<GameObject> _dynamicHandCards = new List<GameObject>();

    private RectTransform _playerTabsRoot;
    private RectTransform _inPlayRoot;
    private RectTransform _handRoot;
    private Text _viewedPlayerTitle;
    private Text _actionsText;
    private Text _buysText;
    private Text _coinsText;
    private Text _handCountText;
    private Text _deckText;
    private Text _discardText;
    private Text _specialZonesText;
    private Text _statusText;
    private Text _logText;
    private GameObject _overlay;
    private Text _overlayTitle;
    private Text _overlayContent;

    private string _viewedPlayerId;

    private static readonly Color Background = new Color(0.075f, 0.075f, 0.075f, 1f);
    private static readonly Color Panel = new Color(0.13f, 0.13f, 0.13f, 1f);
    private static readonly Color PanelAlt = new Color(0.17f, 0.17f, 0.17f, 1f);
    private static readonly Color ButtonColor = new Color(0.23f, 0.23f, 0.23f, 1f);
    private static readonly Color ActiveColor = new Color(0.38f, 0.32f, 0.18f, 1f);
    private static readonly Color ViewedColor = new Color(0.25f, 0.34f, 0.38f, 1f);

    private void Awake()
    {
        BuildLayout();
        NetworkGameState.StateChanged += OnStateChanged;
        NetworkGameState.HydrateFromRoom();
        Refresh(NetworkGameState.State);
    }

    private void OnDestroy()
    {
        NetworkGameState.StateChanged -= OnStateChanged;
    }

    private void OnStateChanged(GameStateSnapshot state)
    {
        Refresh(state);
    }

    private void BuildLayout()
    {
        RectTransform root = GetComponent<RectTransform>();
        Stretch(root);
        AddImage(gameObject, Background);

        RectTransform top = CreatePanel("TopBar", root, new Vector2(0f, 0.92f), new Vector2(1f, 1f), PanelAlt);
        RectTransform body = CreatePanel("Body", root, new Vector2(0f, 0.22f), new Vector2(1f, 0.92f), Background);
        RectTransform hand = CreatePanel("LocalHand", root, new Vector2(0f, 0f), new Vector2(1f, 0.22f), PanelAlt);

        BuildTopBar(top);
        BuildBody(body);
        BuildHand(hand);
        BuildOverlay(root);
    }

    private void BuildTopBar(RectTransform top)
    {
        _playerTabsRoot = CreatePanel("PlayerTabs", top, new Vector2(0f, 0f), new Vector2(0.55f, 1f), PanelAlt);
        HorizontalLayoutGroup tabsLayout = _playerTabsRoot.gameObject.AddComponent<HorizontalLayoutGroup>();
        tabsLayout.padding = new RectOffset(12, 8, 10, 10);
        tabsLayout.spacing = 8f;
        tabsLayout.childControlWidth = false;
        tabsLayout.childControlHeight = true;
        tabsLayout.childForceExpandWidth = false;

        RectTransform globals = CreatePanel("GlobalZones", top, new Vector2(0.55f, 0f), new Vector2(1f, 1f), PanelAlt);
        HorizontalLayoutGroup globalsLayout = globals.gameObject.AddComponent<HorizontalLayoutGroup>();
        globalsLayout.padding = new RectOffset(8, 12, 10, 10);
        globalsLayout.spacing = 8f;
        globalsLayout.childAlignment = TextAnchor.MiddleRight;
        globalsLayout.childControlWidth = false;
        globalsLayout.childControlHeight = true;
        globalsLayout.childForceExpandWidth = false;

        CreateButton("Réserve", globals, 125f, () => ShowGlobalOverlay("Réserve", "Les piles de la Réserve seront affichées ici."));
        CreateButton("Écartées", globals, 125f, () => ShowGlobalOverlay("Cartes écartées", "Zone globale commune à tous les joueurs."));
        CreateButton("Exilées", globals, 125f, () => ShowGlobalOverlay("Cartes exilées", "Zone globale commune à tous les joueurs."));
    }

    private void BuildBody(RectTransform body)
    {
        RectTransform left = CreatePanel("JournalChat", body, new Vector2(0f, 0f), new Vector2(0.22f, 1f), Panel);
        RectTransform center = CreatePanel("ViewedPlayerBoard", body, new Vector2(0.22f, 0f), new Vector2(0.80f, 1f), Background);
        RectTransform right = CreatePanel("ViewedPlayerResources", body, new Vector2(0.80f, 0f), new Vector2(1f, 1f), Panel);

        BuildJournal(left);
        BuildViewedBoard(center);
        BuildResources(right);
    }

    private void BuildJournal(RectTransform left)
    {
        Text header = CreateText("Header", left, "JOURNAL  |  CHAT", 24, TextAnchor.MiddleCenter);
        SetAnchors(header.rectTransform, new Vector2(0f, 0.91f), new Vector2(1f, 1f), 10f);

        _logText = CreateText("Log", left,
            "Journal de partie\n\n• Les cartes jouées et leurs effets apparaîtront ici.\n• Le chat joueur utilisera le même panneau.",
            18, TextAnchor.UpperLeft);
        SetAnchors(_logText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0.91f), 18f);
    }

    private void BuildViewedBoard(RectTransform center)
    {
        _viewedPlayerTitle = CreateText("ViewedPlayerTitle", center, "PLATEAU", 30, TextAnchor.MiddleCenter);
        SetAnchors(_viewedPlayerTitle.rectTransform, new Vector2(0f, 0.88f), new Vector2(1f, 1f), 10f);

        RectTransform inPlayPanel = CreatePanel("InPlayArea", center, new Vector2(0.025f, 0.31f), new Vector2(0.975f, 0.87f), Panel);
        Text inPlayTitle = CreateText("Title", inPlayPanel, "CARTES EN JEU", 21, TextAnchor.MiddleLeft);
        SetAnchors(inPlayTitle.rectTransform, new Vector2(0f, 0.88f), new Vector2(1f, 1f), 12f);

        _inPlayRoot = CreatePanel("Cards", inPlayPanel, new Vector2(0f, 0f), new Vector2(1f, 0.88f), Panel);
        HorizontalLayoutGroup inPlayLayout = _inPlayRoot.gameObject.AddComponent<HorizontalLayoutGroup>();
        inPlayLayout.padding = new RectOffset(14, 14, 12, 12);
        inPlayLayout.spacing = 10f;
        inPlayLayout.childControlHeight = true;
        inPlayLayout.childControlWidth = false;
        inPlayLayout.childForceExpandWidth = false;

        RectTransform deck = CreatePanel("Deck", center, new Vector2(0.025f, 0.035f), new Vector2(0.26f, 0.275f), PanelAlt);
        _deckText = CreateText("DeckText", deck, "PIOCHE\n0 carte", 22, TextAnchor.MiddleCenter);
        Stretch(_deckText.rectTransform, 8f);

        RectTransform discard = CreatePanel("Discard", center, new Vector2(0.285f, 0.035f), new Vector2(0.52f, 0.275f), PanelAlt);
        _discardText = CreateText("DiscardText", discard, "DÉFAUSSE\n0 carte\n\nCarte du dessus uniquement", 20, TextAnchor.MiddleCenter);
        Stretch(_discardText.rectTransform, 8f);

        RectTransform special = CreatePanel("SpecialZones", center, new Vector2(0.545f, 0.035f), new Vector2(0.975f, 0.275f), PanelAlt);
        _specialZonesText = CreateText("SpecialZonesText", special, "PILES / ZONES SPÉCIALES\n\nAucune zone active", 20, TextAnchor.MiddleCenter);
        Stretch(_specialZonesText.rectTransform, 8f);
    }

    private void BuildResources(RectTransform right)
    {
        Text title = CreateText("Title", right, "JOUEUR OBSERVÉ", 22, TextAnchor.MiddleCenter);
        SetAnchors(title.rectTransform, new Vector2(0f, 0.88f), new Vector2(1f, 1f), 10f);

        _actionsText = ResourceLine(right, "Actions", 0.72f);
        _buysText = ResourceLine(right, "Achats", 0.60f);
        _coinsText = ResourceLine(right, "Pièces", 0.48f);
        _handCountText = ResourceLine(right, "Main", 0.36f);

        _statusText = CreateText("Status", right, string.Empty, 18, TextAnchor.UpperCenter);
        SetAnchors(_statusText.rectTransform, new Vector2(0.05f, 0.04f), new Vector2(0.95f, 0.28f), 8f);
    }

    private Text ResourceLine(RectTransform parent, string label, float y)
    {
        Text text = CreateText(label, parent, label + " : 0", 26, TextAnchor.MiddleLeft);
        SetAnchors(text.rectTransform, new Vector2(0.12f, y), new Vector2(0.90f, y + 0.10f), 0f);
        return text;
    }

    private void BuildHand(RectTransform hand)
    {
        Text title = CreateText("Title", hand, "TA MAIN", 23, TextAnchor.MiddleCenter);
        SetAnchors(title.rectTransform, new Vector2(0f, 0.80f), new Vector2(1f, 1f), 6f);

        _handRoot = CreatePanel("Cards", hand, new Vector2(0f, 0f), new Vector2(1f, 0.80f), PanelAlt);
        HorizontalLayoutGroup layout = _handRoot.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(24, 24, 8, 12);
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = false;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
    }

    private void BuildOverlay(RectTransform root)
    {
        _overlay = CreatePanel("Overlay", root, Vector2.zero, Vector2.one, new Color(0f, 0f, 0f, 0.84f)).gameObject;
        _overlay.transform.SetAsLastSibling();

        RectTransform card = CreatePanel("Window", _overlay.GetComponent<RectTransform>(), new Vector2(0.25f, 0.16f), new Vector2(0.75f, 0.84f), PanelAlt);
        _overlayTitle = CreateText("Title", card, "Inspection", 34, TextAnchor.MiddleCenter);
        SetAnchors(_overlayTitle.rectTransform, new Vector2(0f, 0.84f), new Vector2(1f, 1f), 14f);

        _overlayContent = CreateText("Content", card, string.Empty, 23, TextAnchor.UpperCenter);
        SetAnchors(_overlayContent.rectTransform, new Vector2(0.05f, 0.18f), new Vector2(0.95f, 0.82f), 12f);

        Button close = CreateButton("Fermer", card, 160f, HideOverlay);
        RectTransform closeRect = close.GetComponent<RectTransform>();
        closeRect.anchorMin = new Vector2(0.35f, 0.04f);
        closeRect.anchorMax = new Vector2(0.65f, 0.14f);
        closeRect.offsetMin = Vector2.zero;
        closeRect.offsetMax = Vector2.zero;

        _overlay.SetActive(false);
    }

    private void Refresh(GameStateSnapshot state)
    {
        RebuildPlayerTabs(state);

        if (state == null || state.Players == null || state.Players.Count == 0)
        {
            ShowEmptyState();
            return;
        }

        if (string.IsNullOrEmpty(_viewedPlayerId) || state.Players.Find(p => p.PlayerId == _viewedPlayerId) == null)
        {
            string localId = NetworkGameState.LocalPlayerId;
            PlayerStateSnapshot local = state.Players.Find(p => p.PlayerId == localId);
            _viewedPlayerId = local != null ? local.PlayerId : state.Players[0].PlayerId;
        }

        PlayerStateSnapshot viewed = state.Players.Find(p => p.PlayerId == _viewedPlayerId);
        PlayerStateSnapshot localPlayer = state.Players.Find(p => p.PlayerId == NetworkGameState.LocalPlayerId);

        if (viewed != null)
            RefreshViewedPlayer(state, viewed);

        RefreshLocalHand(localPlayer);
        RebuildPlayerTabs(state);
    }

    private void RebuildPlayerTabs(GameStateSnapshot state)
    {
        ClearObjects(_dynamicPlayerTabs);

        if (state == null || state.Players == null)
            return;

        foreach (PlayerStateSnapshot player in state.Players)
        {
            string playerId = player.PlayerId;
            bool isActive = playerId == state.ActivePlayerId;
            bool isViewed = playerId == _viewedPlayerId;
            bool isLocal = playerId == NetworkGameState.LocalPlayerId;

            string label = player.NickName;
            if (isActive)
                label += " ★";
            if (isLocal)
                label += " (vous)";
            if (!player.IsConnected)
                label += " [déconnecté]";

            Button tab = CreateButton(label, _playerTabsRoot, 190f, () => SelectPlayer(playerId));
            tab.image.color = isViewed ? ViewedColor : isActive ? ActiveColor : ButtonColor;
            _dynamicPlayerTabs.Add(tab.gameObject);
        }
    }

    private void SelectPlayer(string playerId)
    {
        _viewedPlayerId = playerId;
        Refresh(NetworkGameState.State);
    }

    private void RefreshViewedPlayer(GameStateSnapshot state, PlayerStateSnapshot player)
    {
        bool active = player.PlayerId == state.ActivePlayerId;
        _viewedPlayerTitle.text = player.NickName + (active ? "  ★ TOUR ACTUEL" : string.Empty);
        _actionsText.text = "Actions : " + player.Actions;
        _buysText.text = "Achats : " + player.Buys;
        _coinsText.text = "Pièces : " + player.Coins;
        _handCountText.text = "Main : " + SafeCount(player.Hand) + " carte" + (SafeCount(player.Hand) > 1 ? "s" : string.Empty);
        _deckText.text = "PIOCHE\n" + SafeCount(player.Deck) + " carte" + (SafeCount(player.Deck) > 1 ? "s" : string.Empty);

        int discardCount = SafeCount(player.Discard);
        string topDiscard = discardCount > 0 ? "Carte du dessus : #" + player.Discard[discardCount - 1] : "Vide";
        _discardText.text = "DÉFAUSSE\n" + discardCount + " carte" + (discardCount > 1 ? "s" : string.Empty) + "\n\n" + topDiscard;

        _specialZonesText.text = "PILES / ZONES SPÉCIALES\n\nAucune zone active pour le moment";
        _statusText.text = state.IsPaused ? "PARTIE EN PAUSE\n" + state.PauseReason : (player.IsConnected ? "Connecté" : "Déconnecté");

        RebuildInPlay(player);
    }

    private void RebuildInPlay(PlayerStateSnapshot player)
    {
        ClearObjects(_dynamicInPlayCards);

        if (player.InPlay == null || player.InPlay.Count == 0)
        {
            Text empty = CreateText("Empty", _inPlayRoot, "Aucune carte jouée", 20, TextAnchor.MiddleCenter);
            LayoutElement layout = empty.gameObject.AddComponent<LayoutElement>();
            layout.preferredWidth = 220f;
            _dynamicInPlayCards.Add(empty.gameObject);
            return;
        }

        foreach (int cardId in player.InPlay)
        {
            int capturedCardId = cardId;
            Button card = CreateCardPlaceholder("Carte #" + cardId, _inPlayRoot, () => InspectCard("Carte en jeu", capturedCardId));
            _dynamicInPlayCards.Add(card.gameObject);
        }
    }

    private void RefreshLocalHand(PlayerStateSnapshot player)
    {
        ClearObjects(_dynamicHandCards);

        if (player == null || player.Hand == null || player.Hand.Count == 0)
        {
            Text empty = CreateText("Empty", _handRoot, "Main vide / cartes pas encore initialisées", 20, TextAnchor.MiddleCenter);
            LayoutElement layout = empty.gameObject.AddComponent<LayoutElement>();
            layout.preferredWidth = 360f;
            _dynamicHandCards.Add(empty.gameObject);
            return;
        }

        foreach (int cardId in player.Hand)
        {
            int capturedCardId = cardId;
            Button card = CreateCardPlaceholder("Carte #" + cardId, _handRoot, () => InspectCard("Carte de votre main", capturedCardId));
            _dynamicHandCards.Add(card.gameObject);
        }
    }

    private Button CreateCardPlaceholder(string label, RectTransform parent, Action inspect)
    {
        Button card = CreateButton(label, parent, 112f, null);
        LayoutElement layout = card.GetComponent<LayoutElement>();
        layout.preferredHeight = 150f;

        CardPointerInteraction interaction = card.gameObject.AddComponent<CardPointerInteraction>();
        interaction.LongPressSeconds = 0.45f;
        interaction.InspectRequested += inspect;
        return card;
    }

    private void InspectCard(string context, int cardId)
    {
        _overlayTitle.text = "Inspection — Carte #" + cardId;
        _overlayContent.text = context + "\n\nLe rendu de la carte complète sera branché ici dès que CardDefinition/CardView seront disponibles.\n\nClic droit ou clic long : inspection.";
        _overlay.SetActive(true);
    }

    private void ShowGlobalOverlay(string title, string content)
    {
        _overlayTitle.text = title;
        _overlayContent.text = content;
        _overlay.SetActive(true);
    }

    private void HideOverlay()
    {
        _overlay.SetActive(false);
    }

    private void ShowEmptyState()
    {
        _viewedPlayerTitle.text = "PLATEAU — en attente du GameState";
        _actionsText.text = "Actions : -";
        _buysText.text = "Achats : -";
        _coinsText.text = "Pièces : -";
        _handCountText.text = "Main : -";
        _deckText.text = "PIOCHE\n-";
        _discardText.text = "DÉFAUSSE\n-";
        _specialZonesText.text = "PILES / ZONES SPÉCIALES\n-";
        _statusText.text = "En attente de l'état réseau";
    }

    private static int SafeCount<T>(List<T> list)
    {
        return list != null ? list.Count : 0;
    }

    private static RectTransform CreatePanel(string name, RectTransform parent, Vector2 anchorMin, Vector2 anchorMax, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        go.GetComponent<Image>().color = color;
        return rect;
    }

    private static Text CreateText(string name, RectTransform parent, string value, int size, TextAnchor alignment)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        Text text = go.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = value;
        text.fontSize = size;
        text.alignment = alignment;
        text.color = Color.white;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }

    private static Button CreateButton(string label, RectTransform parent, float preferredWidth, Action onClick)
    {
        GameObject go = new GameObject(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        Image image = go.GetComponent<Image>();
        image.color = ButtonColor;

        LayoutElement layout = go.GetComponent<LayoutElement>();
        layout.preferredWidth = preferredWidth;
        layout.minWidth = preferredWidth;

        Button button = go.GetComponent<Button>();
        if (onClick != null)
            button.onClick.AddListener(() => onClick());

        Text text = CreateText("Label", rect, label, 18, TextAnchor.MiddleCenter);
        Stretch(text.rectTransform, 6f);
        return button;
    }

    private static void AddImage(GameObject go, Color color)
    {
        Image image = go.GetComponent<Image>();
        if (image == null)
            image = go.AddComponent<Image>();
        image.color = color;
    }

    private static void Stretch(RectTransform rect, float inset = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }

    private static void SetAnchors(RectTransform rect, Vector2 min, Vector2 max, float inset)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }

    private static void ClearObjects(List<GameObject> objects)
    {
        foreach (GameObject go in objects)
        {
            if (go != null)
                UnityEngine.Object.Destroy(go);
        }
        objects.Clear();
    }
}