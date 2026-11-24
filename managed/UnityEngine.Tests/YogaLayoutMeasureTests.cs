// Yoga 1.9.0's C++ tests of pixel-grid rounding, measure functions, the measurement cache and
// baseline functions (YGRoundingFunctionTest, YGRoundingMeasureFuncTest, YGMeasureCacheTest,
// YGBaselineFuncTest), ported by hand. Yoga is Copyright (c) 2014-present, Facebook, Inc., MIT
// licensed.

using Unbound.UIElements.Layout;
using Xunit;

namespace UnityEngine.Tests.YogaLayout
{
    public class YGRoundingFunctionTest
    {
        [Fact]
        public void Rounding_value()
        {
            // Whole numbers stay whole despite the ceil/floor flags.
            Assert.Equal(6.0f, Yoga.RoundValueToPixelGrid(6.000001f, 2.0f, false, false));
            Assert.Equal(6.0f, Yoga.RoundValueToPixelGrid(6.000001f, 2.0f, true, false));
            Assert.Equal(6.0f, Yoga.RoundValueToPixelGrid(6.000001f, 2.0f, false, true));
            Assert.Equal(6.0f, Yoga.RoundValueToPixelGrid(5.999999f, 2.0f, false, false));
            Assert.Equal(6.0f, Yoga.RoundValueToPixelGrid(5.999999f, 2.0f, true, false));
            Assert.Equal(6.0f, Yoga.RoundValueToPixelGrid(5.999999f, 2.0f, false, true));

            // Fractions round according to the ceil/floor flags.
            Assert.Equal(6.0f, Yoga.RoundValueToPixelGrid(6.01f, 2.0f, false, false));
            Assert.Equal(6.5f, Yoga.RoundValueToPixelGrid(6.01f, 2.0f, true, false));
            Assert.Equal(6.0f, Yoga.RoundValueToPixelGrid(6.01f, 2.0f, false, true));
            Assert.Equal(6.0f, Yoga.RoundValueToPixelGrid(5.99f, 2.0f, false, false));
            Assert.Equal(6.0f, Yoga.RoundValueToPixelGrid(5.99f, 2.0f, true, false));
            Assert.Equal(5.5f, Yoga.RoundValueToPixelGrid(5.99f, 2.0f, false, true));
        }

