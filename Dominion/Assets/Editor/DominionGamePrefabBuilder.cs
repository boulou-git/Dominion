#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class DominionGamePrefabBuilder
{
    private const string RootFolder = "Assets/Resources/UI";
    private const string GamePrefabPath = RootFolder + "/GameScreen.prefab";

    private static readonly Color Background = new Color(0.045f, 0.045f, 0.042f, 1f);
    private static readonly Color Panel = new Color(0.085f, 0.082f, 0.075f, 0.98f);
    private static readonly Color PanelAlt = new Color(0.115f, 0.108f, 0.095f, 0.98f);
    private static readonly Color Accent = new Color(0.31f, 0.255f, 0.14f, 1f);
    private static readonly Color Muted = new Color(0.72f, 0.69f, 0.61f, 1f);

    [MenuItem("Dominion/UI/Create or Rebuild Editable Game UI")]
    public static void Build()
    {
        Directory.CreateDirectory(RootFolder);
        AssetDatabase.Refresh();

        GameObject root = UiObject("GameScreen", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(GameScreenController));
        Stretch(root.GetComponent<RectTransform>());

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 1000;

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        scaler.referencePixelsPerUnit = 100f;

        Image background = ChildImage(root.transform, "Background", Vector2.zero, Vector2.one, Background);
        background.raycastTarget = false;

        // TOP — player order + current turn.
        RectTransform top = PanelRect(root.transform, "TopBar", new Vector2(0.015f, 0.915f), new Vector2(0.985f, 0.988f), PanelAlt);
        RectTransform players = EmptyRect(top, "Players", new Vector2(0.012f, 0.12f), new Vector2(0.67f, 0.88f));
        HorizontalLayoutGroup playerLayout = players.gameObject.AddComponent<HorizontalLayoutGroup>();
        playerLayout.spacing = 8f;
        playerLayout.childAlignment = TextAnchor.MiddleLeft;
        playerLayout.childControlWidth = false;
        playerLayout.childControlHeight = true;
        playerLayout.childForceExpandWidth = false;
        playerLayout.childForceExpandHeight = true;

        Text turnText = ChildText(top, "TurnText", "TOUR 1  •  Joueur", 21, TextAnchor.MiddleRight, new Vector2(0.68f, 0.05f), new Vector2(0.98f, 0.95f));
        turnText.fontStyle = FontStyle.Bold;

        // LEFT — compact journal/chat space.
        RectTransform journalPanel = PanelRect(root.transform, "JournalPanel", new Vector2(0.015f, 0.292f), new Vector2(0.185f, 0.905f), Panel);
        ChildText(journalPanel, "Header", "JOURNAL", 20, TextAnchor.MiddleLeft, new Vector2(0.07f, 0.90f), new Vector2(0.93f, 0.985f)).fontStyle = FontStyle.Bold;
        Text journalText = ChildText(journalPanel, "JournalText", "La partie va commencer…", 16, TextAnchor.UpperLeft, new Vector2(0.07f, 0.07f), new Vector2(0.93f, 0.88f));
        journalText.color = Muted;

        // CENTER — supply above, played cards below.
        RectTransform center = EmptyRect(root.transform, "CenterBoard", new Vector2(0.195f, 0.292f), new Vector2(0.805f, 0.905f));

        RectTransform supplyPanel = PanelRect(center, "SupplyPanel", new Vector2(0f, 0.38f), Vector2.one, Panel);
        ChildText(supplyPanel, "Header", "RÉSERVE", 21, TextAnchor.MiddleLeft, new Vector2(0.025f, 0.90f), new Vector2(0.975f, 0.99f)).fontStyle = FontStyle.Bold;

        Text baseLabel = ChildText(supplyPanel, "BaseSupplyLabel", "CARTES DE BASE", 13, TextAnchor.MiddleLeft, new Vector2(0.025f, 0.82f), new Vector2(0.405f, 0.89f));
        baseLabel.color = Muted;
        RectTransform baseSupply = EmptyRect(supplyPanel, "BaseSupply", new Vector2(0.025f, 0.055f), new Vector2(0.405f, 0.81f));
        GridLayoutGroup baseGrid = baseSupply.gameObject.AddComponent<GridLayoutGroup>();
        ConfigureSupplyGrid(baseGrid, 4);

        Text kingdomLabel = ChildText(supplyPanel, "KingdomLabel", "ROYAUME", 13, TextAnchor.MiddleLeft, new Vector2(0.43f, 0.82f), new Vector2(0.975f, 0.89f));
        kingdomLabel.color = Muted;
        RectTransform kingdomSupply = EmptyRect(supplyPanel, "KingdomSupply", new Vector2(0.43f, 0.055f), new Vector2(0.975f, 0.81f));
        GridLayoutGroup kingdomGrid = kingdomSupply.gameObject.AddComponent<GridLayoutGroup>();
        ConfigureSupplyGrid(kingdomGrid, 5);

        RectTransform inPlayPanel = PanelRect(center, "InPlayPanel", new Vector2(0f, 0f), new Vector2(1f, 0.355f), Panel);
        Text boardTitle = ChildText(inPlayPanel, "Header", "PLATEAU", 20, TextAnchor.MiddleLeft, new Vector2(0.025f, 0.80f), new Vector2(0.975f, 0.97f));
        boardTitle.fontStyle = FontStyle.Bold;
        RectTransform inPlay = EmptyRect(inPlayPanel, "Cards", new Vector2(0.025f, 0.06f), new Vector2(0.975f, 0.78f));
        HorizontalLayoutGroup inPlayLayout = inPlay.gameObject.AddComponent<HorizontalLayoutGroup>();
        inPlayLayout.spacing = 10f;
        inPlayLayout.childAlignment = TextAnchor.MiddleCenter;
        inPlayLayout.childControlWidth = false;
        inPlayLayout.childControlHeight = true;
        inPlayLayout.childForceExpandWidth = false;

        // RIGHT — turn resources, draw/discard, status, phase button.
        RectTransform right = PanelRect(root.transform, "StatusPanel", new Vector2(0.815f, 0.292f), new Vector2(0.985f, 0.905f), Panel);
        ChildText(right, "Header", "VOTRE TOUR", 20, TextAnchor.MiddleCenter, new Vector2(0.07f, 0.90f), new Vector2(0.93f, 0.985f)).fontStyle = FontStyle.Bold;

        Text phase = StatusLine(right, "Phase", "PHASE  ACTION", 0.80f, true);
        Text actions = StatusLine(right, "Actions", "Actions  1", 0.69f, false);
        Text buys = StatusLine(right, "Buys", "Achats  1", 0.60f, false);
        Text coins = StatusLine(right, "Coins", "Pièces  0", 0.51f, false);
        Text handCount = StatusLine(right, "HandCount", "Main  5", 0.42f, false);

        RectTransform deckPanel = PanelRect(right, "Deck", new Vector2(0.08f, 0.265f), new Vector2(0.46f, 0.385f), PanelAlt);
        Text deck = ChildText(deckPanel, "Text", "PIOCHE\n0", 16, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one);
        RectTransform discardPanel = PanelRect(right, "Discard", new Vector2(0.54f, 0.265f), new Vector2(0.92f, 0.385f), PanelAlt);
        Text discard = ChildText(discardPanel, "Text", "DÉFAUSSE\n0", 16, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one);

        Text status = ChildText(right, "Status", "À vous de jouer", 15, TextAnchor.MiddleCenter, new Vector2(0.06f, 0.17f), new Vector2(0.94f, 0.25f));
        status.color = Muted;
        Button nextPhase = ChildButton(right, "NextPhaseButton", "PASSER À L’ACHAT", new Vector2(0.08f, 0.055f), new Vector2(0.92f, 0.155f));
        Text nextPhaseText = nextPhase.GetComponentInChildren<Text>();

        // BOTTOM — local hand, deliberately wide and uncluttered.
        RectTransform handPanel = PanelRect(root.transform, "LocalHand", new Vector2(0.015f, 0.015f), new Vector2(0.985f, 0.277f), PanelAlt);
        ChildText(handPanel, "Header", "VOTRE MAIN", 18, TextAnchor.MiddleLeft, new Vector2(0.018f, 0.82f), new Vector2(0.982f, 0.98f)).fontStyle = FontStyle.Bold;
        RectTransform hand = EmptyRect(handPanel, "Cards", new Vector2(0.018f, 0.055f), new Vector2(0.982f, 0.81f));
        HorizontalLayoutGroup handLayout = hand.gameObject.AddComponent<HorizontalLayoutGroup>();
        handLayout.spacing = 8f;
        handLayout.childAlignment = TextAnchor.MiddleCenter;
        handLayout.childControlWidth = false;
        handLayout.childControlHeight = true;
        handLayout.childForceExpandWidth = false;

        // Shared zoom overlay for any card shown on the game screen.
        GameObject zoomOverlay = UiObject("CardZoomOverlay", typeof(Image), typeof(Button));
        zoomOverlay.transform.SetParent(root.transform, false);
        Stretch(zoomOverlay.GetComponent<RectTransform>());
        Image zoomBackground = zoomOverlay.GetComponent<Image>();
        zoomBackground.color = new Color(0f, 0f, 0f, 0.82f);
        Button zoomClose = zoomOverlay.GetComponent<Button>();
        zoomClose.targetGraphic = zoomBackground;

        Image zoomImage = ChildImage(zoomOverlay.transform, "Card", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Color.white);
        RectTransform zoomRect = zoomImage.rectTransform;
        zoomRect.sizeDelta = new Vector2(500f, 771f);
        zoomRect.anchoredPosition = Vector2.zero;
        zoomImage.preserveAspect = true;
        zoomImage.raycastTarget = false;
        ChildText(zoomOverlay.transform, "Hint", "Cliquez pour fermer", 14, TextAnchor.MiddleCenter, new Vector2(0.40f, 0.035f), new Vector2(0.60f, 0.075f)).color = Muted;
        zoomOverlay.SetActive(false);

        GameScreenController controller = root.GetComponent<GameScreenController>();
        SetSerialized(controller, "_playersRoot", players);
        SetSerialized(controller, "_turnText", turnText);
        SetSerialized(controller, "_baseSupplyRoot", baseSupply);
        SetSerialized(controller, "_kingdomSupplyRoot", kingdomSupply);
        SetSerialized(controller, "_inPlayRoot", inPlay);
        SetSerialized(controller, "_boardTitle", boardTitle);
        SetSerialized(controller, "_phaseText", phase);
        SetSerialized(controller, "_actionsText", actions);
        SetSerialized(controller, "_buysText", buys);
        SetSerialized(controller, "_coinsText", coins);
        SetSerialized(controller, "_deckText", deck);
        SetSerialized(controller, "_discardText", discard);
        SetSerialized(controller, "_handCountText", handCount);
        SetSerialized(controller, "_statusText", status);
        SetSerialized(controller, "_nextPhaseButton", nextPhase);
        SetSerialized(controller, "_nextPhaseButtonText", nextPhaseText);
        SetSerialized(controller, "_handRoot", hand);
        SetSerialized(controller, "_journalText", journalText);
        SetSerialized(controller, "_zoomOverlay", zoomOverlay);
        SetSerialized(controller, "_zoomImage", zoomImage);
        SetSerialized(controller, "_zoomCloseButton", zoomClose);

        PrefabUtility.SaveAsPrefabAsset(root, GamePrefabPath);
        Object.DestroyImmediate(root);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(GamePrefabPath);
        Debug.Log("Editable Dominion game UI created at " + GamePrefabPath + ". Open the prefab to redesign it freely.");
    }

    private static Text StatusLine(RectTransform parent, string name, string value, float y, bool accent)
    {
        Text text = ChildText(parent, name, value, accent ? 20 : 19, TextAnchor.MiddleLeft, new Vector2(0.10f, y), new Vector2(0.90f, y + 0.075f));
        text.color = accent ? Color.white : Muted;
        if (accent) text.fontStyle = FontStyle.Bold;
        return text;
    }

    private static GameObject UiObject(string name, params System.Type[] components)
    {
        System.Type[] all = new System.Type[components.Length + 1];
        all[0] = typeof(RectTransform);
        components.CopyTo(all, 1);
        return new GameObject(name, all);
    }

    private static void ConfigureSupplyGrid(GridLayoutGroup grid, int columns)
    {
        grid.cellSize = new Vector2(82f, 127f);
        grid.spacing = new Vector2(7f, 7f);
        grid.padding = new RectOffset(4, 4, 4, 4);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = columns;
        grid.childAlignment = TextAnchor.UpperLeft;
        grid.startAxis = GridLayoutGroup.Axis.Horizontal;
    }

    private static RectTransform EmptyRect(Transform parent, string name, Vector2 min, Vector2 max)
    {
        GameObject go = UiObject(name);
        go.transform.SetParent(parent, false);
        SetAnchors(go.GetComponent<RectTransform>(), min, max);
        return go.GetComponent<RectTransform>();
    }

    private static RectTransform PanelRect(Transform parent, string name, Vector2 min, Vector2 max, Color color)
    {
        GameObject go = UiObject(name, typeof(Image));
        go.transform.SetParent(parent, false);
        SetAnchors(go.GetComponent<RectTransform>(), min, max);
        Image image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
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

    private static Button ChildButton(Transform parent, string name, string label, Vector2 min, Vector2 max)
    {
        GameObject go = UiObject(name, typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        SetAnchors(go.GetComponent<RectTransform>(), min, max);
        Image image = go.GetComponent<Image>();
        image.color = Accent;
        Button button = go.GetComponent<Button>();
        button.targetGraphic = image;
        Text text = ChildText(go.transform, "Text", label, 17, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one);
        text.fontStyle = FontStyle.Bold;
        return button;
    }

    private static void SetSerialized(Object target, string property, Object value)
    {
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty field = serialized.FindProperty(property);
        if (field == null)
        {
            Debug.LogError("Missing serialized field " + property + " on " + target.name + ".");
            return;
        }

        field.objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
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
