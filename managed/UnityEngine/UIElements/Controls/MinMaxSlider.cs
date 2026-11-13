using Unity.Properties;
using System;

// MinMaxSlider, from the Unity 6000.0 Manual page "MinMaxSlider" (UI Toolkit control reference) and
// the Scripting API page MinMaxSlider:
//   - "a slider that lets users select a range of values within a specified minimum and maximum
//     range"; `value` "is a Vector2 where the x is the lower bound and the y is the higher bound";
//     `minValue`/`maxValue` are "the low/high value of the range represented on the slider";
//     `lowLimit`/`highLimit` are the slider's limits; `range` "Returns the range of the low/high
//     limits of the slider";
//   - the constructor parameters (minValue = 0, maxValue = 10, minLimit = -3.4028235E+38,
//     maxLimit = 3.4028235E+38) and their descriptions;
//   - navigation: "The MinMaxSlider cycles through the navigation states when a
//     NavigationSubmitEvent is received": minimum thumb, maximum thumb, middle bar, then none
//     (default navigation); in a state, a NavigationMoveEvent along the slider's direction moves
//     the minimum, the maximum, or both; the element of the current state has the movable class;
//   - USS class names from the page's USS class table.
// Dragging uses the slider drag manipulator (ClampedDragger) on the input element.
// Unbound's choices (unmeasured; the pages say nothing):
//   - both bounds are clamped to the limits (taken in increasing order) and the higher bound is
//     kept at or above the lower one; setting minValue above maxValue moves maxValue along, and
//     setting maxValue below minValue moves minValue along;
//   - changing a limit re-clamps the value through `value` (a ChangeEvent when it changes);
//   - MinMaxSlider() is MinMaxSlider(null), the parameter defaults above;
//   - the element tree: tracker and dragger in the input, the two thumbs in the dragger at its left
//     and right edges; the dragger is placed with inline left/width in proportion to the input's
//     width; nothing is placed while the limits' range is not a finite, non-zero number;
//   - a press on a thumb drags that bound (the other bound stops it), a press between the thumbs
//     drags both (stopped by the limits, keeping the width), a press elsewhere does nothing; where
//     the thumbs overlap, the half nearer the press decides;
//   - a navigation move steps by the power of ten nearest 1% of the limits' range (10 times that
//     with Shift); left lowers, right raises; losing focus returns to default navigation;
//   - the mixed-value display removes the dragger.
namespace UnityEngine.UIElements
{
    public partial class MinMaxSlider : BaseField<Vector2>
    {
        public new static readonly string ussClassName = "unity-min-max-slider";
        public new static readonly string labelUssClassName = ussClassName + "__label";
        public new static readonly string inputUssClassName = ussClassName + "__input";
        public static readonly string trackerUssClassName = ussClassName + "__tracker";
        public static readonly string draggerUssClassName = ussClassName + "__dragger";
        public static readonly string minThumbUssClassName = ussClassName + "__min-thumb";
        public static readonly string maxThumbUssClassName = ussClassName + "__max-thumb";
        public static readonly string movableUssClassName = ussClassName + "--movable";

        /// <summary>What a drag or a navigation move changes.</summary>
        internal enum DragPart
        {
            None,
            MinThumb,
            MaxThumb,
            Middle,
        }

        float m_LowLimit;
        float m_HighLimit;
        readonly VisualElement m_Tracker;
        DragPart m_DragPart;
        Vector2 m_DragStartValue;
        DragPart m_NavigationPart;

        internal VisualElement dragElement { get; private set; }
        internal VisualElement dragMinThumb { get; private set; }
        internal VisualElement dragMaxThumb { get; private set; }
        internal ClampedDragger<float> clampedDragger { get; private set; }

        internal DragPart navigationPart => m_NavigationPart;

        [CreateProperty]
        public float minValue
        {
            get => value.x;
            set => this.value = new Vector2(ClampToLimits(value), Mathf.Max(this.value.y, ClampToLimits(value)));
        }

        [CreateProperty]
        public float maxValue
        {
            get => value.y;
            set => this.value = new Vector2(Mathf.Min(this.value.x, ClampToLimits(value)), ClampToLimits(value));
        }

        public override Vector2 value
        {
            get => base.value;
            set => base.value = ClampValue(value);
        }

        [CreateProperty(ReadOnly = true)]
        public float range => Math.Abs(m_HighLimit - m_LowLimit);

        [CreateProperty]
        public float lowLimit
        {
            get => m_LowLimit;
            set
            {
                if (m_LowLimit == value) return;
                m_LowLimit = value;
                this.value = rawValue;
                UpdateDragElements();
            }
        }

