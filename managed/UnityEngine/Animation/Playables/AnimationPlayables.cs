using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Unbound.Runtime.Playables;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace Unbound.Runtime.Playables
{
    /// <summary>An AnimationClipPlayable: samples its clip at its time (wrapped when the clip loops,
    /// clamped otherwise) and fires the clip's events the time crosses during playback.</summary>
    internal sealed class ClipKind : AnimationKind
    {
        internal AnimationClip Clip;
        internal bool ApplyFootIK = true;
        internal bool ApplyPlayableIK;
        internal bool RemoveStartOffset = true;
        internal bool OverrideLoopTime;
        internal bool LoopTime;
        internal float SampleRate;
        readonly AnimationPose m_Pose = new AnimationPose();
        bool m_WarnedLegacy;
        // The step of time the last evaluation covered, for events.
        double m_From, m_To;
        float m_Weight;

        bool Loops => OverrideLoopTime ? LoopTime : Clip != null && Clip.isLooping;

        internal override bool CanChangeInputs => false;
        internal override bool CanSetWeights => false;
        internal override bool FinishesAtDuration => false;

        double ClipTime(double t)
        {
            float length = Clip.length;
            if (length <= 0f) return Clip.StartTime;
            double local = Loops ? t - Math.Floor(t / length) * length : Math.Clamp(t, 0.0, length);
            return Clip.StartTime + local;
        }

        internal override void Bind(AnimationEvaluation e, PlayableNode node)
        {
            if (Clip == null) return;
            if (Clip.m_Legacy)
            {
                if (!m_WarnedLegacy)
                {
                    m_WarnedLegacy = true;
                    Debug.LogWarning($"The AnimationClip '{Clip.name}' used by an AnimationClipPlayable must not be legacy.", Clip);
                }
                return;
            }
            e.Bindings.Bind(Clip);
        }

        internal override void Evaluate(AnimationEvaluation e, PlayableNode node, int viaPort, float weight, AnimStream into)
        {
            if (Clip == null || Clip.m_Legacy) return;
            var bound = e.Bindings.Bind(Clip);
            double t = e.ReferencePose ? 0.0 : node.Time;
            m_Pose.Reset(e.Bindings.FloatCount, e.Bindings.ObjectCount);
            ClipSampling.Sample(bound, (float)ClipTime(t), 1f, m_Pose);
            int n = Math.Min(into.FloatCount, e.Bindings.FloatCount);
            for (int c = 0; c < n; c++)
            {
                float w = m_Pose.Weight[c];
                if (w <= 0f) continue;
                into.Values[c] = m_Pose.Value[c] / w;
                into.Animated[c] = true;
            }
            // Under root motion playables the root starts where the clip's first frame is removed.
            if (e.RootMotion && RemoveStartOffset) RemoveStart(e, bound, into);
            int m = Math.Min(into.ObjectCount, e.Bindings.ObjectCount);
            for (int c = 0; c < m; c++)
            {
                if (m_Pose.ObjectWeight[c] <= 0f) continue;
                into.Objects[c] = m_Pose.Objects[c];
                into.ObjectAnimated[c] = true;
            }
            if (e.ReferencePose || Clip.m_Events.Count == 0 || weight <= 0f) return;
            // Events fire for the time playback moved through (not for seeks).
            if ((node.FrameFlags & FrameData.Flags.SeekOccured) != 0 || node.PreviousTime == node.Time) return;
            m_From = node.PreviousTime;
            m_To = node.Time;
            m_Weight = weight;
            e.Note(this, node);
        }

        readonly AnimationPose m_StartPose = new AnimationPose();

        /// <summary>The root's position and rotation relative to the clip's first frame.</summary>
        void RemoveStart(AnimationEvaluation e, BoundClip bound, AnimStream into)
        {
            var b = e.Bindings;
            var root = b.Root != null ? b.Root.transform : null;
            if (root == null) return;
            int pos = b.ChannelOf(root, "position"), rot = b.ChannelOf(root, "rotation");
            if ((pos < 0 || !into.Animated[pos]) && (rot < 0 || !into.Animated[rot])) return;
            m_StartPose.Reset(b.FloatCount, b.ObjectCount);
            ClipSampling.Sample(bound, Clip.StartTime, 1f, m_StartPose);
            var startRot = Quaternion.identity;
            if (rot >= 0 && m_StartPose.Weight[rot] > 0f)
                startRot = new Quaternion(m_StartPose.Value[rot], m_StartPose.Value[rot + 1], m_StartPose.Value[rot + 2], m_StartPose.Value[rot + 3]).normalized;
            var inverse = Quaternion.Inverse(startRot);
            if (pos >= 0 && into.Animated[pos] && m_StartPose.Weight[pos] > 0f)
            {
                var start = new Vector3(m_StartPose.Value[pos], m_StartPose.Value[pos + 1], m_StartPose.Value[pos + 2]);
                var p = inverse * (new Vector3(into.Values[pos], into.Values[pos + 1], into.Values[pos + 2]) - start);
                into.Values[pos] = p.x; into.Values[pos + 1] = p.y; into.Values[pos + 2] = p.z;
            }
            if (rot >= 0 && into.Animated[rot])
                AnimationEvaluation.SetQ(into.Values, rot, inverse * AnimationEvaluation.Q(into.Values, rot));
        }

        internal override void AfterWrite(AnimationEvaluation e, PlayableNode node)
        {
            var animator = e.Animator;
            if (animator == null || !animator.m_FireEvents || Clip == null) return;
            float length = Clip.length;
            if (length <= 0f) return;
            var go = animator.m_GameObject;
            var clip = Clip;
            var clipInfo = new AnimatorClipInfo { m_Clip = clip, m_Weight = m_Weight };
            AnimationEvents.FireCrossed(clip, m_From / length, m_To / length, Loops, false, ev =>
            {
                var fired = ev.Copy();
                fired.m_Source = AnimationEventSource.Animator;
                fired.m_AnimatorClipInfo = clipInfo;
                AnimationEvents.Dispatch(go, fired, clip);
            });
        }
    }

    /// <summary>An AnimationMixerPlayable: its inputs summed by weight, the remainder of weight one
    /// taken by the default values (channel by channel, over the inputs animating the channel).</summary>
    internal sealed class MixerKind : AnimationKind
    {
        internal override void Evaluate(AnimationEvaluation e, PlayableNode node, int viaPort, float weight, AnimStream into) =>
            Mix(e, node, weight, into, 0);

        internal static void Mix(AnimationEvaluation e, PlayableNode node, float weight, AnimStream into, int depth)
        {
            var inputs = new List<(AnimStream, float)>(node.Inputs.Count);
            foreach (var i in node.Inputs)
            {
                if (i.Node == null) continue;
                var s = AnimStream.Rent(e.Bindings);
                s.DeltaTime = e.DeltaTime;
                e.EvaluateInto(i.Node, i.SourcePort, weight * i.Weight, s, depth + 1);
                inputs.Add((s, i.Weight));
            }
            AnimationEvaluation.MixInto(into, inputs);
            foreach (var (s, _) in inputs) AnimStream.Return(s);
        }
    }

    /// <summary>An AnimationLayerMixerPlayable: each layer over the ones below, for the channels it
    /// animates and its mask allows; additive layers add their difference to their first frame.</summary>
    internal sealed class LayerMixerKind : AnimationKind
    {
        internal readonly List<AvatarMask> Masks = new List<AvatarMask>();
        internal readonly List<bool> Additive = new List<bool>();
        internal bool SingleLayerOptimization = true;
        readonly Dictionary<int, (AvatarMask Mask, int Count, bool[] Channels)> m_MaskCache = new Dictionary<int, (AvatarMask, int, bool[])>();

        internal void Ensure(int layer)
        {
            while (Masks.Count <= layer) Masks.Add(null);
            while (Additive.Count <= layer) Additive.Add(false);
        }

        bool[] MaskChannels(AnimationEvaluation e, int layer)
        {
            var mask = layer < Masks.Count ? Masks[layer] : null;
            if (mask == null || mask.m_Elements.Count == 0) return null;
            if (m_MaskCache.TryGetValue(layer, out var cached) && cached.Mask == mask && cached.Count == e.Bindings.FloatCount) return cached.Channels;
            var b = e.Bindings;
            var channels = new bool[b.FloatCount];
            var root = b.Root != null ? b.Root.transform : null;
            for (int c = 0; c < channels.Length; c++)
            {
                var t = b.FloatOwner[c]?.Transform;
                if (t == null || root == null) { channels[c] = true; continue; }
                var parts = new List<string>();
                for (var p = t; p != null && p != root; p = p.m_Parent) parts.Insert(0, p.m_GameObject.m_Name);
                channels[c] = mask.Allows(string.Join("/", parts));
            }
            m_MaskCache[layer] = (mask, b.FloatCount, channels);
            return channels;
        }

        internal override void Evaluate(AnimationEvaluation e, PlayableNode node, int viaPort, float weight, AnimStream into)
        {
            for (int l = 0; l < node.Inputs.Count; l++)
            {
                var input = node.Inputs[l];
                if (input.Node == null) continue;
                var s = AnimStream.Rent(e.Bindings);
                s.DeltaTime = e.DeltaTime;
                e.EvaluateInto(input.Node, input.SourcePort, weight * input.Weight, s);
                var mask = MaskChannels(e, l);
                if (l < Additive.Count && Additive[l])
                {
                    bool was = e.ReferencePose;
                    e.ReferencePose = true;
                    var reference = AnimStream.Rent(e.Bindings);
                    e.EvaluateInto(input.Node, input.SourcePort, 0f, reference);
                    e.ReferencePose = was;
                    AnimationEvaluation.AddOver(into, s, reference, input.Weight, mask);
                    AnimStream.Return(reference);
                }
                else AnimationEvaluation.BlendOver(into, s, input.Weight, mask);
                AnimStream.Return(s);
            }
        }
    }

    /// <summary>An AnimatorControllerPlayable: a controller's state machines, advanced in the
    /// prepare pass and sampled into the stream. Its player animates the Animator of the first
    /// animation output that reaches it.</summary>
    internal sealed class ControllerKind : AnimationKind
    {
        internal RuntimeAnimatorController Controller;
        internal AnimatorPlayer Player;
        internal PlayableNode Node;

        internal override bool CanChangeInputs => false;
        internal override bool CanSetWeights => false;
        internal override bool FinishesAtDuration => false;

        /// <summary>The player, made for the Animator an animation output of the graph targets.</summary>
        internal AnimatorPlayer EnsurePlayer()
        {
            if (Player != null || Controller == null || Node == null) return Player;
            foreach (var o in Node.Graph.Outputs)
            {
                var target = (o.Engine as AnimationOutputState)?.Target;
                if (target != null) return EnsurePlayer(target);
            }
            return null;
        }

        internal AnimatorPlayer EnsurePlayer(Animator animator)
        {
            if (Player != null || Controller == null || animator == null) return Player;
            var graph = Controller.Graph;
            if (graph == null) return null;
            Player = new AnimatorPlayer(animator, Controller, graph, animator.StreamBindings)
            {
                Playable = new UnityEngine.Animations.AnimatorControllerPlayable(Node.ToHandle()),
            };
            Player.InstantiateBehaviours();
            return Player;
        }

        internal override void Prepare(PlayableNode node, in FrameData info)
        {
            EnsurePlayer()?.StepLayers(info.deltaTime * info.effectiveSpeed);
        }

        internal override void Bind(AnimationEvaluation e, PlayableNode node) => EnsurePlayer(e.Animator);

        internal override void Evaluate(AnimationEvaluation e, PlayableNode node, int viaPort, float weight, AnimStream into)
        {
            var player = EnsurePlayer(e.Animator);
            if (player == null || player.Bindings != e.Bindings || e.ReferencePose) return;
            player.EvaluateInto(into.Values, into.Animated, into.Objects, into.ObjectAnimated);
            e.Note(this, node);
        }

        internal override void AfterWrite(AnimationEvaluation e, PlayableNode node) => Player?.AfterEvaluate();
    }

    /// <summary>An AnimationOffsetPlayable: its input with the root's position and rotation offset.</summary>
    internal sealed class OffsetKind : AnimationKind
    {
        internal Vector3 Position;
        internal Quaternion Rotation = Quaternion.identity;

        internal override void Evaluate(AnimationEvaluation e, PlayableNode node, int viaPort, float weight, AnimStream into)
        {
            if (node.Inputs.Count > 0 && node.Inputs[0].Node != null)
                e.EvaluateInto(node.Inputs[0].Node, node.Inputs[0].SourcePort, weight, into);
            if (Position == Vector3.zero && Rotation == Quaternion.identity) return;
            var b = e.Bindings;
            int pos = b.BindChannel(CurveBinding.ForType("", "m_LocalPosition.x", typeof(Transform)), false);
            int rot = b.BindChannel(CurveBinding.ForType("", "m_LocalRotation.x", typeof(Transform)), false);
            if (pos >= 0 && pos + 2 < into.FloatCount && into.Animated[pos])
            {
                var p = Rotation * new Vector3(into.Values[pos], into.Values[pos + 1], into.Values[pos + 2]) + Position;
                into.Values[pos] = p.x; into.Values[pos + 1] = p.y; into.Values[pos + 2] = p.z;
            }
            if (rot >= 0 && rot + 3 < into.FloatCount && into.Animated[rot])
                AnimationEvaluation.SetQ(into.Values, rot, Rotation * AnimationEvaluation.Q(into.Values, rot));
        }
    }

    /// <summary>Playables that pass their first input on (motion-to-delta, remove-scale: Unbound
    /// animates generic hierarchies without root motion, where these change nothing).</summary>
    internal sealed class PassKind : AnimationKind
    {
        internal bool AbsoluteMotion;
        /// <summary>A motion-to-delta playable: clips below it are root motion (their start offset
        /// removed); relative motion continues from where the root was when bound.</summary>
        internal bool Motion;

        internal override void Evaluate(AnimationEvaluation e, PlayableNode node, int viaPort, float weight, AnimStream into)
        {
            if (node.Inputs.Count == 0 || node.Inputs[0].Node == null) return;
            bool was = e.RootMotion;
            if (Motion) e.RootMotion = true;
            e.EvaluateInto(node.Inputs[0].Node, node.Inputs[0].SourcePort, weight, into);
            e.RootMotion = was;
            if (!Motion || AbsoluteMotion) return;
            var b = e.Bindings;
            var root = b.Root != null ? b.Root.transform : null;
            if (root == null) return;
            int pos = b.ChannelOf(root, "position"), rot = b.ChannelOf(root, "rotation");
            var baseRot = rot >= 0 ? AnimationEvaluation.Q(b.Defaults, rot) : Quaternion.identity;
            if (pos >= 0 && into.Animated[pos])
            {
                var p = new Vector3(b.Defaults[pos], b.Defaults[pos + 1], b.Defaults[pos + 2]) + baseRot * new Vector3(into.Values[pos], into.Values[pos + 1], into.Values[pos + 2]);
                into.Values[pos] = p.x; into.Values[pos + 1] = p.y; into.Values[pos + 2] = p.z;
            }
            if (rot >= 0 && into.Animated[rot])
                AnimationEvaluation.SetQ(into.Values, rot, baseRot * AnimationEvaluation.Q(into.Values, rot));
        }
    }

    /// <summary>An AnimationPosePlayable: the default pose (or the current one).</summary>
    internal sealed class PoseKind : AnimationKind
    {
        internal bool MustReadPreviousPose;
        internal bool ReadDefaultPose;
        internal bool ApplyFootIK;

        internal override void Evaluate(AnimationEvaluation e, PlayableNode node, int viaPort, float weight, AnimStream into)
        {
            if (!MustReadPreviousPose) return;
            foreach (var g in e.Bindings.Groups)
            {
                if (!g.Alive || g.IsObject) continue;
                g.Read(into.Values, g.First);
                for (int i = 0; i < g.Width; i++) into.Animated[g.First + i] = true;
            }
        }
    }

    internal static class AnimationPlayableUtil
    {
        internal static PlayableHandle Create(PlayableGraph graph, Type type, PlayableKind kind, int inputCount, bool propagateSetTime = false)
        {
            var g = PlayableGraphData.Get(graph) ?? throw new InvalidOperationException("The PlayableGraph is invalid.");
            var n = g.CreateNode(type);
            n.Kind = kind;
            n.PropagateSetTime = propagateSetTime;
            g.SetInputCount(n, inputCount);
            AnimationOutputs.EnsureRegistered();
            return n.ToHandle();
        }

        internal static T Kind<T>(PlayableHandle handle) where T : PlayableKind =>
            PlayableGraphData.Get(handle)?.Kind as T ?? throw new InvalidOperationException("The playable is invalid or not of type " + typeof(T).Name + ".");

        internal static void Check<T>(PlayableHandle handle, string name)
        {
            if (handle.IsValid() && !handle.IsPlayableOfType<T>())
                throw new InvalidCastException("Can't set handle: the playable is not an " + name + ".");
        }
    }
}

