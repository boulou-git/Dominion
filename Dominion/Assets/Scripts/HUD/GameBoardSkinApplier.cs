using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Applies the low-poly medieval GameBoard skin without changing gameplay layout or logic.
/// Source PNGs live in Assets/Resources/UI/GameBoardSkin/.
/// Runtime-created sprites keep the skin modular and let the current editable GameScreen
/// prefab keep its anchors, card containers and gameplay controllers untouched.
/// </summary>
public sealed class GameBoardSkinApplier : MonoBehaviour
{
    private const string ResourceRoot = "UI/GameBoardSkin/";

    private Sprite _background;
    private Sprite _secondaryPanel;
    private Sprite _handPanel;
    private Sprite _turnPanel;
    private Sprite _titlePlate;
    private Sprite _buttonNormal;
    private Sprite _buttonHover;
    private Sprite _buttonPressed;
    private Sprite _separator;

    private bool _applied;

    private void Start()
    {
        Apply();
    }

    public void Apply()
    {
        if (_applied)
            return;

        if (!TryBuildSprites())
        {
            Debug.LogWarning(
                "GameBoard skin assets are missing. Expected PNGs in Assets/Resources/UI/GameBoardSkin/. " +
                "The existing GameScreen layout is left untouched.");
            return;
        }

        ApplyImage("Background", _background, Image.Type.Simple, Color.white);

        // Primary focus areas: strongest contrast and full opacity.
        ApplyImage("LocalHand", _handPanel, Image.Type.Sliced, Color.white);
        ApplyImage("StatusPanel", _turnPanel, Image.Type.Sliced, Color.white);

        // Secondary information areas deliberately sit one visual level lower.
        Color secondaryTint = new Color(0.90f, 0.88f, 0.84f, 0.94f);
        ApplyImage("SupplyPanel", _secondaryPanel, Image.Type.Sliced, secondaryTint);
        ApplyImage("InPlayPanel", _secondaryPanel, Image.Type.Sliced, secondaryTint);
        ApplyImage("JournalPanel", _secondaryPanel, Image.Type.Sliced, secondaryTint);

        // Legacy prototype fills must not sit on top of the new skin.
        MakeContentRootTransparent("InPlayPanel", "Cards");

        StyleTopBar();
        ApplyTitlePlates();
        ApplyButtons();
        StyleDeckAndDiscardTiles();
        ApplyTopBarSeparator();

        _applied = true;
    }

    private bool TryBuildSprites()
    {
        Texture2D backgroundTexture = Resources.Load<Texture2D>(ResourceRoot + "game_background");
        Texture2D boardTexture = Resources.Load<Texture2D>(ResourceRoot + "board");
        Texture2D handTexture = Resources.Load<Texture2D>(ResourceRoot + "hand_board");
        Texture2D turnTexture = Resources.Load<Texture2D>(ResourceRoot + "turn_board");
        Texture2D titleTexture = Resources.Load<Texture2D>(ResourceRoot + "game_text");
        Texture2D buttonTexture = Resources.Load<Texture2D>(ResourceRoot + "buttons");
        Texture2D separatorTexture = Resources.Load<Texture2D>(ResourceRoot + "separators");

        if (backgroundTexture == null || boardTexture == null || handTexture == null ||
            turnTexture == null || titleTexture == null || buttonTexture == null || separatorTexture == null)
            return false;

        _background = CreateWholeSprite(backgroundTexture, Vector4.zero);

        _secondaryPanel = CreateCroppedSprite(
            boardTexture,
            new RectInt(6, 348, 1431, 367),
            new Vector4(72f, 52f, 72f, 52f));

        _handPanel = CreateCroppedSprite(
            handTexture,
            new RectInt(3, 111, 2040, 439),
            new Vector4(86f, 62f, 86f, 62f));

        _turnPanel = CreateCroppedSprite(
            turnTexture,
            new RectInt(29, 25, 966, 1477),
            new Vector4(76f, 82f, 76f, 82f));

        _titlePlate = CreateCroppedSprite(
            titleTexture,
            new RectInt(71, 131, 1907, 360),
            new Vector4(150f, 48f, 150f, 48f));

        _buttonNormal = CreateCroppedSprite(
            buttonTexture,
            new RectInt(94, 58, 1349, 271),
            new Vector4(82f, 38f, 82f, 38f));

        _buttonHover = CreateCroppedSprite(
            buttonTexture,
            new RectInt(93, 372, 1350, 272),
            new Vector4(82f, 38f, 82f, 38f));

        _buttonPressed = CreateCroppedSprite(
            buttonTexture,
            new RectInt(94, 690, 1349, 276),
            new Vector4(82f, 38f, 82f, 38f));

        _separator = CreateCroppedSprite(
            separatorTexture,
            new RectInt(32, 123, 1472, 99),
            new Vector4(68f, 16f, 68f, 16f));

        return _background != null && _secondaryPanel != null && _handPanel != null &&
               _turnPanel != null && _titlePlate != null &&
               _buttonNormal != null && _buttonHover != null && _buttonPressed != null;
    }

