using System.Globalization;
using System.Linq;
using System.Text;
using Unbound;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Floats and doubles print as under Unity's Mono (.NET Framework rules), not .NET's.</summary>
    public class MonoNumberFormatTests
    {
        static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        static string F(float value, string format = null) => MonoNumberFormat.Format(value, format, Invariant);
        static string D(double value, string format = null) => MonoNumberFormat.Format(value, format, Invariant);

        [Fact]
        public void TheDefaultFormatHasSevenOrFifteenSignificantDigits()
        {
            Assert.Equal("1850", F(1849.9999f));
            Assert.Equal("0.3333333", F(1f / 3f));
            Assert.Equal("0.1", F(0.1f));
            Assert.Equal("1E+07", F(1e7f));
            Assert.Equal("1.234568E+08", F(123456789f));
            Assert.Equal("0.3", D(0.1 + 0.2));
            Assert.Equal("1.79769313486232E+308", D(double.MaxValue));
        }

        [Fact]
        public void OtherFormatsRoundTheSignificantDigitsAgain()
        {
            Assert.Equal("1.01", D(1.005, "F2"));
            Assert.Equal("1.01", F(1.005f, "F2"));
            Assert.Equal("1.01", D(1.005, "G3"));
            Assert.Equal("1,234.57", D(1234.5678, "N2"));
            Assert.Equal("1.01", D(1.005, "0.00"));
            Assert.Equal("1.0050E+000", D(1.005, "E4"));
        }

        [Fact]
        public void ZeroHasNoSignAndInfinityIsSpelledOut()
        {
            Assert.Equal("0", D(-0.0));
            Assert.Equal("0.00", D(-0.001, "F2"));
            Assert.Equal("-0.01", D(-0.005, "F2"));
            Assert.Equal("Infinity", F(float.PositiveInfinity));
            Assert.Equal("-Infinity", D(double.NegativeInfinity));
            Assert.Equal("NaN", F(float.NaN));
        }

        [Fact]
        public void RoundTripTriesTheShorterPrecisionFirst()
        {
            Assert.Equal("0.1", F(0.1f, "R"));
            Assert.Equal("0.30000000000000004", D(0.1 + 0.2, "R"));
            Assert.Equal("0.333333343", F(1f / 3f, "R"));
        }

        [Fact]
        public void LargeFixedPointValuesPrintAsInTheEditor()
        {
            // Recorded by the Editor (6000.0.71f1): a Pose holding -3.4e37f, whose vector prints F2
            // by default and for "" and null. The kept digits, then zeros; not the float's exact
            // -33999999014383402399481480781255147520.
            Assert.Equal("-34000000000000000000000000000000000000.00", F(-3.4e37f, "F2"));
            Assert.Equal("-34000000000000000000000000000000000000.0", F(-3.4e37f, "F1"));
            var pose = new Pose(new Vector3(1e-30f, -3.4e37f, 2.5f), new Quaternion(-0.0123f, 0.9876f, 0.1357f, -0.0789f));
            const string Default = "((0.00, -34000000000000000000000000000000000000.00, 2.50), (-0.01230, 0.98760, 0.13570, -0.07890))";
            Assert.Equal(Default, pose.ToString());
            Assert.Equal("((0.0, -34000000000000000000000000000000000000.0, 2.5), (0.0, 1.0, 0.1, -0.1))", pose.ToString("F1"));
            Assert.Equal("((1E-30, -3.3999999E+37, 2.5), (-0.0122999996, 0.987600029, 0.135700002, -0.0789000019))", pose.ToString("G9"));
            Assert.Equal(Default, pose.ToString(""));
            Assert.Equal(Default, pose.ToString(null));
        }

        [Fact]
        public void LargeFixedPointValuesKeepTheirSignificantDigitsInEveryFixedFormat()
        {
            // The same rule for other values, both signs, doubles and the other fixed-point formats
            // with their provider's separators, groups and patterns.
            Assert.Equal("34000000000000000000000000000000000000.00", F(3.4e37f, "F2"));
            Assert.Equal("-34000000000000000000000000000000000000.000", F(-3.4e37f, "f3"));
            Assert.Equal("340282300000000000000000000000000000000", F(float.MaxValue, "F0"));
            Assert.Equal("-340282300000000000000000000000000000000.00", F(float.MinValue, "F"));
            Assert.Equal("20,000,000,000,000,000,000,000,000,000.00", F(2e28f, "N2"));
            var custom = new NumberFormatInfo { NumberDecimalSeparator = ",", NumberGroupSeparator = ".", NumberNegativePattern = 0 };
            Assert.Equal("(34.000.000.000.000.000.000.000.000.000.000.000.000,00)", MonoNumberFormat.Format(-3.4e37f, "N2", custom));
            Assert.Equal("(¤34,000,000,000,000,000,000,000,000,000,000,000,000.00)", F(-3.4e37f, "C2"));
            Assert.Equal("3,400,000,000,000,000,000,000,000,000,000,000,000,000 %", F(3.4e37f, "P0"));
            Assert.Equal("1000000000000000000000000000000.00", D(1e30, "F2"));
            Assert.Equal("-15" + new string('0', 299), D(-1.5e300, "F0"));
            Assert.Equal("179,769,313,486,232" + string.Concat(Enumerable.Repeat(",000", 98)), D(double.MaxValue, "N0"));
        }

        [Fact]
        public void LargeValuesInOtherFormatsAndValuesInDecimalRangeAreUnchanged()
        {
            Assert.Equal("1000000000000000000000000000.00", F(1e27f, "F2"));
            Assert.Equal("9999999999999990000000000000.00", D(9.99999999999999e27, "F2"));
            Assert.Equal("-3.4E+37", F(-3.4e37f));
            Assert.Equal("-3.3999999E+37", F(-3.4e37f, "G9"));
            Assert.Equal("-3.4E+37", F(-3.4e37f, "R"));
            Assert.Equal("-3.400000E+037", F(-3.4e37f, "E6"));
            Assert.Equal("0.00", F(1e-30f, "F2"));
            Assert.Equal("Infinity", F(float.PositiveInfinity, "F2"));
            Assert.Equal("-Infinity", D(double.NegativeInfinity, "N2"));
            Assert.Equal("NaN", F(float.NaN, "F2"));
        }

        [Fact]
        public void FormattingCallsFormatTheirFloatArguments()
        {
            Assert.Equal("1850 x 0.3333333", MonoNumberFormat.Format(Invariant, "{0} x {1}", 1849.9999f, 1f / 3f));
            Assert.Equal("[1.01]", MonoNumberFormat.Format(Invariant, "[{0:F2}]", 1.005));
            Assert.Equal("a0.3333333b", MonoNumberFormat.Concat("a", 1f / 3f, "b"));
            Assert.Equal("1850", MonoNumberFormat.Append(new StringBuilder(), 1849.9999f).ToString());
            float value = 1849.9999f;
            Assert.Equal("1850", MonoNumberFormat.ToString(ref value, Invariant));
        }

        [Fact]
        public void UnityTypesFormatTheirComponentsTheSameWay()
        {
            Assert.Equal("(0.33, 0.00, 1.01)", new Vector3(1f / 3f, -0.001f, 1.005f).ToString());
            Assert.Equal("1850", GetLogString(1849.9999f));
        }

        static string GetLogString(object message)
        {
            string logged = null;
            var handler = new CaptureHandler(m => logged = m);
            var logger = new Logger(handler);
            logger.Log(LogType.Log, message);
            return logged;
        }

        sealed class CaptureHandler : ILogHandler
        {
            readonly System.Action<string> m_Capture;
            public CaptureHandler(System.Action<string> capture) => m_Capture = capture;
            public void LogFormat(LogType logType, Object context, string format, params object[] args) =>
                m_Capture(string.Format(format, args));
            public void LogException(System.Exception exception, Object context) { }
        }
    }
}