namespace UnityEngine.Animations
{
    public partial struct AnimationClipPlayable : IPlayable, IEquatable<AnimationClipPlayable>
    {
        PlayableHandle m_Handle;

        public static AnimationClipPlayable Create(PlayableGraph graph, AnimationClip clip)
        {
            var kind = new ClipKind { Clip = clip, SampleRate = clip != null ? clip.frameRate : 60f };
            return new AnimationClipPlayable(AnimationPlayableUtil.Create(graph, typeof(AnimationClipPlayable), kind, 0));
        }

        internal AnimationClipPlayable(PlayableHandle handle)
        {
            AnimationPlayableUtil.Check<AnimationClipPlayable>(handle, "AnimationClipPlayable");
            m_Handle = handle;
        }

        public PlayableHandle GetHandle() => m_Handle;

        public static implicit operator Playable(AnimationClipPlayable playable) => new Playable(playable.GetHandle());

        public static explicit operator AnimationClipPlayable(Playable playable) => new AnimationClipPlayable(playable.GetHandle());

        public bool Equals(AnimationClipPlayable other) => GetHandle() == other.GetHandle();

        ClipKind Kind => AnimationPlayableUtil.Kind<ClipKind>(m_Handle);

        public AnimationClip GetAnimationClip() => Kind.Clip;

