using System;
using System.Runtime.InteropServices;

namespace UnityEngine
{
    /// <summary>
    /// Unity's global pseudo-random generator: xorshift128 (Marsaglia) over 32-bit words, seeded by
    /// expanding one int with the Mersenne Twister initialization recurrence. The same seed gives
    /// the same sequence as in Unity.
    /// </summary>
    public static partial class Random
    {
        /// <summary>The full internal state of the generator (four 32-bit words).</summary>
        [Serializable]
        [StructLayout(LayoutKind.Sequential)]
        public partial struct State
        {
            private int s0;
            private int s1;
            private int s2;
            private int s3;

            internal State(uint s0, uint s1, uint s2, uint s3)
            {
                this.s0 = (int)s0;
                this.s1 = (int)s1;
                this.s2 = (int)s2;
                this.s3 = (int)s3;
            }

            internal uint S0 => (uint)s0;
            internal uint S1 => (uint)s1;
            internal uint S2 => (uint)s2;
            internal uint S3 => (uint)s3;
        }

        private static uint s_X;
        private static uint s_Y;
        private static uint s_Z;
        private static uint s_W;
        private static int s_Seed;

        static Random()
        {
            // Unity seeds the generator from the clock at startup. UNBOUND_RANDOM_SEED pins the seed,
            // so frame-scripted runs (with --fixed-fps) repeat.
            if (int.TryParse(Environment.GetEnvironmentVariable("UNBOUND_RANDOM_SEED"), out var pinned))
                InitState(pinned);
            else
                InitState(Environment.TickCount ^ (int)DateTime.UtcNow.Ticks);
        }

        public static void InitState(int seed)
        {
            s_Seed = seed;
            s_X = (uint)seed;
            s_Y = s_X * 1812433253u + 1u;
            s_Z = s_Y * 1812433253u + 1u;
            s_W = s_Z * 1812433253u + 1u;
        }

        [Obsolete("Deprecated. Use InitState() function or Random.state property instead.")]
        public static int seed
        {
            get => s_Seed;
            set => InitState(value);
        }

        public static State state
        {
            get => new State(s_X, s_Y, s_Z, s_W);
            set
            {
                s_X = value.S0;
                s_Y = value.S1;
                s_Z = value.S2;
                s_W = value.S3;
            }
        }

        internal static uint NextUInt()
        {
            uint t = s_X ^ (s_X << 11);
            s_X = s_Y;
            s_Y = s_Z;
            s_Z = s_W;
            s_W = s_W ^ (s_W >> 19) ^ t ^ (t >> 8);
            return s_W;
        }

        // 23 random bits mapped onto [0, 1], both ends inclusive.
        private static float NextFloat() => (NextUInt() & 0x007FFFFFu) * (1.0f / 8388607.0f);

        private static float RangedFloat(float min, float max)
        {
            float t = NextFloat();
            return t * min + (1.0f - t) * max;
        }

        /// <summary>A random float in [minInclusive, maxInclusive].</summary>
        public static float Range(float minInclusive, float maxInclusive) => RangedFloat(minInclusive, maxInclusive);

        /// <summary>
        /// A random int in [minInclusive, maxExclusive). If the bounds are equal, minInclusive is
        /// returned; if they are swapped, the result is in (maxExclusive, minInclusive].
        /// </summary>
        public static int Range(int minInclusive, int maxExclusive)
        {
            if (minInclusive < maxExclusive)
            {
                uint range = (uint)(maxExclusive - minInclusive);
                return (int)(NextUInt() % range) + minInclusive;
            }
            if (minInclusive > maxExclusive)
            {
                uint range = (uint)(minInclusive - maxExclusive);
                return minInclusive - (int)(NextUInt() % range);
            }
            return minInclusive;
        }

        [Obsolete("Use Random.Range instead")]
        public static float RandomRange(float min, float max) => Range(min, max);

