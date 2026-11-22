using System;
using System.Text;

namespace Unbound.Runtime
{
    /// <summary>
    /// The numbers of JsonUtility's text. Written: the digits of the Grisu2 algorithm (Loitsch,
    /// "Printing Floating-Point Numbers Quickly and Accurately with Integers", 2010), which are as
    /// few as read the double back; the last digit is moved towards the number only while at most
    /// <see cref="AdjustedFractionDigits"/> digits follow the whole part, so a longer fraction
    /// keeps the digit generated (0.2f is written 0.20000000298023225, not the closer ...224).
    /// Then the digits are laid out as a decimal up to 21 digits, else with an
    /// exponent. Matches the Unity 6000.0.71f1 Editor, tests/projects/builtinserial `json.numbers`
    /// and `json.curveGradient`. Read: <see cref="TryRead"/>.
    /// </summary>
    internal static class JsonNumbers
    {
        const int SignificandBits = 52;
        const ulong HiddenBit = 1UL << SignificandBits;
        const ulong SignificandMask = HiddenBit - 1;
        const int ExponentBias = 0x3FF + SignificandBits;
        const int DenormalExponent = 1 - ExponentBias;

        /// <summary>10^k for k = -348, -340, ..., 340: 64-bit significands rounded to nearest,
        /// and their binary exponents.</summary>
        static readonly ulong[] s_PowerSignificands =
        {
            0xfa8fd5a0081c0288UL, 0xbaaee17fa23ebf76UL, 0x8b16fb203055ac76UL, 0xcf42894a5dce35eaUL,
            0x9a6bb0aa55653b2dUL, 0xe61acf033d1a45dfUL, 0xab70fe17c79ac6caUL, 0xff77b1fcbebcdc4fUL,
            0xbe5691ef416bd60cUL, 0x8dd01fad907ffc3cUL, 0xd3515c2831559a83UL, 0x9d71ac8fada6c9b5UL,
            0xea9c227723ee8bcbUL, 0xaecc49914078536dUL, 0x823c12795db6ce57UL, 0xc21094364dfb5637UL,
            0x9096ea6f3848984fUL, 0xd77485cb25823ac7UL, 0xa086cfcd97bf97f4UL, 0xef340a98172aace5UL,
            0xb23867fb2a35b28eUL, 0x84c8d4dfd2c63f3bUL, 0xc5dd44271ad3cdbaUL, 0x936b9fcebb25c996UL,
            0xdbac6c247d62a584UL, 0xa3ab66580d5fdaf6UL, 0xf3e2f893dec3f126UL, 0xb5b5ada8aaff80b8UL,
            0x87625f056c7c4a8bUL, 0xc9bcff6034c13053UL, 0x964e858c91ba2655UL, 0xdff9772470297ebdUL,
            0xa6dfbd9fb8e5b88fUL, 0xf8a95fcf88747d94UL, 0xb94470938fa89bcfUL, 0x8a08f0f8bf0f156bUL,
            0xcdb02555653131b6UL, 0x993fe2c6d07b7facUL, 0xe45c10c42a2b3b06UL, 0xaa242499697392d3UL,
            0xfd87b5f28300ca0eUL, 0xbce5086492111aebUL, 0x8cbccc096f5088ccUL, 0xd1b71758e219652cUL,
            0x9c40000000000000UL, 0xe8d4a51000000000UL, 0xad78ebc5ac620000UL, 0x813f3978f8940984UL,
            0xc097ce7bc90715b3UL, 0x8f7e32ce7bea5c70UL, 0xd5d238a4abe98068UL, 0x9f4f2726179a2245UL,
            0xed63a231d4c4fb27UL, 0xb0de65388cc8ada8UL, 0x83c7088e1aab65dbUL, 0xc45d1df942711d9aUL,
            0x924d692ca61be758UL, 0xda01ee641a708deaUL, 0xa26da3999aef774aUL, 0xf209787bb47d6b85UL,
            0xb454e4a179dd1877UL, 0x865b86925b9bc5c2UL, 0xc83553c5c8965d3dUL, 0x952ab45cfa97a0b3UL,
            0xde469fbd99a05fe3UL, 0xa59bc234db398c25UL, 0xf6c69a72a3989f5cUL, 0xb7dcbf5354e9beceUL,
            0x88fcf317f22241e2UL, 0xcc20ce9bd35c78a5UL, 0x98165af37b2153dfUL, 0xe2a0b5dc971f303aUL,
            0xa8d9d1535ce3b396UL, 0xfb9b7cd9a4a7443cUL, 0xbb764c4ca7a44410UL, 0x8bab8eefb6409c1aUL,
            0xd01fef10a657842cUL, 0x9b10a4e5e9913129UL, 0xe7109bfba19c0c9dUL, 0xac2820d9623bf429UL,
            0x80444b5e7aa7cf85UL, 0xbf21e44003acdd2dUL, 0x8e679c2f5e44ff8fUL, 0xd433179d9c8cb841UL,
            0x9e19db92b4e31ba9UL, 0xeb96bf6ebadf77d9UL, 0xaf87023b9bf0ee6bUL,
        };
        static readonly short[] s_PowerExponents =
        {
            -1220, -1193, -1166, -1140, -1113, -1087, -1060, -1034, -1007, -980, -954, -927,
            -901, -874, -847, -821, -794, -768, -741, -715, -688, -661, -635, -608,
            -582, -555, -529, -502, -475, -449, -422, -396, -369, -343, -316, -289,
            -263, -236, -210, -183, -157, -130, -103, -77, -50, -24, 3, 30,
            56, 83, 109, 136, 162, 189, 216, 242, 269, 295, 322, 348,
            375, 402, 428, 455, 481, 508, 534, 561, 588, 614, 641, 667,
            694, 720, 747, 774, 800, 827, 853, 880, 907, 933, 960, 986,
            1013, 1039, 1066,
        };
        const int FirstPower = -348, PowerStep = 8;
        /// <summary>log10(2), to find the cached power that brings a number's exponent near -61,
        /// and the offset of that estimate (one less than -<see cref="FirstPower"/>, so that the
        /// estimate rounds up to the table's index).</summary>
        const double Log10Of2 = 0.30102999566398114;
        const int PowerOffset = 347;