    private static Sprite CreateWholeSprite(Texture2D texture, Vector4 border)
    {
        if (texture == null)
            return null;

        return Sprite.Create(
            texture,
            new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect,
            border);
    }

    private static Sprite CreateCroppedSprite(Texture2D texture, RectInt sourceRect, Vector4 border)
    {
        if (texture == null || sourceRect.width <= 0 || sourceRect.height <= 0)
            return null;

        int x = Mathf.Clamp(sourceRect.x, 0, texture.width - 1);
        int top = Mathf.Clamp(sourceRect.y, 0, texture.height - 1);
        int width = Mathf.Clamp(sourceRect.width, 1, texture.width - x);
        int height = Mathf.Clamp(sourceRect.height, 1, texture.height - top);
        int y = Mathf.Clamp(texture.height - top - height, 0, texture.height - height);

        return Sprite.Create(
            texture,
            new Rect(x, y, width, height),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect,
            ClampBorder(border, width, height));
    }

    private static Vector4 ClampBorder(Vector4 border, int width, int height)
    {
        float maxHorizontal = Mathf.Max(0f, width * 0.49f);
        float maxVertical = Mathf.Max(0f, height * 0.49f);
        return new Vector4(
            Mathf.Clamp(border.x, 0f, maxHorizontal),
            Mathf.Clamp(border.y, 0f, maxVertical),
            Mathf.Clamp(border.z, 0f, maxHorizontal),
            Mathf.Clamp(border.w, 0f, maxVertical));
    }

    private void ApplyImage(string objectName, Sprite sprite, Image.Type type, Color tint)
    {
        if (sprite == null)
            return;

        Transform target = FindDeepChild(transform, objectName);
        if (target == null)
            return;

        Image image = target.GetComponent<Image>();
        if (image == null)
            return;

        image.sprite = sprite;
        image.type = type;
        image.color = tint;
        image.preserveAspect = false;
        image.raycastTarget = false;
    }

    private void MakeContentRootTransparent(string panelName, string contentName)
    {
        Transform panel = FindDeepChild(transform, panelName);
        if (panel == null)
            return;

        Transform content = FindDirectChild(panel, contentName) ?? FindDeepChild(panel, contentName);
        if (content == null)
            return;

        Image image = content.GetComponent<Image>();
        if (image == null)
            return;

        Color color = image.color;
        color.a = 0f;
        image.color = color;
        image.raycastTarget = false;
    }

    /// <summary>
    /// Removes the prototype-black TopBar while keeping its exact layout and children.
    /// The wood background remains visible through a subtle warm translucent strip.
    /// </summary>
    private void StyleTopBar()
    {
        Transform topBar = FindDeepChild(transform, "TopBar");
        if (topBar == null)
            return;

        Image image = topBar.GetComponent<Image>();
        if (image != null)
        {
            image.sprite = null;
            image.type = Image.Type.Simple;
            image.color = new Color(0.10f, 0.065f, 0.035f, 0.68f);
            image.raycastTarget = false;
        }
    }