        public bool GetApplyFootIK() => Kind.ApplyFootIK;

        public void SetApplyFootIK(bool value) => Kind.ApplyFootIK = value;

        public bool GetApplyPlayableIK() => Kind.ApplyPlayableIK;

        public void SetApplyPlayableIK(bool value) => Kind.ApplyPlayableIK = value;

        internal bool GetRemoveStartOffset() => Kind.RemoveStartOffset;

        internal void SetRemoveStartOffset(bool value) => Kind.RemoveStartOffset = value;

        internal bool GetOverrideLoopTime() => Kind.OverrideLoopTime;

        internal void SetOverrideLoopTime(bool value) => Kind.OverrideLoopTime = value;

        internal bool GetLoopTime() => Kind.LoopTime;

        internal void SetLoopTime(bool value) => Kind.LoopTime = value;

        internal float GetSampleRate() => Kind.SampleRate;

        internal void SetSampleRate(float value) => Kind.SampleRate = value;
    }

    public partial struct AnimationMixerPlayable : IPlayable, IEquatable<AnimationMixerPlayable>
    {
        PlayableHandle m_Handle;

        static readonly AnimationMixerPlayable m_NullPlayable = new AnimationMixerPlayable(PlayableHandle.Null);

        public static AnimationMixerPlayable Null => m_NullPlayable;

