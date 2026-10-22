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
            if (time <= keys[0].time)
                return keys[0].color;
            int last = keys.Length - 1;
            if (time >= keys[last].time)
                return keys[last].color;

            int i = 1;
            while (keys[i].time < time)
                i++;
            GradientColorKey k0 = keys[i - 1], k1 = keys[i];
            if (m_Mode == GradientMode.Fixed)
                return k1.color;
            float span = k1.time - k0.time;
            float t = span > 0f ? (time - k0.time) / span : 1f;
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

        // Interpolation in Oklab (Bjorn Ottosson, 2020) between colors given in sRGB or linear.
        private static Color PerceptualLerp(Color a, Color b, float t, bool linearKeys)
        {
            Color la = linearKeys ? a : a.linear;
            Color lb = linearKeys ? b : b.linear;
            LinearToOklab(la, out double L1, out double A1, out double B1);
            LinearToOklab(lb, out double L2, out double A2, out double B2);
            double L = L1 + (L2 - L1) * t;
            double A = A1 + (A2 - A1) * t;
            double B = B1 + (B2 - B1) * t;
            Color result = OklabToLinear(L, A, B);
            return linearKeys ? result : result.gamma;
        }

        private static double Cbrt(double v) => Math.Cbrt(v);

        private static void LinearToOklab(Color c, out double L, out double A, out double B)
        {
            double l = 0.4122214708 * c.r + 0.5363325363 * c.g + 0.0514459929 * c.b;
            double m = 0.2119034982 * c.r + 0.6806995451 * c.g + 0.1073969566 * c.b;
            double s = 0.0883024619 * c.r + 0.2817188376 * c.g + 0.6299787005 * c.b;
            l = Cbrt(l);
            m = Cbrt(m);
            s = Cbrt(s);
            L = 0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s;
            A = 1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s;
            B = 0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s;
        }

        private static Color OklabToLinear(double L, double A, double B)
        {
            double l = L + 0.3963377774 * A + 0.2158037573 * B;
            double m = L - 0.1055613458 * A - 0.0638541728 * B;
            double s = L - 0.0894841775 * A - 1.2914855480 * B;
            l = l * l * l;
            m = m * m * m;
            s = s * s * s;
            return new Color(
                (float)(4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s),
                (float)(-1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s),
                (float)(-0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s),
                1f);
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
