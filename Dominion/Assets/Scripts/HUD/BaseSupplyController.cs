using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Renders the seven permanent base Reserve piles in the editable GameScreen.
/// Pile quantities come exclusively from the replicated authoritative GameState.
/// </summary>
public sealed class BaseSupplyController : MonoBehaviour
{
    private static readonly string[] BasePileIds =
    {
        "base:cuivre",
        "base:argent",
        "base:or",
        "base:domaine",
        "base:duche",
        "base:province",
        "base:malediction"
    };

    private readonly List<GameObject> _pileObjects = new List<GameObject>();
    private readonly Dictionary<string, Text> _countLabels = new Dictionary<string, Text>(StringComparer.OrdinalIgnoreCase);

    private RectTransform _baseSupplyRoot;
    private bool _built;

    private void Awake()
    {
        ResolveRoot();
        NetworkGameState.StateChanged += Refresh;
        Refresh(NetworkGameState.State);
    }

    private void OnDestroy()
    {
        NetworkGameState.StateChanged -= Refresh;
    }

    private void Refresh(GameStateSnapshot state)
    {
        ResolveRoot();
        if (_baseSupplyRoot == null)
            return;

        if (!_built)
            BuildPiles();

        foreach (string definitionId in BasePileIds)
        {
            Text label;
            if (!_countLabels.TryGetValue(definitionId, out label) || label == null)
                continue;

            SupplyPileSnapshot pile = NetworkGameState.FindSupplyPile(state, definitionId);
            label.text = pile != null ? Math.Max(0, pile.RemainingCount).ToString() : "—";
        }
    }

    private void ResolveRoot()
    {
        Transform found = FindDeepChild(transform, "BaseSupply");
        if (!(found is RectTransform rect))
            return;

        _baseSupplyRoot = rect;
        _baseSupplyRoot.gameObject.SetActive(true);
        EnsureLayout(_baseSupplyRoot);
    }

    private void BuildPiles()
    {
        ClearGeneratedPiles();
        _countLabels.Clear();
        _built = false;

        if (_baseSupplyRoot == null)
            return;

        foreach (string definitionId in BasePileIds)
        {
            // Keep the fourth cell of the first row empty so Victory cards start on
            // row two. The GridLayoutGroup in GameScreen.prefab owns its dimensions.
            if (string.Equals(definitionId, "base:domaine", StringComparison.OrdinalIgnoreCase))
            {
                GameObject gap = new GameObject("BaseRowGap", typeof(RectTransform));
                gap.transform.SetParent(_baseSupplyRoot, false);
                _pileObjects.Add(gap);
            }

            ExtensionPackageData extension;
            ExtensionCardData card;
            if (!RoomGameSetup.TryResolveCard(definitionId, out extension, out card))
            {
                Debug.LogWarning("Could not resolve base Reserve card: " + definitionId);
                continue;
            }

            Sprite sprite = ExtensionVisualLoader.LoadCardArtwork(extension, card);
            GameObject pileObject = new GameObject(
                "BaseSupply_" + card.id,
                typeof(RectTransform),
                typeof(Image));
            pileObject.transform.SetParent(_baseSupplyRoot, false);

            Image image = pileObject.GetComponent<Image>();
            image.sprite = sprite;
            image.color = sprite != null ? Color.white : new Color(0.55f, 0.12f, 0.12f, 1f);
            image.preserveAspect = true;
            image.raycastTarget = false;

            Text count = CreateCountBadge(pileObject.transform);
            _countLabels[definitionId] = count;
            _pileObjects.Add(pileObject);
        }

        _built = _countLabels.Count > 0;
        LayoutRebuilder.ForceRebuildLayoutImmediate(_baseSupplyRoot);
        Canvas.ForceUpdateCanvases();

        Debug.Log("Base Reserve rendered: " + _pileObjects.Count + " pile(s).");
    }

    private static Text CreateCountBadge(Transform parent)
    {
        GameObject badgeObject = new GameObject("RemainingCount", typeof(RectTransform), typeof(Image));
        badgeObject.transform.SetParent(parent, false);

        RectTransform badgeRect = badgeObject.GetComponent<RectTransform>();
        badgeRect.anchorMin = new Vector2(0.64f, 0.79f);
        badgeRect.anchorMax = new Vector2(0.98f, 0.98f);
        badgeRect.offsetMin = Vector2.zero;
        badgeRect.offsetMax = Vector2.zero;

        Image badge = badgeObject.GetComponent<Image>();
        badge.color = new Color(0.04f, 0.04f, 0.04f, 0.88f);
        badge.raycastTarget = false;

        GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(Outline));
        textObject.transform.SetParent(badgeObject.transform, false);

        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        Text text = textObject.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 22;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.raycastTarget = false;
        text.text = "—";

        Outline outline = textObject.GetComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
        outline.effectDistance = new Vector2(1f, -1f);

        return text;
    }

    private static void EnsureLayout(RectTransform root)
    {
        if (root == null || root.GetComponent<LayoutGroup>() != null)
            return;

        HorizontalLayoutGroup layout = root.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.padding = new RectOffset(4, 4, 4, 4);
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
    }

    private void ClearGeneratedPiles()
    {
        foreach (GameObject pile in _pileObjects)
        {
            if (pile != null)
                Destroy(pile);
        }
        _pileObjects.Clear();
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
