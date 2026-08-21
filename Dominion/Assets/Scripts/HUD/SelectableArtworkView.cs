using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Applies the common selected/unselected treatment used by extension and card tiles.
/// Unselected artwork is grayscale + dimmed; selected artwork remains full colour.
/// </summary>
public sealed class SelectableArtworkView : MonoBehaviour
{
    [SerializeField] private Image _artwork;
    [SerializeField, Range(0f, 1f)] private float _unselectedBrightness = 0.48f;
    [SerializeField] private Color _selectedTint = Color.white;

    private Material _grayscaleMaterial;

    public Image Artwork => _artwork;

    private void Awake()
    {
        EnsureMaterial();
    }

    private void OnDestroy()
    {
        if (_grayscaleMaterial != null)
            Destroy(_grayscaleMaterial);
    }

    public void SetArtwork(Sprite sprite)
    {
        if (_artwork == null)
            return;

        _artwork.sprite = sprite;
        _artwork.enabled = sprite != null;
        _artwork.preserveAspect = false;
    }

    public void SetSelected(bool selected)
    {
        if (_artwork == null)
            return;

        if (selected)
        {
            _artwork.material = null;
            _artwork.color = _selectedTint;
            return;
        }

        EnsureMaterial();
        _artwork.material = _grayscaleMaterial;
        _artwork.color = Color.white;
    }

    private void EnsureMaterial()
    {
        if (_grayscaleMaterial != null)
            return;

        Shader shader = Shader.Find("Dominion/UI/GrayscaleDim");
        if (shader == null)
        {
            Debug.LogWarning("Dominion grayscale UI shader was not found. Unselected artwork will only be dimmed.");
            if (_artwork != null)
                _artwork.color = new Color(_unselectedBrightness, _unselectedBrightness, _unselectedBrightness, 1f);
            return;
        }

        _grayscaleMaterial = new Material(shader)
        {
            name = "DominionGrayscaleDim (Runtime)"
        };
        _grayscaleMaterial.SetFloat("_Dim", _unselectedBrightness);
    }
}
