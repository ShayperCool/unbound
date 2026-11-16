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
    }
}
