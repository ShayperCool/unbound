using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unbound.Runtime;
using UnityEngine.Rendering;

namespace UnityEngine
{
    /// <summary>
    /// The wind one SpeedTree 9 wind asset gives its trees (Unity 6000.0.71f1,
    /// tests/projects/speedtree9wind). Each frame the wind eases toward the scene's directional wind:
    /// its direction over the asset's direction response, smoothed (half linear, half smoothstep)
    /// through the normalized middle of the old and the new direction; its strength over the
    /// strength response, smoothstepped. A larger change takes longer: the response time scales
    /// from half to all of the response with the change. The strength picks a point on the asset's
    /// 20-sample curves (bend, oscillation, turbulence, flexibility, speed); the speed moves each
    /// part's noise position against the wind. Gusts take no part (the measured release never
    /// turns them on). The shader constants of the SpeedTree 9 shaders hold the current values and,
    /// for motion vectors, the previous frame's. A tree in reach of a spherical zone gets a local
    /// wind of its own (<see cref="Begin"/>, <see cref="UpdateLocal"/>).
    /// </summary>
    internal sealed class SpeedTreeWindState
    {
        internal const int ConstantCount = 22, HistoryOffset = 11;
        const int CurveSamples = 20;
        /// <summary>A time not set yet.</summary>
        const float Unset = -1f;
        /// <summary>How close the direction and strength must be to their targets to count as
        /// arrived.</summary>
        const float Arrived = 1e-6f;
        /// <summary>How close a local wind's noise positions, and its wind vector, must be to its
        /// asset's wind to count as the same.</summary>
        const float Close = 1e-5f, CloseSquared = Close * Close;
        /// <summary>A local wind's noise positions converge with its asset's wind's at this many
        /// times the speed the noise drifts at.</summary>
        const float CatchUpSpeed = 4f;
        /// <summary>A return to the asset's wind that would take longer (in seconds) jumps instead.</summary>
        const float LongestCatchUp = 15f;
        /// <summary>Distances below this (between noise positions) take no catching up.</summary>
        const float NoDistance = 1e-6f;

        float m_Time = Unset, m_PreviousTime = Unset;
        float m_Strength, m_PreviousStrength, m_StrengthTarget, m_StrengthFrom, m_StrengthStart, m_StrengthEnd;
        Vector3 m_Direction, m_PreviousDirection, m_DirectionTarget, m_DirectionFrom, m_DirectionMiddle;
        float m_DirectionStart, m_DirectionEnd;
        /// <summary>The noise positions of the shared part, the two branch levels and the ripple,
        /// and those of the frame before.</summary>
        readonly Vector3[] m_Noise = new Vector3[4], m_PreviousNoise = new Vector3[4];
        /// <summary>A local wind's return to its asset's wind, per part: when it starts and ends
        /// (<see cref="Unset"/>: none), where its noise position starts and ends.</summary>
        float[] m_ReturnStart, m_ReturnEnd;
        Vector3[] m_ReturnFrom, m_ReturnTo;

        /// <summary>The 22 constants as boxed Vector4 values, updated in place each frame (the
        /// draws take them without boxing anew).</summary>
        internal readonly object[] Values = NewValues();

        internal static object[] NewValues()
        {
            var values = new object[ConstantCount];
            for (int i = 0; i < values.Length; i++) values[i] = Vector4.zero;
            return values;
        }

        /// <summary>The strength the constants and curves take (clamped to [0, 1]).</summary>
        internal float DrawnStrength => Clamp01(m_Strength);

        /// <summary>Whether the wind still moves: on its way to its target, or with strength.</summary>
        internal bool Blowing => InTransition || Clamp01(m_Strength) > 0f;

        internal bool InTransition =>
            Math.Abs(m_Direction.x - m_DirectionTarget.x) > Arrived || Math.Abs(m_Direction.y - m_DirectionTarget.y) > Arrived ||
            Math.Abs(m_Direction.z - m_DirectionTarget.z) > Arrived || Math.Abs(m_Strength - m_StrengthTarget) > Arrived;

        /// <summary>Advances the wind to <paramref name="time"/> under the scene's wind (a unit
        /// direction, or zero, and its strength) and updates <see cref="Values"/>.</summary>
        internal unsafe void Update(float time, Vector3 direction, float strength, ref SpeedTreeWindConfig9 config)
        {
            fixed (SpeedTreeWindConfig9* c = &config)
            {
                if (!InTransition && Clamp01(m_Strength) <= 0f && strength == 0f)
                {
                    // Calm: the timers start over (the time at zero, so a new wind's direction
                    // eases from the start of play).
                    m_Time = 0f;
                    m_PreviousTime = Unset;
                    m_PreviousStrength = 0f;
                    m_Strength = 0f;
                }
                else
                {
                    SetDirection(time, direction, c->directionResponse);
                    SetStrength(time, strength, c->strengthResponse);
                    Advance(time);
                    MoveNoises(c);
                }
                WriteAll(c);
            }
        }

