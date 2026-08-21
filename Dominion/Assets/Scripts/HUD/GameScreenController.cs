using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Binds the editable GameScreen prefab to replicated state.
/// The prefab owns the layout; this controller only fills dynamic content and local navigation.
/// </summary>
public sealed class GameScreenController : MonoBehaviour
{
    [Header("Top bar")]
    [SerializeField] private RectTransform _playersRoot;
    [SerializeField] private Text _turnText;
    [SerializeField] private Button _reserveButton;
    [SerializeField] private Button _trashedButton;
    [SerializeField] private Button _exiledButton;

    [Header("Observed player board")]
    [SerializeField] private Text _boardTitle;
    [SerializeField] private RectTransform _inPlayRoot;
    [SerializeField] private Text _deckText;
    [SerializeField] private Text _discardText;
    [SerializeField] private Text _specialZonesText;

    [Header("Observed player status")]
    [SerializeField] private Text _phaseText;
    [SerializeField] private Text _actionsText;
    [SerializeField] private Text _buysText;
    [SerializeField] private Text _coinsText;
    [SerializeField] private Text _handCountText;
    [SerializeField] private Text _statusText;
    [SerializeField] private Button _nextPhaseButton;
    [SerializeField] private Text _nextPhaseButtonText;

    [Header("Local hand / journal")]
    [SerializeField] private RectTransform _handRoot;
    [SerializeField] private Text _journalText;

    [Header("Global zones overlay")]
    [SerializeField] private GameObject _globalOverlay;
    [SerializeField] private Text _globalOverlayTitle;
    [SerializeField] private GameObject _reserveContent;
    [SerializeField] private RectTransform _baseSupplyRoot;
    [SerializeField] private RectTransform _kingdomSupplyRoot;
    [SerializeField] private Text _globalPlaceholderText;
    [SerializeField] private Button _globalOverlayCloseButton;

    [Header("Card zoom")]
    [SerializeField] private GameObject _zoomOverlay;
    [SerializeField] private Image _zoomImage;
    [SerializeField] private Button _zoomCloseButton;

    private readonly List<GameObject> _playerPills = new List<GameObject>();
    private readonly List<GameObject> _kingdomCards = new List<GameObject>();
    private readonly List<GameObject> _inPlayCards = new List<GameObject>();
    private string _viewedPlayerId;
    private bool _kingdomBuilt;

    private static readonly Color NormalPlayer = new Color(0.12f, 0.115f, 0.105f, 1f);
    private static readonly Color ActivePlayer = new Color(0.40f, 0.32f, 0.17f, 1f);
    private static readonly Color ViewedPlayer = new Color(0.22f, 0.31f, 0.34f, 1f);

    private void Awake()
    {
        if (_nextPhaseButton != null)
            _nextPhaseButton.onClick.AddListener(RequestNextPhase);
        if (_reserveButton != null)
            _reserveButton.onClick.AddListener(ShowReserve);
        if (_trashedButton != null)
            _trashedButton.onClick.AddListener(() => ShowSimpleGlobalZone("CARTES ÉCARTÉES", "Aucune carte écartée pour le moment."));
        if (_exiledButton != null)
            _exiledButton.onClick.AddListener(() => ShowSimpleGlobalZone("CARTES EXILÉES", "Aucune carte exilée pour le moment."));
        if (_globalOverlayCloseButton != null)
            _globalOverlayCloseButton.onClick.AddListener(HideGlobalOverlay);
        if (_zoomCloseButton != null)
            _zoomCloseButton.onClick.AddListener(HideZoom);

        if (_globalOverlay != null)
            _globalOverlay.SetActive(false);
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
        if (!_kingdomBuilt)
            BuildKingdomSupply();

        if (state == null || state.Players == null || state.Players.Count == 0)
        {
            RefreshPlayerPills(state);
            ShowWaitingState();
            return;
        }

        PlayerStateSnapshot activePlayer = state.Players.Find(p => p != null && p.PlayerId == state.ActivePlayerId);

        if (string.IsNullOrEmpty(_viewedPlayerId) || state.Players.Find(p => p != null && p.PlayerId == _viewedPlayerId) == null)
            _viewedPlayerId = activePlayer != null ? activePlayer.PlayerId : state.Players[0].PlayerId;

        PlayerStateSnapshot viewedPlayer = state.Players.Find(p => p != null && p.PlayerId == _viewedPlayerId);
        PlayerStateSnapshot localPlayer = state.Players.Find(p => p != null && p.PlayerId == NetworkGameState.LocalPlayerId);

        RefreshPlayerPills(state);

        if (_turnText != null)
        {
            string activeName = activePlayer != null && !string.IsNullOrEmpty(activePlayer.NickName) ? activePlayer.NickName : "Joueur";
            _turnText.text = "Tour " + state.TurnNumber + " • " + activeName;
        }

        RefreshObservedPlayer(state, viewedPlayer);
        RefreshLocalTurnControls(state, localPlayer);
        RefreshJournal(state, activePlayer);
    }

