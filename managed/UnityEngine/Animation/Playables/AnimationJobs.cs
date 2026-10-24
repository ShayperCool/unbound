using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Unbound.Runtime.Playables;
using UnityEngine;
using Unity.Collections;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace Unbound.Runtime.Playables
{
    /// <summary>
    /// The handles an Animator's animation jobs bound: stream transforms and properties (channels of
    /// the Animator's animation bindings) and scene transforms and properties (read from the scene).
    /// </summary>
    internal sealed class AnimatorHandles
    {
        internal struct StreamTransform
        {
            internal Transform Transform;
            internal int Position, Rotation, Scale;
        }

        internal struct StreamProperty
        {
            internal int Channel;
            internal bool IsObject;
        }

        internal readonly Animator Animator;
        internal readonly uint Id;
        internal readonly List<StreamTransform> Transforms = new List<StreamTransform>();
        internal readonly List<StreamProperty> Properties = new List<StreamProperty>();
        internal readonly List<Transform> SceneTransforms = new List<Transform>();
        internal readonly List<PropertyGroup> SceneProperties = new List<PropertyGroup>();

        static readonly List<AnimatorHandles> s_ById = new List<AnimatorHandles> { null, null };

        AnimatorHandles(Animator animator)
        {
            Animator = animator;
            Id = (uint)s_ById.Count;
            s_ById.Add(this);
        }

        internal static AnimatorHandles Of(Animator animator) => animator.m_StreamHandles ??= new AnimatorHandles(animator);

        internal static AnimatorHandles Get(uint id) => id < s_ById.Count ? s_ById[(int)id] : null;

        /// <summary>The path of a transform below the Animator ("" for its own), or null.</summary>
        internal string PathOf(Transform t)
        {
            var root = Animator.transform;
            var parts = new List<string>();
            for (var p = t; p != null; p = p.m_Parent)
            {
                if (p == root) return string.Join("/", parts);
                parts.Insert(0, p.m_GameObject.m_Name);
            }
            return null;
        }
    }

    /// <summary>An animation stream as jobs see it: the stream, the Animator's handles, its inputs.</summary>
    internal sealed class StreamContext
    {
        internal AnimStream Stream;
        internal AnimatorHandles Handles;
        internal readonly List<(StreamContext Context, float Weight)> Inputs = new List<(StreamContext, float)>();
        internal int Id;

        static readonly List<StreamContext> s_Active = new List<StreamContext> { null };

        internal static StreamContext Open(AnimStream stream, AnimatorHandles handles)
        {
            var c = new StreamContext { Stream = stream, Handles = handles, Id = s_Active.Count };
            s_Active.Add(c);
            return c;
        }

        internal static void Close(StreamContext c)
        {
            foreach (var (input, _) in c.Inputs) Close(input);
            if (c.Id > 0 && c.Id < s_Active.Count && s_Active[c.Id] == c) s_Active[c.Id] = null;
            while (s_Active.Count > 1 && s_Active[s_Active.Count - 1] == null) s_Active.RemoveAt(s_Active.Count - 1);
        }

        internal static StreamContext Get(int id) => id > 0 && id < s_Active.Count ? s_Active[id] : null;

        internal UnityEngine.Animations.AnimationStream ToStream() =>
            new UnityEngine.Animations.AnimationStream { m_AnimatorBindingsVersion = Handles.Id, m_Context = Id };

        // ------------------------------------------------------------------ transforms in the stream

        internal Vector3 LocalPosition(Transform t, int channel) =>
            channel >= 0 ? new Vector3(Stream.Values[channel], Stream.Values[channel + 1], Stream.Values[channel + 2]) : t.localPosition;

        internal Quaternion LocalRotation(Transform t, int channel) =>
            channel >= 0 ? AnimationEvaluation.Q(Stream.Values, channel).normalized : t.localRotation;

        internal Vector3 LocalScale(Transform t, int channel) =>
            channel >= 0 ? new Vector3(Stream.Values[channel], Stream.Values[channel + 1], Stream.Values[channel + 2]) : t.localScale;

        /// <summary>A transform's local TRS as the stream has it (scene values where it does not).</summary>
        internal void Local(Transform t, out Vector3 p, out Quaternion r, out Vector3 s)
        {
            var b = Stream.Bindings;
            p = LocalPosition(t, b.ChannelOf(t, "position"));
            r = LocalRotation(t, b.ChannelOf(t, "rotation"));
            s = LocalScale(t, b.ChannelOf(t, "scale"));
        }

        /// <summary>Local to world through the stream's values up to the Animator, the scene above.</summary>
        internal Matrix4x4 LocalToWorld(Transform t)
        {
            var root = Handles.Animator.transform;
            var m = Matrix4x4.identity;
            var p = t;
            while (p != null)
            {
                Local(p, out var pos, out var rot, out var scale);
                m = Matrix4x4.TRS(pos, rot, scale) * m;
                if (p == root)
                {
                    if (p.m_Parent != null) m = p.m_Parent.localToWorldMatrix * m;
                    break;
                }
                p = p.m_Parent;
            }
            return m;
        }

        internal void GlobalTR(Transform t, out Vector3 position, out Quaternion rotation)
        {
            var root = Handles.Animator.transform;
            position = Vector3.zero;
            rotation = Quaternion.identity;
            var scale = Vector3.one;
            // Accumulate from the transform up: world = parent * local.
            var chain = new List<Transform>();
            for (var p = t; p != null; p = p.m_Parent)
            {
                chain.Add(p);
                if (p == root) break;
            }
            var top = chain[chain.Count - 1];
            Vector3 wp = top.m_Parent != null ? top.m_Parent.position : Vector3.zero;
            Quaternion wr = top.m_Parent != null ? top.m_Parent.rotation : Quaternion.identity;
            Vector3 ws = top.m_Parent != null ? top.m_Parent.lossyScale : Vector3.one;
            for (int i = chain.Count - 1; i >= 0; i--)
            {
                Local(chain[i], out var lp, out var lr, out var ls);
                wp = wp + wr * Vector3.Scale(ws, lp);
                wr = wr * lr;
                ws = Vector3.Scale(ws, ls);
            }
            position = wp;
            rotation = wr;
        }

        internal void Write(int channel, Vector3 v)
        {
            if (channel < 0) return;
            Stream.Values[channel] = v.x; Stream.Values[channel + 1] = v.y; Stream.Values[channel + 2] = v.z;
            Stream.Animated[channel] = Stream.Animated[channel + 1] = Stream.Animated[channel + 2] = true;
        }

        internal void Write(int channel, Quaternion q)
        {
            if (channel < 0) return;
            AnimationEvaluation.SetQ(Stream.Values, channel, q);
            for (int i = 0; i < 4; i++) Stream.Animated[channel + i] = true;
        }

        internal void ParentGlobalTR(Transform t, out Vector3 position, out Quaternion rotation)
        {
            var parent = t.m_Parent;
            if (parent == null)
            {
                position = Vector3.zero;
                rotation = Quaternion.identity;
                return;
            }
            if (t == Handles.Animator.transform)
            {
                position = parent.position;
                rotation = parent.rotation;
                return;
            }
            GlobalTR(parent, out position, out rotation);
        }
    }

    /// <summary>An AnimationScriptPlayable: runs its job on the stream of its first input (or the
    /// stream it starts from), synchronously, as part of the evaluation.</summary>
    internal sealed class JobKind : AnimationKind
    {
        internal object Job;
        internal bool ProcessInputs = true;

        internal override void Evaluate(AnimationEvaluation e, PlayableNode node, int viaPort, float weight, AnimStream into)
        {
            if (e.ReferencePose) return;
            var handles = AnimatorHandles.Of(e.Animator);
            var context = StreamContext.Open(into, handles);
            try
            {
                for (int i = 0; i < node.Inputs.Count; i++)
                {
                    var input = node.Inputs[i];
                    if (input.Node == null) continue;
                    if (i == 0 && ProcessInputs)
                    {
                        e.EvaluateInto(input.Node, input.SourcePort, weight * input.Weight, into);
                        context.Inputs.Add((context, input.Weight));
                        continue;
                    }
                    if (!ProcessInputs) continue;
                    var s = e.Evaluate(input.Node, input.SourcePort, weight * input.Weight);
                    context.Inputs.Add((StreamContext.Open(s, handles), input.Weight));
                    into.Inputs.Add((s, input.Weight));
                }
                if (Job is UnityEngine.Animations.IAnimationJob job)
                {
                    var stream = context.ToStream();
                    job.ProcessRootMotion(stream);
                    job.ProcessAnimation(stream);
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
            finally
            {
                foreach (var (input, _) in context.Inputs)
                    if (input != context) StreamContext.Close(input);
                context.Inputs.Clear();
                StreamContext.Close(context);
            }
        }
    }

    /// <summary>A stream property bound by name only (AnimatorJobExtensions.BindCustomStreamProperty):
    /// a value jobs hand to each other within a frame, written nowhere.</summary>
    internal sealed class CustomGroup : PropertyGroup
    {
        float m_Value;

        internal CustomGroup(Object owner)
        {
            Target = owner;
            Width = 1;
        }

        internal override void Read(float[] values, int offset) => values[offset] = m_Value;

        internal override bool Write(float[] values, int offset)
        {
            m_Value = values[offset];
            return false;
        }
    }
}

namespace UnityEngine.Animations
{
    public partial interface IAnimationJob
    {
        void ProcessAnimation(AnimationStream stream);

        void ProcessRootMotion(AnimationStream stream);
    }

    public partial interface IAnimationJobPlayable : IPlayable
    {
        T GetJobData<T>() where T : struct, IAnimationJob;

        void SetJobData<T>(T jobData) where T : struct, IAnimationJob;
    }

    public enum CustomStreamPropertyType
    {
        Float = 5,
        Bool = 6,
        Int = 10,
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Field)]
    public partial class NotKeyableAttribute : Attribute
    {
        public NotKeyableAttribute() { }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public partial class DiscreteEvaluationAttribute : Attribute
    {
        public DiscreteEvaluationAttribute() { }
    }

    /// <summary>A playable running an animation job (IAnimationJob) on the animation stream.</summary>
    public partial struct AnimationScriptPlayable : IAnimationJobPlayable, IPlayable, IEquatable<AnimationScriptPlayable>
    {
        PlayableHandle m_Handle;

        static readonly AnimationScriptPlayable m_NullPlayable = new AnimationScriptPlayable(PlayableHandle.Null);

        public static AnimationScriptPlayable Null => m_NullPlayable;

        public static AnimationScriptPlayable Create<T>(PlayableGraph graph, T jobData, int inputCount = 0) where T : struct, IAnimationJob
        {
            var kind = new JobKind { Job = jobData };
            var handle = AnimationPlayableUtil.Create(graph, typeof(AnimationScriptPlayable), kind, inputCount);
            var n = PlayableGraphData.Get(handle);
            if (n != null) n.JobType = typeof(T);
            return new AnimationScriptPlayable(handle);
        }

        internal AnimationScriptPlayable(PlayableHandle handle)
        {
            AnimationPlayableUtil.Check<AnimationScriptPlayable>(handle, "AnimationScriptPlayable");
            m_Handle = handle;
        }

        public PlayableHandle GetHandle() => m_Handle;

        JobKind Kind => AnimationPlayableUtil.Kind<JobKind>(m_Handle);

        void CheckJobType<T>()
        {
            var type = m_Handle.GetJobType();
            if (type != null && type != typeof(T))
                throw new ArgumentException($"Wrong type: the given job type ({typeof(T).FullName}) is different from the creation job type ({type.FullName}).");
        }

        public T GetJobData<T>() where T : struct, IAnimationJob
        {
            CheckJobType<T>();
            return Kind.Job is T t ? t : default;
        }

        public void SetJobData<T>(T jobData) where T : struct, IAnimationJob
        {
            CheckJobType<T>();
            Kind.Job = jobData;
        }

        public static implicit operator Playable(AnimationScriptPlayable playable) => new Playable(playable.GetHandle());

        public static explicit operator AnimationScriptPlayable(Playable playable) => new AnimationScriptPlayable(playable.GetHandle());

        public bool Equals(AnimationScriptPlayable other) => GetHandle() == other.GetHandle();

        public void SetProcessInputs(bool value) => Kind.ProcessInputs = value;

        public bool GetProcessInputs() => Kind.ProcessInputs;
    }

    /// <summary>The animation stream an animation job reads and writes.</summary>
    public partial struct AnimationStream
    {
        internal uint m_AnimatorBindingsVersion;
        internal int m_Context;

        internal const int InvalidIndex = -1;

        internal uint animatorBindingsVersion => m_AnimatorBindingsVersion;

        internal StreamContext Context => StreamContext.Get(m_Context);

        public bool isValid => m_AnimatorBindingsVersion >= 2 && Context != null;

        internal void CheckIsValid()
        {
            if (!isValid) throw new InvalidOperationException("The AnimationStream is invalid.");
        }

        public float deltaTime
        {
            get
            {
                CheckIsValid();
                return Context.Stream.DeltaTime;
            }
        }

        public Vector3 velocity
        {
            get => Vector3.zero;
            set { }
        }

        public Vector3 angularVelocity
        {
            get => Vector3.zero;
            set { }
        }

        public Vector3 rootMotionPosition => Vector3.zero;

        public Quaternion rootMotionRotation => Quaternion.identity;

        public bool isHumanStream => false;

        public int inputStreamCount
        {
            get
            {
                CheckIsValid();
                return Context.Inputs.Count;
            }
        }

        public AnimationStream GetInputStream(int index)
        {
            CheckIsValid();
            var inputs = Context.Inputs;
            if (index < 0 || index >= inputs.Count) return default;
            return inputs[index].Context.ToStream();
        }

        public float GetInputWeight(int index)
        {
            CheckIsValid();
            var inputs = Context.Inputs;
            return index >= 0 && index < inputs.Count ? inputs[index].Weight : 0f;
        }

        public void CopyAnimationStreamMotion(AnimationStream animationStream) { }
    }

    /// <summary>A transform in the animation stream.</summary>
    public partial struct TransformStreamHandle
    {
        internal uint m_AnimatorBindingsVersion;
        internal int handleIndex;

        internal uint animatorBindingsVersion => m_AnimatorBindingsVersion;

        bool Data(AnimationStream stream, out StreamContext context, out AnimatorHandles.StreamTransform data)
        {
            context = stream.Context;
            data = default;
            if (context == null) throw new InvalidOperationException("The AnimationStream is invalid.");
            if (m_AnimatorBindingsVersion != context.Handles.Id || handleIndex < 0 || handleIndex >= context.Handles.Transforms.Count)
                throw new InvalidOperationException("The TransformStreamHandle is invalid. Please use proper function to create the handle.");
            data = context.Handles.Transforms[handleIndex];
            return data.Transform != null;
        }

        public bool IsValid(AnimationStream stream) =>
            stream.Context is StreamContext c && m_AnimatorBindingsVersion == c.Handles.Id && handleIndex >= 0 && handleIndex < c.Handles.Transforms.Count;

        public void Resolve(AnimationStream stream) { }

        public bool IsResolved(AnimationStream stream) => IsValid(stream);

        public Vector3 GetPosition(AnimationStream stream)
        {
            if (!Data(stream, out var c, out var d)) return Vector3.zero;
            c.GlobalTR(d.Transform, out var p, out _);
            return p;
        }

        public void SetPosition(AnimationStream stream, Vector3 position)
        {
            if (!Data(stream, out var c, out var d)) return;
            c.ParentGlobalTR(d.Transform, out var pp, out var pr);
            var parentScale = ParentScale(c, d.Transform);
            var local = Quaternion.Inverse(pr) * (position - pp);
            c.Write(d.Position, new Vector3(Div(local.x, parentScale.x), Div(local.y, parentScale.y), Div(local.z, parentScale.z)));
        }

        public Quaternion GetRotation(AnimationStream stream)
        {
            if (!Data(stream, out var c, out var d)) return Quaternion.identity;
            c.GlobalTR(d.Transform, out _, out var r);
            return r;
        }

        public void SetRotation(AnimationStream stream, Quaternion rotation)
        {
            if (!Data(stream, out var c, out var d)) return;
            c.ParentGlobalTR(d.Transform, out _, out var pr);
            c.Write(d.Rotation, Quaternion.Inverse(pr) * rotation);
        }

        public Vector3 GetLocalPosition(AnimationStream stream) => Data(stream, out var c, out var d) ? c.LocalPosition(d.Transform, d.Position) : Vector3.zero;

        public void SetLocalPosition(AnimationStream stream, Vector3 position)
        {
            if (Data(stream, out var c, out var d)) c.Write(d.Position, position);
        }

        public Quaternion GetLocalRotation(AnimationStream stream) => Data(stream, out var c, out var d) ? c.LocalRotation(d.Transform, d.Rotation) : Quaternion.identity;

        public void SetLocalRotation(AnimationStream stream, Quaternion rotation)
        {
            if (Data(stream, out var c, out var d)) c.Write(d.Rotation, rotation);
        }

        public Vector3 GetLocalScale(AnimationStream stream) => Data(stream, out var c, out var d) ? c.LocalScale(d.Transform, d.Scale) : Vector3.one;

        public void SetLocalScale(AnimationStream stream, Vector3 scale)
        {
            if (Data(stream, out var c, out var d)) c.Write(d.Scale, scale);
        }

        public Matrix4x4 GetLocalToParentMatrix(AnimationStream stream)
        {
            if (!Data(stream, out var c, out var d)) return Matrix4x4.identity;
            return Matrix4x4.TRS(c.LocalPosition(d.Transform, d.Position), c.LocalRotation(d.Transform, d.Rotation), c.LocalScale(d.Transform, d.Scale));
        }

        public bool GetPositionReadMask(AnimationStream stream) => Data(stream, out var c, out var d) && d.Position >= 0;

        public bool GetRotationReadMask(AnimationStream stream) => Data(stream, out var c, out var d) && d.Rotation >= 0;

        public bool GetScaleReadMask(AnimationStream stream) => Data(stream, out var c, out var d) && d.Scale >= 0;

        public void GetLocalTRS(AnimationStream stream, out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            if (!Data(stream, out var c, out var d))
            {
                position = Vector3.zero;
                rotation = Quaternion.identity;
                scale = Vector3.one;
                return;
            }
            position = c.LocalPosition(d.Transform, d.Position);
            rotation = c.LocalRotation(d.Transform, d.Rotation);
            scale = c.LocalScale(d.Transform, d.Scale);
        }

        public void SetLocalTRS(AnimationStream stream, Vector3 position, Quaternion rotation, Vector3 scale, bool useMask)
        {
            if (!Data(stream, out var c, out var d)) return;
            if (!useMask || (d.Position >= 0)) c.Write(d.Position, position);
            if (!useMask || (d.Rotation >= 0)) c.Write(d.Rotation, rotation);
            if (!useMask || (d.Scale >= 0)) c.Write(d.Scale, scale);
        }

        public void GetGlobalTR(AnimationStream stream, out Vector3 position, out Quaternion rotation)
        {
            if (!Data(stream, out var c, out var d))
            {
                position = Vector3.zero;
                rotation = Quaternion.identity;
                return;
            }
            c.GlobalTR(d.Transform, out position, out rotation);
        }

        public Matrix4x4 GetLocalToWorldMatrix(AnimationStream stream) => Data(stream, out var c, out var d) ? c.LocalToWorld(d.Transform) : Matrix4x4.identity;

        public void SetGlobalTR(AnimationStream stream, Vector3 position, Quaternion rotation, bool useMask)
        {
            if (!Data(stream, out var c, out var d)) return;
            c.ParentGlobalTR(d.Transform, out var pp, out var pr);
            var parentScale = ParentScale(c, d.Transform);
            var local = Quaternion.Inverse(pr) * (position - pp);
            if (!useMask || (d.Position >= 0))
                c.Write(d.Position, new Vector3(Div(local.x, parentScale.x), Div(local.y, parentScale.y), Div(local.z, parentScale.z)));
            if (!useMask || (d.Rotation >= 0))
                c.Write(d.Rotation, Quaternion.Inverse(pr) * rotation);
        }

        static float Div(float a, float b) => Math.Abs(b) > 1e-12f ? a / b : a;

        static Vector3 ParentScale(StreamContext c, Transform t)
        {
            var parent = t.m_Parent;
            if (parent == null) return Vector3.one;
            if (t == c.Handles.Animator.transform) return parent.lossyScale;
            var scale = Vector3.one;
            var root = c.Handles.Animator.transform;
            for (var p = parent; p != null; p = p.m_Parent)
            {
                c.Local(p, out _, out _, out var s);
                scale = Vector3.Scale(scale, s);
                if (p == root)
                {
                    if (p.m_Parent != null) scale = Vector3.Scale(scale, p.m_Parent.lossyScale);
                    break;
                }
            }
            return scale;
        }
    }

    /// <summary>A property (a script's field, an enabled flag) in the animation stream.</summary>
    public partial struct PropertyStreamHandle
    {
        internal uint m_AnimatorBindingsVersion;
        internal int handleIndex;

        internal uint animatorBindingsVersion => m_AnimatorBindingsVersion;

        bool Channel(AnimationStream stream, out StreamContext context, out int channel)
        {
            context = stream.Context;
            channel = -1;
            if (context == null) throw new InvalidOperationException("The AnimationStream is invalid.");
            if (m_AnimatorBindingsVersion != context.Handles.Id || handleIndex < 0 || handleIndex >= context.Handles.Properties.Count)
                throw new InvalidOperationException("The PropertyStreamHandle is invalid. Please use proper function to create the handle.");
            var p = context.Handles.Properties[handleIndex];
            channel = p.Channel;
            return channel >= 0 && !p.IsObject && channel < context.Stream.FloatCount;
        }

        public bool IsValid(AnimationStream stream) =>
            stream.Context is StreamContext c && m_AnimatorBindingsVersion == c.Handles.Id && handleIndex >= 0 && handleIndex < c.Handles.Properties.Count;

        public void Resolve(AnimationStream stream) { }

        public bool IsResolved(AnimationStream stream) => IsValid(stream);

        public float GetFloat(AnimationStream stream) => Channel(stream, out var c, out int ch) ? c.Stream.Values[ch] : 0f;

        public void SetFloat(AnimationStream stream, float value)
        {
            if (!Channel(stream, out var c, out int ch)) return;
            c.Stream.Values[ch] = value;
            c.Stream.Animated[ch] = true;
        }

        public int GetInt(AnimationStream stream) => (int)Math.Round(GetFloat(stream));

        public void SetInt(AnimationStream stream, int value) => SetFloat(stream, value);

        public bool GetBool(AnimationStream stream) => GetFloat(stream) >= 0.5f;

        public void SetBool(AnimationStream stream, bool value) => SetFloat(stream, value ? 1f : 0f);

        public bool GetReadMask(AnimationStream stream) => Channel(stream, out _, out _);
    }

    /// <summary>A transform read from the scene during an animation job.</summary>
    public partial struct TransformSceneHandle
    {
        internal uint valid;
        internal int transformSceneHandleDefinitionIndex;

        Transform Transform(AnimationStream stream)
        {
            var handles = AnimatorHandles.Get(valid);
            if (handles == null || transformSceneHandleDefinitionIndex < 0 || transformSceneHandleDefinitionIndex >= handles.SceneTransforms.Count)
                throw new InvalidOperationException("The TransformSceneHandle is invalid. Please use proper function to create the handle.");
            return handles.SceneTransforms[transformSceneHandleDefinitionIndex];
        }

        public bool IsValid(AnimationStream stream) =>
            AnimatorHandles.Get(valid) is AnimatorHandles h && transformSceneHandleDefinitionIndex >= 0 && transformSceneHandleDefinitionIndex < h.SceneTransforms.Count;

        public Vector3 GetPosition(AnimationStream stream) => Transform(stream)?.position ?? Vector3.zero;

        [Obsolete("SceneHandle is now read-only; it was problematic with the engine multithreading and determinism")]
        public void SetPosition(AnimationStream stream, Vector3 position) { }

        public Vector3 GetLocalPosition(AnimationStream stream) => Transform(stream)?.localPosition ?? Vector3.zero;

        [Obsolete("SceneHandle is now read-only; it was problematic with the engine multithreading and determinism")]
        public void SetLocalPosition(AnimationStream stream, Vector3 position) { }

        public Quaternion GetRotation(AnimationStream stream) => Transform(stream)?.rotation ?? Quaternion.identity;

        [Obsolete("SceneHandle is now read-only; it was problematic with the engine multithreading and determinism")]
        public void SetRotation(AnimationStream stream, Quaternion rotation) { }

        public Quaternion GetLocalRotation(AnimationStream stream) => Transform(stream)?.localRotation ?? Quaternion.identity;

        [Obsolete("SceneHandle is now read-only; it was problematic with the engine multithreading and determinism")]
        public void SetLocalRotation(AnimationStream stream, Quaternion rotation) { }

        public Vector3 GetLocalScale(AnimationStream stream) => Transform(stream)?.localScale ?? Vector3.one;

        [Obsolete("SceneHandle is now read-only; it was problematic with the engine multithreading and determinism")]
        public void SetLocalScale(AnimationStream stream, Vector3 scale) { }

        public void GetLocalTRS(AnimationStream stream, out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            var t = Transform(stream);
            position = t != null ? t.localPosition : Vector3.zero;
            rotation = t != null ? t.localRotation : Quaternion.identity;
            scale = t != null ? t.localScale : Vector3.one;
        }

        public Matrix4x4 GetLocalToParentMatrix(AnimationStream stream)
        {
            var t = Transform(stream);
            return t != null ? Matrix4x4.TRS(t.localPosition, t.localRotation, t.localScale) : Matrix4x4.identity;
        }

        public void GetGlobalTR(AnimationStream stream, out Vector3 position, out Quaternion rotation)
        {
            var t = Transform(stream);
            position = t != null ? t.position : Vector3.zero;
            rotation = t != null ? t.rotation : Quaternion.identity;
        }

        public Matrix4x4 GetLocalToWorldMatrix(AnimationStream stream) => Transform(stream)?.localToWorldMatrix ?? Matrix4x4.identity;
    }

    /// <summary>A property read from the scene during an animation job.</summary>
    public partial struct PropertySceneHandle
    {
        internal uint valid;
        internal int handleIndex;

        float Value(AnimationStream stream)
        {
            var handles = AnimatorHandles.Get(valid);
            if (handles == null || handleIndex < 0 || handleIndex >= handles.SceneProperties.Count)
                throw new InvalidOperationException("The PropertySceneHandle is invalid. Please use proper function to create the handle.");
            var g = handles.SceneProperties[handleIndex];
            if (g == null || !g.Alive || g.IsObject) return 0f;
            var buffer = new float[Math.Max(1, g.Width)];
            g.Read(buffer, 0);
            return buffer[0];
        }

        public bool IsValid(AnimationStream stream) =>
            AnimatorHandles.Get(valid) is AnimatorHandles h && handleIndex >= 0 && handleIndex < h.SceneProperties.Count;

        public void Resolve(AnimationStream stream) { }

        public bool IsResolved(AnimationStream stream) => IsValid(stream);

        public float GetFloat(AnimationStream stream) => Value(stream);

        [Obsolete("SceneHandle is now read-only; it was problematic with the engine multithreading and determinism")]
        public void SetFloat(AnimationStream stream, float value) { }

        public int GetInt(AnimationStream stream) => (int)Math.Round(Value(stream));

        [Obsolete("SceneHandle is now read-only; it was problematic with the engine multithreading and determinism")]
        public void SetInt(AnimationStream stream, int value) { }

        public bool GetBool(AnimationStream stream) => Value(stream) >= 0.5f;

        [Obsolete("SceneHandle is now read-only; it was problematic with the engine multithreading and determinism")]
        public void SetBool(AnimationStream stream, bool value) { }
    }

    public static partial class AnimatorJobExtensions
    {
        public static TransformStreamHandle BindStreamTransform(this Animator animator, Transform transform)
        {
            if (animator == null) throw new ArgumentNullException(nameof(animator));
            if (transform == null) throw new ArgumentNullException(nameof(transform));
            var handles = AnimatorHandles.Of(animator);
            var path = handles.PathOf(transform);
            if (path == null) throw new InvalidOperationException($"The transform '{transform.name}' is not in the hierarchy of the Animator '{animator.name}'.");
            var b = animator.StreamBindings;
            handles.Transforms.Add(new AnimatorHandles.StreamTransform
            {
                Transform = transform,
                Position = b.BindChannel(CurveBinding.ForType(path, "m_LocalPosition.x", typeof(Transform)), false),
                Rotation = b.BindChannel(CurveBinding.ForType(path, "m_LocalRotation.x", typeof(Transform)), false),
                Scale = b.BindChannel(CurveBinding.ForType(path, "m_LocalScale.x", typeof(Transform)), false),
            });
            return new TransformStreamHandle { m_AnimatorBindingsVersion = handles.Id, handleIndex = handles.Transforms.Count - 1 };
        }

        public static PropertyStreamHandle BindStreamProperty(this Animator animator, Transform transform, Type type, string property) =>
            BindStreamProperty(animator, transform, type, property, false);

        public static PropertyStreamHandle BindStreamProperty(this Animator animator, Transform transform, Type type, string property, bool isObjectReference)
        {
            if (animator == null) throw new ArgumentNullException(nameof(animator));
            if (transform == null) throw new ArgumentNullException(nameof(transform));
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (string.IsNullOrEmpty(property)) throw new ArgumentNullException(nameof(property));
            var handles = AnimatorHandles.Of(animator);
            var path = handles.PathOf(transform);
            if (path == null) throw new InvalidOperationException($"The transform '{transform.name}' is not in the hierarchy of the Animator '{animator.name}'.");
            int channel = animator.StreamBindings.BindChannel(CurveBinding.ForType(path, property, type), isObjectReference);
            handles.Properties.Add(new AnimatorHandles.StreamProperty { Channel = channel, IsObject = isObjectReference });
            return new PropertyStreamHandle { m_AnimatorBindingsVersion = handles.Id, handleIndex = handles.Properties.Count - 1 };
        }

        public static PropertyStreamHandle BindCustomStreamProperty(this Animator animator, string property, CustomStreamPropertyType type)
        {
            if (animator == null) throw new ArgumentNullException(nameof(animator));
            var handles = AnimatorHandles.Of(animator);
            int channel = animator.StreamBindings.BindCustom(animator, property);
            handles.Properties.Add(new AnimatorHandles.StreamProperty { Channel = channel });
            return new PropertyStreamHandle { m_AnimatorBindingsVersion = handles.Id, handleIndex = handles.Properties.Count - 1 };
        }

        public static TransformSceneHandle BindSceneTransform(this Animator animator, Transform transform)
        {
            if (animator == null) throw new ArgumentNullException(nameof(animator));
            if (transform == null) throw new ArgumentNullException(nameof(transform));
            var handles = AnimatorHandles.Of(animator);
            handles.SceneTransforms.Add(transform);
            return new TransformSceneHandle { valid = handles.Id, transformSceneHandleDefinitionIndex = handles.SceneTransforms.Count - 1 };
        }

        public static PropertySceneHandle BindSceneProperty(this Animator animator, Transform transform, Type type, string property) =>
            BindSceneProperty(animator, transform, type, property, false);

        public static PropertySceneHandle BindSceneProperty(this Animator animator, Transform transform, Type type, string property, bool isObjectReference)
        {
            if (animator == null) throw new ArgumentNullException(nameof(animator));
            if (transform == null) throw new ArgumentNullException(nameof(transform));
            var handles = AnimatorHandles.Of(animator);
            var spec = PropertyBinder.Resolve(transform.gameObject, CurveBinding.ForType("", property, type), isObjectReference, null);
            handles.SceneProperties.Add(spec.Factory?.Invoke());
            return new PropertySceneHandle { valid = handles.Id, handleIndex = handles.SceneProperties.Count - 1 };
        }

        /// <summary>A stream over the Animator's current values, for jobs run outside a graph;
        /// closing it writes what they changed.</summary>
        public static bool OpenAnimationStream(this Animator animator, ref AnimationStream stream)
        {
            if (animator == null) throw new ArgumentNullException(nameof(animator));
            var b = animator.StreamBindings;
            var s = AnimStream.Rent(b);
            foreach (var g in b.Groups)
            {
                if (!g.Alive) continue;
                if (g.IsObject) s.Objects[g.First] = g.ReadObject();
                else g.Read(s.Values, g.First);
            }
            var context = StreamContext.Open(s, AnimatorHandles.Of(animator));
            stream = context.ToStream();
            return true;
        }

        public static void CloseAnimationStream(this Animator animator, ref AnimationStream stream)
        {
            var context = stream.Context;
            if (context == null) return;
            var s = context.Stream;
            PoseWriter.WriteStream(s.Bindings, s.Values, s.Animated, s.Objects, s.ObjectAnimated);
            StreamContext.Close(context);
            AnimStream.Return(s);
            stream = default;
        }

        public static void ResolveAllStreamHandles(this Animator animator) { }

        public static void ResolveAllSceneHandles(this Animator animator) { }

        internal static void UnbindAllHandles(this Animator animator)
        {
            UnbindAllStreamHandles(animator);
            UnbindAllSceneHandles(animator);
        }

        public static void UnbindAllStreamHandles(this Animator animator)
        {
            if (animator?.m_StreamHandles == null) return;
            animator.m_StreamHandles.Transforms.Clear();
            animator.m_StreamHandles.Properties.Clear();
        }

        public static void UnbindAllSceneHandles(this Animator animator)
        {
            if (animator?.m_StreamHandles == null) return;
            animator.m_StreamHandles.SceneTransforms.Clear();
            animator.m_StreamHandles.SceneProperties.Clear();
        }
    }

    public static partial class AnimationStreamHandleUtility
    {
        public static void WriteInts(AnimationStream stream, NativeArray<PropertyStreamHandle> handles, NativeArray<int> buffer, bool useMask)
        {
            int n = Math.Min(handles.Length, buffer.Length);
            for (int i = 0; i < n; i++)
                if (!useMask || handles[i].GetReadMask(stream)) handles[i].SetInt(stream, buffer[i]);
        }

        public static void WriteFloats(AnimationStream stream, NativeArray<PropertyStreamHandle> handles, NativeArray<float> buffer, bool useMask)
        {
            int n = Math.Min(handles.Length, buffer.Length);
            for (int i = 0; i < n; i++)
                if (!useMask || handles[i].GetReadMask(stream)) handles[i].SetFloat(stream, buffer[i]);
        }

        public static void ReadInts(AnimationStream stream, NativeArray<PropertyStreamHandle> handles, NativeArray<int> buffer)
        {
            int n = Math.Min(handles.Length, buffer.Length);
            for (int i = 0; i < n; i++) buffer[i] = handles[i].GetInt(stream);
        }

        public static void ReadFloats(AnimationStream stream, NativeArray<PropertyStreamHandle> handles, NativeArray<float> buffer)
        {
            int n = Math.Min(handles.Length, buffer.Length);
            for (int i = 0; i < n; i++) buffer[i] = handles[i].GetFloat(stream);
        }
    }

    public static partial class AnimationSceneHandleUtility
    {
        public static void ReadInts(AnimationStream stream, NativeArray<PropertySceneHandle> handles, NativeArray<int> buffer)
        {
            int n = Math.Min(handles.Length, buffer.Length);
            for (int i = 0; i < n; i++) buffer[i] = handles[i].GetInt(stream);
        }

        public static void ReadFloats(AnimationStream stream, NativeArray<PropertySceneHandle> handles, NativeArray<float> buffer)
        {
            int n = Math.Min(handles.Length, buffer.Length);
            for (int i = 0; i < n; i++) buffer[i] = handles[i].GetFloat(stream);
        }
    }
}