        /// <summary>Moves each enabled part's noise position against the wind for the time since
        /// the previous update, at its speed for the current strength.</summary>
        unsafe void MoveNoises(SpeedTreeWindConfig9* c)
        {
            Curve(Clamp01(m_Strength), out int i0, out int i1, out float t);
            float dt = m_Time - m_PreviousTime;
            var step = new Vector3(m_Direction.x * dt, m_Direction.y * dt, dt * m_Direction.z);
            if (c->doShared != 0) MoveNoise(0, step, Sample(c->speedShared, i0, i1, t));
            if (c->doBranch1 != 0) MoveNoise(1, step, Sample(c->speedBranch1, i0, i1, t));
            if (c->doBranch2 != 0) MoveNoise(2, step, Sample(c->speedBranch2, i0, i1, t));
            if (c->doRipple != 0) MoveNoise(3, step, Sample(c->speedRipple, i0, i1, t));
        }

        unsafe void WriteAll(SpeedTreeWindConfig9* c)
        {
            Write(0, c, m_Direction, Clamp01(m_Strength), m_Noise);
            Write(HistoryOffset, c, m_PreviousDirection, m_PreviousStrength, m_PreviousNoise);
        }

        /// <summary>
        /// Starts a local wind (a tree's own, in reach of spherical zones) from its asset's wind
        /// <paramref name="from"/>: its direction, strength, targets and time, and its noise
        /// positions, now easing toward <paramref name="wind"/> (the directional and the
        /// spherical wind at the tree), when <paramref name="sync"/> (the asset's wind has
        /// strength); otherwise from rest. Until its first update the local wind gives the
        /// asset's wind's constants of this frame.
        /// </summary>
        internal unsafe void Begin(SpeedTreeWindState from, Vector3 wind, ref SpeedTreeWindConfig9 config, bool sync)
        {
            m_ReturnStart = new[] { Unset, Unset, Unset, Unset };
            m_ReturnEnd = new[] { Unset, Unset, Unset, Unset };
            m_ReturnFrom = new Vector3[4];
            m_ReturnTo = new Vector3[4];
            for (int i = 0; i < ConstantCount; i++) Set(i, Unsafe.Unbox<Vector4>(from.Values[i]));
            if (!sync) return;
            m_Direction = from.m_Direction;
            m_Strength = from.m_Strength;
            m_Time = from.m_Time;
            m_StrengthTarget = from.m_StrengthTarget;
            m_DirectionTarget = from.m_DirectionTarget;
            float time = from.m_Time;
            float length = MathF.Sqrt((wind.x * wind.x + wind.y * wind.y) + wind.z * wind.z);
            fixed (SpeedTreeWindConfig9* c = &config)
            {
                // A spherical wind that cancels the directional one exactly leaves the direction
                // as it is.
                if (length > 0f) SetDirection(time, Divide(wind, length), c->directionResponse);
                SetStrength(time, length, c->strengthResponse);
                Advance(time);
                Array.Copy(from.m_Noise, m_Noise, m_Noise.Length);
                Array.Copy(from.m_PreviousNoise, m_PreviousNoise, m_PreviousNoise.Length);
                MoveNoises(c);
            }
        }

        /// <summary>
        /// Advances a local wind to <paramref name="time"/>: it eases toward the directional wind
        /// plus the spherical wind at its tree (<paramref name="spherical"/>) as the asset's wind
        /// does toward the directional one, and its noise moves while it has strength. Out of
        /// every spherical zone's reach, while the asset's wind blows, its noise positions return
        /// to the asset's wind's (<see cref="StartReturn"/>). Returns whether it has become the
        /// asset's wind again (arrived, the same wind vector and noise): its trees then take the
        /// asset's wind.
        /// </summary>
        internal unsafe bool UpdateLocal(float time, Vector3 directional, Vector3 spherical, SpeedTreeWindState asset, ref SpeedTreeWindConfig9 config)
        {
            bool reached = (spherical.x * spherical.x + spherical.y * spherical.y) + spherical.z * spherical.z != 0f;
            var wind = new Vector3(directional.x + spherical.x, directional.y + spherical.y, directional.z + spherical.z);
            float length = MathF.Sqrt((wind.x * wind.x + wind.y * wind.y) + wind.z * wind.z);
            var direction = length > 0f ? Divide(wind, length) : wind;
            fixed (SpeedTreeWindConfig9* c = &config)
            {
                SetDirection(time, direction, c->directionResponse);
                SetStrength(time, length, c->strengthResponse);
                Advance(time);
                float dt = m_Time - m_PreviousTime;
                if (Clamp01(m_Strength) > 0f) MoveNoises(c);
                float assetStrength = Clamp01(asset.m_Strength);
                if (!reached && assetStrength > 0f && !SameNoise(asset))
                {
                    StartReturn(asset, time - dt, c, assetStrength);
                    Return(time);
                }
                bool same = SameNoise(asset);
                WriteAll(c);
                if (InTransition) return false;
                float dx = directional.x - wind.x, dy = directional.y - wind.y, dz = directional.z - wind.z;
                return (dy * dy + dx * dx) + dz * dz <= CloseSquared && same;
            }
        }

