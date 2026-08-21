#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Creates a real, standalone Kingdom reveal prefab and reconnects it inside
/// LobbySetupScreen.prefab. The host-selection UI is left untouched.
/// </summary>
public static class DominionRevealScreenBuilder
{
    private const string UiFolder = "Assets/Resources/UI";
    private const string LobbyPrefabPath = UiFolder + "/LobbySetupScreen.prefab";
    private const string RevealPrefabPath = UiFolder + "/KingdomRevealScreen.prefab";

    [MenuItem("Dominion/UI/Create or Reconnect Kingdom Reveal Prefab")]
    public static void Build()
    {
        Directory.CreateDirectory(UiFolder);
        AssetDatabase.Refresh();

        GameObject revealPrefab = BuildRevealPrefab();
        if (revealPrefab == null)
            return;

        ConnectRevealToLobby(revealPrefab);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        GameObject savedReveal = AssetDatabase.LoadAssetAtPath<GameObject>(RevealPrefabPath);
        Selection.activeObject = savedReveal;
        EditorGUIUtility.PingObject(savedReveal);

        Debug.Log(
            "Kingdom reveal prefab created and connected successfully. Open it here: "
            + RevealPrefabPath
            + " | Lobby: "
            + LobbyPrefabPath);
    }

    // Keep the old menu entry working, but make it perform the new unambiguous action.
    [MenuItem("Dominion/UI/Rebuild 10-Card Reveal Screen Only")]
    private static void LegacyBuildEntry()
    {
        Build();
    }

