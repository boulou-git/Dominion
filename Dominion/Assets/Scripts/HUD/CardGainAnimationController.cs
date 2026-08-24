using System;
using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Purely visual feedback for cards newly gained by the local player.
/// It observes replicated snapshots, never delays or mutates gameplay, and queues gains
/// so multiple cards received in the same rules resolution remain readable.
/// </summary>
public sealed class CardGainAnimationController : MonoBehaviour
{
    private readonly HashSet<int> _knownOwnedInstanceIds = new HashSet<int>();
    private readonly Queue<GainVisual> _pending = new Queue<GainVisual>();

    private RectTransform _animationRoot;
    private bool _initialized;
    private bool _playing;

    private struct GainVisual
    {
        public int InstanceId;
        public string DefinitionId;
        public CardZone Destination;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        AttachToCurrentScreen();
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        AttachToCurrentScreen();
    }

    private static void AttachToCurrentScreen()
    {
        BuyPhaseGameplayController host = UnityEngine.Object.FindObjectOfType<BuyPhaseGameplayController>();
        if (host == null || host.GetComponent<CardGainAnimationController>() != null)
            return;

        host.gameObject.AddComponent<CardGainAnimationController>();
    }

    private void Awake()
    {
        _animationRoot = transform as RectTransform;
        NetworkGameState.StateChanged += HandleStateChanged;
        HandleStateChanged(NetworkGameState.State);
    }

    private void OnDestroy()
    {
        NetworkGameState.StateChanged -= HandleStateChanged;
    }

    private void HandleStateChanged(GameStateSnapshot state)
    {
        PlayerStateSnapshot local = ResolveLocalPlayer(state);
        if (state == null || local == null || state.CardInstances == null)
            return;

        if (!_initialized)
        {
            foreach (CardInstance card in state.CardInstances)
                if (card != null && string.Equals(card.OwnerPlayerId, local.PlayerId, StringComparison.Ordinal))
                    _knownOwnedInstanceIds.Add(card.InstanceId);

            _initialized = true;
            return;
        }

        foreach (CardInstance card in state.CardInstances)
        {
            if (card == null || !string.Equals(card.OwnerPlayerId, local.PlayerId, StringComparison.Ordinal))
                continue;
            if (!_knownOwnedInstanceIds.Add(card.InstanceId))
                continue;

            CardZone destination = ResolveDestination(local, card.InstanceId);
            _pending.Enqueue(new GainVisual
            {
                InstanceId = card.InstanceId,
                DefinitionId = card.DefinitionId,
                Destination = destination
            });
        }

        if (!_playing && _pending.Count > 0)
            StartCoroutine(PlayQueue());
    }

    private IEnumerator PlayQueue()
    {
        _playing = true;
        while (_pending.Count > 0)
        {
            GainVisual gain = _pending.Dequeue();
            yield return AnimateGain(gain);
        }
        _playing = false;
    }

    private IEnumerator AnimateGain(GainVisual gain)
    {
        if (_animationRoot == null)
            yield break;

        ExtensionPackageData extension;
        ExtensionCardData definition;
        if (!RoomGameSetup.TryResolveCard(gain.DefinitionId, out extension, out definition))
            yield break;

        Sprite sprite = ExtensionVisualLoader.LoadCardArtwork(extension, definition);
        if (sprite == null)
            yield break;

        RectTransform destination = ResolveDestinationTransform(gain.Destination);
        if (destination == null)
            destination = FindDeepChild(transform, "Discard") as RectTransform;

        GameObject visual = new GameObject("GainedCard_" + definition.id, typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
        RectTransform rect = visual.GetComponent<RectTransform>();
        rect.SetParent(_animationRoot, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(230f, 355f);
        rect.position = _animationRoot.TransformPoint(_animationRoot.rect.center);
        rect.localScale = Vector3.one * 0.78f;
        rect.SetAsLastSibling();

        Image image = visual.GetComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;

        CanvasGroup group = visual.GetComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;
        group.alpha = 0f;

        const float popDuration = 0.11f;
        float elapsed = 0f;
        while (elapsed < popDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / popDuration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            rect.localScale = Vector3.Lerp(Vector3.one * 0.78f, Vector3.one, eased);
            group.alpha = eased;
            yield return null;
        }

        yield return new WaitForSecondsRealtime(0.13f);

        Vector3 startWorld = rect.position;
        Vector3 targetWorld = destination != null
            ? destination.TransformPoint(destination.rect.center)
            : startWorld;
        Vector3 startScale = rect.localScale;

        const float flyDuration = 0.28f;
        elapsed = 0f;
        while (elapsed < flyDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / flyDuration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            rect.position = Vector3.Lerp(startWorld, targetWorld, eased);
            rect.localScale = Vector3.Lerp(startScale, Vector3.one * 0.30f, eased);
            group.alpha = Mathf.Lerp(1f, 0.35f, Mathf.Clamp01((t - 0.55f) / 0.45f));
            yield return null;
        }

        Destroy(visual);
        yield return new WaitForSecondsRealtime(0.03f);
    }

    private RectTransform ResolveDestinationTransform(CardZone destination)
    {
        switch (destination)
        {
            case CardZone.Hand:
            {
                Transform localHand = FindDeepChild(transform, "LocalHand");
                return FindDirectChild(localHand, "Cards") as RectTransform;
            }
            case CardZone.Discard:
                return FindDeepChild(transform, "Discard") as RectTransform;
            case CardZone.Deck:
            {
                Transform deck = FindDeepChild(transform, "Deck");
                if (deck == null) deck = FindDeepChild(transform, "DrawPile");
                return deck as RectTransform;
            }
            case CardZone.InPlay:
            {
                Transform panel = FindDeepChild(transform, "InPlayPanel");
                return FindDirectChild(panel, "Cards") as RectTransform;
            }
            default:
                return null;
        }
    }

    private static CardZone ResolveDestination(PlayerStateSnapshot player, int instanceId)
    {
        if (player == null || instanceId <= 0)
            return CardZone.None;
        if (player.Hand != null && player.Hand.Contains(instanceId)) return CardZone.Hand;
        if (player.Discard != null && player.Discard.Contains(instanceId)) return CardZone.Discard;
        if (player.Deck != null && player.Deck.Contains(instanceId)) return CardZone.Deck;
        if (player.InPlay != null && player.InPlay.Contains(instanceId)) return CardZone.InPlay;
        return CardZone.None;
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
}
