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

        // Base piles are built first so the gameplay decorator can immediately bind
        // quantities, inspection and purchasing to every visible Reserve pile.
        if (root.GetComponent<BaseSupplyController>() == null)
            root.AddComponent<BaseSupplyController>();

        // Older/local GameScreen prefabs still give BaseSupply a HorizontalLayoutGroup.
        // BuyPhaseGameplayController expects a grid, so remove that legacy layout first.
        RemoveLegacyBaseSupplyLayout(root.transform);

        if (root.GetComponent<BuyPhaseGameplayController>() == null)
            root.AddComponent<BuyPhaseGameplayController>();

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

    /// <summary>
    /// Displays the whole Reserve at once instead of scrolling it:
    /// left = 7 base piles, right = 10 Kingdom piles.
    /// Base uses a deliberate empty fourth slot on row one so the order is:
    /// Cuivre / Argent / Or, then Domaine / Duché / Province / Malédiction.
    /// </summary>
    private static void ConfigureFixedReserveLayout(Transform root)
    {
        Transform supplyPanel = FindDeepChild(root, "SupplyPanel");
        RectTransform baseSupply = FindDeepChild(root, "BaseSupply") as RectTransform;
        RectTransform kingdomSupply = FindDeepChild(root, "KingdomSupply") as RectTransform;
        Transform baseLabel = FindDeepChild(root, "BaseSupplyLabel");
        Transform kingdomLabel = FindDeepChild(root, "KingdomLabel");

        if (supplyPanel == null || baseSupply == null || kingdomSupply == null)
            return;

        // BuyPhaseGameplayController currently creates a runtime ScrollRect first.
        // Move the useful children back to SupplyPanel, then remove the empty scroll shell.
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

        // Left side: base cards.
        SetAnchors(baseLabel as RectTransform, new Vector2(0.025f, 0.80f), new Vector2(0.39f, 0.88f));
        SetAnchors(baseSupply, new Vector2(0.025f, 0.055f), new Vector2(0.39f, 0.79f));
        ConfigureGrid(baseSupply, 4, new Vector2(82f, 127f), TextAnchor.UpperLeft);
        EnsureBaseFirstRowGap(baseSupply);

        // Right side: the ten Kingdom cards in a clean 5 x 2 grid.
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

        // C / A / O / [vide]
        // D / Du / P / M
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
