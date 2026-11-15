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

        /// <summary>Changes whenever a factor does: render textures made at other factors are
        /// remade at the new size when next used.</summary>
        internal static int Version { get; private set; }

        /// <summary>Whether a factor is not 1.</summary>
        internal static bool Scaling => s_WidthScale != 1f || s_HeightScale != 1f;

        [Unbound.EditorFaithful("scales on the Metal device (tests/projects/nativegfx dynres.*); the Linux Editor's devices keep their buffers' size")]
        public static void ResizeBuffers(float widthScale, float heightScale)
        {
            if (!Unbound.Runtime.GraphicsDeviceProfile.Current.EditorDynamicResolution) return;
            // A factor outside (0, 1], the documented range, is asserted against and clamped
            // (tests/projects/nativegfx dynres.factors.outside and logs).
            if (!(widthScale > 0f && widthScale <= 1f))
                Debug.unityLogger.Log(LogType.Assert, "Invalid widthScale: " + widthScale.ToString("F6", System.Globalization.CultureInfo.InvariantCulture));
            if (!(heightScale > 0f && heightScale <= 1f))
                Debug.unityLogger.Log(LogType.Assert, "Invalid heightScale: " + heightScale.ToString("F6", System.Globalization.CultureInfo.InvariantCulture));
            float width = Mathf.Clamp(widthScale, MinimumScale, 1f), height = Mathf.Clamp(heightScale, MinimumScale, 1f);
            if (width == s_WidthScale && height == s_HeightScale) return;
            s_WidthScale = width;
            s_HeightScale = height;
            Version++;
        }

        public static float widthScaleFactor => s_WidthScale;

        public static float heightScaleFactor => s_HeightScale;

        /// <summary>A side of a dynamically scaled render texture: its size times the factor,
        /// rounded up (15 x 0.5 is 8, 13 x 0.25 is 4).</summary>
        internal static int ScaledWidth(int width) => Scaled(width, s_WidthScale);

        internal static int ScaledHeight(int height) => Scaled(height, s_HeightScale);

        static int Scaled(int size, float factor) => factor == 1f ? size : Math.Max(1, (int)MathF.Ceiling(size * factor));
    }
}
