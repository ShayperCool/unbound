using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unbound.Runtime.Humanoid
{
    /// <summary>
    /// The keyframe reduction of a Humanoid model clip's curves under a reducing animation
    /// compression (Keyframe Reduction, Optimal). Matches the Unity 6000.0.71f1 Editor,
    /// tests/projects/modelclips HumanReduced and HumanOptimal: every curve of the clip (body
    /// position and rotation components, goals, muscles) is reduced on its own as a float curve
    /// with the importer's Position Error; the Rotation and Scale Errors play no part. The rule
    /// is the model importer's float reduction (crates/unbound-assets/src/model/curves.rs
    /// `reduce` with `float_within`), here on the converted curves through
    /// <see cref="KeyframeReduction"/>: a curve the constant at its first value holds becomes two
    /// keys with that value and zero slopes (the Editor's two-key curves have equal ends);
    /// otherwise a key is dropped while the Hermite segment from the last kept key to the next one
    /// holds the curve; kept keys keep their slopes.
    /// Unmeasured: a humanoid oversampling above one (the conversion samples once a frame),
    /// clips of fifty frames and more (the probe's have 21; the segment limit is the generic
    /// reduction's, tests/projects/modelaxis) and other frame rates than 30.
    /// </summary>
    internal static class HumanClipReduction
    {
        /// <summary>The cubic Hermite segment between two keys at <paramref name="time"/>.</summary>
        static float Hermite(float t0, float v0, float slope0, float t1, float v1, float slope1, float time)
        {
            float dt = t1 - t0;
            float s = (time - t0) / dt;
            float s2 = s * s, s3 = s * s * s;
            float h00 = 2f * s3 - 3f * s2 + 1f;
            float h10 = s3 - 2f * s2 + s;
            float h01 = -2f * s3 + 3f * s2;
            float h11 = s3 - s2;
            return h00 * v0 + h10 * slope0 * dt + h01 * v1 + h11 * slope1 * dt;
        }

        /// <summary>The every-frame curve, clamped to its ends, and its Hermite segments.</summary>
        readonly struct Curve : KeyframeReduction.ISource
        {
            readonly float[] m_Times, m_Values, m_Slopes;

            internal Curve(float[] times, float[] values, float[] slopes)
            {
                m_Times = times;
                m_Values = values;
                m_Slopes = slopes;
            }

            public float Evaluate(float time)
            {
                int n = m_Values.Length;
                if (time <= m_Times[0]) return m_Values[0];
                if (time >= m_Times[n - 1]) return m_Values[n - 1];
                int low = 0, high = n;
                while (low < high)
                {
                    int middle = (low + high) / 2;
                    if (m_Times[middle] <= time) low = middle + 1;
                    else high = middle;
                }
                int i = Math.Max(low, 1) - 1;
                return Hermite(m_Times[i], m_Values[i], m_Slopes[i], m_Times[i + 1], m_Values[i + 1], m_Slopes[i + 1], time);
            }

            public float Interpolate(in Keyframe from, in Keyframe to, float time) =>
                Hermite(from.time, from.value, from.outTangent, to.time, to.value, to.inTangent, time);
        }

        /// <summary>The keys of a converted curve keyed at <paramref name="times"/> (one key a
        /// frame of <paramref name="step"/> seconds) with one slope a key: every key without a
        /// reducing compression (<paramref name="kept"/> null), else the keys
        /// <see cref="Reduce"/> keeps with <paramref name="error"/> (<paramref name="kept"/> is
        /// the scratch list of their indices); a flat curve's two keys both take its first value
        /// with zero slopes. Unweighted keys with the default 1/3 weights, as imported muscle keys
        /// report them (tests/projects/humanfirstpose); they evaluate as plain Hermite
        /// segments.</summary>
        internal static Keyframe[] Keys(float[] times, float[] values, float[] slopes, float step, float error, List<int> kept)
        {
            bool flat = kept != null && Reduce(times, values, slopes, step, error, kept);
            var keys = new Keyframe[kept?.Count ?? values.Length];
            for (int k = 0; k < keys.Length; k++)
            {
                int source = kept != null ? kept[k] : k;
                float slope = flat ? 0f : slopes[source];
                float value = flat ? values[0] : values[source];
                keys[k] = Key(times[source], value, slope);
            }
            return keys;
        }

        static Keyframe Key(float time, float value, float slope)
        {
            var key = new Keyframe(time, value, slope, slope, 1f / 3f, 1f / 3f);
            key.weightedMode = WeightedMode.None;
            return key;
        }

        /// <summary>Reduces a curve keyed at <paramref name="times"/> (one key a frame of
        /// <paramref name="step"/> seconds) with one slope a key (<see cref="KeyframeReduction"/>):
        /// fills <paramref name="kept"/> with the indices of the keys that stay. True when the
        /// constant at the first key's value holds the curve: its two keys then both take the
        /// first key's value with zero slopes.</summary>
        internal static bool Reduce(float[] times, float[] values, float[] slopes, float step, float error, List<int> kept)
        {
            var keys = new Keyframe[values.Length];
            for (int i = 0; i < keys.Length; i++) keys[i] = Key(times[i], values[i], slopes[i]);
            return KeyframeReduction.Reduce(new Curve(times, values, slopes), keys, step, error, kept);
        }
    }
}
