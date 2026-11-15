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
    /// for motion vectors, the previous frame's.
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

        float m_Time = Unset, m_PreviousTime = Unset;
        float m_Strength, m_PreviousStrength, m_StrengthTarget, m_StrengthFrom, m_StrengthStart, m_StrengthEnd;
        Vector3 m_Direction, m_PreviousDirection, m_DirectionTarget, m_DirectionFrom, m_DirectionMiddle;
        float m_DirectionStart, m_DirectionEnd;
        /// <summary>The noise positions of the shared part, the two branch levels and the ripple,
        /// and those of the frame before.</summary>
        readonly Vector3[] m_Noise = new Vector3[4], m_PreviousNoise = new Vector3[4];

        /// <summary>The 22 constants as boxed Vector4 values, updated in place each frame (the
        /// draws take them without boxing anew).</summary>
        internal readonly object[] Values = NewValues();

        internal static object[] NewValues()
        {
            var values = new object[ConstantCount];
            for (int i = 0; i < values.Length; i++) values[i] = Vector4.zero;
            return values;
        }

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
                    Curve(Clamp01(m_Strength), out int i0, out int i1, out float t);
                    float dt = m_Time - m_PreviousTime;
                    var step = new Vector3(m_Direction.x * dt, m_Direction.y * dt, dt * m_Direction.z);
                    if (c->doShared != 0) MoveNoise(0, step, Sample(c->speedShared, i0, i1, t));
                    if (c->doBranch1 != 0) MoveNoise(1, step, Sample(c->speedBranch1, i0, i1, t));
                    if (c->doBranch2 != 0) MoveNoise(2, step, Sample(c->speedBranch2, i0, i1, t));
                    if (c->doRipple != 0) MoveNoise(3, step, Sample(c->speedRipple, i0, i1, t));
                }
                Write(0, c, m_Direction, Clamp01(m_Strength), m_Noise);
                Write(HistoryOffset, c, m_PreviousDirection, m_PreviousStrength, m_PreviousNoise);
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
    /// Editor's draw reads whatever an earlier draw left). A spherical zone's own wind on the trees inside its
    /// radius is not simulated: such a tree is warned about and keeps the directional wind.
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
        /// <summary>This frame's spherical zones.</summary>
        static readonly List<WindZone> s_Spherical = new List<WindZone>();
        static bool s_AnyWind, s_WarnedSpherical;

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
        }

        /// <summary>PreUpdate.WindUpdate.</summary>
        internal static void Update()
        {
            var wind = Vector3.zero;
            s_Spherical.Clear();
            foreach (var zone in WindZone.All)
            {
                if (zone.mode == WindZoneMode.Spherical)
                {
                    s_Spherical.Add(zone);
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
            s_AnyWind = wind.x != 0f || wind.y != 0f || wind.z != 0f || s_Spherical.Count > 0 || blowing;
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
                if (s_Spherical.Count > 0 && !s_WarnedSpherical) WarnIfInSphericalZone(renderer);
                if (history && renderer.m_MotionVectorGenerationMode != MotionVectorGenerationMode.Object)
                {
                    ClearParams(data, buffer, at);
                    continue;
                }
                var values = tree.windAsset.Wind.Values;
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

        /// <summary>Lets the spherical zone warning be given again (tests).</summary>
        internal static void ResetSphericalWarning() => s_WarnedSpherical = false;

        /// <summary>Warns once when a tree drawn with wind is inside a spherical zone's radius.</summary>
        static void WarnIfInSphericalZone(Renderer renderer)
        {
            var bounds = renderer.bounds;
            foreach (var zone in s_Spherical)
            {
                if (zone == null) continue;
                float radius = zone.radius;
                if (bounds.SqrDistance(zone.transform.position) >= radius * radius) continue;
                s_WarnedSpherical = true;
                Debug.LogWarning("Unbound: SpeedTree 9 trees inside a spherical WindZone's radius ('" + renderer.name +
                    "') are bent only by the directional wind zones; the spherical zone's own wind is not simulated.");
                return;
            }
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
            if (s_Spherical.Count > 0 && !s_WarnedSpherical) WarnIfInSphericalZone(renderer);
            if (s_AnyWind) tree.m_DrawnWithWind = true;
            var values = tree.m_DrawnWithWind ? asset.Wind.Values : s_Zero;
            var history = renderer.m_MotionVectorGenerationMode == MotionVectorGenerationMode.Object ? values : s_Zero;
            var merged = DrawPropertyPool.Copy(properties);
            for (int i = 0; i < SpeedTreeWindState.HistoryOffset; i++) merged[s_Ids[i]] = values[i];
            for (int i = SpeedTreeWindState.HistoryOffset; i < s_Ids.Length; i++) merged[s_Ids[i]] = history[i];
            return merged;
        }
    }
}
