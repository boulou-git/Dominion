#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Rebuilds only the pre-game Kingdom reveal screen inside LobbySetupScreen.prefab.
/// Host-selection layout and the user's other manual prefab edits are left untouched.
/// </summary>
public static class DominionRevealScreenBuilder
{
    private const string LobbyPrefabPath = "Assets/Resources/UI/LobbySetupScreen.prefab";
    private const string CardPrefabPath = "Assets/Resources/UI/CardSelectionTile.prefab";

    [MenuItem("Dominion/UI/Rebuild 10-Card Reveal Screen Only")]
    public static void Build()
    {
        GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(LobbyPrefabPath);
        if (prefabAsset == null)
        {
            Debug.LogError("LobbySetupScreen.prefab was not found at " + LobbyPrefabPath + ". Generate the editable lobby first.");
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(LobbyPrefabPath);
        try
        {
            EditableLobbySetupController controller = root.GetComponent<EditableLobbySetupController>();
            Transform revealTransform = root.transform.Find("Reveal");
            if (controller == null || revealTransform == null)
            {
                Debug.LogError("LobbySetupScreen.prefab is missing EditableLobbySetupController or the Reveal object.");
                return;
            }

            RectTransform reveal = revealTransform as RectTransform;
            for (int i = reveal.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(reveal.GetChild(i).gameObject);

            Text title = ChildText(
                reveal,
                "Title",
                "ROYAUME DE LA PARTIE",
                38,
                TextAnchor.MiddleCenter,
                new Vector2(0.05f, 0.915f),
                new Vector2(0.95f, 0.985f));
            title.fontStyle = FontStyle.Bold;

            Text subtitle = ChildText(
                reveal,
                "Subtitle",
                "Ces 10 cartes seront disponibles dans la Réserve.",
                20,
                TextAnchor.MiddleCenter,
                new Vector2(0.15f, 0.865f),
                new Vector2(0.85f, 0.915f));
            subtitle.color = new Color(0.82f, 0.82f, 0.82f, 1f);

            RectTransform cardsPanel = ChildPanel(
                reveal,
                "CardsPanel",
                new Vector2(0.07f, 0.17f),
                new Vector2(0.93f, 0.85f),
                new Color(0f, 0f, 0f, 0.20f));

            GameObject cardsAreaObject = UiObject("CardsArea", typeof(GridLayoutGroup));
            cardsAreaObject.transform.SetParent(cardsPanel, false);
            RectTransform cardsArea = cardsAreaObject.GetComponent<RectTransform>();
            Stretch(cardsArea);
            cardsArea.offsetMin = new Vector2(18f, 14f);
            cardsArea.offsetMax = new Vector2(-18f, -14f);

            GridLayoutGroup grid = cardsAreaObject.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(220f, 339f); // 59:91 card ratio
            grid.spacing = new Vector2(22f, 24f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 5;
            grid.childAlignment = TextAnchor.MiddleCenter;
            grid.startAxis = GridLayoutGroup.Axis.Horizontal;
            grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
            grid.padding = new RectOffset(8, 8, 8, 8);

            Text status = ChildText(
                reveal,
                "Status",
                "10/10 cartes",
                19,
                TextAnchor.MiddleLeft,
                new Vector2(0.08f, 0.045f),
                new Vector2(0.61f, 0.135f));
            status.color = new Color(0.88f, 0.88f, 0.88f, 1f);

            Button startButton = ChildButton(
                reveal,
                "StartButton",
                "DÉMARRER LA PARTIE",
                new Vector2(0.65f, 0.045f),
                new Vector2(0.92f, 0.135f));

            CardSelectionTileView cardPrefab = AssetDatabase.LoadAssetAtPath<CardSelectionTileView>(CardPrefabPath);
            if (cardPrefab == null)
                Debug.LogWarning("CardSelectionTile.prefab was not found. The reveal screen will need its card prefab reference set manually.");

            SerializedObject serializedController = new SerializedObject(controller);
            serializedController.FindProperty("_revealCardsRoot").objectReferenceValue = cardsArea;
            serializedController.FindProperty("_revealStatus").objectReferenceValue = status;
            serializedController.FindProperty("_startButton").objectReferenceValue = startButton;
            if (cardPrefab != null)
                serializedController.FindProperty("_revealCardPrefab").objectReferenceValue = cardPrefab;
            serializedController.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, LobbyPrefabPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(LobbyPrefabPath);
            Debug.Log("Rebuilt only the 10-card reveal screen: fixed 5x2 card layout, full card artwork, host Start button. Other lobby prefab sections were preserved.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
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
        colors.highlightedColor = new Color(1.10f, 1.10f, 1.10f, 1f);
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
