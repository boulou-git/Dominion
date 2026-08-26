using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Single startup cover displayed while the first scene and Photon connection initialise.
/// It reuses the connection-screen artwork so no second loading visual appears afterwards.
/// </summary>
public sealed class StartupSplashController : MonoBehaviour
{
    public const float DisplayDurationSeconds = 4f;
    private const string RootName = "DominionStartupSplash";
    private const string ConnectionPrefabPath = "UI/ConnectionScreen";

    private static StartupSplashController _instance;
    private float _shownAt;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CreateBeforeFirstScene()
    {
        if (_instance != null)
            return;

        GameObject root = new GameObject(
            RootName,
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster),
            typeof(StartupSplashController));
        Object.DontDestroyOnLoad(root);

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        ResolveArtwork(out Sprite backgroundSprite, out Sprite logoSprite);
        CreateImage(root.transform, "Background", Vector2.zero, Vector2.one,
            backgroundSprite, Color.white, false);
        CreateImage(root.transform, "Logo", new Vector2(0.22f, 0.29f), new Vector2(0.78f, 0.71f),
            logoSprite, Color.white, true);
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
    }

    private void Update()
    {
        if (Time.realtimeSinceStartup - _shownAt >= DisplayDurationSeconds)
            Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }

    private static void ResolveArtwork(out Sprite background, out Sprite logo)
    {
        background = null;
        logo = null;
        GameObject prefab = Resources.Load<GameObject>(ConnectionPrefabPath);
        if (prefab == null)
            return;

        Image backgroundImage = FindImage(prefab.transform, "Background");
        Image logoImage = FindImage(prefab.transform, "Logo");
        background = backgroundImage != null ? backgroundImage.sprite : null;
        logo = logoImage != null ? logoImage.sprite : null;
    }

    private static Image FindImage(Transform parent, string objectName)
    {
        if (parent == null)
            return null;
        if (parent.name == objectName)
            return parent.GetComponent<Image>();

        for (int index = 0; index < parent.childCount; index++)
        {
            Image result = FindImage(parent.GetChild(index), objectName);
            if (result != null)
                return result;
        }

        return null;
    }

    private static void CreateImage(Transform parent, string objectName, Vector2 anchorMin, Vector2 anchorMax,
        Sprite sprite, Color color, bool preserveAspect)
    {
        GameObject imageObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        imageObject.transform.SetParent(parent, false);

        RectTransform rect = imageObject.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image image = imageObject.GetComponent<Image>();
        image.sprite = sprite;
        image.color = sprite != null ? color : new Color(0.035f, 0.035f, 0.031f, 1f);
        image.preserveAspect = preserveAspect;
        image.raycastTarget = true;
    }
}
