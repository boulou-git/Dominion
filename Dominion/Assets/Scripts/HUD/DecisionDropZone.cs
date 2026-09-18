using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class DecisionDropZone : MonoBehaviour, IDropHandler, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private Color _hoverColor;
    private Image _image;
    private Color _normalColor;
    private bool _highlighted;
    public DecisionWorkspaceView Owner;
    public bool Select;
    public string OptionId;

    public void OnDrop(PointerEventData e)
    {
        RestoreColor();
        DecisionDragCard card = e.pointerDrag != null ? e.pointerDrag.GetComponent<DecisionDragCard>() : null;
        if (card == null || card.Owner != Owner || !Owner.CanInteract(card.DecisionId)) return;
        Owner.Drop(card.InstanceId, Select, OptionId);
    }

    public void OnPointerEnter(PointerEventData e)
    {
        DecisionDragCard card = e.pointerDrag != null ? e.pointerDrag.GetComponent<DecisionDragCard>() : null;
        if (card == null || card.Owner != Owner || !Owner.CanDrop(card.InstanceId, Select, OptionId)) return;
        _image = GetComponent<Image>();
        if (_image == null || _highlighted) return;
        _normalColor = _image.color;
        _image.color = _hoverColor;
        _highlighted = true;
    }

    public void OnPointerExit(PointerEventData e) => RestoreColor();
    private void OnDisable() => RestoreColor();
    private void RestoreColor()
    {
        if (_highlighted && _image != null) _image.color = _normalColor;
        _highlighted = false;
    }
}
