using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Unbound.GameDrive
{
    /// <summary>
    /// One pointer pressed, moved and released on the UI, with the events uGUI's standalone input
    /// module sends for one touch, in its order (com.unity.ugui 2.0.0, StandaloneInputModule's
    /// touch processing with pointer hover sent to parents, the module's default):
    /// <list type="bullet">
    /// <item>press: the selection is dropped when the pressed object is not the selected one, the
    /// object under the pointer and its parents are entered, pointer down goes to the handler at or
    /// above the object, the drag handler gets its potential drag;</item>
    /// <item>move: the objects the pointer left get exit and those it came over enter, up to their
    /// common parent, else the hovered ones get pointer move; a movement past the EventSystem's
    /// drag threshold begins the drag, the press handler gets its up unless it is the drag handler
    /// too, and the drag handler gets the drags;</item>
    /// <item>release: pointer up, a click if it is still eligible and over its original click
    /// handler, a drop, the end of the drag, and exit to the first exit handler at
    /// or above the entered object.</item>
    /// </list>
    /// The events are sent with ExecuteEvents, so they depend on neither the window's focus, the
    /// input backend nor the module in use. A press forced onto a target (<see cref="Down"/>)
    /// treats that object as the one under the pointer until the release. Not sent: hover without
    /// a press, more than one pointer, scrolling, keyboard and navigation events.
    /// </summary>
    public sealed class DrivePointer
    {
        readonly List<RaycastResult> m_Hits = new List<RaycastResult>();
        PointerEventData m_Data;
        GameObject m_Forced;
        GraphicRaycaster m_ForcedRaycaster;

        public bool Pressed => m_Data != null;

        internal static GraphicRaycaster RaycasterFor(GameObject target) => target.GetComponentInParent<GraphicRaycaster>();

        /// <summary>Finds what is under the pointer, using the same event data throughout the touch.</summary>
        RaycastResult Raycast(EventSystem system, PointerEventData data)
        {
            if (system == null) return default;
            m_Hits.Clear();
            system.RaycastAll(data, m_Hits);
            var result = default(RaycastResult);
            foreach (var hit in m_Hits)
                if (hit.gameObject != null)
                {
                    result = hit;
                    break;
                }
            m_Hits.Clear();
            return result;
        }

        /// <summary>The object under the pointer now: the forced target, else what a raycast finds.</summary>
        RaycastResult Over(EventSystem system, PointerEventData data)
        {
            if (m_Forced != null)
                return new RaycastResult { gameObject = m_Forced, module = m_ForcedRaycaster, screenPosition = data.position };
            return Raycast(system, data);
        }

        /// <summary>Presses at a screen position. With <paramref name="target"/> the press goes to
        /// that object whatever is drawn over it; else to the object under the pointer. The
        /// optional callback reports the hit before dispatch. An empty hit can clear selection.
        /// Returns false without an EventSystem or while another press is active.</summary>
        public bool Down(Vector2 position, GameObject target = null, System.Action<GameObject> beforeDispatch = null)
        {
            var system = EventSystem.current;
            if (system == null || m_Data != null) return false;
            m_Forced = target;
            m_ForcedRaycaster = target != null ? RaycasterFor(target) : null;
            m_Data = new PointerEventData(system)
            {
                pointerId = 0,
                button = PointerEventData.InputButton.Left,
                position = position,
                pressPosition = position,
                delta = Vector2.zero,
                eligibleForClick = true,
                dragging = false,
                useDragThreshold = true,
            };
            var hit = Over(system, m_Data);
            var over = hit.gameObject;
            m_Data.pointerCurrentRaycast = hit;
            m_Data.pointerPressRaycast = hit;
            beforeDispatch?.Invoke(over);
            // A press elsewhere takes the selection away; a Selectable selects itself on its press.
            var selectable = ExecuteEvents.GetEventHandler<ISelectHandler>(over);
            if (selectable != system.currentSelectedGameObject) system.SetSelectedGameObject(null, m_Data);
            ExitAndEnter(over);
            var pressed = ExecuteEvents.ExecuteHierarchy(over, m_Data, ExecuteEvents.pointerDownHandler);
            var clicked = ExecuteEvents.GetEventHandler<IPointerClickHandler>(over);
            m_Data.clickCount = 1;
            m_Data.clickTime = Time.unscaledTime;
            m_Data.pointerPress = pressed != null ? pressed : clicked;
            m_Data.rawPointerPress = over;
            m_Data.pointerClick = clicked;
            m_Data.pointerDrag = ExecuteEvents.GetEventHandler<IDragHandler>(over);
            if (m_Data.pointerDrag != null) ExecuteEvents.Execute(m_Data.pointerDrag, m_Data, ExecuteEvents.initializePotentialDrag);
            return true;
        }

        /// <summary>Moves the pressed pointer to a screen position.</summary>
        public void Move(Vector2 position)
        {
            if (m_Data == null) return;
            m_Data.delta = position - m_Data.position;
            m_Data.position = position;
            var hit = Over(EventSystem.current, m_Data);
            m_Data.pointerCurrentRaycast = hit;
            ExitAndEnter(hit.gameObject);
            // A pointer that did not move drags nothing.
            if (m_Data.delta.sqrMagnitude <= 0f || m_Data.pointerDrag == null) return;
            if (!m_Data.dragging)
            {
                float threshold = EventSystem.current != null ? EventSystem.current.pixelDragThreshold : 0;
                if (m_Data.useDragThreshold && (m_Data.pressPosition - position).sqrMagnitude < threshold * threshold) return;
                ExecuteEvents.Execute(m_Data.pointerDrag, m_Data, ExecuteEvents.beginDragHandler);
                m_Data.dragging = true;
            }
            // Once dragged, a press handler that is not the drag handler is released without a click.
            if (m_Data.pointerPress != m_Data.pointerDrag)
            {
                ExecuteEvents.Execute(m_Data.pointerPress, m_Data, ExecuteEvents.pointerUpHandler);
                m_Data.eligibleForClick = false;
                m_Data.pointerPress = null;
                m_Data.rawPointerPress = null;
            }
            ExecuteEvents.Execute(m_Data.pointerDrag, m_Data, ExecuteEvents.dragHandler);
        }

        /// <summary>A frame in which the pressed pointer stays where it is: what is under it may
        /// have changed.</summary>
        public void Stay()
        {
            if (m_Data != null) Move(m_Data.position);
        }

        /// <summary>Releases the pointer where it is.</summary>
        public void Up()
        {
            if (m_Data == null) return;
            var data = m_Data;
            // Up samples the same position as the previous touch sample.
            data.delta = Vector2.zero;
            // What is under the pointer now decides the click and takes the drop.
            var hit = Over(EventSystem.current, data);
            var over = hit.gameObject;
            data.pointerCurrentRaycast = hit;
            ExecuteEvents.Execute(data.pointerPress, data, ExecuteEvents.pointerUpHandler);
            var clicked = over != null ? ExecuteEvents.GetEventHandler<IPointerClickHandler>(over) : null;
            if (data.eligibleForClick && data.pointerClick == clicked)
                ExecuteEvents.Execute(data.pointerClick, data, ExecuteEvents.pointerClickHandler);
            if (data.pointerDrag != null && data.dragging && over != null)
                ExecuteEvents.ExecuteHierarchy(over, data, ExecuteEvents.dropHandler);
            data.eligibleForClick = false;
            data.pointerPress = null;
            data.rawPointerPress = null;
            data.pointerClick = null;
            if (data.pointerDrag != null && data.dragging) ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.endDragHandler);
            data.dragging = false;
            data.pointerDrag = null;
            // A touch leaves when it is lifted: the first exit handler at or above the entered object.
            if (data.pointerEnter != null) ExecuteEvents.ExecuteHierarchy(data.pointerEnter, data, ExecuteEvents.pointerExitHandler);
            data.pointerEnter = null;
            m_Data = null;
            m_Forced = null;
            m_ForcedRaycaster = null;
        }

        /// <summary>The pointer is over <paramref name="target"/> now (null: over nothing): the
        /// objects it left, from the one entered up to the parent both share, get exit, and those
        /// it came over, from the target up to that parent, enter; an unchanged target's hovered
        /// objects get pointer move when the pointer moved.</summary>
        void ExitAndEnter(GameObject target)
        {
            var data = m_Data;
            if (target == null || data.pointerEnter == null)
            {
                for (int i = 0, n = data.hovered.Count; i < n; i++)
                {
                    data.fullyExited = true;
                    ExecuteEvents.Execute(data.hovered[i], data, ExecuteEvents.pointerMoveHandler);
                    ExecuteEvents.Execute(data.hovered[i], data, ExecuteEvents.pointerExitHandler);
                }
                data.hovered.Clear();
                if (target == null)
                {
                    data.pointerEnter = null;
                    return;
                }
            }
            if (data.pointerEnter == target)
            {
                if (data.delta.sqrMagnitude > 0f)
                    for (int i = 0, n = data.hovered.Count; i < n; i++)
                        ExecuteEvents.Execute(data.hovered[i], data, ExecuteEvents.pointerMoveHandler);
                return;
            }
            var shared = SharedParent(data.pointerEnter, target);
            if (data.pointerEnter != null)
                for (var t = data.pointerEnter.transform; t != null && t != shared; t = t.parent)
                {
                    data.fullyExited = true;
                    ExecuteEvents.Execute(t.gameObject, data, ExecuteEvents.pointerMoveHandler);
                    ExecuteEvents.Execute(t.gameObject, data, ExecuteEvents.pointerExitHandler);
                    data.hovered.Remove(t.gameObject);
                }
            data.pointerEnter = target;
            var entered = target.transform;
            // A child-to-parent move keeps the parent hovered and marks the return to it.
            if (entered == shared) data.reentered = true;
            for (var t = entered; t != null && t != shared; t = t.parent)
            {
                data.reentered = false;
                ExecuteEvents.Execute(t.gameObject, data, ExecuteEvents.pointerEnterHandler);
                ExecuteEvents.Execute(t.gameObject, data, ExecuteEvents.pointerMoveHandler);
                data.hovered.Add(t.gameObject);
            }
        }

        /// <summary>The nearest transform that holds both objects (one of them when it holds the
        /// other), or null.</summary>
        static Transform SharedParent(GameObject a, GameObject b)
        {
            if (a == null || b == null) return null;
            for (var t = a.transform; t != null; t = t.parent)
                for (var u = b.transform; u != null; u = u.parent)
                    if (t == u) return t;
            return null;
        }
    }
}
