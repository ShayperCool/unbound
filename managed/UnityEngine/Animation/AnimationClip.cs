using System;
using System.Linq;
using System.Collections.Generic;
using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>Base of what an Animator state plays: an AnimationClip or a blend tree.</summary>
    public partial class Motion : Object
    {
        protected Motion() { }

        public float averageDuration => GetAverageDuration();

        public float averageAngularSpeed => GetMotionStats().AngularSpeed;

        public Vector3 averageSpeed => GetMotionStats().Speed;

        public float apparentSpeed => GetMotionStats().ApparentSpeed;

        internal virtual (Vector3 Speed, float AngularSpeed, float ApparentSpeed) GetMotionStats()
        {
            Unbound.Runtime.Stubs.Ignored("Motion.averageSpeed", "is zero for blend trees (their children's root motion is not averaged yet)");
            return (Vector3.zero, 0f, 0f);
        }

        /// <summary>apparentSpeed from averageSpeed and averageAngularSpeed:
        /// |speed| · (1 + (angular / 2)²), the product in double precision (measured for humanoid
        /// clips, tests/projects/humanoid, and generic root nodes, tests/projects/modelroot).</summary>
        internal static float ApparentSpeed(float x, float y, float z, float angularSpeed)
        {
            float l = MathF.Sqrt(z * z + (x * x + y * y));
            double h = angularSpeed * 0.5f;
            return (float)((h * h + 1.0) * l);
        }

        /// <summary>A humanoid clip's averages, from its muscle clip's root motion.</summary>
        internal static (Vector3, float, float) HumanMotionStats(Unbound.Runtime.Humanoid.HumanClipMotion m)
        {
            return (new Vector3(m.AverageSpeed.x, m.AverageSpeed.y, m.AverageSpeed.z), m.AverageAngularSpeed, m.ApparentSpeed);
        }

        /// <summary>A humanoid clip's root motion constants.</summary>
        internal virtual bool TryHumanMotion(out Unbound.Runtime.Humanoid.HumanClipMotion motion)
        {
            motion = null;
            return false;
        }

        public bool isLooping => GetIsLooping();

        public bool legacy => GetLegacy();

        public bool isHumanMotion => GetIsHumanMotion();

        internal virtual bool GetIsHumanMotion() => false;

        internal virtual float GetAverageDuration() => 0f;
        internal virtual bool GetIsLooping() => false;
        internal virtual bool GetLegacy() => false;
    }

    /// <summary>
    /// Curves over time for properties of a GameObject hierarchy, and events. Read from `.anim`
    /// files: Transform curves (position, rotation as quaternion or Euler angles, scale) are vector
    /// curves; every other property is a float curve named by its serialized property path (with the
    /// component's class id or script); object references (sprite swaps) are stepped PPtr curves.
    /// </summary>
    [ClassId(74)]
    public sealed partial class AnimationClip : Motion, ISerializedAsset, Instantiation.ICloneable
    {
        internal List<ClipCurve> m_Curves = new List<ClipCurve>();
        internal List<ClipObjectCurve> m_ObjectCurves = new List<ClipObjectCurve>();
        internal List<AnimationEvent> m_Events = new List<AnimationEvent>();
        internal float m_SampleRate = 60f;
        internal WrapMode m_WrapMode;
        internal Bounds m_Bounds;
        internal bool m_Legacy;
        /// <summary>The clip's range (m_AnimationClipSettings); without it the range is the curves'.</summary>
        internal bool m_HasRange;
        internal float m_StartTime;
        internal float m_StopTime;
        internal bool m_LoopTime;
        internal float m_CycleOffset;
        internal bool m_LoopBlend;
        /// <summary>The pose additive layers subtract (m_AdditiveReferencePose*): the reference clip
        /// at the reference time when set, else this clip's first frame.</summary>
        internal bool m_HasAdditiveReferencePose;
        internal AnimationClip m_AdditiveReferencePoseClip;
        internal float m_AdditiveReferencePoseTime;
        /// <summary>Settings kept as they are (mirror has no effect on generic clips, measured in
        /// tests/projects/modelclips).</summary>
        internal bool m_Mirror;
        internal float m_OrientationOffsetY;
        internal float m_Level;
        internal bool m_LoopBlendOrientation;
        internal bool m_LoopBlendPositionY;
        internal bool m_LoopBlendPositionXZ;
        internal bool m_KeepOriginalOrientation;
        internal bool m_KeepOriginalPositionY = true;
        internal bool m_KeepOriginalPositionXZ;
        internal bool m_HeightFromFeet;
        /// <summary>Bumped when curves change, so bindings made from the old curves are redone.</summary>
        internal int m_Version;
        /// <summary>The serialized m_HasGenericRootTransform (player data), else computed.</summary>
        internal bool? m_GenericRoot;

        public AnimationClip() { }

        public float length
        {
            get
            {
                if (m_HasRange) return Math.Max(0f, m_StopTime - m_StartTime);
                return CurveEnd();
            }
        }

        /// <summary>Where the clip's time 0 is on its curves.</summary>
        internal float StartTime => m_HasRange ? m_StartTime : 0f;

        float[] m_LoopStart, m_LoopStop;
        int m_LoopVersion = -1;

        /// <summary>Loop pose (m_LoopBlend, with loop time): where the Animator samples the clip at
        /// a time, the phase (time from the start over the length) it corrects the values by, else
        /// -1. The curves themselves stay as they are (measured, tests/projects/modelclips).</summary>
        internal float LoopPosePhase(float time)
        {
            float length = this.length;
            if (!m_LoopTime || !m_LoopBlend || length <= 0f) return -1f;
            if (m_LoopVersion != m_Version || m_LoopStart == null || m_LoopStart.Length != m_Curves.Count)
            {
                m_LoopStart = new float[m_Curves.Count];
                m_LoopStop = new float[m_Curves.Count];
                for (int i = 0; i < m_Curves.Count; i++)
                {
                    m_LoopStart[i] = m_Curves[i].Evaluate(StartTime);
                    m_LoopStop[i] = IsHumanBodyCurve(m_Curves[i]) ? m_LoopStart[i] : m_Curves[i].Evaluate(StartTime + length);
                }
                m_LoopVersion = m_Version;
            }
            return Math.Clamp((time - StartTime) / length, 0f, 1f);
        }

        /// <summary>A float value under loop pose: (start − stop) × phase added.</summary>
        internal float LoopPose(int curve, float value, float phase) =>
            phase < 0f ? value : value + (m_LoopStart[curve] - m_LoopStop[curve]) * phase;

        /// <summary>A rotation (curves <paramref name="first"/>..+3) under loop pose: turned by
        /// normalize(phase · d.xyz, d.w), d = stop⁻¹ · start, of the sampled first and last frames.</summary>
        internal Quaternion LoopPose(int first, Quaternion q, float phase)
        {
            if (phase < 0f) return q;
            var start = Unit(m_LoopStart, first);
            var stop = Unit(m_LoopStop, first);
            var d = new Quaternion(-stop.x, -stop.y, -stop.z, stop.w) * start;
            float x = d.x * phase, y = d.y * phase, z = d.z * phase, w = d.w;
            float m = MathF.Sqrt(x * x + y * y + z * z + w * w);
            if (m < 1e-12f) return q;
            return q * new Quaternion(x / m, y / m, z / m, w / m);
        }

        static Quaternion Unit(float[] v, int i)
        {
            float m = MathF.Sqrt(v[i] * v[i] + v[i + 1] * v[i + 1] + v[i + 2] * v[i + 2] + v[i + 3] * v[i + 3]);
            return m < 1e-12f ? Quaternion.identity : new Quaternion(v[i] / m, v[i + 1] / m, v[i + 2] / m, v[i + 3] / m);
        }

        float CurveEnd()
        {
            float end = 0f;
            foreach (var c in m_Curves) end = Math.Max(end, c.LastKeyTime);
            foreach (var c in m_ObjectCurves)
                if (c.Times.Length > 0) end = Math.Max(end, c.Times[c.Times.Length - 1]);
            foreach (var e in m_Events) end = Math.Max(end, e.m_Time);
            return end;
        }

        public float frameRate
        {
            get => m_SampleRate;
            set => m_SampleRate = value;
        }

        public WrapMode wrapMode
        {
            get => m_WrapMode;
            set => m_WrapMode = value;
        }

        public Bounds localBounds
        {
            get => m_Bounds;
            set => m_Bounds = value;
        }

        public new bool legacy
        {
            get => m_Legacy;
            set => m_Legacy = value;
        }

        public bool empty => m_Curves.Count == 0 && m_ObjectCurves.Count == 0;

        /// <summary>Whether the clip animates the root's position or rotation (a generic root
        /// transform: Timeline then adds its offset and root motion playables).</summary>
        public bool hasGenericRootTransform
        {
            get
            {
                if (m_GenericRoot.HasValue) return m_GenericRoot.Value;
                foreach (var c in m_Curves)
                {
                    var b = c.Binding;
                    if (b.Path.Length != 0 || b.ResolveType() != typeof(Transform)) continue;
                    var a = b.Attribute;
                    if (a.StartsWith("m_LocalPosition", StringComparison.Ordinal) || a.StartsWith("m_LocalRotation", StringComparison.Ordinal) ||
                        a.StartsWith("localEulerAngles", StringComparison.Ordinal) || a.StartsWith("m_LocalEulerAngles", StringComparison.Ordinal))
                        return true;
                }
                return false;
            }
        }

        /// <summary>Whether the clip has a root node's MotionT/MotionQ curves (the model importer
        /// makes them for a generic avatar's root node).</summary>
        internal bool hasRootMotion => MotionCurves() != null;

        public bool hasMotionFloatCurves => MotionCurves() != null;

        public bool hasMotionCurves => MotionCurves() != null;

        /// <summary>Humanoid body curves (RootT/RootQ); generic clips have none.</summary>
        public bool hasRootCurves
        {
            get
            {
                EnsureHumanCurves();
                foreach (var c in m_Curves)
                {
                    var b = c.Binding;
                    if (b.Path.Length == 0 && b.ClassId == AnimatorClassId
                        && (b.Attribute.StartsWith("RootT.", StringComparison.Ordinal) || b.Attribute.StartsWith("RootQ.", StringComparison.Ordinal)))
                        return true;
                }
                return false;
            }
        }

        int[] m_MotionCurves;
        int m_MotionVersion = -1;

        /// <summary>The indices of the MotionT.x/y/z and MotionQ.x/y/z/w curves (on the root's
        /// Animator), or null when the clip has not all of them.</summary>
        internal int[] MotionCurves()
        {
            if (m_MotionVersion == m_Version) return m_MotionCurves;
            m_MotionVersion = m_Version;
            m_MotionCurves = null;
            var names = new[] { "MotionT.x", "MotionT.y", "MotionT.z", "MotionQ.x", "MotionQ.y", "MotionQ.z", "MotionQ.w" };
            var found = new int[names.Length];
            for (int n = 0; n < names.Length; n++)
            {
                found[n] = -1;
                for (int i = 0; i < m_Curves.Count; i++)
                {
                    var b = m_Curves[i].Binding;
                    if (b.Path.Length == 0 && b.ClassId == AnimatorClassId && b.Attribute == names[n]) { found[n] = i; break; }
                }
                if (found[n] < 0) return null;
            }
            return m_MotionCurves = found;
        }

        (Vector3, float, float) m_Stats;
        int m_StatsVersion = -1;

        /// <summary>Root motion statistics of a root node's motion (none without MotionT/MotionQ
        /// curves, root curves included): the motion over twenty equal parts of the clip, each in
        /// the node's frame at its start, over the length; the turn about the up axis likewise.
        /// Bake Into Pose zeroes the baked components of the speed, not the turn (measured,
        /// tests/projects/modelroot).</summary>
        internal override (Vector3 Speed, float AngularSpeed, float ApparentSpeed) GetMotionStats()
        {
            if (TryHumanMotion(out var human)) return HumanMotionStats(human);
            if (m_StatsVersion == m_Version) return m_Stats;
            m_StatsVersion = m_Version;
            m_Stats = (Vector3.zero, 0f, 0f);
            float length = this.length;
            if (MotionCurves() == null || length <= 0f) return m_Stats;
            const int Parts = 20;
            var speed = Vector3.zero;
            float turn = 0f;
            var (p0, q0) = MotionPose(StartTime);
            for (int i = 1; i <= Parts; i++)
            {
                var (p1, q1) = MotionPose(StartTime + length * i / Parts);
                var inverse = Quaternion.Inverse(q0);
                speed += inverse * (p1 - p0);
                var d = inverse * q1;
                turn += 2f * MathF.Atan2(d.y, d.w);
                (p0, q0) = (p1, q1);
            }
            speed /= length;
            if (m_LoopBlendPositionY) speed.y = 0f;
            if (m_LoopBlendPositionXZ) { speed.x = 0f; speed.z = 0f; }
            float angular = turn / length;
            return m_Stats = (speed, angular, ApparentSpeed(speed.x, speed.y, speed.z, angular));
        }

        /// <summary>The root node's pose at a time on the clip's curves (MotionT, MotionQ).</summary>
        internal (Vector3, Quaternion) MotionPose(float time)
        {
            var c = MotionCurves();
            var p = new Vector3(m_Curves[c[0]].Evaluate(time), m_Curves[c[1]].Evaluate(time), m_Curves[c[2]].Evaluate(time));
            var q = new Quaternion(m_Curves[c[3]].Evaluate(time), m_Curves[c[4]].Evaluate(time), m_Curves[c[5]].Evaluate(time), m_Curves[c[6]].Evaluate(time));
            float n = MathF.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return (p, n > 1e-6f ? new Quaternion(q.x / n, q.y / n, q.z / n, q.w / n) : Quaternion.identity);
        }

        public AnimationEvent[] events
        {
            get
            {
                var result = new AnimationEvent[m_Events.Count];
                for (int i = 0; i < result.Length; i++) result[i] = m_Events[i].Copy();
                return result;
            }
            set
            {
                m_Events.Clear();
                if (value != null)
                    foreach (var e in value)
                        if (e != null) m_Events.Add(e.Copy());
                SortEvents();
            }
        }

        public void AddEvent(AnimationEvent evt)
        {
            if (evt == null) throw new ArgumentNullException(nameof(evt));
            m_Events.Add(evt.Copy());
            SortEvents();
        }

        void SortEvents()
        {
            // Stable: events at the same time keep their order.
            var sorted = new List<AnimationEvent>(m_Events);
            sorted.Sort((a, b) => a.m_Time != b.m_Time ? a.m_Time.CompareTo(b.m_Time) : m_Events.IndexOf(a).CompareTo(m_Events.IndexOf(b)));
            m_Events = sorted;
        }

        /// <summary>Samples the clip at <paramref name="time"/> on a GameObject hierarchy, writing the
        /// animated properties.</summary>
        public void SampleAnimation(GameObject go, float time)
        {
            if (go == null) throw new ArgumentNullException(nameof(go));
            ClipSampling.SampleOnce(this, go, time);
        }

        /// <summary>Sets (or with a null curve removes) the curve of one property. In the Editor this
        /// works on any clip (a player refuses non-legacy ones); a non-legacy clip keeps a Transform's
        /// vector whole, so one component's curve brings the others' (measured: zero at the same
        /// times, the clip's bindings x, y, z).</summary>
        public void SetCurve(string relativePath, Type type, string propertyName, AnimationCurve curve)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (propertyName == null) throw new ArgumentNullException(nameof(propertyName));
            relativePath ??= "";
            var attribute = CurveBinding.NormalizeScriptName(type, propertyName);
            m_Curves.RemoveAll(c => c.Binding.Path == relativePath && c.Binding.Attribute == attribute && c.Binding.Matches(type));
            if (curve != null)
            {
                var copy = new AnimationCurve(curve.keys) { preWrapMode = curve.preWrapMode, postWrapMode = curve.postWrapMode };
                m_Curves.Add(new ClipCurve { Binding = CurveBinding.ForType(relativePath, attribute, type), Curve = copy });
                if (!m_Legacy && typeof(Transform).IsAssignableFrom(type)) AddVectorCompanions(relativePath, attribute, type, copy);
            }
            m_Version++;
        }

        void AddVectorCompanions(string path, string attribute, Type type, AnimationCurve curve)
        {
            int dot = attribute.LastIndexOf('.');
            if (dot < 0) return;
            var head = attribute.Substring(0, dot);
            string[] axes;
            switch (head)
            {
                case "m_LocalPosition": case "m_LocalScale": case "localEulerAnglesRaw": axes = new[] { "x", "y", "z" }; break;
                case "m_LocalRotation": axes = new[] { "x", "y", "z", "w" }; break;
                default: return;
            }
            foreach (var axis in axes)
            {
                var name = head + "." + axis;
                if (m_Curves.Exists(c => c.Binding.Path == path && c.Binding.Attribute == name && c.Binding.Matches(type))) continue;
                float value = head == "m_LocalScale" || (head == "m_LocalRotation" && axis == "w") ? 1f : 0f;
                var keys = curve.keys.Select(k => new Keyframe(k.time, value)).ToArray();
                m_Curves.Add(new ClipCurve { Binding = CurveBinding.ForType(path, name, type), Curve = new AnimationCurve(keys) });
            }
        }

        /// <summary>
        /// Realigns quaternion keys for the shortest interpolation path (documented, 6000.0
        /// AnimationClip.EnsureQuaternionContinuity): q and −q are the same rotation, so a key whose
        /// quaternion lies in the opposite hemisphere from the key before it (a negative dot product,
        /// that key already realigned) is negated, from the first key on.
        /// <para>Unbound's choice (unmeasured): the rotations realigned are the clip's quaternion
        /// rotation curves (four marked curves from a clip file's rotation curves) and the
        /// m_LocalRotation.x, .y, .z, .w curves of one Transform (SetCurve's localRotation); Euler
        /// angle curves and float curves on the Animator are kept as they are. A negated key's in
        /// and out tangents are negated with it (its weights are kept), so the curve keeps its shape
        /// about the key. Legacy and non-legacy clips are treated alike. The clip's curves are
        /// replaced only when a key is negated, which also redoes the clip's bindings; clips made
        /// from it before (Animation.AddClip with frames) keep their keys.</para>
        /// <para>A rotation whose four curves do not have their keys at the same times, or that is a
        /// player clip's compiled curve, keeps its keys, with a warning naming it on every call.</para>
        /// </summary>
        public void EnsureQuaternionContinuity()
        {
            bool changed = false;
            List<string> kept = null;
            foreach (var group in QuaternionRotations())
            {
                var reason = AlignQuaternionKeys(group, ref changed);
                if (reason != null) (kept ??= new List<string>()).Add($"{m_Curves[group[0]].Binding} ({reason})");
            }
            if (changed) m_Version++;
            if (kept != null)
                Debug.LogWarning($"Unbound: AnimationClip.EnsureQuaternionContinuity on '{name}': these rotations keep their keys as they are: {string.Join("; ", kept)}.", this);
        }

        /// <summary>The quaternion rotations among the clip's curves, as four curve indices (x, y,
        /// z, w): four curves in a row from one marked <see cref="ClipCurve.QuaternionStart"/>, and
        /// the m_LocalRotation.x, .y, .z, .w curves of one Transform. A rotation with a component
        /// missing or given twice is not one.</summary>
        List<int[]> QuaternionRotations()
        {
            var groups = new List<int[]>();
            var marked = new bool[m_Curves.Count];
            for (int i = 0; i + 3 < m_Curves.Count; i++)
            {
                if (!m_Curves[i].QuaternionStart) continue;
                groups.Add(new[] { i, i + 1, i + 2, i + 3 });
                for (int k = 0; k < 4; k++) marked[i + k] = true;
                i += 3;
            }
            var byTarget = new Dictionary<(string, bool, uint), int[]>();
            var order = new List<int[]>();
            for (int i = 0; i < m_Curves.Count; i++)
            {
                if (marked[i]) continue;
                var b = m_Curves[i].Binding;
                int axis = LocalRotationAxis(b);
                if (axis < 0) continue;
                var key = (b.Path ?? "", b.Hashed, b.PathHash);
                if (!byTarget.TryGetValue(key, out var g))
                {
                    byTarget[key] = g = new[] { -1, -1, -1, -1, 0 };
                    order.Add(g);
                }
                if (g[axis] >= 0) g[4] = 1; // given twice
                else g[axis] = i;
            }
            foreach (var g in order)
                if (g[4] == 0 && g[0] >= 0 && g[1] >= 0 && g[2] >= 0 && g[3] >= 0) groups.Add(new[] { g[0], g[1], g[2], g[3] });
            return groups;
        }

        /// <summary>The component (0..3 for x, y, z, w) of a Transform's m_LocalRotation a binding
        /// animates, or -1.</summary>
        static int LocalRotationAxis(CurveBinding b)
        {
            const string head = "m_LocalRotation.";
            var a = b.Attribute;
            if (a == null || a.Length != head.Length + 1 || !a.StartsWith(head, StringComparison.Ordinal)) return -1;
            bool transform = b.Type != null ? typeof(Transform).IsAssignableFrom(b.Type)
                : b.ClassId == EngineTypes.TransformId || b.ClassId == EngineTypes.RectTransformId;
            return transform ? "xyzw".IndexOf(a[head.Length]) : -1;
        }

        /// <summary>Negates the keys of one rotation (curves <paramref name="group"/>) that are in
        /// the opposite hemisphere from the key before them; null, or why its keys are kept.</summary>
        string AlignQuaternionKeys(int[] group, ref bool changed)
        {
            var keys = new Keyframe[4][];
            for (int k = 0; k < 4; k++)
            {
                var c = m_Curves[group[k]];
                if (c.Sampled != null) return "a player clip's compiled curve";
                if (c.Curve == null) return "a curve without keys";
                keys[k] = c.Curve.keys;
            }
            int n = keys[0].Length;
            for (int k = 1; k < 4; k++)
                if (keys[k].Length != n) return "its x, y, z and w curves have different numbers of keys";
            for (int i = 0; i < n; i++)
                for (int k = 1; k < 4; k++)
                    if (keys[k][i].time != keys[0][i].time) return "its x, y, z and w curves have keys at different times";
            bool flipped = false;
            for (int i = 1; i < n; i++)
            {
                float dot = 0f;
                for (int k = 0; k < 4; k++) dot += keys[k][i - 1].value * keys[k][i].value;
                if (!(dot < 0f)) continue;
                for (int k = 0; k < 4; k++)
                {
                    var key = keys[k][i];
                    key.value = -key.value;
                    key.inTangent = -key.inTangent;
                    key.outTangent = -key.outTangent;
                    keys[k][i] = key;
                }
                flipped = true;
            }
            if (!flipped) return null;
            for (int k = 0; k < 4; k++)
            {
                var c = m_Curves[group[k]];
                m_Curves[group[k]] = new ClipCurve
                {
                    Binding = c.Binding,
                    Curve = new AnimationCurve(keys[k]) { preWrapMode = c.Curve.preWrapMode, postWrapMode = c.Curve.postWrapMode },
                    QuaternionStart = c.QuaternionStart,
                    EulerStart = c.EulerStart,
                };
            }
            changed = true;
            return null;
        }

        public void ClearCurves()
        {
            m_Curves.Clear();
            m_ObjectCurves.Clear();
            m_Version++;
        }

        internal override float GetAverageDuration() => length;

        /// <summary>The clip's loop time setting, legacy or not (a legacy clip's wrap mode is the
        /// Animation's business: measured).</summary>
        internal override bool GetIsLooping() => m_LoopTime;

        internal override bool GetLegacy() => m_Legacy;

        /// <summary>A clip sharing these curves over a part of the range (Animation.AddClip with frames).</summary>
        internal AnimationClip SubClip(float start, float stop)
        {
            var clip = Clone();
            clip.m_HasRange = true;
            clip.m_StartTime = start;
            clip.m_StopTime = Math.Max(start, stop);
            clip.m_Events.RemoveAll(e => e.m_Time < start || e.m_Time > stop);
            return clip;
        }

        internal AnimationClip Clone()
        {
            var clip = new AnimationClip
            {
                m_Curves = new List<ClipCurve>(m_Curves),
                m_ObjectCurves = new List<ClipObjectCurve>(m_ObjectCurves),
                m_SampleRate = m_SampleRate,
                m_WrapMode = m_WrapMode,
                m_Bounds = m_Bounds,
                m_Legacy = m_Legacy,
                m_HasRange = m_HasRange,
                m_StartTime = m_StartTime,
                m_StopTime = m_StopTime,
                m_LoopTime = m_LoopTime,
                m_CycleOffset = m_CycleOffset,
                m_LoopBlend = m_LoopBlend,
                m_HasAdditiveReferencePose = m_HasAdditiveReferencePose,
                m_AdditiveReferencePoseClip = m_AdditiveReferencePoseClip,
                m_AdditiveReferencePoseTime = m_AdditiveReferencePoseTime,
                m_Mirror = m_Mirror,
                m_HumanMirrored = m_HumanMirrored,
                m_OrientationOffsetY = m_OrientationOffsetY,
                m_Level = m_Level,
                m_LoopBlendOrientation = m_LoopBlendOrientation,
                m_LoopBlendPositionY = m_LoopBlendPositionY,
                m_LoopBlendPositionXZ = m_LoopBlendPositionXZ,
                m_KeepOriginalOrientation = m_KeepOriginalOrientation,
                m_KeepOriginalPositionY = m_KeepOriginalPositionY,
                m_KeepOriginalPositionXZ = m_KeepOriginalPositionXZ,
                m_HeightFromFeet = m_HeightFromFeet,
                m_GenericRoot = m_GenericRoot,
            };
            foreach (var e in m_Events) clip.m_Events.Add(e.Copy());
            clip.m_Name = m_Name;
            return clip;
        }

        Object Instantiation.ICloneable.CloneObject() => Clone();

        // ------------------------------------------------------------------ serialized data

        void ISerializedAsset.ReadSerialized(YNode body, IReferenceResolver resolver) => ClipReader.Read(this, body, resolver);
    }
}

