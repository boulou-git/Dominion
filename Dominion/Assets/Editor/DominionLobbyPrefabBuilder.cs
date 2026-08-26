#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class DominionLobbyPrefabBuilder
{
    private const string RootFolder = "Assets/Resources/UI";
    private const string ExtensionPrefabPath = RootFolder + "/ExtensionTile.prefab";
    private const string CardPrefabPath = RootFolder + "/CardSelectionTile.prefab";
    private const string LobbyPrefabPath = RootFolder + "/LobbySetupScreen.prefab";

    [MenuItem("Dominion/UI/Create or Rebuild Editable Lobby Prefabs")]
    public static void Build()
    {
        Directory.CreateDirectory(RootFolder);
        AssetDatabase.Refresh();

        ExtensionTileView extensionPrefab = BuildExtensionTile();
        CardSelectionTileView cardPrefab = BuildCardTile();
        BuildLobby(extensionPrefab, cardPrefab);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(LobbyPrefabPath);
        Debug.Log("Editable Dominion lobby prefabs created in " + RootFolder + ". Open LobbySetupScreen.prefab to redesign it freely.");
    }

    private static ExtensionTileView BuildExtensionTile()
    {
        GameObject root = UiObject("ExtensionTile", typeof(Image), typeof(Button), typeof(LayoutElement), typeof(RectMask2D), typeof(ExtensionTileView));
        LayoutElement layout = root.GetComponent<LayoutElement>();
        layout.preferredWidth = 420f;
        layout.preferredHeight = 270f;
        root.GetComponent<Image>().color = new Color(0.13f, 0.13f, 0.13f, 1f);

        Image artwork = ChildImage(root.transform, "Artwork", Vector2.zero, Vector2.one, Color.white);
        AspectRatioFitter artworkFitter = artwork.gameObject.AddComponent<AspectRatioFitter>();
        artworkFitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        SelectableArtworkView selection = artwork.gameObject.AddComponent<SelectableArtworkView>();
        SetSerialized(selection, "_artwork", artwork);

        Image shade = ChildImage(root.transform, "BottomShade", new Vector2(0f, 0f), new Vector2(1f, 0.34f), new Color(0f, 0f, 0f, 0.72f));
        shade.raycastTarget = false;
        Text name = ChildText(root.transform, "Name", "EXTENSION", 25, TextAnchor.MiddleLeft, new Vector2(0.05f, 0.13f), new Vector2(0.78f, 0.31f));
        Text count = ChildText(root.transform, "Count", "0 cartes", 17, TextAnchor.MiddleLeft, new Vector2(0.05f, 0.025f), new Vector2(0.78f, 0.15f));
        Toggle toggle = ChildToggle(root.transform, "EnabledToggle", new Vector2(0.82f, 0.72f), new Vector2(0.96f, 0.94f));

        ExtensionTileView view = root.GetComponent<ExtensionTileView>();
        SetSerialized(view, "_selectionVisual", selection);
        SetSerialized(view, "_nameText", name);
        SetSerialized(view, "_countText", count);
        SetSerialized(view, "_enabledToggle", toggle);
        SetSerialized(view, "_openButton", root.GetComponent<Button>());

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, ExtensionPrefabPath);
        Object.DestroyImmediate(root);
        return prefab.GetComponent<ExtensionTileView>();
    }

    private static CardSelectionTileView BuildCardTile()
    {
        GameObject root = UiObject("CardSelectionTile", typeof(Image), typeof(LayoutElement), typeof(CardSelectionTileView));
        LayoutElement layout = root.GetComponent<LayoutElement>();
        layout.preferredWidth = 210f;
        layout.preferredHeight = 320f;
        root.GetComponent<Image>().color = new Color(0.12f, 0.12f, 0.12f, 1f);

        Image artwork = ChildImage(root.transform, "Artwork", new Vector2(0.04f, 0.25f), new Vector2(0.96f, 0.96f), Color.white);
        SelectableArtworkView selection = artwork.gameObject.AddComponent<SelectableArtworkView>();
        SetSerialized(selection, "_artwork", artwork);

        Text name = ChildText(root.transform, "Name", "CARTE", 19, TextAnchor.MiddleCenter, new Vector2(0.04f, 0.12f), new Vector2(0.96f, 0.25f));
        Text details = ChildText(root.transform, "Details", "0 • Action", 15, TextAnchor.MiddleCenter, new Vector2(0.04f, 0.02f), new Vector2(0.96f, 0.13f));
        Toggle toggle = ChildToggle(root.transform, "SelectedToggle", new Vector2(0.79f, 0.80f), new Vector2(0.94f, 0.94f));

        CardSelectionTileView view = root.GetComponent<CardSelectionTileView>();
        SetSerialized(view, "_selectionVisual", selection);
        SetSerialized(view, "_nameText", name);
        SetSerialized(view, "_detailsText", details);
        SetSerialized(view, "_selectedToggle", toggle);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, CardPrefabPath);
        Object.DestroyImmediate(root);
        return prefab.GetComponent<CardSelectionTileView>();
    }

    private static void BuildLobby(ExtensionTileView extensionPrefab, CardSelectionTileView cardPrefab)
    {
        GameObject root = UiObject("LobbySetupScreen", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(EditableLobbySetupController));
        RectTransform rootRect = root.GetComponent<RectTransform>();
        Stretch(rootRect);
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 950;
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        GameObject background = UiObject("Background", typeof(Image));
        background.transform.SetParent(root.transform, false);
        Stretch(background.GetComponent<RectTransform>());
        background.GetComponent<Image>().color = new Color(0.055f, 0.055f, 0.055f, 1f);

        GameObject host = Screen(root.transform, "HostSelection");
        ChildText(host.transform, "Title", "PRÉPARATION DE LA PARTIE", 34, TextAnchor.MiddleCenter, new Vector2(0.03f, 0.91f), new Vector2(0.97f, 0.985f));

        RectTransform extPanel = Panel(host.transform, "ExtensionsPanel", new Vector2(0.035f, 0.14f), new Vector2(0.965f, 0.89f));
        ChildText(extPanel, "Header", "EXTENSIONS", 23, TextAnchor.MiddleLeft, new Vector2(0.04f, 0.91f), new Vector2(0.96f, 0.99f));
        RectTransform extContent = ScrollContent(extPanel, "ExtensionScroll", new Vector2(0.04f, 0.05f), new Vector2(0.96f, 0.90f), false, new Vector2(420f, 220f), 16f);

        RectTransform cardsPanel = Panel(host.transform, "CardsPanel", new Vector2(0.035f, 0.14f), new Vector2(0.965f, 0.89f));
        cardsPanel.GetComponent<Image>().color = new Color(0.055f, 0.052f, 0.047f, 1f);
        ChildButton(cardsPanel, "BackButton", "‹  EXTENSIONS", new Vector2(0.025f, 0.905f), new Vector2(0.19f, 0.985f));
        Text cardsTitle = ChildText(cardsPanel, "Header", "CARTES", 27, TextAnchor.MiddleLeft, new Vector2(0.22f, 0.905f), new Vector2(0.97f, 0.99f));
        RectTransform cardsContent = ScrollContent(cardsPanel, "CardsScroll", new Vector2(0.03f, 0.14f), new Vector2(0.97f, 0.90f), true, new Vector2(210f, 320f), 16f);
        Text summary = ChildText(host.transform, "Summary", "0 cartes dans le pool", 19, TextAnchor.MiddleLeft, new Vector2(0.055f, 0.035f), new Vector2(0.64f, 0.125f));
        Button validate = ChildButton(host.transform, "ValidateButton", "VALIDER LA SÉLECTION", new Vector2(0.70f, 0.03f), new Vector2(0.945f, 0.13f));

        GameObject waiting = Screen(root.transform, "Waiting");
        ChildText(waiting.transform, "WaitingText", "En attente de l’hôte…", 34, TextAnchor.MiddleCenter, new Vector2(0.18f, 0.30f), new Vector2(0.82f, 0.70f));

        GameObject reveal = Screen(root.transform, "Reveal");
        ChildText(reveal.transform, "Title", "LES 10 CARTES ROYAUME", 34, TextAnchor.MiddleCenter, new Vector2(0.05f, 0.91f), new Vector2(0.95f, 0.985f));
        RectTransform revealContent = ScrollContent(reveal.transform as RectTransform, "RevealCards", new Vector2(0.05f, 0.17f), new Vector2(0.95f, 0.89f), true, new Vector2(210f, 320f), 18f);
        GridLayoutGroup revealGrid = revealContent.GetComponent<GridLayoutGroup>();
        if (revealGrid != null)
            revealGrid.constraintCount = 5;
        Text revealStatus = ChildText(reveal.transform, "Status", string.Empty, 18, TextAnchor.MiddleLeft, new Vector2(0.05f, 0.045f), new Vector2(0.65f, 0.13f));
        Button start = ChildButton(reveal.transform, "StartButton", "DÉMARRER LA PARTIE", new Vector2(0.70f, 0.035f), new Vector2(0.95f, 0.135f));

        EditableLobbySetupController controller = root.GetComponent<EditableLobbySetupController>();
        SetSerialized(controller, "_hostSelectionScreen", host);
        SetSerialized(controller, "_waitingScreen", waiting);
        SetSerialized(controller, "_revealScreen", reveal);
        SetSerialized(controller, "_extensionsRoot", extContent);
        SetSerialized(controller, "_cardsRoot", cardsContent);
        SetSerialized(controller, "_cardsTitle", cardsTitle);
        SetSerialized(controller, "_selectionSummary", summary);
        SetSerialized(controller, "_validateButton", validate);
        SetSerialized(controller, "_extensionTilePrefab", extensionPrefab);
        SetSerialized(controller, "_cardTilePrefab", cardPrefab);
        SetSerialized(controller, "_waitingText", waiting.GetComponentInChildren<Text>());
        SetSerialized(controller, "_revealCardsRoot", revealContent);
        SetSerialized(controller, "_revealStatus", revealStatus);
        SetSerialized(controller, "_startButton", start);

        PrefabUtility.SaveAsPrefabAsset(root, LobbyPrefabPath);
        Object.DestroyImmediate(root);
    }

    private static GameObject Screen(Transform parent, string name)
    {
        GameObject go = UiObject(name, typeof(Image));
        go.transform.SetParent(parent, false);
        Stretch(go.GetComponent<RectTransform>());
        go.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0f);
        return go;
    }

    private static RectTransform Panel(Transform parent, string name, Vector2 min, Vector2 max)
    {
        GameObject go = UiObject(name, typeof(Image));
        go.transform.SetParent(parent, false);
        SetAnchors(go.GetComponent<RectTransform>(), min, max);
        go.GetComponent<Image>().color = new Color(0.10f, 0.10f, 0.10f, 0.96f);
        return go.GetComponent<RectTransform>();
    }

    private static RectTransform ScrollContent(RectTransform parent, string name, Vector2 min, Vector2 max, bool grid, Vector2 cellSize, float spacing)
    {
        GameObject viewportGo = UiObject(name, typeof(Image), typeof(Mask), typeof(ScrollRect));
        viewportGo.transform.SetParent(parent, false);
        SetAnchors(viewportGo.GetComponent<RectTransform>(), min, max);
        viewportGo.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.015f);
        viewportGo.GetComponent<Mask>().showMaskGraphic = false;

        GameObject contentGo = UiObject("Content", typeof(ContentSizeFitter));
        contentGo.transform.SetParent(viewportGo.transform, false);
        RectTransform content = contentGo.GetComponent<RectTransform>();
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.offsetMin = Vector2.zero;
        content.offsetMax = Vector2.zero;

        ContentSizeFitter fitter = contentGo.GetComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        if (grid)
        {
            GridLayoutGroup layout = contentGo.AddComponent<GridLayoutGroup>();
            layout.cellSize = cellSize;
            layout.spacing = new Vector2(spacing, spacing);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = 4;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.padding = new RectOffset(12, 12, 12, 12);
        }
        else
        {
            VerticalLayoutGroup layout = contentGo.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
        }

        ScrollRect scroll = viewportGo.GetComponent<ScrollRect>();
        scroll.content = content;
        scroll.viewport = viewportGo.GetComponent<RectTransform>();
        scroll.horizontal = false;
        scroll.vertical = true;
        return content;
    }

    private static GameObject UiObject(string name, params System.Type[] components)
    {
        System.Type[] all = new System.Type[components.Length + 1];
        all[0] = typeof(RectTransform);
        components.CopyTo(all, 1);
        return new GameObject(name, all);
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
        return text;
    }

    private static Button ChildButton(Transform parent, string name, string label, Vector2 min, Vector2 max)
    {
        GameObject go = UiObject(name, typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        SetAnchors(go.GetComponent<RectTransform>(), min, max);
        go.GetComponent<Image>().color = new Color(0.27f, 0.23f, 0.15f, 1f);
        ChildText(go.transform, "Text", label, 20, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one);
        return go.GetComponent<Button>();
    }

    private static Toggle ChildToggle(Transform parent, string name, Vector2 min, Vector2 max)
    {
        GameObject go = UiObject(name, typeof(Image), typeof(Toggle));
        go.transform.SetParent(parent, false);
        SetAnchors(go.GetComponent<RectTransform>(), min, max);
        Image background = go.GetComponent<Image>();
        background.color = new Color(0f, 0f, 0f, 0.72f);
        Image check = ChildImage(go.transform, "Check", new Vector2(0.20f, 0.20f), new Vector2(0.80f, 0.80f), Color.white);
        Toggle toggle = go.GetComponent<Toggle>();
        toggle.targetGraphic = background;
        toggle.graphic = check;
        return toggle;
    }

    private static void SetSerialized(Object target, string property, Object value)
    {
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty field = serialized.FindProperty(property);
        if (field != null)
        {
            field.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void SetAnchors(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
#endif
