using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Pure visual references for the editable pre-game Kingdom reveal prefab.
/// The lobby controller owns the network/data logic; this component only exposes
/// the editable Unity UI targets used to render the ten selected Kingdom cards.
/// </summary>
public sealed class KingdomRevealScreenView : MonoBehaviour
{
    [SerializeField] private RectTransform _cardsRoot;
    [SerializeField] private Text _statusText;
    [SerializeField] private Button _startButton;

    public RectTransform CardsRoot => _cardsRoot;
    public Text StatusText => _statusText;
    public Button StartButton => _startButton;
}
