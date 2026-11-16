using System;
using UnityEngine;
using UnityEngine.VFX;

namespace Unbound.Runtime
{
    /// <summary>
    /// The texture an effect's programs sample curves and gradients from (`bakedTexture`): 128
    /// texels wide, a row for every four curves (one channel each) and then a row for each
    /// gradient, in half floats. A graph's "bake" expressions give each curve and gradient a
    /// place in it and evaluate to what the generated code needs to sample it there
    /// (the package's `SampleCurve` and `SampleGradient`): for a curve its time range packed as
    /// two half floats, a scale, the row and the channel with how it continues outside its
    /// keys; for a gradient its row, whether its colours are fixed between keys, and a scale.
    /// Values larger than a half float holds are stored scaled down, the scale beside them.
    /// The places are the compiled data's (<see cref="VisualEffectCompiledData.BakePlaces"/>). The
    /// texels and the values match the Unity 6000.0.71f1 Editor (tests/projects/vfx,
    /// `draw-over life` and `sim/expressions*/inputs/*`).
    /// </summary>
    internal sealed class VFXBakedTexture
    {
        /// <summary>The samples of a curve or gradient: the texture's width.</summary>
        internal const int Width = 128;
        /// <summary>The largest value a half float holds: what a row's values are scaled to
        /// when one of them is larger.</summary>
        const float LargestHalf = 65504f;
        /// <summary>The curves of a row: one per channel.</summary>
        const int CurvesPerRow = 4;
        /// <summary>In a curve's flags, beside its channel: it does not loop after its last
        /// key, and not before its first (the generated code clamps there instead).</summary>
        const uint ClampAfter = 4, ClampBefore = 8;

        readonly int m_CurveRows, m_Height;
        readonly Color[] m_Pixels;
        /// <summary>The curve and gradient objects last baked, per place: baked again only
        /// when the value is another object (a value set from script is a new copy).</summary>
        readonly object[] m_Curves, m_Gradients;
        readonly bool[] m_CurveBaked, m_GradientBaked;
        /// <summary>What a place's stored values are multiplied by to give the real ones.</summary>
        readonly float[] m_CurveScales, m_GradientScales;
        readonly float[] m_Samples = new float[Width];
        Texture2D m_Texture;
        bool m_Dirty;

        VFXBakedTexture(int curves, int gradients)
        {
            m_CurveRows = (curves + CurvesPerRow - 1) / CurvesPerRow;
            m_Height = m_CurveRows + gradients;
            m_Pixels = new Color[Width * m_Height];
            m_Curves = new object[curves];
            m_Gradients = new object[gradients];
            m_CurveBaked = new bool[curves];
            m_GradientBaked = new bool[gradients];
            m_CurveScales = new float[curves];
            m_GradientScales = new float[gradients];
        }

        /// <summary>The texture of a compiled graph's baked curves and gradients, null when
        /// it bakes none.</summary>
        internal static VFXBakedTexture For(VisualEffectCompiledData data) =>
            data.BakedCurves + data.BakedGradients == 0 ? null : new VFXBakedTexture(data.BakedCurves, data.BakedGradients);

        /// <summary>The texture, once something was baked and <see cref="Apply"/> ran.</summary>
        internal Texture2D Texture => m_Texture;

        float RowCoordinate(int row) => (row + 0.5f) / m_Height;

