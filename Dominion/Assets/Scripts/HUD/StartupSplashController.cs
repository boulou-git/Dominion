using UnityEngine;

/// <summary>
/// Single startup cover displayed while the first scene and Photon connection initialise.
/// Its visual hierarchy and timing are configured by Resources/UI/StartupSplash.prefab.
/// </summary>
public sealed class StartupSplashController : MonoBehaviour
{
    private const string RootName = "DominionStartupSplash";
    private const string PrefabPath = "UI/StartupSplash";

    [SerializeField] private CanvasGroup _canvasGroup;
    [SerializeField, Min(0f)] private float _displayDuration = 4f;
    [SerializeField, Min(0.01f)] private float _fadeDuration = 0.3f;

    private static StartupSplashController _instance;
    private float _shownAt;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CreateBeforeFirstScene()
    {
        if (_instance != null)
            return;

        GameObject prefab = Resources.Load<GameObject>(PrefabPath);
        if (prefab == null)
        {
            Debug.LogError("Missing Resources/UI/StartupSplash prefab.");
            return;
        }

        GameObject root = Object.Instantiate(prefab);
        root.name = RootName;
        Object.DontDestroyOnLoad(root);
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        _shownAt = Time.realtimeSinceStartup;
        if (_canvasGroup != null)
        {
            _canvasGroup.alpha = 1f;
            _canvasGroup.interactable = true;
            _canvasGroup.blocksRaycasts = true;
        }
    }

    private void Update()
    {
        float fadeElapsed = Time.realtimeSinceStartup - _shownAt - Mathf.Max(0f, _displayDuration);
        if (fadeElapsed < 0f)
            return;

        float progress = Mathf.Clamp01(fadeElapsed / Mathf.Max(0.01f, _fadeDuration));
        if (_canvasGroup != null)
            _canvasGroup.alpha = 1f - progress;

        if (progress >= 1f)
            Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }
}
