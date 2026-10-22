using System;
using System.Runtime.InteropServices;

namespace UnityEngine
{
    /// <summary>Representation of RGBA colors with float components.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public partial struct Color : IEquatable<Color>, IFormattable
    {
        public float r;
        public float g;
        public float b;
        public float a;

        public Color(float r, float g, float b, float a)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }

        public Color(float r, float g, float b)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            a = 1.0F;
        }

        public override string ToString() => ToString(null, null);

        public string ToString(string format) => ToString(format, null);

        public string ToString(string format, IFormatProvider formatProvider)
        {
            format = MathFormat.Format(format, "F3");
            formatProvider = MathFormat.Provider(formatProvider);
            return "RGBA(" + r.ToString(format, formatProvider) + ", " + g.ToString(format, formatProvider) + ", " +
                   b.ToString(format, formatProvider) + ", " + a.ToString(format, formatProvider) + ")";
        }

        public override int GetHashCode() => ((Vector4)this).GetHashCode();

        public override bool Equals(object other) => other is Color c && Equals(c);

        public bool Equals(Color other) => r.Equals(other.r) && g.Equals(other.g) && b.Equals(other.b) && a.Equals(other.a);

        public static Color operator +(Color a, Color b) => new Color(a.r + b.r, a.g + b.g, a.b + b.b, a.a + b.a);
        public static Color operator -(Color a, Color b) => new Color(a.r - b.r, a.g - b.g, a.b - b.b, a.a - b.a);
        public static Color operator *(Color a, Color b) => new Color(a.r * b.r, a.g * b.g, a.b * b.b, a.a * b.a);
        public static Color operator *(Color a, float b) => new Color(a.r * b, a.g * b, a.b * b, a.a * b);
        public static Color operator *(float b, Color a) => new Color(a.r * b, a.g * b, a.b * b, a.a * b);
        public static Color operator /(Color a, float b) => new Color(a.r / b, a.g / b, a.b / b, a.a / b);

        /// <summary>Approximate equality, compared as Vector4.</summary>
        public static bool operator ==(Color lhs, Color rhs) => (Vector4)lhs == (Vector4)rhs;

        public static bool operator !=(Color lhs, Color rhs) => !(lhs == rhs);

        public static Color Lerp(Color a, Color b, float t)
        {
            t = Mathf.Clamp01(t);
            return new Color(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t, a.a + (b.a - a.a) * t);
        }

        public static Color LerpUnclamped(Color a, Color b, float t) =>
            new Color(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t, a.a + (b.a - a.a) * t);

        internal Color RGBMultiplied(float multiplier) => new Color(r * multiplier, g * multiplier, b * multiplier, a);

        public static Color red => new Color(1F, 0F, 0F, 1F);
        public static Color green => new Color(0F, 1F, 0F, 1F);
        public static Color blue => new Color(0F, 0F, 1F, 1F);
        public static Color white => new Color(1F, 1F, 1F, 1F);
        public static Color black => new Color(0F, 0F, 0F, 1F);
        public static Color yellow => new Color(1F, 235F / 255F, 4F / 255F, 1F);
        public static Color cyan => new Color(0F, 1F, 1F, 1F);
        public static Color magenta => new Color(1F, 0F, 1F, 1F);
        public static Color gray => new Color(.5F, .5F, .5F, 1F);
        public static Color grey => new Color(.5F, .5F, .5F, 1F);
        public static Color clear => new Color(0F, 0F, 0F, 0F);

        /// <summary>Perceived brightness (Rec. 601 luma weights).</summary>
        public float grayscale => 0.299F * r + 0.587F * g + 0.114F * b;

        /// <summary>This sRGB-encoded color converted to linear space (alpha unchanged).</summary>
        public Color linear =>
            new Color(Mathf.GammaToLinearSpace(r), Mathf.GammaToLinearSpace(g), Mathf.GammaToLinearSpace(b), a);

        /// <summary>This linear color converted to sRGB encoding (alpha unchanged).</summary>
        public Color gamma =>
            new Color(Mathf.LinearToGammaSpace(r), Mathf.LinearToGammaSpace(g), Mathf.LinearToGammaSpace(b), a);

        public float maxColorComponent => Mathf.Max(Mathf.Max(r, g), b);

        public static implicit operator Vector4(Color c) => new Vector4(c.r, c.g, c.b, c.a);

        public static implicit operator Color(Vector4 v) => new Color(v.x, v.y, v.z, v.w);

        public float this[int index]
        {
            get
            {
                switch (index)
                {
                    case 0: return r;
                    case 1: return g;
                    case 2: return b;
                    case 3: return a;
                    default: throw new IndexOutOfRangeException("Invalid Color index(" + index + ")!");
                }
            }
            set
            {
                switch (index)
                {
                    case 0: r = value; break;
                    case 1: g = value; break;
                    case 2: b = value; break;
                    case 3: a = value; break;
                    default: throw new IndexOutOfRangeException("Invalid Color index(" + index + ")!");
                }
            }
        }

        /// <summary>Converts RGB to hue, saturation and value, each in [0, 1].</summary>
        public static void RGBToHSV(Color rgbColor, out float H, out float S, out float V)
        {
            float red = rgbColor.r, green = rgbColor.g, blue = rgbColor.b;

            // The dominant channel selects the hue sextant: red 0, green 2, blue 4. Ties prefer
            // red, then green.
            float max, first, second, sextant;
            if (blue > green && blue > red)
            {
                max = blue; first = red; second = green; sextant = 4F;
            }
            else if (green > red)
            {
                max = green; first = blue; second = red; sextant = 2F;
            }
            else
            {
                max = red; first = green; second = blue; sextant = 0F;
            }

            V = max;
            if (max == 0F)
            {
                H = 0F;
                S = 0F;
                return;
            }

            float chroma = max - Math.Min(first, second);
            float hue;
            if (chroma != 0F)
            {
                S = chroma / max;
                hue = sextant + (first - second) / chroma;
            }
            else
            {
                S = 0F;
                hue = sextant + (first - second);
            }
            hue /= 6F;
            if (hue < 0F)
                hue += 1F;
            H = hue;
        }

        public static Color HSVToRGB(float H, float S, float V) => HSVToRGB(H, S, V, true);

        /// <summary>
        /// Converts hue, saturation and value to an opaque RGB color. Without
        /// <paramref name="hdr"/> the components are clamped to [0, 1].
        /// </summary>
        public static Color HSVToRGB(float H, float S, float V, bool hdr)
        {
            if (S == 0F)
                return new Color(V, V, V, 1F);
            if (V == 0F)
                return new Color(0F, 0F, 0F, 1F);

            float scaled = H * 6F;
            int sector = (int)Math.Floor(scaled);
            float fraction = scaled - sector;
            float low = V * (1F - S);
            float falling = V * (1F - S * fraction);
            float rising = V * (1F - S * (1F - fraction));

            Color c = new Color(1F, 1F, 1F, 1F);
            switch (sector)
            {
                case -1: c.r = V; c.g = low; c.b = falling; break;
                case 0: c.r = V; c.g = rising; c.b = low; break;
                case 1: c.r = falling; c.g = V; c.b = low; break;
                case 2: c.r = low; c.g = V; c.b = rising; break;
                case 3: c.r = low; c.g = falling; c.b = V; break;
                case 4: c.r = rising; c.g = low; c.b = V; break;
                case 5: c.r = V; c.g = low; c.b = falling; break;
                case 6: c.r = V; c.g = rising; c.b = low; break;
            }

            if (!hdr)
            {
                c.r = Mathf.Clamp01(c.r);
                c.g = Mathf.Clamp01(c.g);
                c.b = Mathf.Clamp01(c.b);
            }
            return c;
        }
    }
}
