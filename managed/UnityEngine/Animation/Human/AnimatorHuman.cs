using System;
using UnityEngine;

namespace Unbound.Runtime.Humanoid
{
    /// <summary>A humanoid clip an Animator's layer plays in an evaluation.</summary>
    internal struct HumanContribution
    {
        public int Layer;
        /// <summary>The state playing the clip (its blend tree's clips share it).</summary>
        public StateRun Run;
        /// <summary>The state's weight in its layer (transitions) and the clip's (the state's times
        /// its blend trees').</summary>
        public float StateWeight, Weight;
        public UnityEngine.AnimationClip Clip;
        /// <summary>The clip's normalized time less the state's (the state's and blend trees' cycle
        /// offsets).</summary>
        public float Offset;
    }

    /// <summary>A layer of a humanoid evaluation: its weight, blending, body mask and motion.</summary>
    internal struct HumanLayer
    {
        public float Weight;
        public bool Additive;
        /// <summary>The <see cref="HumanMask"/> parts the layer affects.</summary>
        public bool[] Mask;
    }

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

        internal void Set(int curve, float value)
        {
            m_Values[curve] = value;
            m_Present[curve] = true;
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
        internal float PivotWeight => m_Memory.PivotWeight;

        internal void ClearMotion() => m_HasSample = false;

        // The layered evaluation: the pose of every layer mixed, the pose of the layers that place the
        // body, and their workspaces.
        readonly HumanPose m_Layered = new HumanPose(), m_LayeredBase = new HumanPose(), m_LayerPose = new HumanPose(),
            m_StatePose = new HumanPose(), m_ClipPose = new HumanPose(), m_ReferencePose = new HumanPose();
        bool m_HasLayered;
        bool m_RootFromPose = true;
        // Foot IK: whether an evaluated state asks for it, the stabilization's inputs and the feet
        // goals of the last update.
        bool m_FootIK, m_StabilizeFeet;
        Trs m_MotionX = Trs.Identity, m_LeftFootX = Trs.Identity, m_RightFootX = Trs.Identity;
        float m_DeltaTime;

        /// <summary>One clip's human pose and motion for the state's previous and current times: the
        /// body placed relative to the clip's motion; for an additive layer, the difference from the
        /// clip's reference pose (its first frame), goals left out.</summary>
        void EvaluateClip(in HumanContribution c, float speed, bool additive, out HumanRootMotionSample sample)
        {
            var clip = c.Clip;
            var motion = clip.HumanClipMotion;
            var input = new HumanClipMotionInput
            {
                Time = (float)c.Run.Normalized,
                PreviousTime = (float)c.Run.Previous,
                Speed = speed,
                CycleOffset = c.Offset,
            };
            float previous = HumanRootMotion.ClipTime(motion, input.PreviousTime, input.Speed, input.CycleOffset, out _);
            float current = HumanRootMotion.ClipTime(motion, input.Time, input.Speed, input.CycleOffset, out _);
            clip.SampleHuman(previous, m_Previous);
            clip.SampleHuman(current, m_Current);
            var present = clip.HumanPresent;
            HumanRootMotion.Evaluate(motion, input, m_Previous, m_Current, present, out sample);
            HumanClipImport.PoseFromCurves(m_Current, present, m_ClipPose);
            if (!additive)
            {
                HumanRootMotion.ApplyToPose(m_ClipPose, sample);
                return;
            }
            clip.SampleHuman(motion.StartTime, m_Previous);
            HumanClipImport.PoseFromCurves(m_Previous, present, m_ReferencePose);
            HumanBlend.Sub(m_ClipPose, m_ClipPose, m_ReferencePose);
            for (int g = 0; g < m_ClipPose.Goals.Length; g++) m_ClipPose.Goals[g].X = Trs.Identity;
        }

        /// <summary>The layers' humanoid clips evaluated as the Animator's mixers do: each state blends
        /// its clips' poses and motions, each layer its states', and the layers are mixed in order
        /// (override or additive, with their weights and body masks; additive layers do not move the
        /// root). The root motion of the mix is applied as for a single clip. False when no humanoid
        /// clip plays.</summary>
        internal bool EvaluateLayers(System.Collections.Generic.List<HumanContribution> list, HumanLayer[] layers,
            Func<StateRun, float> speedOf, bool rootFromPose, float deltaTime, bool wantDelta, out AnimatorRootMotion result, out bool applied)
        {
            result = default;
            applied = false;
            m_HasLayered = false;
            m_HasSample = false;
            if (list.Count == 0) return false;
            HumanBlend.Clear(m_Layered);
            HumanBlend.Clear(m_LayeredBase);
            var motion = HumanBlend.ClearMotion();
            var full = HumanMask.FullBody;
            for (int l = 0; l < layers.Length; l++)
            {
                bool any = false;
                HumanBlend.Begin(m_LayerPose);
                var layerMotion = HumanBlend.BeginMotion();
                float layerSum = 0f;
                bool additive = layers[l].Additive;
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].Layer != l) continue;
                    var run = list[i].Run;
                    bool seen = false;
                    for (int j = 0; j < i; j++)
                        if (list[j].Layer == l && list[j].Run == run) { seen = true; break; }
                    if (seen) continue;
                    // A state: its clips blended.
                    any = true;
                    float stateWeight = list[i].StateWeight;
                    HumanBlend.Begin(m_StatePose);
                    var stateMotion = HumanBlend.BeginMotion();
                    float stateSum = 0f;
                    for (int j = i; j < list.Count; j++)
                    {
                        if (list[j].Layer != l || list[j].Run != run) continue;
                        float w = stateWeight > 0f ? list[j].Weight / stateWeight : 0f;
                        EvaluateClip(list[j], speedOf(run), additive, out var sample);
                        HumanBlend.Node(m_StatePose, m_ClipPose, w);
                        HumanBlend.MotionNode(ref stateMotion, sample, w, full);
                        stateSum += w;
                    }
                    HumanBlend.End(m_StatePose, stateSum);
                    HumanBlend.EndMotion(ref stateMotion, stateSum, full);
                    HumanBlend.Node(m_LayerPose, m_StatePose, stateWeight);
                    HumanBlend.MotionNode(ref layerMotion, stateMotion, stateWeight, full);
                    layerSum += stateWeight;
                }
                if (!any) continue;
                HumanBlend.End(m_LayerPose, layerSum);
                HumanBlend.EndMotion(ref layerMotion, layerSum, full);
                float weight = l == 0 ? 1f : layers[l].Weight;
                var mask = layers[l].Mask ?? full;
                if (additive)
                {
                    HumanBlend.AdditiveLayer(m_Layered, m_LayerPose, weight, mask);
                    if (mask[HumanMask.Root]) HumanBlend.AdditiveLayer(m_LayeredBase, m_LayerPose, weight, mask);
                }
                else
                {
                    HumanBlend.OverrideLayer(m_Layered, m_LayerPose, weight, mask);
                    if (mask[HumanMask.Root]) HumanBlend.OverrideLayer(m_LayeredBase, m_LayerPose, weight, mask);
                    if (0f < weight) HumanBlend.MotionOverrideLayer(ref motion, layerMotion, weight, mask);
                }
                m_HasLayered = true;
            }
            if (!m_HasLayered) return false;
            m_RootFromPose = rootFromPose;
            m_Dirty = true;
            m_Sample = motion;
            m_FootIK = false;
            foreach (var c in list)
                if (c.Run.Def.IKOnFeet && (c.Layer == 0 || layers[c.Layer].Weight > 0f)) m_FootIK = true;
            HumanRootMotion.ApplyToAnimator(motion, AvatarX(), deltaTime, m_Constant.Human.Scale, m_Animator.m_FeetPivotActive, m_Memory, out result);
            PivotPosition = result.PivotPosition;
            m_MotionX = result.Root;
            m_DeltaTime = deltaTime;
            m_StabilizeFeet = m_Animator.m_StabilizeFeet && !m_Memory.FirstEvaluation;
            HumanRootMotion.EndUpdate(m_Memory, deltaTime);
            applied = wantDelta;
            return true;
        }

        /// <summary>SetBoneLocalRotation: the bone's local rotation in the human skeleton pose the IK
        /// step works on (stored as given; nothing for a bone the avatar does not map).</summary>
        internal void SetBoneLocalRotation(int bone, F4 rotation)
        {
            var h = m_Constant.Human;
            int node = -1;
            if (bone >= 0 && bone < Bone.Last) node = h.BoneIndex[bone];
            else if (bone >= Bone.Last && bone < Bone.Last + 2 * Bone.FingerBoneCount)
            {
                int f = bone - Bone.Last;
                var hand = f < Bone.FingerBoneCount ? h.LeftHand : h.RightHand;
                if (hand != null) node = hand.BoneIndex[f % Bone.FingerBoneCount];
            }
            if (node >= 0) m_W0.X[node].q = rotation;
        }

        /// <summary>Set while OnAnimatorIK runs (the IK API is meant for it).</summary>
        internal bool InIKPass;

        /// <summary>After the layers are written: the pose of the muscle values onto the skeleton.
        /// The retargeted pose is written, then each layer with an IK pass sends OnAnimatorIK and the
        /// IK solve applies the goals set there, as the Editor's Animator does.</summary>
        internal void Apply(Func<int, bool> ikPass, int layerCount)
        {
            if (!m_Dirty) return;
            m_Dirty = false;
            var human = m_Constant.Human;
            bool rootFromPose = true;
            if (m_HasLayered)
            {
                m_Pose.CopyFrom(m_Layered);
                rootFromPose = m_RootFromPose;
                if (!rootFromPose) m_Base.CopyFrom(m_LayeredBase);
            }
            else
            {
                HumanClipImport.PoseFromCurves(m_Values, m_Present, m_Pose);
                if (m_HasSample) HumanRootMotion.ApplyToPose(m_Pose, m_Sample);
            }
            HumanIK.Retarget(human, m_Base, m_Pose, rootFromPose, AvatarX(), Output, m_W0, m_W1, true);
            HasOutput = true;
            if (m_HasLayered && m_FootIK)
            {
                var x = AvatarX();
                HumanIK.SetIKOnFeet(true, human, Output, x, m_StabilizeFeet, ref m_LeftFootX, m_MotionX, m_DeltaTime,
                    m_Sample.LeftFootSpeedT, m_Sample.LeftFootSpeedQ);
                HumanIK.SetIKOnFeet(false, human, Output, x, m_StabilizeFeet, ref m_RightFootX, m_MotionX, m_DeltaTime,
                    m_Sample.RightFootSpeedT, m_Sample.RightFootSpeedQ);
            }
            HumanIK.EvaluateIK(human, Output, m_W0, m_W1, m_W2, true, out m_LeftFootX, out m_RightFootX);
            HumanIK.EvaluateEnd(m_Constant, AvatarX(), m_W0, m_W1, m_W2, m_AvatarPose);
            Write();
            for (int layer = 0; layer < layerCount; layer++)
            {
                if (!ikPass(layer)) continue;
                InIKPass = true;
                try
                {
                    SendOnAnimatorIK(layer);
                }
                finally
                {
                    InIKPass = false;
                }
                HumanIK.EvaluateIK(human, Output, m_W0, m_W1, m_W2, false, out m_LeftFootX, out m_RightFootX);
                HumanIK.EvaluateEnd(m_Constant, AvatarX(), m_W0, m_W1, m_W2, m_AvatarPose);
                Write();
            }
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
            var args = new object[] { layer };
            foreach (var behaviour in go.GetComponents<MonoBehaviour>())
            {
                if (behaviour == null) continue;
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

        /// <summary>The bound transforms below the root take the pose's local positions and rotations.</summary>
        void Write()
        {
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
        internal bool m_StabilizeFeet, m_LayersAffectMassCenter;
        internal float m_FeetPivotActive = 1f;

        /// <summary>Overrides a human bone's local rotation in the pose being evaluated (meant for
        /// OnAnimatorIK, before the IK solve writes the skeleton).</summary>
        public void SetBoneLocalRotation(HumanBodyBones humanBoneId, Quaternion rotation)
        {
            if (humanBoneId < 0 || humanBoneId >= HumanBodyBones.LastBone)
                throw new IndexOutOfRangeException("humanBoneId must be between 0 and " + HumanBodyBones.LastBone);
            Human?.SetBoneLocalRotation(HumanTrait.GetBoneIndexFromMono((int)humanBoneId), new F4(rotation.x, rotation.y, rotation.z, rotation.w));
        }

        /// <summary>Whether a state's Foot IK limits the feet's motion between updates.</summary>
        public bool stabilizeFeet
        {
            get => m_StabilizeFeet;
            set => m_StabilizeFeet = value;
        }

        /// <summary>Whether the layers above the base layer also place the body (mass center).</summary>
        public bool layersAffectMassCenter
        {
            get => m_LayersAffectMassCenter;
            set => m_LayersAffectMassCenter = value;
        }

        /// <summary>How much the root motion pivots about the feet rather than the mass center.</summary>
        public float feetPivotActive
        {
            get => m_FeetPivotActive;
            set => m_FeetPivotActive = value;
        }

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
            h.Apply(layer => player != null && layer < player.Layers.Length && player.Layers[layer].Def.IKPass,
                player?.Layers.Length ?? 0);
        }
    }
}