        [Obsolete("normalizeWeights is obsolete. It has no effect and will be removed.")]
        public static AnimationMixerPlayable Create(PlayableGraph graph, int inputCount, bool normalizeWeights) => Create(graph, inputCount);

        public static AnimationMixerPlayable Create(PlayableGraph graph, int inputCount = 0) =>
            new AnimationMixerPlayable(AnimationPlayableUtil.Create(graph, typeof(AnimationMixerPlayable), new MixerKind(), inputCount));

        internal AnimationMixerPlayable(PlayableHandle handle)
        {
            AnimationPlayableUtil.Check<AnimationMixerPlayable>(handle, "AnimationMixerPlayable");
            m_Handle = handle;
        }

        public PlayableHandle GetHandle() => m_Handle;

        public static implicit operator Playable(AnimationMixerPlayable playable) => new Playable(playable.GetHandle());

        public static explicit operator AnimationMixerPlayable(Playable playable) => new AnimationMixerPlayable(playable.GetHandle());

        public bool Equals(AnimationMixerPlayable other) => GetHandle() == other.GetHandle();
    }

    public partial struct AnimationLayerMixerPlayable : IPlayable, IEquatable<AnimationLayerMixerPlayable>
    {
        PlayableHandle m_Handle;

        static readonly AnimationLayerMixerPlayable m_NullPlayable = new AnimationLayerMixerPlayable(PlayableHandle.Null);

