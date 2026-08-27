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
            RuntimeCardView cardView = RuntimeCardView.Create(
                _baseSupplyRoot, "BaseSupply_" + card.id, card, sprite, false);
            if (cardView == null)
                continue;
            GameObject pileObject = cardView.gameObject;

            cardView.SetRemainingCount(0);
            Text count = cardView.RemainingCountText;
            if (count == null) continue;
            _countLabels[definitionId] = count;
            _pileObjects.Add(pileObject);
        }

        _built = _countLabels.Count > 0;
        LayoutRebuilder.ForceRebuildLayoutImmediate(_baseSupplyRoot);
        Canvas.ForceUpdateCanvases();

        Debug.Log("Base Reserve rendered: " + _pileObjects.Count + " pile(s).");
    }

    private static void EnsureLayout(RectTransform root)
    {
        if (root != null && root.GetComponent<LayoutGroup>() == null)
            Debug.LogError("GameScreen prefab contract is incomplete: BaseSupply needs a LayoutGroup.", root);
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
