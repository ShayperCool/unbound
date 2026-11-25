using System;
using System.Runtime.CompilerServices;

namespace UnityEngine
{
    /// <summary>
    /// The wind one SpeedTree 8 wind asset gives its trees (Unity 6000.0.71f1,
    /// tests/projects/speedtree8wind). Its direction and strength ease toward the scene's wind as a
    /// SpeedTree 9 wind's do (<see cref="SpeedTreeWindMotion"/>), over the asset's responses; its
    /// strength picks a point on the asset's 10-sample curves. Ten phases (the global wind, the two
    /// branch levels, the leaves' ripples, tumbles and twitches, the fronds' ripple) each advance by
    /// its oscillation curve at the strength times the time since the previous update. The shader
    /// constants hold, for each part, its phase before this update and its curves' values at the
    /// strength, beside the asset's settings; the history constants (for per-object motion
    /// vectors) are those of the previous update with the phases after this update's step. Gusts take no part (the measured release never
    /// turns them on, as for SpeedTree 9).
    /// </summary>
    internal sealed class SpeedTree8WindState : SpeedTreeWindMotion
    {
        internal const int ConstantCount = 30, HistoryOffset = 15;
        const int CurveSamples = 10, Phases = 10;
        /// <summary>The smallest curve value a leaf twitch's frequency is taken from (float epsilon).</summary>
        const float LeastOscillation = 1.1920929e-07f;
        /// <summary>How far a direction's components must be from zero for it to be normalized.</summary>
        const float LeastDirection = 1e-6f;
        /// <summary>The leaf twitch's sharpness scale.</summary>
        const float TwitchScale = 10f;
        // The options of the asset's file that turn the branch levels' adherence directional.
        const int BranchDirectional1 = 3, BranchDirectional2 = 9;

        readonly float[] m_Phase = new float[Phases];

        /// <summary>The 30 constants as boxed Vector4 values, updated in place each frame.</summary>
        internal readonly object[] Values = NewValues(ConstantCount);

        /// <summary>Advances the wind to <paramref name="time"/> under the scene's wind (a unit
        /// direction, or zero, and its strength) and updates <see cref="Values"/>.</summary>
        internal void Update(float time, Vector3 direction, float strength, SpeedTreeWindConfig8 config)
        {
            // The previous update's constants are the history.
            for (int i = 0; i < HistoryOffset; i++) Set(HistoryOffset + i, Get(i));
            if (!InTransition && Clamp01(m_Strength) <= 0f && strength == 0f)
            {
                // Calm: the timers start over, as a SpeedTree 9 wind's do.
                m_Time = 0f;
                m_PreviousTime = Unset;
                m_PreviousStrength = 0f;
                m_Strength = 0f;
                Write(config, Clamp01(m_Strength));
                WriteHistoryPhases();
                return;
            }
            SetDirection(time, direction, config.DirectionResponse);
            SetStrength(time, strength, config.StrengthResponse);
            Advance(time);
            float s = Clamp01(m_Strength);
            // The constants take the phases before this update's step.
            Write(config, s);
            Curve(CurveSamples, s, out int i0, out int i1, out float t);
            float dt = m_Time - m_PreviousTime;
            for (int k = 0; k < Phases; k++) m_Phase[k] += Sample(config.Oscillation[k], i0, i1, t) * dt;
            WriteHistoryPhases();
        }

        /// <summary>The history's phases are the ones after this update's step (the rest of it is
        /// the previous update's constants).</summary>
        void WriteHistoryPhases()
        {
            const int h = HistoryOffset;
            Phase(h + 1, 0, m_Phase[0]);
            Phase(h + 2, 0, m_Phase[1]);
            Phase(h + 2, 2, m_Phase[2]);
            Phase(h + 8, 0, m_Phase[3]);
            Phase(h + 9, 0, m_Phase[4]);
            Phase(h + 10, 2, m_Phase[5]);
            Phase(h + 11, 0, m_Phase[6]);
            Phase(h + 12, 0, m_Phase[7]);
            Phase(h + 13, 2, m_Phase[8]);
            Phase(h + 14, 0, m_Phase[9]);
        }

        void Phase(int index, int component, float phase) => Unsafe.Unbox<Vector4>(Values[index])[component] = phase;