namespace Unbound.Runtime
{
    using UnityEngine;
    using Object = UnityEngine.Object;

    /// <summary>What a curve animates: a property (by serialized name) of a component (by class id,
    /// script or type) of the object at a path below the animated root.</summary>
    internal sealed class CurveBinding
    {
        internal string Path = "";
        internal string Attribute = "";
        internal int ClassId;
        internal ObjectRef Script;
        /// <summary>A player clip's binding: its path and (but for Transforms) attribute are CRC32
        /// hashes, resolved against the animated hierarchy when bound.</summary>
        internal bool Hashed;
        internal uint PathHash;
        internal uint AttributeHash;
        internal int CustomType;
        /// <summary>Set for curves made through SetCurve, and once the script is resolved.</summary>
        internal Type Type;
        bool m_ScriptResolved;

        internal static CurveBinding ForType(string path, string attribute, Type type) =>
            new CurveBinding { Path = path, Attribute = attribute, Type = type, m_ScriptResolved = true };

        internal bool Matches(Type type) => Type == type;

        /// <summary>The component type the binding names, or null when it cannot be resolved.</summary>
        internal Type ResolveType()
        {
            if (Type != null || m_ScriptResolved) return Type;
            m_ScriptResolved = true;
            if (ClassId == EngineTypes.MonoBehaviourId)
            {
                Type = Script.Guid == null ? null : GameDomain.ResolveScript(Script.Guid, Script.FileId);
                return Type;
            }
            Type = ClassIds.TypeOf(ClassId);
            return Type;
        }

