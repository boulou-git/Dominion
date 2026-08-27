#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class DominionConnectionPrefabBuilder
{
    private const string RootFolder = "Assets/Resources/UI";
    private const string PrefabPath = RootFolder + "/ConnectionScreen.prefab";
    private const string LogoPath = "Assets/2D/Game_Logo.png";

    private static readonly Color Background = new Color(0.035f, 0.035f, 0.031f, 1f);
    private static readonly Color Panel = new Color(0.075f, 0.073f, 0.066f, 0.985f);
    private static readonly Color Field = new Color(0.12f, 0.115f, 0.102f, 1f);
    private static readonly Color Accent = new Color(0.31f, 0.255f, 0.14f, 1f);
    private static readonly Color Muted = new Color(0.70f, 0.67f, 0.59f, 1f);

    [MenuItem("Dominion/UI/Create Missing Connection UI")]
    public static void Build()
    {
        Directory.CreateDirectory(RootFolder);
        AssetDatabase.Refresh();
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (existing != null)
        {
            Selection.activeObject = existing;
            Debug.Log("ConnectionScreen.prefab already exists and was left untouched.");
            return;
        }
        Sprite logoSprite = LoadLogoSprite();
        if (logoSprite == null)
        {
            Debug.LogError("Connection UI requires the logo at " + LogoPath + ". No fallback UI was generated.");
            return;
        }

        GameObject root = UiObject(
            "ConnectionScreen",
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster),
            typeof(ConnectionScreenController));
        Stretch(root.GetComponent<RectTransform>());

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 1100;

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        GameObject visualRoot = UiObject("VisualRoot", typeof(Image));
        visualRoot.transform.SetParent(root.transform, false);
        Stretch(visualRoot.GetComponent<RectTransform>());
        Image background = visualRoot.GetComponent<Image>();
        background.color = Background;
        background.raycastTarget = true;

        // Very light framing so the screen still belongs to the in-game UI family.
        Image topLine = ChildImage(visualRoot.transform, "TopAccent", new Vector2(0f, 0.982f), Vector2.one, Accent);
        topLine.raycastTarget = false;
        Image bottomLine = ChildImage(visualRoot.transform, "BottomAccent", Vector2.zero, new Vector2(1f, 0.008f), Accent);
        bottomLine.raycastTarget = false;

        Image leftShade = ChildImage(visualRoot.transform, "LeftShade", Vector2.zero, new Vector2(0.18f, 1f), new Color(0f, 0f, 0f, 0.16f));
        leftShade.raycastTarget = false;
        Image rightShade = ChildImage(visualRoot.transform, "RightShade", new Vector2(0.82f, 0f), Vector2.one, new Color(0f, 0f, 0f, 0.16f));
        rightShade.raycastTarget = false;

        Image logo = ChildImage(visualRoot.transform, "Logo", new Vector2(0.29f, 0.70f), new Vector2(0.71f, 0.91f), Color.white);
        logo.sprite = logoSprite;
        logo.preserveAspect = true;
        logo.raycastTarget = false;

        Text edition = ChildText(
            visualRoot.transform,
            "Edition",
            "ADAPTATION PERSONNELLE  •  2e ÉDITION",
            16,
            TextAnchor.MiddleCenter,
            new Vector2(0.30f, 0.665f),
            new Vector2(0.70f, 0.715f));
        edition.color = Muted;

        RectTransform panel = PanelRect(visualRoot.transform, "ConnectionPanel", new Vector2(0.35f, 0.22f), new Vector2(0.65f, 0.65f), Panel);

        Text title = ChildText(panel, "Title", "REJOINDRE UNE PARTIE", 28, TextAnchor.MiddleCenter, new Vector2(0.08f, 0.82f), new Vector2(0.92f, 0.94f));
        title.fontStyle = FontStyle.Bold;

        Text subtitle = ChildText(
            panel,
            "Subtitle",
            "Entrez votre pseudo pour accéder au lobby.",
            16,
            TextAnchor.MiddleCenter,
            new Vector2(0.08f, 0.72f),
            new Vector2(0.92f, 0.82f));
        subtitle.color = Muted;

        Text label = ChildText(panel, "PseudoLabel", "PSEUDO", 15, TextAnchor.MiddleLeft, new Vector2(0.10f, 0.61f), new Vector2(0.90f, 0.68f));
        label.color = Muted;
        label.fontStyle = FontStyle.Bold;

        InputField pseudo = ChildInputField(panel, "PseudoInput", "Votre pseudo", new Vector2(0.10f, 0.45f), new Vector2(0.90f, 0.60f));

        Text status = ChildText(panel, "Status", "Connexion au serveur…", 15, TextAnchor.MiddleCenter, new Vector2(0.10f, 0.31f), new Vector2(0.90f, 0.41f));
        status.color = Muted;

        Button join = ChildButton(panel, "JoinButton", "REJOINDRE LA PARTIE", new Vector2(0.10f, 0.11f), new Vector2(0.90f, 0.28f));

        Text hint = ChildText(
            visualRoot.transform,
            "Hint",
            "Entrée pour rejoindre",
            13,
            TextAnchor.MiddleCenter,
            new Vector2(0.38f, 0.15f),
            new Vector2(0.62f, 0.19f));
        hint.color = new Color(Muted.r, Muted.g, Muted.b, 0.72f);

        Text footer = ChildText(
            visualRoot.transform,
            "Footer",
            "DOMINION UNITY  •  PARTIE EN LIGNE",
            12,
            TextAnchor.MiddleCenter,
            new Vector2(0.25f, 0.025f),
            new Vector2(0.75f, 0.065f));
        footer.color = new Color(Muted.r, Muted.g, Muted.b, 0.55f);

        ConnectionScreenController controller = root.GetComponent<ConnectionScreenController>();
        SetSerialized(controller, "_visualRoot", visualRoot);
        SetSerialized(controller, "_pseudoInput", pseudo);
        SetSerialized(controller, "_joinButton", join);
        SetSerialized(controller, "_statusText", status);

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Debug.Log("Editable Dominion connection UI created at " + PrefabPath + ".");
    }

    private static Sprite LoadLogoSprite()
    {
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(LogoPath);
        if (sprite != null)
            return sprite;

        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(LogoPath))
        {
            if (asset is Sprite found)
                return found;
        }

        return null;
    }

    private static InputField ChildInputField(Transform parent, string name, string placeholderValue, Vector2 min, Vector2 max)
    {
        GameObject go = UiObject(name, typeof(Image), typeof(InputField));
        go.transform.SetParent(parent, false);
        SetAnchors(go.GetComponent<RectTransform>(), min, max);

        Image image = go.GetComponent<Image>();
        image.color = Field;

        Text value = ChildText(go.transform, "Text", string.Empty, 21, TextAnchor.MiddleLeft, new Vector2(0.05f, 0.08f), new Vector2(0.95f, 0.92f));
        value.supportRichText = false;

        Text placeholder = ChildText(go.transform, "Placeholder", placeholderValue, 20, TextAnchor.MiddleLeft, new Vector2(0.05f, 0.08f), new Vector2(0.95f, 0.92f));
        placeholder.color = new Color(Muted.r, Muted.g, Muted.b, 0.65f);
        placeholder.fontStyle = FontStyle.Italic;

        InputField input = go.GetComponent<InputField>();
        input.targetGraphic = image;
        input.textComponent = value;
        input.placeholder = placeholder;
        input.lineType = InputField.LineType.SingleLine;
        input.characterLimit = 20;
        input.caretColor = Color.white;
        input.selectionColor = new Color(Accent.r, Accent.g, Accent.b, 0.45f);

        return input;
    }

    private static Button ChildButton(Transform parent, string name, string value, Vector2 min, Vector2 max)
    {
        GameObject go = UiObject(name, typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        SetAnchors(go.GetComponent<RectTransform>(), min, max);

        Image image = go.GetComponent<Image>();
        image.color = Accent;

        Button button = go.GetComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
        colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
        colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.55f);
        button.colors = colors;

        Text text = ChildText(go.transform, "Text", value, 18, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one);
        text.fontStyle = FontStyle.Bold;
        return button;
    }

    private static RectTransform PanelRect(Transform parent, string name, Vector2 min, Vector2 max, Color color)
    {
        GameObject go = UiObject(name, typeof(Image));
        go.transform.SetParent(parent, false);
        SetAnchors(go.GetComponent<RectTransform>(), min, max);
        Image image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = true;
        return go.GetComponent<RectTransform>();
    }

    private static Image ChildImage(Transform parent, string name, Vector2 min, Vector2 max, Color color)
    {
        GameObject go = UiObject(name, typeof(Image));
        go.transform.SetParent(parent, false);
        SetAnchors(go.GetComponent<RectTransform>(), min, max);
        Image image = go.GetComponent<Image>();
        image.color = color;
        return image;
    }

    private static Text ChildText(Transform parent, string name, string value, int size, TextAnchor alignment, Vector2 min, Vector2 max)
    {
        GameObject go = UiObject(name, typeof(Text));
        go.transform.SetParent(parent, false);
        SetAnchors(go.GetComponent<RectTransform>(), min, max);
        Text text = go.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = value;
        text.fontSize = size;
        text.alignment = alignment;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    private static GameObject UiObject(string name, params System.Type[] components)
    {
        System.Type[] all = new System.Type[components.Length + 1];
        all[0] = typeof(RectTransform);
        components.CopyTo(all, 1);
        return new GameObject(name, all);
    }

    private static void SetSerialized(Object target, string propertyName, Object value)
    {
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property == null)
            throw new System.InvalidOperationException("Missing serialized property " + propertyName + ".");
        property.objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetAnchors(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
#endif
