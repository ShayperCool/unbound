using System;
using System.Collections.Generic;
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
    /// rounds to zero prints no minus sign. Exceptions print their messages as Mono writes them
    /// (Unbound.Runtime.MonoExceptions): Exception.Message, a virtual ToString() and the calls that
    /// format their arguments. Enum values print the name Mono picks among names that share a value
    /// (<see cref="MonoEnumNames"/>).
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
            Span<char> significant = stackalloc char[DoubleExtendedPrecision];
            Digits(value, precision, significant, out int exponent);
            if (TryDigitsAsDecimal(significant.Slice(0, precision), exponent, value < 0, out var number))
                return number.ToString(format, provider);
            // Beyond decimal's range a fixed-point format still prints those digits, then zeros
            // (-3.4e37f "F2" is "-34000000000000000000000000000000000000.00", not the float's exact
            // value): every kept digit is then before the point, so nothing rounds, and an integer
            // holding them goes through the same .NET formatting as the decimal above.
            if (IsFixedPoint(format) && TryDigitsAsInteger(significant.Slice(0, precision), exponent, value < 0, out var integer))
                return integer.ToString(format, provider);
            return single ? ((float)value).ToString(format, provider) : value.ToString(format, provider);
        }

        /// <summary>The first <paramref name="count"/> significant digits of the value's magnitude
        /// into <paramref name="digits"/>, and the decimal exponent of the first, as the Framework
        /// takes them: the exact value rounded half away from zero. .NET's exact formatting rounds a
        /// tie (the exact value has <paramref name="count"/> + 1 significant digits, the last a 5)
        /// to the even digit, so a tie is taken from its <paramref name="count"/> + 1 exact digits
        /// instead and rounded up (Unity 6000.0.71f1, tests/projects/floattext: <c>245f / 1024</c>,
        /// 0.2392578125, is "0.239257813" in "R"; 2^-25 is "2.9802322387695313E-08").</summary>
        static void Digits(double value, int count, Span<char> digits, out int exponent)
        {
            double magnitude = Math.Abs(value);
            if (magnitude == 0)
            {
                exponent = 0;
                digits.Slice(0, count).Fill('0');
                return;
            }
            bool tie = IsTie(magnitude, count);
            Span<char> text = stackalloc char[32];
            magnitude.TryFormat(text, out int written, ExponentialFormats[tie ? count : count - 1], CultureInfo.InvariantCulture);
            text = text.Slice(0, written);
            int e = text.IndexOf('E');
            exponent = int.Parse(text.Slice(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            int n = 0;
            foreach (char c in text.Slice(0, e))
                if (char.IsAsciiDigit(c) && n < count) digits[n++] = c;
            if (tie && Increment(digits.Slice(0, count))) exponent++;
        }

        /// <summary>The first <paramref name="count"/> of <paramref name="length"/> digits, rounded
        /// half away from zero in place (the Framework's second rounding); their new length.</summary>
        static int Round(Span<char> digits, int length, int count, ref int exponent)
        {
            if (count >= length) return length;
            if (digits[count] >= '5' && Increment(digits.Slice(0, count))) exponent++;
            return count;
        }

        /// <summary>Adds one to the last digit; true when that carries out of the first (all nines
        /// become 1 and zeros, one place higher).</summary>
        static bool Increment(Span<char> digits)
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
            Span<char> digits = stackalloc char[DoubleExtendedPrecision];
            Digits(value, taken, digits, out int exponent);
            int length = Round(digits, taken, shown, ref exponent);
            while (length > 1 && digits[length - 1] == '0') length--;
            ReadOnlySpan<char> kept = digits.Slice(0, length);
            var text = Text();
            if (value < 0) text.Append(info.NegativeSign);
            if (exponent >= limit || exponent < -4)
            {
                text.Append(kept[0]);
                if (length > 1) text.Append(info.NumberDecimalSeparator).Append(kept.Slice(1));
                AppendExponent(text, exponent, 2, info, lower);
            }
            else if (exponent >= 0)
            {
                text.Append(kept.Slice(0, Math.Min(length, exponent + 1)));
                if (length <= exponent) text.Append('0', exponent + 1 - length);
                else if (length > exponent + 1) text.Append(info.NumberDecimalSeparator).Append(kept.Slice(exponent + 1));
            }
            else text.Append('0').Append(info.NumberDecimalSeparator).Append('0', -exponent - 1).Append(kept);
            return text.ToString();
        }

        /// <summary>The "E" text: the value's first <paramref name="taken"/> digits, rounded again
        /// to <paramref name="shown"/> (padded with zeros), and an exponent of at least three digits.</summary>
        static string Exponential(double value, int taken, int shown, NumberFormatInfo info, bool lower)
        {
            Span<char> digits = stackalloc char[DoubleExtendedPrecision];
            Digits(value, taken, digits, out int exponent);
            int length = Round(digits, taken, shown, ref exponent);
            var text = Text();
            if (value < 0) text.Append(info.NegativeSign);
            text.Append(digits[0]);
            if (shown > 1)
                text.Append(info.NumberDecimalSeparator).Append(digits.Slice(1, length - 1)).Append('0', shown - length);
            AppendExponent(text, exponent, 3, info, lower);
            return text.ToString();
        }

        static void AppendExponent(StringBuilder text, int exponent, int width, NumberFormatInfo info, bool lower)
        {
            text.Append(lower ? 'e' : 'E').Append(exponent < 0 ? info.NegativeSign : info.PositiveSign);
            int magnitude = Math.Abs(exponent);
            for (int bound = 10, places = 1; places < width; bound *= 10, places++)
                if (magnitude < bound) text.Append('0');
            Span<char> number = stackalloc char[4];
            magnitude.TryFormat(number, out int written, default, CultureInfo.InvariantCulture);
            text.Append(number.Slice(0, written));
        }

        /// <summary>The formats "E0" to "E17": the exact digits .NET gives up to a tie of 17.</summary>
        static readonly string[] ExponentialFormats = MakeExponentialFormats();

        static string[] MakeExponentialFormats()
        {
            var formats = new string[DoubleExtendedPrecision + 1];
            for (int i = 0; i < formats.Length; i++) formats[i] = "E" + i.ToString(CultureInfo.InvariantCulture);
            return formats;
        }

        [ThreadStatic] static StringBuilder cachedText;

        /// <summary>This thread's text builder, emptied: one string per formatted value.</summary>
        static StringBuilder Text()
        {
            var text = cachedText ??= new StringBuilder(64);
            text.Clear();
            return text;
        }

        // 5^0 to 5^27, the largest power of 5 in an unsigned long.
        static readonly ulong[] PowersOfFive = MakePowersOfFive();

        static ulong[] MakePowersOfFive()
        {
            var powers = new ulong[28];
            powers[0] = 1;
            for (int i = 1; i < powers.Length; i++) powers[i] = powers[i - 1] * 5;
            return powers;
        }

        /// <summary>Whether the value's significant digits (trailing zeros dropped) are
        /// <paramref name="significant"/> + 1, the last a 5: halfway between two texts of
        /// <paramref name="significant"/> digits. With the value m × 2^e (m odd), those digits are
        /// those of m × 5^-e for e ≤ 0, and for e &gt; 0 of m / 5^e when 5^e divides m (the value is
        /// then that number × 10^e); otherwise they end in an even digit.</summary>
        static bool IsTie(double magnitude, int significant)
        {
            long bits = BitConverter.DoubleToInt64Bits(magnitude);
            int biased = (int)(bits >> 52);
            long mantissa = bits & ((1L << 52) - 1);
            if (biased == 0) biased = 1;
            else mantissa |= 1L << 52;
            int zeros = BitOperations.TrailingZeroCount(mantissa);
            ulong odd = (ulong)mantissa >> zeros;
            int exponent = biased - 1075 + zeros;
            UInt128 number;
            if (exponent > 0)
            {
                if (exponent >= PowersOfFive.Length || odd % PowersOfFive[exponent] != 0) return false;
                number = odd / PowersOfFive[exponent];
            }
            else
            {
                // m × 5^k has more than (bit length of m - 1) log10 2 + k log10 5 digits: most values
                // with a fraction have too many to be a tie.
                int length = 64 - BitOperations.LeadingZeroCount(odd);
                if ((length - 1) * 0.30103 + -exponent * 0.69897 > significant + 1.001) return false;
                number = (UInt128)odd * PowersOfFive[-exponent];
            }
            if (number % 10 != 5) return false;
            int count = 0;
            for (; number != 0; number /= 10) count++;
            return count == significant + 1;
        }

        /// <summary>"F", "N", "C" or "P", with at most two precision digits.</summary>
        static bool IsFixedPoint(string format) =>
            !string.IsNullOrEmpty(format) && IsStandard(format, out char kind, out _) &&
            char.ToUpperInvariant(kind) is 'F' or 'N' or 'C' or 'P';

        /// <summary>The digits times 10^(exponent - their count + 1) as an integer; false when a
        /// digit falls after the point.</summary>
        static bool TryDigitsAsInteger(ReadOnlySpan<char> digits, int exponent, bool negative, out BigInteger number)
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
        static bool TryDigitsAsDecimal(ReadOnlySpan<char> digits, int exponent, bool negative, out decimal number)
        {
            number = 0;
            int length = digits.Length;
            while (length > 1 && digits[length - 1] == '0') length--;
            // The scale of the last digit kept: decimal holds at most 28 places and 28 digits.
            int scale = length - 1 - exponent;
            if (exponent > 27 || scale > 28) return false;
            var integer = decimal.Parse(digits.Slice(0, length), NumberStyles.None, CultureInfo.InvariantCulture);
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

        // Virtual calls whose receiver may be a boxed float or double, or an exception.

        public static string VirtualToString(object value) => MonoText(value) ?? value.ToString();

        /// <summary>A virtual call of Exception.Message (a non-virtual one is base.Message in an
        /// override: it stays).</summary>
        public static string get_Message(Exception exception) => Unbound.Runtime.MonoExceptions.MessageOf(exception);

        public static string VirtualToString(IFormattable value, string format, IFormatProvider provider) => value switch
        {
            float f => Format(f, format, provider),
            double d => Format(d, format, provider),
            Enum e => MonoEnumNames.ToString(e, format),
            _ => value.ToString(format, provider),
        };

        // Enum calls: ToString on a value held directly (an enum, a nullable or a generic
        // parameter's value, which may also be a float or double), on an Enum reference or a boxed
        // value; the static lookups.

        public static string ConstrainedToString<T>(ref T value, string format, IFormatProvider provider) where T : IFormattable
        {
            if (typeof(T) == typeof(float)) return Format(System.Runtime.CompilerServices.Unsafe.As<T, float>(ref value), format, provider);
            if (typeof(T) == typeof(double)) return Format(System.Runtime.CompilerServices.Unsafe.As<T, double>(ref value), format, provider);
            if (typeof(T).IsEnum) return MonoEnumNames.ToString((Enum)(object)value, format);
            // A reference type parameter may hold a boxed enum, float or double (null throws, as the
            // call it replaces does).
            if (!typeof(T).IsValueType) return value is null ? throw new NullReferenceException() : VirtualToString(value, format, provider);
            return value.ToString(format, provider);
        }

        /// <summary>An Enum overload called through a type parameter constrained to Enum.</summary>
        public static string ConstrainedToString<T>(ref T value, string format) => MonoEnumNames.ToString((Enum)(object)value, format);

        public static string ConstrainedToString<T>(ref T value, IFormatProvider provider) => MonoEnumNames.ToString((Enum)(object)value, null);

        public static string ConstrainedToString<T>(ref T value)
        {
            if (typeof(T) == typeof(float)) return Format(System.Runtime.CompilerServices.Unsafe.As<T, float>(ref value), null, null);
            if (typeof(T) == typeof(double)) return Format(System.Runtime.CompilerServices.Unsafe.As<T, double>(ref value), null, null);
            // A value tuple called directly ((a, b).ToString()).
            if (LibraryTuple<T>.Is) return TupleToString((System.Runtime.CompilerServices.ITuple)value);
            return MonoEnumNames.ToString(ref value);
        }
        public static string ToString(Enum value) => MonoEnumNames.ToString(value, null);
        public static string ToString(Enum value, string format) => MonoEnumNames.ToString(value, format);
        public static string ToString(Enum value, IFormatProvider provider) => MonoEnumNames.ToString(value, null);
        public static string ToString(Enum value, string format, IFormatProvider provider) => MonoEnumNames.ToString(value, format);
        public static string GetName(Type enumType, object value) => MonoEnumNames.GetName(enumType, value);
        public static string GetEnumName(Type enumType, object value) => MonoEnumNames.GetName(enumType, value);
        public static string Format(Type enumType, object value, string format) => MonoEnumNames.Format(enumType, value, format);

        // Calls that format their arguments: floats and doubles among them format as above.

        public static string ToString(float value) => Format(value, null, null);
        public static string ToString(float value, IFormatProvider provider) => Format(value, null, provider);
        public static string ToString(double value) => Format(value, null, null);
        public static string ToString(double value, IFormatProvider provider) => Format(value, null, provider);
        public static string ToString(object value) => MonoText(value) ?? Convert.ToString(value);
        public static string ToString(object value, IFormatProvider provider) => value switch
        {
            float f => Format(f, null, provider),
            double d => Format(d, null, provider),
            _ => MonoText(value) ?? Convert.ToString(value, provider),
        };

        public static string Format(string format, object arg0) => string.Format(format, Wrap(arg0));
        public static string Format(string format, object arg0, object arg1) => string.Format(format, Wrap(arg0), Wrap(arg1));
        public static string Format(string format, object arg0, object arg1, object arg2) => string.Format(format, Wrap(arg0), Wrap(arg1), Wrap(arg2));
        public static string Format(string format, object[] args) => string.Format(format, Wrap(args));
        public static string Format(IFormatProvider provider, string format, object arg0) =>
            Fallback(provider) is { } fallback ? string.Format(fallback, format, arg0) : string.Format(provider, format, Wrap(arg0));
        public static string Format(IFormatProvider provider, string format, object arg0, object arg1) =>
            Fallback(provider) is { } fallback ? string.Format(fallback, format, arg0, arg1) : string.Format(provider, format, Wrap(arg0), Wrap(arg1));
        public static string Format(IFormatProvider provider, string format, object arg0, object arg1, object arg2) =>
            Fallback(provider) is { } fallback ? string.Format(fallback, format, arg0, arg1, arg2) : string.Format(provider, format, Wrap(arg0), Wrap(arg1), Wrap(arg2));
        public static string Format(IFormatProvider provider, string format, object[] args) =>
            Fallback(provider) is { } fallback ? string.Format(fallback, format, args) : string.Format(provider, format, Wrap(args));

        public static string Concat(object arg0) => string.Concat(Wrap(arg0));
        public static string Concat(object arg0, object arg1) => string.Concat(Wrap(arg0), Wrap(arg1));
        public static string Concat(object arg0, object arg1, object arg2) => string.Concat(Wrap(arg0), Wrap(arg1), Wrap(arg2));
        public static string Concat(object[] args) => string.Concat(Wrap(args));

        public static string Join(string separator, object[] values) => string.Join(separator, Wrap(values));
        public static string Join(char separator, object[] values) => string.Join(separator, Wrap(values));
        public static string Join<T>(string separator, IEnumerable<T> values) => Texts<T>.Mono ? string.Join(separator, MonoTexts(values)) : string.Join(separator, values);
        public static string Join<T>(char separator, IEnumerable<T> values) => Texts<T>.Mono ? string.Join(separator, MonoTexts(values)) : string.Join(separator, values);
        public static string Concat<T>(IEnumerable<T> values) => Texts<T>.Mono ? string.Concat(MonoTexts(values)) : string.Concat(values);
        public static StringBuilder AppendJoin(StringBuilder builder, string separator, object[] values) => builder.AppendJoin(separator, Wrap(values));
        public static StringBuilder AppendJoin(StringBuilder builder, char separator, object[] values) => builder.AppendJoin(separator, Wrap(values));
        public static StringBuilder AppendJoin<T>(StringBuilder builder, string separator, IEnumerable<T> values) =>
            Texts<T>.Mono ? builder.AppendJoin(separator, MonoTexts(values)) : builder.AppendJoin(separator, values);
        public static StringBuilder AppendJoin<T>(StringBuilder builder, char separator, IEnumerable<T> values) =>
            Texts<T>.Mono ? builder.AppendJoin(separator, MonoTexts(values)) : builder.AppendJoin(separator, values);

        /// <summary>Whether values of a type may print otherwise under Mono: floats, doubles,
        /// enums, exceptions, or values of a type that can hold any of them.</summary>
        static class Texts<T>
        {
            internal static readonly bool Mono = !typeof(T).IsValueType || PrintsOtherwise(Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T));
        }

        /// <summary>Whether T is a value tuple of the class library (answered once per type).</summary>
        static class LibraryTuple<T>
        {
            internal static readonly bool Is = typeof(T).IsValueType && IsLibraryTuple(typeof(T));
        }

        /// <summary>Whether values of a value type print otherwise under Mono: enums, floats, doubles
        /// and value tuples (whose items may be floats).</summary>
        internal static bool PrintsOtherwise(Type type) => type.IsEnum || type == typeof(float) || type == typeof(double) || IsLibraryTuple(type);

        static IEnumerable<string> MonoTexts<T>(IEnumerable<T> values)
        {
            foreach (var value in values) yield return value is null ? null : ObjectToString(value);
        }

        public static StringBuilder Append(StringBuilder builder, float value) => builder.Append(Format(value, null, null));
        public static StringBuilder Append(StringBuilder builder, double value) => builder.Append(Format(value, null, null));
        public static StringBuilder Append(StringBuilder builder, object value) => builder.Append(Wrap(value));
        public static StringBuilder AppendFormat(StringBuilder builder, string format, object arg0) => builder.AppendFormat(format, Wrap(arg0));
        public static StringBuilder AppendFormat(StringBuilder builder, string format, object arg0, object arg1) => builder.AppendFormat(format, Wrap(arg0), Wrap(arg1));
        public static StringBuilder AppendFormat(StringBuilder builder, string format, object arg0, object arg1, object arg2) => builder.AppendFormat(format, Wrap(arg0), Wrap(arg1), Wrap(arg2));
        public static StringBuilder AppendFormat(StringBuilder builder, string format, object[] args) => builder.AppendFormat(format, Wrap(args));
        public static StringBuilder AppendFormat(StringBuilder builder, IFormatProvider provider, string format, object arg0) =>
            Fallback(provider) is { } fallback ? builder.AppendFormat(fallback, format, arg0) : builder.AppendFormat(provider, format, Wrap(arg0));
        public static StringBuilder AppendFormat(StringBuilder builder, IFormatProvider provider, string format, object arg0, object arg1) =>
            Fallback(provider) is { } fallback ? builder.AppendFormat(fallback, format, arg0, arg1) : builder.AppendFormat(provider, format, Wrap(arg0), Wrap(arg1));
        public static StringBuilder AppendFormat(StringBuilder builder, IFormatProvider provider, string format, object arg0, object arg1, object arg2) =>
            Fallback(provider) is { } fallback ? builder.AppendFormat(fallback, format, arg0, arg1, arg2) : builder.AppendFormat(provider, format, Wrap(arg0), Wrap(arg1), Wrap(arg2));
        public static StringBuilder AppendFormat(StringBuilder builder, IFormatProvider provider, string format, object[] args) =>
            Fallback(provider) is { } fallback ? builder.AppendFormat(fallback, format, args) : builder.AppendFormat(provider, format, Wrap(args));

        /// <summary>An object's string as Mono makes it.</summary>
        public static string ObjectToString(object value) => MonoText(value) ?? value?.ToString();

        /// <summary>The string Mono makes of a value whose string differs from .NET's (null for any
        /// other): floats and doubles by the rules above, exceptions, enums, and a tuple's items each
        /// so, "(a, b)" (tests/projects/apierrors `format.number.*tuple*`).</summary>
        static string MonoText(object value) => value switch
        {
            float f => Format(f, null, null),
            double d => Format(d, null, null),
            Exception e => Unbound.Runtime.MonoExceptions.ToStringOf(e),
            Enum e => MonoEnumNames.ToString(e, null),
            System.Runtime.CompilerServices.ITuple tuple when IsLibraryTuple(tuple.GetType()) => TupleToString(tuple),
            _ => null,
        };

        /// <summary>The class library's tuples (System.Tuple and System.ValueTuple), whose text is
        /// their items'; a game type that is an ITuple keeps its own.</summary>
        internal static bool IsLibraryTuple(Type type) =>
            type.Namespace == "System" && (type.Name.StartsWith("ValueTuple", StringComparison.Ordinal) || type.Name.StartsWith("Tuple`", StringComparison.Ordinal))
            && typeof(System.Runtime.CompilerServices.ITuple).IsAssignableFrom(type);

        /// <summary>A tuple as ValueTuple and Tuple write it, each item as Mono writes it.</summary>
        static string TupleToString(System.Runtime.CompilerServices.ITuple tuple)
        {
            var text = new StringBuilder("(");
            for (int i = 0; i < tuple.Length; i++)
            {
                if (i != 0) text.Append(", ");
                text.Append(ObjectToString(tuple[i]));
            }
            return text.Append(')').ToString();
        }

        static object Wrap(object value) => value switch
        {
            float f => new Boxed(f, true),
            double d => new Boxed(d, false),
            Exception e => Unbound.Runtime.MonoExceptions.ToStringOf(e),
            Enum e => new BoxedEnum(e),
            System.Runtime.CompilerServices.ITuple tuple when IsLibraryTuple(tuple.GetType()) => TupleToString(tuple),
            _ => value,
        };

        /// <summary>For a provider with a custom formatter, a provider whose formatter gives the
        /// game's the arguments themselves (it may test their types) and prints the ones it leaves
        /// (returns null for) as Mono does; null for any other provider.</summary>
        static IFormatProvider Fallback(IFormatProvider provider) =>
            provider?.GetFormat(typeof(ICustomFormatter)) is ICustomFormatter custom ? new MonoFallback(provider, custom) : null;

        sealed class MonoFallback : IFormatProvider, ICustomFormatter
        {
            readonly IFormatProvider m_Provider;
            readonly ICustomFormatter m_Custom;

            internal MonoFallback(IFormatProvider provider, ICustomFormatter custom)
            {
                m_Provider = provider;
                m_Custom = custom;
            }

            public object GetFormat(Type type) => type == typeof(ICustomFormatter) ? this : m_Provider.GetFormat(type);

            // What the game's formatter leaves prints as the class library would, with the game's
            // provider (a type may test it); a null result leaves a plain object to ToString().
            public string Format(string format, object arg, IFormatProvider provider) => m_Custom.Format(format, arg, m_Provider) ?? arg switch
            {
                float f => MonoNumberFormat.Format(f, format, m_Provider),
                double d => MonoNumberFormat.Format(d, format, m_Provider),
                Enum e => MonoEnumNames.ToString(e, format),
                Exception e => Unbound.Runtime.MonoExceptions.ToStringOf(e),
                IFormattable formattable => formattable.ToString(format, m_Provider),
                System.Runtime.CompilerServices.ITuple tuple when IsLibraryTuple(tuple.GetType()) => TupleToString(tuple),
                _ => null,
            };
        }

        static object[] Wrap(object[] args)
        {
            if (args == null) return null;
            object[] wrapped = null;
            for (int i = 0; i < args.Length; i++)
            {
                object arg = Wrap(args[i]);
                if (ReferenceEquals(arg, args[i])) continue;
                wrapped ??= (object[])args.Clone();
                wrapped[i] = arg;
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

        /// <summary>An enum value that string.Format and string.Concat print with Mono's names.</summary>
        sealed class BoxedEnum : IFormattable
        {
            readonly Enum m_Value;

            internal BoxedEnum(Enum value) => m_Value = value;

            public string ToString(string format, IFormatProvider provider) => MonoEnumNames.ToString(m_Value, format);

            public override string ToString() => MonoEnumNames.ToString(m_Value, null);
        }
    }
}
