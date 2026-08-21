using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Single pre-game UI bootstrap.
/// The same Lobby scene hosts the first connection screen and, once in a room,
/// the editable setup/reveal UI. No parallel network flow is created here.
/// </summary>
public static class EditableLobbyBootstrap
{
    private const string LobbySceneName = "Lobby";

    private const string SetupRootName = "DominionLobbySetupUI";
    private const string SetupPrefabResourcePath = "UI/LobbySetupScreen";

    private const string ConnectionRootName = "DominionConnectionUI";
    private const string ConnectionPrefabResourcePath = "UI/ConnectionScreen";

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

        EnsureSetupUi(scene);
        EnsureConnectionUi(scene);
    }

    private static void EnsureSetupUi(Scene scene)
    {
        GameObject prefab = Resources.Load<GameObject>(SetupPrefabResourcePath);
        if (prefab == null)
        {
            Debug.LogError("Missing Resources/UI/LobbySetupScreen prefab. Dominion pre-game UI cannot start.");
            return;
        }

        GameObject existing = GameObject.Find(SetupRootName);
        if (existing != null)
        {
            if (existing.GetComponent<EditableLobbySetupController>() != null)
                return;

            UnityEngine.Object.Destroy(existing);
        }

        GameObject instance = UnityEngine.Object.Instantiate(prefab);
        instance.name = SetupRootName;
        SceneManager.MoveGameObjectToScene(instance, scene);
    }

    private static void EnsureConnectionUi(Scene scene)
    {
        GameObject prefab = Resources.Load<GameObject>(ConnectionPrefabResourcePath);
        if (prefab == null)
        {
            Debug.LogWarning(
                "Missing Resources/UI/ConnectionScreen prefab. Run Dominion > UI > Create or Rebuild Connection UI once.");
            return;
        }

        GameObject existing = GameObject.Find(ConnectionRootName);
        if (existing != null)
        {
            if (existing.GetComponent<ConnectionScreenController>() != null)
                return;

            UnityEngine.Object.Destroy(existing);
        }

        GameObject instance = UnityEngine.Object.Instantiate(prefab);
        instance.name = ConnectionRootName;
        SceneManager.MoveGameObjectToScene(instance, scene);
    }
}
