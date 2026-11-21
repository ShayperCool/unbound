using System;

namespace UnityEngine
{
    /// <summary>Dynamic resolution scaling. On devices whose Editor scales buffers (Metal and
    /// Vulkan: tests/projects/nativegfx <c>dynres.*</c>, tests/projects/corecaps <c>dynres.*</c>)
    /// <c>ResizeBuffers</c> quantizes its factors to 20 steps of 0.05 in [0.0500000119, 1] and every
    /// render texture marked <c>useDynamicScale</c> takes its scaled size (each side rounded up),
    /// existing ones as well. On OpenGL Core the factors stay 1 and resizing does nothing, though
    /// out-of-range arguments still log the validation assertions (tests/projects/corecaps
    /// <c>dynres.factors.logs</c>).</summary>
    public static partial class ScalableBufferManager
    {
        const int ScaleSteps = 20;
        const float ScaleStepDelta = -0.05f;
        /// <summary>The smallest factor kept: step 19 (<c>1f + 19 * -0.05f</c>, 0.0500000119f;
        /// tests/projects/nativegfx and tests/projects/corecaps <c>dynres.factors.outside</c>).</summary>
        const float MinimumScale = 1f + (ScaleSteps - 1) * ScaleStepDelta;

        static float s_WidthScale = 1f, s_HeightScale = 1f;

        /// <summary>Whether a factor is not 1.</summary>
        internal static bool Scaling => s_WidthScale != 1f || s_HeightScale != 1f;

        [Unbound.EditorFaithful("scales on Metal and Vulkan (tests/projects/nativegfx, corecaps dynres.*); OpenGL Core validates and keeps factors at 1")]
        public static void ResizeBuffers(float widthScale, float heightScale)
        {
            Validate(widthScale, "widthScale");
            Validate(heightScale, "heightScale");
            if (!Unbound.Runtime.GraphicsDeviceProfile.Current.EditorDynamicResolution) return;
            s_WidthScale = Quantize(widthScale);
            s_HeightScale = Quantize(heightScale);
        }

        static void Validate(float scale, string name)
        {
            if (!(scale > 0f && scale <= 1f))
                Debug.unityLogger.Log(LogType.Assert, $"Invalid {name}: {Printed(scale)}");
        }

        /// <summary>Quantizes <paramref name="scale"/> to Unity's 20 discrete 0.05 steps
        /// (<c>1f + i * -0.05f</c> for <c>i</c> in 0..19), picking the smallest step &gt;= scale
        /// (NaN and &gt;= 1 become 1; &lt;= MinimumScale becomes MinimumScale; measured on Vulkan in
        /// tests/projects/corecaps <c>dynres.factors.*</c>).</summary>
        internal static float Quantize(float scale)
        {
            if (float.IsNaN(scale) || scale >= 1f) return 1f;
            if (scale <= MinimumScale) return MinimumScale;
            int step = (int)((1f - scale) / 0.05f);
            if (step < 0) step = 0;
            if (step >= ScaleSteps - 1) return MinimumScale;
            float lower = 1f + (step + 1) * ScaleStepDelta;
            return lower >= scale ? lower : 1f + step * ScaleStepDelta;
        }

        /// <summary>A float as the Editor's message prints it: six decimals, "nan"/"-nan" and "inf".</summary>
        static string Printed(float value)
        {
            if (float.IsNaN(value))
                return Unbound.Runtime.GraphicsDeviceProfile.Current.Type == Rendering.GraphicsDeviceType.Metal
                    || BitConverter.SingleToInt32Bits(value) >= 0 ? "nan" : "-nan";
            if (float.IsInfinity(value)) return value > 0 ? "inf" : "-inf";
            return value.ToString("F6", System.Globalization.CultureInfo.InvariantCulture);
        }

        public static float widthScaleFactor => s_WidthScale;

        public static float heightScaleFactor => s_HeightScale;

        /// <summary>A side of a dynamically scaled render texture: its size times the factor in
        /// single precision, rounded up (15 x 0.5 is 8, 13 x 0.25 is 4, 10 x 0.3 is 3).</summary>
        internal static int Scaled(int size, float factor) => factor == 1f ? size : Math.Max(1, (int)MathF.Ceiling(size * factor));
    }
}
