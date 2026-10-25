using System;

namespace UnityEngine
{
    /// <summary>A collection of common math functions (Unity's <c>Mathf</c>).</summary>
    public partial struct Mathf
    {
        public const float PI = (float)Math.PI;
        public const float Infinity = float.PositiveInfinity;
        public const float NegativeInfinity = float.NegativeInfinity;
        public const float Deg2Rad = PI * 2F / 360F;
        public const float Rad2Deg = 1F / Deg2Rad;

        /// <summary>
        /// The smallest positive float. Unity's players that flush denormals to zero use the smallest
        /// normal float instead; Unbound runs on CoreCLR, which keeps denormals, like the Editor.
        /// </summary>
        public static readonly float Epsilon = float.Epsilon;

        public static float Sin(float f) => (float)Math.Sin(f);
        public static float Cos(float f) => (float)Math.Cos(f);
        public static float Tan(float f) => (float)Math.Tan(f);
        public static float Asin(float f) => (float)Math.Asin(f);
        public static float Acos(float f) => (float)Math.Acos(f);
        public static float Atan(float f) => (float)Math.Atan(f);
        public static float Atan2(float y, float x) => (float)Math.Atan2(y, x);
        public static float Sqrt(float f) => (float)Math.Sqrt(f);
        public static float Abs(float f) => Math.Abs(f);
        public static int Abs(int value) => Math.Abs(value);

        public static float Min(float a, float b) => a < b ? a : b;

        public static float Min(params float[] values)
        {
            int len = values.Length;
            if (len == 0)
                return 0;
            float m = values[0];
            for (int i = 1; i < len; i++)
                if (values[i] < m)
                    m = values[i];
            return m;
        }

        public static int Min(int a, int b) => a < b ? a : b;

        public static int Min(params int[] values)
        {
            int len = values.Length;
            if (len == 0)
                return 0;
            int m = values[0];
            for (int i = 1; i < len; i++)
                if (values[i] < m)
                    m = values[i];
            return m;
        }

        public static float Max(float a, float b) => a > b ? a : b;

        public static float Max(params float[] values)
        {
            int len = values.Length;
            if (len == 0)
                return 0;
            float m = values[0];
            for (int i = 1; i < len; i++)
                if (values[i] > m)
                    m = values[i];
            return m;
        }

        public static int Max(int a, int b) => a > b ? a : b;

        public static int Max(params int[] values)
        {
            int len = values.Length;
            if (len == 0)
                return 0;
            int m = values[0];
            for (int i = 1; i < len; i++)
                if (values[i] > m)
                    m = values[i];
            return m;
        }

        public static float Pow(float f, float p) => (float)Math.Pow(f, p);
        public static float Exp(float power) => (float)Math.Exp(power);
        public static float Log(float f, float p) => (float)Math.Log(f, p);
        public static float Log(float f) => (float)Math.Log(f);
        public static float Log10(float f) => (float)Math.Log10(f);
        public static float Ceil(float f) => (float)Math.Ceiling(f);
        public static float Floor(float f) => (float)Math.Floor(f);

        /// <summary>Rounds to the nearest integer; halves go to the even neighbour, like Unity.</summary>
        public static float Round(float f) => (float)Math.Round(f);

        public static int CeilToInt(float f) => (int)Math.Ceiling(f);
        public static int FloorToInt(float f) => (int)Math.Floor(f);
        public static int RoundToInt(float f) => (int)Math.Round(f);

        public static float Sign(float f) => f >= 0F ? 1F : -1F;

        public static float Clamp(float value, float min, float max)
        {
            if (value < min)
                value = min;
            else if (value > max)
                value = max;
            return value;
        }

        public static int Clamp(int value, int min, int max)
        {
            if (value < min)
                value = min;
            else if (value > max)
                value = max;
            return value;
        }

        public static float Clamp01(float value)
        {
            if (value < 0F)
                return 0F;
            if (value > 1F)
                return 1F;
            return value;
        }

        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);

        public static float LerpUnclamped(float a, float b, float t) => a + (b - a) * t;

        /// <summary>Like Lerp, but interpolates along the shortest arc for angles in degrees.</summary>
        public static float LerpAngle(float a, float b, float t)
        {
            float delta = Repeat(b - a, 360);
            if (delta > 180)
                delta -= 360;
            return a + delta * Clamp01(t);
        }

