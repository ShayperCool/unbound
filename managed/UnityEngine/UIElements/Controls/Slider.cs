using System;
using System.Collections.Generic;
using System.Globalization;

namespace UnityEngine.UIElements
{
    /// <summary>A field set by dragging a dragger along a tracker (or clicking the tracker), as Unity's
    /// sliders do: the dragger is moved with its transform within the drag container.</summary>
    public abstract partial class BaseSlider<TValueType> : BaseField<TValueType> where TValueType : IComparable<TValueType>
    {
        internal enum SliderKey
        {
            None,
            Lowest,
            LowerPage,
            Lower,
            Higher,
            HigherPage,
            Highest,
        }

        float m_AdjustedPageSizeFromClick;
        bool m_IsEditingTextField;
        bool m_Fill;
        TValueType m_LowValue;
        TValueType m_HighValue;
        float m_PageSize;
        bool m_ShowInputField;
        Rect m_DragElementStartPos;
        SliderDirection m_Direction;
        bool m_Inverted;

        internal const float kDefaultPageSize = 0f;
        internal const bool kDefaultShowInputField = false;
        internal const bool kDefaultInverted = false;
        public new static readonly string ussClassName = "unity-base-slider";
        public new static readonly string labelUssClassName = ussClassName + "__label";
        public new static readonly string inputUssClassName = ussClassName + "__input";
        public static readonly string horizontalVariantUssClassName = ussClassName + "--horizontal";
        public static readonly string verticalVariantUssClassName = ussClassName + "--vertical";
        public static readonly string dragContainerUssClassName = ussClassName + "__drag-container";
        public static readonly string trackerUssClassName = ussClassName + "__tracker";
        public static readonly string draggerUssClassName = ussClassName + "__dragger";
        public static readonly string draggerBorderUssClassName = ussClassName + "__dragger-border";
        public static readonly string textFieldClassName = ussClassName + "__text-field";
        public static readonly string fillUssClassName = ussClassName + "__fill";
        public static readonly string movableUssClassName = ussClassName + "--movable";
        internal const string k_FillElementName = "unity-fill";

        internal VisualElement dragContainer { get; private set; }
        internal VisualElement dragElement { get; private set; }
        internal VisualElement trackElement { get; private set; }
        internal VisualElement dragBorderElement { get; private set; }
        internal TextField inputTextField { get; private set; }
        internal VisualElement fillElement { get; private set; }

        private protected override bool canSwitchToMixedValue => inputTextField == null || !inputTextField.textInputBase.textElement.hasFocus;

        public TValueType lowValue
        {
            get => m_LowValue;
            set
            {
                if (EqualityComparer<TValueType>.Default.Equals(m_LowValue, value)) return;
                m_LowValue = value;
                ClampValue();
                UpdateDragElementPosition();
            }
        }

        public TValueType highValue
        {
            get => m_HighValue;
            set
            {
                if (EqualityComparer<TValueType>.Default.Equals(m_HighValue, value)) return;
                m_HighValue = value;
                ClampValue();
                UpdateDragElementPosition();
            }
        }

        public TValueType range => SliderRange();

        public virtual float pageSize
        {
            get => m_PageSize;
            set => m_PageSize = value;
        }

        public virtual bool showInputField
        {
            get => m_ShowInputField;
            set
            {
                if (m_ShowInputField == value) return;
                m_ShowInputField = value;
                UpdateTextFieldVisibility();
            }
        }

        public bool fill
        {
            get => m_Fill;
            set
            {
                if (m_Fill == value) return;
                m_Fill = value;
                if (value)
                {
                    UpdateDragElementPosition();
                }
                else if (fillElement != null)
                {
                    fillElement.RemoveFromHierarchy();
                    fillElement = null;
                }
            }
        }

        internal bool clamped { get; set; } = true;

        internal ClampedDragger<TValueType> clampedDragger { get; private set; }

        public override TValueType value
        {
            get => base.value;
            set => base.value = clamped ? GetClampedValue(value) : value;
        }

