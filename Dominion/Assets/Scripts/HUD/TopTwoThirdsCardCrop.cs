using UnityEngine;

/// <summary>
/// Keeps a full 2:3 card artwork aligned to the top of a square masked viewport.
/// The viewport therefore displays exactly the upper two thirds of the source card.
/// Anchors and the visible square remain authored in SupplyCard.prefab.
/// </summary>
[ExecuteAlways]
public sealed class TopTwoThirdsCardCrop : MonoBehaviour
{
    [SerializeField] private RectTransform _viewport;
    [SerializeField] private RectTransform _artwork;
    [SerializeField, Min(1f)] private float _fullArtworkHeightToWidth = 1.5f;

    public float FullArtworkHeightToWidth => _fullArtworkHeightToWidth;

    private void OnEnable()
    {
        RefreshCrop();
    }

    private void OnRectTransformDimensionsChange()
    {
        RefreshCrop();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        RefreshCrop();
    }
#endif

    public void RefreshCrop()
    {
        if (_viewport == null || _artwork == null)
            return;

        float width = _viewport.rect.width;
        if (width <= 0f)
            width = ((RectTransform)transform).rect.width;
        if (width <= 0f)
            return;

        Vector2 size = _artwork.sizeDelta;
        size.y = width * _fullArtworkHeightToWidth;
        _artwork.sizeDelta = size;
    }
}
