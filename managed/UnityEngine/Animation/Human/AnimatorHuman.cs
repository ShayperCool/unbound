using System;
using UnityEngine;

namespace Unbound.Runtime.Humanoid
{
    /// <summary>A humanoid Animator's human pose: the muscle curve values the layers write, turned
    /// into the skeleton (retargeting through the avatar's human, twist distribution) and written
    /// to the human skeleton's transforms after every evaluation, as the Editor's Animator does.</summary>
    internal sealed class AnimatorHuman
    {
        readonly Animator m_Animator;
        readonly AvatarConstant m_Constant;
        readonly Transform[] m_Bound;
        readonly SkeletonPose m_AvatarPose, m_W0, m_W1, m_W2;
        readonly HumanPose m_Pose = new HumanPose();

        /// <summary>The retargeted pose of the last evaluation (world root and goals).</summary>
        internal readonly HumanPose Output = new HumanPose();
        internal bool HasOutput;

        readonly float[] m_Values = new float[HumanClipImport.CurveCount];
        readonly bool[] m_Present = new bool[HumanClipImport.CurveCount];
        bool m_Dirty;

        internal AvatarConstant Constant => m_Constant;

        internal AnimatorHuman(Animator animator, AvatarConstant constant)
        {
            m_Animator = animator;
            m_Constant = constant;
            m_Bound = AvatarBinding.Bind(animator.transform, constant);
            int n = constant.AvatarSkeleton.Count, h = constant.Human.Skeleton.Count;
            m_AvatarPose = new SkeletonPose(n);
            m_W0 = new SkeletonPose(h);
            m_W1 = new SkeletonPose(h);
            m_W2 = new SkeletonPose(h);
            for (int i = 0; i < m_Values.Length; i++) m_Values[i] = DefaultValue(i);
        }

        /// <summary>A muscle curve's value when nothing animates it: 0, a rotation's w 1.</summary>
        internal static float DefaultValue(int curve) => curve < HumanClipImport.MuscleFirst && curve % 7 == 6 ? 1f : 0f;

        /// <summary>Set by the pose's writer when the evaluation asks for Foot IK (an
        /// AnimationClipPlayable's applyFootIK, a state's Foot IK); the next <see cref="ApplyPose"/>
        /// applies and clears it.</summary>
        internal bool FootIK;

        internal void Set(int curve, float value)
        {
            m_Values[curve] = value;
            m_Present[curve] = true;
            m_Dirty = true;
        }

        internal float Value(int curve) => m_Values[curve];

        /// <summary>An evaluation that wrote no humanoid curve (its clips have none) still poses the
        /// avatar: every muscle 0, the body on the root (6000.0.71f1, tests/projects/humanoid
        /// genericOnHuman, genericOnHuman.graph).</summary>
        internal void DefaultPoseUnlessWritten()
        {
            if (m_Dirty) return;
            for (int i = 0; i < m_Values.Length; i++) m_Values[i] = DefaultValue(i);
            Array.Clear(m_Present);
            m_Dirty = true;
        }

        // The mass center pose's curve values (layers whose mask leaves out Root left out), when set.
        readonly float[] m_MassCenterValues = new float[HumanClipImport.CurveCount];
        bool m_HasMassCenter;

        /// <summary>A curve value of the pose that places the body, when it differs from the
        /// animated pose (see AnimatorPlayer.WriteMassCenterPose); kept for the next
        /// <see cref="Apply"/>.</summary>
        internal void SetMassCenter(int curve, float value)
        {
            if (!m_HasMassCenter) Array.Copy(m_Values, m_MassCenterValues, m_Values.Length);
            m_MassCenterValues[curve] = value;
            m_HasMassCenter = true;
            m_Dirty = true;
        }

        readonly HumanPose m_Base = new HumanPose();

        // The root motion of this update: the body relative to the motion, the feet pivot.
        readonly HumanRootMotionMemory m_Memory = new HumanRootMotionMemory();
        HumanRootMotionSample m_Sample;
        bool m_HasSample;
        readonly float[] m_Previous = new float[HumanClipImport.CurveCount];
        readonly float[] m_Current = new float[HumanClipImport.CurveCount];
        internal F4 PivotPosition;
        internal float PivotWeight => PivotHidden ? 0.5f : m_Memory.PivotWeight;
        /// <summary>Set by a playable output's motion while no root motion is requested: the public
        /// pivot weight then reads 0.5 (its position zero) while the feet memory still steps
        /// (6000.0.71f1, tests/projects/humanoid mixer.none).</summary>
        internal bool PivotHidden;