        /// <summary>Whether each part's noise position is within <see cref="Close"/> of the other
        /// wind's.</summary>
        bool SameNoise(SpeedTreeWindState other)
        {
            for (int part = 0; part < m_Noise.Length; part++)
            {
                var a = m_Noise[part];
                var b = other.m_Noise[part];
                float dx = b.x - a.x, dy = b.y - a.y, dz = b.z - a.z;
                if ((dz * dz + dy * dy) + dx * dx > CloseSquared) return false;
            }
            return true;
        }

        /// <summary>
        /// Starts each part's return (one not already returning) to the asset's wind's noise
        /// position from <paramref name="now"/>: the asset's position drifts against the asset's
        /// wind at the part's speed; this one moves straight at <see cref="CatchUpSpeed"/> times
        /// that speed to where they meet, the earliest positive meeting time (none: it jumps). A
        /// part without speed whose positions already agree takes no return. When any part's
        /// meeting is later than <see cref="LongestCatchUp"/>, every part jumps to its meeting
        /// point. Every part counts, whether the asset's wind uses it or not.
        /// </summary>
        unsafe void StartReturn(SpeedTreeWindState asset, float now, SpeedTreeWindConfig9* c, float strength)
        {
            bool late = false;
            var direction = asset.m_Direction;
            Curve(strength, out int i0, out int i1, out float t);
            for (int part = 0; part < 4; part++)
            {
                if (m_ReturnStart[part] != Unset) continue;
                float* curve = part switch { 0 => c->speedShared, 1 => c->speedBranch1, 2 => c->speedBranch2, _ => c->speedRipple };
                float speed = Sample(curve, i0, i1, t);
                var position = m_Noise[part];
                var target = asset.m_Noise[part];
                float dx = position.x - target.x, dy = position.y - target.y, dz = position.z - target.z;
                if (speed == 0f && (dy * dy + dx * dx) + dz * dz <= CloseSquared) continue;
                // The asset's position drifts against the wind.
                float vx = direction.x * speed, vy = direction.y * speed, vz = direction.z * speed;
                float drift_x = -vx, drift_y = -0f - vy, drift_z = -vz;
                float distance = MathF.Sqrt((dx * dx + dy * dy) + dz * dz);
                float meeting = 0f;
                if (Math.Abs(distance) > NoDistance)
                {
                    float driftSpeed = MathF.Sqrt((vx * vx + vy * vy) + vz * vz);
                    float ratio = speed * CatchUpSpeed / driftSpeed;
                    float cos = (drift_x / driftSpeed * (dx / distance) + dy / distance * (drift_y / driftSpeed)) + drift_z / driftSpeed * (dz / distance);
                    float discriminant = (cos * cos + ratio * ratio) + -1f;
                    if (!(0f > discriminant))
                    {
                        float root = MathF.Sqrt(discriminant);
                        if (ratio != 0f)
                        {
                            float scale = distance / (ratio * ratio + -1f);
                            float first = scale * (root - cos) / driftSpeed, second = scale * (-cos - root) / driftSpeed;
                            if (second > 0f && first > 0f) meeting = second > first ? first : second;
                            else
                            {
                                float later = first > second ? first : second;
                                meeting = later > 0f ? later : 0f;
                            }
                        }
                    }
                }
                late |= meeting > LongestCatchUp;
                m_ReturnStart[part] = now;
                m_ReturnEnd[part] = (meeting > LongestCatchUp ? 0f : meeting) + now;
                m_ReturnTo[part] = new Vector3(drift_x * meeting + target.x, drift_y * meeting + target.y, meeting * drift_z + target.z);
                m_ReturnFrom[part] = position;
            }
            if (!late) return;
            for (int part = 0; part < 4; part++) m_ReturnStart[part] = m_ReturnEnd[part] = now;
        }

        /// <summary>Moves each part's noise position along its return at <paramref name="time"/>
        /// (the previous position becomes the history); a part at the end of its return (or
        /// without one) is at its end point, and its return is over.</summary>
        void Return(float time)
        {
            for (int part = 0; part < 4; part++)
            {
                m_PreviousNoise[part] = m_Noise[part];
                float start = m_ReturnStart[part], span = m_ReturnEnd[part] - start;
                float f = span != 0f ? (time - start) / span : 1f;
                if (1f <= f) f = 1f;
                if (f <= 0f) f = 0f;
                float g = 1f - f;
                Vector3 a = m_ReturnFrom[part], b = m_ReturnTo[part];
                m_Noise[part] = new Vector3(a.x * g + b.x * f, a.y * g + b.y * f, g * a.z + b.z * f);
                if (f == 1f) m_ReturnStart[part] = m_ReturnEnd[part] = Unset;
            }
        }

