using Unity.Properties;

namespace UnityEngine.UIElements
{
    /// <summary>
    /// Two panes side by side (or stacked) with a drag line between them (Unity 6000.0 Manual
    /// "TwoPaneSplitView": "requires exactly two child elements"; one pane has a fixed size, the
    /// other flexes). The fixed pane gets the initial dimension once both children are present and
    /// laid out; dragging the line resizes it between both panes' minimum sizes, and resizing the
    /// view keeps the flexed pane at least its minimum. CollapseChild hides one pane; UnCollapse
    /// shows both again. Matches the Unity 6000.0.71f1 Editor (tests/projects/uitkcontrols).
    /// </summary>
    public partial class TwoPaneSplitView : VisualElement
    {
        internal static readonly BindingId fixedPaneIndexProperty = nameof(fixedPaneIndex);
        internal static readonly BindingId fixedPaneInitialDimensionProperty = nameof(fixedPaneInitialDimension);
        internal static readonly BindingId orientationProperty = nameof(orientation);

        const float k_FixedPaneInitialDimension = 100f;

        static readonly string s_UssClassName = "unity-two-pane-split-view";
        static readonly string s_ContentContainerClassName = "unity-two-pane-split-view__content-container";
        static readonly string s_HandleDragLineClassName = "unity-two-pane-split-view__dragline";
        static readonly string s_HandleDragLineVerticalClassName = s_HandleDragLineClassName + "--vertical";
        static readonly string s_HandleDragLineHorizontalClassName = s_HandleDragLineClassName + "--horizontal";
        static readonly string s_HandleDragLineAnchorClassName = "unity-two-pane-split-view__dragline-anchor";
        static readonly string s_HandleDragLineAnchorVerticalClassName = s_HandleDragLineAnchorClassName + "--vertical";
        static readonly string s_HandleDragLineAnchorHorizontalClassName = s_HandleDragLineAnchorClassName + "--horizontal";
        static readonly string s_VerticalClassName = "unity-two-pane-split-view--vertical";
        static readonly string s_HorizontalClassName = "unity-two-pane-split-view--horizontal";

        VisualElement m_LeftPane;
        VisualElement m_RightPane;
        VisualElement m_FixedPane;
        VisualElement m_FlexedPane;
        [SerializeField, DontCreateProperty]
        float m_FixedPaneDimension = -1f;
        VisualElement m_DragLine;
        VisualElement m_DragLineAnchor;
        bool m_CollapseMode;
        bool m_PendingCollapseToExecute;
        int m_CollapsedChildIndex = -1;
        VisualElement m_Content;
        TwoPaneSplitViewOrientation m_Orientation;
        int m_FixedPaneIndex;
        float m_FixedPaneInitialDimension = k_FixedPaneInitialDimension;
        internal TwoPaneSplitViewResizer m_Resizer;

        public VisualElement fixedPane => m_FixedPane;

        public VisualElement flexedPane => m_FlexedPane;

        internal VisualElement dragLine => m_DragLine;

        [CreateProperty]
        public int fixedPaneIndex
        {
            get => m_FixedPaneIndex;
            set
            {
                if (value == m_FixedPaneIndex) return;
                Init(value, m_FixedPaneInitialDimension, m_Orientation);
                NotifyPropertyChanged(fixedPaneIndexProperty);
            }
        }

        [CreateProperty]
        public float fixedPaneInitialDimension
        {
            get => m_FixedPaneInitialDimension;
            set
            {
                if (value == m_FixedPaneInitialDimension) return;
                Init(m_FixedPaneIndex, value, m_Orientation);
                NotifyPropertyChanged(fixedPaneInitialDimensionProperty);
            }
        }

        [CreateProperty]
        public TwoPaneSplitViewOrientation orientation
        {
            get => m_Orientation;
            set
            {
                if (value == m_Orientation) return;
                Init(m_FixedPaneIndex, m_FixedPaneInitialDimension, value);
                NotifyPropertyChanged(orientationProperty);
            }
        }

