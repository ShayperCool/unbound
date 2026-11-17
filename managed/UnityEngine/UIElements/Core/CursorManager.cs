namespace UnityEngine.UIElements
{
    /// <summary>Applies the USS cursor of the element under the mouse.</summary>
    internal partial interface ICursorManager
    {
        void SetCursor(Cursor cursor);
        void ResetCursor();
    }

    /// <summary>
    /// A panel's cursor: an element's texture cursor becomes the system cursor (Cursor.SetCursor);
    /// a cursor without a texture restores the default one, which a keyword cursor other than
    /// `arrow` cannot be on a runtime panel (reported). Matches the Unity 6000.0.71f1 Editor
    /// (tests/projects/uitkcursors).
    /// </summary>
    internal partial class CursorManager : ICursorManager
    {
        /// <summary>Whether a texture cursor is set (restoring the default one is then due).</summary>
        public bool isCursorOverriden { get; private set; }

        public CursorManager() { }

        public void SetCursor(Cursor cursor)
        {
            if (cursor.texture != null)
            {
                UnityEngine.Cursor.SetCursor(cursor.texture, cursor.hotspot, CursorMode.Auto);
                isCursorOverriden = true;
                return;
            }
            if (cursor.defaultCursorId != 0)
                Debug.LogWarning("Runtime cursors other than the default cursor need to be defined using a texture.");
            ResetCursor();
        }

        public void ResetCursor()
        {
            if (!isCursorOverriden) return;
            UnityEngine.Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            isCursorOverriden = false;
        }
    }

    internal abstract partial class BaseVisualElementPanel
    {
        internal virtual ICursorManager cursorManager { get; set; } = new CursorManager();
    }

    public partial class VisualElement
    {
        /// <summary>
        /// The cursor update a mouse event makes before it is dispatched to this element (its
        /// target): entering the element under the mouse takes its cursor, leaving it restores the
        /// default, unless another element holds the mouse capture; the end of a capture takes the
        /// cursor of the element under the mouse. A cursor style changed under a resting pointer is
        /// taken at the next of these events.
        /// </summary>
        internal void UpdateCursorStyle(long eventType)
        {
            var panel = elementPanel;
            if (panel == null) return;
            if (eventType == EventBase<MouseCaptureOutEvent>.TypeId())
            {
                var under = panel.GetTopElementUnderPointer(PointerId.mousePointerId);
                if (under != null) panel.cursorManager.SetCursor(under.computedValues.cursor);
                else panel.cursorManager.ResetCursor();
                return;
            }
            var capturing = panel.GetCapturingElement(PointerId.mousePointerId);
            if (capturing != null && capturing != this) return;
            if (eventType == EventBase<MouseOverEvent>.TypeId() && panel.GetTopElementUnderPointer(PointerId.mousePointerId) == this)
                panel.cursorManager.SetCursor(computedValues.cursor);
            else if (eventType == EventBase<MouseOutEvent>.TypeId() && capturing == null)
                panel.cursorManager.ResetCursor();
        }
    }
}