        public static float MoveTowards(float current, float target, float maxDelta)
        {
            if (Abs(target - current) <= maxDelta)
                return target;
            return current + Sign(target - current) * maxDelta;
        }

        public static float MoveTowardsAngle(float current, float target, float maxDelta)
        {
            float deltaAngle = DeltaAngle(current, target);
            if (-maxDelta < deltaAngle && deltaAngle < maxDelta)
                return target;
            target = current + deltaAngle;
            return MoveTowards(current, target, maxDelta);
        }

        public static float SmoothStep(float from, float to, float t)
        {
            t = Clamp01(t);
            t = -2.0F * t * t * t + 3.0F * t * t;
            return to * t + from * (1F - t);
        }

        public static float Gamma(float value, float absmax, float gamma)
        {
            bool negative = value < 0F;
            float absval = Abs(value);
            if (absval > absmax)
                return negative ? -absval : absval;
            float result = Pow(absval / absmax, gamma) * absmax;
            return negative ? -result : result;
        }

        /// <summary>
        /// Compares two floats with a tolerance relative to their magnitude, floored at a few
        /// multiples of <see cref="Epsilon"/>.
        /// </summary>
        public static bool Approximately(float a, float b) =>
            Abs(b - a) < Max(0.000001f * Max(Abs(a), Abs(b)), Epsilon * 8);

        public static float SmoothDamp(float current, float target, ref float currentVelocity, float smoothTime, float maxSpeed)
        {
            float deltaTime = Time.deltaTime;
            return SmoothDamp(current, target, ref currentVelocity, smoothTime, maxSpeed, deltaTime);
        }

        public static float SmoothDamp(float current, float target, ref float currentVelocity, float smoothTime)
        {
            float deltaTime = Time.deltaTime;
            float maxSpeed = Infinity;
            return SmoothDamp(current, target, ref currentVelocity, smoothTime, maxSpeed, deltaTime);
        }

        /// <summary>
        /// Critically damped spring towards <paramref name="target"/> (Game Programming Gems 4,
        /// "Critically Damped Ease-In/Ease-Out Smoothing"), never overshooting the target.
        /// </summary>
        public static float SmoothDamp(float current, float target, ref float currentVelocity, float smoothTime, float maxSpeed, float deltaTime)
        {
            smoothTime = Max(0.0001F, smoothTime);
            float omega = 2F / smoothTime;

            float x = omega * deltaTime;
            float exp = 1F / (1F + x + 0.48F * x * x + 0.235F * x * x * x);
            float change = current - target;
            float originalTo = target;

            float maxChange = maxSpeed * smoothTime;
            change = Clamp(change, -maxChange, maxChange);
            target = current - change;

            float temp = (currentVelocity + omega * change) * deltaTime;
            currentVelocity = (currentVelocity - omega * temp) * exp;
            float output = target + (change + temp) * exp;

            if (originalTo - current > 0.0F == output > originalTo)
            {
                output = originalTo;
                currentVelocity = (output - originalTo) / deltaTime;
            }
            return output;
        }

        public static float SmoothDampAngle(float current, float target, ref float currentVelocity, float smoothTime, float maxSpeed)
        {
            float deltaTime = Time.deltaTime;
            return SmoothDampAngle(current, target, ref currentVelocity, smoothTime, maxSpeed, deltaTime);
        }

        public static float SmoothDampAngle(float current, float target, ref float currentVelocity, float smoothTime)
        {
            float deltaTime = Time.deltaTime;
            float maxSpeed = Infinity;
            return SmoothDampAngle(current, target, ref currentVelocity, smoothTime, maxSpeed, deltaTime);
        }

        public static float SmoothDampAngle(float current, float target, ref float currentVelocity, float smoothTime, float maxSpeed, float deltaTime)
        {
            target = current + DeltaAngle(current, target);
            return SmoothDamp(current, target, ref currentVelocity, smoothTime, maxSpeed, deltaTime);
        }

        /// <summary>Loops <paramref name="t"/> into [0, length].</summary>
        public static float Repeat(float t, float length) => Clamp(t - Floor(t / length) * length, 0.0f, length);

