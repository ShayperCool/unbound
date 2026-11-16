using UnityEngine.UIElements;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>ExpressionEvaluator and the scalar numeric fields (Unity 6000.0.71f1,
    /// tests/projects/numericfields). Controls outside the probe: delayed expressions evaluated with
    /// an index and count, the seeded random function, a delayed field's drag and reading text that
    /// starts a relative expression from the edit's starting text.</summary>
    public sealed class NumericFieldTests
    {
        [Theory]
        [InlineData("2+3*4", true, 14d)]
        [InlineData("2^-1", false, 0d)]
        [InlineData("1/3*3", true, 0.999999999999999d)]
        [InlineData("1e400", false, 0d)]
        [InlineData("(2+3", false, 0d)]
        [InlineData("2+3)", true, 5d)]
        [InlineData("1,5", true, 1.5d)]
        [InlineData("2*", true, 2d)]
        [InlineData("0/0", false, 0d)]
        public void ExpressionsEvaluateAsTheReleaseDoes(string expression, bool ok, double expected)
        {
            Assert.Equal(ok, ExpressionEvaluator.Evaluate(expression, out double value));
            Assert.Equal(expected, value);
        }

        [Fact]
        public void DelayedExpressionsUseTheirValueIndexAndCount()
        {
            Assert.False(ExpressionEvaluator.Evaluate("x*2", out float _, out var delayed));
            float v = 3;
            Assert.True(delayed.Evaluate(ref v));
            Assert.Equal(6f, v);
            Assert.Equal("x 2 *", delayed.ToString());
            Assert.False(ExpressionEvaluator.Evaluate("L(0,10)", out int _, out var ramp));
            int n = 0;
            Assert.True(ramp.Evaluate(ref n, 2, 5));
            Assert.Equal(5, n);
            double single = 0;
            Assert.True(ramp.Evaluate(ref single, 0, 1));
            Assert.Equal(5d, single);
            Assert.False(ExpressionEvaluator.Evaluate("R(0,1)", out double _, out var random));
            ExpressionEvaluator.SetRandomState(7);
            double first = 0;
            random.Evaluate(ref first);
            ExpressionEvaluator.SetRandomState(7);
            double again = 0;
            random.Evaluate(ref again);
            Assert.Equal(first, again);
            Assert.InRange(first, 0, 1);
            Assert.True(ExpressionEvaluator.Evaluate("-5", out ulong u));
            Assert.Equal(0ul, u);
        }

        [Fact]
        public void TypedTextReadsRelativeToTheEditsStartingText()
        {
            Assert.True(UINumericFieldsUtils.TryConvertStringToInt("+=5", "10", out int i, out _));
            Assert.Equal(15, i);
            Assert.True(UINumericFieldsUtils.TryConvertStringToFloat("1e39", null, out float f, out _));
            Assert.Equal(float.MaxValue, f);
            Assert.True(UINumericFieldsUtils.TryConvertStringToUInt("-5", null, out uint ui, out _));
            Assert.Equal(0u, ui);
            Assert.True(UINumericFieldsUtils.TryConvertStringToDouble("-Infinity", out double d));
            Assert.Equal(double.NegativeInfinity, d);
        }

        [Fact]
        public void FieldsPrintParseAndStep()
        {
            var integer = new IntegerField("Count") { value = 7 };
            Assert.Equal("7", integer.text);
            Assert.Contains(BaseField<int>.labelDraggerVariantUssClassName, integer.labelElement.GetClasses());
            integer.formatString = "000";
            Assert.Equal("007", integer.text);
            integer.ApplyInputDeviceDelta(new Vector3(10, 0, 0), DeltaSpeed.Normal, 7);
            Assert.Equal(17, integer.value);
            var real = new FloatField { value = 1f / 3 };
            Assert.Equal("0.3333333", real.text);
            var precise = new DoubleField { value = 0.1 };
            Assert.Equal("0.1", precise.text);
            var big = new UnsignedLongField { value = ulong.MaxValue - 1 };
            big.ApplyInputDeviceDelta(new Vector3(10, 0, 0), DeltaSpeed.Fast, big.value);
            Assert.Equal(ulong.MaxValue, big.value);
            var negative = new LongField { value = long.MinValue + 1 };
            negative.ApplyInputDeviceDelta(new Vector3(-10, 0, 0), DeltaSpeed.Fast, negative.value);
            Assert.Equal(long.MinValue, negative.value);
            integer.isReadOnly = true;
            Assert.DoesNotContain(BaseField<int>.labelDraggerVariantUssClassName, integer.labelElement.GetClasses());
        }

        [Fact]
        public void ADelayedFieldShowsADraggedValueUntilTheEditEnds()
        {
            using var panel = new Panel(null, ContextType.Player, EventDispatcher.CreateDefault(), null);
            var field = new IntegerField { isDelayed = true };
            panel.visualTree.Add(field);
            field.SetValueWithoutNotify(7);
            int changes = 0;
            field.RegisterValueChangedCallback(_ => changes++);
            field.ApplyInputDeviceDelta(new Vector3(10, 0, 0), DeltaSpeed.Normal, 7);
            Assert.Equal(7, field.value);
            Assert.Equal("17", field.text);
            Assert.Equal(0, changes);
            field.StopDragging();
            Assert.Equal(17, field.value);
            Assert.Equal(1, changes);
        }
    }
}
