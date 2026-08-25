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
    private const string VictoryPointShieldResource = "UI/VictoryPointShield";

    private const string RootName = "DominionEndGameFlow";
    private const string PrefabResourcePath = "UI/EndGameFlow";

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

    private static readonly Color Backdrop = new Color(0.015f, 0.014f, 0.012f, 0.96f);
    private static readonly Color Window = new Color(0.075f, 0.07f, 0.06f, 0.995f);
    private static readonly Color InnerPanel = new Color(0.115f, 0.105f, 0.087f, 0.98f);
    private static readonly Color Parchment = new Color(0.77f, 0.66f, 0.47f, 0.96f);
    private static readonly Color ParchmentDark = new Color(0.22f, 0.17f, 0.11f, 1f);
    private static readonly Color Gold = new Color(0.90f, 0.72f, 0.30f, 1f);
    private static readonly Color MutedGold = new Color(0.56f, 0.43f, 0.20f, 1f);
    private static readonly Color BlueBanner = new Color(0.08f, 0.19f, 0.27f, 1f);
    private static readonly Color GreenButton = new Color(0.14f, 0.29f, 0.11f, 1f);
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
    private Sprite _vpShield;
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
        Canvas canvas = GetComponent<Canvas>();
        if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
        if (canvas != null)
        {
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 6000;
        }

        CanvasScaler scaler = GetComponent<CanvasScaler>();
        if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
        if (GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();
        if (scaler != null)
        {
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
        }

        RectTransform ownRect = transform as RectTransform;
        if (ownRect == null)
        {
            Debug.LogError("EndGameFlow root must use a RectTransform.");
            return;
        }
        Stretch(ownRect);

        _vpShield = LoadVictoryPointShield();
        _cardBack = CardBackReference.LoadSprite();
        BuildSurface();
        NetworkGameState.StateChanged += OnStateChanged;
        NetworkGameState.HydrateFromRoom(true);
        OnStateChanged(NetworkGameState.State);
    }

    private static Sprite LoadVictoryPointShield()
    {
        Sprite sprite = Resources.Load<Sprite>(VictoryPointShieldResource);
        if (sprite != null)
        {
            Debug.Log("Loaded victory point shield as Sprite from Resources/UI/VictoryPointShield.");
            return sprite;
        }

        Texture2D texture = Resources.Load<Texture2D>(VictoryPointShieldResource);
        if (texture != null)
        {
            Sprite runtimeSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                100f);
            runtimeSprite.name = "VictoryPointShield_Runtime";
            Debug.LogWarning("Victory point shield was not exposed as a Sprite; created one from the Resources texture at runtime.");
            return runtimeSprite;
        }

        Debug.LogError("Victory point shield could not be loaded from Resources/UI/VictoryPointShield (neither Sprite nor Texture2D).");
        return null;
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

    private void BuildSurface()
    {
        _surface = CreatePanel("EndGameSurface", transform as RectTransform, Vector2.zero, Vector2.one, Backdrop);
        _surface.SetAsLastSibling();
        _surface.gameObject.SetActive(false);
    }

    private void ShowLocalScoring()
    {
        if (_surface == null || _finalState == null)
            return;

        _surface.gameObject.SetActive(true);
        ClearStage();
        _stageRoot = CreatePanel("ScoringStage", _surface, new Vector2(0.09f, 0.07f), new Vector2(0.91f, 0.93f), Window);

        Text title = CreateText("Title", _stageRoot, "DÉCOMPTE DES POINTS", 40, TextAnchor.MiddleCenter, Gold, FontStyle.Bold);
        SetAnchors(title.rectTransform, new Vector2(0.18f, 0.88f), new Vector2(0.82f, 0.975f));
        AddPanelBehind(title.rectTransform, "TitleBanner", BlueBanner, 10f);

        Text reason = CreateText("EndReason", _stageRoot, FormatEndReason(_finalState), 18, TextAnchor.MiddleCenter, new Color(0.85f, 0.82f, 0.75f, 1f));
        SetAnchors(reason.rectTransform, new Vector2(0.12f, 0.82f), new Vector2(0.88f, 0.875f));

        PlayerStateSnapshot localPlayer = ResolveLocalPlayer(_finalState);
        if (localPlayer == null)
        {
            Text error = CreateText("NoLocalPlayer", _stageRoot, "Impossible d’identifier le joueur local.", 22, TextAnchor.MiddleCenter, Color.white);
            SetAnchors(error.rectTransform, new Vector2(0.1f, 0.35f), new Vector2(0.9f, 0.65f));
            return;
        }

        PlayerScoreResult score = ScoringRules.CalculatePlayerScore(_finalState, localPlayer);
        RectTransform left = CreatePanel("CardCounting", _stageRoot, new Vector2(0.035f, 0.12f), new Vector2(0.47f, 0.80f), InnerPanel);
        RectTransform right = CreatePanel("Breakdown", _stageRoot, new Vector2(0.50f, 0.12f), new Vector2(0.965f, 0.80f), InnerPanel);

        BuildAnimationSide(left, score);
        BuildAnimatedBreakdown(right, score);

        _continueButton = CreateButton("ContinuerButton", _stageRoot, "CONTINUER", new Vector2(0.39f, 0.025f), new Vector2(0.61f, 0.095f), GreenButton, ShowFinalRanking);
        _continueButton.gameObject.SetActive(false);

        _animationLayer = CreateEmptyRect("AnimationLayer", _stageRoot, Vector2.zero, Vector2.one);
        _animationLayer.SetAsLastSibling();
        _scoreRoutine = StartCoroutine(AnimateOwnedCards(localPlayer, score));
    }

    private void BuildAnimationSide(RectTransform parent, PlayerScoreResult score)
    {
        Text playerName = CreateText("PlayerName", parent, string.IsNullOrEmpty(score.PlayerName) ? "Vous" : score.PlayerName, 28, TextAnchor.MiddleCenter, Gold, FontStyle.Bold);
        SetAnchors(playerName.rectTransform, new Vector2(0.08f, 0.82f), new Vector2(0.92f, 0.95f));

        _sourceDeck = CreateEmptyRect("SourceDeck", parent, new Vector2(0.11f, 0.28f), new Vector2(0.36f, 0.70f));
        Image deckImage = _sourceDeck.gameObject.AddComponent<Image>();
        deckImage.sprite = _cardBack;
        deckImage.preserveAspect = true;
        deckImage.color = _cardBack != null ? Color.white : new Color(0.16f, 0.14f, 0.11f, 1f);
        deckImage.raycastTarget = false;

        RectTransform deckShadow = CreatePanel("DeckShadow", parent, new Vector2(0.095f, 0.265f), new Vector2(0.345f, 0.685f), new Color(0f, 0f, 0f, 0.35f));
        deckShadow.SetSiblingIndex(_sourceDeck.GetSiblingIndex());

        Text deckLabel = CreateText("DeckLabel", parent, score.TotalCards + " cartes possédées", 18, TextAnchor.MiddleCenter, new Color(0.88f, 0.84f, 0.72f, 1f));
        SetAnchors(deckLabel.rectTransform, new Vector2(0.06f, 0.16f), new Vector2(0.42f, 0.25f));

        _nonScoringSink = CreateEmptyRect("CountedCards", parent, new Vector2(0.58f, 0.31f), new Vector2(0.84f, 0.70f));
        Image sinkImage = _nonScoringSink.gameObject.AddComponent<Image>();
        sinkImage.color = new Color(0.14f, 0.12f, 0.09f, 0.5f);
        sinkImage.raycastTarget = false;

        Text sinkLabel = CreateText("SinkLabel", parent, "CARTES\nCOMPTÉES", 16, TextAnchor.MiddleCenter, new Color(0.70f, 0.64f, 0.51f, 1f), FontStyle.Bold);
        SetAnchors(sinkLabel.rectTransform, new Vector2(0.55f, 0.16f), new Vector2(0.87f, 0.27f));

        _scoreStatus = CreateText("CountingStatus", parent, "Comptage en cours…", 19, TextAnchor.MiddleCenter, Color.white);
        SetAnchors(_scoreStatus.rectTransform, new Vector2(0.08f, 0.035f), new Vector2(0.92f, 0.13f));
    }

}