        [CreateProperty]
        public float highLimit
        {
            get => m_HighLimit;
            set
            {
                if (m_HighLimit == value) return;
                m_HighLimit = value;
                this.value = rawValue;
                UpdateDragElements();
            }
        }

        public MinMaxSlider() : this(null) { }

        public MinMaxSlider(float minValue, float maxValue, float minLimit, float maxLimit) : this(null, minValue, maxValue, minLimit, maxLimit) { }

        public MinMaxSlider(string label, float minValue = 0f, float maxValue = 10f, float minLimit = float.MinValue, float maxLimit = float.MaxValue) : base(label, null)
        {
            AddToClassList(ussClassName);
            labelElement.AddToClassList(labelUssClassName);
            visualInput.AddToClassList(inputUssClassName);
            visualInput.pickingMode = PickingMode.Position;
            m_LowLimit = minLimit;
            m_HighLimit = maxLimit;

            m_Tracker = new VisualElement { pickingMode = PickingMode.Ignore };
            m_Tracker.AddToClassList(trackerUssClassName);
            visualInput.Add(m_Tracker);
            dragElement = new VisualElement { pickingMode = PickingMode.Ignore, usageHints = UsageHints.DynamicTransform };
            dragElement.AddToClassList(draggerUssClassName);
            dragElement.style.position = Position.Absolute;
            visualInput.Add(dragElement);
            dragMinThumb = new VisualElement { pickingMode = PickingMode.Ignore };
            dragMinThumb.AddToClassList(minThumbUssClassName);
            dragMinThumb.style.position = Position.Absolute;
            dragMinThumb.style.left = 0f;
            dragElement.Add(dragMinThumb);
            dragMaxThumb = new VisualElement { pickingMode = PickingMode.Ignore };
            dragMaxThumb.AddToClassList(maxThumbUssClassName);
            dragMaxThumb.style.position = Position.Absolute;
            dragMaxThumb.style.right = 0f;
            dragElement.Add(dragMaxThumb);

            clampedDragger = new ClampedDragger<float>(null, () => { }, OnDrag);
            visualInput.AddManipulator(clampedDragger);
            visualInput.RegisterCallback<GeometryChangedEvent>(e => UpdateDragElements());
            RegisterCallback<NavigationSubmitEvent>(OnNavigationSubmit);
            RegisterCallback<NavigationMoveEvent>(OnNavigationMove);
            RegisterCallback<FocusOutEvent>(e => SetNavigationPart(DragPart.None));
            SetValueWithoutNotify(new Vector2(minValue, maxValue));
        }

        public override void SetValueWithoutNotify(Vector2 newValue)
        {
            base.SetValueWithoutNotify(ClampValue(newValue));
            UpdateDragElements();
        }

        [global::UnityEngine.UIElements.@EventInterestAttribute(new global::System.Type[] { typeof(global::UnityEngine.UIElements.GeometryChangedEvent) })] protected override void HandleEventBubbleUp(EventBase evt)
        {
            base.HandleEventBubbleUp(evt);
            if (evt != null && evt.eventTypeId == GeometryChangedEvent.TypeId()) UpdateDragElements();
        }

        protected override void UpdateMixedValueContent()
        {
            if (showMixedValue)
            {
                dragElement.RemoveFromHierarchy();
                return;
            }
            if (dragElement.hierarchy.parent != visualInput) visualInput.Add(dragElement);
            UpdateDragElements();
        }

        internal override void RegisterEditingCallbacks()
        {
            visualInput.RegisterCallback<PointerDownEvent>(StartEditing, TrickleDown.TrickleDown);
            visualInput.RegisterCallback<PointerUpEvent>(EndEditing);
        }

        internal override void UnregisterEditingCallbacks()
        {
            visualInput.UnregisterCallback<PointerDownEvent>(StartEditing, TrickleDown.TrickleDown);
            visualInput.UnregisterCallback<PointerUpEvent>(EndEditing);
        }

        float ClampToLimits(float bound)
        {
            float low = Mathf.Min(m_LowLimit, m_HighLimit), high = Mathf.Max(m_LowLimit, m_HighLimit);
            return Mathf.Clamp(bound, low, high);
        }

        Vector2 ClampValue(Vector2 bounds)
        {
            float low = ClampToLimits(bounds.x);
            float high = ClampToLimits(bounds.y);
            return new Vector2(low, Mathf.Max(low, high));
        }

