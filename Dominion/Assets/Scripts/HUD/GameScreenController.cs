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
    private bool _kingdomBuilt;

    private static readonly Color Panel = new Color(0.11f, 0.105f, 0.095f, 1f);
    private static readonly Color Active = new Color(0.40f, 0.32f, 0.17f, 1f);
    private static readonly Color Local = new Color(0.20f, 0.29f, 0.32f, 1f);

    private void Awake()
    {
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
        RefreshPlayerPills(state);

        if (!_kingdomBuilt)
            BuildKingdomSupply();

        if (state == null || state.Players == null || state.Players.Count == 0)
        {
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
        PlayerStateSnapshot localPlayer = state.Players.Find(p => p != null && p.PlayerId == NetworkGameState.LocalPlayerId);

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
            Sprite captured = sprite;
            if (captured != null)
                button.onClick.AddListener(() => ShowZoom(captured));

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