        static readonly uint[] s_Pow10 = { 1, 10, 100, 1000, 10000, 100000, 1000000, 10000000, 100000000, 1000000000 };
        /// <summary>The most fractional digits after which the last digit is still moved towards
        /// the number.</summary>
        const int AdjustedFractionDigits = 9;

        struct DiyFp
        {
            internal ulong F;
            internal int E;

            internal DiyFp(ulong f, int e) { F = f; E = e; }

            internal static DiyFp Of(double value)
            {
                ulong bits = (ulong)BitConverter.DoubleToInt64Bits(value);
                int biased = (int)((bits >> SignificandBits) & 0x7FF);
                ulong significand = bits & SignificandMask;
                return biased != 0 ? new DiyFp(significand + HiddenBit, biased - ExponentBias) : new DiyFp(significand, DenormalExponent);
            }

            internal DiyFp Times(DiyFp other)
            {
                ulong high = Math.BigMul(F, other.F, out ulong low);
                // Rounded to nearest by the low half's top bit.
                return new DiyFp(high + (low >> 63), E + other.E + 64);
            }

            internal DiyFp Normalized()
            {
                var result = this;
                while ((result.F & (1UL << 63)) == 0) { result.F <<= 1; result.E--; }
                return result;
            }

            /// <summary>The boundaries halfway to the neighbouring doubles, on one exponent.</summary>
            internal void Boundaries(out DiyFp minus, out DiyFp plus)
            {
                var p = new DiyFp((F << 1) + 1, E - 1);
                while ((p.F & (HiddenBit << 1)) == 0) { p.F <<= 1; p.E--; }
                const int Spare = 64 - SignificandBits - 2;
                p.F <<= Spare;
                p.E -= Spare;
                var m = F == HiddenBit ? new DiyFp((F << 2) - 1, E - 2) : new DiyFp((F << 1) - 1, E - 1);
                m.F <<= m.E - p.E;
                m.E = p.E;
                minus = m;
                plus = p;
            }
        }

        static DiyFp CachedPower(int e, out int k)
        {
            double dk = (-61 - e) * Log10Of2 + PowerOffset;
            int ik = (int)dk;
            if (dk - ik > 0.0) ik++;
            int index = (ik >> 3) + 1;
            k = -(FirstPower + index * PowerStep);
            return new DiyFp(s_PowerSignificands[index], s_PowerExponents[index]);
        }

        static int DecimalDigits(uint n) =>
            n < 10 ? 1 : n < 100 ? 2 : n < 1000 ? 3 : n < 10000 ? 4 : n < 100000 ? 5 : n < 1000000 ? 6 : n < 10000000 ? 7 : n < 100000000 ? 8 : 9;