        void SetDirection(float time, Vector3 direction, float response)
        {
            if ((direction.x * direction.x + direction.y * direction.y) + direction.z * direction.z == 0f) return;
            if (direction.x == m_DirectionTarget.x && direction.y == m_DirectionTarget.y && m_DirectionStart <= time && direction.z == m_DirectionTarget.z) return;
            m_DirectionTarget = direction;
            float change = 1f - ((m_Direction.y * direction.y + m_Direction.x * direction.x) + m_Direction.z * direction.z + 1f) * 0.5f;
            float start = m_Time == Unset ? time : m_Time;
            m_DirectionStart = start;
            m_DirectionEnd = change * response + (1f - change) * (response * 0.5f) + start;
            m_DirectionFrom = m_Direction;
            var middle = new Vector3((m_DirectionTarget.x + m_DirectionFrom.x) * 0.5f, (m_DirectionFrom.y + m_DirectionTarget.y) * 0.5f,
                (m_DirectionTarget.z + m_DirectionFrom.z) * 0.5f);
            float length = (middle.y * middle.y + middle.x * middle.x) + middle.z * middle.z;
            m_DirectionMiddle = length != 0f ? Divide(middle, MathF.Sqrt(length)) : Vector3.zero;
        }

        void SetStrength(float time, float strength, float response)
        {
            if (m_StrengthTarget == strength && !(time < m_StrengthStart)) return;
            float start = m_Time > 0f ? m_Time : time;
            m_StrengthStart = start;
            float change = Math.Abs(strength - m_Strength);
            m_StrengthEnd = change * response + (1f - change) * (0.5f * response) + start;
            m_StrengthFrom = m_Strength;
            m_StrengthTarget = strength;
        }

        void Advance(float time)
        {
            float before = m_Time;
            m_PreviousTime = before == Unset ? time : before;
            m_PreviousDirection = m_Direction;
            m_PreviousStrength = Clamp01(m_Strength);
            m_Time = time;
            float f = m_DirectionEnd != m_DirectionStart ? Progress(time, m_DirectionStart, m_DirectionEnd) : 1f;
            // Half linear, half smoothstep.
            float s = f * 0.5f + (3f - (f + f)) * (f * f) * 0.5f;
            Vector3 d;
            if (s < 0.5f)
            {
                float u = s + s;
                d = new Vector3(u * m_DirectionMiddle.x + (1f - u) * m_DirectionFrom.x, u * m_DirectionMiddle.y + (1f - u) * m_DirectionFrom.y,
                    u * m_DirectionMiddle.z + (1f - u) * m_DirectionFrom.z);
            }
            else
            {
                float u = s + -0.5f;
                u += u;
                d = new Vector3(u * m_DirectionTarget.x + (1f - u) * m_DirectionMiddle.x, u * m_DirectionTarget.y + (1f - u) * m_DirectionMiddle.y,
                    u * m_DirectionTarget.z + (1f - u) * m_DirectionMiddle.z);
            }
            float length = MathF.Sqrt((d.y * d.y + d.x * d.x) + d.z * d.z);
            m_Direction = length > 0f ? Divide(d, length) : d;
            float g = m_StrengthEnd != m_StrengthStart ? Progress(time, m_StrengthStart, m_StrengthEnd) : 0f;
            float h = 0f * g + (3f - (g + g)) * (g * g);
            m_Strength = h * m_StrengthTarget + (1f - h) * m_StrengthFrom;
        }

        /// <summary>How far <paramref name="time"/> is from <paramref name="start"/> to
        /// <paramref name="end"/>, in [0, 1].</summary>
        static float Progress(float time, float start, float end)
        {
            float x = (time - start) / (end - start);
            return 0f > x ? 0f : 1f < x ? 1f : x;
        }

        static Vector3 Divide(Vector3 v, float length) => new Vector3(v.x / length, v.y / length, v.z / length);

        void MoveNoise(int part, Vector3 step, float speed)
        {
            var noise = m_Noise[part];
            m_PreviousNoise[part] = noise;
            m_Noise[part] = new Vector3(noise.x - step.x * speed, noise.y - step.y * speed, noise.z - speed * step.z);
        }

        /// <summary>The strength as the curves read it.</summary>
        static float Clamp01(float strength)
        {
            float clamped = strength <= 1f ? strength : 1f;
            return strength < 0f ? 0f : clamped;
        }

        /// <summary>The two curve samples around <paramref name="strength"/> and the weight of the
        /// second.</summary>
        static void Curve(float strength, out int i0, out int i1, out float t)
        {
            float x = (CurveSamples - 1f) * strength;
            i0 = (int)x;
            i1 = i0 + 1;
            t = 0f;
            if (i1 >= CurveSamples)
            {
                i0 = i1 = CurveSamples - 1;
                return;
            }
            if (i0 < 0)
            {
                i0 = i1 = 0;
                return;
            }
            t = x - i0;
        }

        static unsafe float Sample(float* curve, int i0, int i1, float t) => curve[i1] * t + curve[i0] * (1f - t);