        /// <summary>Bakes a curve at its place when it is another object than the one there,
        /// and writes the four words the generated code samples it with.</summary>
        internal void BakeCurve(int place, AnimationCurve curve, uint[] words, int at)
        {
            if ((uint)place >= (uint)m_Curves.Length) return;
            int count = curve?.length ?? 0;
            // The times the samples span: from the first key to the last; one second from
            // zero without keys.
            float start = count != 0 ? curve[0].time : 0f;
            float range = count != 0 ? curve[count - 1].time - start : 1f;
            bool loops = count != 0 && curve.preWrapMode == WrapMode.Loop && curve.postWrapMode == WrapMode.Loop;
            bool clampBefore = curve == null || curve.preWrapMode != WrapMode.Loop, clampAfter = curve == null || curve.postWrapMode != WrapMode.Loop;
            int row = place / CurvesPerRow, channel = place % CurvesPerRow;
            if (!m_CurveBaked[place] || !ReferenceEquals(m_Curves[place], curve))
            {
                m_CurveBaked[place] = true;
                m_Curves[place] = curve;
                float largest = 0f;
                for (int i = 0; i < Width; i++)
                {
                    // A curve that loops both ways is sampled at texel centres (its end wraps
                    // onto its start, and the generated code samples it without the half
                    // texel it adds where it clamps); any other from its first key to its
                    // last. A curve of one key has no range: every sample is its value, and
                    // the words below are not numbers (the generated code then samples at
                    // whatever its clamp of that gives; the texels are all the same).
                    float time = loops ? (i + 0.5f) * range * (1f / Width) : i * range / (Width - 1);
                    float value = curve != null ? curve.EvaluateCubic(time + start) : 0f;
                    m_Samples[i] = value;
                    largest = Math.Max(largest, Math.Abs(value));
                }
                float fit = largest > LargestHalf ? LargestHalf / largest : 1f;
                for (int i = 0; i < Width; i++) m_Pixels[row * Width + i][channel] = largest > LargestHalf ? m_Samples[i] * fit : m_Samples[i];
                m_CurveScales[place] = largest > LargestHalf ? largest / LargestHalf : 1f;
                m_Dirty = true;
            }
            words[at] = (uint)Mathf.FloatToHalf(1f / range) | (uint)Mathf.FloatToHalf(-start / range) << 16;
            words[at + 1] = BitConverter.SingleToUInt32Bits(m_CurveScales[place]);
            words[at + 2] = BitConverter.SingleToUInt32Bits(RowCoordinate(row));
            words[at + 3] = (uint)channel | (clampAfter ? ClampAfter : 0) | (clampBefore ? ClampBefore : 0);
        }

        /// <summary>Bakes a gradient at its place when it is another object than the one
        /// there, and writes the three words the generated code samples it with.</summary>
        internal void BakeGradient(int place, Gradient gradient, uint[] words, int at)
        {
            if ((uint)place >= (uint)m_Gradients.Length) return;
            int row = m_CurveRows + place;
            if (!m_GradientBaked[place] || !ReferenceEquals(m_Gradients[place], gradient))
            {
                m_GradientBaked[place] = true;
                m_Gradients[place] = gradient;
                float largest = 0f;
                for (int i = 0; i < Width; i++)
                {
                    var color = gradient != null ? gradient.Evaluate(i / (float)(Width - 1)) : default;
                    m_Pixels[row * Width + i] = color;
                    largest = Math.Max(largest, Math.Max(color.r, Math.Max(color.g, color.b)));
                }
                m_GradientScales[place] = 1f;
                if (largest > LargestHalf)
                {
                    // The colours scale, the alpha stays.
                    float fit = LargestHalf / largest;
                    for (int i = 0; i < Width; i++)
                    {
                        ref var pixel = ref m_Pixels[row * Width + i];
                        pixel.r *= fit;
                        pixel.g *= fit;
                        pixel.b *= fit;
                    }
                    m_GradientScales[place] = largest / LargestHalf;
                }
                m_Dirty = true;
            }
            words[at] = BitConverter.SingleToUInt32Bits(RowCoordinate(row));
            words[at + 1] = BitConverter.SingleToUInt32Bits(gradient != null && gradient.mode == GradientMode.Fixed ? 1f : 0f);
            words[at + 2] = BitConverter.SingleToUInt32Bits(m_GradientScales[place]);
        }

        /// <summary>Uploads what was baked since the last time.</summary>
        internal void Apply()
        {
            if (!m_Dirty) return;
            m_Dirty = false;
            if (m_Texture == null)
            {
                m_Texture = new Texture2D(Width, m_Height, TextureFormat.RGBAHalf, false, true)
                {
                    name = "VFX baked curves and gradients", hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Repeat,
                };
            }
            m_Texture.SetPixels(m_Pixels);
            m_Texture.Apply(false);
        }

        internal void Release()
        {
            if (m_Texture != null) UnityEngine.Object.Destroy(m_Texture);
            m_Texture = null;
        }
    }
}