        public static AnimationLayerMixerPlayable Null => m_NullPlayable;

        public static AnimationLayerMixerPlayable Create(PlayableGraph graph, int inputCount = 0) => Create(graph, inputCount, true);

        public static AnimationLayerMixerPlayable Create(PlayableGraph graph, int inputCount, bool singleLayerOptimization)
        {
            var kind = new LayerMixerKind { SingleLayerOptimization = singleLayerOptimization };
            return new AnimationLayerMixerPlayable(AnimationPlayableUtil.Create(graph, typeof(AnimationLayerMixerPlayable), kind, inputCount), singleLayerOptimization);
        }

        internal AnimationLayerMixerPlayable(PlayableHandle handle, bool singleLayerOptimization = true)
        {
            AnimationPlayableUtil.Check<AnimationLayerMixerPlayable>(handle, "AnimationLayerMixerPlayable");
            m_Handle = handle;
        }

        public PlayableHandle GetHandle() => m_Handle;

        public static implicit operator Playable(AnimationLayerMixerPlayable playable) => new Playable(playable.GetHandle());

        public static explicit operator AnimationLayerMixerPlayable(Playable playable) => new AnimationLayerMixerPlayable(playable.GetHandle());

        public bool Equals(AnimationLayerMixerPlayable other) => GetHandle() == other.GetHandle();