        [Obsolete("Use Random.Range instead")]
        public static int RandomRange(int min, int max) => Range(min, max);

        /// <summary>A random float in [0, 1] (both inclusive).</summary>
        public static float value => NextFloat();

        public static Vector3 insideUnitSphere
        {
            get
            {
                Vector3 dir = RandomUnitVector();
                return dir * (float)Math.Pow(NextFloat(), 1.0 / 3.0);
            }
        }

        public static Vector2 insideUnitCircle
        {
            get
            {
                float theta = RangedFloat(0.0f, 2.0f * Mathf.PI);
                float radius = (float)Math.Sqrt(RangedFloat(0.0f, 1.0f));
                return new Vector2(radius * (float)Math.Cos(theta), radius * (float)Math.Sin(theta));
            }
        }

        public static Vector3 onUnitSphere => RandomUnitVector();

        private static Vector3 RandomUnitVector()
        {
            float z = RangedFloat(-1.0f, 1.0f);
            float a = RangedFloat(0.0f, 2.0f * Mathf.PI);
            float r = (float)Math.Sqrt(1.0f - z * z);
            return new Vector3(r * (float)Math.Cos(a), r * (float)Math.Sin(a), z);
        }

        /// <summary>A random rotation (normalized random 4-vector; not uniformly distributed).</summary>
        public static Quaternion rotation
        {
            get
            {
                Quaternion q = new Quaternion(
                    RangedFloat(-1.0f, 1.0f),
                    RangedFloat(-1.0f, 1.0f),
                    RangedFloat(-1.0f, 1.0f),
                    RangedFloat(-1.0f, 1.0f));
                return Quaternion.Normalize(q);
            }
        }

        /// <summary>A uniformly distributed random rotation (Shoemake's subgroup algorithm).</summary>
        public static Quaternion rotationUniform
        {
            get
            {
                float u1 = NextFloat();
                float u2 = RangedFloat(0.0f, 2.0f * Mathf.PI);
                float u3 = RangedFloat(0.0f, 2.0f * Mathf.PI);
                float sq1 = (float)Math.Sqrt(1.0f - u1);
                float sq2 = (float)Math.Sqrt(u1);
                return Quaternion.Normalize(new Quaternion(
                    sq1 * (float)Math.Sin(u2),
                    sq1 * (float)Math.Cos(u2),
                    sq2 * (float)Math.Sin(u3),
                    sq2 * (float)Math.Cos(u3)));
            }
        }

        public static Color ColorHSV() => ColorHSV(0f, 1f, 0f, 1f, 0f, 1f, 1f, 1f);

        public static Color ColorHSV(float hueMin, float hueMax) => ColorHSV(hueMin, hueMax, 0f, 1f, 0f, 1f, 1f, 1f);

        public static Color ColorHSV(float hueMin, float hueMax, float saturationMin, float saturationMax) =>
            ColorHSV(hueMin, hueMax, saturationMin, saturationMax, 0f, 1f, 1f, 1f);

        public static Color ColorHSV(float hueMin, float hueMax, float saturationMin, float saturationMax, float valueMin, float valueMax) =>
            ColorHSV(hueMin, hueMax, saturationMin, saturationMax, valueMin, valueMax, 1f, 1f);

        /// <summary>A random color from HSV and alpha ranges (each sampled uniformly, in this order).</summary>
        public static Color ColorHSV(float hueMin, float hueMax, float saturationMin, float saturationMax, float valueMin, float valueMax, float alphaMin, float alphaMax)
        {
            float h = Mathf.Lerp(hueMin, hueMax, value);
            float s = Mathf.Lerp(saturationMin, saturationMax, value);
            float v = Mathf.Lerp(valueMin, valueMax, value);
            Color result = Color.HSVToRGB(h, s, v, true);
            result.a = Mathf.Lerp(alphaMin, alphaMax, value);
            return result;
        }
    }
}
