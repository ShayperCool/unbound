using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

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
    /// <item>release: pointer up, the click when the pointer is over the click handler it pressed
    /// and nothing was dragged, a drop, the end of the drag, and exit to the first exit handler at
    /// or above the entered object.</item>
    /// </list>
    /// The events are sent with ExecuteEvents, so they depend on neither the window's focus, the
    /// input backend nor the module in use. A press forced onto a target (<see cref="Down"/>)
    /// treats that object as the one under the pointer until the release. Not sent: hover without
    /// a press, more than one pointer, scrolling, keyboard and navigation events.
    /// </summary>
    public sealed class DrivePointer
    {
        static readonly List<RaycastResult> s_Hits = new List<RaycastResult>();

        PointerEventData m_Data;
        GameObject m_Forced;

        public bool Pressed => m_Data != null;

        /// <summary>The topmost UI object at a screen position (pixels from the bottom left), or null.</summary>
        public static GameObject ObjectAt(Vector2 position, out RaycastResult hit)
        {
            hit = default;
            var system = EventSystem.current;
            if (system == null) return null;
            var data = new PointerEventData(system) { position = position };
            s_Hits.Clear();
            system.RaycastAll(data, s_Hits);
            foreach (var h in s_Hits)
                if (h.gameObject != null)
                {
                    hit = h;
                    return h.gameObject;
                }
            return null;
        }

        /// <summary>The object under the pointer now: the forced target, else what a raycast finds.</summary>
        GameObject Over(Vector2 position, out RaycastResult hit)
        {
            if (m_Forced != null)
            {
                hit = new RaycastResult { gameObject = m_Forced, screenPosition = position };
                return m_Forced;
            }
            var over = ObjectAt(position, out hit);
            if (over == null) hit = new RaycastResult { screenPosition = position };
            return over;
        }

        /// <summary>Presses at a screen position. With <paramref name="target"/> the press goes to
        /// that object whatever is drawn over it; else to the object under the pointer. False when
        /// there is no EventSystem or nothing to press.</summary>
        public bool Down(Vector2 position, GameObject target = null)
        {
            var system = EventSystem.current;
            if (system == null || m_Data != null) return false;
            m_Forced = target;
            var over = Over(position, out var hit);
            if (over == null)
            {
                m_Forced = null;
                return false;
            }
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
                pointerCurrentRaycast = hit,
                pointerPressRaycast = hit,
            };
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
            var over = Over(position, out var hit);
            m_Data.pointerCurrentRaycast = hit;
            ExitAndEnter(over);
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
            // What is under the pointer now decides the click and takes the drop.
            var over = Over(data.position, out var hit);
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
            for (var t = target.transform; t != null && t != shared; t = t.parent)
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
