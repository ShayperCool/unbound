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
    /// vectors) take the previous update's strength and direction with the phases after this
    /// update's step. A calm keeps the state: its frames draw the last windy update's constants, and a
    /// history at strength 0 (the strength is reset with the timers). Gusts take no part (the measured release never
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

        /// <summary>The phases after the latest step, and before it (the ones the current
        /// constants draw).</summary>
        readonly float[] m_Phase = new float[Phases], m_DrawnPhase = new float[Phases];

        /// <summary>The 30 constants as boxed Vector4 values, updated in place each frame.</summary>
        internal readonly object[] Values = NewValues(ConstantCount);

        /// <summary>Advances the wind to <paramref name="time"/> under the scene's wind (a unit
        /// direction, or zero, and its strength) and updates <see cref="Values"/>.</summary>
        internal void Update(float time, Vector3 direction, float strength, SpeedTreeWindConfig8 config)
        {
            if (!InTransition && Clamp01(m_Strength) <= 0f && strength == 0f)
            {
                // Calm: the timers and the strengths start over, as a SpeedTree 9 wind's do (the
                // history is drawn at strength 0, tests/projects/speedtree8wind f1566); the
                // phases, the direction and the previous update's direction stay as they are.
                m_Time = 0f;
                m_PreviousTime = Unset;
                m_PreviousStrength = 0f;
                m_Strength = 0f;
            }
            else
            {
                SetDirection(time, direction, config.DirectionResponse);
                SetStrength(time, strength, config.StrengthResponse);
                Advance(time);
                float s = Clamp01(m_Strength);
                Curve(CurveSamples, s, out int i0, out int i1, out float t);
                float dt = m_Time - m_PreviousTime;
                for (int k = 0; k < Phases; k++)
                {
                    m_DrawnPhase[k] = m_Phase[k];
                    m_Phase[k] += Sample(config.Oscillation[k], i0, i1, t) * dt;
                }
            }
            // The current constants: this update's strength and direction, the phases before its
            // step; the history: the previous update's strength and direction, the phases after it.
            Write(0, config, Clamp01(m_Strength), m_Direction, m_DrawnPhase);
            Write(HistoryOffset, config, m_PreviousStrength, m_PreviousDirection, m_Phase);
        }

        void Write(int at, SpeedTreeWindConfig8 c, float s, Vector3 d, float[] a)
        {
            Curve(CurveSamples, s, out int i0, out int i1, out float t);
            float C(float[] curve) => Sample(curve, i0, i1, t);
            var b1 = c.BranchLevels[0];
            var b2 = c.BranchLevels[1];
            var l1 = c.LeafGroups[0];
            var l2 = c.LeafGroups[1];
            Set(at + 0, new Vector4(d.x, d.y, d.z, s));
            Set(at + 1, new Vector4(a[0], C(c.GlobalDistance), c.GlobalHeight != 0f ? 1f / c.GlobalHeight : 1f, c.GlobalHeightExponent));
            Set(at + 2, new Vector4(a[1], C(b1.Distance), a[2], C(b2.Distance)));
            Set(at + 3, new Vector4(b1.Twitch, b1.TwitchFreqScale, b2.Twitch, b2.TwitchFreqScale));
            Set(at + 4, new Vector4(C(b1.Whip), C(b2.Whip), 0f, 0f));
            Set(at + 5, Anchor(c, d));
            // Unmeasured: the sample sets neither option.
            float adherence1 = C(b1.DirectionAdherence), adherence2 = C(b2.DirectionAdherence);
            if (c.Options[BranchDirectional1]) adherence1 = c.MaxBranchLevel1Length * adherence1;
            if (c.Options[BranchDirectional2]) adherence2 = c.MaxBranchLevel1Length * adherence2;
            Set(at + 6, new Vector4(C(c.GlobalDirectionAdherence), adherence1, adherence2, 0f));
            Set(at + 7, new Vector4(b1.Turbulence, b2.Turbulence, 0f, 0f));
            Set(at + 8, new Vector4(a[3], C(l1.RippleDistance), l1.LeewardScalar, 0f));
            Set(at + 9, new Vector4(a[4], C(l1.TumbleFlip), C(l1.TumbleTwist), C(l1.TumbleDirectionAdherence)));
            Set(at + 10, new Vector4(C(l1.TwitchThrow), Twitch(C(c.Oscillation[5]), l1.TwitchSharpness), a[5], 0f));
            Set(at + 11, new Vector4(a[6], C(l2.RippleDistance), l2.LeewardScalar, 0f));
            Set(at + 12, new Vector4(a[7], C(l2.TumbleFlip), C(l2.TumbleTwist), C(l2.TumbleDirectionAdherence)));
            Set(at + 13, new Vector4(C(l2.TwitchThrow), Twitch(C(c.Oscillation[8]), l2.TwitchSharpness), a[8], 0f));
            Set(at + 14, new Vector4(a[9], C(c.FrondRippleDistance), c.FrondRippleTile, c.FrondRippleLightingScalar));
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

        void Set(int index, Vector4 value) => Unsafe.Unbox<Vector4>(Values[index]) = value;
    }
}
