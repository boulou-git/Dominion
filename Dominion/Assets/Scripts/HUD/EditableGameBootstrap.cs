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
            Debug.LogError("GameScreen prefab missing. Run Dominion > UI > Create Missing Editable Game UI.");
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

        // Decorate the existing Pioche panel with the shared card back. The GameState
        // remains authoritative for the actual deck contents and count.
        if (root.GetComponent<DeckPileVisualController>() == null)
            root.AddComponent<DeckPileVisualController>();

        if (root.GetComponent<BuyPhaseGameplayController>() == null)
            root.AddComponent<BuyPhaseGameplayController>();

        // Generic durable decision presentation. This is attached at runtime so local
        // GameScreen prefab edits never need to be rebuilt just to support new card choices.
        if (root.GetComponent<PendingDecisionController>() == null)
            root.AddComponent<PendingDecisionController>();

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
