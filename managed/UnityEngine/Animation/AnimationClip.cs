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

        public float averageAngularSpeed
        {
            get
            {
                Unbound.Runtime.Stubs.Ignored("Motion.averageAngularSpeed", "is always 0 (clip root motion is not computed yet)");
                return 0f;
            }
        }

        public Vector3 averageSpeed
        {
            get
            {
                Unbound.Runtime.Stubs.Ignored("Motion.averageSpeed", "is always zero (clip root motion is not computed yet)");
                return Vector3.zero;
            }
        }

        public float apparentSpeed
        {
            get
            {
                Unbound.Runtime.Stubs.Ignored("Motion.apparentSpeed", "is always 0 (clip root motion is not computed yet)");
                return 0f;
            }
        }

        public bool isLooping => GetIsLooping();

        public bool legacy => GetLegacy();

        public bool isHumanMotion
        {
            get
            {
                Unbound.Runtime.Stubs.Ignored("Motion.isHumanMotion", "is always false (humanoid avatars are not supported yet)");
                return false;
            }
        }

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

        public bool humanMotion
        {
            get
            {
                Unbound.Runtime.Stubs.Ignored("AnimationClip.humanMotion", "is always false (humanoid avatars are not supported yet)");
                return false;
            }
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

        /// <summary>Root motion of a humanoid or root-node clip: none in generic clips.</summary>
        internal bool hasRootMotion => false;

        public bool hasMotionFloatCurves
        {
            get
            {
                Unbound.Runtime.Stubs.Ignored("AnimationClip.hasMotionFloatCurves", "is always false (root motion curves are not read yet)");
                return false;
            }
        }

        public bool hasMotionCurves
        {
            get
            {
                Unbound.Runtime.Stubs.Ignored("AnimationClip.hasMotionCurves", "is always false (root motion curves are not read yet)");
                return false;
            }
        }

        public bool hasRootCurves
        {
            get
            {
                Unbound.Runtime.Stubs.Ignored("AnimationClip.hasRootCurves", "is always false (root curves are not read yet)");
                return false;
            }
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

        public void EnsureQuaternionContinuity() =>
            Unbound.Runtime.Stubs.Ignored("AnimationClip.EnsureQuaternionContinuity", "does not flip quaternion keys yet");

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

        internal float Evaluate(float time) => Sampled != null ? Sampled.Evaluate(time) : Curve.Evaluate(time);

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
                int c = 4 * lo;
                return ((Coefficients[c] * dt + Coefficients[c + 1]) * dt + Coefficients[c + 2]) * dt + Coefficients[c + 3];
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
            }

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