    private void ApplyButtons()
    {
        Button[] buttons = GetComponentsInChildren<Button>(true);
        foreach (Button button in buttons)
        {
            if (button == null)
                continue;

            Image image = button.targetGraphic as Image;
            if (image == null)
                image = button.GetComponent<Image>();
            if (image == null)
                continue;

            image.sprite = _buttonNormal;
            image.type = Image.Type.Sliced;
            image.color = Color.white;

            SpriteState sprites = button.spriteState;
            sprites.highlightedSprite = _buttonHover;
            sprites.selectedSprite = _buttonHover;
            sprites.pressedSprite = _buttonPressed;
            sprites.disabledSprite = _buttonNormal;
            button.spriteState = sprites;
            button.targetGraphic = image;
            button.transition = Selectable.Transition.SpriteSwap;
        }
    }

    /// <summary>
    /// The deck/discard counters are not Buttons, so they escaped the regular button skin
    /// and remained black prototype rectangles. Detect their labels and skin the nearest
    /// Image-bearing parent with the quieter Normal button sprite.
    /// </summary>
    private void StyleDeckAndDiscardTiles()
    {
        Text[] labels = GetComponentsInChildren<Text>(true);
        foreach (Text label in labels)
        {
            if (label == null || string.IsNullOrWhiteSpace(label.text))
                continue;

            string value = label.text.Trim().ToUpperInvariant();
            if (!value.StartsWith("PIOCHE", StringComparison.Ordinal) &&
                !value.StartsWith("DÉFAUSSE", StringComparison.Ordinal) &&
                !value.StartsWith("DEFAUSSE", StringComparison.Ordinal))
                continue;

            Transform current = label.transform.parent;
            Image tileImage = null;
            for (int depth = 0; depth < 3 && current != null; depth++, current = current.parent)
            {
                tileImage = current.GetComponent<Image>();
                if (tileImage != null)
                    break;
            }

            if (tileImage == null)
                continue;

            tileImage.sprite = _buttonNormal;
            tileImage.type = Image.Type.Sliced;
            tileImage.color = new Color(0.80f, 0.72f, 0.58f, 0.96f);
            tileImage.raycastTarget = false;

            label.color = new Color(0.98f, 0.94f, 0.84f, 1f);
        }
    }

    private void ApplyTitlePlates()
    {
        Text[] labels = GetComponentsInChildren<Text>(true);
        foreach (Text label in labels)
        {
            if (label == null || !ShouldDecorateTitle(label.text))
                continue;

            NudgeTitleInward(label);
            EnsureCompactTitlePlateBehind(label);
            label.color = new Color(0.98f, 0.93f, 0.80f, 1f);
            label.fontStyle = FontStyle.Bold;
        }
    }

    private static bool ShouldDecorateTitle(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        string value = text.Trim().ToUpperInvariant();
        return value == "VOTRE MAIN" || value == "VOTRE TOUR" || value == "RÉSERVE" ||
               value.StartsWith("PLATEAU", StringComparison.Ordinal) || value == "JOURNAL";
    }

    /// <summary>
    /// Existing title labels sit directly on the outer frame edge. Move them a few pixels
    /// into the content area while preserving their anchors and controller references.
    /// </summary>
    private static void NudgeTitleInward(Text label)
    {
        RectTransform rect = label.rectTransform;
        if (rect == null || rect.gameObject.GetComponent<TitleSkinAdjustedMarker>() != null)
            return;

        rect.anchoredPosition += new Vector2(8f, -8f);
        rect.gameObject.AddComponent<TitleSkinAdjustedMarker>();
    }

