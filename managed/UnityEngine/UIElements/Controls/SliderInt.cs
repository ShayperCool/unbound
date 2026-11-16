using System;
using System.Globalization;

namespace UnityEngine.UIElements
{
    /// <summary>A slider of whole numbers: values in between round to the nearest integer, its page
    /// size is a whole number, and label drags and keys move it by at least 1.</summary>
    public partial class SliderInt : BaseSlider<int>
    {
        internal const int kDefaultHighValue = 10;
        public new static readonly string ussClassName = "unity-slider-int";
        public new static readonly string labelUssClassName = ussClassName + "__label";
        public new static readonly string inputUssClassName = ussClassName + "__input";

        public SliderInt() : this(null, 0, kDefaultHighValue) { }

        public SliderInt(int start, int end, SliderDirection direction = SliderDirection.Horizontal, float pageSize = kDefaultPageSize)
            : this(null, start, end, direction, pageSize) { }

        public SliderInt(string label, int start = 0, int end = kDefaultHighValue, SliderDirection direction = SliderDirection.Horizontal, float pageSize = kDefaultPageSize)
            : base(label, start, end, direction, pageSize)
        {
            AddToClassList(ussClassName);
            labelElement.AddToClassList(labelUssClassName);
            visualInput.AddToClassList(inputUssClassName);
        }

        public override float pageSize
        {
            get => base.pageSize;
            set => base.pageSize = Mathf.RoundToInt(value);
        }

        /// <summary>A label drag moves the value by the pointer's travel times 0.03 % of the range
        /// (at least 1) per pixel, each move rounded to whole steps (measured:
        /// tests/projects/sliderdrag).</summary>
        public override void ApplyInputDeviceDelta(Vector3 delta, DeltaSpeed speed, int startValue)
        {
            double sensitivity = NumericFieldDraggerUtility.CalculateIntDragSensitivity(startValue, lowValue, highValue);
            float acceleration = NumericFieldDraggerUtility.Acceleration(speed == DeltaSpeed.Fast, speed == DeltaSpeed.Slow);
            long v = value;
            v += (long)Math.Round(NumericFieldDraggerUtility.NiceDelta(delta, acceleration) * sensitivity);
            value = (int)v;
        }

        internal override int SliderLerpUnclamped(int a, int b, float interpolant) =>
            Mathf.RoundToInt(Mathf.LerpUnclamped(a, b, interpolant));

        internal override float SliderNormalizeValue(int currentValue, int lowerValue, int higherValue) =>
            higherValue - lowerValue == 0 ? 1f : ((float)currentValue - lowerValue) / ((float)higherValue - lowerValue);

        internal override int SliderRange() => Math.Abs(highValue - lowValue);

        /// <summary>The text field's integer (expressions are not evaluated, as for
        /// <see cref="Slider"/>); 0 when it does not parse.</summary>
        internal override int ParseStringToValue(string previousValue, string newValue) =>
            int.TryParse(newValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;

        /// <summary>A click beside the dragger with a page size steps the value by the page size
        /// towards the click; without one it jumps as other sliders do.</summary>
        internal override void ComputeValueAndDirectionFromClick(float sliderLength, float dragElementLength, float dragElementPos, float dragElementLastPos)
        {
            if (Mathf.Approximately(pageSize, 0f))
            {
                base.ComputeValueAndDirectionFromClick(sliderLength, dragElementLength, dragElementPos, dragElementLastPos);
                return;
            }
            if (Mathf.Abs(sliderLength - dragElementLength) < 1e-30f) return;
            int step = (int)pageSize;
            if ((lowValue > highValue && !inverted) || (lowValue < highValue && inverted) || (direction == SliderDirection.Vertical && !inverted))
                step = -step;
            bool before = dragElementLastPos < dragElementPos;
            bool after = dragElementLastPos > dragElementPos + dragElementLength;
            bool towardsLow = inverted ? after : before;
            bool towardsHigh = inverted ? before : after;
            if (towardsLow && clampedDragger.dragDirection != ClampedDragger<int>.DragDirection.LowToHigh)
            {
                clampedDragger.dragDirection = ClampedDragger<int>.DragDirection.HighToLow;
                value = value - step;
            }
            else if (towardsHigh && clampedDragger.dragDirection != ClampedDragger<int>.DragDirection.HighToLow)
            {
                clampedDragger.dragDirection = ClampedDragger<int>.DragDirection.LowToHigh;
                value = value + step;
            }
        }

        /// <summary>Arrow keys move by a hundredth of the range rounded to a power of ten (at least
        /// 1; shift ten times that, page keys the page size), then round to a multiple of it.</summary>
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
            if (step < 1) step = 1;
            if (page) step *= pageSize;
            else if (isShift) step *= 10;
            if (sliderKey == SliderKey.Lower || sliderKey == SliderKey.LowerPage) step = -step;
            value = Mathf.RoundToInt(RoundToMultipleOf(value + step * 0.5001f, Mathf.Abs(step)));
        }
    }
}
