using System;

namespace UnityEngine.UIElements
{
    /// <summary>A scroll bar: two repeat buttons and a slider whose dragger is sized to the visible share.</summary>
    public partial class Scroller : VisualElement
    {
        sealed class ScrollerSlider : Slider
        {
            public ScrollerSlider(float start, float end, SliderDirection direction, float pageSize) : base(start, end, direction, pageSize) { }

            internal override float SliderNormalizeValue(float currentValue, float lowerValue, float higherValue) =>
                Mathf.Clamp(base.SliderNormalizeValue(currentValue, lowerValue, higherValue), 0f, 1f);
        }

        internal const float kDefaultPageSize = 20f;
        public static readonly string ussClassName = "unity-scroller";
        public static readonly string horizontalVariantUssClassName = ussClassName + "--horizontal";
        public static readonly string verticalVariantUssClassName = ussClassName + "--vertical";
        public static readonly string sliderUssClassName = ussClassName + "__slider";
        public static readonly string lowButtonUssClassName = ussClassName + "__low-button";
        public static readonly string highButtonUssClassName = ussClassName + "__high-button";

        public Slider slider { get; }
        public RepeatButton lowButton { get; }
        public RepeatButton highButton { get; }

        public float value
        {
            get => slider.value;
            set => slider.value = value;
        }

        public float lowValue
        {
            get => slider.lowValue;
            set => slider.lowValue = value;
        }

        public float highValue
        {
            get => slider.highValue;
            set => slider.highValue = value;
        }

        public SliderDirection direction
        {
            get => resolvedStyle.flexDirection != FlexDirection.Row ? SliderDirection.Vertical : SliderDirection.Horizontal;
            set
            {
                slider.direction = value;
                slider.inverted = value == SliderDirection.Vertical;
                if (value == SliderDirection.Horizontal)
                {
                    style.flexDirection = FlexDirection.Row;
                    AddToClassList(horizontalVariantUssClassName);
                    RemoveFromClassList(verticalVariantUssClassName);
                }
                else
                {
                    style.flexDirection = FlexDirection.Column;
                    AddToClassList(verticalVariantUssClassName);
                    RemoveFromClassList(horizontalVariantUssClassName);
                }
            }
        }

        public event Action<float> valueChanged;

        public Scroller() : this(0f, 0f, null) { }

        public Scroller(float lowValue, float highValue, Action<float> valueChanged, SliderDirection direction = SliderDirection.Vertical)
        {
            AddToClassList(ussClassName);
            slider = new ScrollerSlider(lowValue, highValue, direction, kDefaultPageSize) { name = "unity-slider", viewDataKey = "Slider" };
            slider.AddToClassList(sliderUssClassName);
            slider.RegisterValueChangedCallback(OnSliderValueChange);
            lowButton = new RepeatButton(ScrollPageUp, 250L, 30L) { name = "unity-low-button" };
            lowButton.AddToClassList(lowButtonUssClassName);
            Add(lowButton);
            highButton = new RepeatButton(ScrollPageDown, 250L, 30L) { name = "unity-high-button" };
            highButton.AddToClassList(highButtonUssClassName);
            Add(highButton);
            Add(slider);
            this.direction = direction;
            this.valueChanged = valueChanged;
        }

        public void Adjust(float factor)
        {
            SetEnabled(factor < 1f);
            slider.AdjustDragElement(factor);
        }

        void OnSliderValueChange(ChangeEvent<float> evt)
        {
            value = evt.newValue;
            valueChanged?.Invoke(slider.value);
            IncrementVersion(VersionChangeType.Repaint);
        }

        public void ScrollPageUp() => ScrollPageUp(1f);

        public void ScrollPageDown() => ScrollPageDown(1f);

        public void ScrollPageUp(float factor) => value -= factor * (slider.pageSize * (slider.lowValue < slider.highValue ? 1f : -1f));

        public void ScrollPageDown(float factor) => value += factor * (slider.pageSize * (slider.lowValue < slider.highValue ? 1f : -1f));
    }

    /// <summary>A viewport onto a content container moved by scroll offsets: scroll bars appear when the
    /// content overflows, the wheel scrolls by lines, touch drags scroll with inertia and elasticity.</summary>
    public partial class ScrollView : VisualElement
    {
        public enum TouchScrollBehavior
        {
            Unrestricted,
            Elastic,
            Clamped,
        }

        public enum NestedInteractionKind
        {
            Default,
            StopScrolling,
            ForwardScrolling,
        }

        internal enum TouchScrollingResult
        {
            Apply,
            Forward,
            Block,
        }

        const float k_SizeThreshold = 0.001f;
        const string k_SingleLineHeightPropertyName = "--unity-metrics-single_line-height";
        const float k_DefaultSingleLineHeight = 18f;
        internal const float k_UnsetPageSizeValue = -1f;
        internal const float k_MouseWheelScrollSizeDefaultValue = 18f;
        internal const float k_MouseWheelScrollSizeUnset = -1f;
        internal const float ScrollThresholdSquared = 100f;
        static readonly float k_DefaultScrollDecelerationRate = 0.135f;
        static readonly float k_DefaultElasticity = 0.1f;
        const float k_ScaledPixelsPerPointMultiplier = 10f;
        const float k_TouchScrollInertiaBaseTimeInterval = 0.004167f;