        void Write(SpeedTreeWindConfig8 c, float s)
        {
            Curve(CurveSamples, s, out int i0, out int i1, out float t);
            float C(float[] curve) => Sample(curve, i0, i1, t);
            var d = m_Direction;
            var b1 = c.BranchLevels[0];
            var b2 = c.BranchLevels[1];
            var l1 = c.LeafGroups[0];
            var l2 = c.LeafGroups[1];
            Set(0, new Vector4(d.x, d.y, d.z, s));
            Set(1, new Vector4(m_Phase[0], C(c.GlobalDistance), c.GlobalHeight != 0f ? 1f / c.GlobalHeight : 1f, c.GlobalHeightExponent));
            Set(2, new Vector4(m_Phase[1], C(b1.Distance), m_Phase[2], C(b2.Distance)));
            Set(3, new Vector4(b1.Twitch, b1.TwitchFreqScale, b2.Twitch, b2.TwitchFreqScale));
            Set(4, new Vector4(C(b1.Whip), C(b2.Whip), 0f, 0f));
            Set(5, Anchor(c, d));
            float adherence1 = C(b1.DirectionAdherence), adherence2 = C(b2.DirectionAdherence);
            if (c.Options[BranchDirectional1]) adherence1 = c.MaxBranchLevel1Length * adherence1;
            if (c.Options[BranchDirectional2]) adherence2 = c.MaxBranchLevel1Length * adherence2;
            Set(6, new Vector4(C(c.GlobalDirectionAdherence), adherence1, adherence2, 0f));
            Set(7, new Vector4(b1.Turbulence, b2.Turbulence, 0f, 0f));
            Set(8, new Vector4(m_Phase[3], C(l1.RippleDistance), l1.LeewardScalar, 0f));
            Set(9, new Vector4(m_Phase[4], C(l1.TumbleFlip), C(l1.TumbleTwist), C(l1.TumbleDirectionAdherence)));
            Set(10, new Vector4(C(l1.TwitchThrow), Twitch(C(c.Oscillation[5]), l1.TwitchSharpness), m_Phase[5], 0f));
            Set(11, new Vector4(m_Phase[6], C(l2.RippleDistance), l2.LeewardScalar, 0f));
            Set(12, new Vector4(m_Phase[7], C(l2.TumbleFlip), C(l2.TumbleTwist), C(l2.TumbleDirectionAdherence)));
            Set(13, new Vector4(C(l2.TwitchThrow), Twitch(C(c.Oscillation[8]), l2.TwitchSharpness), m_Phase[8], 0f));
            Set(14, new Vector4(m_Phase[9], C(c.FrondRippleDistance), c.FrondRippleTile, c.FrondRippleLightingScalar));
        }

        /// <summary>A leaf twitch's sharpness over its oscillation (none where the oscillation is
        /// below float epsilon).</summary>
        static float Twitch(float oscillation, float sharpness) =>
            oscillation >= LeastOscillation ? 1f / oscillation * sharpness * TwitchScale : 0f;

        /// <summary>The branch anchor: the asset's anchor moved along the wind (raised by the
        /// anchor offset, normalized when it is not zero) by the first branch level's length times
        /// the anchor distance scale; its direction and its length.</summary>
        static Vector4 Anchor(SpeedTreeWindConfig8 c, Vector3 d)
        {
            float x = d.x + 0f, y = c.AnchorOffset + d.y, z = d.z + 0f;
            if (Math.Abs(x) > LeastDirection || Math.Abs(y) > LeastDirection || Math.Abs(z) > LeastDirection)
            {
                float length = MathF.Sqrt((y * y + x * x) + z * z);
                x /= length;
                y /= length;
                z /= length;
            }
            float reach = c.MaxBranchLevel1Length * c.AnchorDistanceScale;
            var anchor = c.BranchWindAnchor;
            float ax = x * reach + anchor.x, ay = anchor.y + reach * y, az = anchor.z + reach * z;
            float l = MathF.Sqrt((ax * ax + ay * ay) + az * az);
            return l != 0f ? new Vector4(ax / l, ay / l, az / l, l) : Vector4.zero;
        }

        Vector4 Get(int index) => Unsafe.Unbox<Vector4>(Values[index]);

        void Set(int index, Vector4 value) => Unsafe.Unbox<Vector4>(Values[index]) = value;
    }
}