        [Fact]
        public void UIToolkit_pixel_rounding_uses_float32_milli_epsilon_and_gates_by_release()
        {
            // Direct helper boundary cases measured on Unity 6000.0.71f1 (uitkpercent pixelRound.*).
            Assert.Equal(10f, Yoga.RoundValueToPixelGridUIToolkit(10.4989f, 1f));
            Assert.Equal(11f, Yoga.RoundValueToPixelGridUIToolkit(10.4990f, 1f));
            Assert.Equal(11f, Yoga.RoundValueToPixelGridUIToolkit(10.499023f, 1f));
            Assert.Equal(564f, Yoga.RoundValueToPixelGridUIToolkit(564.4989f, 1f));
            Assert.Equal(565f, Yoga.RoundValueToPixelGridUIToolkit(564.499023f, 1f));
            Assert.Equal(-10f, Yoga.RoundValueToPixelGridUIToolkit(-10.5009f, 1f));
            Assert.Equal(-11f, Yoga.RoundValueToPixelGridUIToolkit(-10.5010f, 1f));
            Assert.Equal(0f, Yoga.RoundValueToPixelGridUIToolkit(-0.5010f, 1f));
            Assert.Equal(-2f, Yoga.RoundValueToPixelGridUIToolkit(-2.5010f, 1f));
            Assert.Equal(-564f, Yoga.RoundValueToPixelGridUIToolkit(-564.5010f, 1f));
            Assert.Equal(0f, Yoga.RoundValueToPixelGridUIToolkit(-0.0005f, 1f));
            Assert.Equal(-1f, Yoga.RoundValueToPixelGridUIToolkit(-0.9995f, 1f));
            Assert.Equal(564f / 1.5f, Yoga.RoundValueToPixelGridUIToolkit(564.4989f / 1.5f, 1.5f));
            Assert.Equal(565f / 1.5f, Yoga.RoundValueToPixelGridUIToolkit(564.499023f / 1.5f, 1.5f));
            Assert.Equal(5f, Yoga.RoundValueToPixelGridUIToolkit(10.4989f / 2f, 2f));
            Assert.Equal(5.5f, Yoga.RoundValueToPixelGridUIToolkit(10.4990f / 2f, 2f));
            Assert.True(float.IsPositiveInfinity(Yoga.RoundValueToPixelGridUIToolkit(float.PositiveInfinity, 1f)));
            Assert.True(float.IsNegativeInfinity(Yoga.RoundValueToPixelGridUIToolkit(float.NegativeInfinity, 1f)));
            Assert.True(Yoga.IsUndefined(Yoga.RoundValueToPixelGridUIToolkit(float.NaN, 1f)));

            // Layout pass: UIToolkit config under 6000.0 rounds 564.499023/1.5 up to 565/1.5;
            // standard YogaConfig and unmeasured releases keep the 0.0001 epsilon (564/1.5).
            float LayoutHeight(YogaConfig config, float scale, float rawHeight)
            {
                config.PointScaleFactor = scale;
                var root = new YogaNode(config) { Width = YogaValue.Point(1000f), Height = YogaValue.Point(1000f) };
                var child = new YogaNode(config)
                {
                    PositionType = YogaPositionType.Absolute,
                    Width = YogaValue.Point(10f),
                    Height = YogaValue.Point(rawHeight),
                };
                child.SetPosition(YogaEdge.Left, YogaValue.Point(0f));
                child.SetPosition(YogaEdge.Top, YogaValue.Point(0f));
                root.Insert(0, child);
                root.CalculateLayout(1000f, 1000f, YogaDirection.LTR);
                return child.LayoutHeight;
            }

            using (TestRelease.Pin(TestRelease.Measured))
            {
                Assert.Equal(565f / 1.5f, LayoutHeight(YogaConfig.UIToolkit(), 1.5f, 564.499023f / 1.5f));
                Assert.Equal(564f / 1.5f, LayoutHeight(YogaConfig.UIToolkit(), 1.5f, 564.4989f / 1.5f));
                Assert.Equal(564f / 1.5f, LayoutHeight(new YogaConfig(), 1.5f, 564.499023f / 1.5f));

                // Measurement cache comparison keeps the standard 0.0001 epsilon (uitkpercent pixelRound.s1.cache).
                var uitk = YogaConfig.UIToolkit();
                uitk.PointScaleFactor = 1f;
                Assert.True(Yoga.CanUseCachedMeasurement(
                    YogaMeasureMode.AtMost, 10.4990f, YogaMeasureMode.AtMost, 40f,
                    YogaMeasureMode.AtMost, 10.4989f, YogaMeasureMode.AtMost, 40f,
                    20f, 10f, 0f, 0f, uitk));
                Assert.False(Yoga.CanUseCachedMeasurement(
                    YogaMeasureMode.AtMost, 10.5005f, YogaMeasureMode.AtMost, 40f,
                    YogaMeasureMode.AtMost, 10.4989f, YogaMeasureMode.AtMost, 40f,
                    20f, 10f, 0f, 0f, uitk));
            }

            using (TestRelease.Pin(TestRelease.Unmeasured))
            {
                Assert.Equal(564f / 1.5f, LayoutHeight(YogaConfig.UIToolkit(), 1.5f, 564.499023f / 1.5f));
            }
        }
    }

    public class YGRoundingMeasureFuncTest
    {
        static YogaMeasureFunc Fixed(float size) => (node, w, wm, h, hm) => new YogaSize(size, size);