        /// <summary>The fixed pane's size: the dragged size when the view has a view data key, else
        /// the initial dimension.</summary>
        internal float fixedPaneDimension
        {
            get => string.IsNullOrEmpty(viewDataKey) ? m_FixedPaneInitialDimension : m_FixedPaneDimension;
            set
            {
                if (value == m_FixedPaneDimension) return;
                m_FixedPaneDimension = value;
            }
        }

        public TwoPaneSplitView()
        {
            SetupSplitView();
            Init(m_FixedPaneIndex, m_FixedPaneInitialDimension, m_Orientation);
        }

        public TwoPaneSplitView(int fixedPaneIndex, float fixedPaneStartDimension, TwoPaneSplitViewOrientation orientation)
        {
            SetupSplitView();
            Init(fixedPaneIndex, fixedPaneStartDimension, orientation);
        }

        void SetupSplitView()
        {
            AddToClassList(s_UssClassName);
            m_Content = new VisualElement { name = "unity-content-container" };
            m_Content.AddToClassList(s_ContentContainerClassName);
            hierarchy.Add(m_Content);
            m_DragLineAnchor = new VisualElement { name = "unity-dragline-anchor" };
            m_DragLineAnchor.AddToClassList(s_HandleDragLineAnchorClassName);
            hierarchy.Add(m_DragLineAnchor);
            m_DragLine = new VisualElement { name = "unity-dragline" };
            m_DragLine.AddToClassList(s_HandleDragLineClassName);
            m_DragLineAnchor.Add(m_DragLine);
        }

        public void CollapseChild(int index)
        {
            if (index != 0 && index != 1)
            {
                Debug.LogError("Invalid index. Must be 0 or 1.");
                return;
            }
            if (m_LeftPane == null)
            {
                // Collapsed once the panes are known.
                m_PendingCollapseToExecute = true;
                m_CollapsedChildIndex = index;
                return;
            }
            m_DragLine.style.display = DisplayStyle.None;
            m_DragLineAnchor.style.display = DisplayStyle.None;
            var shown = index == 0 ? m_RightPane : m_LeftPane;
            var hidden = index == 0 ? m_LeftPane : m_RightPane;
            shown.style.width = StyleKeyword.Initial;
            shown.style.height = StyleKeyword.Initial;
            shown.style.flexGrow = 1f;
            hidden.style.display = DisplayStyle.None;
            m_CollapseMode = true;
            AdjustPanesBasedOnAnchor();
        }

        public void UnCollapse()
        {
            if (m_LeftPane == null) return;
            VisualElement collapsed = null;
            if (m_LeftPane.style.display == DisplayStyle.None) collapsed = m_LeftPane;
            else if (m_RightPane.style.display == DisplayStyle.None) collapsed = m_RightPane;
            if (collapsed == null) return;
            m_LeftPane.style.display = DisplayStyle.Flex;
            m_RightPane.style.display = DisplayStyle.Flex;
            m_DragLine.style.display = DisplayStyle.Flex;
            m_DragLineAnchor.style.display = DisplayStyle.Flex;
            m_LeftPane.style.flexGrow = 0f;
            m_RightPane.style.flexGrow = 0f;
            m_CollapseMode = false;
            m_PendingCollapseToExecute = false;
            m_CollapsedChildIndex = -1;
            Init(m_FixedPaneIndex, m_FixedPaneInitialDimension, m_Orientation);
            AdjustPanesBasedOnAnchor();
            // The drag line follows the pane once it has its size again.
            collapsed.RegisterCallback<GeometryChangedEvent>(OnUncollapsedPaneResized);
        }

        void OnUncollapsedPaneResized(GeometryChangedEvent evt)
        {
            UpdateDragLineAnchorOffset();
            evt.elementTarget.UnregisterCallback<GeometryChangedEvent>(OnUncollapsedPaneResized);
        }