        int m_FirstLayoutPass = -1;
        ScrollerVisibility m_HorizontalScrollerVisibility;
        ScrollerVisibility m_VerticalScrollerVisibility;
        long m_ElasticAnimationIntervalMs = 16L;
        VisualElement m_AttachedRootVisualContainer;
        float m_SingleLineHeight = k_DefaultSingleLineHeight;
        bool m_SingleLineHeightDirtyFlag;
        internal bool m_MouseWheelScrollSizeIsInline;
        Vector2 m_ScrollOffset;
        float m_HorizontalPageSize;
        float m_VerticalPageSize;
        float m_MouseWheelScrollSize = k_MouseWheelScrollSizeDefaultValue;
        float m_ScrollDecelerationRate = k_DefaultScrollDecelerationRate;
        float m_Elasticity = k_DefaultElasticity;
        TouchScrollBehavior m_TouchScrollBehavior;
        NestedInteractionKind m_NestedInteractionKind;
        readonly VisualElement m_ContentContainer;
        readonly VisualElement m_ContentAndVerticalScrollContainer;
        float m_PreviousVerticalTouchScrollTimeStamp;
        float m_PreviousHorizontalTouchScrollTimeStamp;
        float m_ElapsedTimeSinceLastVerticalTouchScroll;
        float m_ElapsedTimeSinceLastHorizontalTouchScroll;
        ScrollViewMode m_Mode;
        IVisualElementScheduledItem m_ScheduledLayoutPassResetItem;
        Vector2 m_StartPosition;
        Vector2 m_PointerStartPosition;
        Vector2 m_Velocity;
        Vector2 m_SpringBackVelocity;
        Vector2 m_LowBounds;
        Vector2 m_HighBounds;
        float m_LastVelocityLerpTime;
        bool m_StartedMoving;
        bool m_TouchPointerMoveAllowed;
        bool m_TouchStoppedVelocity;
        VisualElement m_CapturedTarget;
        readonly EventCallback<PointerMoveEvent> m_CapturedTargetPointerMoveCallback;
        readonly EventCallback<PointerUpEvent> m_CapturedTargetPointerUpCallback;
        internal IVisualElementScheduledItem m_PostPointerUpAnimation;

        public static readonly string ussClassName = "unity-scroll-view";
        public static readonly string viewportUssClassName = ussClassName + "__content-viewport";
        public static readonly string horizontalVariantViewportUssClassName = viewportUssClassName + "--horizontal";
        public static readonly string verticalVariantViewportUssClassName = viewportUssClassName + "--vertical";
        public static readonly string verticalHorizontalVariantViewportUssClassName = viewportUssClassName + "--vertical-horizontal";
        public static readonly string contentAndVerticalScrollUssClassName = ussClassName + "__content-and-vertical-scroll-container";
        public static readonly string contentUssClassName = ussClassName + "__content-container";
        public static readonly string horizontalVariantContentUssClassName = contentUssClassName + "--horizontal";
        public static readonly string verticalVariantContentUssClassName = contentUssClassName + "--vertical";
        public static readonly string verticalHorizontalVariantContentUssClassName = contentUssClassName + "--vertical-horizontal";
        public static readonly string hScrollerUssClassName = ussClassName + "__horizontal-scroller";
        public static readonly string vScrollerUssClassName = ussClassName + "__vertical-scroller";
        public static readonly string horizontalVariantUssClassName = ussClassName + "--horizontal";
        public static readonly string verticalVariantUssClassName = ussClassName + "--vertical";
        public static readonly string verticalHorizontalVariantUssClassName = ussClassName + "--vertical-horizontal";
        public static readonly string scrollVariantUssClassName = ussClassName + "--scroll";

        public ScrollerVisibility horizontalScrollerVisibility
        {
            get => m_HorizontalScrollerVisibility;
            set
            {
                m_HorizontalScrollerVisibility = value;
                UpdateScrollers(needsHorizontal, needsVertical);
            }
        }

        public ScrollerVisibility verticalScrollerVisibility
        {
            get => m_VerticalScrollerVisibility;
            set
            {
                m_VerticalScrollerVisibility = value;
                UpdateScrollers(needsHorizontal, needsVertical);
            }
        }

        public long elasticAnimationIntervalMs
        {
            get => m_ElasticAnimationIntervalMs;
            set
            {
                if (m_ElasticAnimationIntervalMs == value) return;
                m_ElasticAnimationIntervalMs = value;
                m_PostPointerUpAnimation = schedule.Execute(PostPointerUpAnimation).Every(m_ElasticAnimationIntervalMs);
            }
        }

        [Obsolete("showHorizontal is obsolete. Use horizontalScrollerVisibility instead")]
        public bool showHorizontal
        {
            get => horizontalScrollerVisibility == ScrollerVisibility.AlwaysVisible;
            set => m_HorizontalScrollerVisibility = value ? ScrollerVisibility.AlwaysVisible : ScrollerVisibility.Auto;
        }

        [Obsolete("showVertical is obsolete. Use verticalScrollerVisibility instead")]
        public bool showVertical
        {
            get => verticalScrollerVisibility == ScrollerVisibility.AlwaysVisible;
            set => m_VerticalScrollerVisibility = value ? ScrollerVisibility.AlwaysVisible : ScrollerVisibility.Auto;
        }

        internal bool needsHorizontal =>
            (mode != ScrollViewMode.Vertical && horizontalScrollerVisibility == ScrollerVisibility.AlwaysVisible) ||
            (horizontalScrollerVisibility == ScrollerVisibility.Auto && scrollableWidth > k_SizeThreshold);

        internal bool needsVertical =>
            (mode != ScrollViewMode.Horizontal && verticalScrollerVisibility == ScrollerVisibility.AlwaysVisible) ||
            (verticalScrollerVisibility == ScrollerVisibility.Auto && scrollableHeight > k_SizeThreshold);

        internal bool isVerticalScrollDisplayed => verticalScroller.resolvedStyle.display == DisplayStyle.Flex;

        internal bool isHorizontalScrollDisplayed => horizontalScroller.resolvedStyle.display == DisplayStyle.Flex;

        public Vector2 scrollOffset
        {
            get => m_ScrollOffset;
            set
            {
                if (value == m_ScrollOffset) return;
                horizontalScroller.value = value.x;
                verticalScroller.value = value.y;
                m_ScrollOffset = new Vector2(horizontalScroller.value, verticalScroller.value);
                if (panel == null) return;
                UpdateScrollers(needsHorizontal, needsVertical);
                UpdateContentViewTransform();
            }
        }

        public float horizontalPageSize
        {
            get => m_HorizontalPageSize;
            set
            {
                m_HorizontalPageSize = value;
                UpdateHorizontalSliderPageSize();
            }
        }

        public float verticalPageSize
        {
            get => m_VerticalPageSize;
            set
            {
                m_VerticalPageSize = value;
                UpdateVerticalSliderPageSize();
            }
        }