    private void RefreshObservedPlayer(GameStateSnapshot state, PlayerStateSnapshot player)
    {
        Clear(_inPlayCards);

        if (player == null)
            return;

        if (_boardTitle != null)
            _boardTitle.text = string.IsNullOrEmpty(player.NickName) ? "JOUEUR" : player.NickName.ToUpperInvariant();
        if (_phaseText != null)
            _phaseText.text = "Phase : " + PhaseLabel(state.Phase);
        if (_actionsText != null)
            _actionsText.text = "Actions : " + player.Actions;
        if (_buysText != null)
            _buysText.text = "Achats : " + player.Buys;
        if (_coinsText != null)
            _coinsText.text = "Pièces : " + player.Coins;
        if (_handCountText != null)
            _handCountText.text = "Main : " + SafeCount(player.Hand) + " carte" + (SafeCount(player.Hand) > 1 ? "s" : string.Empty);
        if (_deckText != null)
            _deckText.text = "PIOCHE\n[" + SafeCount(player.Deck) + "]";
        if (_discardText != null)
            _discardText.text = "DÉFAUSSE\n[" + SafeCount(player.Discard) + "]";
        if (_specialZonesText != null)
            _specialZonesText.text = "ZONES SPÉCIALES\n—";

        if (_statusText != null)
        {
            bool viewedIsActive = player.PlayerId == state.ActivePlayerId;
            _statusText.text = !player.IsConnected
                ? "Joueur déconnecté"
                : viewedIsActive ? "Joueur actif" : "Joueur observé";
        }

        if (_inPlayRoot == null)
            return;

        if (player.InPlay == null || player.InPlay.Count == 0)
        {
            Text empty = RuntimeText("Aucune carte en jeu", _inPlayRoot, 18, TextAnchor.MiddleCenter);
            LayoutElement element = empty.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = 260f;
            _inPlayCards.Add(empty.gameObject);
            return;
        }

        foreach (int instanceId in player.InPlay)
        {
            GameObject placeholder = RuntimePanel("Card_" + instanceId, _inPlayRoot);
            LayoutElement layout = placeholder.AddComponent<LayoutElement>();
            layout.preferredWidth = 110f;
            layout.preferredHeight = 170f;
            Text label = RuntimeText("#" + instanceId, placeholder.transform, 16, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform, 6f);
            _inPlayCards.Add(placeholder);
        }
    }

    private void RefreshLocalTurnControls(GameStateSnapshot state, PlayerStateSnapshot localPlayer)
    {
        bool localTurn = state.ActivePlayerId == NetworkGameState.LocalPlayerId;

        if (_nextPhaseButton != null)
            _nextPhaseButton.interactable = localTurn && state.IsStarted && !state.IsPaused;
        if (_nextPhaseButtonText != null)
            _nextPhaseButtonText.text = NextPhaseLabel(state.Phase, localTurn);

        // The bottom hand always belongs to the local player, independently of the observed board.
        if (_handRoot != null && _handRoot.childCount == 0)
        {
            string handLabel = localPlayer == null || SafeCount(localPlayer.Hand) == 0
                ? "Votre main apparaîtra ici"
                : SafeCount(localPlayer.Hand) + " cartes en main";
            Text placeholder = RuntimeText(handLabel, _handRoot, 18, TextAnchor.MiddleCenter);
            placeholder.gameObject.name = "HandPlaceholder";
        }
    }