        internal virtual void Init(int fixedPaneIndex, float fixedPaneInitialDimension, TwoPaneSplitViewOrientation orientation)
        {
            m_Orientation = orientation;
            m_FixedPaneIndex = fixedPaneIndex;
            m_FixedPaneInitialDimension = fixedPaneInitialDimension;
            bool horizontal = m_Orientation == TwoPaneSplitViewOrientation.Horizontal;
            m_Content.RemoveFromClassList(s_HorizontalClassName);
            m_Content.RemoveFromClassList(s_VerticalClassName);
            m_Content.AddToClassList(horizontal ? s_HorizontalClassName : s_VerticalClassName);
            m_DragLineAnchor.RemoveFromClassList(s_HandleDragLineAnchorHorizontalClassName);
            m_DragLineAnchor.RemoveFromClassList(s_HandleDragLineAnchorVerticalClassName);
            m_DragLineAnchor.AddToClassList(horizontal ? s_HandleDragLineAnchorHorizontalClassName : s_HandleDragLineAnchorVerticalClassName);
            m_DragLine.RemoveFromClassList(s_HandleDragLineHorizontalClassName);
            m_DragLine.RemoveFromClassList(s_HandleDragLineVerticalClassName);
            m_DragLine.AddToClassList(horizontal ? s_HandleDragLineHorizontalClassName : s_HandleDragLineVerticalClassName);
            if (m_Resizer != null)
            {
                m_DragLineAnchor.RemoveManipulator(m_Resizer);
                m_Resizer = null;
            }
            // Without both panes yet (a UXML element gets its children after its attributes), the
            // setup waits for the first layout.
            if (m_Content.childCount != 2) RegisterCallback<GeometryChangedEvent>(OnPostDisplaySetup);
            else PostDisplaySetup();
        }

        void OnPostDisplaySetup(GeometryChangedEvent evt)
        {
            if (m_Content.childCount != 2)
            {
                Debug.LogError("TwoPaneSplitView needs exactly 2 children.");
                return;
            }
            bool firstSetup = m_LeftPane == null;
            PostDisplaySetup();
            if (firstSetup && m_PendingCollapseToExecute)
            {
                CollapseChild(m_CollapsedChildIndex);
                m_PendingCollapseToExecute = false;
            }
            UnregisterCallback<GeometryChangedEvent>(OnPostDisplaySetup);
            AdjustPanesBasedOnAnchor();
        }

        /// <summary>Makes room in the content for the drag line anchor, unless a pane is collapsed.</summary>
        void AdjustPanesBasedOnAnchor()
        {
            bool collapsed = m_LeftPane.style.display == DisplayStyle.None || m_RightPane.style.display == DisplayStyle.None;
            bool horizontal = m_Orientation == TwoPaneSplitViewOrientation.Horizontal;
            if (horizontal)
            {
                float width = collapsed ? 0f : m_DragLineAnchor.layout.width;
                m_RightPane.style.left = width;
                m_Content.style.paddingRight = width;
            }
            else
            {
                float height = collapsed ? 0f : m_DragLineAnchor.layout.height;
                m_RightPane.style.top = height;
                m_Content.style.paddingBottom = height;
            }
        }

        void IdentifyLeftAndRightPane()
        {
            m_LeftPane = m_Content[0];
            if (m_FixedPaneIndex == 0) m_FixedPane = m_LeftPane;
            else m_FlexedPane = m_LeftPane;
            m_RightPane = m_Content[1];
            if (m_FixedPaneIndex == 1) m_FixedPane = m_RightPane;
            else m_FlexedPane = m_RightPane;
        }

