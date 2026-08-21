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
            GameObject cardBackObject = new GameObject("CardBack", typeof(RectTransform), typeof(Image));
            cardBackObject.transform.SetParent(_deckPanel, false);
            RectTransform rect = cardBackObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.12f, 0.04f);
            rect.anchorMax = new Vector2(0.88f, 0.96f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            _cardBackImage = cardBackObject.GetComponent<Image>();
            _cardBackImage.preserveAspect = true;
            _cardBackImage.raycastTarget = false;
            _cardBackImage.color = Color.white;
            _cardBackImage.sprite = CardBackReference.LoadSprite();
            cardBackObject.transform.SetAsFirstSibling();
        }
        else if (_cardBackImage.sprite == null)
        {
            _cardBackImage.sprite = CardBackReference.LoadSprite();
        }

        if (_deckText != null)
        {
            _deckText.color = Color.white;
            _deckText.fontStyle = FontStyle.Bold;
            _deckText.transform.SetAsLastSibling();

            Outline outline = _deckText.GetComponent<Outline>();
            if (outline == null)
                outline = _deckText.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);
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
