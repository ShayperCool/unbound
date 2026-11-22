using System;

namespace UnityEngine.UIElements
{
    /// <summary>
    /// An IMGUIContainer runs its handler as the Unity 6000.0.71f1 Editor does in a runtime panel
    /// (tests/projects/imguicontainer): a Layout pass measures it when its size is not fixed (and
    /// again once <see cref="MarkDirtyLayout"/> is called), and a UI Toolkit event that carries an
    /// IMGUI event (a mouse or key event) reaches the handler after a Layout pass, in the
    /// container's coordinates. A runtime panel sends it no Repaint, so it draws nothing there, and
    /// every panel of Unbound is one. The handler has its own control IDs and layout; the container
    /// takes the panel's focus when one of its controls takes the keyboard.
    /// Not done: Tab does not move between a container's controls (Unbound's IMGUI has no Tab
    /// traversal), and a container under a scaled or rotated element gets its position only.
    /// </summary>
    public partial class IMGUIContainer
    {
        // Unique scopes for the containers' control IDs, apart from behaviours' instance IDs.
        static int s_NextScope = int.MinValue;
        static readonly Event s_MeasureEvent = new Event { type = EventType.Layout };

        int m_Scope;
        GUILayoutUtility.LayoutCache m_Cache;
        bool m_HasFocusableControls;
        // Where the container was when it was last painted or sent an event: nothing before that.
        Vector2 m_CachedOrigin;
        Rect m_CachedClip;
        Rect m_PaintedClip;
        // A blur or focus event acts on the IMGUI state in the handler's next call.
        bool m_LostFocus, m_ReceivedFocus, m_IsFocusDelegated;
        FocusChangeDirection m_FocusChangeDirection = FocusChangeDirection.unspecified;
        int m_NewKeyboardFocusControlID;
        Action m_LayoutAgain;

        GUILayoutUtility.LayoutCache cache => m_Cache ??= new GUILayoutUtility.LayoutCache();

        float layoutMeasuredWidth => Mathf.Ceil(cache.topLevel.maxWidth);

        float layoutMeasuredHeight => Mathf.Ceil(cache.topLevel.maxHeight);

        int Scope => m_Scope != 0 ? m_Scope : m_Scope = ++s_NextScope;

        internal bool focusOnlyIfHasFocusableControls { get; set; } = true;

        public override bool canGrabFocus => focusOnlyIfHasFocusableControls ? m_HasFocusableControls && base.canGrabFocus : base.canGrabFocus;

        /// <summary>Measures the container again: every event already comes after a Layout pass.</summary>
        public void MarkDirtyLayout()
        {
            IncrementVersion(VersionChangeType.Layout);
        }

        // Painting draws nothing (no Repaint event in a runtime panel) and records the clip.
        void OnGenerateVisualContent(MeshGenerationContext mgc)
        {
            if (elementPanel is BaseRuntimePanel { drawsInCameras: true }) return;
            m_PaintedClip = worldBound;
            m_CachedClip = m_PaintedClip;
            m_CachedOrigin = WorldOrigin();
        }

        Vector2 WorldOrigin()
        {
            var transform = worldTransform;
            return new Vector2(transform.m03, transform.m13);
        }

        // Its clip when last painted; its bounds before that.
        Rect CurrentClip() => m_PaintedClip.width == 0f || m_PaintedClip.height == 0f ? worldBound : m_PaintedClip;

        // The GUI state the handler may change, kept for the code around it.
        struct GUIGlobals
        {
            public Matrix4x4 matrix;
            public Color color, contentColor, backgroundColor;
            public bool enabled, changed;
        }

        static GUIGlobals SaveGlobals() => new GUIGlobals
        {
            matrix = GUI.matrix, color = GUI.color, contentColor = GUI.contentColor, backgroundColor = GUI.backgroundColor,
            enabled = GUI.enabled, changed = GUI.changed,
        };

        static void RestoreGlobals(GUIGlobals g)
        {
            GUI.matrix = g.matrix;
            GUI.color = g.color;
            GUI.contentColor = g.contentColor;
            GUI.backgroundColor = g.backgroundColor;
            GUI.enabled = g.enabled;
            GUI.changed = g.changed;
        }

