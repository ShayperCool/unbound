using System.Globalization;

namespace UnityEngine
{
    /// <summary>Unity's internal formatting helper: string.Format with the invariant culture's
    /// number format, printing numbers as Mono does (Unbound.MonoNumberFormat). Matches the Unity
    /// 6000.0.71f1 Editor (tests/projects/coreremainder).</summary>
    internal sealed partial class UnityString
    {
        public static string Format(string fmt, params object[] args) =>
            Unbound.MonoNumberFormat.Format(CultureInfo.InvariantCulture.NumberFormat, fmt, args);
    }
}