        public float mouseWheelScrollSize
        {
            get => m_MouseWheelScrollSize;
            set
            {
                if (Math.Abs(m_MouseWheelScrollSize - value) <= float.Epsilon) return;
                m_MouseWheelScrollSizeIsInline = true;
                m_MouseWheelScrollSize = value;
            }
        }

        internal float scrollableWidth => contentContainer.boundingBox.width - contentViewport.layout.width;

        internal float scrollableHeight => contentContainer.boundingBox.height - contentViewport.layout.height;

        bool hasInertia => scrollDecelerationRate > 0f;

        public float scrollDecelerationRate
        {
            get => m_ScrollDecelerationRate;
            set => m_ScrollDecelerationRate = Mathf.Max(0f, value);
        }

        public float elasticity
        {
            get => m_Elasticity;
            set => m_Elasticity = Mathf.Max(0f, value);
        }

        public TouchScrollBehavior touchScrollBehavior
        {
            get => m_TouchScrollBehavior;
            set
            {
                m_TouchScrollBehavior = value;
                bool clamped = m_TouchScrollBehavior == TouchScrollBehavior.Clamped;
                horizontalScroller.slider.clamped = clamped;
                verticalScroller.slider.clamped = clamped;
            }
        }

        public NestedInteractionKind nestedInteractionKind
        {
            get => m_NestedInteractionKind;
            set => m_NestedInteractionKind = value;
        }

        public VisualElement contentViewport { get; }

        public Scroller horizontalScroller { get; }

        public Scroller verticalScroller { get; }

        public override VisualElement contentContainer => m_ContentContainer;

        public ScrollViewMode mode
        {
            get => m_Mode;
            set => SetScrollViewMode(value);
        }

        public ScrollView() : this(ScrollViewMode.Vertical) { }

        public ScrollView(ScrollViewMode scrollViewMode)
        {
            AddToClassList(ussClassName);
            m_ContentAndVerticalScrollContainer = new VisualElement { name = "unity-content-and-vertical-scroll-container" };
            m_ContentAndVerticalScrollContainer.AddToClassList(contentAndVerticalScrollUssClassName);
            hierarchy.Add(m_ContentAndVerticalScrollContainer);
            contentViewport = new VisualElement { name = "unity-content-viewport" };
            contentViewport.AddToClassList(viewportUssClassName);
            contentViewport.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            contentViewport.pickingMode = PickingMode.Ignore;
            m_ContentAndVerticalScrollContainer.RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            m_ContentAndVerticalScrollContainer.RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
            m_ContentAndVerticalScrollContainer.Add(contentViewport);
            m_ContentContainer = new VisualElement { name = "unity-content-container" };
            m_ContentContainer.disableClipping = true;
            m_ContentContainer.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            m_ContentContainer.AddToClassList(contentUssClassName);
            m_ContentContainer.usageHints = UsageHints.GroupTransform;
            contentViewport.Add(m_ContentContainer);
            SetScrollViewMode(scrollViewMode);
            horizontalScroller = new Scroller(0f, int.MaxValue, v =>
            {
                scrollOffset = new Vector2(v, scrollOffset.y);
                UpdateContentViewTransform();
            }, SliderDirection.Horizontal) { viewDataKey = "HorizontalScroller" };
            horizontalScroller.AddToClassList(hScrollerUssClassName);
            horizontalScroller.style.display = DisplayStyle.None;
            hierarchy.Add(horizontalScroller);
            verticalScroller = new Scroller(0f, int.MaxValue, v =>
            {
                scrollOffset = new Vector2(scrollOffset.x, v);
                UpdateContentViewTransform();
            }) { viewDataKey = "VerticalScroller" };
            horizontalScroller.slider.onSetValueWithoutNotify += v => m_ScrollOffset = new Vector2(v, scrollOffset.y);
            verticalScroller.slider.onSetValueWithoutNotify += v => m_ScrollOffset = new Vector2(scrollOffset.x, v);
            horizontalScroller.slider.clampedDragger.draggingEnded += UpdateElasticBehaviour;
            verticalScroller.slider.clampedDragger.draggingEnded += UpdateElasticBehaviour;
            horizontalScroller.lowButton.AddAction(UpdateElasticBehaviour);
            horizontalScroller.highButton.AddAction(UpdateElasticBehaviour);
            verticalScroller.lowButton.AddAction(UpdateElasticBehaviour);
            verticalScroller.highButton.AddAction(UpdateElasticBehaviour);
            verticalScroller.AddToClassList(vScrollerUssClassName);
            verticalScroller.style.display = DisplayStyle.None;
            m_ContentAndVerticalScrollContainer.Add(verticalScroller);
            touchScrollBehavior = TouchScrollBehavior.Clamped;
            UaRegisterCallback<WheelEvent>(OnScrollWheel, UaInvokePolicy.IncludeDisabled);
            verticalScroller.RegisterCallback<GeometryChangedEvent>(OnScrollersGeometryChanged);
            horizontalScroller.RegisterCallback<GeometryChangedEvent>(OnScrollersGeometryChanged);
            horizontalPageSize = k_UnsetPageSizeValue;
            verticalPageSize = k_UnsetPageSizeValue;
            horizontalScroller.slider.dragElement.RegisterCallback<GeometryChangedEvent>(e =>
            {
                if (e.oldRect.size != e.newRect.size) UpdateHorizontalSliderPageSize();
            });
            verticalScroller.slider.dragElement.RegisterCallback<GeometryChangedEvent>(e =>
            {
                if (e.oldRect.size != e.newRect.size) UpdateVerticalSliderPageSize();
            });
            m_CapturedTargetPointerMoveCallback = OnPointerMove;
            m_CapturedTargetPointerUpCallback = OnPointerUp;
            scrollOffset = Vector2.zero;
            m_ContentContainer.receivesHierarchyGeometryChangedEvents = true;
        }

        void UpdateHorizontalSliderPageSize()
        {
            float width = horizontalScroller.resolvedStyle.width;
            float page = m_HorizontalPageSize;
            if (width > 0f && Mathf.Approximately(m_HorizontalPageSize, k_UnsetPageSizeValue))
                page = horizontalScroller.slider.dragElement.resolvedStyle.width * 0.9f;
            if (page >= 0f) horizontalScroller.slider.pageSize = page;
        }