        void DoOnGUI(Event evt, Vector2 origin, Rect clip, bool isComputingLayout, Rect layoutSize, bool canAffectFocus = true)
        {
            var handler = m_OnGUIHandler;
            if (handler == null || panel == null) return;
            var outerEvent = Event.current;
            // Inside another GUI pass (a container measured from an OnGUI) that pass's state is
            // kept; its matrix is read before this pass resets the clips.
            var globals = SaveGlobals();
            float measuredWidth = layoutMeasuredWidth, measuredHeight = layoutMeasuredHeight;
            Event.current = evt;
            var outer = GUIUtility.BeginContainerGUI(Scope, cache, origin, clip, nested: outerEvent != null);
            GUI.enabled = enabledInHierarchy;
            var type = evt.type;
            if (type != EventType.Layout) ApplyFocusEvents();
            bool exited = false;
            try
            {
                handler();
            }
            catch (Exception exception)
            {
                // A Layout pass never interrupts the panel's layout or the event that follows it.
                if (type != EventType.Layout) throw;
                exited = GUIUtility.IsExitGUIException(exception);
                if (!exited) Debug.LogException(exception);
            }
            finally
            {
                if (evt.type != EventType.Layout && canAffectFocus)
                {
                    SyncPanelFocus(type);
                    m_HasFocusableControls = GUIUtility.HasFocusableControls();
                }
                bool reportClips = !exited && evt.rawType != EventType.Ignore && evt.rawType != EventType.Used;
                GUIUtility.EndContainerGUI(outer, new Rect(0f, 0f, Defined(layoutSize.width, cache.topLevel.maxWidth), Defined(layoutSize.height, cache.topLevel.maxHeight)), reportClips);
                Event.current = outerEvent;
                RestoreGlobals(globals);
            }
            if (evt.type == EventType.Layout
                && (!Mathf.Approximately(measuredWidth, layoutMeasuredWidth) || !Mathf.Approximately(measuredHeight, layoutMeasuredHeight)))
            {
                // A Layout pass for an event that changes the measured size lays the panel out
                // again. A measure returns its size itself: only one before the container's first
                // paint is followed by another layout, a frame later (auto.frame2, late.frame2;
                // after a paint auto.markedDirty has one pass).
                if (!isComputingLayout) IncrementVersion(VersionChangeType.Layout);
                else if (clip == Rect.zero) schedule.Execute(m_LayoutAgain ??= () => IncrementVersion(VersionChangeType.Layout));
            }
            if (evt.type == EventType.Used) IncrementVersion(VersionChangeType.Repaint);
        }

        static float Defined(float value, float fallback) => float.IsNaN(value) ? fallback : value;

        // Before the handler: a blur takes the keyboard from this container's control, a focus
        // gives it to the first or last one (the direction focus moved in) and tells the panel.
        void ApplyFocusEvents()
        {
            var controller = focusController;
            if (m_LostFocus)
            {
                if (controller != null && GUIUtility.OwnsId(GUIUtility.keyboardControl))
                {
                    GUIUtility.keyboardControl = 0;
                    controller.imguiKeyboardControl = 0;
                }
                m_LostFocus = false;
            }
            if (!m_ReceivedFocus) return;
            bool directed = m_FocusChangeDirection != FocusChangeDirection.unspecified && m_FocusChangeDirection != FocusChangeDirection.none;
            if (m_HasFocusableControls)
            {
                if (directed)
                {
                    if (m_FocusChangeDirection == VisualElementFocusChangeDirection.left) GUIUtility.SetKeyboardControlToLastControlId();
                    else if (m_FocusChangeDirection == VisualElementFocusChangeDirection.right) GUIUtility.SetKeyboardControlToFirstControlId();
                }
                else if (GUIUtility.keyboardControl == 0 && m_IsFocusDelegated) GUIUtility.SetKeyboardControlToFirstControlId();
            }
            if (controller != null)
            {
                if (controller.imguiKeyboardControl != GUIUtility.keyboardControl && m_FocusChangeDirection != FocusChangeDirection.unspecified)
                    m_NewKeyboardFocusControlID = GUIUtility.keyboardControl;
                controller.imguiKeyboardControl = GUIUtility.keyboardControl;
            }
            m_ReceivedFocus = false;
            m_FocusChangeDirection = FocusChangeDirection.unspecified;
        }

        // After the handler: the panel's focus follows the control that has the keyboard. A mouse
        // press focuses the container when a control took the keyboard and blurs the panel when
        // none did.
        void SyncPanelFocus(EventType type)
        {
            var controller = focusController;
            if (controller == null) return;
            int keyboard = GUIUtility.keyboardControl;
            if (type == EventType.MouseDown) controller.SyncIMGUIFocus(keyboard, this, !focusOnlyIfHasFocusableControls);
            else if (keyboard != controller.imguiKeyboardControl)
            {
                m_NewKeyboardFocusControlID = keyboard;
                if (controller.GetLeafFocusedElement() == this) controller.imguiKeyboardControl = keyboard;
                else controller.SyncIMGUIFocus(keyboard, this, false);
            }
        }

        // Mouse moves and window enter/leave reach a container only when its panel asks for them;
        // a runtime panel does not.
        static bool WantsEvent(EventType type) =>
            type != EventType.MouseMove && type != EventType.MouseEnterWindow && type != EventType.MouseLeaveWindow;

        internal bool HandleIMGUIEvent(Event e, bool canAffectFocus)
        {
            if (e == null || m_OnGUIHandler == null || elementPanel == null || !WantsEvent(e.rawType)) return false;
            m_CachedOrigin = WorldOrigin();
            m_CachedClip = CurrentClip();
            var rawType = e.rawType;
            if (rawType != EventType.Layout)
            {
                // Each event comes after a Layout pass.
                e.type = EventType.Layout;
                DoOnGUI(e, m_CachedOrigin, m_CachedClip, false, layout, canAffectFocus);
                e.type = rawType;
            }
            DoOnGUI(e, m_CachedOrigin, m_CachedClip, false, layout, canAffectFocus);
            if (m_NewKeyboardFocusControlID > 0)
            {
                // The controls hear that the keyboard went to another one.
                m_NewKeyboardFocusControlID = 0;
                HandleIMGUIEvent(new Event { type = EventType.ExecuteCommand, commandName = "NewKeyboardFocus" }, true);
            }
            if (e.rawType == EventType.Used) return true;
            // A mouse release no control used, while this container holds the mouse: the control
            // that took the press is in another container, and lets go.
            if (e.rawType == EventType.MouseUp && this.HasMouseCapture()) GUIUtility.hotControl = 0;
            return false;
        }

