using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

/// <summary>
/// Keeps one Input System EventSystem alive while Lobby and Game scenes overlap
/// during additive transitions. Scene-authored duplicates are disabled immediately.
/// </summary>
public static class SingleEventSystemGuard
{
    private static EventSystem _keeper;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        _keeper = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnsureExactlyOne();
    }

    public static void EnsureExactlyOne()
    {
        EventSystem[] systems = Object.FindObjectsByType<EventSystem>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (_keeper == null)
        {
            foreach (EventSystem candidate in systems)
                if (candidate != null && candidate.enabled && candidate.gameObject.activeInHierarchy)
                {
                    _keeper = candidate;
                    break;
                }
        }

        if (_keeper == null) return;
        if (_keeper.transform.parent != null) _keeper.transform.SetParent(null);
        Object.DontDestroyOnLoad(_keeper.gameObject);

        foreach (EventSystem candidate in systems)
        {
            if (candidate == null || candidate == _keeper) continue;
            candidate.enabled = false;
            BaseInputModule inputModule = candidate.GetComponent<BaseInputModule>();
            if (inputModule != null) inputModule.enabled = false;
            Object.Destroy(candidate.gameObject);
        }
    }
}

/// <summary>
/// Shared scene-level ownership helper for runtime UI roots. It also deactivates a
/// duplicate before deferred destruction, so it cannot receive clicks for one frame.
/// </summary>
public static class SceneUiInstanceGuard
{
    public static T KeepSingle<T>(Scene scene, string canonicalRootName) where T : Component
    {
        T keeper = null;
        T[] instances = Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        // Prefer the canonical root when a malformed duplicate is also present. The
        // FindObjectsSortMode.None order is intentionally unspecified by Unity.
        foreach (T instance in instances)
            if (instance != null && instance.gameObject.scene == scene &&
                string.Equals(instance.gameObject.name, canonicalRootName, System.StringComparison.Ordinal))
            {
                keeper = instance;
                break;
            }

        foreach (T instance in instances)
        {
            if (instance == null || instance.gameObject.scene != scene)
                continue;
            if (keeper == null)
            {
                keeper = instance;
                continue;
            }
            if (instance == keeper) continue;
            DeactivateAndDestroy(instance.gameObject);
        }
        if (keeper != null && !string.IsNullOrEmpty(canonicalRootName))
        {
            keeper.gameObject.name = canonicalRootName;
            if (!keeper.gameObject.activeSelf)
                keeper.gameObject.SetActive(true);
        }
        return keeper;
    }

    public static void RemoveAll<T>(Scene scene) where T : Component
    {
        T[] instances = Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (T instance in instances)
            if (instance != null && instance.gameObject.scene == scene)
                DeactivateAndDestroy(instance.gameObject);
    }

    public static void RemoveRootNamed(Scene scene, string rootName)
    {
        if (string.IsNullOrEmpty(rootName)) return;
        foreach (GameObject root in scene.GetRootGameObjects())
            if (root != null && string.Equals(root.name, rootName, System.StringComparison.Ordinal))
                DeactivateAndDestroy(root);
    }

    private static void DeactivateAndDestroy(GameObject target)
    {
        if (target == null) return;
        target.SetActive(false);
        Object.Destroy(target);
    }
}