        [Fact]
        public void Rounding_feature_with_custom_measure_func_floor()
        {
            var config = new YogaConfig();
            var root = new YogaNode(config);
            var child = new YogaNode(config) { MeasureFunction = Fixed(10.2f) };
            root.Insert(0, child);

            config.PointScaleFactor = 0.0f;
            root.CalculateLayout(float.NaN, float.NaN, YogaDirection.RTL);
            Assert.Equal(10.2f, child.LayoutWidth);
            Assert.Equal(10.2f, child.LayoutHeight);

            config.PointScaleFactor = 1.0f;
            root.CalculateLayout(float.NaN, float.NaN, YogaDirection.LTR);
            Assert.Equal(11f, child.LayoutWidth);
            Assert.Equal(11f, child.LayoutHeight);

            config.PointScaleFactor = 2.0f;
            root.CalculateLayout(float.NaN, float.NaN, YogaDirection.RTL);
            Assert.Equal(10.5f, child.LayoutWidth);
            Assert.Equal(10.5f, child.LayoutHeight);

            config.PointScaleFactor = 4.0f;
            root.CalculateLayout(float.NaN, float.NaN, YogaDirection.LTR);
            Assert.Equal(10.25f, child.LayoutWidth);
            Assert.Equal(10.25f, child.LayoutHeight);

            config.PointScaleFactor = 1.0f / 3.0f;
            root.CalculateLayout(float.NaN, float.NaN, YogaDirection.RTL);
            Assert.Equal(12.0f, child.LayoutWidth);
            Assert.Equal(12.0f, child.LayoutHeight);
        }

        [Fact]
        public void Rounding_feature_with_custom_measure_func_ceil()
        {
            var config = new YogaConfig();
            var root = new YogaNode(config);
            var child = new YogaNode(config) { MeasureFunction = Fixed(10.5f) };
            root.Insert(0, child);

            config.PointScaleFactor = 1.0f;
            root.CalculateLayout(float.NaN, float.NaN, YogaDirection.LTR);
            Assert.Equal(11f, child.LayoutWidth);
            Assert.Equal(11f, child.LayoutHeight);
        }

        [Fact]
        public void Rounding_feature_with_custom_measure_and_fractial_matching_scale()
        {
            var config = new YogaConfig();
            var root = new YogaNode(config);
            var child = new YogaNode(config);
            child.SetPosition(YogaEdge.Left, YogaValue.Point(73.625f));
            child.MeasureFunction = Fixed(0.5f);
            root.Insert(0, child);

            config.PointScaleFactor = 2.0f;
            root.CalculateLayout(float.NaN, float.NaN, YogaDirection.LTR);
            Assert.Equal(0.5f, child.LayoutWidth);
            Assert.Equal(0.5f, child.LayoutHeight);
            Assert.Equal(73.5f, child.LayoutX);
        }
    }

    public class YGMeasureCacheTest
    {
        sealed class Counter { public int Count; }

        static YogaMeasureFunc MeasureMax(Counter counter) => (node, width, widthMode, height, heightMode) =>
        {
            counter.Count++;
            return new YogaSize(
                widthMode == YogaMeasureMode.Undefined ? 10 : width,
                heightMode == YogaMeasureMode.Undefined ? 10 : height);
        };

        static YogaMeasureFunc MeasureMin(Counter counter) => (node, width, widthMode, height, heightMode) =>
        {
            counter.Count++;
            return new YogaSize(
                widthMode == YogaMeasureMode.Undefined || (widthMode == YogaMeasureMode.AtMost && width > 10) ? 10 : width,
                heightMode == YogaMeasureMode.Undefined || (heightMode == YogaMeasureMode.AtMost && height > 10) ? 10 : height);
        };

        [Fact]
        public void Measure_once_single_flexible_child()
        {
            var root = new YogaNode
            {
                FlexDirection = YogaFlexDirection.Row,
                AlignItems = YogaAlign.FlexStart,
                Width = YogaValue.Point(100),
                Height = YogaValue.Point(100),
            };
            var counter = new Counter();
            var child = new YogaNode { MeasureFunction = MeasureMax(counter), FlexGrow = 1 };
            root.Insert(0, child);

            root.CalculateLayout();
            Assert.Equal(1, counter.Count);
        }

        [Fact]
        public void Remeasure_with_same_exact_width_larger_than_needed_height()
        {
            var root = new YogaNode();
            var counter = new Counter();
            root.Insert(0, new YogaNode { MeasureFunction = MeasureMin(counter) });

            root.CalculateLayout(100, 100);
            root.CalculateLayout(100, 50);
            Assert.Equal(1, counter.Count);
        }