        static void Round(Span<char> buffer, int length, ulong delta, ulong rest, ulong tenKappa, ulong wpw)
        {
            while (rest < wpw && delta - rest >= tenKappa && (rest + tenKappa < wpw || wpw - rest > rest + tenKappa - wpw))
            {
                buffer[length - 1]--;
                rest += tenKappa;
            }
        }

        static int Digits(DiyFp w, DiyFp mp, ulong delta, Span<char> buffer, ref int k)
        {
            var one = new DiyFp(1UL << -mp.E, mp.E);
            ulong wpw = mp.F - w.F;
            uint p1 = (uint)(mp.F >> -one.E);
            ulong p2 = mp.F & (one.F - 1);
            int kappa = DecimalDigits(p1);
            int length = 0;
            while (kappa > 0)
            {
                uint unit = s_Pow10[kappa - 1];
                uint d = p1 / unit;
                p1 %= unit;
                if (d != 0 || length != 0) buffer[length++] = (char)('0' + d);
                kappa--;
                ulong rest = ((ulong)p1 << -one.E) + p2;
                if (rest <= delta)
                {
                    k += kappa;
                    Round(buffer, length, delta, rest, (ulong)s_Pow10[kappa] << -one.E, wpw);
                    return length;
                }
            }
            int fractional = 0;
            while (true)
            {
                p2 *= 10;
                delta *= 10;
                fractional++;
                char d = (char)(p2 >> -one.E);
                if (d != 0 || length != 0) buffer[length++] = (char)('0' + d);
                p2 &= one.F - 1;
                kappa--;
                if (p2 < delta)
                {
                    k += kappa;
                    if (fractional <= AdjustedFractionDigits) Round(buffer, length, delta, p2, one.F, wpw * s_Pow10[fractional]);
                    return length;
                }
            }
        }

        /// <summary>The text of a finite number (NaN and the infinities are the caller's).</summary>
        internal static string Format(double value)
        {
            var text = new StringBuilder(32);
            Append(text, value);
            return text.ToString();
        }

        /// <summary>The most digits a double is written with (17), with room to spare.</summary>
        const int DigitBuffer = 24;

        /// <summary>Appends the text of a finite number.</summary>
        internal static void Append(StringBuilder text, double value)
        {
            // Negative zero is written without its sign.
            if (value == 0) { text.Append("0.0"); return; }
            if (value < 0) { text.Append('-'); value = -value; }
            var v = DiyFp.Of(value);
            v.Boundaries(out var minus, out var plus);
            var cached = CachedPower(plus.E, out int k);
            var w = v.Normalized().Times(cached);
            var wp = plus.Times(cached);
            var wm = minus.Times(cached);
            wm.F++;
            wp.F--;
            Span<char> buffer = stackalloc char[DigitBuffer];
            int length = Digits(w, wp, wp.F - wm.F, buffer, ref k);
            Layout(text, buffer.Slice(0, length), k);
        }

        /// <summary>10^0 .. 10^308, each the nearest double.</summary>
        static readonly double[] s_PowersOfTen = PowersOfTen();
        const int LargestPower = 308;

        static double[] PowersOfTen()
        {
            var powers = new double[LargestPower + 1];
            for (int i = 0; i <= LargestPower; i++)
                powers[i] = double.Parse("1e" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture);
            return powers;
        }

        /// <summary>The largest whole numbers that take one more digit as an integer: an
        /// unsigned 64-bit one (and then a last digit up to 5), a negative one (up to 8).</summary>
        const ulong UnsignedTenth = ulong.MaxValue / 10;
        const ulong NegativeTenth = (ulong)long.MaxValue / 10;
        /// <summary>The whole part as a double takes no further digit from here on.</summary>
        const double WholeLimit = 1.7976931348623157e307;
        const int ExponentDigitsLimit = 100000;

        static bool IsDigit(char c) => c >= '0' && c <= '9';

        static double Scaled(double d, int power)
        {
            // Only a fraction of more than 308 digits lets the exponent alone go beyond the table.
            for (; power > LargestPower; power -= LargestPower) d *= s_PowersOfTen[LargestPower];
            return power < -LargestPower ? 0.0 : power >= 0 ? d * s_PowersOfTen[power] : d / s_PowersOfTen[-power];
        }