    private static GameObject BuildRevealPrefab()
    {
        GameObject root = UiObject("Reveal", typeof(Image), typeof(KingdomRevealScreenView));
        RectTransform rootRect = root.GetComponent<RectTransform>();
        Stretch(rootRect);

        Image background = root.GetComponent<Image>();
        background.color = new Color(0.055f, 0.055f, 0.055f, 1f);
        background.raycastTarget = true;

        Text title = ChildText(
            root.transform,
            "Title",
            "ROYAUME DE LA PARTIE",
            38,
            TextAnchor.MiddleCenter,
            new Vector2(0.05f, 0.915f),
            new Vector2(0.95f, 0.985f));
        title.fontStyle = FontStyle.Bold;

        Text subtitle = ChildText(
            root.transform,
            "Subtitle",
            "Ces 10 cartes seront disponibles dans la Réserve.",
            20,
            TextAnchor.MiddleCenter,
            new Vector2(0.15f, 0.865f),
            new Vector2(0.85f, 0.915f));
        subtitle.color = new Color(0.82f, 0.82f, 0.82f, 1f);

        RectTransform cardsPanel = ChildPanel(
            root.transform,
            "CardsPanel",
            new Vector2(0.06f, 0.17f),
            new Vector2(0.94f, 0.85f),
            new Color(0f, 0f, 0f, 0.22f));

        GameObject cardsAreaObject = UiObject("CardsArea", typeof(GridLayoutGroup));
        cardsAreaObject.transform.SetParent(cardsPanel, false);
        RectTransform cardsArea = cardsAreaObject.GetComponent<RectTransform>();
        Stretch(cardsArea);
        cardsArea.offsetMin = new Vector2(18f, 14f);
        cardsArea.offsetMax = new Vector2(-18f, -14f);

        GridLayoutGroup grid = cardsAreaObject.GetComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(220f, 339f); // locked 59:91 ratio
        grid.spacing = new Vector2(22f, 24f);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 5;
        grid.childAlignment = TextAnchor.MiddleCenter;
        grid.startAxis = GridLayoutGroup.Axis.Horizontal;
        grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
        grid.padding = new RectOffset(8, 8, 8, 8);

        Text status = ChildText(
            root.transform,
            "Status",
            "0/10 cartes",
            19,
            TextAnchor.MiddleLeft,
            new Vector2(0.08f, 0.045f),
            new Vector2(0.61f, 0.135f));
        status.color = new Color(0.88f, 0.88f, 0.88f, 1f);

        Button startButton = ChildButton(
            root.transform,
            "StartButton",
            "DÉMARRER LA PARTIE",
            new Vector2(0.65f, 0.045f),
            new Vector2(0.92f, 0.135f));

        KingdomRevealScreenView view = root.GetComponent<KingdomRevealScreenView>();
        SetSerialized(view, "_cardsRoot", cardsArea);
        SetSerialized(view, "_statusText", status);
        SetSerialized(view, "_startButton", startButton);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, RevealPrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    private static void ConnectRevealToLobby(GameObject revealPrefab)
    {
        GameObject lobbyAsset = AssetDatabase.LoadAssetAtPath<GameObject>(LobbyPrefabPath);
        if (lobbyAsset == null)
        {
            Debug.LogError(
                "LobbySetupScreen.prefab was not found at "
                + LobbyPrefabPath
                + ". Run Dominion/UI/Create or Rebuild Editable Lobby Prefabs first.");
            return;
        }

        GameObject lobbyRoot = PrefabUtility.LoadPrefabContents(LobbyPrefabPath);
        try
        {
            EditableLobbySetupController controller = lobbyRoot.GetComponent<EditableLobbySetupController>();
            if (controller == null)
            {
                Debug.LogError("LobbySetupScreen.prefab has no EditableLobbySetupController.");
                return;
            }

            Transform oldReveal = lobbyRoot.transform.Find("Reveal");
            if (oldReveal != null)
                Object.DestroyImmediate(oldReveal.gameObject);

            GameObject revealInstance = PrefabUtility.InstantiatePrefab(revealPrefab, lobbyRoot.transform) as GameObject;
            if (revealInstance == null)
            {
                Debug.LogError("Could not instantiate KingdomRevealScreen.prefab inside LobbySetupScreen.prefab.");
                return;
            }

            revealInstance.name = "Reveal";
            RectTransform revealRect = revealInstance.GetComponent<RectTransform>();
            if (revealRect != null)
                Stretch(revealRect);

            KingdomRevealScreenView view = revealInstance.GetComponent<KingdomRevealScreenView>();
            if (view == null)
            {
                Debug.LogError("Generated reveal prefab is missing KingdomRevealScreenView.");
                return;
            }

            SerializedObject serializedController = new SerializedObject(controller);
            serializedController.FindProperty("_revealScreen").objectReferenceValue = revealInstance;
            serializedController.FindProperty("_revealCardsRoot").objectReferenceValue = view.CardsRoot;
            serializedController.FindProperty("_revealStatus").objectReferenceValue = view.StatusText;
            serializedController.FindProperty("_startButton").objectReferenceValue = view.StartButton;

            SerializedProperty legacyPrefab = serializedController.FindProperty("_revealCardPrefab");
            if (legacyPrefab != null)
                legacyPrefab.objectReferenceValue = null;

            serializedController.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(lobbyRoot, LobbyPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(lobbyRoot);
        }
    }

    private static RectTransform ChildPanel(Transform parent, string name, Vector2 min, Vector2 max, Color color)
    {
        GameObject go = UiObject(name, typeof(Image));
        go.transform.SetParent(parent, false);
        RectTransform rect = go.GetComponent<RectTransform>();
        SetAnchors(rect, min, max);
        Image image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return rect;
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

    private static Button ChildButton(Transform parent, string name, string label, Vector2 min, Vector2 max)
    {
        GameObject go = UiObject(name, typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        SetAnchors(go.GetComponent<RectTransform>(), min, max);

        Image image = go.GetComponent<Image>();
        image.color = new Color(0.31f, 0.25f, 0.14f, 1f);

        Button button = go.GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 1f, 1f, 1f);
        colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
        colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.65f);
        button.colors = colors;

        Text text = ChildText(go.transform, "Text", label, 21, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one);
        text.fontStyle = FontStyle.Bold;
        return button;
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
        {
            Debug.LogError("Could not find serialized field '" + propertyName + "' on " + target.name + ".");
            return;
        }

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