    private void EnsureCompactTitlePlateBehind(Text label)
    {
        RectTransform labelRect = label.rectTransform;
        RectTransform parentRect = labelRect.parent as RectTransform;
        if (parentRect == null)
            return;

        string plateName = "SkinTitlePlate_" + label.gameObject.name;
        Transform existing = FindDirectChild(parentRect, plateName);
        RectTransform plateRect;
        Image plateImage;

        if (existing == null)
        {
            GameObject plateObject = new GameObject(
                plateName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            plateObject.transform.SetParent(parentRect, false);
            plateObject.transform.SetSiblingIndex(labelRect.GetSiblingIndex());
            plateRect = (RectTransform)plateObject.transform;
            plateImage = plateObject.GetComponent<Image>();
        }
        else
        {
            plateRect = existing as RectTransform;
            plateImage = existing.GetComponent<Image>();
        }

        if (plateRect == null || plateImage == null)
            return;

        float preferredWidth = Mathf.Max(1f, label.preferredWidth);
        float plateWidth = Mathf.Clamp(preferredWidth + 38f, 118f, 285f);
        float plateHeight = Mathf.Clamp(Mathf.Max(label.preferredHeight + 12f, 32f), 32f, 46f);

        Vector2 anchor = (labelRect.anchorMin + labelRect.anchorMax) * 0.5f;
        plateRect.anchorMin = anchor;
        plateRect.anchorMax = anchor;
        plateRect.pivot = new Vector2(0.5f, 0.5f);
        plateRect.sizeDelta = new Vector2(plateWidth, plateHeight);
        plateRect.localScale = Vector3.one;

        Vector2 position = labelRect.anchoredPosition;
        float availableWidth = Mathf.Max(labelRect.rect.width, preferredWidth);
        float horizontalTravel = Mathf.Max(0f, (availableWidth - plateWidth) * 0.5f);

        switch (label.alignment)
        {
            case TextAnchor.UpperLeft:
            case TextAnchor.MiddleLeft:
            case TextAnchor.LowerLeft:
                position.x -= horizontalTravel;
                break;
            case TextAnchor.UpperRight:
            case TextAnchor.MiddleRight:
            case TextAnchor.LowerRight:
                position.x += horizontalTravel;
                break;
        }

        position.x += (0.5f - labelRect.pivot.x) * labelRect.rect.width;
        position.y += (0.5f - labelRect.pivot.y) * labelRect.rect.height;
        plateRect.anchoredPosition = position;

        plateImage.sprite = _titlePlate;
        plateImage.type = Image.Type.Sliced;
        plateImage.color = new Color(0.90f, 0.82f, 0.67f, 0.94f);
        plateImage.raycastTarget = false;
    }

    private void ApplyTopBarSeparator()
    {
        if (_separator == null)
            return;

        Transform topBar = FindDeepChild(transform, "TopBar");
        if (topBar == null)
            return;

        Transform existing = FindDirectChild(topBar, "SkinBottomSeparator");
        RectTransform rect;
        Image image;

        if (existing == null)
        {
            GameObject separatorObject = new GameObject(
                "SkinBottomSeparator",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            separatorObject.transform.SetParent(topBar, false);
            separatorObject.transform.SetAsFirstSibling();
            rect = (RectTransform)separatorObject.transform;
            image = separatorObject.GetComponent<Image>();
        }
        else
        {
            rect = existing as RectTransform;
            image = existing.GetComponent<Image>();
        }

        if (rect == null || image == null)
            return;

        rect.anchorMin = new Vector2(0.02f, 0f);
        rect.anchorMax = new Vector2(0.98f, 0.075f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        image.sprite = _separator;
        image.type = Image.Type.Sliced;
        image.color = new Color(0.82f, 0.70f, 0.48f, 0.82f);
        image.raycastTarget = false;
    }

    private static Transform FindDirectChild(Transform parent, string childName)
    {
        if (parent == null)
            return null;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (string.Equals(child.name, childName, StringComparison.Ordinal))
                return child;
        }

        return null;
    }

    private static Transform FindDeepChild(Transform parent, string childName)
    {
        if (parent == null)
            return null;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (string.Equals(child.name, childName, StringComparison.Ordinal))
                return child;

            Transform nested = FindDeepChild(child, childName);
            if (nested != null)
                return nested;
        }

        return null;
    }

    /// <summary>
    /// Marker only: prevents the runtime skin from nudging title labels more than once if
    /// Apply is called manually after Start. It carries no state and no gameplay behaviour.
    /// </summary>
    private sealed class TitleSkinAdjustedMarker : MonoBehaviour
    {
    }
}