        /// <summary>One set of 11 constants: the wind vector, the tree's extents and limits, and
        /// each enabled part's noise position and curve values at <paramref name="strength"/> (a
        /// disabled part's stay zero).</summary>
        unsafe void Write(int at, SpeedTreeWindConfig9* c, Vector3 direction, float strength, Vector3[] noise)
        {
            Curve(strength, out int i0, out int i1, out float t);
            Set(at + 0, new Vector4(direction.x, direction.y, direction.z, strength));
            Set(at + 1, new Vector4(c->treeExtentX, c->treeExtentY, c->treeExtentZ, c->sharedHeightStart));
            Set(at + 2, new Vector4(c->branch1StretchLimit, c->branch2StretchLimit, c->windIndependence, c->importScale));
            if (c->doShared != 0)
            {
                Set(at + 3, new Vector4(noise[0].x, noise[0].y, noise[0].z, c->independenceShared));
                Set(at + 4, new Vector4(Sample(c->bendShared, i0, i1, t), Sample(c->oscillationShared, i0, i1, t),
                    Sample(c->turbulenceShared, i0, i1, t), Sample(c->flexibilityShared, i0, i1, t)));
            }
            if (c->doBranch1 != 0)
            {
                Set(at + 5, new Vector4(noise[1].x, noise[1].y, noise[1].z, c->independenceBranch1));
                Set(at + 6, new Vector4(Sample(c->bendBranch1, i0, i1, t), Sample(c->oscillationBranch1, i0, i1, t),
                    Sample(c->turbulenceBranch1, i0, i1, t), Sample(c->flexibilityBranch1, i0, i1, t)));
            }
            if (c->doBranch2 != 0)
            {
                Set(at + 7, new Vector4(noise[2].x, noise[2].y, noise[2].z, c->independenceBranch2));
                Set(at + 8, new Vector4(Sample(c->bendBranch2, i0, i1, t), Sample(c->oscillationBranch2, i0, i1, t),
                    Sample(c->turbulenceBranch2, i0, i1, t), Sample(c->flexibilityBranch2, i0, i1, t)));
            }
            if (c->doRipple != 0)
            {
                Set(at + 9, new Vector4(noise[3].x, noise[3].y, noise[3].z, c->independenceRipple));
                Set(at + 10, new Vector4(Sample(c->planarRipple, i0, i1, t), Sample(c->directionalRipple, i0, i1, t),
                    Sample(c->flexibilityRipple, i0, i1, t), c->shimmerRipple));
            }
        }

        void Set(int index, Vector4 value) => Unsafe.Unbox<Vector4>(Values[index]) = value;
    }

    /// <summary>
    /// Runs the SpeedTree 9 winds (see <see cref="SpeedTreeWindState"/>) and hands their
    /// constants to the draws of trees' renderers. Each wind asset that exists keeps its wind,
    /// which moves once a frame in PreUpdate.WindUpdate toward the sum of the active directional
    /// wind zones (each its forward, through the hierarchy's rotations and scale signs,
    /// normalized, times windMain; pulse and turbulence take no part) at Time.time. A tree's
    /// renderer gets its constants when it first draws while the world has wind (a directional
    /// wind, a spherical zone or a wind still moving; not a directional zone without wind; a
    /// spherical zone without wind is unmeasured and counts) and from then on
    /// draws with its wind's constants of each frame, also once the wind has settled (its last
    /// direction, no strength, its noise where it stopped). Before that it draws with zeros (the
    /// Editor's draw reads whatever an earlier draw left).
    /// <para>
    /// A tree drawn while spherical zones reach its bounds (the bounds of its LOD group's tree
    /// renderers, else its renderer's) gets a local wind of its own after that draw (the draw
    /// still takes its asset's wind): each zone whose radius reaches the bounds blows from the
    /// zone's position toward the bounds' center raised by a quarter of their height, at its
    /// main wind. The local wind starts from the asset's wind and eases each frame toward the
    /// directional wind plus the spherical wind at the tree (<see cref="SpeedTreeWindState.UpdateLocal"/>);
    /// out of every zone's reach it returns to the asset's wind and is given up once it is that
    /// wind again. The trees of one LOD group with the same asset share the local wind of the
    /// group's first such renderer (tests/projects/speedtree9wind GroupLevel1).
    /// </para>
    /// The GPU Resident Drawer's trees (<see cref="WriteBuffer"/>) take the same constants from
    /// their instance data instead, which the drawer writes each frame they are visible.
    /// </summary>
    internal static class SpeedTreeWindSystem
    {
        static readonly List<SpeedTreeWindAsset> s_Assets = new List<SpeedTreeWindAsset>();
        /// <summary>The active trees, by game object (a tree's wind asset may be set after it is
        /// enabled; the draws read it).</summary>
        static readonly Dictionary<GameObject, Tree> s_Trees = new Dictionary<GameObject, Tree>();
        /// <summary>The constants of a tree not yet drawn with wind, and the history constants of a
        /// renderer without per-object motion vectors.</summary>
        static readonly object[] s_Zero = SpeedTreeWindState.NewValues();
        static readonly int[] s_Ids = Ids();
        /// <summary>Whether a spherical zone was active at this frame's wind update (the draws
        /// look for local winds only then, though they take the zones active when they draw).</summary>
        static bool s_AnySpherical;
        /// <summary>The local winds, in the order they started, and the directional wind of this
        /// frame.</summary>
        static readonly List<LocalWind> s_Locals = new List<LocalWind>();
        static Vector3 s_Directional;
        static bool s_AnyWind;

