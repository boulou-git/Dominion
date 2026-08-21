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
        // Unity UI forbids two LayoutGroup components on the same object, so attempting
        // to add the GridLayoutGroup used by the scrollable Reserve returned null and
        // caused BuyPhaseGameplayController.ReparentGrid to throw. Remove only that
        // incompatible legacy layout before the gameplay controller is attached.
        RemoveLegacyBaseSupplyLayout(root.transform);

        if (root.GetComponent<BuyPhaseGameplayController>() == null)
            root.AddComponent<BuyPhaseGameplayController>();

        FixReserveScrollLayout(root.transform);
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
    /// The Reserve scroll content owns the vertical stacking of its two card grids.
    /// It must control child heights so the LayoutElement heights calculated by the
    /// gameplay controller are actually respected; otherwise Base and Kingdom retain
    /// their old anchored heights and overlap visually.
    /// </summary>
    private static void FixReserveScrollLayout(Transform root)
    {
        Transform content = FindDeepChild(root, "ReserveScrollContent");
        if (content == null)
            return;

        VerticalLayoutGroup vertical = content.GetComponent<VerticalLayoutGroup>();
        if (vertical != null)
        {
            vertical.childControlHeight = true;
            vertical.childForceExpandHeight = false;
            vertical.spacing = 10f;
        }

        RectTransform baseSupply = FindDeepChild(content, "BaseSupply") as RectTransform;
        RectTransform kingdomSupply = FindDeepChild(content, "KingdomSupply") as RectTransform;
        ApplyGridHeight(baseSupply, 7, 4);
        ApplyGridHeight(kingdomSupply, 10, 5);

        RectTransform contentRect = content as RectTransform;
        if (contentRect != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
        Canvas.ForceUpdateCanvases();
    }

    private static void ApplyGridHeight(RectTransform gridRoot, int cardCount, int columns)
    {
        if (gridRoot == null)
            return;

        int rows = Mathf.Max(1, Mathf.CeilToInt(cardCount / (float)Mathf.Max(1, columns)));
        const float cellHeight = 142f;
        const float rowSpacing = 10f;
        const float verticalPadding = 10f;
        float height = verticalPadding + rows * cellHeight + Mathf.Max(0, rows - 1) * rowSpacing;

        LayoutElement element = gridRoot.GetComponent<LayoutElement>();
        if (element == null)
            element = gridRoot.gameObject.AddComponent<LayoutElement>();
        element.preferredHeight = height;
        element.minHeight = height;
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