        public SliderDirection direction
        {
            get => m_Direction;
            set
            {
                m_Direction = value;
                if (m_Direction == SliderDirection.Horizontal)
                {
                    RemoveFromClassList(verticalVariantUssClassName);
                    AddToClassList(horizontalVariantUssClassName);
                }
                else
                {
                    RemoveFromClassList(horizontalVariantUssClassName);
                    AddToClassList(verticalVariantUssClassName);
                }
            }
        }

        public bool inverted
        {
            get => m_Inverted;
            set
            {
                if (m_Inverted == value) return;
                m_Inverted = value;
                UpdateDragElementPosition();
            }
        }

        internal event Action<TValueType> onSetValueWithoutNotify;

        internal void SetHighValueWithoutNotify(TValueType newHighValue)
        {
            m_HighValue = newHighValue;
            SetValueWithoutNotify(clamped ? GetClampedValue(value) : value);
            UpdateDragElementPosition();
        }

        static TValueType Clamp(TValueType value, TValueType low, TValueType high)
        {
            if (low.CompareTo(value) > 0) return low;
            if (high.CompareTo(value) < 0) return high;
            return value;
        }

        TValueType GetClampedValue(TValueType newValue)
        {
            TValueType low = lowValue, high = highValue;
            if (low.CompareTo(high) > 0) (low, high) = (high, low);
            return Clamp(newValue, low, high);
        }

        public virtual void ApplyInputDeviceDelta(Vector3 delta, DeltaSpeed speed, TValueType startValue) { }

        public override void SetValueWithoutNotify(TValueType newValue)
        {
            var clampedValue = clamped ? GetClampedValue(newValue) : newValue;
            base.SetValueWithoutNotify(clampedValue);
            onSetValueWithoutNotify?.Invoke(clampedValue);
            UpdateDragElementPosition();
            UpdateTextFieldValue();
        }

        internal BaseSlider(string label, TValueType start, TValueType end, SliderDirection direction = SliderDirection.Horizontal, float pageSize = 0f) : base(label, null)
        {
            AddToClassList(ussClassName);
            labelElement.AddToClassList(labelUssClassName);
            visualInput.AddToClassList(inputUssClassName);
            this.direction = direction;
            this.pageSize = pageSize;
            lowValue = start;
            highValue = end;
            pickingMode = PickingMode.Ignore;
            dragContainer = new VisualElement { name = "unity-drag-container" };
            dragContainer.AddToClassList(dragContainerUssClassName);
            dragContainer.RegisterCallback<GeometryChangedEvent>(UpdateDragElementPosition);
            visualInput.Add(dragContainer);
            trackElement = new VisualElement { name = "unity-tracker", usageHints = UsageHints.DynamicColor };
            trackElement.AddToClassList(trackerUssClassName);
            dragContainer.Add(trackElement);
            dragBorderElement = new VisualElement { name = "unity-dragger-border" };
            dragBorderElement.AddToClassList(draggerBorderUssClassName);
            dragContainer.Add(dragBorderElement);
            dragElement = new VisualElement { name = "unity-dragger", usageHints = UsageHints.DynamicTransform };
            dragElement.RegisterCallback<GeometryChangedEvent>(UpdateDragElementPosition);
            dragElement.AddToClassList(draggerUssClassName);
            dragContainer.Add(dragElement);
            clampedDragger = new ClampedDragger<TValueType>(this, SetSliderValueFromClick, SetSliderValueFromDrag);
            dragContainer.pickingMode = PickingMode.Position;
            dragContainer.AddManipulator(clampedDragger);
            RegisterCallback<KeyDownEvent>(OnKeyDown);
            RegisterCallback<FocusInEvent>(OnFocusIn);
            RegisterCallback<FocusOutEvent>(OnFocusOut);
            RegisterCallback<NavigationSubmitEvent>(OnNavigationSubmit);
            RegisterCallback<NavigationMoveEvent>(OnNavigationMove);
            UpdateTextFieldVisibility();
            labelElement.AddToClassList(labelDraggerVariantUssClassName);
        }

