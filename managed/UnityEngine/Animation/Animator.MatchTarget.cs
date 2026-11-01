using System;
using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>Target matching of a generic rig's root (AvatarTarget.Root): while the base
    /// layer's state runs from the start to the target normalized time, root motion is corrected
    /// so that the root ends at the target (measured, tests/projects/modelroot).</summary>
    public partial class Animator
    {
        internal sealed class MatchState
        {
            internal Vector3 Position;
            internal Quaternion Rotation;
            internal MatchTargetWeightMask Mask;
            internal AnimatorPlayer Player;
            internal StateRun Run;
            internal bool CompleteOnInterruption, Finish;
            /// <summary>Unwrapped normalized times of the base layer's state.</summary>
            internal double Start, End;
        }

        internal MatchState m_Match;

        public bool isMatchingTarget => m_Match != null;

        public void MatchTarget(Vector3 matchPosition, Quaternion matchRotation, AvatarTarget targetBodyPart, MatchTargetWeightMask weightMask, float startNormalizedTime) =>
            MatchTarget(matchPosition, matchRotation, targetBodyPart, weightMask, startNormalizedTime, 1f, true);

        public void MatchTarget(Vector3 matchPosition, Quaternion matchRotation, AvatarTarget targetBodyPart, MatchTargetWeightMask weightMask, float startNormalizedTime,
            float targetNormalizedTime) =>
            MatchTarget(matchPosition, matchRotation, targetBodyPart, weightMask, startNormalizedTime, targetNormalizedTime, true);

        /// <summary>Queues one generic root match at a time. A start already passed in the
        /// current loop begins at the state's current time (tests/projects/modelroot).</summary>
        public void MatchTarget(Vector3 matchPosition, Quaternion matchRotation, AvatarTarget targetBodyPart, MatchTargetWeightMask weightMask, float startNormalizedTime,
            float targetNormalizedTime, bool completeMatch)
        {
            if (m_Match != null) return;
            if (targetBodyPart != AvatarTarget.Root)
            {
                Unbound.Runtime.Stubs.Ignored("Animator.MatchTarget", "matches only AvatarTarget.Root (generic rigs); body parts need humanoid avatars");
                return;
            }
            if (isHuman)
            {
                Unbound.Runtime.Stubs.Ignored("Animator.MatchTarget", "target matching on humanoid avatars is not supported yet");
                return;
            }
            var p = EnsurePlayer();
            var run = p != null && p.Layers.Length > 0 ? p.Layers[0].Current : null;
            if (run == null) return;
            if (p.CompletedNonLoopingBaseState(run)) return;
            double now = run.Normalized, loop = Math.Floor(now);
            double start = Math.Max(now, loop + startNormalizedTime), end = loop + targetNormalizedTime;
            m_Match = new MatchState { Position = matchPosition, Rotation = matchRotation, Mask = weightMask,
                Start = start, End = end, Player = p, Run = run, CompleteOnInterruption = completeMatch };
        }

        public void InterruptMatchTarget() => InterruptMatchTarget(true);

        /// <summary>Cancels matching, or queues its remaining correction for root motion on the
        /// next evaluation, including Animator.Update(0) (tests/projects/modelroot).</summary>
        public void InterruptMatchTarget(bool completeMatch)
        {
            var match = m_Match;
            if (match == null) return;
            if (completeMatch) match.Finish = true;
            else m_Match = null;
        }
    }

    public partial struct MatchTargetWeightMask
    {
        Vector3 m_PositionXYZWeight;
        float m_RotationWeight;

        public MatchTargetWeightMask(Vector3 positionXYZWeight, float rotationWeight)
        {
            m_PositionXYZWeight = positionXYZWeight;
            m_RotationWeight = rotationWeight;
        }

        public Vector3 positionXYZWeight
        {
            get => m_PositionXYZWeight;
            set => m_PositionXYZWeight = value;
        }

        public float rotationWeight
        {
            get => m_RotationWeight;
            set => m_RotationWeight = value;
        }
    }
}
