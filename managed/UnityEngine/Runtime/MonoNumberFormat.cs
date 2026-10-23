using System;
using System.Globalization;
using System.Text;

namespace Unbound
{
    /// <summary>
    /// Floating-point formatting as Unity's Mono class library does it (.NET Framework rules), which
    /// .NET Core 3.0 changed: the default and "G" formats print 7 significant digits for float and 15
    /// for double (not the shortest round-trippable string, so <c>1849.9999f</c> prints "1850"), the
    /// other formats round those digits again ("F2" of <c>1.005</c> is "1.01"), "R" tries 7/15 digits
    /// before 9/17, and a value that rounds to zero prints no minus sign.
    /// Game assemblies' calls are redirected here when they load (Unbound.Host.NumberFormatShims);
    /// the methods take an instance's value by reference first, like the call they replace.
    /// </summary>
    public static class MonoNumberFormat
    {
        const int SinglePrecision = 7;
        const int DoublePrecision = 15;

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
            if (value == 0) value = 0; // no negative zero
            if (IsStandard(format, out char kind, out int digits))
            {
                switch (char.ToUpperInvariant(kind))
                {
                    case 'R':
                    {
                        // The shorter precision when it reads back as the same value.
                        string shorter = single ? ((float)value).ToString("G7", provider) : value.ToString("G15", provider);
                        bool same = single
                            ? float.TryParse(shorter, NumberStyles.Float, provider, out var f) && f == (float)value
                            : double.TryParse(shorter, NumberStyles.Float, provider, out var d) && d == value;
                        if (same) return shorter;
                        return single ? ((float)value).ToString("G9", provider) : value.ToString("G17", provider);
                    }
                    case 'G':
                        // All the significant digits: the exactly rounded ones. Fewer: those digits
                        // rounded again, below.
                        if (digits != 0 && digits < precision) break;
                        digits = digits == 0 ? precision : Math.Min(digits, single ? 9 : 17);
                        var g = kind + digits.ToString(CultureInfo.InvariantCulture);
                        return single ? ((float)value).ToString(g, provider) : value.ToString(g, provider);
                    case 'E':
                        if (digits > precision - 1) precision = single ? 9 : 17;
                        break;
                    case 'D':
                    case 'X':
                    case 'B':
                        // Invalid for floating point in both: let .NET throw its FormatException.
                        return value.ToString(format, provider);
                }
            }
            // The value's first `precision` significant digits, formatted as a decimal number: the
            // Framework rounds those digits again (half away from zero) and drops the sign of a zero.
            var exact = single ? ((float)value).ToString("E" + (precision - 1), CultureInfo.InvariantCulture)
                               : value.ToString("E" + (precision - 1), CultureInfo.InvariantCulture);
            if (TryDigitsAsDecimal(exact, out var number)) return number.ToString(format, provider);
            return single ? ((float)value).ToString(format, provider) : value.ToString(format, provider);
        }

        /// <summary>A standard format: one letter and at most two digits (empty means "G").</summary>
        static bool IsStandard(string format, out char kind, out int digits)
        {
            kind = 'G';
            digits = 0;
            if (string.IsNullOrEmpty(format)) return true;
            if (!char.IsAsciiLetter(format[0]) || format.Length > 3) return false;
            for (int i = 1; i < format.Length; i++)
            {
                if (!char.IsAsciiDigit(format[i])) return false;
                digits = digits * 10 + (format[i] - '0');
            }
            kind = format[0];
            return true;
        }

        /// <summary>"d.dddE+xxx" as a decimal holding exactly those digits (trailing zeros dropped,
        /// so it formats like the number and not like its scale); false out of decimal's range.</summary>
        static bool TryDigitsAsDecimal(string exponential, out decimal number)
        {
            number = 0;
            int e = exponential.IndexOf('E');
            int exponent = int.Parse(exponential.AsSpan(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            bool negative = exponential[0] == '-';
            var mantissa = new StringBuilder();
            foreach (char c in exponential.AsSpan(0, e))
                if (char.IsAsciiDigit(c)) mantissa.Append(c);
            int length = mantissa.Length;
            while (length > 1 && mantissa[length - 1] == '0') length--;
            // The scale of the last digit kept: decimal holds at most 28 places and 28 digits.
            int scale = length - 1 - exponent;
            if (exponent > 27 || scale > 28) return false;
            var integer = decimal.Parse(mantissa.ToString(0, length), NumberStyles.None, CultureInfo.InvariantCulture);
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