        public static float PingPong(float t, float length)
        {
            t = Repeat(t, length * 2F);
            return length - Abs(t - length);
        }

        public static float InverseLerp(float a, float b, float value)
        {
            if (a != b)
                return Clamp01((value - a) / (b - a));
            return 0.0f;
        }

        /// <summary>Shortest signed difference between two angles in degrees, in (-180, 180].</summary>
        public static float DeltaAngle(float current, float target)
        {
            float delta = Repeat(target - current, 360.0F);
            if (delta > 180.0F)
                delta -= 360.0F;
            return delta;
        }

        public static int ClosestPowerOfTwo(int value)
        {
            int next = NextPowerOfTwo(value);
            int prev = next >> 1;
            if (value - prev < next - value)
                return prev;
            return next;
        }

        public static bool IsPowerOfTwo(int value) => (value & (value - 1)) == 0;

        public static int NextPowerOfTwo(int value)
        {
            value -= 1;
            value |= value >> 16;
            value |= value >> 8;
            value |= value >> 4;
            value |= value >> 2;
            value |= value >> 1;
            return value + 1;
        }

        /// <summary>Converts an sRGB-encoded value to linear (the exact sRGB transfer function).</summary>
        public static float GammaToLinearSpace(float value)
        {
            if (value <= 0.04045F)
                return value / 12.92F;
            if (value < 1.0F)
                return Pow((value + 0.055F) / 1.055F, 2.4F);
            if (value == 1.0F)
                return 1.0F;
            return Pow(value, 2.2F);
        }

        /// <summary>Converts a linear value to sRGB encoding (the exact sRGB transfer function).</summary>
        public static float LinearToGammaSpace(float value)
        {
            if (value <= 0.0F)
                return 0.0F;
            if (value <= 0.0031308F)
                return 12.92F * value;
            if (value < 1.0F)
                return 1.055F * Pow(value, 0.4166667F) - 0.055F;
            if (value == 1.0F)
                return 1.0F;
            return Pow(value, 0.45454545F);
        }

        /// <summary>
        /// Colour of a black body at <paramref name="kelvin"/> (1000..40000 K), as linear RGB
        /// normalized so the largest component is 1. Uses Krystek's approximation of the Planckian
        /// locus in CIE 1960 UCS, converted through CIE XYZ to linear sRGB primaries.
        /// </summary>
        public static Color CorrelatedColorTemperatureToRGB(float kelvin)
        {
            double t = Clamp(kelvin, 1000f, 40000f);
            double u = (0.860117757 + 1.54118254e-4 * t + 1.28641212e-7 * t * t) /
                       (1.0 + 8.42420235e-4 * t + 7.08145163e-7 * t * t);
            double v = (0.317398726 + 4.22806245e-5 * t + 4.20481691e-8 * t * t) /
                       (1.0 - 2.89741816e-5 * t + 1.61456053e-7 * t * t);
            double d = 2.0 * u - 8.0 * v + 4.0;
            double x = 3.0 * u / d;
            double y = 2.0 * v / d;
            double X = x / y;
            double Z = (1.0 - x - y) / y;
            double r = 3.2404542 * X - 1.5371385 - 0.4985314 * Z;
            double g = -0.9692660 * X + 1.8760108 + 0.0415560 * Z;
            double b = 0.0556434 * X - 0.2040259 + 1.0572252 * Z;
            r = Math.Max(r, 0.0);
            g = Math.Max(g, 0.0);
            b = Math.Max(b, 0.0);
            double m = Math.Max(r, Math.Max(g, b));
            if (m > 0.0)
            {
                r /= m;
                g /= m;
                b /= m;
            }
            return new Color((float)r, (float)g, (float)b, 1f);
        }

        public static ushort FloatToHalf(float val) => BitConverter.HalfToUInt16Bits((Half)val);

        public static float HalfToFloat(ushort val) => (float)BitConverter.UInt16BitsToHalf(val);

