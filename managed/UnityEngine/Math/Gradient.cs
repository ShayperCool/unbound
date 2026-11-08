using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace UnityEngine
{
    /// <summary>A color key of a Gradient.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public partial struct GradientColorKey
    {
        public Color color;
        public float time;

        public GradientColorKey(Color col, float time)
        {
            color = col;
            this.time = time;
        }
    }

    /// <summary>An alpha key of a Gradient.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public partial struct GradientAlphaKey
    {
        public float alpha;
        public float time;

        public GradientAlphaKey(float alpha, float time)
        {
            this.alpha = alpha;
            this.time = time;
        }
    }

    /// <summary>How a Gradient interpolates between its keys.</summary>
    public enum GradientMode
    {
        Blend = 0,
        Fixed = 1,
        PerceptualBlend = 2,
    }

    /// <summary>
    /// A color gradient over [0, 1] made of up to 8 color keys and 8 alpha keys. Unity backs this
    /// class with a native object; in Unbound it is plain managed state.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public partial class Gradient : IEquatable<Gradient>
    {
        internal const int kMaxKeys = 8;

        private GradientColorKey[] m_ColorKeys;
        private GradientAlphaKey[] m_AlphaKeys;
        private GradientMode m_Mode = GradientMode.Blend;
        private ColorSpace m_ColorSpace = ColorSpace.Gamma;

        /// <summary>A white, opaque gradient (keys at 0 and 1).</summary>
        public Gradient()
        {
            m_ColorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) };
            m_AlphaKeys = new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) };
        }

        public GradientColorKey[] colorKeys
        {
            get => (GradientColorKey[])m_ColorKeys.Clone();
            set => m_ColorKeys = SortedColorKeys(value);
        }

        public GradientAlphaKey[] alphaKeys
        {
            get => (GradientAlphaKey[])m_AlphaKeys.Clone();
            set => m_AlphaKeys = SortedAlphaKeys(value);
        }

        public GradientMode mode
        {
            get => m_Mode;
            set => m_Mode = value;
        }

        /// <summary>The color space the color keys are expressed in (affects PerceptualBlend).</summary>
        public ColorSpace colorSpace
        {
            get => m_ColorSpace;
            set => m_ColorSpace = value;
        }

        public void SetKeys(GradientColorKey[] colorKeys, GradientAlphaKey[] alphaKeys)
        {
            GradientColorKey[] c = SortedColorKeys(colorKeys);
            GradientAlphaKey[] a = SortedAlphaKeys(alphaKeys);
            m_ColorKeys = c;
            m_AlphaKeys = a;
        }

        private static GradientColorKey[] SortedColorKeys(GradientColorKey[] keys)
        {
            if (keys == null)
                throw new ArgumentNullException("colorKeys");
            if (keys.Length > kMaxKeys)
                throw new ArgumentException("Gradient can have at most " + kMaxKeys + " color keys.", "colorKeys");
            var copy = (GradientColorKey[])keys.Clone();
            for (int i = 1; i < copy.Length; i++)
            {
                GradientColorKey k = copy[i];
                int j = i - 1;
                while (j >= 0 && copy[j].time > k.time)
                {
                    copy[j + 1] = copy[j];
                    j--;
                }
                copy[j + 1] = k;
            }
            return copy;
        }

        private static GradientAlphaKey[] SortedAlphaKeys(GradientAlphaKey[] keys)
        {
            if (keys == null)
                throw new ArgumentNullException("alphaKeys");
            if (keys.Length > kMaxKeys)
                throw new ArgumentException("Gradient can have at most " + kMaxKeys + " alpha keys.", "alphaKeys");
            var copy = (GradientAlphaKey[])keys.Clone();
            for (int i = 1; i < copy.Length; i++)
            {
                GradientAlphaKey k = copy[i];
                int j = i - 1;
                while (j >= 0 && copy[j].time > k.time)
                {
                    copy[j + 1] = copy[j];
                    j--;
                }
                copy[j + 1] = k;
            }
            return copy;
        }

        /// <summary>
        /// The color at <paramref name="time"/>. Blend interpolates linearly between the
        /// surrounding keys, PerceptualBlend interpolates in Oklab, and Fixed takes the first key at
        /// or after <paramref name="time"/>. Times outside the keys take the nearest end key.
        /// </summary>
        public Color Evaluate(float time)
        {
            Color rgb = EvaluateColor(time);
            rgb.a = EvaluateAlpha(time);
            return rgb;
        }

        private Color EvaluateColor(float time)
        {
            GradientColorKey[] keys = m_ColorKeys;
            if (keys.Length == 0)
                return Color.white;
            int last = keys.Length - 1;
            GradientColorKey k0, k1;
            float t;
            if (time <= keys[0].time || time >= keys[last].time)
            {
                k0 = k1 = time <= keys[0].time ? keys[0] : keys[last];
                t = 0f;
            }
            else
            {
                int i = 1;
                while (keys[i].time < time)
                    i++;
                k0 = keys[i - 1];
                k1 = keys[i];
                if (m_Mode == GradientMode.Fixed)
                    return k1.color;
                float span = k1.time - k0.time;
                t = span > 0f ? (time - k0.time) / span : 1f;
            }
            // PerceptualBlend goes through Oklab also at and beyond the end keys (6000.0.71f1,
            // tests/projects/particlecurves gradient/struct/perceptual); Blend and Fixed return the
            // keys' own values.
            if (m_Mode == GradientMode.PerceptualBlend)
                return PerceptualLerp(k0.color, k1.color, t, m_ColorSpace == ColorSpace.Linear);
            return Color.LerpUnclamped(k0.color, k1.color, t);
        }

        private float EvaluateAlpha(float time)
        {
            GradientAlphaKey[] keys = m_AlphaKeys;
            if (keys.Length == 0)
                return 1f;
            if (time <= keys[0].time)
                return keys[0].alpha;
            int last = keys.Length - 1;
            if (time >= keys[last].time)
                return keys[last].alpha;

            int i = 1;
            while (keys[i].time < time)
                i++;
            GradientAlphaKey k0 = keys[i - 1], k1 = keys[i];
            if (m_Mode == GradientMode.Fixed)
                return k1.alpha;
            float span = k1.time - k0.time;
            float t = span > 0f ? (time - k0.time) / span : 1f;
            return k0.alpha + (k1.alpha - k0.alpha) * t;
        }

        // Interpolation in Oklab (Bjorn Ottosson, 2020) between colors given in sRGB or linear, in
        // single precision as the 6000.0.71f1 Editor computes it: the cube-rooted LMS values are
        // interpolated (Oklab's L, a, b are a linear map of them), and a result for sRGB keys
        // comes back in 8-bit steps (tests/projects/particlecurves gradient/struct/perceptual).
        private static Color PerceptualLerp(Color a, Color b, float t, bool linearKeys)
        {
            Vector3 la = CubeRootLms(a, linearKeys), lb = CubeRootLms(b, linearKeys);
            float l = la.x + (lb.x - la.x) * t, m = la.y + (lb.y - la.y) * t, s = la.z + (lb.z - la.z) * t;
            l = l * l * l;
            m = m * m * m;
            s = s * s * s;
            float r = s * 0.230969936f + m * -3.3077116f + l * 4.0767417f;
            float g = s * -0.341319382f + m * 2.60975742f + l * -1.26843798f;
            float bl = s * 1.70761466f + m * -0.703418612f + l * -0.00419608643f;
            if (linearKeys) return new Color(r, g, bl, 1f);
            return new Color(LinearToSrgb(r), LinearToSrgb(g), LinearToSrgb(bl), 1f);
        }

        private static Vector3 CubeRootLms(Color c, bool linear)
        {
            float r = linear ? c.r : SrgbToLinear(c.r), g = linear ? c.g : SrgbToLinear(c.g), b = linear ? c.b : SrgbToLinear(c.b);
            float l = g * 0.536332548f + b * 0.0514459945f + r * 0.412221462f;
            float m = g * 0.680699527f + b * 0.10739696f + r * 0.211903498f;
            float s = g * 0.28171885f + b * 0.629978716f + r * 0.0883024633f;
            return new Vector3(MathF.Cbrt(l), MathF.Cbrt(m), MathF.Cbrt(s));
        }

        private static float SrgbToLinear(float c)
        {
            c = Math.Max(c, 0f);
            if (c < 0.04045f) return c * 0.0773993805f;
            return c < 1f ? MathF.Pow((c + 0.055f) * 0.947867334f, 2.4f) : MathF.Pow(c, 2.2f);
        }

        /// <summary>To sRGB: in 8-bit steps up to 1, by the power 1/2.2 above.</summary>
        private static float LinearToSrgb(float x)
        {
            x = Math.Max(x, 0f);
            if (x > 1f) return MathF.Pow(x, 0.454545468f);
            float srgb = x <= 0.0031308f ? x * 12.92f : 1.055f * MathF.Pow(x, 0.416666687f) - 0.055f;
            return MathF.Floor(srgb * 255f + 0.5f) / 255f;
        }

        public override bool Equals(object o)
        {
            if (o is null)
                return false;
            if (ReferenceEquals(this, o))
                return true;
            return o is Gradient other && Equals(other);
        }

        /// <summary>True if both gradients have the same keys, mode and color space.</summary>
        public bool Equals(Gradient other)
        {
            if (other is null)
                return false;
            if (ReferenceEquals(this, other))
                return true;
            if (m_Mode != other.m_Mode || m_ColorSpace != other.m_ColorSpace ||
                m_ColorKeys.Length != other.m_ColorKeys.Length || m_AlphaKeys.Length != other.m_AlphaKeys.Length)
                return false;
            for (int i = 0; i < m_ColorKeys.Length; i++)
                if (!m_ColorKeys[i].time.Equals(other.m_ColorKeys[i].time) || !m_ColorKeys[i].color.Equals(other.m_ColorKeys[i].color))
                    return false;
            for (int i = 0; i < m_AlphaKeys.Length; i++)
                if (!m_AlphaKeys[i].time.Equals(other.m_AlphaKeys[i].time) || !m_AlphaKeys[i].alpha.Equals(other.m_AlphaKeys[i].alpha))
                    return false;
            return true;
        }

        /// <summary>Identity-based, like Unity's (which hashes the native pointer).</summary>
        public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
    }
}
