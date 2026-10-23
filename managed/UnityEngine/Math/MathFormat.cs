using System;
using System.Globalization;

namespace UnityEngine
{
    // Shared formatting rules of the value types' ToString overloads: a null provider means the
    // invariant culture, an empty format means the type's default precision.
    internal static class MathFormat
    {
        internal static IFormatProvider Provider(IFormatProvider formatProvider) =>
            formatProvider ?? CultureInfo.InvariantCulture.NumberFormat;

        internal static string Format(string format, string defaultFormat) =>
            string.IsNullOrEmpty(format) ? defaultFormat : format;

        /// <summary>A float as Unity's Mono formats it (see <see cref="Unbound.MonoNumberFormat"/>).</summary>
        internal static string F(float value, string format, IFormatProvider provider) =>
            Unbound.MonoNumberFormat.Format(value, format, provider);

        internal static string I(int value, string format, IFormatProvider provider) =>
            value.ToString(format, provider);
    }
}