        /// <summary>
        /// The double JsonUtility reads from a number's text, which is not always the nearest
        /// one: the digits are gathered into a 64-bit integer while they fit and into a double
        /// from there on (each step rounded), and the result is that times or divided by one
        /// power of ten. A number written without a fraction or an exponent that fits an integer
        /// is that integer ("-0" is 0, "-0.0" is -0). An exponent below -308 reads as 0; when
        /// only the exponent and the fraction's digits together go below it, the number is
        /// scaled by the exponent and then by the digits. False for a number too big for a
        /// double. <paramref name="read"/> is how much of the text was a number. Matches the Unity
        /// 6000.0.71f1 Editor, tests/projects/builtinserial `json.numbers.fromJson` and
        /// `json.numbers.read.*`. Not measured: negative numbers with twenty and more digits of
        /// fraction; a fraction of more than 308 digits (0 here unless an exponent brings it
        /// back); a number whose digits alone exceed a double (false here, or 0 with such a
        /// fraction).
        /// </summary>
        internal static bool TryRead(ReadOnlySpan<char> s, out double value, out int read)
        {
            value = 0;
            read = 0;
            int p = 0, n = s.Length;
            bool minus = p < n && s[p] == '-';
            if (minus) p++;
            ulong whole = 0;
            double d = 0;
            bool inDouble = false;
            ulong tenth = minus ? NegativeTenth : UnsignedTenth;
            char lastDigit = minus ? '8' : '5';
            if (p < n && s[p] == '0') p++;
            else
            {
                while (p < n && IsDigit(s[p]))
                {
                    if (whole >= tenth && (whole != tenth || s[p] > lastDigit)) { d = whole; inDouble = true; break; }
                    whole = whole * 10 + (uint)(s[p++] - '0');
                }
            }
            while (inDouble && p < n && IsDigit(s[p]))
            {
                if (d >= WholeLimit) return false;
                d = d * 10 + (s[p++] - '0');
            }
            bool integer = !inDouble;
            int fraction = 0;
            if (p < n && s[p] == '.')
            {
                p++;
                integer = false;
                if (!inDouble)
                {
                    while (p < n && IsDigit(s[p]))
                    {
                        if (whole >= UnsignedTenth && (whole != UnsignedTenth || s[p] > '5')) break;
                        whole = whole * 10 + (uint)(s[p++] - '0');
                        fraction--;
                    }
                    d = whole;
                    inDouble = true;
                }
                while (p < n && IsDigit(s[p]))
                {
                    d = d * 10 + (s[p++] - '0');
                    fraction--;
                }
            }
            int exponent = 0;
            if (p < n && (s[p] == 'e' || s[p] == 'E'))
            {
                p++;
                integer = false;
                if (!inDouble) d = whole;
                bool negative = false;
                if (p < n && (s[p] == '+' || s[p] == '-')) negative = s[p++] == '-';
                for (; p < n && IsDigit(s[p]); p++)
                    if (exponent < ExponentDigitsLimit) exponent = exponent * 10 + (s[p] - '0');
                if (negative) exponent = -exponent;
                else if (exponent > LargestPower - fraction) return false;
            }
            read = p;
            if (integer)
            {
                value = whole == 0 ? 0.0 : minus ? -(double)whole : whole;
                return true;
            }
            int power = exponent + fraction;
            if (exponent < -LargestPower) d = 0.0;
            else if (power < -LargestPower) d = Scaled(Scaled(d, exponent), fraction);
            else d = Scaled(d, power);
            if (!double.IsFinite(d)) return false;
            value = minus ? -d : d;
            return true;
        }

        /// <summary>Digits d1..dn times 10^k as text: a plain decimal while the point falls within
        /// 21 digits (".0" after a whole number), "0.000ddd" down to six zeros, else an exponent.</summary>
        static void Layout(StringBuilder text, ReadOnlySpan<char> digits, int k)
        {
            int length = digits.Length;
            int point = length + k;
            if (k >= 0 && point <= 21)
            {
                text.Append(digits).Append('0', k).Append(".0");
            }
            else if (point > 0 && point <= 21)
            {
                text.Append(digits.Slice(0, point)).Append('.').Append(digits.Slice(point));
            }
            else if (point > -6 && point <= 0)
            {
                text.Append("0.").Append('0', -point).Append(digits);
            }
            else
            {
                text.Append(digits[0]);
                if (length > 1) text.Append('.').Append(digits.Slice(1));
                text.Append('e');
                int exponent = point - 1;
                if (exponent < 0) { text.Append('-'); exponent = -exponent; }
                text.Append(exponent.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }
    }
}