        void UpdateVerticalSliderPageSize()
        {
            float height = verticalScroller.resolvedStyle.height;
            float page = m_VerticalPageSize;
            if (height > 0f && Mathf.Approximately(m_VerticalPageSize, k_UnsetPageSizeValue))
                page = verticalScroller.slider.dragElement.resolvedStyle.height * 0.9f;
            if (page >= 0f) verticalScroller.slider.pageSize = page;
        }

        /// <summary>Moves the content by the scroll offset, rounded to the pixel grid.</summary>
        internal void UpdateContentViewTransform()
        {
            var position = contentContainer.transform.position;
            var offset = scrollOffset;
            if (needsVertical) offset.y += contentContainer.resolvedStyle.top;
            position.x = GUIUtility.RoundToPixelGrid(-offset.x);
            position.y = GUIUtility.RoundToPixelGrid(-offset.y);
            contentContainer.transform.position = position;
            IncrementVersion(VersionChangeType.Repaint);
        }

        public void ScrollTo(VisualElement child)
        {
            if (child == null) throw new ArgumentNullException(nameof(child));
            if (!contentContainer.Contains(child)) throw new ArgumentException("Cannot scroll to a VisualElement that's not a child of the ScrollView content-container.");
            m_Velocity = Vector2.zero;
            float dy = 0f, dx = 0f;
            if (scrollableHeight > 0f)
            {
                dy = GetYDeltaOffset(child);
                verticalScroller.value = scrollOffset.y + dy;
            }
            if (scrollableWidth > 0f)
            {
                dx = GetXDeltaOffset(child);
                horizontalScroller.value = scrollOffset.x + dx;
            }
            if (dy != 0f || dx != 0f) UpdateContentViewTransform();
        }

        float GetXDeltaOffset(VisualElement child)
        {
            float shift = -contentContainer.transform.position.x;
            var view = contentViewport.worldBound;
            float viewMin = view.xMin + shift, viewMax = view.xMax + shift;
            var bounds = child.worldBound;
            float childMin = bounds.xMin + shift, childMax = bounds.xMax + shift;
            if ((childMin >= viewMin && childMax <= viewMax) || float.IsNaN(childMin) || float.IsNaN(childMax)) return 0f;
            return GetDeltaDistance(viewMin, viewMax, childMin, childMax) * horizontalScroller.highValue / scrollableWidth;
        }

        float GetYDeltaOffset(VisualElement child)
        {
            float shift = -contentContainer.transform.position.y;
            var view = contentViewport.worldBound;
            float viewMin = view.yMin + shift, viewMax = view.yMax + shift;
            var bounds = child.worldBound;
            float childMin = bounds.yMin + shift, childMax = bounds.yMax + shift;
            if ((childMin >= viewMin && childMax <= viewMax) || float.IsNaN(childMin) || float.IsNaN(childMax)) return 0f;
            return GetDeltaDistance(viewMin, viewMax, childMin, childMax) * verticalScroller.highValue / scrollableHeight;
        }

        static float GetDeltaDistance(float viewMin, float viewMax, float childBoundaryMin, float childBoundaryMax)
        {
            float viewSize = viewMax - viewMin;
            float childSize = childBoundaryMax - childBoundaryMin;
            if (childSize > viewSize)
            {
                if (viewMin > childBoundaryMin && childBoundaryMax > viewMax) return 0f;
                return childBoundaryMin > viewMin ? childBoundaryMin - viewMin : childBoundaryMax - viewMax;
            }
            float delta = childBoundaryMax - viewMax;
            if (delta < -1f) delta = childBoundaryMin - viewMin;
            return delta;
        }

        void SetScrollViewMode(ScrollViewMode newMode)
        {
            m_Mode = newMode;
            RemoveFromClassList(verticalVariantUssClassName);
            RemoveFromClassList(horizontalVariantUssClassName);
            RemoveFromClassList(verticalHorizontalVariantUssClassName);
            RemoveFromClassList(scrollVariantUssClassName);
            contentContainer.RemoveFromClassList(verticalVariantContentUssClassName);
            contentContainer.RemoveFromClassList(horizontalVariantContentUssClassName);
            contentContainer.RemoveFromClassList(verticalHorizontalVariantContentUssClassName);
            contentViewport.RemoveFromClassList(verticalVariantViewportUssClassName);
            contentViewport.RemoveFromClassList(horizontalVariantViewportUssClassName);
            contentViewport.RemoveFromClassList(verticalHorizontalVariantViewportUssClassName);
            AddToClassList(scrollVariantUssClassName);
            switch (newMode)
            {
                case ScrollViewMode.Vertical:
                    AddToClassList(verticalVariantUssClassName);
                    contentViewport.AddToClassList(verticalVariantViewportUssClassName);
                    contentContainer.AddToClassList(verticalVariantContentUssClassName);
                    break;
                case ScrollViewMode.Horizontal:
                    AddToClassList(horizontalVariantUssClassName);
                    contentViewport.AddToClassList(horizontalVariantViewportUssClassName);
                    contentContainer.AddToClassList(horizontalVariantContentUssClassName);
                    break;
                case ScrollViewMode.VerticalAndHorizontal:
                    AddToClassList(verticalHorizontalVariantUssClassName);
                    contentViewport.AddToClassList(verticalHorizontalVariantViewportUssClassName);
                    contentContainer.AddToClassList(verticalHorizontalVariantContentUssClassName);
                    break;
            }
        }

        void OnAttachToPanel(AttachToPanelEvent evt)
        {
            if (evt.destinationPanel == null) return;
            m_AttachedRootVisualContainer = GetRootVisualContainer() ?? evt.destinationPanel.visualTree;
            m_AttachedRootVisualContainer?.RegisterCallback<CustomStyleResolvedEvent>(OnRootCustomStyleResolved);
            RegisterCallback<CustomStyleResolvedEvent>(OnRootCustomStyleResolved);
            m_SingleLineHeightDirtyFlag = true;
            if (evt.destinationPanel.contextType != ContextType.Player) return;
            m_ContentAndVerticalScrollContainer.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            contentContainer.RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
            contentContainer.RegisterCallback<PointerCancelEvent>(OnPointerCancel);
            contentContainer.RegisterCallback<PointerUpEvent>(OnPointerUp, TrickleDown.TrickleDown);
            contentContainer.RegisterCallback<PointerCaptureEvent>(OnPointerCapture);
            contentContainer.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            evt.destinationPanel.visualTree.RegisterCallback<PointerUpEvent>(OnRootPointerUp, TrickleDown.TrickleDown);
        }

