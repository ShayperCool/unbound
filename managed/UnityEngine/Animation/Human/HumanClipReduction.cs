using System;
using System.Collections.Generic;

namespace Unbound.Runtime.Humanoid
{
    /// <summary>
    /// The keyframe reduction of a Humanoid model clip's curves under a reducing animation
    /// compression (Keyframe Reduction, Optimal). Matches the Unity 6000.0.71f1 Editor,
    /// tests/projects/modelclips HumanReduced and HumanOptimal: every curve of the clip (body
    /// position and rotation components, goals, muscles) is reduced on its own as a float curve
    /// with the importer's Position Error; the Rotation and Scale Errors play no part. The rule
    /// is the model importer's float reduction (crates/unbound-assets/src/model/curves.rs
    /// `reduce` with `float_within`), here on the converted curves:
    /// <list type="bullet">
    /// <item>a curve that the constant at its first key's value holds (checked as a segment,
    /// below, from the first key to the last key's time with the first value at both ends, zero
    /// slopes and no span limit) becomes two keys at the first and last times, both with the
    /// first key's value and zero slopes (the Editor's two-key curves have equal ends);</item>
    /// <item>otherwise, walking from the first key, a key is dropped while the Hermite segment
    /// from the last kept key to the next one, with their own slopes, stays within the error of
    /// the curve at every frame from its start, at every dropped key and halfway between
    /// consecutive ones, and spans fewer than <see cref="MaxReducedFrames"/> frames; kept keys
    /// keep their slopes.</item>
    /// </list>
    /// Unmeasured: a humanoid oversampling above one (the conversion samples once a frame),
    /// clips of fifty frames and more (the probe's have 21; the segment limit is the generic
    /// reduction's, tests/projects/modelaxis) and other frame rates than 30.
    /// </summary>
    internal static class HumanClipReduction
    {
        /// <summary>A reduced segment spans fewer frames than this.</summary>
        const float MaxReducedFrames = 50f;
        /// <summary>Below this (times the error) a value counts as zero.</summary>
        const float ZeroTolerance = 1e-5f;

        /// <summary>Whether a reduced value is close enough to the curve's: within
        /// <paramref name="error"/> (a fraction) of it, values near zero aside.</summary>
        static bool Within(float curve, float reduced, float error)
        {
            float zero = ZeroTolerance * error;
            return (MathF.Abs(curve) <= zero && MathF.Abs(reduced) <= zero) || MathF.Abs(curve - reduced) <= MathF.Abs(curve) * error;
        }

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

        /// <summary>The every-frame curve at <paramref name="time"/>, clamped to its ends.</summary>
        static float Evaluate(float[] times, float[] values, float[] slopes, float time)
        {
            int n = values.Length;
            if (time <= times[0]) return values[0];
            if (time >= times[n - 1]) return values[n - 1];
            int low = 0, high = n;
            while (low < high)
            {
                int middle = (low + high) / 2;
                if (times[middle] <= time) low = middle + 1;
                else high = middle;
            }
            int i = Math.Max(low, 1) - 1;
            return Hermite(times[i], values[i], slopes[i], times[i + 1], values[i + 1], slopes[i + 1], time);
        }

        /// <summary>Whether the segment from key <paramref name="a"/> to key <paramref name="b"/>,
        /// leaving with value <paramref name="va"/> and slope <paramref name="sa"/> and arriving
        /// with <paramref name="vb"/> and <paramref name="sb"/>, holds the curve: at every frame
        /// from its start, at the keys between them and halfway between consecutive ones; a
        /// <paramref name="limited"/> segment also spans fewer than
        /// <see cref="MaxReducedFrames"/> frames.</summary>
        static bool Holds(float[] times, float[] values, float[] slopes, int a, int b, float va, float sa, float vb, float sb,
            bool limited, float step, float error)
        {
            float span = times[b] - times[a];
            if (limited && !(span < step * MaxReducedFrames)) return false;
            bool Check(float time) => Within(Evaluate(times, values, slopes, time),
                Hermite(times[a], va, sa, times[b], vb, sb, time), error);
            int frames = (int)(span / step);
            for (int i = 0; i <= frames; i++)
                if (!Check((float)i * step + times[a])) return false;
            float previous = times[a];
            for (int k = a + 1; k < b; k++)
            {
                float time = times[k];
                if (!Check(time) || !Check((previous + time) * 0.5f)) return false;
                previous = time;
            }
            return Check((previous + times[b]) * 0.5f);
        }

        /// <summary>Reduces a curve keyed at <paramref name="times"/> (one key a frame of
        /// <paramref name="step"/> seconds) with one slope a key: fills <paramref name="kept"/>
        /// with the indices of the keys that stay. True when the constant at the first key's value
        /// holds the curve: its two keys then both take the first key's value with zero
        /// slopes.</summary>
        internal static bool Reduce(float[] times, float[] values, float[] slopes, float step, float error, List<int> kept)
        {
            kept.Clear();
            int n = values.Length;
            if (n < 3 || !(step > 0f))
            {
                for (int i = 0; i < n; i++) kept.Add(i);
                return false;
            }
            bool constant = Holds(times, values, slopes, 0, n - 1, values[0], 0f, values[0], 0f, false, step, error);
            kept.Add(0);
            if (!constant)
            {
                int last = 0;
                for (int i = 1; i < n - 1; i++)
                {
                    if (Holds(times, values, slopes, last, i + 1, values[last], slopes[last], values[i + 1], slopes[i + 1], true, step, error))
                        continue;
                    kept.Add(i);
                    last = i;
                }
            }
            kept.Add(n - 1);
            return constant;
        }
    }
}