        /// <summary>A local wind and the trees that draw with it (its first tree's bounds say
        /// which zones reach it).</summary>
        internal sealed class LocalWind
        {
            internal readonly SpeedTreeWindState Wind = new SpeedTreeWindState();
            internal readonly SpeedTreeWindAsset Asset;
            internal readonly List<Tree> Trees = new List<Tree>();

            internal LocalWind(SpeedTreeWindAsset asset) => Asset = asset;
        }

        static int[] Ids()
        {
            string[] names =
            {
                "_ST_WindVector", "_ST_TreeExtents_SharedHeightStart", "_ST_BranchStretchLimits",
                "_ST_Shared_NoisePosTurbulence_Independence", "_ST_Shared_Bend_Oscillation_Turbulence_Flexibility",
                "_ST_Branch1_NoisePosTurbulence_Independence", "_ST_Branch1_Bend_Oscillation_Turbulence_Flexibility",
                "_ST_Branch2_NoisePosTurbulence_Independence", "_ST_Branch2_Bend_Oscillation_Turbulence_Flexibility",
                "_ST_Ripple_NoisePosTurbulence_Independence", "_ST_Ripple_Planar_Directional_Flexibility_Shimmer",
            };
            var ids = new int[SpeedTreeWindState.ConstantCount];
            for (int i = 0; i < names.Length; i++)
            {
                ids[i] = Shader.PropertyToID(names[i]);
                ids[SpeedTreeWindState.HistoryOffset + i] = Shader.PropertyToID(names[i].Insert(4, "History"));
            }
            return ids;
        }

        internal static void Register(SpeedTreeWindAsset asset)
        {
            if (!s_Assets.Contains(asset)) s_Assets.Add(asset);
        }

        internal static void Unregister(SpeedTreeWindAsset asset) => s_Assets.Remove(asset);

        internal static void TreeEnabled(Tree tree)
        {
            s_Trees.TryAdd(tree.m_GameObject, tree);
        }

        internal static void TreeDisabled(Tree tree)
        {
            if (s_Trees.TryGetValue(tree.m_GameObject, out var known) && ReferenceEquals(known, tree)) s_Trees.Remove(tree.m_GameObject);
            Detach(tree);
        }

        /// <summary>Takes a tree off its local wind, which ends with its last tree.</summary>
        internal static void Detach(Tree tree)
        {
            var local = tree.m_LocalWind;
            if (local == null) return;
            tree.m_LocalWind = null;
            local.Trees.Remove(tree);
            if (local.Trees.Count == 0) s_Locals.Remove(local);
        }

        /// <summary>PreUpdate.WindUpdate.</summary>
        internal static void Update()
        {
            var wind = Vector3.zero;
            s_AnySpherical = false;
            foreach (var zone in WindZone.All)
            {
                if (zone.mode == WindZoneMode.Spherical)
                {
                    s_AnySpherical = true;
                    continue;
                }
                var d = zone.SpeedTreeDirection();
                float main = zone.windMain;
                wind = new Vector3(wind.x + d.x * main, wind.y + d.y * main, wind.z + main * d.z);
            }
            float strength = MathF.Sqrt((wind.x * wind.x + wind.y * wind.y) + wind.z * wind.z);
            var direction = strength != 0f ? new Vector3(wind.x / strength, wind.y / strength, wind.z / strength) : wind;
            float time = (float)Time.s_Time;
            bool blowing = false;
            foreach (var asset in s_Assets)
            {
                asset.Wind.Update(time, direction, strength, ref asset.Config);
                blowing |= asset.Wind.Blowing;
            }
            s_Directional = wind;
            UpdateLocals(time);
            s_AnyWind = wind.x != 0f || wind.y != 0f || wind.z != 0f || s_AnySpherical || blowing || s_Locals.Count > 0;
        }

        /// <summary>Advances the local winds after their assets' winds. A local wind that has
        /// become its asset's wind again gives up one tree a frame (its first; the order is
        /// unmeasured); one whose asset is gone gives up all of them.</summary>
        static void UpdateLocals(float time)
        {
            for (int i = 0; i < s_Locals.Count; i++)
            {
                var local = s_Locals[i];
                var asset = local.Asset;
                if (asset == null)
                {
                    foreach (var tree in local.Trees) tree.m_LocalWind = null;
                    local.Trees.Clear();
                    s_Locals.RemoveAt(i--);
                    continue;
                }
                // A safety net: destroying a tree detaches it. A destroyed first tree leaves, and
                // the same place is looked at again (the next wind, or this one's next tree).
                if (local.Trees[0] == null)
                {
                    Detach(local.Trees[0]);
                    i--;
                    continue;
                }
                var spherical = SphericalWind(TreeBounds(local.Trees[0]));
                if (!local.Wind.UpdateLocal(time, s_Directional, spherical, asset.Wind, ref asset.Config)) continue;
                var first = local.Trees[0];
                Detach(first);
                if (local.Trees.Count == 0) i--;
            }
        }