        protected internal static float GetClosestPowerOfTen(float positiveNumber) =>
            positiveNumber <= 0f ? 1f : Mathf.Pow(10f, Mathf.RoundToInt(Mathf.Log10(positiveNumber)));

        protected internal static float RoundToMultipleOf(float value, float roundingValue) =>
            roundingValue == 0f ? value : Mathf.Round(value / roundingValue) * roundingValue;

        void ClampValue() => value = rawValue;

        internal abstract TValueType SliderLerpUnclamped(TValueType a, TValueType b, float interpolant);

        internal abstract float SliderNormalizeValue(TValueType currentValue, TValueType lowerValue, TValueType higherValue);

        internal abstract TValueType SliderRange();

        internal abstract TValueType ParseStringToValue(string previousValue, string newValue);

        internal abstract void ComputeValueFromKey(SliderKey sliderKey, bool isShift);

        TValueType SliderLerpDirectionalUnclamped(TValueType a, TValueType b, float positionInterpolant)
        {
            float t = direction == SliderDirection.Vertical ? 1f - positionInterpolant : positionInterpolant;
            return inverted ? SliderLerpUnclamped(b, a, t) : SliderLerpUnclamped(a, b, t);
        }

        void SetSliderValueFromDrag()
        {
            if (clampedDragger.dragDirection != ClampedDragger<TValueType>.DragDirection.Free) return;
            var delta = clampedDragger.delta;
            if (direction == SliderDirection.Horizontal)
                ComputeValueAndDirectionFromDrag(dragContainer.resolvedStyle.width, dragElement.resolvedStyle.width, m_DragElementStartPos.x + delta.x);
            else
                ComputeValueAndDirectionFromDrag(dragContainer.resolvedStyle.height, dragElement.resolvedStyle.height, m_DragElementStartPos.y + delta.y);
        }

        void ComputeValueAndDirectionFromDrag(float sliderLength, float dragElementLength, float dragElementPos)
        {
            float free = sliderLength - dragElementLength;
            if (Mathf.Abs(free) < 1e-30f) return;
            float t = clamped ? Mathf.Max(0f, Mathf.Min(dragElementPos, free)) / free : dragElementPos / free;
            var previous = value;
            value = SliderLerpDirectionalUnclamped(lowValue, highValue, t);
            if (EqualityComparer<TValueType>.Default.Equals(value, previous)) UpdateDragElementPosition();
        }

        void SetSliderValueFromClick()
        {
            if (clampedDragger.dragDirection == ClampedDragger<TValueType>.DragDirection.Free) return;
            if (clampedDragger.dragDirection == ClampedDragger<TValueType>.DragDirection.None)
            {
                if (Mathf.Approximately(pageSize, 0f))
                {
                    float length, draggerLength, x, y, pos;
                    if (direction == SliderDirection.Horizontal)
                    {
                        length = dragContainer.resolvedStyle.width;
                        draggerLength = dragElement.resolvedStyle.width;
                        x = Mathf.Max(0f, Mathf.Min(clampedDragger.startMousePosition.x - draggerLength / 2f, length - draggerLength));
                        y = dragElement.transform.position.y;
                        pos = x;
                    }
                    else
                    {
                        length = dragContainer.resolvedStyle.height;
                        draggerLength = dragElement.resolvedStyle.height;
                        x = dragElement.transform.position.x;
                        y = Mathf.Max(0f, Mathf.Min(clampedDragger.startMousePosition.y - draggerLength / 2f, length - draggerLength));
                        pos = y;
                    }
                    var position = new Vector3(x, y, 0f);
                    dragElement.transform.position = position;
                    dragBorderElement.transform.position = position;
                    m_DragElementStartPos = new Rect(x, y, dragElement.resolvedStyle.width, dragElement.resolvedStyle.height);
                    clampedDragger.dragDirection = ClampedDragger<TValueType>.DragDirection.Free;
                    ComputeValueAndDirectionFromDrag(length, draggerLength, pos);
                    return;
                }
                m_DragElementStartPos = new Rect(dragElement.transform.position.x, dragElement.transform.position.y, dragElement.resolvedStyle.width, dragElement.resolvedStyle.height);
            }
            if (direction == SliderDirection.Horizontal)
                ComputeValueAndDirectionFromClick(dragContainer.resolvedStyle.width, dragElement.resolvedStyle.width, dragElement.transform.position.x, clampedDragger.lastMousePosition.x);
            else
                ComputeValueAndDirectionFromClick(dragContainer.resolvedStyle.height, dragElement.resolvedStyle.height, dragElement.transform.position.y, clampedDragger.lastMousePosition.y);
        }

