using System;
using UnityEngine;
using UnityEngine.SceneManagement;

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

        if (root.GetComponent<BuyPhaseGameplayController>() == null)
            root.AddComponent<BuyPhaseGameplayController>();
    }
}