        /// <summary>The wind of the active spherical zones at bounds: each zone whose radius
        /// reaches the bounds blows at its main wind from its position toward the bounds' center
        /// raised by a quarter of their height (Unity 6000.0.71f1, tests/projects/speedtree9wind).</summary>
        internal static Vector3 SphericalWind(Bounds bounds)
        {
            var sum = Vector3.zero;
            foreach (var zone in WindZone.All)
            {
                if (zone.mode != WindZoneMode.Spherical) continue;
                var position = zone.transform.position;
                float radius = zone.radius;
                // A zone without radius reaches nothing (unmeasured).
                if (!(radius > 0f) || 1f - bounds.SqrDistanceUnchecked(position) / (radius * radius) <= 0f) continue;
                Vector3 center = bounds.center, extents = bounds.extents;
                float x = center.x - position.x;
                float y = ((center.y + extents.y) - (center.y - extents.y)) * 0.25f + center.y - position.y;
                float z = center.z - position.z;
                float length = MathF.Sqrt((x * x + y * y) + z * z);
                // A zone exactly at the point blows nowhere (unmeasured).
                if (length == 0f) continue;
                float main = zone.windMain;
                sum = new Vector3(sum.x + x / length * main, sum.y + y / length * main, sum.z + z / length * main);
            }
            return sum;
        }

        /// <summary>The bounds the spherical zones reach a tree by: those of its LOD group's
        /// renderers with a Tree together (levels it does not draw included), else its renderer's
        /// (Unity 6000.0.71f1, tests/projects/speedtree9wind `SplitGroup`: a zone reaching only the
        /// moved renderer of a level the group does not draw gives the drawn level a local wind).</summary>
        static Bounds TreeBounds(Tree tree)
        {
            var renderer = tree.m_GameObject.GetComponent<MeshRenderer>();
            if (renderer is null) return new Bounds(tree.transform.position, Vector3.zero);
            var group = renderer.m_LODGroup;
            if (group is null || !group) return renderer.bounds;
            bool any = false;
            var bounds = default(Bounds);
            foreach (var lod in group.m_LODs)
            {
                if (lod.renderers == null) continue;
                foreach (var member in lod.renderers)
                {
                    if (member == null || member.m_GameObject.GetComponent<Tree>() is null) continue;
                    if (any) bounds.Encapsulate(member.bounds);
                    else bounds = member.bounds;
                    any = true;
                }
            }
            return any ? bounds : renderer.bounds;
        }

        /// <summary>The tree whose local wind a tree shares: the first renderer of its LOD group
        /// whose Tree has the same wind asset, else the tree itself.</summary>
        static Tree Sharer(Tree tree, Renderer renderer, SpeedTreeWindAsset asset)
        {
            var group = renderer.m_LODGroup;
            if (group is null || !group) return tree;
            foreach (var lod in group.m_LODs)
            {
                if (lod.renderers == null) continue;
                foreach (var member in lod.renderers)
                {
                    if (member == null) continue;
                    // An inactive level counts as the bounds do (unmeasured); its local wind is
                    // still updated.
                    var other = member.m_GameObject.GetComponent<Tree>();
                    if (other is not null && ReferenceEquals(other.windAsset, asset)) return other;
                }
            }
            return tree;
        }

        /// <summary>
        /// The wind a tree's draw takes: its local wind's, else its asset's. A tree drawn while
        /// spherical zones blow on it and without a local wind gets one after the draw (its group's
        /// sharer gets it first; the tree joins it at a later draw); when the asset's wind has
        /// strength it starts from the asset's wind, otherwise from rest (tests/projects/speedtree9wind LateSettled).
        /// </summary>
        static SpeedTreeWindState WindOf(Tree tree, Renderer renderer, SpeedTreeWindAsset asset)
        {
            var local = tree.m_LocalWind;
            if (local != null) return local.Wind;
            if (s_AnySpherical) Join(tree, renderer, asset);
            return asset.Wind;
        }

        static void Join(Tree tree, Renderer renderer, SpeedTreeWindAsset asset)
        {
            var spherical = SphericalWind(TreeBounds(tree));
            // Zones whose winds cancel exactly at the tree start no local wind (Unity 6000.0.71f1,
            // tests/projects/speedtree9wind `Cancelled`, measured with no directional wind).
            if (spherical.x == 0f && spherical.y == 0f && spherical.z == 0f) return;
            var sharer = Sharer(tree, renderer, asset);
            if (sharer.m_LocalWind != null)
            {
                if (ReferenceEquals(sharer, tree)) return;
                tree.m_LocalWind = sharer.m_LocalWind;
                sharer.m_LocalWind.Trees.Add(tree);
                return;
            }
            var local = new LocalWind(asset);
            local.Trees.Add(sharer);
            sharer.m_LocalWind = local;
            s_Locals.Add(local);
            var wind = new Vector3(s_Directional.x + spherical.x, s_Directional.y + spherical.y, s_Directional.z + spherical.z);
            local.Wind.Begin(asset.Wind, wind, ref asset.Config, asset.Wind.DrawnStrength != 0f);
        }