        void PostDisplaySetup()
        {
            if (m_Content.childCount != 2)
            {
                Debug.LogError("TwoPaneSplitView needs exactly 2 children.");
                return;
            }
            if (fixedPaneDimension < 0f) fixedPaneDimension = m_FixedPaneInitialDimension;
            float dimension = fixedPaneDimension;
            IdentifyLeftAndRightPane();
            m_FixedPane.style.flexBasis = StyleKeyword.Null;
            m_FixedPane.style.flexShrink = StyleKeyword.Null;
            m_FixedPane.style.flexGrow = StyleKeyword.Null;
            m_FlexedPane.style.flexGrow = StyleKeyword.Null;
            m_FlexedPane.style.flexShrink = StyleKeyword.Null;
            m_FlexedPane.style.flexBasis = StyleKeyword.Null;
            m_FixedPane.style.width = StyleKeyword.Null;
            m_FixedPane.style.height = StyleKeyword.Null;
            m_FlexedPane.style.width = StyleKeyword.Null;
            m_FlexedPane.style.height = StyleKeyword.Null;
            bool horizontal = m_Orientation == TwoPaneSplitViewOrientation.Horizontal;
            if (horizontal)
            {
                m_FixedPane.style.width = dimension;
                m_FixedPane.style.height = StyleKeyword.Null;
            }
            else
            {
                m_FixedPane.style.width = StyleKeyword.Null;
                m_FixedPane.style.height = dimension;
            }
            m_FixedPane.style.flexShrink = 0f;
            m_FixedPane.style.flexGrow = 0f;
            m_FlexedPane.style.flexGrow = 1f;
            m_FlexedPane.style.flexShrink = 0f;
            m_FlexedPane.style.flexBasis = 0f;
            m_DragLineAnchor.style.left = 0f;
            m_DragLineAnchor.style.top = 0f;
            if (horizontal)
            {
                float margins = m_FixedPane.resolvedStyle.marginLeft + m_FixedPane.resolvedStyle.marginRight;
                m_DragLineAnchor.style.left = m_FixedPaneIndex == 0
                    ? margins + dimension
                    : resolvedStyle.width - margins - dimension - m_DragLineAnchor.resolvedStyle.width;
            }
            else
            {
                float margins = m_FixedPane.resolvedStyle.marginTop + m_FixedPane.resolvedStyle.marginBottom;
                m_DragLineAnchor.style.top = m_FixedPaneIndex == 0
                    ? margins + dimension
                    : resolvedStyle.height - margins - dimension - m_DragLineAnchor.resolvedStyle.height;
            }
            // Dragging toward the flexed pane grows the fixed one.
            int direction = m_FixedPaneIndex == 0 ? 1 : -1;
            if (m_Resizer != null) m_DragLineAnchor.RemoveManipulator(m_Resizer);
            m_Resizer = new TwoPaneSplitViewResizer(this, direction);
            m_DragLineAnchor.AddManipulator(m_Resizer);
            RegisterCallback<GeometryChangedEvent>(OnSizeChange);
        }

        void OnSizeChange(GeometryChangedEvent evt) => UpdateLayout(true, true);

        void UpdateDragLineAnchorOffset() => UpdateLayout(false, true);

        /// <summary>Fits the fixed pane and the drag line to the view's size: the flexed pane keeps
        /// its minimum size, then the fixed pane keeps its own.</summary>
        void UpdateLayout(bool updateFixedPane, bool updateDragLine)
        {
            if (m_CollapseMode) return;
            if (resolvedStyle.display == DisplayStyle.None || resolvedStyle.visibility == Visibility.Hidden) return;
            bool vertical = m_Orientation == TwoPaneSplitViewOrientation.Vertical;
            var fixedStyle = m_FixedPane.resolvedStyle;
            var flexedStyle = m_FlexedPane.resolvedStyle;
            float maxLength = vertical ? resolvedStyle.height : resolvedStyle.width;
            float fixedPaneLength = vertical ? fixedStyle.height : fixedStyle.width;
            float fixedPaneMargins = vertical ? fixedStyle.marginTop + fixedStyle.marginBottom : fixedStyle.marginLeft + fixedStyle.marginRight;
            float fixedPaneMinLength = (vertical ? fixedStyle.minHeight : fixedStyle.minWidth).value;
            float flexedPaneMargins = vertical ? flexedStyle.marginTop + flexedStyle.marginBottom : flexedStyle.marginLeft + flexedStyle.marginRight;
            float flexedPaneMinLength = (vertical ? flexedStyle.minHeight : flexedStyle.minWidth).value;
            if (maxLength >= fixedPaneLength + fixedPaneMargins + flexedPaneMinLength + flexedPaneMargins)
            {
                // Both panes fit: only the drag line follows the fixed pane.
                if (updateDragLine)
                    SetDragLineOffset(m_FixedPaneIndex == 0 ? fixedPaneLength + fixedPaneMargins : maxLength - fixedPaneLength - fixedPaneMargins);
            }
            else if (maxLength >= fixedPaneMinLength + fixedPaneMargins + flexedPaneMinLength + flexedPaneMargins)
            {
                // The fixed pane shrinks to leave the flexed pane its minimum.
                float newDimension = maxLength - flexedPaneMinLength - flexedPaneMargins - fixedPaneMargins;
                newDimension -= vertical ? m_DragLineAnchor.layout.height : m_DragLineAnchor.layout.width;
                bool underMinimum = newDimension < fixedPaneMinLength;
                bool overMinimum = fixedPaneLength > fixedPaneMinLength;
                if (updateFixedPane)
                {
                    if (!underMinimum) SetFixedPaneDimension(newDimension);
                    else if (overMinimum) SetFixedPaneDimension(fixedPaneMinLength);
                }
                if (updateDragLine)
                {
                    if (underMinimum)
                        SetDragLineOffset(m_FixedPaneIndex == 0 ? fixedPaneMinLength : maxLength - fixedPaneMinLength - fixedPaneMargins);
                    else
                        SetDragLineOffset(m_FixedPaneIndex == 0 ? newDimension + fixedPaneMargins : flexedPaneMinLength + flexedPaneMargins);
                }
            }
            else
            {
                // Neither pane fits: the fixed pane keeps its minimum.
                if (updateFixedPane) SetFixedPaneDimension(fixedPaneMinLength);
                if (updateDragLine)
                    SetDragLineOffset(m_FixedPaneIndex == 0 ? fixedPaneMinLength + fixedPaneMargins : flexedPaneMinLength + flexedPaneMargins);
            }
        }

