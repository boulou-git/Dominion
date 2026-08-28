using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Sizes the zoom surface to the source image ratio inside a prefab-authored maximum.
/// Portrait cards and horizontal 3:1 Artefacts therefore both use the available screen.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(RectTransform), typeof(Image))]
public sealed class AdaptiveCardZoomView : MonoBehaviour
{
    [SerializeField] private Vector2 _maximumSize = new Vector2(920f, 800f);

    private Image _image;
    private Sprite _lastSprite;

    public Vector2 MaximumSize => _maximumSize;

    private void OnEnable()
    {
        RefreshSize();
    }

    private void LateUpdate()
    {
        EnsureImage();
        if (_image != null && _image.sprite != _lastSprite)
            RefreshSize();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        RefreshSize();
    }
#endif

    public void RefreshSize()
    {
        EnsureImage();
        if (_image == null || _image.sprite == null)
            return;

        _lastSprite = _image.sprite;
        Rect source = _lastSprite.rect;
        if (source.width <= 0f || source.height <= 0f)
            return;

        float aspect = source.width / source.height;
        float width = Mathf.Max(1f, _maximumSize.x);
        float height = width / aspect;
        if (height > _maximumSize.y)
        {
            height = Mathf.Max(1f, _maximumSize.y);
            width = height * aspect;
        }

        ((RectTransform)transform).sizeDelta = new Vector2(width, height);
    }

    private void EnsureImage()
    {
        if (_image == null)
            _image = GetComponent<Image>();
    }
}
