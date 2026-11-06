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

        /// <summary>A clip's root motion between a state's previous and current times.</summary>
        internal void SampleMotion(AnimationClip clip, HumanClipMotion motion, in HumanClipMotionInput input, out HumanRootMotionSample sample)
        {
            float previous = HumanRootMotion.ClipTime(motion, input.PreviousTime, input.Speed, input.CycleOffset, out _);
            float current = HumanRootMotion.ClipTime(motion, input.Time, input.Speed, input.CycleOffset, out _);
            clip.SampleHuman(previous, m_Previous);
            clip.SampleHuman(current, m_Current);
            HumanRootMotion.Evaluate(motion, input, m_Previous, m_Current, clip.HumanPresent, out sample);
        }

        /// <summary>The update's (blended) root motion: the root's delta, the feet pivot, and the
        /// body relative to the motion for this update's pose.</summary>
        internal void ApplyMotion(in HumanRootMotionSample sample, float deltaTime, out AnimatorRootMotion result)
        {
            m_Sample = sample;
            m_HasSample = true;
            HumanRootMotion.ApplyToAnimator(m_Sample, AvatarX(), deltaTime, m_Constant.Human.Scale, 1f, m_Memory, out result);
            PivotPosition = result.PivotPosition;
            HumanRootMotion.EndUpdate(m_Memory, deltaTime);
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
            HumanClipImport.PoseFromCurves(m_Values, m_Present, m_Pose);
            if (m_HasSample) HumanRootMotion.ApplyToPose(m_Pose, m_Sample);
            HumanIK.Retarget(human, m_Base, m_Pose, true, AvatarX(), Output, m_W0, m_W1, true);
            HasOutput = true;
            HumanIK.EvaluateIK(human, Output, m_W0, m_W1, m_W2, true, out _, out _);
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
                HumanIK.EvaluateIK(human, Output, m_W0, m_W1, m_W2, false, out _, out _);
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