        void OnKeyDown(KeyDownEvent evt)
        {
            var key = SliderKey.None;
            bool horizontal = direction == SliderDirection.Horizontal;
            if ((horizontal && evt.keyCode == KeyCode.Home) || (!horizontal && evt.keyCode == KeyCode.End))
                key = inverted ? SliderKey.Highest : SliderKey.Lowest;
            else if ((horizontal && evt.keyCode == KeyCode.End) || (!horizontal && evt.keyCode == KeyCode.Home))
                key = inverted ? SliderKey.Lowest : SliderKey.Highest;
            else if ((horizontal && evt.keyCode == KeyCode.PageUp) || (!horizontal && evt.keyCode == KeyCode.PageDown))
                key = inverted ? SliderKey.HigherPage : SliderKey.LowerPage;
            else if ((horizontal && evt.keyCode == KeyCode.PageDown) || (!horizontal && evt.keyCode == KeyCode.PageUp))
                key = inverted ? SliderKey.LowerPage : SliderKey.HigherPage;
            if (key == SliderKey.None) return;
            ComputeValueFromKey(key, evt.shiftKey);
            evt.StopPropagation();
        }

        void OnNavigationMove(NavigationMoveEvent evt)
        {
            if (!dragElement.ClassListContains(movableUssClassName)) return;
            var key = SliderKey.None;
            bool horizontal = direction == SliderDirection.Horizontal;
            if (evt.direction == (horizontal ? NavigationMoveEvent.Direction.Left : NavigationMoveEvent.Direction.Down))
                key = inverted ? SliderKey.Higher : SliderKey.Lower;
            else if (evt.direction == (horizontal ? NavigationMoveEvent.Direction.Right : NavigationMoveEvent.Direction.Up))
                key = inverted ? SliderKey.Lower : SliderKey.Higher;
            if (key == SliderKey.None) return;
            ComputeValueFromKey(key, evt.shiftKey);
            evt.StopPropagation();
            focusController?.IgnoreEvent(evt);
        }

        void OnNavigationSubmit(NavigationSubmitEvent evt)
        {
            if (!m_IsEditingTextField) dragElement.EnableInClassList(movableUssClassName, !dragElement.ClassListContains(movableUssClassName));
        }

        internal virtual void ComputeValueAndDirectionFromClick(float sliderLength, float dragElementLength, float dragElementPos, float dragElementLastPos)
        {
            float free = sliderLength - dragElementLength;
            if (Mathf.Abs(free) < 1e-30f) return;
            bool before = dragElementLastPos < dragElementPos;
            bool after = dragElementLastPos > dragElementPos + dragElementLength;
            bool towardsLow = inverted ? after : before;
            bool towardsHigh = inverted ? before : after;
            m_AdjustedPageSizeFromClick = inverted ? m_AdjustedPageSizeFromClick - pageSize : m_AdjustedPageSizeFromClick + pageSize;
            if (towardsLow && clampedDragger.dragDirection != ClampedDragger<TValueType>.DragDirection.LowToHigh)
            {
                clampedDragger.dragDirection = ClampedDragger<TValueType>.DragDirection.HighToLow;
                float t = Mathf.Max(0f, Mathf.Min(dragElementPos - m_AdjustedPageSizeFromClick, free)) / free;
                value = SliderLerpDirectionalUnclamped(lowValue, highValue, t);
            }
            else if (towardsHigh && clampedDragger.dragDirection != ClampedDragger<TValueType>.DragDirection.HighToLow)
            {
                clampedDragger.dragDirection = ClampedDragger<TValueType>.DragDirection.LowToHigh;
                float t = Mathf.Max(0f, Mathf.Min(dragElementPos + m_AdjustedPageSizeFromClick, free)) / free;
                value = SliderLerpDirectionalUnclamped(lowValue, highValue, t);
            }
        }

