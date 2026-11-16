using System;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace Unbound
{
    /// <summary>
    /// Floating-point formatting as Unity's Mono class library does it (.NET Framework rules), which
    /// .NET Core 3.0 changed: the default and "G" formats print 7 significant digits for float and 15
    /// for double (not the shortest round-trippable string, so <c>1849.9999f</c> prints "1850"), the
    /// other formats round those digits again ("F2" of <c>1.005</c> is "1.01", "G8" of a float takes 9
    /// digits and rounds them to 8), "R" tries 7/15 digits before 9/17, a value exactly halfway
    /// between two texts rounds away from zero (.NET rounds it to the even digit), and a value that
    /// rounds to zero prints no minus sign.
    /// Game assemblies' calls are redirected here when they load (Unbound.Host.NumberFormatShims);
    /// the methods take an instance's value by reference first, like the call they replace.
    /// </summary>
    public static class MonoNumberFormat
    {
        // The significant digits the Framework takes from a value: 7 or 15, or 9 or 17 when the
        // format asks for more ("R" after the shorter text, "G" over 7 or 15 digits, "E" over 6 or
        // 14), each rounded half away from zero (tests/projects/floattext).
        const int SinglePrecision = 7;
        const int DoublePrecision = 15;
        const int SingleExtendedPrecision = 9;
        const int DoubleExtendedPrecision = 17;

        public static string Format(float value, string format, IFormatProvider provider) =>
            Format(value, format, provider, true);

        public static string Format(double value, string format, IFormatProvider provider) =>
            Format(value, format, provider, false);

        static string Format(double value, string format, IFormatProvider provider, bool single)
        {
            var info = NumberFormatInfo.GetInstance(provider);
            // Mono's cultures spell infinity out where ICU's use "∞".
            if (double.IsNaN(value)) return info.NaNSymbol;
            if (double.IsInfinity(value))
            {
                var symbol = value > 0 ? info.PositiveInfinitySymbol : info.NegativeInfinitySymbol;
                return symbol.Replace("∞", "Infinity", StringComparison.Ordinal);
            }
            int precision = single ? SinglePrecision : DoublePrecision;
            int extended = single ? SingleExtendedPrecision : DoubleExtendedPrecision;
            if (value == 0) value = 0; // no negative zero
            if (IsStandard(format, out char kind, out int digits))
            {
                bool lower = char.IsLower(kind);
                switch (char.ToUpperInvariant(kind))
                {
                    case 'R':
                    {
                        // The shorter precision when it reads back as the same value; "r" too prints
                        // an upper-case exponent.
                        string shorter = General(value, precision, precision, precision, info, false);
                        bool same = single
                            ? float.TryParse(shorter, NumberStyles.Float, provider, out var f) && f == (float)value
                            : double.TryParse(shorter, NumberStyles.Float, provider, out var d) && d == value;
                        if (same) return shorter;
                        return General(value, extended, extended, extended, info, false);
                    }
                    case 'G':
                        if (digits <= 0) return General(value, precision, precision, precision, info, lower);
                        int taken = digits > precision ? extended : precision;
                        return General(value, taken, Math.Min(digits, taken), digits, info, lower);
                    case 'E':
                        if (digits < 0) digits = 6;
                        return Exponential(value, digits >= precision ? extended : precision, digits + 1, info, lower);
                    case 'D':
                    case 'X':
                    case 'B':
                        // Invalid for floating point in both: let .NET throw its FormatException.
                        return value.ToString(format, provider);
                }
            }
            // The value's first `precision` significant digits, formatted as a decimal number: the
            // Framework rounds those digits again (half away from zero) and drops the sign of a zero.
            var significant = Digits(value, precision, out int exponent);
            if (TryDigitsAsDecimal(significant, exponent, value < 0, out var number)) return number.ToString(format, provider);
            // Beyond decimal's range a fixed-point format still prints those digits, then zeros
            // (-3.4e37f "F2" is "-34000000000000000000000000000000000000.00", not the float's exact
            // value): every kept digit is then before the point, so nothing rounds, and an integer
            // holding them goes through the same .NET formatting as the decimal above.
            if (IsFixedPoint(format) && TryDigitsAsInteger(significant, exponent, value < 0, out var integer))
                return integer.ToString(format, provider);
            return single ? ((float)value).ToString(format, provider) : value.ToString(format, provider);
        }

        /// <summary>The first <paramref name="count"/> significant digits of the value's magnitude
        /// and the decimal exponent of the first, as the Framework takes them: the exact value
        /// rounded half away from zero. .NET's exact formatting rounds a tie (the exact value has
        /// <paramref name="count"/> + 1 digits, the last a 5) to the even digit, so a tie is taken
        /// from its <paramref name="count"/> + 1 exact digits instead and rounded up (Unity
        /// 6000.0.71f1, tests/projects/floattext: <c>245f / 1024</c>, 0.2392578125, is "0.239257813"
        /// in "R"; 2^-25 is "2.9802322387695313E-08").</summary>
        static string Digits(double value, int count, out int exponent)
        {
            double magnitude = Math.Abs(value);
            if (magnitude == 0)
            {
                exponent = 0;
                return new string('0', count);
            }
            bool tie = IsTie(magnitude, count);
            string text = magnitude.ToString("E" + (tie ? count : count - 1).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            int e = text.IndexOf('E');
            exponent = int.Parse(text.AsSpan(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            var digits = new StringBuilder(count + 1);
            foreach (char c in text.AsSpan(0, e))
                if (char.IsAsciiDigit(c)) digits.Append(c);
            if (tie)
            {
                digits.Length = count;
                if (Increment(digits)) exponent++;
            }
            return digits.ToString();
        }

        /// <summary>The first <paramref name="count"/> of the digits, rounded half away from zero
        /// (the Framework's second rounding).</summary>
        static string Round(string digits, int count, ref int exponent)
        {
            if (count >= digits.Length) return digits;
            var kept = new StringBuilder(digits, 0, count, count);
            if (digits[count] >= '5' && Increment(kept)) exponent++;
            return kept.ToString();
        }

        /// <summary>Adds one to the last digit; true when that carries out of the first (all nines
        /// become 1 and zeros, one place higher).</summary>
        static bool Increment(StringBuilder digits)
        {
            for (int i = digits.Length - 1; i >= 0; i--)
            {
                if (digits[i] != '9')
                {
                    digits[i]++;
                    return false;
                }
                digits[i] = '0';
            }
            if (digits.Length > 0) digits[0] = '1';
            return true;
        }

        /// <summary>The "G" text: the value's first <paramref name="taken"/> digits, rounded again
        /// to <paramref name="shown"/>, trailing zeros dropped; in scientific notation when the
        /// exponent is at least <paramref name="limit"/> or below -4, with a sign and at least two
        /// exponent digits.</summary>
        static string General(double value, int taken, int shown, int limit, NumberFormatInfo info, bool lower)
        {
            if (value == 0) return "0";
            var digits = Round(Digits(value, taken, out int exponent), shown, ref exponent).TrimEnd('0');
            var text = new StringBuilder(digits.Length + 8);
            if (value < 0) text.Append(info.NegativeSign);
            if (exponent >= limit || exponent < -4)
            {
                text.Append(digits[0]);
                if (digits.Length > 1) text.Append(info.NumberDecimalSeparator).Append(digits, 1, digits.Length - 1);
                AppendExponent(text, exponent, 2, info, lower);
            }
            else if (exponent >= 0)
            {
                text.Append(digits, 0, Math.Min(digits.Length, exponent + 1));
                if (digits.Length <= exponent) text.Append('0', exponent + 1 - digits.Length);
                else if (digits.Length > exponent + 1)
                    text.Append(info.NumberDecimalSeparator).Append(digits, exponent + 1, digits.Length - exponent - 1);
            }
            else text.Append('0').Append(info.NumberDecimalSeparator).Append('0', -exponent - 1).Append(digits);
            return text.ToString();
        }

        /// <summary>The "E" text: the value's first <paramref name="taken"/> digits, rounded again
        /// to <paramref name="shown"/> (padded with zeros), and an exponent of at least three digits.</summary>
        static string Exponential(double value, int taken, int shown, NumberFormatInfo info, bool lower)
        {
            int exponent;
            var digits = Round(Digits(value, taken, out exponent), shown, ref exponent);
            var text = new StringBuilder(shown + 8);
            if (value < 0) text.Append(info.NegativeSign);
            text.Append(digits[0]);
            if (shown > 1)
                text.Append(info.NumberDecimalSeparator).Append(digits, 1, digits.Length - 1).Append('0', shown - digits.Length);
            AppendExponent(text, exponent, 3, info, lower);
            return text.ToString();
        }

        static void AppendExponent(StringBuilder text, int exponent, int width, NumberFormatInfo info, bool lower)
        {
            text.Append(lower ? 'e' : 'E').Append(exponent < 0 ? info.NegativeSign : info.PositiveSign);
            text.Append(Math.Abs(exponent).ToString("D" + width.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture));
        }

        /// <summary>Whether the exact decimal value has <paramref name="significant"/> + 1
        /// significant digits, the last a 5: halfway between two texts of
        /// <paramref name="significant"/> digits. With the value m × 2^e (m odd), only an integer
        /// (e = 0) or a value with a fractional part (e &lt; 0) can end in 5, and the digits of the
        /// latter are those of m × 5^-e.</summary>
        static bool IsTie(double magnitude, int significant)
        {
            long bits = BitConverter.DoubleToInt64Bits(magnitude);
            int biased = (int)(bits >> 52);
            long mantissa = bits & ((1L << 52) - 1);
            if (biased == 0) biased = 1;
            else mantissa |= 1L << 52;
            int zeros = BitOperations.TrailingZeroCount(mantissa);
            mantissa >>= zeros;
            int exponent = biased - 1075 + zeros;
            // 5^26 alone has 19 digits, more than a tie of the 17 digits taken at most.
            if (exponent > 0 || exponent < -25) return false;
            UInt128 exact = (ulong)mantissa;
            for (int i = 0; i < -exponent; i++) exact *= 5;
            if (exact % 10 != 5) return false;
            int count = 0;
            for (; exact != 0; exact /= 10) count++;
            return count == significant + 1;
        }

        /// <summary>"F", "N", "C" or "P", with at most two precision digits.</summary>
        static bool IsFixedPoint(string format) =>
            !string.IsNullOrEmpty(format) && IsStandard(format, out char kind, out _) &&
            char.ToUpperInvariant(kind) is 'F' or 'N' or 'C' or 'P';

        /// <summary>The digits times 10^(exponent - their count + 1) as an integer; false when a
        /// digit falls after the point.</summary>
        static bool TryDigitsAsInteger(string digits, int exponent, bool negative, out BigInteger number)
        {
            number = BigInteger.Zero;
            int zeros = exponent - (digits.Length - 1);
            if (zeros < 0) return false;
            number = BigInteger.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture) * BigInteger.Pow(10, zeros);
            if (negative) number = -number;
            return true;
        }

        /// <summary>A standard format: one letter and at most two digits (empty means "G"); digits
        /// is -1 when the format gives none.</summary>
        static bool IsStandard(string format, out char kind, out int digits)
        {
            kind = 'G';
            digits = -1;
            if (string.IsNullOrEmpty(format)) return true;
            if (!char.IsAsciiLetter(format[0]) || format.Length > 3) return false;
            for (int i = 1; i < format.Length; i++)
            {
                if (!char.IsAsciiDigit(format[i])) return false;
                digits = Math.Max(digits, 0) * 10 + (format[i] - '0');
            }
            kind = format[0];
            return true;
        }

        /// <summary>The digits times 10^(exponent - their count + 1) as a decimal (trailing zeros
        /// dropped, so it formats like the number and not like its scale); false out of decimal's
        /// range.</summary>
        static bool TryDigitsAsDecimal(string digits, int exponent, bool negative, out decimal number)
        {
            number = 0;
            int length = digits.Length;
            while (length > 1 && digits[length - 1] == '0') length--;
            // The scale of the last digit kept: decimal holds at most 28 places and 28 digits.
            int scale = length - 1 - exponent;
            if (exponent > 27 || scale > 28) return false;
            var integer = decimal.Parse(digits.AsSpan(0, length), NumberStyles.None, CultureInfo.InvariantCulture);
            if (scale > 0) number = new decimal(decimal.GetBits(integer)[0], decimal.GetBits(integer)[1], decimal.GetBits(integer)[2], negative, (byte)scale);
            else
            {
                number = integer;
                for (int i = 0; i < -scale; i++) number *= 10;
                if (negative) number = -number;
            }
            return true;
        }

        // Instance calls on float and double, redirected (the receiver comes by reference).

        public static string ToString(ref float value) => Format(value, null, null);
        public static string ToString(ref float value, string format) => Format(value, format, null);
        public static string ToString(ref float value, IFormatProvider provider) => Format(value, null, provider);
        public static string ToString(ref float value, string format, IFormatProvider provider) => Format(value, format, provider);
        public static string ToString(ref double value) => Format(value, null, null);
        public static string ToString(ref double value, string format) => Format(value, format, null);
        public static string ToString(ref double value, IFormatProvider provider) => Format(value, null, provider);
        public static string ToString(ref double value, string format, IFormatProvider provider) => Format(value, format, provider);

        // Virtual calls whose receiver may be a boxed float or double.

        public static string VirtualToString(object value) => value switch
        {
            float f => Format(f, null, null),
            double d => Format(d, null, null),
            _ => value.ToString(),
        };

        public static string VirtualToString(IFormattable value, string format, IFormatProvider provider) => value switch
        {
            float f => Format(f, format, provider),
            double d => Format(d, format, provider),
            _ => value.ToString(format, provider),
        };

        // Calls that format their arguments: floats and doubles among them format as above.

        public static string ToString(float value) => Format(value, null, null);
        public static string ToString(float value, IFormatProvider provider) => Format(value, null, provider);
        public static string ToString(double value) => Format(value, null, null);
        public static string ToString(double value, IFormatProvider provider) => Format(value, null, provider);
        public static string ToString(object value) => value switch
        {
            float f => Format(f, null, null),
            double d => Format(d, null, null),
            _ => Convert.ToString(value),
        };
        public static string ToString(object value, IFormatProvider provider) => value switch
        {
            float f => Format(f, null, provider),
            double d => Format(d, null, provider),
            _ => Convert.ToString(value, provider),
        };

        public static string Format(string format, object arg0) => string.Format(format, Wrap(arg0));
        public static string Format(string format, object arg0, object arg1) => string.Format(format, Wrap(arg0), Wrap(arg1));
        public static string Format(string format, object arg0, object arg1, object arg2) => string.Format(format, Wrap(arg0), Wrap(arg1), Wrap(arg2));
        public static string Format(string format, object[] args) => string.Format(format, Wrap(args));
        public static string Format(IFormatProvider provider, string format, object arg0) => string.Format(provider, format, Wrap(arg0));
        public static string Format(IFormatProvider provider, string format, object arg0, object arg1) => string.Format(provider, format, Wrap(arg0), Wrap(arg1));
        public static string Format(IFormatProvider provider, string format, object arg0, object arg1, object arg2) => string.Format(provider, format, Wrap(arg0), Wrap(arg1), Wrap(arg2));
        public static string Format(IFormatProvider provider, string format, object[] args) => string.Format(provider, format, Wrap(args));

        public static string Concat(object arg0) => string.Concat(Wrap(arg0));
        public static string Concat(object arg0, object arg1) => string.Concat(Wrap(arg0), Wrap(arg1));
        public static string Concat(object arg0, object arg1, object arg2) => string.Concat(Wrap(arg0), Wrap(arg1), Wrap(arg2));
        public static string Concat(object[] args) => string.Concat(Wrap(args));

        public static StringBuilder Append(StringBuilder builder, float value) => builder.Append(Format(value, null, null));
        public static StringBuilder Append(StringBuilder builder, double value) => builder.Append(Format(value, null, null));
        public static StringBuilder Append(StringBuilder builder, object value) => builder.Append(Wrap(value));
        public static StringBuilder AppendFormat(StringBuilder builder, string format, object arg0) => builder.AppendFormat(format, Wrap(arg0));
        public static StringBuilder AppendFormat(StringBuilder builder, string format, object arg0, object arg1) => builder.AppendFormat(format, Wrap(arg0), Wrap(arg1));
        public static StringBuilder AppendFormat(StringBuilder builder, string format, object arg0, object arg1, object arg2) => builder.AppendFormat(format, Wrap(arg0), Wrap(arg1), Wrap(arg2));
        public static StringBuilder AppendFormat(StringBuilder builder, string format, object[] args) => builder.AppendFormat(format, Wrap(args));
        public static StringBuilder AppendFormat(StringBuilder builder, IFormatProvider provider, string format, object arg0) => builder.AppendFormat(provider, format, Wrap(arg0));
        public static StringBuilder AppendFormat(StringBuilder builder, IFormatProvider provider, string format, object arg0, object arg1) => builder.AppendFormat(provider, format, Wrap(arg0), Wrap(arg1));
        public static StringBuilder AppendFormat(StringBuilder builder, IFormatProvider provider, string format, object arg0, object arg1, object arg2) => builder.AppendFormat(provider, format, Wrap(arg0), Wrap(arg1), Wrap(arg2));
        public static StringBuilder AppendFormat(StringBuilder builder, IFormatProvider provider, string format, object[] args) => builder.AppendFormat(provider, format, Wrap(args));

        /// <summary>An object's string as Mono makes it (floats and doubles by the rules above).</summary>
        public static string ObjectToString(object value) => value switch
        {
            float f => Format(f, null, null),
            double d => Format(d, null, null),
            _ => value?.ToString(),
        };

        static object Wrap(object value) => value switch
        {
            float f => new Boxed(f, true),
            double d => new Boxed(d, false),
            _ => value,
        };

        static object[] Wrap(object[] args)
        {
            if (args == null) return null;
            object[] wrapped = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] is not (float or double)) continue;
                wrapped ??= (object[])args.Clone();
                wrapped[i] = Wrap(args[i]);
            }
            return wrapped ?? args;
        }

        /// <summary>A float or double that string.Format and string.Concat format the Mono way.</summary>
        sealed class Boxed : IFormattable
        {
            readonly double m_Value;
            readonly bool m_Single;

            internal Boxed(double value, bool single)
            {
                m_Value = value;
                m_Single = single;
            }

            public string ToString(string format, IFormatProvider provider) => Format(m_Value, format, provider, m_Single);

            public override string ToString() => Format(m_Value, null, null, m_Single);
        }
    }
}
