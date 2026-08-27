using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Turns a prefab-authored GridLayoutGroup into a clipped, vertically scrollable grid.
/// Cell size, spacing, padding and column count remain owned by the prefab.
/// </summary>
[RequireComponent(typeof(RectTransform), typeof(GridLayoutGroup))]
public sealed class DecisionScrollGrid : MonoBehaviour, IScrollHandler, IBeginDragHandler, IDragHandler
{
    [SerializeField] private float _wheelSpeed = 48f;

    private RectTransform _viewport;
    private RectTransform _content;
    private GridLayoutGroup _contentGrid;
    private ContentSizeFitter _contentFitter;
    private float _offset;

    public RectTransform Content
    {
        get
        {
            EnsureContent();
            return _content;
        }
    }

    private void Awake()
    {
        EnsureContent();
    }

    private void OnEnable()
    {
        RefreshLayout(true);
    }

    public void RefreshLayout(bool resetToTop = false)
    {
        EnsureContent();
        if (_content == null || _contentGrid == null)
            return;

        if (resetToTop)
            _offset = 0f;

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
        ClampAndApplyOffset();
    }

    public void OnScroll(PointerEventData eventData)
    {
        _offset -= eventData.scrollDelta.y * _wheelSpeed;
        ClampAndApplyOffset();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
    }

    public void OnDrag(PointerEventData eventData)
    {
        _offset += eventData.delta.y;
        ClampAndApplyOffset();
    }

    private void EnsureContent()
    {
        if (_content != null)
            return;

        _viewport = transform as RectTransform;
        if (_viewport == null)
            return;

        GridLayoutGroup authoredGrid = GetComponent<GridLayoutGroup>();
        if (authoredGrid == null)
            return;

        if (GetComponent<RectMask2D>() == null)
            gameObject.AddComponent<RectMask2D>();

        Transform existing = transform.Find("Content");
        GameObject contentObject;
        if (existing != null)
            contentObject = existing.gameObject;
        else
        {
            contentObject = new GameObject("Content", typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
            contentObject.transform.SetParent(transform, false);
        }

        _content = contentObject.GetComponent<RectTransform>();
        _content.anchorMin = new Vector2(0f, 1f);
        _content.anchorMax = new Vector2(1f, 1f);
        _content.pivot = new Vector2(0.5f, 1f);
        _content.anchoredPosition = Vector2.zero;
        _content.sizeDelta = Vector2.zero;

        _contentGrid = contentObject.GetComponent<GridLayoutGroup>();
        CopyGrid(authoredGrid, _contentGrid);
        authoredGrid.enabled = false;

        _contentFitter = contentObject.GetComponent<ContentSizeFitter>();
        _contentFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        _contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    private void ClampAndApplyOffset()
    {
        if (_viewport == null || _content == null)
            return;

        Canvas.ForceUpdateCanvases();
        float maxOffset = Mathf.Max(0f, _content.rect.height - _viewport.rect.height);
        _offset = Mathf.Clamp(_offset, 0f, maxOffset);
        _content.anchoredPosition = new Vector2(0f, _offset);
    }

    private static void CopyGrid(GridLayoutGroup source, GridLayoutGroup destination)
    {
        destination.padding = new RectOffset(source.padding.left, source.padding.right, source.padding.top, source.padding.bottom);
        destination.cellSize = source.cellSize;
        destination.spacing = source.spacing;
        destination.startCorner = source.startCorner;
        destination.startAxis = source.startAxis;
        destination.childAlignment = source.childAlignment;
        destination.constraint = source.constraint;
        destination.constraintCount = source.constraintCount;
    }
}