        LayerMixerKind Kind => AnimationPlayableUtil.Kind<LayerMixerKind>(m_Handle);

        void CheckLayer(uint layerIndex)
        {
            if (layerIndex >= m_Handle.GetInputCount())
                throw new ArgumentOutOfRangeException(nameof(layerIndex), $"layerIndex {layerIndex} must be in the range of 0 to {m_Handle.GetInputCount() - 1}.");
        }

        public bool IsLayerAdditive(uint layerIndex)
        {
            CheckLayer(layerIndex);
            var k = Kind;
            return layerIndex < k.Additive.Count && k.Additive[(int)layerIndex];
        }

        public void SetLayerAdditive(uint layerIndex, bool value)
        {
            CheckLayer(layerIndex);
            var k = Kind;
            k.Ensure((int)layerIndex);
            k.Additive[(int)layerIndex] = value;
        }

        public void SetLayerMaskFromAvatarMask(uint layerIndex, AvatarMask mask)
        {
            CheckLayer(layerIndex);
            if (mask == null) throw new ArgumentNullException(nameof(mask));
            var k = Kind;
            k.Ensure((int)layerIndex);
            // The mask is copied, as Unity does.
            var copy = new AvatarMask();
            copy.m_Elements.AddRange(mask.m_Elements);
            k.Masks[(int)layerIndex] = copy;
        }
    }

    internal partial struct AnimationOffsetPlayable : IPlayable, IEquatable<AnimationOffsetPlayable>
    {
        PlayableHandle m_Handle;

        static readonly AnimationOffsetPlayable m_NullPlayable = new AnimationOffsetPlayable(PlayableHandle.Null);

        public static AnimationOffsetPlayable Null => m_NullPlayable;

        public static AnimationOffsetPlayable Create(PlayableGraph graph, Vector3 position, Quaternion rotation, int inputCount) =>
            new AnimationOffsetPlayable(AnimationPlayableUtil.Create(graph, typeof(AnimationOffsetPlayable), new OffsetKind { Position = position, Rotation = rotation }, inputCount, propagateSetTime: true));

        internal AnimationOffsetPlayable(PlayableHandle handle)
        {
            AnimationPlayableUtil.Check<AnimationOffsetPlayable>(handle, "AnimationOffsetPlayable");
            m_Handle = handle;
        }

        public PlayableHandle GetHandle() => m_Handle;

        public static implicit operator Playable(AnimationOffsetPlayable playable) => new Playable(playable.GetHandle());

        public static explicit operator AnimationOffsetPlayable(Playable playable) => new AnimationOffsetPlayable(playable.GetHandle());

        public bool Equals(AnimationOffsetPlayable other) => GetHandle() == other.GetHandle();

        public Vector3 GetPosition() => AnimationPlayableUtil.Kind<OffsetKind>(m_Handle).Position;

