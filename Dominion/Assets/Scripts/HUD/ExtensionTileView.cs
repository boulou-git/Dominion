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

        LayoutElement layout = GetComponent<LayoutElement>();
        if (layout != null)
            layout.preferredHeight = 270f;

        if (_nameText != null)
            _nameText.text = string.IsNullOrEmpty(extension.name) ? extension.id : extension.name;
        if (_countText != null)
            _countText.text = (extension.cards != null ? extension.cards.Count : 0) + " cartes";

        if (_selectionVisual != null)
        {
            Sprite artworkSprite = ExtensionVisualLoader.LoadExtensionArtwork(extension);
            _selectionVisual.SetArtwork(artworkSprite);
            _selectionVisual.SetSelected(enabled);

            Image artwork = _selectionVisual.Artwork;
            if (artwork != null)
            {
                if (GetComponent<RectMask2D>() == null)
                    gameObject.AddComponent<RectMask2D>();
                AspectRatioFitter fitter = artwork.GetComponent<AspectRatioFitter>();
                if (fitter == null)
                    fitter = artwork.gameObject.AddComponent<AspectRatioFitter>();
                if (artworkSprite != null && artworkSprite.rect.height > 0f)
                    fitter.aspectRatio = artworkSprite.rect.width / artworkSprite.rect.height;
                fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                artwork.preserveAspect = false;
                artwork.raycastTarget = false;
            }
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
