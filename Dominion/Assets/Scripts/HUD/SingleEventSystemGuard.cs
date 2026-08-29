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
