using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unbound.Runtime
{
    /// <summary>
    /// Unity's keyframe reduction of a float curve keyed on frames of <c>step</c> seconds, as the
    /// Unity 6000.0.71f1 Editor reduces a Humanoid model clip's converted curves
    /// (tests/projects/modelclips HumanReduced, HumanOptimal; <see cref="Humanoid.HumanClipReduction"/>)
    /// and the curves GameObjectRecorder.SaveToClip saves (tests/projects/animrecorder):
    /// <list type="bullet">
    /// <item>a curve that the constant at its first key's value holds (the segment from the first
    /// key to the last key's time, both with the first value and zero slopes; no span limit) becomes
    /// those two keys;</item>
    /// <item>otherwise, walking from the first key: a key whose out-slope and the next key's in-slope
    /// are both infinite (a step) is dropped when its value is within the error of the next one's,
    /// else it and the next key stay; any other key is dropped while the segment from the last kept
    /// key to the next one, with their own slopes, holds the curve (<see cref="Holds"/>) and spans
    /// fewer than <see cref="MaxReducedFrames"/> frames. The first and last keys stay; kept keys keep
    /// everything they had.</item>
    /// </list>
    /// A value holds when it lies within the error (a fraction) of the curve's, both values near zero
    /// aside (<see cref="Within"/>).
    /// </summary>
    internal static class KeyframeReduction
    {
        /// <summary>A reduced segment spans fewer frames than this (whole-curve reduction aside).</summary>
        internal const float MaxReducedFrames = 50f;
        /// <summary>Below this (times the error) a value counts as zero.</summary>
        internal const float ZeroTolerance = 1e-5f;

        /// <summary>The curve under reduction and the segment between two of its keys.</summary>
        internal interface ISource
        {
            /// <summary>The curve's value at <paramref name="time"/>.</summary>
            float Evaluate(float time);

            /// <summary>The segment from <paramref name="from"/> to <paramref name="to"/> at
            /// <paramref name="time"/>.</summary>
            float Interpolate(in Keyframe from, in Keyframe to, float time);
        }

        /// <summary>Whether a reduced value is close enough to the curve's: within
        /// <paramref name="error"/> (a fraction) of it, values near zero aside.</summary>
        internal static bool Within(float curve, float reduced, float error)
        {
            float zero = ZeroTolerance * error;
            return (MathF.Abs(curve) <= zero && MathF.Abs(reduced) <= zero) || MathF.Abs(curve - reduced) <= MathF.Abs(curve) * error;
        }

        /// <summary>Fills <paramref name="kept"/> with the indices of the keys that stay; true when
        /// the constant at the first key's value holds the curve (its two keys then take that value
        /// and zero slopes). Fewer than three keys all stay.</summary>
        internal static bool Reduce<T>(T source, IReadOnlyList<Keyframe> keys, float step, float error, List<int> kept)
            where T : ISource
        {
            kept.Clear();
            int n = keys.Count;
            if (n < 3 || !(step > 0f))
            {
                for (int i = 0; i < n; i++) kept.Add(i);
                return false;
            }
            var first = Flat(keys[0], keys[0].value);
            var last = Flat(keys[n - 1], keys[0].value);
            kept.Add(0);
            if (Holds(source, keys, first, last, 0, n - 1, false, step, error))
            {
                kept.Add(n - 1);
                return true;
            }
            int from = 0;
            for (int i = 1; i < n - 1;)
            {
                Keyframe key = keys[i], next = keys[i + 1];
                if (!float.IsFinite(key.outTangent) && !float.IsFinite(next.inTangent))
                {
                    if (Within(key.value, next.value, error))
                    {
                        i++;
                        continue;
                    }
                    kept.Add(i);
                    kept.Add(i + 1);
                    from = i + 1;
                    i += 2;
                    continue;
                }
                if (!Holds(source, keys, keys[from], next, from + 1, i + 1, true, step, error))
                {
                    kept.Add(i);
                    from = i;
                }
                i++;
            }
            if (from != n - 1) kept.Add(n - 1);
            return false;
        }

        /// <summary>A key with another value and flat slopes, as the whole-curve check takes the
        /// ends.</summary>
        static Keyframe Flat(Keyframe key, float value)
        {
            key.value = value;
            key.inTangent = 0f;
            key.outTangent = 0f;
            return key;
        }

        /// <summary>Whether the segment from <paramref name="a"/> to <paramref name="b"/> holds the
        /// curve: at every frame from its start, at the times of the keys
        /// <paramref name="firstBetween"/> to <paramref name="last"/> (excluded) and halfway between
        /// consecutive ones, and halfway to key <paramref name="last"/>; a
        /// <paramref name="limited"/> segment also spans fewer than <see cref="MaxReducedFrames"/>
        /// frames.</summary>
        static bool Holds<T>(T source, IReadOnlyList<Keyframe> keys, in Keyframe a, in Keyframe b, int firstBetween, int last,
            bool limited, float step, float error) where T : ISource
        {
            float span = b.time - a.time;
            int frames = (int)(span / step);
            for (int i = 0; i <= frames; i++)
                if (!Check(source, a, b, (float)i * step + a.time, error)) return false;
            float previous = a.time;
            for (int k = firstBetween; k < last; k++)
            {
                float time = keys[k].time;
                if (!Check(source, a, b, time, error) || !Check(source, a, b, (previous + time) * 0.5f, error)) return false;
                previous = time;
            }
            if (!Check(source, a, b, (previous + keys[last].time) * 0.5f, error)) return false;
            return !limited || span < step * MaxReducedFrames;
        }

        static bool Check<T>(T source, in Keyframe a, in Keyframe b, float time, float error) where T : ISource =>
            Within(source.Evaluate(time), source.Interpolate(a, b, time), error);
    }
}
