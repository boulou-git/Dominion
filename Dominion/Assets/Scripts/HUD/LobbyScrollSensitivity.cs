using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Applies a comfortable mouse-wheel speed to every ScrollRect inside the editable lobby.
/// This is runtime-only so designers can freely edit/rebuild prefabs without losing the setting.
/// </summary>
public sealed class LobbyScrollSensitivity : MonoBehaviour
{
    private const string LobbySceneName = "Lobby";
    private const string LobbyRootName = "DominionLobbySetupUI";
    private const float ScrollSensitivity = 45f;

    private bool _applied;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateForCurrentScene()
    {
        EnsureForScene(SceneManager.GetActiveScene());
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnsureForScene(scene);
    }

    private static void EnsureForScene(Scene scene)
    {
        if (!string.Equals(scene.name, LobbySceneName, StringComparison.Ordinal))
            return;

        if (UnityEngine.Object.FindFirstObjectByType<LobbyScrollSensitivity>() != null)
            return;

        GameObject go = new GameObject("DominionLobbyScrollSensitivity");
        SceneManager.MoveGameObjectToScene(go, scene);
        go.AddComponent<LobbyScrollSensitivity>();
    }

    private void Update()
    {
        if (_applied)
            return;

        GameObject root = GameObject.Find(LobbyRootName);
        if (root == null)
            return;

        ScrollRect[] scrollRects = root.GetComponentsInChildren<ScrollRect>(true);
        if (scrollRects.Length == 0)
            return;

        foreach (ScrollRect scrollRect in scrollRects)
        {
            if (scrollRect != null)
                scrollRect.scrollSensitivity = ScrollSensitivity;
        }

        _applied = true;
        Debug.Log("Dominion lobby scroll sensitivity set to " + ScrollSensitivity + ".");
    }
}