        /// <summary>SetCurve takes C# names for Transform properties; curves store serialized names.</summary>
        internal static string NormalizeScriptName(Type type, string property)
        {
            if (!typeof(Transform).IsAssignableFrom(type)) return property;
            int dot = property.IndexOf('.');
            var head = dot < 0 ? property : property.Substring(0, dot);
            var tail = dot < 0 ? "" : property.Substring(dot);
            switch (head)
            {
                case "localPosition": return "m_LocalPosition" + tail;
                case "localRotation": return "m_LocalRotation" + tail;
                case "localScale": return "m_LocalScale" + tail;
                case "localEulerAngles": return "localEulerAnglesRaw" + tail;
                default: return property;
            }
        }

        public override string ToString() => $"{(Path.Length == 0 ? "<root>" : Path)}: {Type?.Name ?? $"class {ClassId}"}.{Attribute}";
    }

    /// <summary>One float curve of a clip. Quaternion rotation curves are four consecutive curves
    /// (x, y, z, w), the first marked, so that they are sampled and blended as a rotation.</summary>
    internal sealed class ClipCurve
    {
        internal CurveBinding Binding;
        internal AnimationCurve Curve;
        /// <summary>A player clip's curve (its muscle clip's segments or samples) instead of keys.</summary>
        internal SampledCurve Sampled;
        internal bool QuaternionStart;
        /// <summary>The first of three Euler angle curves: they drive the rotation as a quaternion
        /// (blended with other clips' rotations), as Mecanim does.</summary>
        internal bool EulerStart;