        void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            m_ScheduledLayoutPassResetItem?.Pause();
            m_FirstLayoutPass = -1;
            if (evt.originPanel == null) return;
            m_AttachedRootVisualContainer?.UnregisterCallback<CustomStyleResolvedEvent>(OnRootCustomStyleResolved);
            m_AttachedRootVisualContainer = null;
            UnregisterCallback<CustomStyleResolvedEvent>(OnRootCustomStyleResolved);
            if (evt.originPanel.contextType != ContextType.Player) return;
            m_ContentAndVerticalScrollContainer.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
            contentContainer.UnregisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
            contentContainer.UnregisterCallback<PointerCancelEvent>(OnPointerCancel);
            contentContainer.UnregisterCallback<PointerUpEvent>(OnPointerUp, TrickleDown.TrickleDown);
            contentContainer.UnregisterCallback<PointerCaptureEvent>(OnPointerCapture);
            contentContainer.UnregisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            evt.originPanel.visualTree.UnregisterCallback<PointerUpEvent>(OnRootPointerUp, TrickleDown.TrickleDown);
        }

        void OnPointerCapture(PointerCaptureEvent evt)
        {
            m_CapturedTarget = evt.elementTarget;
            if (m_CapturedTarget == null) return;
            m_TouchPointerMoveAllowed = true;
            m_CapturedTarget.RegisterCallback(m_CapturedTargetPointerMoveCallback);
            m_CapturedTarget.RegisterCallback(m_CapturedTargetPointerUpCallback);
        }

