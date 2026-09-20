using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Lightweight placeholder icon used by the four emote prefabs. Replace this
/// component with an Image when final artwork is available.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class EmoteCircleGraphic : MaskableGraphic
{
    [SerializeField, Range(8, 64)] private int _segments = 32;

    protected override void OnPopulateMesh(VertexHelper vertexHelper)
    {
        vertexHelper.Clear();
        Rect area = GetPixelAdjustedRect();
        float radius = Mathf.Min(area.width, area.height) * 0.5f;
        Vector2 center = area.center;

        UIVertex vertex = UIVertex.simpleVert;
        vertex.color = color;
        vertex.position = center;
        vertexHelper.AddVert(vertex);

        int segments = Mathf.Clamp(_segments, 8, 64);
        for (int index = 0; index <= segments; index++)
        {
            float angle = (index / (float)segments) * Mathf.PI * 2f;
            vertex.position = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            vertexHelper.AddVert(vertex);
        }

        for (int index = 1; index <= segments; index++)
            vertexHelper.AddTriangle(0, index, index + 1);
    }
}