        internal void ClearMotion() => m_HasSample = false;

        /// <summary>A playable's body relative to its motion, retained for visibility replay.
        /// Does not apply root movement or step the feet-pivot memory.</summary>
        internal void SetPoseMotion(in HumanRootMotionSample sample)
        {
            m_Sample = sample;
            m_HasSample = true;
        }

        /// <summary>A clip's root motion between a state's previous and current times.</summary>
        internal void SampleMotion(AnimationClip clip, HumanClipMotion motion, in HumanClipMotionInput input, out HumanRootMotionSample sample)
        {
            float previous = HumanRootMotion.ClipTime(motion, input.PreviousTime, input.Speed, input.CycleOffset, out _);
            float current = HumanRootMotion.ClipTime(motion, input.Time, input.Speed, input.CycleOffset, out _);
            clip.SampleHuman(previous, m_Previous);
            clip.SampleHuman(current, m_Current);
            HumanRootMotion.Evaluate(motion, input, m_Previous, m_Current, clip.HumanPresent, out sample);
        }

        /// <summary>A target body part of a clip at <c>input.Time</c> relative to the clip's motion at
        /// <c>input.PreviousTime</c> (<see cref="HumanRootMotion.MatchPart"/>).</summary>
        internal Trs MatchPart(AnimationClip clip, HumanClipMotion motion, in HumanClipMotionInput input, int target)
        {
            clip.SampleHuman(HumanRootMotion.ClipTime(motion, input.PreviousTime, input.Speed, input.CycleOffset, out _), m_Previous);
            clip.SampleHuman(HumanRootMotion.ClipTime(motion, input.Time, input.Speed, input.CycleOffset, out _), m_Current);
            return HumanRootMotion.MatchPart(motion, input, m_Previous, m_Current, clip.HumanPresent, target);
        }

        /// <summary>A target body part in the previous evaluation's pose relative to its motion (the
        /// root motion pass runs before this evaluation's pose): nothing for the root, the body, or a
        /// goal in the API's goal frame. The body is the animated one, also when a mass center pose
        /// placed it (unmeasured).</summary>
        internal Trs PreviousPosePart(int target)
        {
            if (target == 0) return Trs.Identity;
            if (target == 1) return m_Pose.RootX;
            var x = m_Pose.Goals[target - 2].X;
            x.q = M.QNormalize(M.QMul(x.q, HumanIK.GoalOrientationOffset[target - 2]));
            return x;
        }

        /// <summary>An additive layer clip's body difference at a state's time: its body (RootX)
        /// relative to the body of its additive reference pose (the reference clip at the reference
        /// time, else the clip's first frame), reference⁻¹ · body.</summary>
        internal Trs AdditiveBodyDifference(AnimationClip clip, HumanClipMotion motion, in HumanClipMotionInput input)
        {
            float time = HumanRootMotion.ClipTime(motion, input.Time, input.Speed, input.CycleOffset, out float cycle);
            clip.SampleHuman(time, m_Current);
            var body = HumanRootMotion.ReadRoot(m_Current, clip.HumanPresent);
            if (motion.HasRootCurves && motion.LoopTime && (motion.Settings.LoopBlend || cycle != 0f))
            {
                // The body relative to its motion takes the clip's loop pose; a looping clip's body
                // goes on with its motion across whole cycles (tests/projects/humanoid additiveLoopPose).
                var x = HumanRootMotion.MotionX(motion, body.t, body.q, motion.HeightFromFeet ? HumanRootMotion.FeetY(m_Current, clip.HumanPresent, body) : 0f);
                var relative = HumanRootMotion.LoopBlend(motion, M.InvMul(x, body), time);
                if (cycle != 0f) x = HumanRootMotion.CycleX(x, motion.MotionStartX, motion.MotionStopX, (int)cycle);
                body = M.Mul(x, relative);
            }
            bool referenced = clip.m_HasAdditiveReferencePose && clip.m_AdditiveReferencePoseClip != null;
            var source = referenced ? clip.m_AdditiveReferencePoseClip : clip;
            float referenceTime = source.StartTime + (clip.m_HasAdditiveReferencePose ? clip.m_AdditiveReferencePoseTime : 0f);
            source.SampleHuman(referenceTime, m_Previous);
            var present = source.HumanPresent;
            if (!HumanRootMotion.HasRoot(present))
                Unbound.Runtime.Stubs.Ignored("Animator additive humanoid reference without a body",
                    "an additive reference clip without body curves (RootT, RootQ) counts as the identity body (not measured)");
            var reference = HumanRootMotion.ReadRoot(m_Previous, present);
            return M.InvMul(reference, body);
        }