        /// <summary>The number of float4 wind parameters per drawer tree instance (the SpeedTree 8
        /// layout's 15 and one spare; SpeedTree 9 uses the first 11).</summary>
        const int BufferParams = 16;

        /// <summary>Whether the GPU Resident Drawer treats a renderer as a SpeedTree tree (its
        /// instances get wind parameters): its object has a Tree with a wind asset (Unity
        /// 6000.0.71f1, tests/projects/residentdrawer speedtree, whose trees all have one; a Tree
        /// without a wind asset is unmeasured).</summary>
        internal static bool HasTree(MeshRenderer renderer)
        {
            var tree = renderer.m_GameObject.GetComponent<Tree>();
            return tree is not null && tree.windAsset is not null;
        }

        /// <summary>
        /// SpeedTreeWindManager.UpdateWindAndWriteBufferWindParams: the wind parameters of the
        /// drawer's visible trees (by renderer ID) for its upload buffer (Unity 6000.0.71f1,
        /// tests/projects/residentdrawer speedtree). While the world has no wind (see
        /// <see cref="Update"/>) every listed instance gets zeros; so does it outside Play mode
        /// (unmeasured: the probe records Play mode only). Otherwise a tree with a wind asset gets
        /// its wind's 11 constants, the previous frame's with <paramref name="history"/> (the
        /// drawer asks for those first for a tree that has just become visible, then copies them
        /// into its history parameters); a renderer without per-object motion vectors gets zeros
        /// for the history. The parameters the SpeedTree 9 layout does not use, and those of an
        /// instance that is no longer a tree with a live wind asset (its Tree or asset was
        /// removed after the drawer took it), get zeros: the drawer uploads every parameter of
        /// every listed instance from a buffer it does not clear (what the Editor uploads for
        /// them is unmeasured).
        /// </summary>
        internal static unsafe void WriteBuffer(ReadOnlySpan<int> renderersID, in SpeedTreeWindParamsBufferIterator buffer, bool history)
        {
            uint* data = (uint*)buffer.bufferPtr;
            bool wind = Application.isPlaying && s_AnyWind;
            for (int i = 0; i < renderersID.Length; i++)
            {
                int at = buffer.uintStride * i;
                if (!wind)
                {
                    ClearParams(data, buffer, at);
                    continue;
                }
                if (s_Trees.Count == 0 || !(ObjectRegistry.FindById(renderersID[i]) is MeshRenderer renderer) ||
                    !s_Trees.TryGetValue(renderer.m_GameObject, out var tree) || tree.windAsset == null)
                {
                    ClearParams(data, buffer, at);
                    continue;
                }
                var values = WindOf(tree, renderer, tree.windAsset).Values;
                if (history && renderer.m_MotionVectorGenerationMode != MotionVectorGenerationMode.Object)
                {
                    ClearParams(data, buffer, at);
                    continue;
                }
                int from = history ? SpeedTreeWindState.HistoryOffset : 0;
                for (int p = 0; p < SpeedTreeWindState.HistoryOffset; p++)
                    *(Vector4*)(data + buffer.uintParamOffsets[p] + at) = Unsafe.Unbox<Vector4>(values[from + p]);
                ClearParams(data, buffer, at, SpeedTreeWindState.HistoryOffset);
            }
        }

        /// <summary>Zeros an instance's parameters from <paramref name="first"/> on.</summary>
        static unsafe void ClearParams(uint* data, in SpeedTreeWindParamsBufferIterator buffer, int at, int first = 0)
        {
            for (int p = first; p < BufferParams; p++) *(Vector4*)(data + buffer.uintParamOffsets[p] + at) = Vector4.zero;
        }

        /// <summary>A draw's properties with the wind constants of its renderer's tree, if it has
        /// one. The previous frame's constants are only for a renderer that draws per-object
        /// motion vectors (others draw with zeros there).</summary>
        internal static Dictionary<int, object> Apply(Renderer renderer, Dictionary<int, object> properties)
        {
            if (s_Trees.Count == 0 || !s_Trees.TryGetValue(renderer.m_GameObject, out var tree)) return properties;
            var asset = tree.windAsset;
            // A destroyed asset's wind is no longer advanced: the tree draws without wind.
            if (asset == null) return properties;
            if (s_AnyWind) tree.m_DrawnWithWind = true;
            var wind = WindOf(tree, renderer, asset);
            var values = tree.m_DrawnWithWind ? wind.Values : s_Zero;
            var history = renderer.m_MotionVectorGenerationMode == MotionVectorGenerationMode.Object ? values : s_Zero;
            var merged = DrawPropertyPool.Copy(properties);
            for (int i = 0; i < SpeedTreeWindState.HistoryOffset; i++) merged[s_Ids[i]] = values[i];
            for (int i = SpeedTreeWindState.HistoryOffset; i < s_Ids.Length; i++) merged[s_Ids[i]] = history[i];
            return merged;
        }
    }
}
