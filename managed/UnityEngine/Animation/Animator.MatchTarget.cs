using System;
using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>Target matching: while the base layer's state runs from the start to the target
    /// normalized time, root motion is corrected so that the target body part ends at the match
    /// position and rotation (Unity 6000.0.71f1, tests/projects/modelroot for generic rigs,
    /// tests/projects/humanoid match.* for every AvatarTarget on humanoid rigs; on a generic rig
    /// every target matches the root).</summary>
    public partial class Animator
    {
        internal sealed class MatchState
        {
            internal Vector3 Position;
            internal Quaternion Rotation;
            internal MatchTargetWeightMask Mask;
            internal AvatarTarget Target;
            internal AnimatorPlayer Player;
            internal StateRun Run;
            internal bool CompleteOnInterruption, Finish, PredictCurrentPose;
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

        /// <summary>Queues one match at a time; a call while matching only changes what an
        /// interruption does. In the current loop of the state, a start already passed begins at the
        /// state's current time and a target already passed moves both times to the next loop; a
        /// state that does not loop ignores a target before its start. A call in a transition or with
        /// a target outside AvatarTarget does nothing but warn (tests/projects/humanoid match.*,
        /// tests/projects/modelroot).</summary>
        public void MatchTarget(Vector3 matchPosition, Quaternion matchRotation, AvatarTarget targetBodyPart, MatchTargetWeightMask weightMask, float startNormalizedTime,
            float targetNormalizedTime, bool completeMatch)
        {
            if (m_Match != null) m_Match.CompleteOnInterruption = completeMatch;
            if ((uint)targetBodyPart > (uint)AvatarTarget.RightHand)
            {
                if (logWarnings) Debug.LogWarning("Invalid Target Index");
                return;
            }
            if (m_Match != null) return;
            var p = EnsurePlayer();
            var run = p != null && p.Layers.Length > 0 ? p.Layers[0].Current : null;
            if (run == null) return;
            if (p.Layers[0].Transition != null)
            {
                if (logWarnings) Debug.LogWarning("Calling Animator.MatchTarget while in transition does not have any effect.");
                return;
            }
            double now = run.Normalized, loop = Math.Floor(now), within = now - loop;
            double start, end;
            if (within <= startNormalizedTime)
            {
                start = loop + startNormalizedTime;
                end = loop + targetNormalizedTime;
            }
            else if (targetNormalizedTime <= within)
            {
                start = loop + 1.0 + startNormalizedTime;
                end = loop + 1.0 + targetNormalizedTime;
            }
            else
            {
                start = now;
                end = loop + targetNormalizedTime;
            }
            if (targetNormalizedTime < start && !p.BaseStateLoops(run)) return;
            if (end < start) end += 1.0;
            m_Match = new MatchState { Position = matchPosition, Rotation = matchRotation, Mask = weightMask, Target = targetBodyPart,
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