    private void RefreshJournal(GameStateSnapshot state, PlayerStateSnapshot activePlayer)
    {
        if (_journalText == null)
            return;

        if (state.IsPaused)
        {
            _journalText.text = state.PauseReason;
            return;
        }

        string activeName = activePlayer != null && !string.IsNullOrEmpty(activePlayer.NickName) ? activePlayer.NickName : "Joueur";
        _journalText.text = "Tour " + state.TurnNumber + "\n\n" + activeName + " — phase " + PhaseLabel(state.Phase) + ".";
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
            bool viewed = player.PlayerId == _viewedPlayerId;
            string capturedPlayerId = player.PlayerId;

            GameObject pill = new GameObject("Player_" + player.ActorNumber, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            pill.transform.SetParent(_playersRoot, false);

            Image background = pill.GetComponent<Image>();
            background.color = viewed ? ViewedPlayer : active ? ActivePlayer : NormalPlayer;

            Button button = pill.GetComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(() => SelectViewedPlayer(capturedPlayerId));

            LayoutElement layout = pill.GetComponent<LayoutElement>();
            layout.preferredWidth = 170f;
            layout.minWidth = 135f;

            Text label = RuntimeText("Name", pill.transform, 17, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform, 7f);
            label.text = player.NickName
                + (active ? "  ★" : string.Empty)
                + (viewed ? "  ◉" : string.Empty)
                + (!player.IsConnected ? "  ×" : string.Empty);

            _playerPills.Add(pill);
        }
    }

    private void SelectViewedPlayer(string playerId)
    {
        if (string.IsNullOrEmpty(playerId))
            return;

        _viewedPlayerId = playerId;
        Refresh(NetworkGameState.State);
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
            GameObject cardObject = new GameObject("Supply_" + card.id, typeof(RectTransform), typeof(Image), typeof(Button));
            cardObject.transform.SetParent(_kingdomSupplyRoot, false);

            Image image = cardObject.GetComponent<Image>();
            image.sprite = sprite;
            image.color = sprite != null ? Color.white : new Color(0.16f, 0.08f, 0.08f, 1f);
            image.preserveAspect = true;
            image.raycastTarget = true;

            Button button = cardObject.GetComponent<Button>();
            button.targetGraphic = image;
            Sprite capturedSprite = sprite;
            if (capturedSprite != null)
                button.onClick.AddListener(() => ShowZoom(capturedSprite));

            _kingdomCards.Add(cardObject);
        }

        _kingdomBuilt = _kingdomCards.Count > 0;
    }

    private void ShowReserve()
    {
        if (_globalOverlay == null)
            return;

        if (_globalOverlayTitle != null)
            _globalOverlayTitle.text = "RÉSERVE";
        if (_reserveContent != null)
            _reserveContent.SetActive(true);
        if (_globalPlaceholderText != null)
            _globalPlaceholderText.gameObject.SetActive(false);

        _globalOverlay.SetActive(true);
        _globalOverlay.transform.SetAsLastSibling();
    }

    private void ShowSimpleGlobalZone(string title, string text)
    {
        if (_globalOverlay == null)
            return;

        if (_globalOverlayTitle != null)
            _globalOverlayTitle.text = title;
        if (_reserveContent != null)
            _reserveContent.SetActive(false);
        if (_globalPlaceholderText != null)
        {
            _globalPlaceholderText.gameObject.SetActive(true);
            _globalPlaceholderText.text = text;
        }

        _globalOverlay.SetActive(true);
        _globalOverlay.transform.SetAsLastSibling();
    }

    private void HideGlobalOverlay()
    {
        if (_globalOverlay != null)
            _globalOverlay.SetActive(false);
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

    private void ShowWaitingState()
    {
        if (_turnText != null) _turnText.text = "En attente";
        if (_boardTitle != null) _boardTitle.text = "JOUEUR";
        if (_phaseText != null) _phaseText.text = "Phase : —";
        if (_actionsText != null) _actionsText.text = "Actions : —";
        if (_buysText != null) _buysText.text = "Achats : —";
        if (_coinsText != null) _coinsText.text = "Pièces : —";
        if (_handCountText != null) _handCountText.text = "Main : —";
        if (_deckText != null) _deckText.text = "PIOCHE\n[—]";
        if (_discardText != null) _discardText.text = "DÉFAUSSE\n[—]";
        if (_specialZonesText != null) _specialZonesText.text = "ZONES SPÉCIALES\n—";
        if (_statusText != null) _statusText.text = "Synchronisation…";
        if (_nextPhaseButton != null) _nextPhaseButton.interactable = false;
    }

    private static string PhaseLabel(string phase)
    {
        switch (phase)
        {
            case NetworkGameState.ActionPhase: return "Action";
            case NetworkGameState.BuyPhase: return "Achat";
            case NetworkGameState.CleanupPhase: return "Ajustement";
            default: return string.IsNullOrEmpty(phase) ? "—" : phase;
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

    private static GameObject RuntimePanel(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = NormalPlayer;
        return go;
    }

    private static Text RuntimeText(string value, Transform parent, int fontSize, TextAnchor alignment)
    {
        GameObject go = new GameObject("Text", typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        Text text = go.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = Color.white;
        text.raycastTarget = false;
        text.text = value;
        return text;
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