        public void AdjustDragElement(float factor)
        {
            if (factor < 1f)
            {
                dragElement.style.visibility = new StyleEnum<Visibility>(Visibility.Visible, StyleKeyword.Null);
                var s = dragElement.style;
                if (direction == SliderDirection.Horizontal)
                {
                    float min = resolvedStyle.minWidth == StyleKeyword.Auto ? 0f : resolvedStyle.minWidth.value;
                    s.width = Mathf.Round(Mathf.Max(dragContainer.layout.width * factor, min));
                }
                else
                {
                    float min = resolvedStyle.minHeight == StyleKeyword.Auto ? 0f : resolvedStyle.minHeight.value;
                    s.height = Mathf.Round(Mathf.Max(dragContainer.layout.height * factor, min));
                }
            }
            else
            {
                dragElement.style.visibility = new StyleEnum<Visibility>(Visibility.Hidden, StyleKeyword.Undefined);
            }
            dragBorderElement.visible = dragElement.visible;
        }

        void UpdateDragElementPosition(GeometryChangedEvent evt)
        {
            if (evt.oldRect.size != evt.newRect.size) UpdateDragElementPosition();
        }

        static bool SameValues(float a, float b, float epsilon) => Mathf.Abs(b - a) < epsilon;

        void UpdateDragElementPosition()
        {
            if (panel == null) return;
            float normalized = SliderNormalizeValue(value, lowValue, highValue);
            float t = inverted ? 1f - normalized : normalized;
            float epsilon = scaledPixelsPerPoint * 0.5f;
            if (direction == SliderDirection.Horizontal)
            {
                float width = dragElement.resolvedStyle.width;
                float margins = -dragElement.resolvedStyle.marginLeft - dragElement.resolvedStyle.marginRight;
                float free = dragContainer.layout.width - width + margins;
                float x = t * free;
                if (float.IsNaN(x)) return;
                if (!SameValues(dragElement.transform.position.x, x, epsilon))
                {
                    var position = new Vector3(x, 0f, 0f);
                    dragElement.transform.position = position;
                    dragBorderElement.transform.position = position;
                    m_AdjustedPageSizeFromClick = 0f;
                }
            }
            else
            {
                float height = dragElement.resolvedStyle.height;
                float free = dragContainer.resolvedStyle.height - height;
                float y = (1f - t) * free;
                if (float.IsNaN(y)) return;
                if (!SameValues(dragElement.transform.position.y, y, epsilon))
                {
                    var position = new Vector3(0f, y, 0f);
                    dragElement.transform.position = position;
                    dragBorderElement.transform.position = position;
                    m_AdjustedPageSizeFromClick = 0f;
                }
            }
            UpdateFill(normalized);
        }

        void UpdateFill(float normalizedValue)
        {
            if (!fill) return;
            if (fillElement == null)
            {
                fillElement = new VisualElement { name = k_FillElementName, usageHints = UsageHints.DynamicColor };
                fillElement.AddToClassList(fillUssClassName);
                trackElement.Add(fillElement);
            }
            var length = Length.Percent((1f - normalizedValue) * 100f);
            var s = fillElement.style;
            if (direction == SliderDirection.Vertical)
            {
                s.right = 0f;
                s.left = 0f;
                s.bottom = inverted ? length : (Length)0f;
                s.top = inverted ? (Length)0f : length;
            }
            else
            {
                s.top = 0f;
                s.bottom = 0f;
                s.left = inverted ? length : (Length)0f;
                s.right = inverted ? (Length)0f : length;
            }
        }

        protected override void HandleEventBubbleUp(EventBase evt)
        {
            base.HandleEventBubbleUp(evt);
            if (evt != null && evt.eventTypeId == GeometryChangedEvent.TypeId()) UpdateDragElementPosition((GeometryChangedEvent)evt);
        }