        /// <summary>The update's (blended) root motion: the root's delta, the feet pivot, and the
        /// body relative to the motion for this update's pose.</summary>
        internal void ApplyMotion(in HumanRootMotionSample sample, float deltaTime, out AnimatorRootMotion result)
        {
            PivotHidden = false;
            m_Sample = sample;
            m_HasSample = true;
            HumanRootMotion.ApplyToAnimator(m_Sample, AvatarX(), deltaTime, m_Constant.Human.Scale, 1f, m_Memory, out result);
            PivotPosition = result.PivotPosition;
            HumanRootMotion.EndUpdate(m_Memory, deltaTime);
        }

        /// <summary>An update in which no humanoid clip plays (<see cref="HumanRootMotion.Idle"/>).</summary>
        internal void IdleMotion(float deltaTime)
        {
            PivotHidden = false;
            PivotPosition = HumanRootMotion.Idle(m_Memory, AvatarX(), deltaTime);
        }

        /// <summary>A re-blended sample replaces the retained one (mixer weights changed while
        /// culled): the feet pivot is placed between its feet again, with the pivot weight kept
        /// (6000.0.71f1, tests/projects/animculling humanMixerShape/live: the pivot is the new
        /// body's at the return). Nothing else of the motion memory changes.</summary>
        internal void RepivotMotion(in HumanRootMotionSample sample)
        {
            var a = sample.LeftFoot.t;
            var b = sample.RightFoot.t;
            float pw = m_Memory.PivotWeight;
            m_Memory.Pivot = new F4((b.x - a.x) * pw + a.x, (b.y - a.y) * pw + a.y, (b.z - a.z) * pw + a.z, (b.w - a.w) * pw + a.w);
        }

        internal void ReplayMotion(in AnimatorRootMotion motion, float deltaTime, out AnimatorRootMotion result)
        {
            HumanRootMotion.ProjectMotion(motion.Delta, AvatarX(), deltaTime, m_Constant.Human.Scale,
                m_Memory.Pivot, out result);
            PivotPosition = result.PivotPosition;
        }

        /// <summary>A human bone's local rotation in the IK step's skeleton pose (relative to its
        /// parent human bone), which the next IK solve and the twist distribution start from.</summary>
        internal void SetBoneLocalRotation(int monoBone, F4 rotation)
        {
            int node = m_Constant.HumanNodeOf(monoBone);
            if (node >= 0) m_W0.X[node].q = rotation;
        }

        /// <summary>Set while OnAnimatorIK runs (the IK API is meant for it).</summary>
        internal bool InIKPass;

        /// <summary>After the layers are written: the pose of the muscle values onto the skeleton,
        /// then each layer with an IK pass (<see cref="RunIK"/>), as the Editor's Animator does in an
        /// explicit evaluation. The player loop runs the IK passes of all Animators layer by layer
        /// instead (<see cref="Unbound.Runtime.AnimationPhases"/>).</summary>
        /// <paramref name="playableIK"/>: playable outputs request an IK pass on the base layer;
        /// the controller's layers request theirs by their IK pass setting.
        internal void Apply(Unbound.Runtime.AnimatorPlayer player, bool playableIK)
        {
            int layers = ApplyPose(player, playableIK);
            for (int layer = 0; layer < layers; layer++) RunIK(layer);
        }

        Unbound.Runtime.AnimatorPlayer m_IKPlayer;
        bool m_IKPlayable;
        int m_IKLayers;

