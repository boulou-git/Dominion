using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Applies the low-poly medieval GameBoard skin without changing gameplay layout or logic.
/// The skin is intentionally asset-driven: drop the source PNGs in
/// Assets/Resources/UI/GameBoardSkin/ and this component builds the required runtime sprites,
/// including cropped 9-sliced panel sprites and the Normal/Hover/Pressed button states.
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

        ApplyImage("Background", _background, Image.Type.Simple);

        // Primary focus areas deliberately share the richer visual language.
        ApplyImage("LocalHand", _handPanel, Image.Type.Sliced);
        ApplyImage("StatusPanel", _turnPanel, Image.Type.Sliced);

        // Secondary information areas use the quieter common board frame.
        ApplyImage("SupplyPanel", _secondaryPanel, Image.Type.Sliced);
        ApplyImage("InPlayPanel", _secondaryPanel, Image.Type.Sliced);
        ApplyImage("JournalPanel", _secondaryPanel, Image.Type.Sliced);

        ApplyTitlePlates();
        ApplyButtons();
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

        // Rects are based on the supplied source canvases. Coordinates here are expressed
        // top-left so the values remain easy to compare with the artwork files.
        _secondaryPanel = CreateCroppedSprite(boardTexture,
            new RectInt(6, 348, 1431, 367), new Vector4(72f, 52f, 72f, 52f));
        _handPanel = CreateCroppedSprite(handTexture,
            new RectInt(3, 111, 2040, 439), new Vector4(86f, 62f, 86f, 62f));
        _turnPanel = CreateCroppedSprite(turnTexture,
            new RectInt(29, 25, 966, 1477), new Vector4(76f, 82f, 76f, 82f));
        _titlePlate = CreateCroppedSprite(titleTexture,
            new RectInt(71, 131, 1907, 360), new Vector4(150f, 48f, 150f, 48f));

        // buttons.png contains three vertically stacked states: Normal, Hover, Pressed.
        _buttonNormal = CreateCroppedSprite(buttonTexture,
            new RectInt(94, 58, 1349, 271), new Vector4(82f, 38f, 82f, 38f));
        _buttonHover = CreateCroppedSprite(buttonTexture,
            new RectInt(93, 372, 1350, 272), new Vector4(82f, 38f, 82f, 38f));
        _buttonPressed = CreateCroppedSprite(buttonTexture,
            new RectInt(94, 690, 1349, 276), new Vector4(82f, 38f, 82f, 38f));

        // Use the first (long) supplied separator for the board-wide divider.
        _separator = CreateCroppedSprite(separatorTexture,
            new RectInt(32, 123, 1472, 99), new Vector4(68f, 16f, 68f, 16f));

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

    /// <summary>
    /// SourceRect uses image-editor coordinates (origin at top-left). Unity Sprite.Create
    /// uses bottom-left coordinates, so conversion is centralised here.
    /// </summary>
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

    private void ApplyImage(string objectName, Sprite sprite, Image.Type type)
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
        image.color = Color.white;
        image.preserveAspect = false;
        image.raycastTarget = false;
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

    private void ApplyTitlePlates()
    {
        Text[] labels = GetComponentsInChildren<Text>(true);
        foreach (Text label in labels)
        {
            if (label == null || !ShouldDecorateTitle(label.text))
                continue;

            EnsureTitlePlateBehind(label);
            label.color = new Color(0.96f, 0.90f, 0.73f, 1f);
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

    private void EnsureTitlePlateBehind(Text label)
    {
        RectTransform labelRect = label.rectTransform;
        Transform parent = labelRect.parent;
        if (parent == null)
            return;

        string plateName = "SkinTitlePlate_" + label.gameObject.name;
        Transform existing = FindDirectChild(parent, plateName);
        RectTransform plateRect;
        Image plateImage;

        if (existing == null)
        {
            GameObject plateObject = new GameObject(plateName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            plateObject.transform.SetParent(parent, false);
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

        plateRect.anchorMin = labelRect.anchorMin;
        plateRect.anchorMax = labelRect.anchorMax;
        plateRect.pivot = labelRect.pivot;
        plateRect.anchoredPosition = labelRect.anchoredPosition;
        plateRect.sizeDelta = labelRect.sizeDelta + new Vector2(26f, 14f);
        plateRect.localScale = Vector3.one;

        plateImage.sprite = _titlePlate;
        plateImage.type = Image.Type.Sliced;
        plateImage.color = Color.white;
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
                "SkinBottomSeparator", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
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

        rect.anchorMin = new Vector2(0.015f, 0f);
        rect.anchorMax = new Vector2(0.985f, 0.12f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        image.sprite = _separator;
        image.type = Image.Type.Sliced;
        image.color = Color.white;
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
}
