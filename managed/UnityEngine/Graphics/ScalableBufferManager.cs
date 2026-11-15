using System;

namespace UnityEngine
{
    /// <summary>Dynamic resolution scaling. On a device whose Editor scales buffers (the macOS
    /// Editor's Metal device) <c>ResizeBuffers</c> keeps its factors within [0.05, 1] and every
    /// render texture marked <c>useDynamicScale</c> takes its scaled size (each side rounded up),
    /// existing ones as well (tests/projects/nativegfx <c>dynres.*</c>). The Linux Editor's devices
    /// do not scale: the factors stay 1 and resizing does nothing (tests/projects/builtinfx).</summary>
    public static partial class ScalableBufferManager
    {
        /// <summary>The smallest factor kept (a request of 0 keeps it; tests/projects/nativegfx
        /// <c>dynres.factors.outside</c>).</summary>
        const float MinimumScale = 0.0500000119f;

        static float s_WidthScale = 1f, s_HeightScale = 1f;

        /// <summary>Whether a factor is not 1.</summary>
        internal static bool Scaling => s_WidthScale != 1f || s_HeightScale != 1f;

        [Unbound.EditorFaithful("scales on the Metal device (tests/projects/nativegfx dynres.*); the Linux Editor's devices keep their buffers' size")]
        public static void ResizeBuffers(float widthScale, float heightScale)
        {
            if (!Unbound.Runtime.GraphicsDeviceProfile.Current.EditorDynamicResolution) return;
            s_WidthScale = Kept(widthScale, "widthScale");
            s_HeightScale = Kept(heightScale, "heightScale");
        }

        /// <summary>A factor outside (0, 1], the documented range, is asserted against and kept
        /// within [0.05, 1], NaN as 1 (tests/projects/nativegfx dynres.factors.outside, .nan and
        /// their logs).</summary>
        static float Kept(float scale, string name)
        {
            if (scale > 0f && scale <= 1f && scale >= MinimumScale) return scale;
            if (!(scale > 0f && scale <= 1f))
                Debug.unityLogger.Log(LogType.Assert, $"Invalid {name}: {Printed(scale)}");
            return float.IsNaN(scale) ? 1f : Mathf.Clamp(scale, MinimumScale, 1f);
        }

        /// <summary>A float as the Editor's message prints it: six decimals, "nan" and "inf".</summary>
        static string Printed(float value) => float.IsNaN(value) ? "nan"
            : float.IsInfinity(value) ? (value > 0 ? "inf" : "-inf")
            : value.ToString("F6", System.Globalization.CultureInfo.InvariantCulture);

        public static float widthScaleFactor => s_WidthScale;

        public static float heightScaleFactor => s_HeightScale;

        /// <summary>A side of a dynamically scaled render texture: its size times the factor in
        /// single precision, rounded up (15 x 0.5 is 8, 13 x 0.25 is 4, 10 x 0.3 is 3).</summary>
        internal static int Scaled(int size, float factor) => factor == 1f ? size : Math.Max(1, (int)MathF.Ceiling(size * factor));
    }
}
