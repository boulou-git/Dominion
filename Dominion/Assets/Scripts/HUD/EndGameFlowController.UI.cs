using System;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class EndGameFlowController
{
    private void ClearStage()
    {
        if (_scoreRoutine != null)
        {
            StopCoroutine(_scoreRoutine);
            _scoreRoutine = null;
        }
        _activeCardAnimations = 0;
        _animatedRows.Clear();
        _rankingButtons.Clear();

        if (_stageRoot != null)
        {
            Destroy(_stageRoot.gameObject);
            _stageRoot = null;
        }
    }

    private static RectTransform CreatePanel(string name, RectTransform parent, Vector2 min, Vector2 max, Color color)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        SetAnchors(rect, min, max);
        Image image = obj.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = true;
        return rect;
    }

    private static RectTransform CreateEmptyRect(string name, RectTransform parent, Vector2 min, Vector2 max)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform));
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        SetAnchors(rect, min, max);
        return rect;
    }

    private static Text CreateText(string name, RectTransform parent, string value, int size, TextAnchor alignment, Color color, FontStyle style = FontStyle.Normal)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        Text text = obj.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = value;
        text.fontSize = size;
        text.alignment = alignment;
        text.color = color;
        text.fontStyle = style;
        text.raycastTarget = false;
        text.supportRichText = true;
        return text;
    }

    private Button CreateButton(string name, RectTransform parent, string label, Vector2 min, Vector2 max, Color color, Action action)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        SetAnchors(rect, min, max);
        Image image = obj.GetComponent<Image>();
        image.color = color;
        Button button = obj.GetComponent<Button>();
        button.targetGraphic = image;
        if (action != null) button.onClick.AddListener(() => action());
        Text text = CreateText("Label", rect, label, 22, TextAnchor.MiddleCenter, Gold, FontStyle.Bold);
        Stretch(text.rectTransform, 6f);
        return button;
    }

    private void CreatePointValue(RectTransform parent, string name, int value, out Text valueText, Vector2 min, Vector2 max, int fontSize, Color textColor)
    {
        RectTransform root = CreateEmptyRect(name, parent, min, max);
        RectTransform iconRoot = CreateEmptyRect("Shield", root, new Vector2(0.02f, 0.12f), new Vector2(0.34f, 0.88f));
        Image icon = iconRoot.gameObject.AddComponent<Image>();
        icon.sprite = _vpShield;
        icon.preserveAspect = true;
        icon.color = _vpShield != null ? Color.white : Gold;
        icon.raycastTarget = false;

        valueText = CreateText("Value", root, value.ToString(), fontSize, TextAnchor.MiddleRight, textColor, FontStyle.Bold);
        SetAnchors(valueText.rectTransform, new Vector2(0.37f, 0f), new Vector2(0.98f, 1f));
    }

    private static void AddPanelBehind(RectTransform target, string name, Color color, float inset)
    {
        if (target == null || target.parent == null) return;
        RectTransform parent = target.parent as RectTransform;
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = target.anchorMin;
        rect.anchorMax = target.anchorMax;
        rect.anchoredPosition = target.anchoredPosition;
        rect.offsetMin = target.offsetMin - new Vector2(inset, inset);
        rect.offsetMax = target.offsetMax + new Vector2(inset, inset);
        Image image = obj.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        rect.SetSiblingIndex(Mathf.Max(0, target.GetSiblingIndex()));
        target.SetAsLastSibling();
    }

    private static void SetAnchors(RectTransform rect, Vector2 min, Vector2 max)
    {
        if (rect == null) return;
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void Stretch(RectTransform rect, float inset = 0f)
    {
        if (rect == null) return;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }

    private static void ClearChildren(RectTransform parent)
    {
        if (parent == null) return;
        for (int i = parent.childCount - 1; i >= 0; i--)
            Destroy(parent.GetChild(i).gameObject);
    }

    private static string Sanitize(string value)
    {
        if (string.IsNullOrEmpty(value)) return "card";
        return value.Replace(':', '_').Replace('/', '_').Replace(' ', '_');
    }
}