        public override VisualElement contentContainer => m_Content;

        void SetDragLineOffset(float offset)
        {
            if (m_Orientation == TwoPaneSplitViewOrientation.Horizontal) m_DragLineAnchor.style.left = offset;
            else m_DragLineAnchor.style.top = offset;
        }

        void SetFixedPaneDimension(float dimension)
        {
            if (m_Orientation == TwoPaneSplitViewOrientation.Horizontal) m_FixedPane.style.width = dimension;
            else m_FixedPane.style.height = dimension;
        }
    }

    /// <summary>Drags a TwoPaneSplitView's line with the left button, resizing its fixed pane within
    /// both panes' minimum sizes (matches the Unity 6000.0.71f1 Editor, tests/projects/uitkcontrols).</summary>
    partial class TwoPaneSplitViewResizer : PointerManipulator
    {
        const float k_DragLineTolerance = 1f;

        Vector3 m_Start;
        protected bool m_Active;
        readonly TwoPaneSplitView m_SplitView;
        readonly int m_Direction;
        float m_Delta;

        bool vertical => m_SplitView.orientation == TwoPaneSplitViewOrientation.Vertical;

        VisualElement fixedPane => m_SplitView.fixedPane;

        VisualElement flexedPane => m_SplitView.flexedPane;

        public float delta => m_Delta;

        float fixedPaneMinDimension => (vertical ? fixedPane.resolvedStyle.minHeight : fixedPane.resolvedStyle.minWidth).value;

        float fixedPaneMargins => vertical
            ? fixedPane.resolvedStyle.marginTop + fixedPane.resolvedStyle.marginBottom
            : fixedPane.resolvedStyle.marginLeft + fixedPane.resolvedStyle.marginRight;

        float flexedPaneMinDimension => (vertical ? flexedPane.resolvedStyle.minHeight : flexedPane.resolvedStyle.minWidth).value;

        float flexedPaneMargin => vertical
            ? flexedPane.resolvedStyle.marginTop + flexedPane.resolvedStyle.marginBottom
            : flexedPane.resolvedStyle.marginLeft + flexedPane.resolvedStyle.marginRight;

        public TwoPaneSplitViewResizer(TwoPaneSplitView splitView, int dir)
        {
            m_SplitView = splitView;
            m_Direction = dir;
            activators.Add(new ManipulatorActivationFilter { button = MouseButton.LeftMouse });
            m_Active = false;
        }

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<PointerDownEvent>(OnPointerDown);
            target.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            target.RegisterCallback<PointerUpEvent>(OnPointerUp);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<PointerDownEvent>(OnPointerDown);
            target.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
            target.UnregisterCallback<PointerUpEvent>(OnPointerUp);
        }