        AnimationCurve m_SegmentsOf;
        KeySegments m_Segments;

        /// <summary>The curve's value at a time on the clip, as the Animator samples it: a player
        /// clip's compiled curve, else the keys' cubic segments (<see cref="KeySegments"/>).</summary>
        internal float Evaluate(float time)
        {
            if (Sampled != null) return Sampled.Evaluate(time);
            if (!ReferenceEquals(m_SegmentsOf, Curve))
            {
                // Clip curves are private copies, replaced rather than edited, so the reference
                // identifies the keys.
                m_SegmentsOf = Curve;
                m_Segments = KeySegments.Of(Curve);
            }
            return m_Segments != null && m_Segments.TryEvaluate(time, out float value) ? value : Curve.Evaluate(time);
        }

        internal float LastKeyTime
        {
            get
            {
                if (Sampled != null) return Sampled.LastTime;
                int n = Curve.length;
                return n > 0 ? Curve[n - 1].time : 0f;
            }
        }
    }

    /// <summary>
    /// A curve of a player AnimationClip (built into a player or an AssetBundle): Unity's compiled
    /// form (the muscle clip) keeps streamed curves as cubic segments ((a t + b) t + c) t + d from
    /// each key to the next, dense curves as samples at a rate (linear in between) and constant
    /// curves as one value.
    /// </summary>
    internal sealed class SampledCurve
    {
        // Segments: their start times and four coefficients each.
        internal float[] Times;
        internal float[] Coefficients;
        // Samples: the values from a begin time at a rate.
        internal float[] Samples;
        internal float BeginTime;
        internal float SampleRate;
        internal float Constant;

