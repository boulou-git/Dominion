using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Selectable Kingdom card tile used by the lobby.
/// When a rendered card PNG exists, the PNG itself becomes the tile: it keeps the locked
/// 59:91 aspect ratio, fills almost the whole item and receives the selected/unselected treatment.
/// </summary>
public sealed class CardSelectionTileView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private SelectableArtworkView _selectionVisual;
    [SerializeField] private Text _nameText;
    [SerializeField] private Text _detailsText;
    [SerializeField] private Toggle _selectedToggle;
    [SerializeField] private float _hoverScale = 1.06f;
    [SerializeField] private float _hoverSpeed = 14f;

    private bool _suppressToggle;
    private bool _hasArtwork;
    private Vector3 _targetScale = Vector3.one;

    private void Update()
    {
        float t = 1f - Mathf.Exp(-_hoverSpeed * Time.unscaledDeltaTime);
        transform.localScale = Vector3.Lerp(transform.localScale, _targetScale, t);
    }

    public void Bind(ExtensionPackageData extension, ExtensionCardData card, bool selected, bool interactable, Action<bool> toggle)
    {
        if (card == null)
            return;

        Sprite artwork = ExtensionVisualLoader.LoadCardArtwork(extension, card);
        _hasArtwork = artwork != null;

        if (_selectionVisual != null)
        {
            _selectionVisual.SetArtwork(artwork);
            _selectionVisual.SetSelected(selected);

            Image image = _selectionVisual.Artwork;
            if (image != null)
            {
                RectTransform artRect = image.rectTransform;
                // A rendered card already contains its artwork, name and rules text, so it is
                // displayed essentially full-frame. A tiny inset leaves room for hover scaling.
                artRect.anchorMin = new Vector2(0.02f, 0.02f);
                artRect.anchorMax = new Vector2(0.98f, 0.98f);
                artRect.offsetMin = Vector2.zero;
                artRect.offsetMax = Vector2.zero;
                image.preserveAspect = true;
            }
        }

        // PNG cards already contain their printed name/rules. Keep the old labels only as
        // a useful fallback when no image has been supplied yet.
        if (_nameText != null)
        {
            _nameText.gameObject.SetActive(!_hasArtwork);
            _nameText.text = card.name;
        }

        if (_detailsText != null)
        {
            _detailsText.gameObject.SetActive(!_hasArtwork);
            string types = card.types == null ? string.Empty : string.Join(" • ", card.types);
            _detailsText.text = card.cost + "  •  " + types;
        }

        if (_selectedToggle != null)
        {
            RectTransform toggleRect = _selectedToggle.GetComponent<RectTransform>();
            if (toggleRect != null && _hasArtwork)
            {
                toggleRect.anchorMin = new Vector2(0.78f, 0.80f);
                toggleRect.anchorMax = new Vector2(0.95f, 0.96f);
                toggleRect.offsetMin = Vector2.zero;
                toggleRect.offsetMax = Vector2.zero;
                _selectedToggle.transform.SetAsLastSibling();
            }

            _suppressToggle = true;
            _selectedToggle.isOn = selected;
            _selectedToggle.interactable = interactable;
            _suppressToggle = false;
            _selectedToggle.onValueChanged.RemoveAllListeners();
            _selectedToggle.onValueChanged.AddListener(value =>
            {
                if (!_suppressToggle)
                    toggle?.Invoke(value);
            });
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!_hasArtwork)
            return;

        _targetScale = Vector3.one * _hoverScale;
        transform.SetAsLastSibling();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _targetScale = Vector3.one;
    }
}