        public void SetPosition(Vector3 value) => AnimationPlayableUtil.Kind<OffsetKind>(m_Handle).Position = value;

        public Quaternion GetRotation() => AnimationPlayableUtil.Kind<OffsetKind>(m_Handle).Rotation;

        public void SetRotation(Quaternion value) => AnimationPlayableUtil.Kind<OffsetKind>(m_Handle).Rotation = value;
    }

    internal partial struct AnimationMotionXToDeltaPlayable : IPlayable, IEquatable<AnimationMotionXToDeltaPlayable>
    {
        PlayableHandle m_Handle;

        static readonly AnimationMotionXToDeltaPlayable m_NullPlayable = new AnimationMotionXToDeltaPlayable(PlayableHandle.Null);

        public static AnimationMotionXToDeltaPlayable Null => m_NullPlayable;

        public static AnimationMotionXToDeltaPlayable Create(PlayableGraph graph) =>
            new AnimationMotionXToDeltaPlayable(AnimationPlayableUtil.Create(graph, typeof(AnimationMotionXToDeltaPlayable), new PassKind { Motion = true }, 1));

        AnimationMotionXToDeltaPlayable(PlayableHandle handle)
        {
            AnimationPlayableUtil.Check<AnimationMotionXToDeltaPlayable>(handle, "AnimationMotionXToDeltaPlayable");
            m_Handle = handle;
        }

        public PlayableHandle GetHandle() => m_Handle;

        public static implicit operator Playable(AnimationMotionXToDeltaPlayable playable) => new Playable(playable.GetHandle());

        public static explicit operator AnimationMotionXToDeltaPlayable(Playable playable) => new AnimationMotionXToDeltaPlayable(playable.GetHandle());

        public bool Equals(AnimationMotionXToDeltaPlayable other) => GetHandle() == other.GetHandle();

        public bool IsAbsoluteMotion() => AnimationPlayableUtil.Kind<PassKind>(m_Handle).AbsoluteMotion;

        public void SetAbsoluteMotion(bool value) => AnimationPlayableUtil.Kind<PassKind>(m_Handle).AbsoluteMotion = value;
    }

    internal partial struct AnimationRemoveScalePlayable : IPlayable, IEquatable<AnimationRemoveScalePlayable>
    {
        PlayableHandle m_Handle;

        static readonly AnimationRemoveScalePlayable m_NullPlayable = new AnimationRemoveScalePlayable(PlayableHandle.Null);

        public static AnimationRemoveScalePlayable Null => m_NullPlayable;

        public static AnimationRemoveScalePlayable Create(PlayableGraph graph, int inputCount) =>
            new AnimationRemoveScalePlayable(AnimationPlayableUtil.Create(graph, typeof(AnimationRemoveScalePlayable), new PassKind(), inputCount, propagateSetTime: true));

        internal AnimationRemoveScalePlayable(PlayableHandle handle)
        {
            AnimationPlayableUtil.Check<AnimationRemoveScalePlayable>(handle, "AnimationRemoveScalePlayable");
            m_Handle = handle;
        }

        public PlayableHandle GetHandle() => m_Handle;

        public static implicit operator Playable(AnimationRemoveScalePlayable playable) => new Playable(playable.GetHandle());

        public static explicit operator AnimationRemoveScalePlayable(Playable playable) => new AnimationRemoveScalePlayable(playable.GetHandle());

        public bool Equals(AnimationRemoveScalePlayable other) => GetHandle() == other.GetHandle();
    }

    internal partial struct AnimationPosePlayable : IPlayable, IEquatable<AnimationPosePlayable>
    {
        PlayableHandle m_Handle;

        static readonly AnimationPosePlayable m_NullPlayable = new AnimationPosePlayable(PlayableHandle.Null);

        public static AnimationPosePlayable Null => m_NullPlayable;

        public static AnimationPosePlayable Create(PlayableGraph graph) =>
            new AnimationPosePlayable(AnimationPlayableUtil.Create(graph, typeof(AnimationPosePlayable), new PoseKind(), 0));

        internal AnimationPosePlayable(PlayableHandle handle)
        {
            AnimationPlayableUtil.Check<AnimationPosePlayable>(handle, "AnimationPosePlayable");
            m_Handle = handle;
        }

        public PlayableHandle GetHandle() => m_Handle;

