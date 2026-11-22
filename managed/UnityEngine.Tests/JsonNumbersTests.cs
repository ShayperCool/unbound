using System;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The number text JsonUtility writes (tests/projects/builtinserial
    /// `json.curveGradient`, `json.numbers`: the Editor writes 0.2f, 0.4f and 0.6f as below) and
    /// the doubles it reads from number text (`json.numbers.fromJson`, `json.numbers.read.*`).</summary>
    public class JsonNumbersTests
    {
        [Theory]
        // As few digits as read the double back, not always the closest of those.
        [InlineData(0.2f, "0.20000000298023225")]
        [InlineData(0.4f, "0.4000000059604645")]
        [InlineData(0.6f, "0.6000000238418579")]
        [InlineData(0.25f, "0.25")]
        public void FloatsAreWrittenAsTheEditorWritesThem(float value, string text) => Assert.Equal(text, JsonNumbers.Format(value));

        [Theory]
        [InlineData(0.0, "0.0")]
        [InlineData(-0.0, "0.0")]
        [InlineData(1.0, "1.0")]
        [InlineData(-2.5, "-2.5")]
        [InlineData(100.0, "100.0")]
        [InlineData(0.1, "0.1")]
        [InlineData(1.0 / 3.0, "0.3333333333333333")]
        // A whole number up to 21 digits, then an exponent.
        [InlineData(123456789012345678901.0, "123456789012345680000.0")]
        [InlineData(1e21, "1e21")]
        [InlineData(1.5e22, "1.5e22")]
        // Down to six zeros after the point, then an exponent.
        [InlineData(0.0000015, "0.0000015")]
        [InlineData(1e-7, "1e-7")]
        [InlineData(1.25e-10, "1.25e-10")]
        [InlineData(double.MaxValue, "1.7976931348623157e308")]
        [InlineData(double.Epsilon, "5e-324")]
        [InlineData(2.2250738585072014e-308, "2.2250738585072014e-308")]
        public void DoublesAreLaidOutAsDecimalsOrWithAnExponent(double value, string text) => Assert.Equal(text, JsonNumbers.Format(value));

        [Theory]
        // The Editor's digits for doubles whose shortest text ends otherwise.
        [InlineData(1.0 / 7.0, "0.14285714285714286")]
        [InlineData(4.0 / 3e-5, "133333.33333333335")]
        [InlineData(6.8474229291646805e-84, "6.8474229291646809e-84")]
        [InlineData(6.8304626092319105e+276, "6.8304626092319109e276")]
        public void DoublesAreWrittenAsTheEditorWritesThem(double value, string text) => Assert.Equal(text, JsonNumbers.Format(value));

        static string Read(string text) =>
            !JsonNumbers.TryRead(text, out double value, out int read) ? "too big"
            : read != text.Length ? "read " + read
            : BitConverter.DoubleToInt64Bits(value).ToString("x16");

        [Theory]
        // Whole numbers: an integer while it fits one, else digit by digit in a double.
        [InlineData("7", "401c000000000000")]
        [InlineData("-0", "0000000000000000")]
        [InlineData("-0.0", "8000000000000000")]
        [InlineData("18446744073709551615", "43f0000000000000")]
        [InlineData("123456789012345678901", "441ac53a7e04bcd9")]
        [InlineData("-9223372036854775809", "c3e0000000000000")]
        [InlineData("123456789012345678901234567890", "45f8ee90ff6c373d")]
        // The same whole number with a fraction is scaled back by a division: another double.
        [InlineData("123456789012345680000.0", "441ac53a7e04bcda")]
        [InlineData("9007199254740993", "4340000000000000")]
        [InlineData("9007199254740993.0", "4340000000000001")]
        // Not the nearest double: 4/3e-5 and 1/(5*997) as the Editor writes them.
        [InlineData("133333.33333333335", "410046aaaaaaaaac")]
        [InlineData("0.00020060180541624874", "3f2a4b1463374eed")]
        [InlineData("1.00000000000000000000000001", "3feffffffffffffe")]
        // More digits than a 64-bit integer holds: the last one it takes is at most 5.
        [InlineData("1.8446744073709551615", "3ffd83c94fb6d2ac")]
        [InlineData("1.84467440737095516150001", "3ffd83c94fb6d2ad")]
        [InlineData("9.29058107097009616897993", "402294c70ac94e72")]
        [InlineData("1E5", "40f86a0000000000")]
        [InlineData("1e+5", "40f86a0000000000")]
        [InlineData("1.7976931348623157e308", "7fefffffffffffff")]
        // Below the smallest normal double: an exponent under -308 reads as 0, whatever the digits.
        [InlineData("2.2250738585072014e-308", "0010000000000000")]
        [InlineData("2e-308", "000e61acf033d1a4")]
        [InlineData("1e-310", "0000000000000000")]
        [InlineData("22250738585072014e-324", "0000000000000000")]
        [InlineData("0.00000000000000000000000000000000000001e-280", "00000000000316a2")]
        // The exponent first, then the fraction's digits (the other order gives ...634).
        [InlineData("99.9999999999999999e-304", "013b6e3d22865635")]
        [InlineData("1e-400", "0000000000000000")]
        [InlineData("1e400", "too big")]
        // What follows a number is not read (JsonUtility then keeps what the whole text parses to).
        [InlineData("007", "read 1")]
        [InlineData("1.2.3", "read 3")]
        public void NumberTextIsReadAsTheEditorReadsIt(string text, string bits) => Assert.Equal(bits, Read(text));

        [Serializable]
        class Numbers
        {
            public float[] floats = new float[0];
            public double[] doubles = new double[0];
        }

        [Fact]
        public void FromJsonReadsNumbersThatWayAndRefusesOnesTooBig()
        {
            var read = UnityEngine.JsonUtility.FromJson<Numbers>("{\"floats\":[0.20000000298023225,3.4028236e38,1e-46,-0.0],\"doubles\":[133333.33333333335,-0,1e-310]}");
            Assert.Equal(new[] { 0.2f, float.PositiveInfinity, 0f, -0f }, read.floats);
            Assert.True(float.IsNegative(read.floats[3]));
            Assert.Equal("410046aaaaaaaaac", BitConverter.DoubleToInt64Bits(read.doubles[0]).ToString("x16"));
            Assert.False(double.IsNegative(read.doubles[1]));
            Assert.Equal(0.0, read.doubles[2]);
            var error = Assert.Throws<ArgumentException>(() => UnityEngine.JsonUtility.FromJson<Numbers>("{\"doubles\":[1e400,1]}"));
            Assert.Equal("JSON parse error: Number too big to be stored in double.", error.Message);
        }

        [Fact]
        public void AFractionOfHundredsOfDigitsStaysInRange()
        {
            // The exponent alone is beyond the powers of ten a double holds; the value is 1e-350.
            Assert.Equal("0000000000000000", Read("0." + new string('0', 699) + "1e350"));
            // 0.5e309 is 5e308, too big; 0.1e309 is 1e308.
            Assert.Equal("too big", Read("0.5e309"));
            Assert.Equal(BitConverter.DoubleToInt64Bits(1e308).ToString("x16"), Read("0.1e309"));
        }

        [Fact]
        public void TextThatIsNotOneNumberKeepsWhatItParsesTo()
        {
            var read = UnityEngine.JsonUtility.FromJson<Numbers>("{\"doubles\":[007,1.2.3,5]}");
            Assert.Equal(new[] { 7.0, 0.0, 5.0 }, read.doubles);
        }

        [Fact]
        public void AnotherFamilyKeepsTheShortestTextAndTheNearestDouble()
        {
            using var release = TestRelease.Pin(TestRelease.Unmeasured);
            var numbers = new Numbers { floats = new[] { 0.2f }, doubles = new[] { 1.0 / 7.0, -0.0, 3.0 } };
            Assert.Equal("{\"floats\":[0.20000000298023224],\"doubles\":[0.14285714285714285,-0.0,3.0]}", UnityEngine.JsonUtility.ToJson(numbers));
            var read = UnityEngine.JsonUtility.FromJson<Numbers>("{\"doubles\":[133333.33333333335,5e-324]}");
            Assert.Equal(new[] { 133333.33333333335, double.Epsilon }, read.doubles);
        }

        [Fact]
        public void AGradientTakesNoMoreKeysThanItHolds()
        {
            var read = UnityEngine.JsonUtility.FromJson<Gradients>("{\"over\":{\"m_NumColorKeys\":50,\"m_NumAlphaKeys\":-3}}");
            Assert.Equal(8, read.over.colorKeys.Length);
        }

        [Serializable]
        class Gradients
        {
            public Gradient over = new Gradient();
        }
    }
}
