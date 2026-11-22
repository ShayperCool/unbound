using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>Records the pointer events an object gets, under its name.</summary>
public class DriveTap : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
{
    public void OnPointerEnter(PointerEventData e) => DriveProbe.Event(name + ".enter");
    public void OnPointerExit(PointerEventData e) => DriveProbe.Event(name + ".exit");
    public void OnPointerDown(PointerEventData e) => DriveProbe.Event(name + ".down");
    public void OnPointerUp(PointerEventData e) => DriveProbe.Event(name + ".up");
    public void OnPointerClick(PointerEventData e) => DriveProbe.Event(name + ".click");
}

/// <summary>A swipe area: the direction of a drag from where it was pressed to where it ended.</summary>
public class DriveSwipe : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public void OnBeginDrag(PointerEventData e) => DriveProbe.Event(name + ".begin");

    public void OnDrag(PointerEventData e) => DriveProbe.Event(name + ".drag");

    public void OnEndDrag(PointerEventData e)
    {
        var d = e.position - e.pressPosition;
        string direction = Mathf.Abs(d.x) > Mathf.Abs(d.y) ? d.x > 0 ? "right" : "left" : d.y > 0 ? "up" : "down";
        DriveProbe.Event(name + ".end " + direction);
    }
}

/// <summary>An on-screen stick: the pointer's offset from where it was pressed, in halves of the
/// stick's smaller side, at most 1 long; what it was when the pointer was released.</summary>
public class DriveStick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    Vector2 m_Value;

    public void OnPointerDown(PointerEventData e)
    {
        m_Value = Vector2.zero;
        DriveProbe.Event(name + ".down");
    }

    public void OnDrag(PointerEventData e)
    {
        var corners = new Vector3[4];
        ((RectTransform)transform).GetWorldCorners(corners);
        float radius = Mathf.Min(corners[2].x - corners[0].x, corners[2].y - corners[0].y) * 0.5f;
        m_Value = Vector2.ClampMagnitude((e.position - e.pressPosition) / radius, 1f);
        DriveProbe.Event(name + ".drag");
    }

    public void OnPointerUp(PointerEventData e)
    {
        DriveProbe.Event(name + ".up " + DriveProbe.F2(m_Value.x) + "," + DriveProbe.F2(m_Value.y));
        m_Value = Vector2.zero;
    }
}

/// <summary>Records the pointer entering and leaving an object that holds others.</summary>
public class DriveHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public void OnPointerEnter(PointerEventData e) => DriveProbe.Event(name + ".enter");
    public void OnPointerExit(PointerEventData e) => DriveProbe.Event(name + ".exit");
}

/// <summary>Records the hover state passed to handlers on moves and at release.</summary>
public class DriveHoverState : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public void OnPointerEnter(PointerEventData e) => DriveProbe.Event(name + ".enter");
    public void OnPointerExit(PointerEventData e) =>
        DriveProbe.Event($"{name}.exit reentered={e.reentered} moved={e.IsPointerMoving()}");
}

/// <summary>Records an object taking and losing the selection.</summary>
public class DriveSelect : MonoBehaviour, ISelectHandler, IDeselectHandler
{
    public void OnSelect(BaseEventData e) => DriveProbe.Event(name + ".select");
    public void OnDeselect(BaseEventData e) => DriveProbe.Event(name + ".deselect");
}

#if UNITY_EDITOR
/// <summary>Supplies one scripted touch to the Editor's uGUI input module.</summary>
public class DriveProbeInputModule : StandaloneInputModule
{
    public void Sample(Vector2 position, TouchPhase phase)
    {
        var touch = new Touch { fingerId = 0, position = position, phase = phase };
        var pointer = GetTouchPointerEventData(touch, out bool pressed, out bool released);
        ProcessTouchPress(pointer, pressed, released);
        if (released)
            RemovePointerData(pointer);
        else
        {
            ProcessMove(pointer);
            ProcessDrag(pointer);
        }
    }
}
#endif
