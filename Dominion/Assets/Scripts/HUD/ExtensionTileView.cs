using System;
using UnityEngine;
using UnityEngine.UI;

public sealed class ExtensionTileView : MonoBehaviour
{
    [SerializeField] private SelectableArtworkView _selectionVisual;
    [SerializeField] private Text _nameText;
    [SerializeField] private Text _countText;
    [SerializeField] private Toggle _enabledToggle;
    [SerializeField] private Button _openButton;

    private bool _suppressToggle;

    public void Bind(ExtensionPackageData extension, bool enabled, Action open, Action<bool> toggle)
    {
        if (extension == null)
            return;

        if (_nameText != null)
            _nameText.text = string.IsNullOrEmpty(extension.name) ? extension.id : extension.name;
        if (_countText != null)
            _countText.text = (extension.cards != null ? extension.cards.Count : 0) + " cartes";

        if (_selectionVisual != null)
        {
            _selectionVisual.SetArtwork(ExtensionVisualLoader.LoadExtensionArtwork(extension));
            _selectionVisual.SetSelected(enabled);
        }

        if (_enabledToggle != null)
        {
            _suppressToggle = true;
            _enabledToggle.isOn = enabled;
            _suppressToggle = false;
            _enabledToggle.onValueChanged.RemoveAllListeners();
            _enabledToggle.onValueChanged.AddListener(value =>
            {
                if (!_suppressToggle)
                    toggle?.Invoke(value);
            });
        }

        if (_openButton != null)
        {
            _openButton.onClick.RemoveAllListeners();
            _openButton.onClick.AddListener(() => open?.Invoke());
        }
    }
}