        internal float LastTime
        {
            get
            {
                if (Times != null)
                {
                    for (int i = Times.Length - 1; i >= 0; i--)
                        if (!float.IsInfinity(Times[i]) && Times[i] > -float.MaxValue) return Times[i];
                    return 0f;
                }
                if (Samples != null) return BeginTime + Math.Max(0, Samples.Length - 1) / Math.Max(1e-6f, SampleRate);
                return 0f;
            }
        }

        /// <summary>The keys (segment starts or samples) as time and value.</summary>
        internal IEnumerable<(float, float)> Keys()
        {
            if (Times != null)
            {
                for (int i = 0; i < Times.Length; i++)
                {
                    if (float.IsPositiveInfinity(Times[i])) continue;
                    float t = Times[i] > -float.MaxValue ? Times[i] : 0f;
                    yield return (t, Coefficients[4 * i + 3]);
                }
            }
            else if (Samples != null)
            {
                for (int i = 0; i < Samples.Length; i++) yield return (BeginTime + i / Math.Max(1e-6f, SampleRate), Samples[i]);
            }
            else yield return (0f, Constant);
        }

        internal float Evaluate(float time)
        {
            if (Times != null)
            {
                int lo = 0, hi = Times.Length - 1;
                if (hi < 0) return 0f;
                if (time < Times[0]) lo = 0;
                else
                {
                    while (lo < hi)
                    {
                        int mid = (lo + hi + 1) >> 1;
                        if (Times[mid] <= time) lo = mid;
                        else hi = mid - 1;
                    }
                }
                float start = Times[lo];
                float dt = start > -float.MaxValue ? time - start : 0f;
                return Cubic(Coefficients, 4 * lo, dt);
            }
            if (Samples != null)
            {
                if (Samples.Length == 0) return 0f;
                float f = (time - BeginTime) * SampleRate;
                if (f <= 0f) return Samples[0];
                int i = (int)f;
                if (i >= Samples.Length - 1) return Samples[Samples.Length - 1];
                float t = f - i;
                return Samples[i] + (Samples[i + 1] - Samples[i]) * t;
            }
            return Constant;
        }

