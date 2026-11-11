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
            /// <summary>The skeleton node the channels animate on an Animator without its
            /// transform hierarchy (-1: they animate <see cref="Transform"/>).</summary>
            internal int Node;
            /// <summary>A stream has read or written the handle, or resolved it (TransformStreamHandle.IsResolved).</summary>
            internal bool Resolved;
            /// <summary>The node of the avatar's human skeleton the handle reads and writes in a
            /// humanoid's stream: the hips or a node below them (0: none, the channels are
            /// its values), found at an access for the avatar the Animator has then
            /// (<see cref="HumanFor"/>).</summary>
            internal int Human;
            internal Unbound.Runtime.Humanoid.AvatarConstant HumanFor;
        }

        internal struct StreamProperty
        {
            internal int Channel;
            internal bool IsObject;
            /// <summary>A stream has read or written the handle, or resolved it (PropertyStreamHandle.IsResolved).</summary>
            internal bool Resolved;
        }

        internal readonly Animator Animator;
        internal readonly uint Id;
        internal readonly List<StreamTransform> Transforms = new List<StreamTransform>();
        /// <summary>The transforms of <see cref="Transforms"/> whose handle animates the transform
        /// itself and not a node of an optimized hierarchy's skeleton: the stream's own, which
        /// the skeleton no longer poses (AnimatorSkeleton.Apply).</summary>
        internal readonly HashSet<Transform> StreamOwned = new HashSet<Transform>();
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

        /// <summary>What the handle's channels animate: a skeleton node while the hierarchy is
        /// optimized and the handle's path is a node's (its transform is then the node's exposed
        /// copy, or gone), else the transform at its path.</summary>
        StreamTransform Target(StreamTransform handle)
        {
            int channel = handle.Position >= 0 ? handle.Position : handle.Rotation >= 0 ? handle.Rotation : handle.Scale;
            var bindings = Animator.StreamBindings;
            var owner = channel >= 0 && channel < bindings.FloatCount ? bindings.FloatOwner[channel] : null;
            handle.Node = owner is SkeletonGroup node ? node.Node : -1;
            if (handle.Node < 0 && owner?.Transform != null) handle.Transform = owner.Transform;
            if (handle.Node < 0 && owner != null && handle.Transform != null) StreamOwned.Add(handle.Transform);
            handle.Human = 0;
            handle.HumanFor = null;
            return handle;
        }

        internal void Bound(StreamTransform handle) => Transforms.Add(Target(handle));

        /// <summary>The hierarchy was optimized or deoptimized: the handles' channels moved to
        /// the skeleton's nodes or back to the transforms of their paths, and the handles are
        /// unresolved until their next access (Scripting API, TransformStreamHandle.IsResolved:
        /// a handle "can become unresolved if the animator bindings have changed"; 6000.0.71f1,
        /// tests/projects/animoptimize jobReoptimize.Rig.1).</summary>
        internal void HierarchyChanged()
        {
            StreamOwned.Clear();
            m_NodeChannelsFor = -1;
            for (int i = 0; i < Transforms.Count; i++)
            {
                var handle = Target(Transforms[i]);
                handle.Resolved = false;
                Transforms[i] = handle;
            }
            for (int i = 0; i < Properties.Count; i++)
            {
                var property = Properties[i];
                property.Resolved = false;
                Properties[i] = property;
            }
        }

        int[] m_NodeChannels = Array.Empty<int>();
        AnimationBindings m_NodeChannelsOf;
        int m_NodeChannelsFor = -1;

        /// <summary>The channels of a skeleton node's position, rotation and scale in
        /// <paramref name="bindings"/> (-1: not bound).</summary>
        internal void NodeChannels(AnimationBindings bindings, int node, out int position, out int rotation, out int scale)
        {
            if (!ReferenceEquals(m_NodeChannelsOf, bindings) || m_NodeChannelsFor != bindings.FloatCount) IndexNodes(bindings);
            int i = node * 3;
            bool known = node >= 0 && i + 2 < m_NodeChannels.Length;
            position = known ? m_NodeChannels[i] : -1;
            rotation = known ? m_NodeChannels[i + 1] : -1;
            scale = known ? m_NodeChannels[i + 2] : -1;
        }

        /// <summary>Bindings only grow between hierarchy changes, so their channel count dates the
        /// index. A node's Euler curves are not in it, as a transform's are not in the whole
        /// hierarchy's lookup: such a node is read from the skeleton's last written value.</summary>
        void IndexNodes(AnimationBindings bindings)
        {
            m_NodeChannelsOf = bindings;
            m_NodeChannelsFor = bindings.FloatCount;
            int count = (Animator.Skeleton?.Avatar.AvatarSkeleton.Count ?? 0) * 3;
            if (m_NodeChannels.Length != count) m_NodeChannels = new int[count];
            Array.Fill(m_NodeChannels, -1);
            foreach (var entry in bindings.Index)
            {
                if (!(entry.Value is SkeletonGroup group) || group.IsObject) continue;
                int slot = group.Property == TransformProperty.Position ? 0 : group.Property == TransformProperty.Rotation ? 1
                    : group.Property == TransformProperty.Scale ? 2 : -1;
                int i = group.Node * 3 + slot;
                if (slot >= 0 && i < count) m_NodeChannels[i] = group.First;
            }
        }

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
        /// <summary>The stream's root motion (AnimationStream.velocity, rootMotionPosition, ...).</summary>
        internal StreamMotion Motion = StreamMotion.None;
        /// <summary>The evaluation the stream belongs to.</summary>
        internal AnimationEvaluation Evaluation;
        Unbound.Runtime.Playables.HumanStreamPose m_Human;

        static readonly List<StreamContext> s_Active = new List<StreamContext> { null };

        internal static StreamContext Open(AnimStream stream, AnimatorHandles handles, AnimationEvaluation evaluation = null)
        {
            var c = new StreamContext { Stream = stream, Handles = handles, Id = s_Active.Count, Evaluation = evaluation };
            s_Active.Add(c);
            return c;
        }

        internal static void Close(StreamContext c)
        {
            foreach (var (input, _) in c.Inputs) Close(input);
            c.CloseHuman();
            if (c.Id > 0 && c.Id < s_Active.Count && s_Active[c.Id] == c) s_Active[c.Id] = null;
            while (s_Active.Count > 1 && s_Active[s_Active.Count - 1] == null) s_Active.RemoveAt(s_Active.Count - 1);
        }

        internal static StreamContext Get(int id) => id > 0 && id < s_Active.Count ? s_Active[id] : null;

        /// <summary>The stream's human pose (AnimationStream.AsHuman), opened on first use.</summary>
        internal Unbound.Runtime.Playables.HumanStreamPose HumanPose()
        {
            if (m_Human != null) return m_Human;
            using var engine = MainThreadCheck.Engine();
            var human = Handles.Animator?.Human ?? throw new InvalidOperationException("Cannot create an AnimationHumanStream for a generic rig.");
            if (Stream.HasMassCenter)
                Stubs.Ignored("AnimationHumanStream under a layer without Root",
                    "reads and writes the animated pose; the pose that places the body (layers whose mask keeps Root) is not part of it (not measured)");
            bool humanMotion = Evaluation != null && Evaluation.MotionMode == RootMotionMode.Human;
            var root = human.StreamRoot(humanMotion, Stream.BodyMotion, Stream.DeltaTime);
            m_Human = human.RentStreamPose();
            m_Human.Load(Stream, root);
            return m_Human;
        }

        /// <summary>The stream's human pose when the handle is one of its bones: a bone of the
        /// avatar the Animator has now (a handle bound before the avatar was assigned or replaced
        /// follows it). An optimized humanoid's handles stay with their skeleton nodes
        /// (unmeasured).</summary>
        internal bool HumanBone(int index, ref AnimatorHandles.StreamTransform handle, out Unbound.Runtime.Playables.HumanStreamPose pose)
        {
            pose = null;
            var animator = Handles.Animator;
            if (handle.Node >= 0 || animator == null) return false;
            using var engine = MainThreadCheck.Engine();
            if (!animator.IsHuman || handle.Transform == null) return false;
            var constant = animator.Human?.Constant;
            if (!ReferenceEquals(handle.HumanFor, constant))
            {
                var path = Handles.PathOf(handle.Transform);
                handle.Human = path == null ? 0 : Unbound.Runtime.Playables.HumanStreamPose.BoneNode(constant, path);
                handle.HumanFor = constant;
                Handles.Transforms[index] = handle;
            }
            if (handle.Human <= 0) return false;
            pose = HumanPose();
            return true;
        }

        /// <summary>The world position, rotation and scale of the hips' parent in the stream: the
        /// avatar root where the update's root motion puts it, then the transforms between it
        /// and the hips as the stream has them.</summary>
        void HipsParentGlobal(Transform hips, Unbound.Runtime.Playables.HumanStreamPose pose, out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            using var engine = MainThreadCheck.Engine();
            var root = Handles.Animator.transform;
            var x = pose.Root;
            position = new Vector3(x.t.x, x.t.y, x.t.z);
            rotation = Unbound.Runtime.Humanoid.AnimatorHuman.Rotation(x.q);
            scale = new Vector3(x.s.x, x.s.y, x.s.z);
            m_Chain.Clear();
            for (var p = hips.m_Parent; p != null && p != root; p = p.m_Parent) m_Chain.Add(p);
            for (int i = m_Chain.Count - 1; i >= 0; i--)
            {
                Local(m_Chain[i], out var lp, out var lr, out var ls);
                position = position + rotation * Vector3.Scale(scale, lp);
                rotation = rotation * lr;
                scale = Vector3.Scale(scale, ls);
            }
        }

        readonly List<Transform> m_Chain = new List<Transform>();

        /// <summary>What a humanoid bone's handle reads as its local values: the bone in the
        /// stream's skeleton; the hips relative to their parent transform, their scale the
        /// skeleton's hips scale over the parent's (tests/projects/humanoid bone.read).</summary>
        internal void HumanLocal(in AnimatorHandles.StreamTransform d, Unbound.Runtime.Playables.HumanStreamPose pose, out Vector3 p, out Quaternion r, out Vector3 s)
        {
            if (!pose.IsHips(d.Human))
            {
                pose.BoneLocal(d.Human, out p, out r, out s);
                return;
            }
            pose.HipsPlaced(out var wp, out var wr, out var ws);
            HipsParentGlobal(d.Transform, pose, out var pp, out var pr, out var ps);
            var local = Quaternion.Inverse(pr) * (wp - pp);
            p = new Vector3(Div(local.x, ps.x), Div(local.y, ps.y), Div(local.z, ps.z));
            r = Quaternion.Inverse(pr) * wr;
            s = new Vector3(Div(ws.x, ps.x), Div(ws.y, ps.y), Div(ws.z, ps.z));
        }

        internal void HumanGlobal(in AnimatorHandles.StreamTransform d, Unbound.Runtime.Playables.HumanStreamPose pose, out Vector3 p, out Quaternion r, out Vector3 s) =>
            pose.BoneWorld(d.Human, out p, out r, out s);

        /// <summary>A write through a humanoid bone's handle, local (null: unchanged). The hips'
        /// scale is not a value of the pose and is not taken (unmeasured).</summary>
        internal void HumanSetLocal(in AnimatorHandles.StreamTransform d, Unbound.Runtime.Playables.HumanStreamPose pose, Vector3? p, Quaternion? r, Vector3? s)
        {
            if (!pose.IsHips(d.Human))
            {
                if (p.HasValue) pose.SetBoneLocalPosition(d.Human, p.Value);
                if (r.HasValue) pose.SetBoneLocalRotation(d.Human, r.Value);
                if (s.HasValue) pose.SetBoneLocalScale(d.Human, s.Value);
                return;
            }
            HumanLocal(d, pose, out var lp, out var lr, out var ls);
            if (s.HasValue && (s.Value - ls).sqrMagnitude > 1e-10f * ls.sqrMagnitude)
                Stubs.Ignored("TransformStreamHandle scale of a humanoid's hips", "is not taken: the hips' scale in the stream comes from the pose's body (unmeasured)");
            if (!p.HasValue && !r.HasValue) return;
            // The rotation is taken at unit length in the frame it is written in.
            if (r.HasValue && !Unbound.Runtime.Playables.HumanStreamPose.UnitRotation(r.Value, out lr)) return;
            HipsParentGlobal(d.Transform, pose, out var pp, out var pr, out var ps);
            pose.SetHipsPlaced(pp + pr * Vector3.Scale(ps, p ?? lp), pr * lr);
        }

        /// <summary>A write through a humanoid bone's handle, in the world (null: unchanged).</summary>
        internal void HumanSetGlobal(in AnimatorHandles.StreamTransform d, Unbound.Runtime.Playables.HumanStreamPose pose, Vector3? p, Quaternion? r)
        {
            if (pose.IsHips(d.Human))
            {
                pose.BoneWorld(d.Human, out Vector3 wp, out Quaternion wr, out Vector3 _);
                pose.SetHipsWorld(p ?? wp, r ?? wr);
                return;
            }
            pose.BoneParentWorld(d.Human, out var pp, out var pr, out var ps);
            if (p.HasValue)
            {
                var local = Quaternion.Inverse(pr) * (p.Value - pp);
                pose.SetBoneLocalPosition(d.Human, new Vector3(Div(local.x, ps.x), Div(local.y, ps.y), Div(local.z, ps.z)));
            }
            if (r.HasValue && Unbound.Runtime.Playables.HumanStreamPose.UnitRotation(r.Value, out Quaternion unit))
                pose.SetBoneLocalRotation(d.Human, Quaternion.Inverse(pr) * unit);
        }

        internal static float Div(float a, float b) => Math.Abs(b) > 1e-12f ? a / b : a;

        /// <summary>A write with a read mask on a humanoid bone writes everything: which of a
        /// bone's values the Editor's masks let through is unmeasured.</summary>
        internal static void BoneMaskUnmeasured() =>
            Stubs.Ignored("TransformStreamHandle write with useMask on a humanoid bone", "writes every value (the bone's read masks are unmeasured)");

        /// <summary>The end of a job: a human pose it opened goes back into the stream.</summary>
        void CloseHuman()
        {
            var pose = m_Human;
            if (pose == null) return;
            m_Human = null;
            pose.Store();
            Handles.Animator?.Human?.ReturnStreamPose(pose);
        }

        /// <summary>A foot's velocity in the update's humanoid root motion: how far its goal moved
        /// during the step, per second (zero for a zero delta time or without body motion).</summary>
        internal Vector3 FootVelocity(bool left)
        {
            using var engine = MainThreadCheck.Engine();
            float dt = Stream.DeltaTime;
            var sample = Stream.BodyMotion;
            var animator = Handles.Animator;
            // Without root motion requested the stream reports no motion (measured,
            // tests/projects/humanoid stream.readNoMotion).
            if (dt == 0f || !sample.HasMotion || animator == null || !(animator.m_ApplyRootMotion || MoveCallbacks.Any(animator))) return Vector3.zero;
            var slip = (left ? sample.LeftFootSlip : sample.RightFootSlip).t;
            return new Vector3(slip.x / dt, slip.y / dt, slip.z / dt);
        }

        internal UnityEngine.Animations.AnimationStream ToStream() =>
            new UnityEngine.Animations.AnimationStream { m_AnimatorBindingsVersion = Handles.Id, m_Context = Id };

        // ------------------------------------------------------------------ transforms in the stream

        internal Vector3 LocalPosition(Transform t, int channel) { using var engine = MainThreadCheck.Engine(); return channel >= 0 ? new Vector3(Stream.Values[channel], Stream.Values[channel + 1], Stream.Values[channel + 2]) : t.localPosition; }

        internal Quaternion LocalRotation(Transform t, int channel) { using var engine = MainThreadCheck.Engine(); return channel >= 0 ? QuaternionMath.Normalize(AnimationEvaluation.Q(Stream.Values, channel)) : t.localRotation; }

        internal Vector3 LocalScale(Transform t, int channel) { using var engine = MainThreadCheck.Engine(); return channel >= 0 ? new Vector3(Stream.Values[channel], Stream.Values[channel + 1], Stream.Values[channel + 2]) : t.localScale; }

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
            using var engine = MainThreadCheck.Engine();
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
            using var engine = MainThreadCheck.Engine();
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

        // ------------------------------------------------------------------ skeleton nodes in the stream

        /// <summary>A skeleton node's local TRS as the stream has it (the skeleton's where it does not).</summary>
        void NodeLocal(AnimatorSkeleton skeleton, int node, out Vector3 p, out Quaternion r, out Vector3 s)
        {
            var x = skeleton.Local(node);
            Handles.NodeChannels(Stream.Bindings, node, out int position, out int rotation, out int scale);
            p = position >= 0 ? LocalPosition(null, position) : new Vector3(x.t.x, x.t.y, x.t.z);
            r = rotation >= 0 ? LocalRotation(null, rotation) : Unbound.Runtime.Humanoid.AnimatorHuman.Rotation(x.q);
            s = scale >= 0 ? LocalScale(null, scale) : new Vector3(x.s.x, x.s.y, x.s.z);
        }

        /// <summary>A skeleton node's world position, rotation and scale through the stream: the
        /// Animator's own (node 0), then the nodes' locals down to it.</summary>
        void NodeGlobal(AnimatorSkeleton skeleton, int node, out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            if (node <= 0)
            {
                var root = Handles.Animator.transform;
                GlobalTR(root, out position, out rotation);
                Local(root, out _, out _, out scale);
                if (root.m_Parent != null) scale = Vector3.Scale(root.m_Parent.lossyScale, scale);
                return;
            }
            NodeGlobal(skeleton, skeleton.Avatar.AvatarSkeleton.Parent[node], out position, out rotation, out scale);
            NodeLocal(skeleton, node, out var lp, out var lr, out var ls);
            position = position + rotation * Vector3.Scale(scale, lp);
            rotation = rotation * lr;
            scale = Vector3.Scale(scale, ls);
        }

        Matrix4x4 NodeToWorld(AnimatorSkeleton skeleton, int node)
        {
            if (node <= 0) return LocalToWorld(Handles.Animator.transform);
            NodeLocal(skeleton, node, out var p, out var r, out var s);
            return NodeToWorld(skeleton, skeleton.Avatar.AvatarSkeleton.Parent[node]) * Matrix4x4.TRS(p, r, s);
        }

        /// <summary>The skeleton node a handle animates, when it does (see AnimatorHandles.StreamTransform.Node).</summary>
        bool NodeOf(in AnimatorHandles.StreamTransform handle, out AnimatorSkeleton skeleton)
        {
            skeleton = handle.Node >= 0 ? Handles.Animator.Skeleton : null;
            return skeleton != null;
        }

        internal void GlobalTR(in AnimatorHandles.StreamTransform handle, out Vector3 position, out Quaternion rotation)
        {
            if (NodeOf(handle, out var skeleton)) NodeGlobal(skeleton, handle.Node, out position, out rotation, out _);
            else GlobalTR(handle.Transform, out position, out rotation);
        }

        internal Matrix4x4 LocalToWorld(in AnimatorHandles.StreamTransform handle) =>
            NodeOf(handle, out var skeleton) ? NodeToWorld(skeleton, handle.Node) : LocalToWorld(handle.Transform);

        /// <summary>The world position, rotation and scale of what the handle's target is below.</summary>
        internal bool NodeParentGlobal(in AnimatorHandles.StreamTransform handle, out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            position = default;
            rotation = default;
            scale = default;
            if (!NodeOf(handle, out var skeleton)) return false;
            NodeGlobal(skeleton, skeleton.Avatar.AvatarSkeleton.Parent[handle.Node], out position, out rotation, out scale);
            return true;
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
            using var engine = MainThreadCheck.Engine();
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

    /// <summary>
    /// An animation stream's root motion for the evaluated frame. Velocities are per second; Unbound
    /// takes them in world space, as Animator.velocity/angularVelocity are (Unbound's choice,
    /// unmeasured); the root motion pose is the root's current pose moved by them over the stream's
    /// delta time. Unbound computes it from the existing Animator root motion of an
    /// AnimatorControllerPlayable input, from the root motion a generic Animator's clips and mixers
    /// carry in the stream (RootMotionStep), or carries what a job below set; other streams report
    /// no motion (zero, identity) and warn when read.
    /// <para>Also Unbound's choices (unmeasured): the input's weight does not scale the motion; the
    /// motion includes the playable's speed but is divided by the stream's unscaled delta time;
    /// CopyAnimationStreamMotion copies the start pose and whether the motion was computed too; a
    /// stream without computed motion starts from the origin.</para>
    /// </summary>
    internal struct StreamMotion
    {
        /// <summary>Derived from an input's root motion (else reads warn).</summary>
        internal bool Computed;
        internal Vector3 Velocity, AngularVelocity;
        /// <summary>The root's pose before this frame's motion.</summary>
        internal Vector3 StartPosition;
        internal Quaternion StartRotation;

        internal static StreamMotion None => new StreamMotion { StartRotation = Quaternion.identity };

        internal Vector3 Position(float deltaTime) => StartPosition + Velocity * deltaTime;

        internal Quaternion Rotation(float deltaTime)
        {
            float speed = AngularVelocity.magnitude;
            if (speed == 0f || deltaTime == 0f) return StartRotation;
            return Quaternion.AngleAxis(speed * deltaTime * Mathf.Rad2Deg, AngularVelocity / speed) * StartRotation;
        }

        /// <summary>The motion of the stream an input playable produced (just evaluated): a
        /// controller playable's root motion, the motion an animation job's stream ended with, or
        /// the root motion clips and mixers carry in the stream (a generic Animator's playable
        /// output root motion, see AnimationEvaluation.Run).</summary>
        internal static StreamMotion Of(AnimationEvaluation e, PlayableNode node, AnimStream stream)
        {
            float deltaTime = stream.DeltaTime;
            switch (node?.Kind)
            {
                case ControllerKind controller when controller.Player is AnimatorPlayer player && player.Bindings == e.Bindings:
                    if (!player.StreamRootMotion(deltaTime, out var start, out var startRotation, out var velocity, out var angular))
                    {
                        Stubs.Ignored("AnimationStream root motion of humanoid clips",
                            "is not computed: velocity and angularVelocity read zero for an AnimatorControllerPlayable playing humanoid clips");
                        return new StreamMotion { StartPosition = start, StartRotation = startRotation };
                    }
                    return new StreamMotion
                    {
                        Computed = true, Velocity = velocity, AngularVelocity = angular,
                        StartPosition = start, StartRotation = startRotation,
                    };
                case JobKind job:
                    return job.Motion;
                default:
                    if (e.MotionMode == RootMotionMode.Human && stream.BodyMotion.HasMotion && e.Animator?.Human is { } human)
                        return Human(human, stream.BodyMotion, deltaTime);
                    if (!stream.Motion.Known || e.Animator == null) return None;
                    stream.Motion.ToStream(e.Animator, deltaTime, out var from, out var fromRotation, out var linear, out var turn);
                    return new StreamMotion
                    {
                        Computed = true, Velocity = linear, AngularVelocity = turn,
                        StartPosition = from, StartRotation = fromRotation,
                    };
            }
        }

        /// <summary>A humanoid stream's motion: the update's root motion step (after the feet pivot
        /// correction, as the Animator applies it; none without root motion requested) per second, in the avatar root's frame and the
        /// clip's units (not scaled by the human scale), its rotation as an angular velocity (6000.0.71f1, tests/projects/humanoid stream.*). Where the root motion
        /// pose starts is not measured: the avatar's origin.</summary>
        static StreamMotion Human(Unbound.Runtime.Humanoid.AnimatorHuman human, in Unbound.Runtime.Humanoid.HumanRootMotionSample sample, float deltaTime)
        {
            using var engine = MainThreadCheck.Engine();
            var animator = human.Animator;
            if (deltaTime == 0f || !(animator.m_ApplyRootMotion || MoveCallbacks.Any(animator)))
                return new StreamMotion { Computed = true, StartRotation = Quaternion.identity };
            human.PredictMotion(sample, deltaTime, out var step);
            var t = step.Delta.t;
            var w = Unbound.Runtime.Humanoid.HumanRootMotion.AngularVelocityOf(step.Delta.q, deltaTime);
            return new StreamMotion
            {
                Computed = true,
                Velocity = new Vector3(t.x / deltaTime, t.y / deltaTime, t.z / deltaTime),
                AngularVelocity = new Vector3(w.x, w.y, w.z),
                StartRotation = Quaternion.identity,
            };
        }

        /// <summary>The same motion values (what a job can change).</summary>
        internal bool SameAs(StreamMotion other) =>
            Computed == other.Computed && Velocity.Equals(other.Velocity) && AngularVelocity.Equals(other.AngularVelocity);
    }

    /// <summary>An AnimationScriptPlayable: runs its job on the stream of its first input (or the
    /// stream it starts from), synchronously, as part of the evaluation.</summary>
    internal sealed class JobKind : AnimationKind
    {
        internal object Job;
        internal bool ProcessInputs = true;

        /// <summary>A job on a humanoid Animator may read and write its whole human pose
        /// (AnimationStream.AsHuman): every humanoid curve gets a channel.</summary>
        internal override void Bind(AnimationEvaluation e, PlayableNode node)
        {
            if (e.Animator != null && e.Animator.isHuman) e.Bindings.BindHumanCurves();
        }
        /// <summary>The motion its stream ended with in its last evaluation (a job above reads it).</summary>
        internal StreamMotion Motion = StreamMotion.None;

        internal override void Evaluate(AnimationEvaluation e, PlayableNode node, int viaPort, float weight, AnimStream into)
        {
            into.JobRan = true;
            if (e.ReferencePose) return;
            var handles = AnimatorHandles.Of(e.Animator);
            var context = StreamContext.Open(into, handles, e);
            var entry = StreamMotion.None;
            bool ran = false;
            try
            {
                for (int i = 0; i < node.Inputs.Count; i++)
                {
                    var input = node.Inputs[i];
                    if (input.Node == null) continue;
                    if (i == 0 && ProcessInputs)
                    {
                        e.EvaluateInto(input.Node, input.SourcePort, weight * input.Weight, into);
                        context.Motion = StreamMotion.Of(e, input.Node, into);
                        context.Inputs.Add((context, input.Weight));
                        continue;
                    }
                    if (!ProcessInputs) continue;
                    var s = e.Evaluate(input.Node, input.SourcePort, weight * input.Weight);
                    var inputContext = StreamContext.Open(s, handles, e);
                    inputContext.Motion = StreamMotion.Of(e, input.Node, s);
                    context.Inputs.Add((inputContext, input.Weight));
                    into.Inputs.Add((s, input.Weight));
                }
                entry = context.Motion;
                ran = true;
                if (Job is UnityEngine.Animations.IAnimationJob job)
                {
                    var stream = context.ToStream();
                    using var jobScope = Unbound.Runtime.Jobs.JobSystem.AnimationJob();
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
                Motion = context.Motion;
                // The motion the job's stream ends with is its stream's root motion for the output
                // when the job changed it (set velocities, CopyAnimationStreamMotion) or the stream
                // does not carry its input's motion itself (an input whose motion is not known).
                bool changed = ran && !context.Motion.SameAs(entry);
                if (e.MotionMode == RootMotionMode.Compute && (changed || (ran && context.Motion.Computed && !into.Motion.Known)))
                    into.Motion = RootMotionStep.FromVelocities(e.Animator, into.DeltaTime, context.Motion.Velocity, context.Motion.AngularVelocity);
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

        /// <summary>The stream's motion, warning once per member when Unbound has not computed it
        /// (the stream's input is not an AnimatorControllerPlayable, a generic Animator's clip or
        /// mixer, or a job above one).</summary>
        StreamContext MotionContext(string member)
        {
            CheckIsValid();
            var c = Context;
            if (!c.Motion.Computed)
                Unbound.Runtime.Stubs.Ignored("AnimationStream." + member,
                    "is not computed for this stream (only from an AnimatorControllerPlayable input, or a generic Animator's clip or mixer input): it reads no motion or the value a job set");
            return c;
        }

        /// <summary>A job's motion change stays in the stream (jobs above and CopyAnimationStreamMotion
        /// see it) and moves a generic Animator through its playable output (AnimationEvaluation.Run).
        /// Setting stays loud: how Unity turns a set velocity into the Animator's motion is not
        /// measured, and humanoid Animators and Animators whose own controller plays do not apply it.</summary>
        StreamContext SetMotionContext(string member)
        {
            CheckIsValid();
            Unbound.Runtime.Stubs.Ignored("AnimationStream." + member + " (set)",
                "moves a generic Animator through its playable output by velocity times the stream's delta time (Unbound's choice, unmeasured); humanoid Animators and Animators whose own controller plays do not apply it");
            return Context;
        }

        public Vector3 velocity
        {
            get => MotionContext("velocity").Motion.Velocity;
            set => SetMotionContext("velocity").Motion.Velocity = value;
        }

        public Vector3 angularVelocity
        {
            get => MotionContext("angularVelocity").Motion.AngularVelocity;
            set => SetMotionContext("angularVelocity").Motion.AngularVelocity = value;
        }

        public Vector3 rootMotionPosition
        {
            get
            {
                var c = MotionContext("rootMotionPosition");
                return c.Motion.Position(c.Stream.DeltaTime);
            }
        }

        public Quaternion rootMotionRotation
        {
            get
            {
                var c = MotionContext("rootMotionRotation");
                return c.Motion.Rotation(c.Stream.DeltaTime);
            }
        }

        public bool isHumanStream
        {
            get
            {
                CheckIsValid();
                var animator = Context.Handles.Animator;
                return animator != null && animator.IsHuman;
            }
        }

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

        public void CopyAnimationStreamMotion(AnimationStream animationStream)
        {
            CheckIsValid();
            animationStream.CheckIsValid();
            Context.Motion = animationStream.Context.Motion;
        }
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
            if (!data.Resolved)
            {
                data.Resolved = true;
                context.Handles.Transforms[handleIndex] = data;
            }
            return data.Node >= 0 ? context.Handles.Animator.Skeleton != null : data.Transform != null;
        }

        public bool IsValid(AnimationStream stream) =>
            stream.Context is StreamContext c && m_AnimatorBindingsVersion == c.Handles.Id && handleIndex >= 0 && handleIndex < c.Handles.Transforms.Count;

        /// <summary>Resolves the handle now instead of at its first access (Scripting API:
        /// "Handles are lazily resolved as they're accessed").</summary>
        public void Resolve(AnimationStream stream) => Data(stream, out _, out _);

        /// <summary>Whether a stream has accessed or resolved the handle: false in the first job
        /// evaluation until then, true afterwards (6000.0.71f1, tests/projects/animoptimize
        /// job.Rig.0 and job.Rig.1).</summary>
        public bool IsResolved(AnimationStream stream) =>
            IsValid(stream) && ((StreamContext)stream.Context).Handles.Transforms[handleIndex].Resolved;

        public Vector3 GetPosition(AnimationStream stream)
        {
            using var engine = MainThreadCheck.Engine();
            if (!Data(stream, out var c, out var d)) return Vector3.zero;
            if (c.HumanBone(handleIndex, ref d, out var human)) { c.HumanGlobal(d, human, out var hp, out _, out _); return hp; }
            c.GlobalTR(d, out var p, out _);
            return p;
        }

        public void SetPosition(AnimationStream stream, Vector3 position)
        {
            if (!Data(stream, out var c, out var d)) return;
            if (c.HumanBone(handleIndex, ref d, out var human)) { c.HumanSetGlobal(d, human, position, null); return; }
            ParentGlobal(c, d, out var pp, out var pr, out var parentScale);
            var local = Quaternion.Inverse(pr) * (position - pp);
            c.Write(d.Position, new Vector3(StreamContext.Div(local.x, parentScale.x), StreamContext.Div(local.y, parentScale.y), StreamContext.Div(local.z, parentScale.z)));
        }

        public Quaternion GetRotation(AnimationStream stream)
        {
            using var engine = MainThreadCheck.Engine();
            if (!Data(stream, out var c, out var d)) return Quaternion.identity;
            if (c.HumanBone(handleIndex, ref d, out var human)) { c.HumanGlobal(d, human, out _, out var hr, out _); return hr; }
            c.GlobalTR(d, out _, out var r);
            return r;
        }

        public void SetRotation(AnimationStream stream, Quaternion rotation)
        {
            if (!Data(stream, out var c, out var d)) return;
            if (c.HumanBone(handleIndex, ref d, out var human)) { c.HumanSetGlobal(d, human, null, rotation); return; }
            ParentGlobal(c, d, out _, out var pr, out _);
            c.Write(d.Rotation, Quaternion.Inverse(pr) * rotation);
        }

        public Vector3 GetLocalPosition(AnimationStream stream)
        {
            GetLocalTRS(stream, out var position, out _, out _);
            return position;
        }

        public void SetLocalPosition(AnimationStream stream, Vector3 position)
        {
            if (!Data(stream, out var c, out var d)) return;
            if (c.HumanBone(handleIndex, ref d, out var human)) c.HumanSetLocal(d, human, position, null, null);
            else c.Write(d.Position, position);
        }

        public Quaternion GetLocalRotation(AnimationStream stream)
        {
            using var engine = MainThreadCheck.Engine();
            if (!Data(stream, out var c, out var d)) return Quaternion.identity;
            if (c.HumanBone(handleIndex, ref d, out var human)) { c.HumanLocal(d, human, out _, out var hr, out _); return hr; }
            return c.LocalRotation(d.Transform, d.Rotation);
        }

        public void SetLocalRotation(AnimationStream stream, Quaternion rotation)
        {
            if (!Data(stream, out var c, out var d)) return;
            if (c.HumanBone(handleIndex, ref d, out var human)) c.HumanSetLocal(d, human, null, rotation, null);
            else c.Write(d.Rotation, rotation);
        }

        public Vector3 GetLocalScale(AnimationStream stream)
        {
            using var engine = MainThreadCheck.Engine();
            if (!Data(stream, out var c, out var d)) return Vector3.one;
            if (c.HumanBone(handleIndex, ref d, out var human)) { c.HumanLocal(d, human, out _, out _, out var hs); return hs; }
            return c.LocalScale(d.Transform, d.Scale);
        }

        public void SetLocalScale(AnimationStream stream, Vector3 scale)
        {
            if (!Data(stream, out var c, out var d)) return;
            if (c.HumanBone(handleIndex, ref d, out var human)) c.HumanSetLocal(d, human, null, null, scale);
            else c.Write(d.Scale, scale);
        }

        public Matrix4x4 GetLocalToParentMatrix(AnimationStream stream)
        {
            using var engine = MainThreadCheck.Engine();
            if (!Data(stream, out var c, out var d)) return Matrix4x4.identity;
            if (c.HumanBone(handleIndex, ref d, out var human)) { c.HumanLocal(d, human, out var hp, out var hr, out var hs); return Matrix4x4.TRS(hp, hr, hs); }
            return Matrix4x4.TRS(c.LocalPosition(d.Transform, d.Position), c.LocalRotation(d.Transform, d.Rotation), c.LocalScale(d.Transform, d.Scale));
        }

        public bool GetPositionReadMask(AnimationStream stream) => Data(stream, out var c, out var d) && d.Position >= 0;

        public bool GetRotationReadMask(AnimationStream stream) => Data(stream, out var c, out var d) && d.Rotation >= 0;

        public bool GetScaleReadMask(AnimationStream stream) => Data(stream, out var c, out var d) && d.Scale >= 0;

        public void GetLocalTRS(AnimationStream stream, out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            using var engine = MainThreadCheck.Engine();
            if (!Data(stream, out var c, out var d))
            {
                position = Vector3.zero;
                rotation = Quaternion.identity;
                scale = Vector3.one;
                return;
            }
            if (c.HumanBone(handleIndex, ref d, out var human)) { c.HumanLocal(d, human, out position, out rotation, out scale); return; }
            position = c.LocalPosition(d.Transform, d.Position);
            rotation = c.LocalRotation(d.Transform, d.Rotation);
            scale = c.LocalScale(d.Transform, d.Scale);
        }

        public void SetLocalTRS(AnimationStream stream, Vector3 position, Quaternion rotation, Vector3 scale, bool useMask)
        {
            if (!Data(stream, out var c, out var d)) return;
            if (c.HumanBone(handleIndex, ref d, out var human))
            {
                if (useMask) StreamContext.BoneMaskUnmeasured();
                c.HumanSetLocal(d, human, position, rotation, scale);
                return;
            }
            if (!useMask || (d.Position >= 0)) c.Write(d.Position, position);
            if (!useMask || (d.Rotation >= 0)) c.Write(d.Rotation, rotation);
            if (!useMask || (d.Scale >= 0)) c.Write(d.Scale, scale);
        }

        public void GetGlobalTR(AnimationStream stream, out Vector3 position, out Quaternion rotation)
        {
            using var engine = MainThreadCheck.Engine();
            if (!Data(stream, out var c, out var d))
            {
                position = Vector3.zero;
                rotation = Quaternion.identity;
                return;
            }
            if (c.HumanBone(handleIndex, ref d, out var human)) { c.HumanGlobal(d, human, out position, out rotation, out _); return; }
            c.GlobalTR(d, out position, out rotation);
        }

        public Matrix4x4 GetLocalToWorldMatrix(AnimationStream stream)
        {
            using var engine = MainThreadCheck.Engine();
            if (!Data(stream, out var c, out var d)) return Matrix4x4.identity;
            if (c.HumanBone(handleIndex, ref d, out var human)) { c.HumanGlobal(d, human, out var hp, out var hr, out var hs); return Matrix4x4.TRS(hp, hr, hs); }
            return c.LocalToWorld(d);
        }

        public void SetGlobalTR(AnimationStream stream, Vector3 position, Quaternion rotation, bool useMask)
        {
            if (!Data(stream, out var c, out var d)) return;
            if (c.HumanBone(handleIndex, ref d, out var human))
            {
                if (useMask) StreamContext.BoneMaskUnmeasured();
                c.HumanSetGlobal(d, human, position, rotation);
                return;
            }
            ParentGlobal(c, d, out var pp, out var pr, out var parentScale);
            var local = Quaternion.Inverse(pr) * (position - pp);
            if (!useMask || (d.Position >= 0))
                c.Write(d.Position, new Vector3(StreamContext.Div(local.x, parentScale.x), StreamContext.Div(local.y, parentScale.y), StreamContext.Div(local.z, parentScale.z)));
            if (!useMask || (d.Rotation >= 0))
                c.Write(d.Rotation, Quaternion.Inverse(pr) * rotation);
        }


        static void ParentGlobal(StreamContext c, in AnimatorHandles.StreamTransform d, out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            if (c.NodeParentGlobal(d, out position, out rotation, out scale)) return;
            c.ParentGlobalTR(d.Transform, out position, out rotation);
            scale = ParentScale(c, d.Transform);
        }

        static Vector3 ParentScale(StreamContext c, Transform t)
        {
            using var engine = MainThreadCheck.Engine();
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
            if (!p.Resolved)
            {
                p.Resolved = true;
                context.Handles.Properties[handleIndex] = p;
            }
            channel = p.Channel;
            return channel >= 0 && !p.IsObject && channel < context.Stream.FloatCount;
        }

        public bool IsValid(AnimationStream stream) =>
            stream.Context is StreamContext c && m_AnimatorBindingsVersion == c.Handles.Id && handleIndex >= 0 && handleIndex < c.Handles.Properties.Count;

        /// <summary>Resolves the handle now instead of at its first access.</summary>
        public void Resolve(AnimationStream stream) => Channel(stream, out _, out _);

        /// <summary>Whether a stream has accessed or resolved the handle, as a transform's
        /// (6000.0.71f1, tests/projects/animoptimize jobIdle.Rig and jobProp.Rig: a custom
        /// property's handle is unresolved until the job reads it).</summary>
        public bool IsResolved(AnimationStream stream) =>
            IsValid(stream) && ((StreamContext)stream.Context).Handles.Properties[handleIndex].Resolved;

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
            var transform = handles.SceneTransforms[transformSceneHandleDefinitionIndex];
            return transform != null ? transform : null;
        }

        /// <summary>False once the transform is destroyed (6000.0.71f1, tests/projects/animoptimize
        /// jobDestroyed.Rig.1); its values then read as a missing transform's.</summary>
        public bool IsValid(AnimationStream stream) =>
            AnimatorHandles.Get(valid) is AnimatorHandles h && transformSceneHandleDefinitionIndex >= 0 && transformSceneHandleDefinitionIndex < h.SceneTransforms.Count
            && h.SceneTransforms[transformSceneHandleDefinitionIndex] != null;

        public Vector3 GetPosition(AnimationStream stream) { using var engine = MainThreadCheck.Engine(); return Transform(stream)?.position ?? Vector3.zero; }

        [Obsolete("SceneHandle is now read-only; it was problematic with the engine multithreading and determinism")]
        [Unbound.EditorFaithful("obsolete: scene handles are read-only, Unity does nothing too")]
        public void SetPosition(AnimationStream stream, Vector3 position) { }

        public Vector3 GetLocalPosition(AnimationStream stream) { using var engine = MainThreadCheck.Engine(); return Transform(stream)?.localPosition ?? Vector3.zero; }

        [Obsolete("SceneHandle is now read-only; it was problematic with the engine multithreading and determinism")]
        [Unbound.EditorFaithful("obsolete: scene handles are read-only, Unity does nothing too")]
        public void SetLocalPosition(AnimationStream stream, Vector3 position) { }

        public Quaternion GetRotation(AnimationStream stream) { using var engine = MainThreadCheck.Engine(); return Transform(stream)?.rotation ?? Quaternion.identity; }

        [Obsolete("SceneHandle is now read-only; it was problematic with the engine multithreading and determinism")]
        [Unbound.EditorFaithful("obsolete: scene handles are read-only, Unity does nothing too")]
        public void SetRotation(AnimationStream stream, Quaternion rotation) { }

        public Quaternion GetLocalRotation(AnimationStream stream) { using var engine = MainThreadCheck.Engine(); return Transform(stream)?.localRotation ?? Quaternion.identity; }

        [Obsolete("SceneHandle is now read-only; it was problematic with the engine multithreading and determinism")]
        [Unbound.EditorFaithful("obsolete: scene handles are read-only, Unity does nothing too")]
        public void SetLocalRotation(AnimationStream stream, Quaternion rotation) { }

        public Vector3 GetLocalScale(AnimationStream stream) { using var engine = MainThreadCheck.Engine(); return Transform(stream)?.localScale ?? Vector3.one; }

        [Obsolete("SceneHandle is now read-only; it was problematic with the engine multithreading and determinism")]
        [Unbound.EditorFaithful("obsolete: scene handles are read-only, Unity does nothing too")]
        public void SetLocalScale(AnimationStream stream, Vector3 scale) { }

        public void GetLocalTRS(AnimationStream stream, out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            using var engine = MainThreadCheck.Engine();
            var t = Transform(stream);
            position = t != null ? t.localPosition : Vector3.zero;
            rotation = t != null ? t.localRotation : Quaternion.identity;
            scale = t != null ? t.localScale : Vector3.one;
        }

        public Matrix4x4 GetLocalToParentMatrix(AnimationStream stream)
        {
            using var engine = MainThreadCheck.Engine();
            var t = Transform(stream);
            return t != null ? Matrix4x4.TRS(t.localPosition, t.localRotation, t.localScale) : Matrix4x4.identity;
        }

        public void GetGlobalTR(AnimationStream stream, out Vector3 position, out Quaternion rotation)
        {
            using var engine = MainThreadCheck.Engine();
            var t = Transform(stream);
            position = t != null ? t.position : Vector3.zero;
            rotation = t != null ? t.rotation : Quaternion.identity;
        }

        public Matrix4x4 GetLocalToWorldMatrix(AnimationStream stream) { using var engine = MainThreadCheck.Engine(); return Transform(stream)?.localToWorldMatrix ?? Matrix4x4.identity; }
    }

    /// <summary>A property read from the scene during an animation job.</summary>
    public partial struct PropertySceneHandle
    {
        internal uint valid;
        internal int handleIndex;

        float Value(AnimationStream stream)
        {
            using var engine = MainThreadCheck.Engine();
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

        [Unbound.EditorFaithful("a handle is bound when it is created: IsResolved is true for every valid handle")]
        public void Resolve(AnimationStream stream) { }

        public bool IsResolved(AnimationStream stream) => IsValid(stream);

        public float GetFloat(AnimationStream stream) => Value(stream);

        [Obsolete("SceneHandle is now read-only; it was problematic with the engine multithreading and determinism")]
        [Unbound.EditorFaithful("obsolete: scene handles are read-only, Unity does nothing too")]
        public void SetFloat(AnimationStream stream, float value) { }

        public int GetInt(AnimationStream stream) => (int)Math.Round(Value(stream));

        [Obsolete("SceneHandle is now read-only; it was problematic with the engine multithreading and determinism")]
        [Unbound.EditorFaithful("obsolete: scene handles are read-only, Unity does nothing too")]
        public void SetInt(AnimationStream stream, int value) { }

        public bool GetBool(AnimationStream stream) => Value(stream) >= 0.5f;

        [Obsolete("SceneHandle is now read-only; it was problematic with the engine multithreading and determinism")]
        [Unbound.EditorFaithful("obsolete: scene handles are read-only, Unity does nothing too")]
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
            handles.Bound(new AnimatorHandles.StreamTransform
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

        [Unbound.EditorFaithful("6000.0.71f1, tests/projects/animoptimize jobResolveAll.Rig and jobIdleResolveAll.Rig: called before a manual graph's first evaluation or between two, it leaves IsResolved false; a handle resolves at its first access in a stream")]
        public static void ResolveAllStreamHandles(this Animator animator) { }

        [Unbound.EditorFaithful("a handle is bound when it is created: IsResolved is true for every valid handle")]
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
            animator.m_StreamHandles.StreamOwned.Clear();
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
