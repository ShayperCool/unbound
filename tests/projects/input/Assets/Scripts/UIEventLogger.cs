using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>Records the EventSystem's events on a UI element.</summary>
public class UIEventLogger : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler,
    IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler,
    ISelectHandler, IDeselectHandler, IMoveHandler, ISubmitHandler, ICancelHandler
{
    void Pointer(string type, PointerEventData e)
    {
        ProbeLog.Add("ui", "type", ProbeLog.Q(type), "on", ProbeLog.Q(name),
            "pos", ProbeLog.V(e.position), "delta", ProbeLog.V(e.delta), "button", ProbeLog.Q(e.button.ToString()),
            "clicks", e.clickCount.ToString(), "scroll", ProbeLog.V(e.scrollDelta), "id", e.pointerId.ToString(),
            "press", ProbeLog.V(e.pressPosition), "dragging", e.dragging ? "true" : "false");
    }

    void Base(string type, BaseEventData e) => ProbeLog.Add("ui", "type", ProbeLog.Q(type), "on", ProbeLog.Q(name));

    public void OnPointerEnter(PointerEventData e) => Pointer("enter", e);
    public void OnPointerExit(PointerEventData e) => Pointer("exit", e);
    public void OnPointerDown(PointerEventData e) => Pointer("down", e);
    public void OnPointerUp(PointerEventData e) => Pointer("up", e);
    public void OnPointerClick(PointerEventData e) => Pointer("click", e);
    public void OnInitializePotentialDrag(PointerEventData e) => Pointer("potentialDrag", e);
    public void OnBeginDrag(PointerEventData e) => Pointer("beginDrag", e);
    public void OnDrag(PointerEventData e) => Pointer("drag", e);
    public void OnEndDrag(PointerEventData e) => Pointer("endDrag", e);
    public void OnScroll(PointerEventData e) => Pointer("scroll", e);
    public void OnSelect(BaseEventData e) => Base("select", e);
    public void OnDeselect(BaseEventData e) => Base("deselect", e);
    public void OnSubmit(BaseEventData e) => Base("submit", e);
    public void OnCancel(BaseEventData e) => Base("cancel", e);

    public void OnMove(AxisEventData e) =>
        ProbeLog.Add("ui", "type", ProbeLog.Q("move"), "on", ProbeLog.Q(name), "dir", ProbeLog.Q(e.moveDir.ToString()), "vector", ProbeLog.V(e.moveVector));
}
