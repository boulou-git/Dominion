using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Single pre-game UI bootstrap.
/// Loads the editable Resources/UI/LobbySetupScreen prefab when the Lobby scene is loaded.
/// </summary>
public static class EditableLobbyBootstrap
{
    private const string LobbySceneName = "Lobby";
    private const string RootName = "DominionLobbySetupUI";
    private const string PrefabResourcePath = "UI/LobbySetupScreen";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!string.Equals(scene.name, LobbySceneName, StringComparison.Ordinal))
            return;

        GameObject prefab = Resources.Load<GameObject>(PrefabResourcePath);
        if (prefab == null)
        {
            Debug.LogError("Missing Resources/UI/LobbySetupScreen prefab. Dominion pre-game UI cannot start.");
            return;
        }

        GameObject existing = GameObject.Find(RootName);
        if (existing != null)
        {
            if (existing.GetComponent<EditableLobbySetupController>() != null)
                return;

            UnityEngine.Object.Destroy(existing);
        }

        GameObject instance = UnityEngine.Object.Instantiate(prefab);
        instance.name = RootName;
        SceneManager.MoveGameObjectToScene(instance, scene);
    }
}