        [Fact]
        public void Remeasure_with_same_atmost_width_larger_than_needed_height()
        {
            var root = new YogaNode { AlignItems = YogaAlign.FlexStart };
            var counter = new Counter();
            root.Insert(0, new YogaNode { MeasureFunction = MeasureMin(counter) });

            root.CalculateLayout(100, 100);
            root.CalculateLayout(100, 50);
            Assert.Equal(1, counter.Count);
        }

        [Fact]
        public void Remeasure_with_computed_width_larger_than_needed_height()
        {
            var root = new YogaNode { AlignItems = YogaAlign.FlexStart };
            var counter = new Counter();
            root.Insert(0, new YogaNode { MeasureFunction = MeasureMin(counter) });

            root.CalculateLayout(100, 100);
            root.AlignItems = YogaAlign.Stretch;
            root.CalculateLayout(10, 50);
            Assert.Equal(1, counter.Count);
        }

        [Fact]
        public void Remeasure_with_atmost_computed_width_undefined_height()
        {
            var root = new YogaNode { AlignItems = YogaAlign.FlexStart };
            var counter = new Counter();
            root.Insert(0, new YogaNode { MeasureFunction = MeasureMin(counter) });

            root.CalculateLayout(100, float.NaN);
            root.CalculateLayout(10, float.NaN);
            Assert.Equal(1, counter.Count);
        }

        [Fact]
        public void Remeasure_with_already_measured_value_smaller_but_still_float_equal()
        {
            var counter = new Counter();
            var root = new YogaNode
            {
                Width = YogaValue.Point(288f),
                Height = YogaValue.Point(288f),
                FlexDirection = YogaFlexDirection.Row,
            };
            var child = new YogaNode { FlexDirection = YogaFlexDirection.Row };
            child.SetPadding(YogaEdge.All, YogaValue.Point(2.88f));
            root.Insert(0, child);
            child.Insert(0, new YogaNode
            {
                MeasureFunction = (node, w, wm, h, hm) =>
                {
                    counter.Count++;
                    return new YogaSize(84f, 49f);
                },
            });

            root.CalculateLayout();
            Assert.Equal(1, counter.Count);
        }
    }

    public class YGBaselineFuncTest
    {
        [Fact]
        public void Align_baseline_customer_func()
        {
            var root = new YogaNode
            {
                FlexDirection = YogaFlexDirection.Row,
                AlignItems = YogaAlign.Baseline,
                Width = YogaValue.Point(100),
                Height = YogaValue.Point(100),
            };
            var child0 = new YogaNode { Width = YogaValue.Point(50), Height = YogaValue.Point(50) };
            root.Insert(0, child0);
            var child1 = new YogaNode { Width = YogaValue.Point(50), Height = YogaValue.Point(20) };
            root.Insert(1, child1);
            var child1Child0 = new YogaNode
            {
                Width = YogaValue.Point(50),
                Height = YogaValue.Point(20),
                BaselineFunction = (node, width, height) => 10,
            };
            child1.Insert(0, child1Child0);
            root.CalculateLayout();

            Assert.Equal(0f, root.LayoutX);
            Assert.Equal(0f, root.LayoutY);
            Assert.Equal(100f, root.LayoutWidth);
            Assert.Equal(100f, root.LayoutHeight);

            Assert.Equal(0f, child0.LayoutX);
            Assert.Equal(0f, child0.LayoutY);
            Assert.Equal(50f, child0.LayoutWidth);
            Assert.Equal(50f, child0.LayoutHeight);

            Assert.Equal(50f, child1.LayoutX);
            Assert.Equal(40f, child1.LayoutY);
            Assert.Equal(50f, child1.LayoutWidth);
            Assert.Equal(20f, child1.LayoutHeight);

            Assert.Equal(0f, child1Child0.LayoutX);
            Assert.Equal(0f, child1Child0.LayoutY);
            Assert.Equal(50f, child1Child0.LayoutWidth);
            Assert.Equal(20f, child1Child0.LayoutHeight);
        }
    }
}