        /// <summary>The value after moving <paramref name="part"/> by <paramref name="delta"/> (in
        /// value units) from <paramref name="start"/>.</summary>
        internal Vector2 MovedValue(DragPart part, Vector2 start, float delta)
        {
            float lower = Mathf.Min(m_LowLimit, m_HighLimit), upper = Mathf.Max(m_LowLimit, m_HighLimit);
            switch (part)
            {
                case DragPart.MinThumb:
                    return new Vector2(Mathf.Clamp(start.x + delta, lower, start.y), start.y);
                case DragPart.MaxThumb:
                    return new Vector2(start.x, Mathf.Clamp(start.y + delta, start.x, upper));
                case DragPart.Middle:
                    float shift = Mathf.Clamp(delta, lower - start.x, upper - start.y);
                    return new Vector2(start.x + shift, start.y + shift);
                default:
                    return start;
            }
        }

        /// <summary>The fraction of the limits' range at <paramref name="bound"/>, or NaN when the
        /// range is not a finite, non-zero number.</summary>
        float Normalized(float bound)
        {
            float span = m_HighLimit - m_LowLimit;
            if (float.IsNaN(span) || float.IsInfinity(span) || Mathf.Approximately(span, 0f)) return float.NaN;
            return Mathf.Clamp01((bound - m_LowLimit) / span);
        }

        float InputWidth()
        {
            float width = visualInput.layout.width;
            return float.IsNaN(width) || width <= 0f ? float.NaN : width;
        }

        void UpdateDragElements()
        {
            if (dragElement == null || panel == null) return;
            float width = InputWidth();
            float low = Normalized(value.x), high = Normalized(value.y);
            if (float.IsNaN(width) || float.IsNaN(low) || float.IsNaN(high)) return;
            dragElement.style.left = low * width;
            dragElement.style.width = (high - low) * width;
        }

        DragPart PartAt(float x)
        {
            float width = InputWidth();
            float low = Normalized(value.x) * width, high = Normalized(value.y) * width;
            if (float.IsNaN(low) || float.IsNaN(high)) return DragPart.None;
            float thumb = dragMinThumb.resolvedStyle.width;
            if (float.IsNaN(thumb) || thumb < 0f) thumb = 0f;
            bool onMin = x >= low && x <= low + thumb;
            bool onMax = x >= high - thumb && x <= high;
            if (onMin && onMax) return x < (low + high) * 0.5f ? DragPart.MinThumb : DragPart.MaxThumb;
            if (onMin) return DragPart.MinThumb;
            if (onMax) return DragPart.MaxThumb;
            if (x > low && x < high) return DragPart.Middle;
            return DragPart.None;
        }

        void OnDrag()
        {
            if (clampedDragger.dragDirection == ClampedDragger<float>.DragDirection.None)
            {
                m_DragStartValue = value;
                m_DragPart = PartAt(clampedDragger.startMousePosition.x);
                return;
            }
            if (m_DragPart == DragPart.None) return;
            float width = InputWidth();
            float span = m_HighLimit - m_LowLimit;
            if (float.IsNaN(width) || float.IsNaN(span) || float.IsInfinity(span)) return;
            value = MovedValue(m_DragPart, m_DragStartValue, clampedDragger.delta.x / width * span);
        }

        void SetNavigationPart(DragPart part)
        {
            m_NavigationPart = part;
            dragMinThumb.EnableInClassList(movableUssClassName, part == DragPart.MinThumb);
            dragMaxThumb.EnableInClassList(movableUssClassName, part == DragPart.MaxThumb);
            dragElement.EnableInClassList(movableUssClassName, part == DragPart.Middle);
        }

        void OnNavigationSubmit(NavigationSubmitEvent evt)
        {
            switch (m_NavigationPart)
            {
                case DragPart.None:
                    SetNavigationPart(DragPart.MinThumb);
                    break;
                case DragPart.MinThumb:
                    SetNavigationPart(DragPart.MaxThumb);
                    break;
                case DragPart.MaxThumb:
                    SetNavigationPart(DragPart.Middle);
                    break;
                default:
                    SetNavigationPart(DragPart.None);
                    break;
            }
            evt.StopPropagation();
        }

        void OnNavigationMove(NavigationMoveEvent evt)
        {
            if (m_NavigationPart == DragPart.None) return;
            float sign;
            if (evt.direction == NavigationMoveEvent.Direction.Left) sign = -1f;
            else if (evt.direction == NavigationMoveEvent.Direction.Right) sign = 1f;
            else return;
            float span = Math.Abs(m_HighLimit - m_LowLimit);
            if (float.IsNaN(span) || float.IsInfinity(span)) return;
            float step = BaseSlider<float>.GetClosestPowerOfTen(span * 0.01f);
            if (evt.shiftKey) step *= 10f;
            value = MovedValue(m_NavigationPart, value, sign * step);
            evt.StopPropagation();
            focusController?.IgnoreEvent(evt);
        }
    }
}
