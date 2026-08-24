using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Applies the low-poly medieval GameBoard skin without changing gameplay layout or logic.
/// Source PNGs live in Assets/Resources/UI/GameBoardSkin/.
/// The skin keeps frames decorative and uses calm dark interiors, matching the reference UI hierarchy.
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

    private void Start() => Apply();

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

        ApplyImage("Background", _background, Image.Type.Simple, new Color(0.72f, 0.62f, 0.50f, 1f));

        // Frames stay decorative; the dark inner shades below provide the calm content surfaces.
        ApplyImage("LocalHand", _handPanel, Image.Type.Sliced, Color.white);
        ApplyImage("StatusPanel", _turnPanel, Image.Type.Sliced, Color.white);
        ApplyImage("SupplyPanel", _secondaryPanel, Image.Type.Sliced, new Color(0.78f, 0.76f, 0.72f, 1f));
        ApplyImage("InPlayPanel", _secondaryPanel, Image.Type.Sliced, new Color(0.78f, 0.76f, 0.72f, 1f));
        ApplyImage("JournalPanel", _secondaryPanel, Image.Type.Sliced, new Color(0.78f, 0.76f, 0.72f, 1f));

        MakeContentRootTransparent("InPlayPanel", "Cards");

        // Reference look: dark neutral interiors inside the carved frames.
        EnsureInnerShade("SupplyPanel", 22f, 22f, 34f, 22f, new Color(0.055f, 0.050f, 0.043f, 0.68f));
        EnsureInnerShade("InPlayPanel", 22f, 22f, 30f, 22f, new Color(0.055f, 0.050f, 0.043f, 0.62f));
        EnsureInnerShade("JournalPanel", 20f, 20f, 30f, 20f, new Color(0.050f, 0.047f, 0.042f, 0.70f));
        EnsureInnerShade("StatusPanel", 18f, 18f, 42f, 24f, new Color(0.050f, 0.047f, 0.042f, 0.60f));
        EnsureInnerShade("LocalHand", 24f, 24f, 34f, 22f, new Color(0.055f, 0.050f, 0.043f, 0.48f));

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
        _secondaryPanel = CreateCroppedSprite(boardTexture,
            new RectInt(6, 348, 1431, 367), new Vector4(72f, 52f, 72f, 52f));
        _handPanel = CreateCroppedSprite(handTexture,
            new RectInt(3, 111, 2040, 439), new Vector4(86f, 62f, 86f, 62f));
        _turnPanel = CreateCroppedSprite(turnTexture,
            new RectInt(29, 25, 966, 1477), new Vector4(76f, 82f, 76f, 82f));
        _titlePlate = CreateCroppedSprite(titleTexture,
            new RectInt(71, 131, 1907, 360), new Vector4(150f, 48f, 150f, 48f));
        _buttonNormal = CreateCroppedSprite(buttonTexture,
            new RectInt(94, 58, 1349, 271), new Vector4(82f, 38f, 82f, 38f));
        _buttonHover = CreateCroppedSprite(buttonTexture,
            new RectInt(93, 372, 1350, 272), new Vector4(82f, 38f, 82f, 38f));
        _buttonPressed = CreateCroppedSprite(buttonTexture,
            new RectInt(94, 690, 1349, 276), new Vector4(82f, 38f, 82f, 38f));
        _separator = CreateCroppedSprite(separatorTexture,
            new RectInt(32, 123, 1472, 99), new Vector4(68f, 16f, 68f, 16f));

        return _background != null && _secondaryPanel != null && _handPanel != null &&
               _turnPanel != null && _titlePlate != null &&
               _buttonNormal != null && _buttonHover != null && _buttonPressed != null;
    }

    private static Sprite CreateWholeSprite(Texture2D texture, Vector4 border)
    {
        if (texture == null) return null;
        return Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
    }

    private static Sprite CreateCroppedSprite(Texture2D texture, RectInt sourceRect, Vector4 border)
    {
        if (texture == null || sourceRect.width <= 0 || sourceRect.height <= 0) return null;

        int x = Mathf.Clamp(sourceRect.x, 0, texture.width - 1);
        int top = Mathf.Clamp(sourceRect.y, 0, texture.height - 1);
        int width = Mathf.Clamp(sourceRect.width, 1, texture.width - x);
        int height = Mathf.Clamp(sourceRect.height, 1, texture.height - top);
        int y = Mathf.Clamp(texture.height - top - height, 0, texture.height - height);

        return Sprite.Create(texture, new Rect(x, y, width, height),
            new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
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
        if (sprite == null) return;
        Transform target = FindDeepChild(transform, objectName);
        if (target == null) return;
        Image image = target.GetComponent<Image>();
        if (image == null) return;

        image.sprite = sprite;
        image.type = type;
        image.color = tint;
        image.preserveAspect = false;
        image.raycastTarget = false;
    }

    private void EnsureInnerShade(string panelName, float left, float right, float top, float bottom, Color color)
    {
        Transform panel = FindDeepChild(transform, panelName);
        if (panel == null) return;

        Transform existing = FindDirectChild(panel, "SkinInnerShade");
        RectTransform rect;
        Image image;
        if (existing == null)
        {
            GameObject go = new GameObject("SkinInnerShade", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(panel, false);
            go.transform.SetAsFirstSibling();
            rect = (RectTransform)go.transform;
            image = go.GetComponent<Image>();
        }
        else
        {
            rect = existing as RectTransform;
            image = existing.GetComponent<Image>();
        }

        if (rect == null || image == null) return;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
        image.sprite = null;
        image.type = Image.Type.Simple;
        image.color = color;
        image.raycastTarget = false;
    }

    private void MakeContentRootTransparent(string panelName, string contentName)
    {
        Transform panel = FindDeepChild(transform, panelName);
        if (panel == null) return;
        Transform content = FindDirectChild(panel, contentName) ?? FindDeepChild(panel, contentName);
        if (content == null) return;
        Image image = content.GetComponent<Image>();
        if (image == null) return;
        Color color = image.color;
        color.a = 0f;
        image.color = color;
        image.raycastTarget = false;
    }

    private void StyleTopBar()
    {
        Transform topBar = FindDeepChild(transform, "TopBar");
        if (topBar == null) return;
        Image image = topBar.GetComponent<Image>();
        if (image == null) return;
        image.sprite = null;
        image.type = Image.Type.Simple;
        image.color = new Color(0.035f, 0.032f, 0.028f, 0.86f);
        image.raycastTarget = false;
    }

    private void ApplyButtons()
    {
        Button[] buttons = GetComponentsInChildren<Button>(true);
        foreach (Button button in buttons)
        {
            if (button == null) continue;
            Image image = button.targetGraphic as Image ?? button.GetComponent<Image>();
            if (image == null) continue;

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

    private void StyleDeckAndDiscardTiles()
    {
        Text[] labels = GetComponentsInChildren<Text>(true);
        foreach (Text label in labels)
        {
            if (label == null || string.IsNullOrWhiteSpace(label.text)) continue;
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
                if (tileImage != null) break;
            }
            if (tileImage == null) continue;

            tileImage.sprite = null;
            tileImage.type = Image.Type.Simple;
            tileImage.color = new Color(0.035f, 0.032f, 0.028f, 0.92f);
            tileImage.raycastTarget = false;
            label.color = new Color(0.88f, 0.80f, 0.64f, 1f);
            label.fontStyle = FontStyle.Bold;

            Outline outline = tileImage.GetComponent<Outline>();
            if (outline == null) outline = tileImage.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.55f, 0.42f, 0.24f, 0.72f);
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = true;
        }
    }

    private void ApplyTitlePlates()
    {
        Text[] labels = GetComponentsInChildren<Text>(true);
        foreach (Text label in labels)
        {
            if (label == null || !ShouldDecorateTitle(label.text)) continue;
            ConfigureTitleTransform(label);
            EnsureTitlePlateBehind(label);
            label.color = new Color(0.16f, 0.105f, 0.055f, 1f);
            label.fontStyle = FontStyle.Bold;
            label.alignment = TextAnchor.MiddleCenter;
        }
    }

    private static bool ShouldDecorateTitle(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        string value = text.Trim().ToUpperInvariant();
        return value == "VOTRE MAIN" || value == "VOTRE TOUR" || value == "RÉSERVE" ||
               value.StartsWith("PLATEAU", StringComparison.Ordinal) || value == "JOURNAL";
    }

    private void ConfigureTitleTransform(Text label)
    {
        string value = label.text.Trim().ToUpperInvariant();
        RectTransform rect = label.rectTransform;

        Vector2 anchor = new Vector2(0.5f, 1f);
        Vector2 position = new Vector2(0f, -12f);
        float width = Mathf.Clamp(label.preferredWidth + 64f, 150f, 330f);
        float height = 42f;

        if (value == "VOTRE MAIN")
        {
            anchor = new Vector2(0.16f, 1f);
            position = new Vector2(0f, -10f);
            width = Mathf.Clamp(label.preferredWidth + 58f, 170f, 260f);
        }
        else if (value == "VOTRE TOUR")
        {
            position = new Vector2(0f, -13f);
            width = Mathf.Clamp(label.preferredWidth + 48f, 155f, 235f);
        }
        else if (value == "JOURNAL")
        {
            position = new Vector2(0f, -12f);
            width = Mathf.Clamp(label.preferredWidth + 52f, 145f, 230f);
        }
        else if (value == "RÉSERVE")
        {
            width = Mathf.Clamp(label.preferredWidth + 70f, 185f, 280f);
        }
        else if (value.StartsWith("PLATEAU", StringComparison.Ordinal))
        {
            width = Mathf.Clamp(label.preferredWidth + 70f, 235f, 360f);
        }

        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(width - 24f, height - 6f);
        label.fontSize = Mathf.Clamp(label.fontSize, 16, 22);
    }

    private void EnsureTitlePlateBehind(Text label)
    {
        RectTransform labelRect = label.rectTransform;
        RectTransform parentRect = labelRect.parent as RectTransform;
        if (parentRect == null) return;

        string plateName = "SkinTitlePlate_" + label.gameObject.name;
        Transform existing = FindDirectChild(parentRect, plateName);
        RectTransform plateRect;
        Image plateImage;

        if (existing == null)
        {
            GameObject go = new GameObject(plateName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parentRect, false);
            go.transform.SetSiblingIndex(labelRect.GetSiblingIndex());
            plateRect = (RectTransform)go.transform;
            plateImage = go.GetComponent<Image>();
        }
        else
        {
            plateRect = existing as RectTransform;
            plateImage = existing.GetComponent<Image>();
        }

        if (plateRect == null || plateImage == null) return;
        plateRect.anchorMin = labelRect.anchorMin;
        plateRect.anchorMax = labelRect.anchorMax;
        plateRect.pivot = new Vector2(0.5f, 0.5f);
        plateRect.anchoredPosition = labelRect.anchoredPosition;
        plateRect.sizeDelta = labelRect.sizeDelta + new Vector2(24f, 8f);
        plateRect.localScale = Vector3.one;

        plateImage.sprite = _titlePlate;
        plateImage.type = Image.Type.Sliced;
        plateImage.color = new Color(0.96f, 0.86f, 0.66f, 1f);
        plateImage.raycastTarget = false;
    }

    private void ApplyTopBarSeparator()
    {
        if (_separator == null) return;
        Transform topBar = FindDeepChild(transform, "TopBar");
        if (topBar == null) return;

        Transform existing = FindDirectChild(topBar, "SkinBottomSeparator");
        RectTransform rect;
        Image image;
        if (existing == null)
        {
            GameObject go = new GameObject("SkinBottomSeparator", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(topBar, false);
            go.transform.SetAsFirstSibling();
            rect = (RectTransform)go.transform;
            image = go.GetComponent<Image>();
        }
        else
        {
            rect = existing as RectTransform;
            image = existing.GetComponent<Image>();
        }

        if (rect == null || image == null) return;
        rect.anchorMin = new Vector2(0.02f, 0f);
        rect.anchorMax = new Vector2(0.98f, 0.055f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        image.sprite = _separator;
        image.type = Image.Type.Sliced;
        image.color = new Color(0.62f, 0.50f, 0.32f, 0.58f);
        image.raycastTarget = false;
    }

    private static Transform FindDirectChild(Transform parent, string childName)
    {
        if (parent == null) return null;
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (string.Equals(child.name, childName, StringComparison.Ordinal)) return child;
        }
        return null;
    }

    private static Transform FindDeepChild(Transform parent, string childName)
    {
        if (parent == null) return null;
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (string.Equals(child.name, childName, StringComparison.Ordinal)) return child;
            Transform nested = FindDeepChild(child, childName);
            if (nested != null) return nested;
        }
        return null;
    }
}