        [Obsolete("ExecuteDefaultAction override has been removed because default event handling was migrated to HandleEventBubbleUp. Please use HandleEventBubbleUp.", false)]
        protected override void ExecuteDefaultAction(EventBase evt) { }

        void UpdateTextFieldVisibility()
        {
            if (showInputField)
            {
                if (inputTextField != null) return;
                inputTextField = new TextField { name = "unity-text-field" };
                inputTextField.AddToClassList(textFieldClassName);
                inputTextField.RegisterValueChangedCallback(OnTextFieldValueChange);
                inputTextField.RegisterCallback<FocusInEvent>(OnTextFieldFocusIn);
                inputTextField.RegisterCallback<FocusOutEvent>(OnTextFieldFocusOut);
                visualInput.Add(inputTextField);
                UpdateTextFieldValue();
            }
            else if (inputTextField != null && inputTextField.panel != null)
            {
                inputTextField.RemoveFromHierarchy();
                inputTextField.UnregisterValueChangedCallback(OnTextFieldValueChange);
                inputTextField.UnregisterCallback<FocusInEvent>(OnTextFieldFocusIn);
                inputTextField.UnregisterCallback<FocusOutEvent>(OnTextFieldFocusOut);
                inputTextField = null;
            }
        }

        void UpdateTextFieldValue()
        {
            if (inputTextField == null || m_IsEditingTextField) return;
            inputTextField.SetValueWithoutNotify(string.Format(CultureInfo.InvariantCulture, "{0:g7}", value));
        }

        void OnFocusIn(FocusInEvent evt) => dragElement.AddToClassList(movableUssClassName);

        void OnFocusOut(FocusOutEvent evt) => dragElement.RemoveFromClassList(movableUssClassName);

        void OnTextFieldFocusIn(FocusInEvent evt) => m_IsEditingTextField = true;

        void OnTextFieldFocusOut(FocusOutEvent evt)
        {
            m_IsEditingTextField = false;
            UpdateTextFieldValue();
        }

        void OnTextFieldValueChange(ChangeEvent<string> evt)
        {
            var parsed = GetClampedValue(ParseStringToValue(evt.previousValue, evt.newValue));
            if (EqualityComparer<TValueType>.Default.Equals(parsed, value)) return;
            value = parsed;
            evt.StopPropagation();
            UpdateDragElementPosition();
        }

        protected override void UpdateMixedValueContent()
        {
            if (showMixedValue)
            {
                dragElement?.RemoveFromHierarchy();
                if (inputTextField != null) inputTextField.showMixedValue = true;
            }
            else
            {
                dragContainer.Add(dragElement);
                if (inputTextField != null) inputTextField.showMixedValue = false;
            }
        }

        internal override void RegisterEditingCallbacks()
        {
            labelElement.RegisterCallback<PointerDownEvent>(StartEditing, TrickleDown.TrickleDown);
            dragContainer.RegisterCallback<PointerDownEvent>(StartEditing, TrickleDown.TrickleDown);
            dragContainer.RegisterCallback<PointerUpEvent>(EndEditing);
        }

        internal override void UnregisterEditingCallbacks()
        {
            labelElement.UnregisterCallback<PointerDownEvent>(StartEditing, TrickleDown.TrickleDown);
            dragContainer.UnregisterCallback<PointerDownEvent>(StartEditing, TrickleDown.TrickleDown);
            dragContainer.UnregisterCallback<PointerUpEvent>(EndEditing);
        }
    }

    public partial class Slider : BaseSlider<float>
    {
        internal const float kDefaultHighValue = 10f;
        public new static readonly string ussClassName = "unity-slider";
        public new static readonly string labelUssClassName = ussClassName + "__label";
        public new static readonly string inputUssClassName = ussClassName + "__input";

        public Slider() : this(null) { }

        public Slider(float start, float end, SliderDirection direction = SliderDirection.Horizontal, float pageSize = 0f) : this(null, start, end, direction, pageSize) { }