        /// <summary>Retargets and writes the pose the layers wrote, before any IK pass. Returns the
        /// number of layers whose IK pass <see cref="RunIK"/> may run (0 when nothing was posed).</summary>
        internal int ApplyPose(Unbound.Runtime.AnimatorPlayer player, bool playableIK)
        {
            m_IKLayers = 0;
            bool footIK = FootIK;
            FootIK = false;
            if (!m_Dirty) return 0;
            m_Dirty = false;
            var human = m_Constant.Human;
            HumanClipImport.PoseFromCurves(m_Values, m_Present, m_Pose);
            if (m_HasSample) HumanRootMotion.ApplyToPose(m_Pose, m_Sample);
            // The mass center pose places the body, then the animated pose's muscles apply.
            bool massCenter = m_HasMassCenter;
            m_HasMassCenter = false;
            if (massCenter)
            {
                HumanClipImport.PoseFromCurves(m_MassCenterValues, m_Present, m_Base);
                if (m_HasSample) HumanRootMotion.ApplyToPose(m_Base, m_Sample);
            }
            HumanIK.Retarget(human, m_Base, m_Pose, !massCenter, AvatarX(), Output, m_W0, m_W1, true);
            HasOutput = true;
            // Foot IK: after the retarget step, the feet goals take full weight, left foot first,
            // and the solve below moves the legs onto them. Animator.stabilizeFeet is not
            // supported (its setter reports it), so it is off here.
            if (footIK)
            {
                var avatarX = AvatarX();
                HumanIK.SetIKOnFeet(true, human, Output, avatarX, false, m_Memory.FirstEvaluation);
                HumanIK.SetIKOnFeet(false, human, Output, avatarX, false, m_Memory.FirstEvaluation);
            }
            HumanIK.EvaluateIK(human, Output, m_W0, m_W1, m_W2, true, out _, out _);
            HumanIK.EvaluateEnd(m_Constant, AvatarX(), m_W0, m_W1, m_W2, m_AvatarPose);
            Write();
            m_IKPlayer = player;
            m_IKPlayable = playableIK;
            m_IKLayers = Math.Max(playableIK ? 1 : 0, player?.Layers.Length ?? 0);
            return m_IKLayers;
        }

        /// <summary>How many layers the last <see cref="ApplyPose"/> may run an IK pass on.</summary>
        internal int IKLayers => m_IKLayers;

        /// <summary>Whether the last <see cref="ApplyPose"/> requested an IK pass on a layer.</summary>
        internal bool HasIKPass(int layer) => layer < m_IKLayers
            && (layer == 0 && m_IKPlayable || m_IKPlayer != null && layer < m_IKPlayer.Layers.Length && m_IKPlayer.Layers[layer].Def.IKPass);

        /// <summary>One layer's IK pass: OnAnimatorIK(layer), then OnStateIK of that layer's
        /// states, then the solve of the goals they set (6000.0.71f1, tests/projects/animcallbacks).</summary>
        internal void RunIK(int layer)
        {
            if (!HasIKPass(layer)) return;
            InIKPass = true;
            var player = m_IKPlayer != null && m_IKPlayer.Attached ? m_IKPlayer : null;
            bool outer = player?.EnterDeferred() ?? false;
            try
            {
                SendOnAnimatorIK(layer);
                player?.InvokeStateIK(m_Animator, layer);
            }
            finally
            {
                InIKPass = false;
                player?.ExitDeferred(outer);
            }
            HumanIK.EvaluateIK(m_Constant.Human, Output, m_W0, m_W1, m_W2, false, out _, out _);
            HumanIK.EvaluateEnd(m_Constant, AvatarX(), m_W0, m_W1, m_W2, m_AvatarPose);
            Write();
        }

        /// <summary>The avatar root's world transform (position, rotation, lossy scale).</summary>
        Trs AvatarX()
        {
            var t = m_Animator.transform;
            return new Trs(AvatarBuild.V(t.position), AvatarBuild.Q(t.rotation), AvatarBuild.V(t.lossyScale));
        }

        static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, System.Reflection.MethodInfo> s_OnAnimatorIK =
            new System.Collections.Concurrent.ConcurrentDictionary<Type, System.Reflection.MethodInfo>();

        /// <summary>OnAnimatorIK(layerIndex) to the Animator's GameObject's scripts (SendMessage).</summary>
        void SendOnAnimatorIK(int layer)
        {
            var go = m_Animator.gameObject;
            if (go == null) return;
            // Reentrant IK passes (an evaluation inside OnAnimatorIK) each take their own buffers.
            var behaviours = s_Behaviours.Count > 0 ? s_Behaviours.Pop() : new System.Collections.Generic.List<MonoBehaviour>();
            var args = LayerArguments(layer);
            go.GetComponents(behaviours);
            try
            {
                SendOnAnimatorIK(behaviours, args);
            }
            finally
            {
                behaviours.Clear();
                s_Behaviours.Push(behaviours);
            }
        }

