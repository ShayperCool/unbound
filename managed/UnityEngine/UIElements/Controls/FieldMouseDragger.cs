using System;

namespace UnityEngine.UIElements
{
    /// <summary>A field whose value a drag can change (see <see cref="FieldMouseDragger{T}"/>).</summary>
    public partial interface IValueField<T>
    {
        void ApplyInputDeviceDelta(Vector3 delta, DeltaSpeed speed, T startValue);
        void StartDragging();
        void StopDragging();
        T value { get; set; }
    }

    /// <summary>Changes a value field by dragging a drag zone (usually the field's label).</summary>
    public abstract partial class BaseFieldMouseDragger
    {
        /// <summary>The whole element is the drag zone.</summary>
        public void SetDragZone(VisualElement dragElement) => SetDragZone(dragElement, new Rect(0, 0, -1, -1));

        public abstract void SetDragZone(VisualElement dragElement, Rect hotZone);
    }

    /// <summary>A left mouse press on the drag zone (any pointer in an Editor panel) captures the
    /// pointer and starts a drag from the field's value; each pointer move applies its delta
    /// through <see cref="IValueField{T}.ApplyInputDeviceDelta"/> (shift fast, alt slow); release
    /// ends the drag and Escape ends it restoring the start value. Measured, Unity 6000.0.71f1:
    /// tests/projects/sliderdrag.</summary>
    public partial class FieldMouseDragger<T> : BaseFieldMouseDragger
    {
        readonly IValueField<T> m_DrivenField;
        VisualElement m_DragElement;
        Rect m_DragHotZone;

        public FieldMouseDragger(IValueField<T> drivenField)
        {
            m_DrivenField = drivenField;
            m_DragElement = null;
            m_DragHotZone = new Rect(0, 0, -1, -1);
            dragging = false;
        }

        public bool dragging { get; set; }

        public T startValue { get; set; }

        public sealed override void SetDragZone(VisualElement dragElement, Rect hotZone)
        {
            if (m_DragElement != null)
            {
                m_DragElement.UnregisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
                m_DragElement.UnregisterCallback<PointerUpEvent>(OnPointerUp);
                m_DragElement.UnregisterCallback<KeyDownEvent>(OnKeyDown);
            }
            m_DragElement = dragElement;
            m_DragHotZone = hotZone;
            if (m_DragElement != null)
            {
                dragging = false;
                m_DragElement.RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
                m_DragElement.RegisterCallback<PointerUpEvent>(OnPointerUp);
                m_DragElement.RegisterCallback<KeyDownEvent>(OnKeyDown);
            }
        }

        bool CanStartDrag(int button, Vector2 localPosition) =>
            button == 0 && (m_DragHotZone.width < 0 || m_DragHotZone.height < 0
                || m_DragHotZone.Contains(m_DragElement.WorldToLocal(localPosition)));

        void OnPointerDown(PointerDownEvent evt)
        {
            if (!CanStartDrag(evt.button, evt.localPosition)) return;
            if (evt.pointerType == PointerType.mouse)
            {
                m_DragElement.CaptureMouse();
                StartDrag(evt);
            }
            else if (m_DragElement.panel.contextType == ContextType.Editor)
            {
                m_DragElement.CapturePointer(evt.pointerId);
                StartDrag(evt);
            }
        }

        void StartDrag(EventBase evt)
        {
            evt.StopPropagation();
            dragging = true;
            m_DragElement.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            startValue = m_DrivenField.value;
            m_DrivenField.StartDragging();
            // The Editor also lets the cursor wrap at the screen's edges while dragging; there is no
            // such cursor here.
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            if (!dragging) return;
            var speed = evt.shiftKey ? DeltaSpeed.Fast : evt.altKey ? DeltaSpeed.Slow : DeltaSpeed.Normal;
            m_DrivenField.ApplyInputDeviceDelta(evt.deltaPosition, speed, startValue);
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            if (!dragging) return;
            dragging = false;
            m_DragElement.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
            m_DragElement.ReleasePointer(evt.pointerId);
            if (evt is IMouseEvent) m_DragElement.panel.ProcessPointerCapture(PointerId.mousePointerId);
            m_DrivenField.StopDragging();
        }

        void OnKeyDown(KeyDownEvent evt)
        {
            if (!dragging || evt.keyCode != KeyCode.Escape) return;
            // The move callback stays registered; moves after this find the drag ended.
            dragging = false;
            m_DrivenField.value = startValue;
            m_DrivenField.StopDragging();
            (evt.target as VisualElement)?.panel.ReleasePointer(PointerId.mousePointerId);
        }
    }
}
