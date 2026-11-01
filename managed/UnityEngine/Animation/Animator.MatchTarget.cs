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

        /// <summary>Queues one match at a time; another while matching is discarded. A start
        /// before the state's time in the current loop moves the match to the next loop.</summary>
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
            double now = run.Normalized, loop = Math.Floor(now);
            double start = loop + startNormalizedTime, end = loop + targetNormalizedTime;
            if (start < now && p.LoopsInBaseLayer(run)) { start += 1.0; end += 1.0; }
            m_Match = new MatchState { Position = matchPosition, Rotation = matchRotation, Mask = weightMask, Start = start, End = end };
        }

        public void InterruptMatchTarget() => InterruptMatchTarget(true);

        /// <summary>Stops matching; with <paramref name="completeMatch"/> the root moves to the target.</summary>
        public void InterruptMatchTarget(bool completeMatch)
        {
            var match = m_Match;
            m_Match = null;
            if (match == null || !completeMatch) return;
            var t = transform;
            var w = match.Mask.positionXYZWeight;
            var p = t.position;
            t.position = new Vector3(Mathf.Lerp(p.x, match.Position.x, w.x), Mathf.Lerp(p.y, match.Position.y, w.y), Mathf.Lerp(p.z, match.Position.z, w.z));
            t.rotation = Quaternion.Slerp(t.rotation, match.Rotation, match.Mask.rotationWeight);
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
