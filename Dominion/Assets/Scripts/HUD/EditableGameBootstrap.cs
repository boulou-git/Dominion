using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Single bootstrap for the in-game UI. Loads the editable Resources/UI/GameScreen prefab.
/// </summary>
public static class EditableGameBootstrap
{
    private const string GameSceneName = "Game";
    private const string RootName = "DominionGameUI";
    private const string PrefabResourcePath = "UI/GameScreen";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!string.Equals(scene.name, GameSceneName, StringComparison.Ordinal))
            return;

        GameObject prefab = Resources.Load<GameObject>(PrefabResourcePath);
        if (prefab == null)
        {
            Debug.LogError("GameScreen prefab missing. Run Dominion > UI > Create or Rebuild Editable Game UI.");
            return;
        }

        GameObject existing = GameObject.Find(RootName);
        if (existing != null)
        {
            if (existing.GetComponent<GameScreenController>() != null)
            {
                EnsureGameUiControllers(existing);
                return;
            }

            UnityEngine.Object.Destroy(existing);
        }

        GameObject instance = UnityEngine.Object.Instantiate(prefab);
        instance.name = RootName;
        SceneManager.MoveGameObjectToScene(instance, scene);
        EnsureGameUiControllers(instance);
    }

    private static void EnsureGameUiControllers(GameObject root)
    {
        if (root == null)
            return;

        if (root.GetComponent<BaseSupplyController>() == null)
            root.AddComponent<BaseSupplyController>();

        if (root.GetComponent<DeckPileVisualController>() == null)
            root.AddComponent<DeckPileVisualController>();

        RemoveLegacyBaseSupplyLayout(root.transform);

        if (root.GetComponent<BuyPhaseGameplayController>() == null)
            root.AddComponent<BuyPhaseGameplayController>();

        // BuyPhaseGameplayController owns the Buy/Cleanup animation on this button.
        // This small binding restores the missing Action -> Buy transition without
        // replacing that existing listener.
        if (root.GetComponent<PhaseAdvanceButtonBinding>() == null)
            root.AddComponent<PhaseAdvanceButtonBinding>();

        // One generic popup/availability controller for all future blocking effects
        // (discard, trash, gain, reveal, choose-one, attack reactions, etc.).
        if (root.GetComponent<PendingChoiceUiController>() == null)
            root.AddComponent<PendingChoiceUiController>();

        ConfigureFixedReserveLayout(root.transform);
    }

    private static void RemoveLegacyBaseSupplyLayout(Transform root)
    {
        Transform baseSupply = FindDeepChild(root, "BaseSupply");
        if (baseSupply == null || baseSupply.GetComponent<GridLayoutGroup>() != null)
            return;

        LayoutGroup legacyLayout = baseSupply.GetComponent<LayoutGroup>();
        if (legacyLayout != null)
            UnityEngine.Object.DestroyImmediate(legacyLayout);
    }

    private static void ConfigureFixedReserveLayout(Transform root)
    {
        Transform supplyPanel = FindDeepChild(root, "SupplyPanel");
        RectTransform baseSupply = FindDeepChild(root, "BaseSupply") as RectTransform;
        RectTransform kingdomSupply = FindDeepChild(root, "KingdomSupply") as RectTransform;
        Transform baseLabel = FindDeepChild(root, "BaseSupplyLabel");
        Transform kingdomLabel = FindDeepChild(root, "KingdomLabel");

        if (supplyPanel == null || baseSupply == null || kingdomSupply == null)
            return;

        if (baseLabel != null)
            baseLabel.SetParent(supplyPanel, false);
        baseSupply.SetParent(supplyPanel, false);

        if (kingdomLabel != null)
            kingdomLabel.SetParent(supplyPanel, false);
        kingdomSupply.SetParent(supplyPanel, false);

        Transform scrollViewport = FindDeepChild(supplyPanel, "ReserveScrollViewport");
        if (scrollViewport != null)
        {
            scrollViewport.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(scrollViewport.gameObject);
        }

        SetAnchors(baseLabel as RectTransform, new Vector2(0.025f, 0.80f), new Vector2(0.39f, 0.88f));
        SetAnchors(baseSupply, new Vector2(0.025f, 0.055f), new Vector2(0.39f, 0.79f));
        ConfigureGrid(baseSupply, 4, new Vector2(82f, 127f), TextAnchor.UpperLeft);
        EnsureBaseFirstRowGap(baseSupply);

        SetAnchors(kingdomLabel as RectTransform, new Vector2(0.41f, 0.80f), new Vector2(0.975f, 0.88f));
        SetAnchors(kingdomSupply, new Vector2(0.41f, 0.055f), new Vector2(0.975f, 0.79f));
        ConfigureGrid(kingdomSupply, 5, new Vector2(82f, 127f), TextAnchor.UpperLeft);

        LayoutRebuilder.ForceRebuildLayoutImmediate(baseSupply);
        LayoutRebuilder.ForceRebuildLayoutImmediate(kingdomSupply);
        if (supplyPanel is RectTransform supplyRect)
            LayoutRebuilder.ForceRebuildLayoutImmediate(supplyRect);
        Canvas.ForceUpdateCanvases();
    }

    private static void ConfigureGrid(RectTransform root, int columns, Vector2 cellSize, TextAnchor alignment)
    {
        if (root == null)
            return;

        GridLayoutGroup grid = root.GetComponent<GridLayoutGroup>();
        LayoutGroup[] layouts = root.GetComponents<LayoutGroup>();
        foreach (LayoutGroup layout in layouts)
        {
            if (layout != null && !(layout is GridLayoutGroup))
                UnityEngine.Object.DestroyImmediate(layout);
        }

        if (grid == null)
            grid = root.gameObject.AddComponent<GridLayoutGroup>();
        if (grid == null)
            return;

        grid.enabled = true;
        grid.cellSize = cellSize;
        grid.spacing = new Vector2(7f, 7f);
        grid.padding = new RectOffset(4, 4, 4, 4);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = columns;
        grid.childAlignment = alignment;
        grid.startAxis = GridLayoutGroup.Axis.Horizontal;
    }

    private static void EnsureBaseFirstRowGap(RectTransform baseSupply)
    {
        if (baseSupply == null)
            return;

        Transform gap = FindDirectChild(baseSupply, "BaseRowGap");
        if (gap == null)
        {
            GameObject gapObject = new GameObject("BaseRowGap", typeof(RectTransform));
            gapObject.transform.SetParent(baseSupply, false);
            gap = gapObject.transform;
        }

        gap.SetSiblingIndex(Mathf.Min(3, baseSupply.childCount - 1));
    }

    private static void SetAnchors(RectTransform rect, Vector2 min, Vector2 max)
    {
        if (rect == null)
            return;

        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
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