        /// <summary>A cubic segment ((a t + b) t + c) t + d at <paramref name="dt"/> from its start,
        /// in single precision in this order, its coefficients from <paramref name="c"/>.</summary>
        internal static float Cubic(float[] coefficients, int c, float dt) =>
            ((coefficients[c] * dt + coefficients[c + 1]) * dt + coefficients[c + 2]) * dt + coefficients[c + 3];
    }

    /// <summary>
    /// An authored clip curve's keys as the Animator samples them: each pair of keys becomes the
    /// cubic ((a t + b) t + c) t + d in t = time − the first key's time (a = (d1 + d2 − 2 dy) / dx³,
    /// b = (3 dy − 2 d1 − d2) / dx², c the first key's out slope, d its value, with dx the keys'
    /// distance, at least 1e-4, dy their value difference and d1, d2 the out and in slopes times dx),
    /// computed and evaluated in single precision; a +∞ slope holds the first key's value and a −∞
    /// slope the second's. This is the Hermite curve AnimationCurve.Evaluate gives, rounded as the
    /// Animator rounds it (Unity 6000.0.71f1: tests/projects/humanfirstpose, animculling, humanoid).
    /// Weighted segments and times outside the keys are left to AnimationCurve.Evaluate.
    /// </summary>
    internal sealed class KeySegments
    {
        /// <summary>The smallest key distance of a segment (closer keys count as this far apart).</summary>
        const float MinSegmentLength = 1e-4f;

        readonly float[] m_Times;
        readonly float[] m_Coefficients;
        /// <summary>Per segment: a weighted tangent shapes it (left to AnimationCurve.Evaluate).</summary>
        readonly bool[] m_Weighted;

        KeySegments(float[] times, float[] coefficients, bool[] weighted)
        {
            m_Times = times;
            m_Coefficients = coefficients;
            m_Weighted = weighted;
        }

