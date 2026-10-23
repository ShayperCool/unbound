using System.Globalization;
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