        static readonly System.Collections.Generic.Stack<System.Collections.Generic.List<MonoBehaviour>> s_Behaviours =
            new System.Collections.Generic.Stack<System.Collections.Generic.List<MonoBehaviour>>();
        // The callee cannot keep or change a reflection argument array: one per layer index.
        static object[][] s_LayerArguments = Array.Empty<object[]>();

        static object[] LayerArguments(int layer)
        {
            if (layer >= s_LayerArguments.Length)
            {
                var grown = new object[layer + 8][];
                Array.Copy(s_LayerArguments, grown, s_LayerArguments.Length);
                for (int i = s_LayerArguments.Length; i < grown.Length; i++) grown[i] = new object[] { i };
                s_LayerArguments = grown;
            }
            return s_LayerArguments[layer];
        }

        void SendOnAnimatorIK(System.Collections.Generic.List<MonoBehaviour> behaviours, object[] args)
        {
            foreach (var behaviour in behaviours)
            {
                if (behaviour == null || (Compat.DestroyPhasesMeasured && !behaviour.IsActiveAndEnabledNoThrow)) continue;
                var method = s_OnAnimatorIK.GetOrAdd(behaviour.GetType(), type => ScriptMessages.Find(type, "OnAnimatorIK", new[] { typeof(int) }));
                if (method == null) continue;
                try
                {
                    method.Invoke(behaviour, args);
                }
                catch (System.Reflection.TargetInvocationException e)
                {
                    Debug.LogException(e.InnerException ?? e, behaviour);
                }
            }
        }

        bool[] m_Nodes;

        /// <summary>The bound transforms below the root take the pose's local positions and rotations
        /// (the skeleton's nodes, for an Animator without its transform hierarchy).</summary>
        void Write()
        {
            var skeleton = m_Animator.m_NoHierarchy ? m_Animator.Skeleton : null;
            if (skeleton != null && skeleton.Avatar == m_Constant)
            {
                m_Nodes ??= AvatarBinding.Nodes(m_Constant);
                for (int i = 1; i < m_Nodes.Length; i++)
                {
                    if (!m_Nodes[i]) continue;
                    // The pose as the human gives it, its scale included (the hips carry the
                    // avatar's scale: tests/projects/animoptimize HumanOpt).
                    var x = m_AvatarPose.X[i];
                    var q = Rotation(x.q);
                    x.q = new F4(q.x, q.y, q.z, q.w);
                    skeleton.SetLocal(i, x);
                }
                skeleton.Apply();
                return;
            }
            for (int i = 1; i < m_Bound.Length; i++)
            {
                var t = m_Bound[i];
                if (t == null) continue;
                var x = m_AvatarPose.X[i];
                t.localPosition = new Vector3(x.t.x, x.t.y, x.t.z);
                t.localRotation = Rotation(x.q);
            }
        }

        internal static Quaternion Rotation(F4 q)
        {
            float a = q.y * q.y + q.x * q.x, b = q.w * q.w + q.z * q.z;
            if (b + a <= 1e-30f) return Quaternion.identity;
            var n = M.QNormalize(q);
            return new Quaternion(n.x, n.y, n.z, n.w);
        }
    }
}

namespace UnityEngine
{
    using Unbound.Runtime.Humanoid;

    public partial class Animator
    {
        AnimatorHuman m_Human;

        /// <summary>The human pose state of a humanoid Animator (null otherwise).</summary>
        internal AnimatorHuman Human
        {
            get
            {
                var c = AvatarConstant;
                if (c == null || !c.IsHuman || m_GameObject == null) return m_Human = null;
                if (m_Human == null || m_Human.Constant != c) m_Human = new AnimatorHuman(this, c);
                return m_Human;
            }
        }

        /// <summary>Applies the human pose the layers just wrote (humanoid Animators).</summary>
        internal void ApplyHumanPose()
        {
            var h = m_Human;
            var player = m_Player;
            if (h == null) return;
            Unbound.Runtime.AnimationPhases.ApplyHuman(h, player, playableIK: false);
        }

        /// <summary>Applies the combined playable pose and its requested IK pass. Measured in
        /// Unity 6000.0.71f1: clip and mixer outputs request one pass on a humanoid Animator,
        /// including when its attached controller also requests IK.</summary>
        internal void ApplyPlayableHumanPose(bool playableIK)
        {
            var h = m_Human;
            var player = m_Player;
            if (h == null) return;
            Unbound.Runtime.AnimationPhases.ApplyHuman(h, player, playableIK);
        }
    }
}