        /// <summary>The segments of a curve's keys, or null with fewer than two keys.</summary>
        internal static KeySegments Of(AnimationCurve curve)
        {
            if (curve == null || curve.length < 2) return null;
            var keys = curve.keys;
            int n = keys.Length - 1;
            var times = new float[keys.Length];
            var coefficients = new float[4 * n];
            var weighted = new bool[n];
            for (int i = 0; i < keys.Length; i++) times[i] = keys[i].time;
            for (int i = 0; i < n; i++)
            {
                Keyframe k0 = keys[i], k1 = keys[i + 1];
                weighted[i] = (k0.weightedMode & WeightedMode.Out) != 0 || (k1.weightedMode & WeightedMode.In) != 0;
                int c = 4 * i;
                if (k0.outTangent >= float.PositiveInfinity || k1.inTangent >= float.PositiveInfinity)
                {
                    coefficients[c + 3] = k0.value;
                    continue;
                }
                if (k0.outTangent <= float.NegativeInfinity || k1.inTangent <= float.NegativeInfinity)
                {
                    coefficients[c + 3] = k1.value;
                    continue;
                }
                float dx = MathF.Max(k1.time - k0.time, MinSegmentLength);
                float inverse = 1f / dx;
                float inverse2 = inverse * inverse;
                float dy = k1.value - k0.value;
                float d1 = k0.outTangent * dx, d2 = k1.inTangent * dx;
                coefficients[c] = (d1 + d2 - dy - dy) * inverse2 * inverse;
                coefficients[c + 1] = (dy + dy + dy - d1 - d1 - d2) * inverse2;
                coefficients[c + 2] = k0.outTangent;
                coefficients[c + 3] = k0.value;
            }
            return new KeySegments(times, coefficients, weighted);
        }