        void OnPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            ReleaseScrolling(evt.pointerId, evt.target);
            if (m_CapturedTarget == null) return;
            m_CapturedTarget.UnregisterCallback(m_CapturedTargetPointerMoveCallback);
            m_CapturedTarget.UnregisterCallback(m_CapturedTargetPointerUpCallback);
            m_CapturedTarget = null;
        }

        /// <summary>The viewport or content changed size: scroll bars are shown as needed (once the layout
        /// keeps changing for more than five passes, a shown scroll bar stays shown).</summary>
        void OnGeometryChanged(GeometryChangedEvent evt)
        {
            if (evt.oldRect.size == evt.newRect.size) return;
            bool vertical = needsVertical;
            bool horizontal = needsHorizontal;
            if (m_FirstLayoutPass == -1)
            {
                m_FirstLayoutPass = evt.layoutPass;
            }
            else if (evt.layoutPass - m_FirstLayoutPass > 5)
            {
                vertical = vertical || isVerticalScrollDisplayed;
                horizontal = horizontal || isHorizontalScrollDisplayed;
            }
            UpdateScrollers(horizontal, vertical);
            UpdateContentViewTransform();
            if (m_ScheduledLayoutPassResetItem == null)
            {
                m_ScheduledLayoutPassResetItem = schedule.Execute(() => m_FirstLayoutPass = -1);
            }
            else
            {
                m_ScheduledLayoutPassResetItem.Pause();
                m_ScheduledLayoutPassResetItem.Resume();
            }
        }

        static float ComputeElasticOffset(float deltaPointer, float initialScrollOffset, float lowLimit, float hardLowLimit, float highLimit, float hardHighLimit)
        {
            initialScrollOffset = Mathf.Max(initialScrollOffset, hardLowLimit * 0.95f);
            initialScrollOffset = Mathf.Min(initialScrollOffset, hardHighLimit * 0.95f);
            float delta, span;
            if (initialScrollOffset < lowLimit && hardLowLimit < lowLimit)
            {
                span = lowLimit - hardLowLimit;
                float ratio = (lowLimit - initialScrollOffset) / span;
                delta = ratio * span / (1f - ratio) + deltaPointer;
                initialScrollOffset = lowLimit;
            }
            else if (initialScrollOffset > highLimit && hardHighLimit > highLimit)
            {
                span = hardHighLimit - highLimit;
                float ratio = (initialScrollOffset - highLimit) / span;
                delta = -1f * ratio * span / (1f - ratio) + deltaPointer;
                initialScrollOffset = highLimit;
            }
            else
            {
                delta = deltaPointer;
            }
            float target = initialScrollOffset - delta;
            float sign;
            if (target < lowLimit)
            {
                delta = lowLimit - target;
                initialScrollOffset = lowLimit;
                span = lowLimit - hardLowLimit;
                sign = 1f;
            }
            else
            {
                if (target <= highLimit) return target;
                delta = target - highLimit;
                initialScrollOffset = highLimit;
                span = hardHighLimit - highLimit;
                sign = -1f;
            }
            if (Mathf.Abs(delta) < 1e-30f) return initialScrollOffset;
            float elastic = delta / (delta + span) * span * sign;
            return initialScrollOffset - elastic;
        }

        void ComputeInitialSpringBackVelocity()
        {
            if (touchScrollBehavior != TouchScrollBehavior.Elastic)
            {
                m_SpringBackVelocity = Vector2.zero;
                return;
            }
            m_SpringBackVelocity.x = scrollOffset.x < m_LowBounds.x ? m_LowBounds.x - scrollOffset.x : scrollOffset.x > m_HighBounds.x ? m_HighBounds.x - scrollOffset.x : 0f;
            m_SpringBackVelocity.y = scrollOffset.y < m_LowBounds.y ? m_LowBounds.y - scrollOffset.y : scrollOffset.y > m_HighBounds.y ? m_HighBounds.y - scrollOffset.y : 0f;
        }

        void SpringBack()
        {
            if (touchScrollBehavior != TouchScrollBehavior.Elastic)
            {
                m_SpringBackVelocity = Vector2.zero;
                return;
            }
            var offset = scrollOffset;
            if (offset.x < m_LowBounds.x || offset.x > m_HighBounds.x)
            {
                float target = offset.x < m_LowBounds.x ? m_LowBounds.x : m_HighBounds.x;
                offset.x = Mathf.SmoothDamp(offset.x, target, ref m_SpringBackVelocity.x, elasticity, float.PositiveInfinity, m_ElapsedTimeSinceLastHorizontalTouchScroll);
                if (Mathf.Abs(m_SpringBackVelocity.x) < scaledPixelsPerPoint) m_SpringBackVelocity.x = 0f;
            }
            else
            {
                m_SpringBackVelocity.x = 0f;
            }
            if (offset.y < m_LowBounds.y || offset.y > m_HighBounds.y)
            {
                float target = offset.y < m_LowBounds.y ? m_LowBounds.y : m_HighBounds.y;
                offset.y = Mathf.SmoothDamp(offset.y, target, ref m_SpringBackVelocity.y, elasticity, float.PositiveInfinity, m_ElapsedTimeSinceLastVerticalTouchScroll);
                if (Mathf.Abs(m_SpringBackVelocity.y) < scaledPixelsPerPoint) m_SpringBackVelocity.y = 0f;
            }
            else
            {
                m_SpringBackVelocity.y = 0f;
            }
            scrollOffset = offset;
        }

        internal void ApplyScrollInertia()
        {
            if (!hasInertia || m_Velocity == Vector2.zero)
            {
                m_Velocity = Vector2.zero;
                return;
            }
            var moved = Vector2.zero;
            float time = 0f;
            while (time < m_ElapsedTimeSinceLastVerticalTouchScroll)
            {
                m_Velocity *= Mathf.Pow(scrollDecelerationRate, k_TouchScrollInertiaBaseTimeInterval);
                time += k_TouchScrollInertiaBaseTimeInterval;
                moved += m_Velocity * k_TouchScrollInertiaBaseTimeInterval;
            }
            float rest = m_ElapsedTimeSinceLastVerticalTouchScroll - time;
            if (rest > 0f && rest < k_TouchScrollInertiaBaseTimeInterval)
            {
                m_Velocity *= Mathf.Pow(scrollDecelerationRate, rest);
                moved += m_Velocity * rest;
            }
            float threshold = scaledPixelsPerPoint * k_ScaledPixelsPerPointMultiplier;
            if (Mathf.Abs(m_Velocity.x) <= threshold || (touchScrollBehavior == TouchScrollBehavior.Elastic && (scrollOffset.x < m_LowBounds.x || scrollOffset.x > m_HighBounds.x))) m_Velocity.x = 0f;
            if (Mathf.Abs(m_Velocity.y) <= threshold || (touchScrollBehavior == TouchScrollBehavior.Elastic && (scrollOffset.y < m_LowBounds.y || scrollOffset.y > m_HighBounds.y))) m_Velocity.y = 0f;
            scrollOffset += moved;
        }

        void PostPointerUpAnimation()
        {
            m_ElapsedTimeSinceLastVerticalTouchScroll = Time.unscaledTime - m_PreviousVerticalTouchScrollTimeStamp;
            m_PreviousVerticalTouchScrollTimeStamp = Time.unscaledTime;
            m_ElapsedTimeSinceLastHorizontalTouchScroll = Time.unscaledTime - m_PreviousHorizontalTouchScrollTimeStamp;
            m_PreviousHorizontalTouchScrollTimeStamp = Time.unscaledTime;
            ApplyScrollInertia();
            SpringBack();
            if (m_SpringBackVelocity != Vector2.zero || m_Velocity != Vector2.zero) return;
            m_PostPointerUpAnimation.Pause();
            m_ElapsedTimeSinceLastVerticalTouchScroll = 0f;
            m_ElapsedTimeSinceLastHorizontalTouchScroll = 0f;
            m_PreviousVerticalTouchScrollTimeStamp = 0f;
            m_PreviousHorizontalTouchScrollTimeStamp = 0f;
        }

        void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.pointerType == PointerType.mouse || !evt.isPrimary) return;
            if (evt.pointerId != PointerId.invalidPointerId) ReleaseScrolling(evt.pointerId, evt.target);
            m_PostPointerUpAnimation?.Pause();
            bool fast = Mathf.Abs(m_Velocity.x) > 10f || Mathf.Abs(m_Velocity.y) > 10f;
            m_TouchPointerMoveAllowed = true;
            m_StartedMoving = false;
            InitTouchScrolling(evt.position);
            if (!fast) return;
            contentContainer.CapturePointer(evt.pointerId);
            contentContainer.elementPanel?.PreventCompatibilityMouseEvents(evt.pointerId);
            evt.StopPropagation();
            m_TouchStoppedVelocity = true;
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            if (evt.pointerType == PointerType.mouse || !evt.isPrimary || !m_TouchPointerMoveAllowed) return;
            if (evt.isHandledByDraggable)
            {
                m_PointerStartPosition = evt.position;
                m_StartPosition = scrollOffset;
                return;
            }
            Vector2 delta = (Vector2)evt.position - m_PointerStartPosition;
            if (mode == ScrollViewMode.Horizontal) delta.y = 0f;
            else if (mode == ScrollViewMode.Vertical) delta.x = 0f;
            if (!m_TouchStoppedVelocity && !m_StartedMoving && delta.sqrMagnitude < ScrollThresholdSquared) return;
            if (ComputeTouchScrolling(evt.position) != TouchScrollingResult.Forward)
            {
                evt.isHandledByDraggable = true;
                evt.StopPropagation();
                if (!contentContainer.HasPointerCapture(evt.pointerId)) contentContainer.CapturePointer(evt.pointerId);
            }
            else
            {
                m_Velocity = Vector2.zero;
            }
        }

        void OnPointerCancel(PointerCancelEvent evt) => ReleaseScrolling(evt.pointerId, evt.target);

        void OnPointerUp(PointerUpEvent evt)
        {
            if (!ReleaseScrolling(evt.pointerId, evt.target)) return;
            contentContainer.elementPanel?.PreventCompatibilityMouseEvents(evt.pointerId);
            evt.StopPropagation();
        }

        internal void InitTouchScrolling(Vector2 position)
        {
            m_PointerStartPosition = position;
            m_StartPosition = scrollOffset;
            m_Velocity = Vector2.zero;
            m_SpringBackVelocity = Vector2.zero;
            m_LowBounds = new Vector2(Mathf.Min(horizontalScroller.lowValue, horizontalScroller.highValue), Mathf.Min(verticalScroller.lowValue, verticalScroller.highValue));
            m_HighBounds = new Vector2(Mathf.Max(horizontalScroller.lowValue, horizontalScroller.highValue), Mathf.Max(verticalScroller.lowValue, verticalScroller.highValue));
        }

        internal TouchScrollingResult ComputeTouchScrolling(Vector2 position)
        {
            Vector2 offset = default;
            if (touchScrollBehavior == TouchScrollBehavior.Clamped)
            {
                offset = m_StartPosition - (position - m_PointerStartPosition);
                offset = Vector2.Max(offset, m_LowBounds);
                offset = Vector2.Min(offset, m_HighBounds);
            }
            else if (touchScrollBehavior == TouchScrollBehavior.Elastic)
            {
                var delta = position - m_PointerStartPosition;
                offset.x = ComputeElasticOffset(delta.x, m_StartPosition.x, m_LowBounds.x, m_LowBounds.x - contentViewport.resolvedStyle.width, m_HighBounds.x, m_HighBounds.x + contentViewport.resolvedStyle.width);
                offset.y = ComputeElasticOffset(delta.y, m_StartPosition.y, m_LowBounds.y, m_LowBounds.y - contentViewport.resolvedStyle.height, m_HighBounds.y, m_HighBounds.y + contentViewport.resolvedStyle.height);
                m_PreviousVerticalTouchScrollTimeStamp = Time.unscaledTime;
                m_PreviousHorizontalTouchScrollTimeStamp = Time.unscaledTime;
            }
            else
            {
                offset = m_StartPosition - (position - m_PointerStartPosition);
            }
            if (mode == ScrollViewMode.Vertical) offset.x = m_LowBounds.x;
            else if (mode == ScrollViewMode.Horizontal) offset.y = m_LowBounds.y;
            if (scrollOffset != offset) return ApplyTouchScrolling(offset) ? TouchScrollingResult.Apply : TouchScrollingResult.Forward;
            return !m_StartedMoving || nestedInteractionKind == NestedInteractionKind.ForwardScrolling ? TouchScrollingResult.Forward : TouchScrollingResult.Block;
        }

        bool ApplyTouchScrolling(Vector2 newScrollOffset)
        {
            m_StartedMoving = true;
            if (hasInertia)
            {
                if (newScrollOffset == m_LowBounds || newScrollOffset == m_HighBounds)
                {
                    m_Velocity = Vector2.zero;
                    scrollOffset = newScrollOffset;
                    return false;
                }
                if (m_LastVelocityLerpTime > 0f)
                {
                    float since = Time.unscaledTime - m_LastVelocityLerpTime;
                    m_Velocity = Vector2.Lerp(m_Velocity, Vector2.zero, since * 10f);
                }
                m_LastVelocityLerpTime = Time.unscaledTime;
                float dt = k_TouchScrollInertiaBaseTimeInterval;
                var velocity = (newScrollOffset - scrollOffset) / dt;
                m_Velocity = Vector2.Lerp(m_Velocity, velocity, dt * 10f);
            }
            bool changed = scrollOffset != newScrollOffset;
            scrollOffset = newScrollOffset;
            return changed;
        }

        bool ReleaseScrolling(int pointerId, IEventHandler target)
        {
            m_TouchStoppedVelocity = false;
            m_StartedMoving = false;
            m_TouchPointerMoveAllowed = false;
            if (target != contentContainer || !contentContainer.HasPointerCapture(pointerId)) return false;
            m_PreviousVerticalTouchScrollTimeStamp = Time.unscaledTime;
            m_PreviousHorizontalTouchScrollTimeStamp = Time.unscaledTime;
            if (touchScrollBehavior == TouchScrollBehavior.Elastic || hasInertia) ExecuteElasticSpringAnimation();
            contentContainer.ReleasePointer(pointerId);
            return true;
        }

        void ExecuteElasticSpringAnimation()
        {
            ComputeInitialSpringBackVelocity();
            if (m_PostPointerUpAnimation == null) m_PostPointerUpAnimation = schedule.Execute(PostPointerUpAnimation).Every(m_ElasticAnimationIntervalMs);
            else m_PostPointerUpAnimation.Resume();
        }

        void AdjustScrollers()
        {
            var bounds = contentContainer.boundingBox;
            float horizontal = bounds.width > 1e-30f ? contentViewport.layout.width / bounds.width : 1f;
            float vertical = bounds.height > 1e-30f ? contentViewport.layout.height / bounds.height : 1f;
            horizontalScroller.Adjust(horizontal);
            verticalScroller.Adjust(vertical);
        }

        internal void UpdateScrollers(bool displayHorizontal, bool displayVertical)
        {
            AdjustScrollers();
            bool canVertical = scrollableHeight > 0f;
            bool canHorizontal = scrollableWidth > 0f;
            horizontalScroller.SetEnabled(canHorizontal);
            verticalScroller.SetEnabled(canVertical);
            verticalScroller.slider.clampedDragger.acceptClicksIfDisabled = canVertical;
            verticalScroller.lowButton.acceptClicksIfDisabled = canVertical;
            verticalScroller.highButton.acceptClicksIfDisabled = canVertical;
            horizontalScroller.slider.clampedDragger.acceptClicksIfDisabled = canHorizontal;
            horizontalScroller.lowButton.acceptClicksIfDisabled = canHorizontal;
            horizontalScroller.highButton.acceptClicksIfDisabled = canHorizontal;
            var horizontalDisplay = displayHorizontal && m_HorizontalScrollerVisibility != ScrollerVisibility.Hidden ? DisplayStyle.Flex : DisplayStyle.None;
            var verticalDisplay = displayVertical && m_VerticalScrollerVisibility != ScrollerVisibility.Hidden ? DisplayStyle.Flex : DisplayStyle.None;
            if (horizontalDisplay != horizontalScroller.style.display) horizontalScroller.style.display = horizontalDisplay;
            if (verticalDisplay != verticalScroller.style.display) verticalScroller.style.display = verticalDisplay;
            verticalScroller.lowValue = 0f;
            verticalScroller.highValue = float.IsNaN(scrollableHeight) ? verticalScroller.highValue : scrollableHeight;
            horizontalScroller.lowValue = 0f;
            horizontalScroller.highValue = float.IsNaN(scrollableWidth) ? horizontalScroller.highValue : scrollableWidth;
        }

        void OnScrollersGeometryChanged(GeometryChangedEvent evt)
        {
            if (evt.oldRect.size == evt.newRect.size) return;
            if (needsHorizontal && m_HorizontalScrollerVisibility != ScrollerVisibility.Hidden)
                horizontalScroller.style.marginRight = verticalScroller.layout.width;
            AdjustScrollers();
        }

        /// <summary>The wheel scrolls by lines (the theme's single line height, or mouseWheelScrollSize when set).</summary>
        void OnScrollWheel(WheelEvent evt)
        {
            if (Application.platform == RuntimePlatform.OSXEditor || Application.platform == RuntimePlatform.OSXPlayer)
            {
                if (evt.commandKey) return;
            }
            else if (evt.ctrlKey)
            {
                return;
            }
            bool scrolled = false;
            bool canVertical = mode != ScrollViewMode.Horizontal && scrollableHeight > 0f;
            bool canHorizontal = mode != ScrollViewMode.Vertical && scrollableWidth > 0f;
            float horizontalDelta = canHorizontal && !canVertical ? evt.delta.y : evt.delta.x;
            if ((canHorizontal || canVertical) && !m_MouseWheelScrollSizeIsInline)
            {
                var p = parent;
                if (m_SingleLineHeightDirtyFlag || (p != null && p.isRootVisualContainer && p != m_AttachedRootVisualContainer)) ReadSingleLineHeight();
            }
            float lineSize = m_MouseWheelScrollSizeIsInline ? mouseWheelScrollSize : m_SingleLineHeight;
            if (canVertical)
            {
                float before = verticalScroller.value;
                verticalScroller.value += evt.delta.y * (verticalScroller.lowValue < verticalScroller.highValue ? 1f : -1f) * lineSize;
                if (nestedInteractionKind == NestedInteractionKind.StopScrolling || !Mathf.Approximately(verticalScroller.value, before))
                {
                    evt.StopPropagation();
                    scrolled = true;
                }
            }
            if (canHorizontal)
            {
                float before = horizontalScroller.value;
                horizontalScroller.value += horizontalDelta * (horizontalScroller.lowValue < horizontalScroller.highValue ? 1f : -1f) * lineSize;
                if (nestedInteractionKind == NestedInteractionKind.StopScrolling || !Mathf.Approximately(horizontalScroller.value, before))
                {
                    evt.StopPropagation();
                    scrolled = true;
                }
            }
            if (!scrolled) return;
            UpdateElasticBehaviour();
            UpdateContentViewTransform();
        }

        void OnRootCustomStyleResolved(CustomStyleResolvedEvent evt) => m_SingleLineHeightDirtyFlag = true;

        void OnRootPointerUp(PointerUpEvent evt) => m_TouchPointerMoveAllowed = false;

        void ReadSingleLineHeight()
        {
            if (Unbound.UIElements.UaCustomProperties.TryReadDimension(this, k_SingleLineHeightPropertyName, out float height))
            {
                m_SingleLineHeightDirtyFlag = false;
                m_SingleLineHeight = height;
                return;
            }
            var root = GetFirstAncestorWhere(x => x.isRootVisualContainer);
            if (root != m_AttachedRootVisualContainer)
            {
                m_AttachedRootVisualContainer?.UnregisterCallback<CustomStyleResolvedEvent>(OnRootCustomStyleResolved);
                m_AttachedRootVisualContainer = root;
                m_AttachedRootVisualContainer?.RegisterCallback<CustomStyleResolvedEvent>(OnRootCustomStyleResolved);
            }
            if (m_AttachedRootVisualContainer != null && Unbound.UIElements.UaCustomProperties.TryReadDimension(m_AttachedRootVisualContainer, k_SingleLineHeightPropertyName, out height))
            {
                m_SingleLineHeightDirtyFlag = false;
                m_SingleLineHeight = height;
                return;
            }
            m_SingleLineHeight = k_DefaultSingleLineHeight;
            m_SingleLineHeightDirtyFlag = false;
        }

        void UpdateElasticBehaviour()
        {
            if (touchScrollBehavior != TouchScrollBehavior.Elastic) return;
            m_LowBounds = new Vector2(Mathf.Min(horizontalScroller.lowValue, horizontalScroller.highValue), Mathf.Min(verticalScroller.lowValue, verticalScroller.highValue));
            m_HighBounds = new Vector2(Mathf.Max(horizontalScroller.lowValue, horizontalScroller.highValue), Mathf.Max(verticalScroller.lowValue, verticalScroller.highValue));
            ExecuteElasticSpringAnimation();
        }

        internal void SetScrollOffsetWithoutNotify(Vector2 value)
        {
            horizontalScroller.slider.SetValueWithoutNotify(value.x);
            verticalScroller.slider.SetValueWithoutNotify(value.y);
            m_ScrollOffset = new Vector2(horizontalScroller.value, verticalScroller.value);
        }
    }
}

namespace Unbound.UIElements
{
    using UnityEngine.UIElements;

    internal static class UaCustomProperties
    {
        /// <summary>A number or dimension custom property (--name) of an element's computed style.</summary>
        internal static bool TryReadDimension(VisualElement element, string name, out float value)
        {
            value = 0f;
            var props = element.computedValues.customProperties;
            if (props == null || !props.TryGetValue(name, out var v)) return false;
            if (v.handle.valueType != StyleValueType.Float && v.handle.valueType != StyleValueType.Dimension) return false;
            value = v.sheet.ReadFloat(v.handle);
            return true;
        }
    }
}
