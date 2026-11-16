using UnityEngine.UIElements;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Slider label drags and SliderInt (Unity 6000.0.71f1, tests/projects/sliderdrag):
    /// the values here are the reference's.</summary>
    [Collection("Frames")]
    public sealed class UitkSliderDragTests
    {
        [Fact]
        public void SlidersAreValueFieldsDrivenByTheirLabel()
        {
            Assert.True(typeof(IValueField<float>).IsAssignableFrom(typeof(Slider)));
            Assert.True(typeof(IValueField<int>).IsAssignableFrom(typeof(SliderInt)));
            Assert.False(typeof(IValueField<Vector2>).IsAssignableFrom(typeof(MinMaxSlider)));
            Assert.Contains(BaseSlider<float>.labelDraggerVariantUssClassName, new Slider(0f, 10f).labelElement.GetClasses());
        }

        [Fact]
        public void ADeltaMovesBySensitivityOfTheRange()
        {
            var slider = new Slider("Amount", 0f, 10f) { value = 2f };
            IValueField<float> field = slider;
            field.ApplyInputDeviceDelta(new Vector3(10f, 0f, 0f), DeltaSpeed.Normal, 9f);
            Assert.Equal(2.03f, slider.value);
            field.ApplyInputDeviceDelta(new Vector3(10f, 0f, 0f), DeltaSpeed.Fast, 0f);
            Assert.Equal(2.14999986f, slider.value);
            field.ApplyInputDeviceDelta(new Vector3(10f, 0f, 0f), DeltaSpeed.Slow, 0f);
            Assert.Equal(2.15749979f, slider.value);
            field.ApplyInputDeviceDelta(new Vector3(0f, 0f, 50f), DeltaSpeed.Normal, 0f);
            Assert.Equal(2.15749979f, slider.value);
        }

        [Fact]
        public void AnIntegerSliderRoundsEachMoveHalfToEven()
        {
            var slider = new SliderInt("Count", 0, 100) { value = 50 };
            slider.ApplyInputDeviceDelta(new Vector3(3f, 0f, 0f), DeltaSpeed.Slow, 50);
            Assert.Equal(51, slider.value);
            slider.ApplyInputDeviceDelta(new Vector3(2f, 0f, 0f), DeltaSpeed.Slow, 50);
            Assert.Equal(51, slider.value);
            slider.ApplyInputDeviceDelta(new Vector3(-12f, 0f, 0f), DeltaSpeed.Normal, 0);
            Assert.Equal(39, slider.value);
            slider.ApplyInputDeviceDelta(new Vector3(500f, 0f, 0f), DeltaSpeed.Normal, 0);
            Assert.Equal(100, slider.value);
        }

        [Fact]
        public void SliderIntState()
        {
            var plain = new SliderInt();
            Assert.Equal((0, 10, 0, 0f, 10), (plain.lowValue, plain.highValue, plain.value, plain.pageSize, plain.range));
            Assert.Contains("unity-slider-int", plain.GetClasses());
            Assert.Contains("unity-slider-int__label", plain.labelElement.GetClasses());
            plain.pageSize = 2.6f;
            Assert.Equal(3f, plain.pageSize);
            plain.pageSize = 2.5f;
            Assert.Equal(2f, plain.pageSize);
            var reversed = new SliderInt("Reversed", 10, 0) { value = 4 };
            Assert.Equal(10, reversed.range);
            reversed.value = 15;
            Assert.Equal(10, reversed.value);
        }

        /// <summary>The integer slider's tracker rules without a panel (the probe's `track*` and
        /// `drag.*` rows measure them through events).</summary>
        [Theory]
        [InlineData(0, 10, false, SliderDirection.Horizontal, true, 3)]
        [InlineData(0, 10, false, SliderDirection.Horizontal, false, 7)]
        [InlineData(0, 10, true, SliderDirection.Horizontal, true, 3)]
        [InlineData(10, 0, false, SliderDirection.Horizontal, true, 7)]
        [InlineData(0, 10, false, SliderDirection.Vertical, true, 7)]
        [InlineData(0, 10, true, SliderDirection.Vertical, true, 3)]
        [InlineData(10, 0, true, SliderDirection.Horizontal, true, 7)]
        [InlineData(10, 0, true, SliderDirection.Vertical, true, 7)]
        public void ATrackerClickStepsByThePageSize(int low, int high, bool inverted, SliderDirection direction, bool beforeDragger, int expected)
        {
            var slider = new SliderInt(null, low, high, direction, 2f) { value = 5, inverted = inverted };
            slider.clampedDragger.dragDirection = ClampedDragger<int>.DragDirection.None;
            slider.ComputeValueAndDirectionFromClick(300f, 20f, 140f, beforeDragger ? 10f : 290f);
            Assert.Equal(expected, slider.value);
        }

        [Fact]
        public void ATrackerWithoutRoomOrPageSizeLeavesTheValue()
        {
            var slider = new SliderInt(null, 0, 10, SliderDirection.Horizontal, 2f) { value = 5 };
            slider.clampedDragger.dragDirection = ClampedDragger<int>.DragDirection.None;
            slider.ComputeValueAndDirectionFromClick(20f, 20f, 0f, 10f);
            Assert.Equal(5, slider.value);
        }

        [Fact]
        public void DraggerPositionsRoundToWholeValues()
        {
            var slider = new SliderInt(null, 0, 10);
            Assert.Equal(4, slider.SliderLerpUnclamped(0, 10, 0.45f));
            Assert.Equal(6, slider.SliderLerpUnclamped(0, 10, 0.55f));
            Assert.Equal(-2, slider.SliderLerpUnclamped(0, 10, -0.2f));
            Assert.Equal(1f, slider.SliderNormalizeValue(3, 4, 4));
            Assert.Equal(0.25f, slider.SliderNormalizeValue(3, 2, 6));
        }
    }
}
