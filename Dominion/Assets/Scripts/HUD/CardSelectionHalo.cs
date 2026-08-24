using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Lightweight selection glow that never re-renders the card sprite.
/// It draws translucent edge bands around the card so selection remains readable
/// without the duplicated-artwork look produced by Unity UI Outline.
/// </summary>
public sealed class CardSelectionHalo : MonoBehaviour
{
    private static readonly Color DefaultColor = new Color(1.0f, 0.843f, 0.0f, 1.0f);
    private readonly List<Image> _segments = new List<Image>();
    private bool _built;

    public void SetVisible(bool visible)
    {
        EnsureBuilt();
        for (int i = 0; i < _segments.Count; i++)
            if (_segments[i] != null)
                _segments[i].gameObject.SetActive(visible);
    }

    public void SetColor(Color color)
    {
        EnsureBuilt();
        float[] alpha = { 0.48f, 0.24f, 0.10f };
        for (int i = 0; i < _segments.Count; i++)
        {
            if (_segments[i] == null) continue;
            int layer = i / 4;
            float a = layer >= 0 && layer < alpha.Length ? alpha[layer] : 0.10f;
            _segments[i].color = new Color(color.r, color.g, color.b, a);
        }
    }

    private void EnsureBuilt()
    {
        if (_built) return;
        _built = true;

        CreateLayer(0, 1.5f, 2.5f, 0.48f);
        CreateLayer(1, 4.5f, 3.5f, 0.24f);
        CreateLayer(2, 8.5f, 4.5f, 0.10f);
    }

    private void CreateLayer(int layer, float distance, float thickness, float alpha)
    {
        Color color = new Color(DefaultColor.r, DefaultColor.g, DefaultColor.b, alpha);
        CreateSegment("HaloTop_" + layer, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(-distance - thickness, distance), new Vector2(distance + thickness, distance + thickness), color);
        CreateSegment("HaloBottom_" + layer, new Vector2(0f, 0f), new Vector2(1f, 0f),
            new Vector2(-distance - thickness, -distance - thickness), new Vector2(distance + thickness, -distance), color);
        CreateSegment("HaloLeft_" + layer, new Vector2(0f, 0f), new Vector2(0f, 1f),
            new Vector2(-distance - thickness, -distance), new Vector2(-distance, distance), color);
        CreateSegment("HaloRight_" + layer, new Vector2(1f, 0f), new Vector2(1f, 1f),
            new Vector2(distance, -distance), new Vector2(distance + thickness, distance), color);
    }

    private void CreateSegment(string objectName, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 offsetMin, Vector2 offsetMax, Color color)
    {
        GameObject segmentObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
        segmentObject.transform.SetParent(transform, false);
        segmentObject.transform.SetAsFirstSibling();

        RectTransform rect = segmentObject.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;

        Image image = segmentObject.GetComponent<Image>();
        image.sprite = null;
        image.color = color;
        image.raycastTarget = false;
        image.gameObject.SetActive(false);
        _segments.Add(image);
    }
}
