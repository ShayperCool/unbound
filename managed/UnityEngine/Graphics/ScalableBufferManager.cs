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
            var device = Unbound.Runtime.GraphicsDeviceProfile.Current;
            if (!device.EditorDynamicResolution) return;
            s_WidthScale = Quantize(widthScale);
            s_HeightScale = Quantize(heightScale);
            // The factors are the Vulkan Editor's; what is drawn into and read from a scaled render
            // texture follows the Metal Editor instead (tests/projects/corecaps residuals-vulkan.json).
            if (Scaling && device.Type == Rendering.GraphicsDeviceType.Vulkan)
                Unbound.Runtime.Stubs.Ignored("ScalableBufferManager.ResizeBuffers",
                    "scales render textures on Vulkan as the Metal Editor does: the Linux Vulkan Editor scales both axes by the width factor, and its ReadPixels and texel-size answers on a scaled render texture differ");
        }

        static void Validate(float scale, string name)
        {
            if (!(scale > 0f && scale <= 1f))
                Debug.unityLogger.Log(LogType.Assert, $"Invalid {name}: {Printed(scale)}");
        }

        /// <summary>The factor kept for a requested scale: one of 20 steps <c>1f + i * -0.05f</c>.
        /// The step index is the single-precision quotient <c>(1f - scale) / 0.05f</c> cut to an
        /// integer; the next smaller step is kept when it still covers the scale, else the
        /// index's own step. Single precision decides the edges (0.35f keeps 0.4, 0.2f keeps
        /// 0.2), and a quotient that is no integer (NaN, or past the integers: an infinite or
        /// huge scale) counts as step 0, so NaN and +infinity keep 1 and -infinity keeps 0.95.
        /// Matches the Unity 6000.0.71f1 Editor on Vulkan, tests/projects/corecaps
        /// <c>dynres.factors.*</c>; on Metal only the factors of tests/projects/nativegfx are
        /// measured.</summary>
        internal static float Quantize(float scale)
        {
            int index = StepIndex((1f - scale) / 0.05f);
            if (index >= ScaleSteps - 1) return MinimumScale;
            int step = Math.Max(index, 0);
            float next = 1f + Math.Min(step + 1, ScaleSteps - 1) * ScaleStepDelta;
            return scale <= next ? next : 1f + step * ScaleStepDelta;
        }

        /// <summary>A quotient cut to an integer; one that has no integer value (NaN, or outside
        /// the 32-bit range) is the lowest integer.</summary>
        static int StepIndex(float quotient) =>
            quotient >= -2147483648f && quotient < 2147483648f ? (int)quotient : int.MinValue;

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