        /// <summary>
        /// 2D gradient noise: Perlin's improved (3D) noise on the z = 0 plane at (|x|, |y|),
        /// remapped so that its value range of about [-0.69, 0.793] falls in [0, 1]. Like Unity,
        /// every integer lattice point gives the same value (about 0.4652731), the noise is
        /// mirrored about both axes, and results may slightly leave [0, 1] (measured against the
        /// Editor: tests/projects/grass).
        /// </summary>
        public static float PerlinNoise(float x, float y)
        {
            float n = PerlinImpl.Noise2(Math.Abs(x), Math.Abs(y));
            return (n + 0.69F) / (0.793F + 0.69F);
        }

        /// <summary>The 2D noise along the x axis (as Unity's).</summary>
        public static float PerlinNoise1D(float x) => PerlinNoise(x, 0F);

        private static class PerlinImpl
        {
            // Ken Perlin's reference permutation ("Improved Noise", SIGGRAPH 2002), repeated.
            private static readonly byte[] s_Perm = BuildPermutation();

            private static byte[] BuildPermutation()
            {
                byte[] p =
                {
                    151, 160, 137, 91, 90, 15, 131, 13, 201, 95, 96, 53, 194, 233, 7, 225, 140, 36, 103, 30, 69, 142,
                    8, 99, 37, 240, 21, 10, 23, 190, 6, 148, 247, 120, 234, 75, 0, 26, 197, 62, 94, 252, 219, 203, 117,
                    35, 11, 32, 57, 177, 33, 88, 237, 149, 56, 87, 174, 20, 125, 136, 171, 168, 68, 175, 74, 165, 71,
                    134, 139, 48, 27, 166, 77, 146, 158, 231, 83, 111, 229, 122, 60, 211, 133, 230, 220, 105, 92, 41,
                    55, 46, 245, 40, 244, 102, 143, 54, 65, 25, 63, 161, 1, 216, 80, 73, 209, 76, 132, 187, 208, 89,
                    18, 169, 200, 196, 135, 130, 116, 188, 159, 86, 164, 100, 109, 198, 173, 186, 3, 64, 52, 217, 226,
                    250, 124, 123, 5, 202, 38, 147, 118, 126, 255, 82, 85, 212, 207, 206, 59, 227, 47, 16, 58, 17, 182,
                    189, 28, 42, 223, 183, 170, 213, 119, 248, 152, 2, 44, 154, 163, 70, 221, 153, 101, 155, 167, 43,
                    172, 9, 129, 22, 39, 253, 19, 98, 108, 110, 79, 113, 224, 232, 178, 185, 112, 104, 218, 246, 97,
                    228, 251, 34, 242, 193, 238, 210, 144, 12, 191, 179, 162, 241, 81, 51, 145, 235, 249, 14, 239,
                    107, 49, 192, 214, 31, 181, 199, 106, 157, 184, 84, 204, 176, 115, 121, 50, 45, 127, 4, 150, 254,
                    138, 236, 205, 93, 222, 114, 67, 29, 24, 72, 243, 141, 128, 195, 78, 66, 215, 61, 156, 180,
                };
                var perm = new byte[512];
                for (int i = 0; i < 512; i++)
                    perm[i] = p[i & 255];
                return perm;
            }

            private static float Fade(float t) => t * t * t * (t * (t * 6F - 15F) + 10F);

            private static float Grad2(int hash, float x, float y)
            {
                int h = hash & 15;
                float u = h < 8 ? x : y;
                float v = h < 4 ? y : (h == 12 || h == 14 ? x : 0F);
                return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
            }

            internal static float Noise2(float x, float y)
            {
                float fx = (float)Math.Floor(x);
                float fy = (float)Math.Floor(y);
                int X = (int)(long)fx & 255;
                int Y = (int)(long)fy & 255;
                x -= fx;
                y -= fy;
                float u = Fade(x);
                float v = Fade(y);
                byte[] p = s_Perm;
                // The 3D reference's hashing with Z = 0: AA = p[A] + 0, gradients from p[AA].
                int a = p[X] + Y, aa = p[a], ab = p[a + 1];
                int b = p[X + 1] + Y, ba = p[b], bb = p[b + 1];
                float x1 = LerpUnclamped(Grad2(p[aa], x, y), Grad2(p[ba], x - 1F, y), u);
                float x2 = LerpUnclamped(Grad2(p[ab], x, y - 1F), Grad2(p[bb], x - 1F, y - 1F), u);
                return LerpUnclamped(x1, x2, v);
            }
        }
    }
}