        /// <summary>Resizes the fixed pane by <paramref name="delta"/>: shrinking stops at its minimum,
        /// growing stops where the flexed pane reaches its own; the drag line follows.</summary>
        public void ApplyDelta(float delta)
        {
            float oldDimension = vertical ? fixedPane.resolvedStyle.height : fixedPane.resolvedStyle.width;
            float newDimension = oldDimension + delta;
            var dragLineStyle = m_SplitView.dragLine.resolvedStyle;
            var anchorBounds = target.worldBound;
            float minDimension = fixedPaneMinDimension;
            if (m_SplitView.fixedPaneIndex == 1)
                minDimension += vertical ? anchorBounds.height + Mathf.Abs(dragLineStyle.top) : anchorBounds.width + Mathf.Abs(dragLineStyle.left);
            if (newDimension < oldDimension && newDimension < minDimension) newDimension = minDimension;
            float maxDimension = vertical ? m_SplitView.resolvedStyle.height : m_SplitView.resolvedStyle.width;
            maxDimension -= flexedPaneMinDimension + flexedPaneMargin + fixedPaneMargins;
            if (m_SplitView.fixedPaneIndex == 0)
                maxDimension -= vertical
                    ? Mathf.Abs(anchorBounds.height - (dragLineStyle.height - Mathf.Abs(dragLineStyle.top)))
                    : Mathf.Abs(anchorBounds.width - (dragLineStyle.width - Mathf.Abs(dragLineStyle.left)));
            if (newDimension > oldDimension && newDimension > maxDimension) newDimension = maxDimension;
            if (vertical) fixedPane.style.height = newDimension;
            else fixedPane.style.width = newDimension;
            float viewLength = vertical ? m_SplitView.resolvedStyle.height : m_SplitView.resolvedStyle.width;
            if (m_SplitView.fixedPaneIndex == 0)
            {
                float offset = newDimension + fixedPaneMargins;
                if (offset >= fixedPaneMinDimension) SetOffset(offset);
            }
            else
            {
                float offset = viewLength - newDimension - fixedPaneMargins;
                if (offset >= flexedPaneMinDimension + flexedPaneMargin) SetOffset(offset);
            }
            m_SplitView.fixedPaneDimension = newDimension;
        }

        void SetOffset(float offset)
        {
            if (vertical) target.style.top = offset;
            else target.style.left = offset;
        }

        protected void OnPointerDown(PointerDownEvent e)
        {
            if (m_Active)
            {
                e.StopImmediatePropagation();
                return;
            }
            if (!CanStartManipulation(e)) return;
            m_Start = e.localPosition;
            m_Active = true;
            target.CapturePointer(e.pointerId);
            e.StopPropagation();
        }

        protected void OnPointerMove(PointerMoveEvent e)
        {
            if (!m_Active || !target.HasPointerCapture(e.pointerId)) return;
            var lineBounds = m_SplitView.dragLine.worldBound;
            var anchorBounds = target.worldBound;
            bool lineBeforeAnchor = vertical ? lineBounds.y < anchorBounds.y : lineBounds.x < anchorBounds.x;
            float distance = Mathf.Abs(vertical ? anchorBounds.y - lineBounds.y : anchorBounds.x - lineBounds.x);
            float lineOffset = vertical ? m_SplitView.dragLine.resolvedStyle.top : m_SplitView.dragLine.resolvedStyle.left;
            // A line drawn outside its anchor stops the drag once the anchor passes it.
            if (lineBeforeAnchor && Mathf.Abs(lineOffset) + k_DragLineTolerance <= distance)
            {
                InterruptPointerMove(e);
                return;
            }
            Vector2 diff = e.localPosition - m_Start;
            m_Delta = m_Direction * (vertical ? diff.y : diff.x);
            ApplyDelta(m_Delta);
            e.StopPropagation();
        }

        protected void OnPointerUp(PointerUpEvent e)
        {
            if (!m_Active || !target.HasPointerCapture(e.pointerId) || !CanStopManipulation(e)) return;
            m_Active = false;
            target.ReleasePointer(e.pointerId);
            e.StopPropagation();
        }

        protected void InterruptPointerMove(PointerMoveEvent e)
        {
            if (!CanStopManipulation(e)) return;
            m_Active = false;
            target.ReleasePointer(e.pointerId);
            e.StopPropagation();
        }
    }
}