        // A pointer event reaches IMGUI only from a pen or a touch (not done: Unbound's pointer
        // events carry no IMGUI event); mouse events carry the IMGUI event. A mouse press, release
        // or move counts inside the container's clip, or while it captures the mouse.
        bool SendEventToIMGUI(EventBase evt)
        {
            if (evt is IPointerEvent) return false;
            if (IsLocalEvent(evt) && !this.HasMouseCapture() && !CurrentClip().Contains(evt.originalMousePosition)) return false;
            return HandleIMGUIEvent(evt.imguiEvent, true);
        }

        static bool IsLocalEvent(EventBase evt) =>
            evt is MouseDownEvent || evt is MouseUpEvent || evt is MouseMoveEvent;

        [EventInterest(EventInterestOptionsInternal.TriggeredByOS)]
        [EventInterest(new[] { typeof(NavigationMoveEvent), typeof(NavigationSubmitEvent), typeof(NavigationCancelEvent), typeof(BlurEvent), typeof(FocusEvent), typeof(DetachFromPanelEvent), typeof(AttachToPanelEvent) })]
        internal override void HandleEventBubbleUpDisabled(EventBase evt) => HandleEventBubbleUp(evt);

        // The element's own handling is left out: a click focuses the container only through a
        // control that takes the keyboard, and the container never shows as focused itself.
        [EventInterest(EventInterestOptionsInternal.TriggeredByOS)]
        [EventInterest(new[] { typeof(NavigationMoveEvent), typeof(NavigationSubmitEvent), typeof(NavigationCancelEvent), typeof(BlurEvent), typeof(FocusEvent), typeof(DetachFromPanelEvent), typeof(AttachToPanelEvent) })]
        protected override void HandleEventBubbleUp(EventBase evt)
        {
            // Navigation stops here: IMGUI handles the key events itself.
            if ((evt.imguiEvent != null && SendEventToIMGUI(evt)) || evt is NavigationMoveEvent || evt is NavigationSubmitEvent || evt is NavigationCancelEvent)
            {
                evt.StopPropagation();
                focusController?.IgnoreEvent(evt);
            }
            else if (evt is BlurEvent)
            {
                m_LostFocus = true;
                IncrementVersion(VersionChangeType.Repaint);
            }
            else if (evt is FocusEvent focus)
            {
                m_ReceivedFocus = true;
                m_FocusChangeDirection = focus.direction;
                m_IsFocusDelegated = focus.IsFocusDelegated;
            }
            else if (evt is DetachFromPanelEvent)
            {
                if (m_Scope != 0) GUIUtility.ForgetIdList(m_Scope);
                m_Scope = 0;
            }
            else if (evt is AttachToPanelEvent) SetFoldoutDepthClass();
        }

        // Inside foldouts the container has the class of its depth (tests/projects/imguicontainer
        // foldout.*): none outside one, "max" past the foldouts' greatest styled depth.
        void SetFoldoutDepthClass()
        {
            var names = ussFoldoutChildDepthClassNames;
            for (int i = 0; i < names.Count; i++) RemoveFromClassList(names[i]);
            int depth = 0;
            for (var ancestor = parent; ancestor != null; ancestor = ancestor.parent)
                if (ancestor is Foldout) depth++;
            if (depth != 0) AddToClassList(names[Mathf.Min(depth, names.Count - 1)]);
        }

        // Its size from its IMGUI layout, where the panel leaves it free.
        protected internal override Vector2 DoMeasure(float desiredWidth, MeasureMode widthMode, float desiredHeight, MeasureMode heightMode)
        {
            float width = float.NaN, height = float.NaN;
            if (widthMode != MeasureMode.Exactly || heightMode != MeasureMode.Exactly)
            {
                var size = layout;
                if (widthMode == MeasureMode.Exactly) size.width = desiredWidth;
                if (heightMode == MeasureMode.Exactly) size.height = desiredHeight;
                s_MeasureEvent.type = EventType.Layout;
                DoOnGUI(s_MeasureEvent, m_CachedOrigin, m_CachedClip, true, size);
                width = layoutMeasuredWidth;
                height = layoutMeasuredHeight;
            }
            switch (widthMode)
            {
                case MeasureMode.Exactly: width = desiredWidth; break;
                case MeasureMode.AtMost: width = Mathf.Min(width, desiredWidth); break;
            }
            switch (heightMode)
            {
                case MeasureMode.Exactly: height = desiredHeight; break;
                case MeasureMode.AtMost: height = Mathf.Min(height, desiredHeight); break;
            }
            return new Vector2(width, height);
        }
    }
}
