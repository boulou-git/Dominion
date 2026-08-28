using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Decorates the existing Deck panel with the shared card back while the GameState
/// remains the sole owner of the actual draw-pile contents.
/// </summary>
public sealed class DeckPileVisualController : MonoBehaviour
{
    private RectTransform _deckPanel;
    private Image _cardBackImage;
    private Text _deckText;
    private GameScreenController _screenController;

    private void Awake()
    {
        ResolveVisuals();
        _screenController = GetComponent<GameScreenController>();
        if (_screenController != null)
            _screenController.BoardPlayerChanged += HandleBoardPlayerChanged;
        NetworkGameState.StateChanged += Refresh;
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
        ResolveVisuals();
        if (_deckPanel == null)
            return;

        PlayerStateSnapshot viewedPlayer = _screenController != null
            ? _screenController.ResolveViewedPlayer(state)
            : ResolveActivePlayer(state);
        int count = viewedPlayer != null && viewedPlayer.Deck != null ? viewedPlayer.Deck.Count : 0;

        if (_cardBackImage != null)
            _cardBackImage.gameObject.SetActive(count > 0 && _cardBackImage.sprite != null);

        if (_deckText != null)
            _deckText.text = "PIOCHE\n" + (viewedPlayer != null ? count.ToString() : "—");
    }

    private void ResolveVisuals()
    {
        if (_deckPanel == null)
        {
            Transform deck = FindDeepChild(transform, "Deck");
            _deckPanel = deck as RectTransform;
        }

        if (_deckPanel == null)
            return;

        if (_deckText == null)
        {
            Transform textTransform = FindDirectChild(_deckPanel, "Text");
            _deckText = textTransform != null ? textTransform.GetComponent<Text>() : _deckPanel.GetComponentInChildren<Text>();
        }

        if (_cardBackImage == null)
        {
            Transform existing = FindDirectChild(_deckPanel, "CardBack");
            if (existing != null)
                _cardBackImage = existing.GetComponent<Image>();
        }

        if (_cardBackImage == null)
        {
            Debug.LogError("GameScreen prefab contract is incomplete: Deck/CardBack is missing.", this);
            return;
        }

        if (_cardBackImage.sprite == null)
        {
            _cardBackImage.sprite = CardBackReference.LoadSprite();
        }
    }

    private void HandleBoardPlayerChanged()
    {
        Refresh(NetworkGameState.State);
    }

    private static PlayerStateSnapshot ResolveActivePlayer(GameStateSnapshot state)
    {
        if (state == null || state.Players == null)
            return null;
        return state.Players.Find(player => player != null && player.PlayerId == state.ActivePlayerId);
    }

    private static Transform FindDirectChild(Transform parent, string childName)
    {
        if (parent == null)
            return null;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child.name == childName)
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
            if (child.name == childName)
                return child;

            Transform nested = FindDeepChild(child, childName);
            if (nested != null)
                return nested;
        }

        return null;
    }
}
