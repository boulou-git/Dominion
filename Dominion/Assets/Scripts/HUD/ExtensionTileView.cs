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

    private Action<bool> _toggleCallback;

    public void Bind(ExtensionPackageData extension, bool enabled, Action open, Action<bool> toggle)
    {
        if (extension == null)
            return;

        _toggleCallback = toggle;

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
            _enabledToggle.SetIsOnWithoutNotify(enabled);
            _enabledToggle.onValueChanged.RemoveAllListeners();
            _enabledToggle.onValueChanged.AddListener(OnToggleChanged);
        }

        if (_openButton != null)
        {
            _openButton.onClick.RemoveAllListeners();
            _openButton.onClick.AddListener(() => open?.Invoke());
        }
    }

    private void OnToggleChanged(bool enabled)
    {
        if (_selectionVisual != null)
            _selectionVisual.SetSelected(enabled);

        _toggleCallback?.Invoke(enabled);
    }
}
