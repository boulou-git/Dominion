using Photon.Pun;
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

    private void Awake()
    {
        ResolveVisuals();
        NetworkGameState.StateChanged += Refresh;
        Refresh(NetworkGameState.State);
    }

    private void OnDestroy()
    {
        NetworkGameState.StateChanged -= Refresh;
    }

    private void Refresh(GameStateSnapshot state)
    {
        ResolveVisuals();
        if (_deckPanel == null)
            return;

        PlayerStateSnapshot localPlayer = ResolveLocalPlayer(state);
        int count = localPlayer != null && localPlayer.Deck != null ? localPlayer.Deck.Count : 0;

        if (_cardBackImage != null)
            _cardBackImage.gameObject.SetActive(count > 0 && _cardBackImage.sprite != null);

        if (_deckText != null)
            _deckText.text = "PIOCHE\n" + (localPlayer != null ? count.ToString() : "—");
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

    private static PlayerStateSnapshot ResolveLocalPlayer(GameStateSnapshot state)
    {
        if (state == null || state.Players == null)
            return null;

        string localId = NetworkGameState.LocalPlayerId;
        PlayerStateSnapshot localPlayer = state.Players.Find(player => player != null && player.PlayerId == localId);
        if (localPlayer != null)
            return localPlayer;

        if (PhotonNetwork.LocalPlayer != null)
        {
            int actorNumber = PhotonNetwork.LocalPlayer.ActorNumber;
            localPlayer = state.Players.Find(player => player != null && player.ActorNumber == actorNumber);
            if (localPlayer != null)
                return localPlayer;
        }

        return state.Players.Count == 1 ? state.Players[0] : null;
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
