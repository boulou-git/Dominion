using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Stable selectable Kingdom card tile used by the lobby.
/// The rendered card PNG is the visual itself; the GridLayoutGroup remains the sole owner
/// of item ordering/positioning. Hover must never reorder siblings or move grid items.
/// </summary>
public sealed class CardSelectionTileView : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private SelectableArtworkView _selectionVisual;
    [SerializeField] private Text _nameText;
    [SerializeField] private Text _detailsText;
    [SerializeField] private Toggle _selectedToggle;

    private Action<bool> _toggleCallback;
    private bool _interactable;
    private bool _hasArtwork;

    public void Bind(ExtensionPackageData extension, ExtensionCardData card, bool selected, bool interactable, Action<bool> toggle)
    {
        if (card == null)
            return;

        _toggleCallback = toggle;
        _interactable = interactable;

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
                artRect.anchorMin = new Vector2(0.02f, 0.02f);
                artRect.anchorMax = new Vector2(0.98f, 0.98f);
                artRect.offsetMin = Vector2.zero;
                artRect.offsetMax = Vector2.zero;
                image.preserveAspect = true;
                image.raycastTarget = true;
                DynamicCardCostView.Attach(image.gameObject, card);
            }
        }

        // A complete rendered card already contains its own name/rules.
        // Keep labels only as a fallback while an image is missing.
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
            }

            // Reveal tiles are display-only, so they do not need a disabled checkbox overlay.
            bool displayOnly = toggle == null;
            _selectedToggle.gameObject.SetActive(!displayOnly);
            _selectedToggle.interactable = interactable;
            _selectedToggle.SetIsOnWithoutNotify(selected);
            _selectedToggle.onValueChanged.RemoveAllListeners();
            _selectedToggle.onValueChanged.AddListener(OnToggleChanged);
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!_interactable || eventData.button != PointerEventData.InputButton.Left || _selectedToggle == null)
            return;

        // If the click already landed on the Toggle itself, let Toggle process it once.
        GameObject hit = eventData.pointerPressRaycast.gameObject;
        if (hit != null && hit.transform.IsChildOf(_selectedToggle.transform))
            return;

        _selectedToggle.isOn = !_selectedToggle.isOn;
    }

    private void OnToggleChanged(bool selected)
    {
        // Update locally first so the click feels immediate even before Photon echoes room state.
        if (_selectionVisual != null)
            _selectionVisual.SetSelected(selected);

        _toggleCallback?.Invoke(selected);
    }
}
