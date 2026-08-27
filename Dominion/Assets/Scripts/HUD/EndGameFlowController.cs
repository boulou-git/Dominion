using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// Two-step Dominion end-game presentation:
/// 1) local animated scoring, 2) final ranking with per-player score details.
/// This is presentation-only; authoritative scoring/end conditions remain in Rules/Network.
/// </summary>
public sealed partial class EndGameFlowController : MonoBehaviour
{
    private const string RootName = "DominionEndGameFlow";
    private const string PrefabResourcePath = "UI/EndGameFlow";
    private const string ScoringStagePrefabPath = "UI/EndGameScoringStage";
    private const string RankingStagePrefabPath = "UI/EndGameRankingStage";
    private const string ScoreRowPrefabPath = "UI/EndGameScoreRow";
    private const string RankingRowPrefabPath = "UI/EndGameRankingRow";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!string.Equals(scene.name, "Game", StringComparison.Ordinal) || GameObject.Find(RootName) != null)
            return;

        GameObject prefab = Resources.Load<GameObject>(PrefabResourcePath);
        if (prefab == null)
        {
            Debug.LogError("EndGameFlow prefab missing at Resources/UI/EndGameFlow.");
            return;
        }

        GameObject instance = Instantiate(prefab);
        instance.name = RootName;
        SceneManager.MoveGameObjectToScene(instance, scene);
    }

    private static readonly Color RowDark = new Color(0.095f, 0.086f, 0.072f, 0.98f);
    private static readonly Color RowSelected = new Color(0.18f, 0.27f, 0.29f, 1f);
    private static readonly Color WinnerRow = new Color(0.39f, 0.29f, 0.11f, 1f);

    private RectTransform _surface;
    private RectTransform _stageRoot;
    private RectTransform _animationLayer;
    private RectTransform _sourceDeck;
    private RectTransform _nonScoringSink;
    private Text _scoreStatus;
    private Text _scoreTotalText;
    private Button _continueButton;
    private Sprite _cardBack;
    private Coroutine _scoreRoutine;
    private GameStateSnapshot _finalState;
    private string _shownMatchId;
    private int _activeCardAnimations;
    private int _animatedRunningTotal;
    private readonly Dictionary<string, ScoreRowVisual> _animatedRows = new Dictionary<string, ScoreRowVisual>(StringComparer.OrdinalIgnoreCase);
    private readonly List<Button> _rankingButtons = new List<Button>();

    private sealed class ScoreRowVisual
    {
        public CardScoreBreakdown Data;
        public RectTransform Root;
        public Text CopiesText;
        public Text PointsText;
        public int RevealedCopies;
    }

    private void Awake()
    {
        _surface = transform.Find("EndGameSurface") as RectTransform;
        if (GetComponent<Canvas>() == null || GetComponent<CanvasScaler>() == null ||
            GetComponent<GraphicRaycaster>() == null || _surface == null)
        {
            Debug.LogError("EndGameFlow prefab contract is incomplete.", this);
            enabled = false;
            return;
        }

        _cardBack = CardBackReference.LoadSprite();
        _surface.gameObject.SetActive(false);
        NetworkGameState.StateChanged += OnStateChanged;
        NetworkGameState.HydrateFromRoom(true);
        OnStateChanged(NetworkGameState.State);
    }

    private void OnDestroy()
    {
        NetworkGameState.StateChanged -= OnStateChanged;
    }

    private void OnStateChanged(GameStateSnapshot state)
    {
        if (state == null || !state.IsGameOver)
            return;

        if (!string.IsNullOrEmpty(_shownMatchId) && string.Equals(_shownMatchId, state.MatchId, StringComparison.Ordinal))
            return;

        _shownMatchId = state.MatchId;
        _finalState = state;
        ShowLocalScoring();
    }

    private void ShowLocalScoring()
    {
        if (_surface == null || _finalState == null)
            return;

        _surface.gameObject.SetActive(true);
        ClearStage();
        GameObject stagePrefab = Resources.Load<GameObject>(ScoringStagePrefabPath);
        if (stagePrefab == null)
        {
            Debug.LogError("EndGameScoringStage prefab missing.", this);
            return;
        }
        _stageRoot = Instantiate(stagePrefab, _surface).GetComponent<RectTransform>();
        Text reason = _stageRoot.transform.Find("EndReason")?.GetComponent<Text>();
        if (reason != null) reason.text = FormatEndReason(_finalState);

        PlayerStateSnapshot localPlayer = ResolveLocalPlayer(_finalState);
        if (localPlayer == null)
        {
            Transform error = _stageRoot.transform.Find("NoLocalPlayer");
            if (error != null) error.gameObject.SetActive(true);
            return;
        }

        PlayerScoreResult score = ScoringRules.CalculatePlayerScore(_finalState, localPlayer);
        RectTransform left = _stageRoot.transform.Find("CardCounting") as RectTransform;
        RectTransform right = _stageRoot.transform.Find("Breakdown") as RectTransform;
        _animationLayer = _stageRoot.transform.Find("AnimationLayer") as RectTransform;
        _continueButton = _stageRoot.transform.Find("ContinueButton")?.GetComponent<Button>();
        if (left == null || right == null || _animationLayer == null || _continueButton == null)
        {
            Debug.LogError("EndGameScoringStage prefab contract is incomplete.", _stageRoot);
            return;
        }

        BuildAnimationSide(left, score);
        BuildAnimatedBreakdown(right, score);

        _continueButton.onClick.RemoveAllListeners();
        _continueButton.onClick.AddListener(ShowFinalRanking);
        _continueButton.gameObject.SetActive(false);
        _scoreRoutine = StartCoroutine(AnimateOwnedCards(localPlayer, score));
    }

    private void BuildAnimationSide(RectTransform parent, PlayerScoreResult score)
    {
        Text playerName = parent.Find("PlayerName")?.GetComponent<Text>();
        if (playerName != null) playerName.text = string.IsNullOrEmpty(score.PlayerName) ? "Vous" : score.PlayerName;

        _sourceDeck = parent.Find("SourceDeck") as RectTransform;
        Image deckImage = _sourceDeck != null ? _sourceDeck.GetComponent<Image>() : null;
        if (deckImage == null)
        {
            Debug.LogError("EndGameScoringStage CardCounting/SourceDeck is missing its Image.", parent);
            return;
        }
        deckImage.sprite = _cardBack;
        deckImage.color = _cardBack != null ? Color.white : new Color(0.16f, 0.14f, 0.11f, 1f);
        Text deckLabel = parent.Find("DeckLabel")?.GetComponent<Text>();
        if (deckLabel != null) deckLabel.text = score.TotalCards + " cartes possédées";
        _nonScoringSink = parent.Find("CountedCards") as RectTransform;
        _scoreStatus = parent.Find("CountingStatus")?.GetComponent<Text>();
    }

}
