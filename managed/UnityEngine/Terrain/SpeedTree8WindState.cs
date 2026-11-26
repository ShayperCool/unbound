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

        /// <summary>A local wind's return to its asset's wind, per phase: when it starts and ends
        /// (<see cref="SpeedTreeWindMotion.Unset"/>: none), the phase it starts from and the one it
        /// heads for while it is ahead of the asset's.</summary>
        readonly float[] m_ReturnStart = Filled(Unset), m_ReturnEnd = Filled(Unset), m_ReturnFrom = new float[Phases], m_ReturnTo = new float[Phases];

        /// <summary>How close a local wind's phases, the current and the previous ones, must be to
        /// its asset's wind's to count as the same.</summary>
        const float SamePhase = 5e-4f;
        /// <summary>A phase behind the asset's catches up this much faster than the asset's moves,
        /// and at least this fast (phase per second).</summary>
        const float CatchUp = 1.2f, LeastCatchUp = 1.1f;
        /// <summary>A phase ahead of the asset's meets it halfway.</summary>
        const float Halfway = 0.5f;
        /// <summary>A return that would take longer (in seconds) jumps instead.</summary>
        const float LongestReturn = 15f;

        static float[] Filled(float value)
        {
            var a = new float[Phases];
            for (int i = 0; i < a.Length; i++) a[i] = value;
            return a;
        }

        /// <summary>The 30 constants as boxed Vector4 values, updated in place each frame.</summary>
        internal readonly object[] Values = NewValues(ConstantCount);

        /// <summary>A SpeedTree 8 wind starts blowing along +x (its direction and the previous
        /// update's), where a SpeedTree 9 wind starts without one: a wind from the first frame
        /// turns from +x toward the scene's over the direction response, and a first update
        /// without wind takes it to rest at once (Unity 6000.0.71f1, tests/projects/speedtree8wind
        /// `first`).</summary>
        internal SpeedTree8WindState()
        {
            m_Direction = m_PreviousDirection = Vector3.right;
        }

        internal override object[] Constants => Values;

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
                StepPhases(config);
            }
            // The current constants: this update's strength and direction, the phases before its
            // step; the history: the previous update's strength and direction, the phases after it.
            Write(0, config, Clamp01(m_Strength), m_Direction, m_DrawnPhase);
            Write(HistoryOffset, config, m_PreviousStrength, m_PreviousDirection, m_Phase);
        }

        /// <summary>Each phase on by its oscillation curve at the strength times the time since the
        /// previous update (the previous phases kept as the drawn ones).</summary>
        void StepPhases(SpeedTreeWindConfig8 config)
        {
            Curve(CurveSamples, Clamp01(m_Strength), out int i0, out int i1, out float t);
            float dt = m_Time - m_PreviousTime;
            for (int k = 0; k < Phases; k++)
            {
                m_DrawnPhase[k] = m_Phase[k];
                m_Phase[k] += Sample(config.Oscillation[k], i0, i1, t) * dt;
            }
        }

        // A local wind's parts are its phases (Unity 6000.0.71f1, tests/projects/speedtree8wind
        // FieldNear).

        protected override void Responses(SpeedTreeWindAsset asset, out float direction, out float strength)
        {
            direction = asset.Config8.DirectionResponse;
            strength = asset.Config8.StrengthResponse;
        }

        protected override void Step(SpeedTreeWindAsset asset) => StepPhases(asset.Config8);

        protected override void CopyParts(SpeedTreeWindMotion from)
        {
            var other = (SpeedTree8WindState)from;
            Array.Copy(other.m_Phase, m_Phase, Phases);
            Array.Copy(other.m_DrawnPhase, m_DrawnPhase, Phases);
        }

        /// <summary>Whether every phase, the current and the previous one, is within
        /// <see cref="SamePhase"/> of the other wind's.</summary>
        protected override bool SameParts(SpeedTreeWindMotion other)
        {
            var o = (SpeedTree8WindState)other;
            for (int k = 0; k < Phases; k++)
                if (!(Math.Abs(m_Phase[k] - o.m_Phase[k]) <= SamePhase) || !(Math.Abs(m_DrawnPhase[k] - o.m_DrawnPhase[k]) <= SamePhase))
                    return false;
            return true;
        }

        /// <summary>
        /// Starts each phase's return (one not returning already) to the asset's wind's from
        /// <paramref name="now"/>: a phase behind the asset's catches up at its oscillation (at
        /// the asset's strength) times <see cref="CatchUp"/>, at least <see cref="LeastCatchUp"/>,
        /// over the gap; one ahead moves back to meet the asset's halfway, in the time the asset's
        /// takes to cover the other half. A return longer than <see cref="LongestReturn"/> jumps.
        /// </summary>
        protected override void StartReturn(SpeedTreeWindMotion asset, float now, SpeedTreeWindAsset owner, float strength)
        {
            var other = (SpeedTree8WindState)asset;
            var config = owner.Config8;
            Curve(CurveSamples, strength, out int i0, out int i1, out float t);
            for (int k = 0; k < Phases; k++)
            {
                float target = other.m_Phase[k], local = m_Phase[k];
                float speed = Sample(config.Oscillation[k], i0, i1, t);
                if (!(local > target))
                {
                    speed *= CatchUp;
                    speed = speed > LeastCatchUp ? speed : LeastCatchUp;
                }
                if (m_ReturnStart[k] != Unset) continue;
                float gap = Math.Abs(target - local), half = gap * Halfway;
                bool ahead = target < local;
                float span = (ahead ? half : gap) / speed;
                if (LongestReturn < span) span = 0f;
                m_ReturnStart[k] = now;
                m_ReturnEnd[k] = span + now;
                m_ReturnFrom[k] = local;
                m_ReturnTo[k] = ahead ? half + target : target;
            }
        }

        /// <summary>Moves each phase along its return at <paramref name="time"/> (the previous
        /// phase becomes the drawn one): one behind the asset's toward the asset's phase as it is
        /// now, one ahead toward the meeting point, behind or ahead as it is this frame; a return
        /// at its end is over. A return that would be too long ends at once, phase by phase
        /// (tests/projects/speedtree8wind profile zones: FieldLong's slowest phase).</summary>
        protected override void Return(float time, SpeedTreeWindMotion asset)
        {
            var other = (SpeedTree8WindState)asset;
            for (int k = 0; k < Phases; k++)
            {
                float local = m_Phase[k];
                m_DrawnPhase[k] = local;
                float start = m_ReturnStart[k];
                float f = (time - start) / (m_ReturnEnd[k] - start);
                f = f < 1f ? f : 1f;
                f = f > 0f ? f : 0f;
                float target = other.m_Phase[k];
                m_Phase[k] = !(local > target)
                    ? m_ReturnFrom[k] * (1f - f) + target * f
                    : m_ReturnTo[k] * f + m_ReturnFrom[k] * (1f - f);
                if (f == 1f) m_ReturnStart[k] = m_ReturnEnd[k] = Unset;
            }
        }

        protected override void WriteAll(SpeedTreeWindAsset asset)
        {
            Write(0, asset.Config8, Clamp01(m_Strength), m_Direction, m_DrawnPhase);
            Write(HistoryOffset, asset.Config8, m_PreviousStrength, m_PreviousDirection, m_Phase);
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
            // Both levels by the first level's length (tests/projects/speedtree8wind FieldDirectional,
            // a staged copy with both options set and a second-level adherence curve).
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