        public Slider(string label, float start = 0f, float end = 10f, SliderDirection direction = SliderDirection.Horizontal, float pageSize = 0f)
            : base(label, start, end, direction, pageSize)
        {
            AddToClassList(ussClassName);
            labelElement.AddToClassList(labelUssClassName);
            visualInput.AddToClassList(inputUssClassName);
        }

        /// <summary>The value between two others, rounded to the precision one pixel of the drag
        /// container represents.</summary>
        internal override float SliderLerpUnclamped(float a, float b, float interpolant)
        {
            float lerp = Mathf.LerpUnclamped(a, b, interpolant);
            float perPixel = Mathf.Abs((highValue - lowValue) / (dragContainer.resolvedStyle.width - dragElement.resolvedStyle.width));
            int digits = perPixel == 0f
                ? Mathf.Clamp((int)(5.0 - Mathf.Log10(Mathf.Abs(perPixel))), 0, 15)
                : Mathf.Clamp(-Mathf.FloorToInt(Mathf.Log10(Mathf.Abs(perPixel))), 0, 15);
            return (float)Math.Round(lerp, digits, MidpointRounding.AwayFromZero);
        }

        internal override float SliderNormalizeValue(float currentValue, float lowerValue, float higherValue)
        {
            float range = higherValue - lowerValue;
            return Mathf.Approximately(range, 0f) ? 1f : (currentValue - lowerValue) / range;
        }

        internal override float SliderRange() => Math.Abs(highValue - lowValue);

        internal override float ParseStringToValue(string previousValue, string newValue) =>
            float.TryParse(newValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0f;

        internal override void ComputeValueFromKey(SliderKey sliderKey, bool isShift)
        {
            switch (sliderKey)
            {
                case SliderKey.None:
                    return;
                case SliderKey.Lowest:
                    value = lowValue;
                    return;
                case SliderKey.Highest:
                    value = highValue;
                    return;
            }
            bool page = sliderKey == SliderKey.LowerPage || sliderKey == SliderKey.HigherPage;
            float step = GetClosestPowerOfTen(Mathf.Abs((highValue - lowValue) * 0.01f));
            if (page) step *= pageSize;
            else if (isShift) step *= 10f;
            if (sliderKey == SliderKey.Lower || sliderKey == SliderKey.LowerPage) step = -step;
            value = RoundToMultipleOf(value + step * 0.5001f, Mathf.Abs(step));
        }
    }

    /// <summary>The clicks and drags of a slider's drag container: a repeating click while held, then
    /// free dragging once the pointer moves.</summary>
    internal partial class ClampedDragger<T> : Clickable where T : IComparable<T>
    {
        [Flags]
        public enum DragDirection
        {
            None = 0,
            LowToHigh = 1,
            HighToLow = 2,
            Free = 4,
        }

        public DragDirection dragDirection { get; set; }

        BaseSlider<T> slider { get; set; }

        public Vector2 startMousePosition { get; private set; }

        public Vector2 delta => lastMousePosition - startMousePosition;

        public event Action dragging;

        public event Action draggingEnded;

        public ClampedDragger(BaseSlider<T> slider, Action clickHandler, Action dragHandler) : base(clickHandler, 250L, 30L)
        {
            dragDirection = DragDirection.None;
            this.slider = slider;
            dragging += dragHandler;
        }

        protected override void ProcessDownEvent(EventBase evt, Vector2 localPosition, int pointerId)
        {
            startMousePosition = localPosition;
            dragDirection = DragDirection.None;
            base.ProcessDownEvent(evt, localPosition, pointerId);
            dragging?.Invoke();
        }

        protected override void ProcessUpEvent(EventBase evt, Vector2 localPosition, int pointerId)
        {
            base.ProcessUpEvent(evt, localPosition, pointerId);
            draggingEnded?.Invoke();
        }

        protected override void ProcessMoveEvent(EventBase evt, Vector2 localPosition)
        {
            base.ProcessMoveEvent(evt, localPosition);
            if (dragDirection == DragDirection.None) dragDirection = DragDirection.Free;
            if (dragDirection != DragDirection.Free) return;
            dragging?.Invoke();
        }
    }
}