        /// <summary>The value at <paramref name="time"/> when it lies within the keys on an
        /// unweighted segment; false otherwise (wrap modes, clamping and weights stay with the
        /// curve's own evaluation).</summary>
        internal bool TryEvaluate(float time, out float value)
        {
            value = 0f;
            var times = m_Times;
            int last = times.Length - 1;
            if (!(time >= times[0] && time <= times[last])) return false;
            // The last segment starting at or before the time (a time on the last key reads its value).
            int lo = 0, hi = last;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (times[mid] <= time) lo = mid;
                else hi = mid - 1;
            }
            if (lo == last) return false;
            if (m_Weighted[lo]) return false;
            value = SampledCurve.Cubic(m_Coefficients, 4 * lo, time - times[lo]);
            return true;
        }
    }

    /// <summary>A stepped curve of object references (sprite swaps, material swaps).</summary>
    internal sealed class ClipObjectCurve
    {
        internal CurveBinding Binding;
        internal float[] Times;
        internal Object[] Values;

        internal Object Evaluate(float time)
        {
            if (Values.Length == 0) return null;
            int index = 0;
            for (int i = 0; i < Times.Length; i++)
            {
                if (Times[i] <= time) index = i;
                else break;
            }
            return Values[index];
        }
    }

    /// <summary>Engine types by class id, including the abstract bases curves may name.</summary>
    internal static class ClassIds
    {
        internal static Type TypeOf(int classId)
        {
            switch (classId)
            {
                case 1: return typeof(GameObject);
                case 2: return typeof(Component);
                case 4: return typeof(Transform);
                case 8: return typeof(Behaviour);
                case 25: return typeof(Renderer);
                case 114: return typeof(MonoBehaviour);
                case 224: return typeof(RectTransform);
            }
            return EngineTypes.ForClassId(classId, $"class {classId}");
        }
    }

    /// <summary>Reads an AnimationClip document.</summary>
    internal static class ClipReader
    {
        static readonly string[] s_Xyz = { "x", "y", "z" };
        static readonly string[] s_Xyzw = { "x", "y", "z", "w" };

        internal static void Read(AnimationClip clip, YNode body, IReferenceResolver resolver)
        {
            clip.m_Legacy = body.Bool("m_Legacy");
            clip.m_SampleRate = body.Float("m_SampleRate", 60f);
            clip.m_WrapMode = (WrapMode)body.Int("m_WrapMode");
            var bounds = body.Get("m_Bounds");
            if (bounds != null) clip.m_Bounds = new Bounds(bounds.Vector3("m_Center"), bounds.Vector3("m_Extent") * 2f);

            clip.m_Curves.Clear();
            clip.m_ObjectCurves.Clear();
            foreach (var item in Items(body, "m_PositionCurves"))
                AddVectorCurves(clip, item, "m_LocalPosition", s_Xyz, quaternion: false);
            foreach (var item in Items(body, "m_RotationCurves"))
                AddVectorCurves(clip, item, "m_LocalRotation", s_Xyzw, quaternion: true);
            foreach (var item in Items(body, "m_EulerCurves"))
                AddVectorCurves(clip, item, "localEulerAnglesRaw", s_Xyz, quaternion: false);
            foreach (var item in Items(body, "m_ScaleCurves"))
                AddVectorCurves(clip, item, "m_LocalScale", s_Xyz, quaternion: false);
            foreach (var item in Items(body, "m_FloatCurves"))
            {
                var curveNode = item.Get("curve");
                if (curveNode == null) continue;
                clip.m_Curves.Add(new ClipCurve
                {
                    Binding = BindingOf(item, item.Str("attribute")),
                    Curve = BuiltinReaders.AnimationCurve(curveNode),
                });
            }
            foreach (var item in Items(body, "m_PPtrCurves"))
            {
                var keys = item.Get("curve")?.Seq() ?? Array.Empty<YNode>();
                var times = new List<float>();
                var values = new List<Object>();
                foreach (var key in keys)
                {
                    times.Add(key.Float("time"));
                    var reference = key.Ref("value");
                    values.Add(reference.IsNull ? null : resolver.Resolve(reference, typeof(Object)));
                }
                clip.m_ObjectCurves.Add(new ClipObjectCurve
                {
                    Binding = BindingOf(item, item.Str("attribute")),
                    Times = times.ToArray(),
                    Values = values.ToArray(),
                });
            }

            var humanAvatar = body.Ref("m_UnboundHumanAvatar");
            if (!humanAvatar.IsNull) clip.m_HumanAvatar = resolver.Resolve(humanAvatar, typeof(Avatar)) as Avatar;

            var muscle = body.Get("m_MuscleClip");
            if (muscle != null && body.Int("m_MuscleClipSize") > 0) PlayerClip.Read(clip, body, muscle, resolver);
            else if (muscle != null) clip.m_LoopTime = muscle.Bool("m_LoopTime");
            if (body.Get("m_HasGenericRootTransform") != null && body.Get("m_AnimationClipSettings") == null)
                clip.m_GenericRoot = body.Bool("m_HasGenericRootTransform");

            var settings = body.Get("m_AnimationClipSettings");
            if (settings != null)
            {
                clip.m_StartTime = settings.Float("m_StartTime");
                clip.m_StopTime = settings.Float("m_StopTime");
                clip.m_HasRange = clip.m_StopTime > clip.m_StartTime || !HasKeysAfter(clip, clip.m_StartTime);
                clip.m_LoopTime = settings.Bool("m_LoopTime");
                clip.m_CycleOffset = settings.Float("m_CycleOffset");
                clip.m_LoopBlend = settings.Bool("m_LoopBlend");
                clip.m_HasAdditiveReferencePose = settings.Bool("m_HasAdditiveReferencePose");
                var reference = settings.Ref("m_AdditiveReferencePoseClip");
                clip.m_AdditiveReferencePoseClip = reference.IsNull ? null : resolver.Resolve(reference, typeof(AnimationClip)) as AnimationClip;
                clip.m_AdditiveReferencePoseTime = settings.Float("m_AdditiveReferencePoseTime");
                clip.m_Mirror = settings.Bool("m_Mirror");
                clip.m_OrientationOffsetY = settings.Float("m_OrientationOffsetY");
                clip.m_Level = settings.Float("m_Level");
                clip.m_LoopBlendOrientation = settings.Bool("m_LoopBlendOrientation");
                clip.m_LoopBlendPositionY = settings.Bool("m_LoopBlendPositionY");
                clip.m_LoopBlendPositionXZ = settings.Bool("m_LoopBlendPositionXZ");
                clip.m_KeepOriginalOrientation = settings.Bool("m_KeepOriginalOrientation");
                clip.m_KeepOriginalPositionY = settings.Get("m_KeepOriginalPositionY") == null || settings.Bool("m_KeepOriginalPositionY");
                clip.m_KeepOriginalPositionXZ = settings.Bool("m_KeepOriginalPositionXZ");
                clip.m_HeightFromFeet = settings.Bool("m_HeightFromFeet");
            }
            // Imported clips are NotEditable, a take's preview clip also HideInHierarchy.
            clip.hideFlags = (HideFlags)body.Int("m_ObjectHideFlags");

            clip.m_Events.Clear();
            foreach (var e in Items(body, "m_Events"))
            {
                var reference = e.Ref("objectReferenceParameter");
                clip.m_Events.Add(new AnimationEvent
                {
                    m_Time = e.Float("time"),
                    m_FunctionName = e.Str("functionName"),
                    m_StringParameter = e.Str("data"),
                    m_ObjectReferenceParameter = reference.IsNull ? null : resolver.Resolve(reference, typeof(Object)),
                    m_FloatParameter = e.Float("floatParameter"),
                    m_IntParameter = e.Int("intParameter"),
                    m_MessageOptions = e.Int("messageOptions"),
                });
            }
            clip.m_Events.Sort((a, b) => a.m_Time.CompareTo(b.m_Time));
            clip.m_Version++;
        }

        static bool HasKeysAfter(AnimationClip clip, float time)
        {
            foreach (var c in clip.m_Curves)
                if (c.LastKeyTime > time) return true;
            return false;
        }

        static IEnumerable<YNode> Items(YNode body, string key) => body.Get(key)?.Seq() ?? Array.Empty<YNode>();

        static CurveBinding BindingOf(YNode item, string attribute) => new CurveBinding
        {
            Path = item.Str("path"),
            Attribute = attribute,
            ClassId = item.Get("classID") != null ? item.Int("classID") : EngineTypes.TransformId,
            Script = item.Ref("script"),
        };

        /// <summary>A curve of Vector3 or Quaternion keys becomes one float curve per component.</summary>
        static void AddVectorCurves(AnimationClip clip, YNode item, string attribute, string[] components, bool quaternion)
        {
            var curve = item.Get("curve");
            if (curve == null) return;
            var keys = curve.Get("m_Curve")?.Seq() ?? Array.Empty<YNode>();
            var pre = InfinityOf(curve.Int("m_PreInfinity", 2));
            var post = InfinityOf(curve.Int("m_PostInfinity", 2));
            var path = item.Str("path");
            for (int c = 0; c < components.Length; c++)
            {
                var name = components[c];
                var frames = new List<Keyframe>();
                foreach (var k in keys)
                {
                    var key = new Keyframe(k.Float("time"), Component(k, "value", name, 0f), Component(k, "inSlope", name, 0f),
                        Component(k, "outSlope", name, 0f), Component(k, "inWeight", name, 1f / 3f), Component(k, "outWeight", name, 1f / 3f));
                    key.weightedMode = (WeightedMode)k.Int("weightedMode");
                    key.tangentModeInternal = k.Int("tangentMode");
                    frames.Add(key);
                }
                clip.m_Curves.Add(new ClipCurve
                {
                    Binding = new CurveBinding { Path = path, Attribute = attribute + "." + name, ClassId = EngineTypes.TransformId },
                    Curve = new AnimationCurve(frames.ToArray()) { preWrapMode = pre, postWrapMode = post },
                    QuaternionStart = quaternion && c == 0,
                    EulerStart = attribute == "localEulerAnglesRaw" && c == 0 && components.Length == 3,
                });
            }
        }

        static float Component(YNode key, string field, string component, float fallback)
        {
            var node = key.Get(field);
            if (node == null) return fallback;
            if (node.Kind == YKind.Scalar) return node.AsFloat(fallback);
            return node.Float(component, fallback);
        }

        /// <summary>Curves store their infinity modes as 0 ping-pong, 1 loop, 2 clamp.</summary>
        static WrapMode InfinityOf(int infinity) =>
            infinity == 0 ? WrapMode.PingPong : infinity == 1 ? WrapMode.Loop : WrapMode.ClampForever;
    }
}