        public static implicit operator Playable(AnimationPosePlayable playable) => new Playable(playable.GetHandle());

        public static explicit operator AnimationPosePlayable(Playable playable) => new AnimationPosePlayable(playable.GetHandle());

        public bool Equals(AnimationPosePlayable other) => GetHandle() == other.GetHandle();

        PoseKind Kind => AnimationPlayableUtil.Kind<PoseKind>(m_Handle);

        public bool GetMustReadPreviousPose() => Kind.MustReadPreviousPose;

        public void SetMustReadPreviousPose(bool value) => Kind.MustReadPreviousPose = value;

        public bool GetReadDefaultPose() => Kind.ReadDefaultPose;

        public void SetReadDefaultPose(bool value) => Kind.ReadDefaultPose = value;

        public bool GetApplyFootIK() => Kind.ApplyFootIK;

        public void SetApplyFootIK(bool value) => Kind.ApplyFootIK = value;
    }

    public static partial class AnimationPlayableExtensions
    {
        /// <summary>Declares the properties a playable animates ahead of time (the Animator binds
        /// them); Unbound binds what clips animate when they are evaluated.</summary>
        public static void SetAnimatedProperties<U>(this U playable, AnimationClip clip) where U : struct, IPlayable
        {
            var handle = playable.GetHandle();
            SetAnimatedPropertiesInternal(ref handle, clip);
        }

        internal static void SetAnimatedPropertiesInternal(ref PlayableHandle playable, AnimationClip animatedProperties) { }
    }
}

namespace UnityEngine.Playables
{
    public static partial class AnimationPlayableUtilities
    {
        [Obsolete("This function is no longer used as it overrides the Time Update Mode of the Playable Graph. Refer to the documentation for an example of an equivalent function.")]
        public static void Play(Animator animator, Playable playable, PlayableGraph graph)
        {
            var output = UnityEngine.Animations.AnimationPlayableOutput.Create(graph, "AnimationClip", animator);
            output.SetSourcePlayable(playable, 0);
            graph.SyncUpdateAndTimeMode(animator);
            graph.Play();
        }

        public static UnityEngine.Animations.AnimationClipPlayable PlayClip(Animator animator, AnimationClip clip, out PlayableGraph graph)
        {
            graph = PlayableGraph.Create();
            var output = UnityEngine.Animations.AnimationPlayableOutput.Create(graph, "AnimationClip", animator);
            var playable = UnityEngine.Animations.AnimationClipPlayable.Create(graph, clip);
            return PlayOn(output, playable, animator, graph);
        }

        public static UnityEngine.Animations.AnimationMixerPlayable PlayMixer(Animator animator, int inputCount, out PlayableGraph graph)
        {
            graph = PlayableGraph.Create();
            var output = UnityEngine.Animations.AnimationPlayableOutput.Create(graph, "Mixer", animator);
            var playable = UnityEngine.Animations.AnimationMixerPlayable.Create(graph, inputCount);
            return PlayOn(output, playable, animator, graph);
        }

        public static UnityEngine.Animations.AnimationLayerMixerPlayable PlayLayerMixer(Animator animator, int inputCount, out PlayableGraph graph)
        {
            graph = PlayableGraph.Create();
            var output = UnityEngine.Animations.AnimationPlayableOutput.Create(graph, "Mixer", animator);
            var playable = UnityEngine.Animations.AnimationLayerMixerPlayable.Create(graph, inputCount);
            return PlayOn(output, playable, animator, graph);
        }

        public static UnityEngine.Animations.AnimatorControllerPlayable PlayAnimatorController(Animator animator, RuntimeAnimatorController controller, out PlayableGraph graph)
        {
            graph = PlayableGraph.Create();
            var output = UnityEngine.Animations.AnimationPlayableOutput.Create(graph, "AnimatorControllerPlayable", animator);
            var playable = UnityEngine.Animations.AnimatorControllerPlayable.Create(graph, controller);
            return PlayOn(output, playable, animator, graph);
        }

        static V PlayOn<V>(UnityEngine.Animations.AnimationPlayableOutput output, V playable, Animator animator, PlayableGraph graph) where V : struct, IPlayable
        {
            output.SetSourcePlayable(playable);
            graph.SyncUpdateAndTimeMode(animator);
            graph.Play();
            return playable;
        }
    }
}
