using System;
using UnityEngine;
using UnityEngine.UI;

public sealed class CardSelectionTileView : MonoBehaviour
{
    [SerializeField] private SelectableArtworkView _selectionVisual;
    [SerializeField] private Text _nameText;
    [SerializeField] private Text _detailsText;
    [SerializeField] private Toggle _selectedToggle;

    private bool _suppressToggle;

    public void Bind(ExtensionPackageData extension, ExtensionCardData card, bool selected, bool interactable, Action<bool> toggle)
    {
        if (card == null)
            return;

        if (_nameText != null)
            _nameText.text = card.name;

        if (_detailsText != null)
        {
            string types = card.types == null ? string.Empty : string.Join(" • ", card.types);
            _detailsText.text = card.cost + "  •  " + types;
        }

        if (_selectionVisual != null)
        {
            _selectionVisual.SetArtwork(ExtensionVisualLoader.LoadCardArtwork(extension, card));
            _selectionVisual.SetSelected(selected);
        }

        if (_selectedToggle != null)
        {
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
}
