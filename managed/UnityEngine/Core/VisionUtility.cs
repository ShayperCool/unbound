using System;

namespace UnityEngine.Accessibility
{
    /// <summary>
    /// A palette of colors distinguishable with color blindness, as the Unity 6000.0.71f1 Editor
    /// answers it (tests/projects/smallstubs palette.*): its 19 colors in the Editor's order; a
    /// request keeps those whose perceived luminance (Rec. 709 weights of the linear color, back
    /// to gamma space) lies in the range, fills the whole array cycling through them (default
    /// colors when none qualifies) and returns how many distinct colors it used. A null or empty
    /// array throws ArgumentNullException, as the Editor does.
    /// </summary>
    public static partial class VisionUtility
    {
        static readonly Color[] s_ColorBlindSafePalette =
        {
            new Color32(0, 0, 0, 255),
            new Color32(73, 0, 146, 255),
            new Color32(7, 71, 81, 255),
            new Color32(0, 146, 146, 255),
            new Color32(182, 109, 255, 255),
            new Color32(255, 109, 182, 255),
            new Color32(109, 182, 255, 255),
            new Color32(36, 255, 36, 255),
            new Color32(255, 182, 219, 255),
            new Color32(182, 219, 255, 255),
            new Color32(255, 255, 109, 255),
            new Color32(30, 92, 92, 255),
            new Color32(74, 154, 87, 255),
            new Color32(113, 66, 183, 255),
            new Color32(162, 66, 183, 255),
            new Color32(178, 92, 25, 255),
            new Color32(100, 100, 100, 255),
            new Color32(80, 203, 181, 255),
            new Color32(82, 205, 242, 255),
        };

        static float[] s_Luminance;

        internal static float ComputePerceivedLuminance(Color color)
        {
            color = color.linear;
            return Mathf.LinearToGammaSpace(0.2126f * color.r + 0.7152f * color.g + 0.0722f * color.b);
        }

        internal static void GetLuminanceValuesForPalette(Color[] palette, ref float[] outLuminanceValues)
        {
            Debug.Assert(palette != null && outLuminanceValues != null, "Passed in arrays can't be null.");
            Debug.Assert(palette.Length == outLuminanceValues.Length, "Passed in arrays need to be of the same length.");
            for (int i = 0; i < palette.Length; i++) outLuminanceValues[i] = ComputePerceivedLuminance(palette[i]);
        }

        public static int GetColorBlindSafePalette(Color[] palette, float minimumLuminance, float maximumLuminance)
        {
            if (palette == null || palette.Length == 0) throw new ArgumentNullException(nameof(palette));
            return Fill(palette.Length, minimumLuminance, maximumLuminance, (i, c) => palette[i] = c);
        }

        internal static int GetColorBlindSafePalette(Color32[] palette, float minimumLuminance, float maximumLuminance)
        {
            if (palette == null || palette.Length == 0) throw new ArgumentNullException(nameof(palette));
            return Fill(palette.Length, minimumLuminance, maximumLuminance, (i, c) => palette[i] = c);
        }

        static int Fill(int length, float minimumLuminance, float maximumLuminance, Action<int, Color> set)
        {
            if (s_Luminance == null)
            {
                var values = new float[s_ColorBlindSafePalette.Length];
                GetLuminanceValuesForPalette(s_ColorBlindSafePalette, ref values);
                s_Luminance = values;
            }
            var accepted = new Color[s_ColorBlindSafePalette.Length];
            int count = 0;
            for (int i = 0; i < s_ColorBlindSafePalette.Length; i++)
                if (s_Luminance[i] >= minimumLuminance && s_Luminance[i] <= maximumLuminance)
                    accepted[count++] = s_ColorBlindSafePalette[i];
            int unique = Math.Min(length, count);
            for (int i = 0; i < length; i++) set(i, unique > 0 ? accepted[i % unique] : default);
            return unique;
        }
    }
}
