using System;
using Unbound.Runtime.Humanoid;

namespace UnityEngine
{
    /// <summary>The hints of a humanoid's limbs (knees, elbows) for the IK solve.</summary>
    public enum AvatarIKHint
    {
        LeftKnee = 0,
        RightKnee = 1,
        LeftElbow = 2,
        RightElbow = 3,
    }

    /// <summary>The humanoid IK API: goals, hints, look-at and body transform of the Animator's IK
    /// pose (world space), meant for OnAnimatorIK.</summary>
    public partial class Animator
    {
        /// <summary>The Animator's IK pose (null without a human).</summary>
        Unbound.Runtime.Humanoid.HumanPose IKPose => m_Human != null && m_Human.HasOutput ? m_Human.Output : null;

        internal bool IsInIKPass() => m_Human != null && m_Human.InIKPass;

        void CheckIfInIKPass()
        {
            if (logWarnings && !IsInIKPass())
                Debug.LogWarning("Setting and getting Body Position/Rotation, IK Goals, Lookat and BoneLocalRotation should only be done in OnAnimatorIK or OnStateIK");
        }

        static Vector3 V(F4 v) => new Vector3(v.x, v.y, v.z);

        static bool Goal(int goal) => goal >= 0 && goal < 4;

        public Vector3 bodyPosition
        {
            get
            {
                CheckIfInIKPass();
                return IKPose is { } p ? V(HumanIK.GetBodyPosition(p)) : Vector3.zero;
            }
            set
            {
                CheckIfInIKPass();
                if (IKPose is { } p) HumanIK.SetBodyPosition(p, AvatarBuild.V(value));
            }
        }

        public Quaternion bodyRotation
        {
            get
            {
                CheckIfInIKPass();
                if (!(IKPose is { } p)) return Quaternion.identity;
                var q = HumanIK.GetBodyRotation(p);
                return new Quaternion(q.x, q.y, q.z, q.w);
            }
            set
            {
                CheckIfInIKPass();
                if (IKPose is { } p) HumanIK.SetBodyRotation(p, AvatarBuild.Q(value));
            }
        }

        public Vector3 GetIKPosition(AvatarIKGoal goal)
        {
            CheckIfInIKPass();
            return IKPose is { } p && Goal((int)goal) ? V(HumanIK.GetGoalPosition(p, (int)goal)) : Vector3.zero;
        }

        public void SetIKPosition(AvatarIKGoal goal, Vector3 goalPosition)
        {
            CheckIfInIKPass();
            if (IKPose is { } p && Goal((int)goal)) HumanIK.SetGoalPosition(p, (int)goal, AvatarBuild.V(goalPosition));
        }

        public Quaternion GetIKRotation(AvatarIKGoal goal)
        {
            CheckIfInIKPass();
            if (!(IKPose is { } p) || !Goal((int)goal)) return Quaternion.identity;
            var q = HumanIK.GetGoalRotation(p, (int)goal);
            return new Quaternion(q.x, q.y, q.z, q.w);
        }

        public void SetIKRotation(AvatarIKGoal goal, Quaternion goalRotation)
        {
            CheckIfInIKPass();
            if (IKPose is { } p && Goal((int)goal)) HumanIK.SetGoalRotation(p, (int)goal, AvatarBuild.Q(goalRotation));
        }

        public float GetIKPositionWeight(AvatarIKGoal goal)
        {
            CheckIfInIKPass();
            return IKPose is { } p && Goal((int)goal) ? HumanIK.GetGoalWeightPosition(p, (int)goal) : 0f;
        }

        public void SetIKPositionWeight(AvatarIKGoal goal, float value)
        {
            CheckIfInIKPass();
            if (IKPose is { } p && Goal((int)goal)) HumanIK.SetGoalWeightPosition(p, (int)goal, value);
        }

        public float GetIKRotationWeight(AvatarIKGoal goal)
        {
            CheckIfInIKPass();
            return IKPose is { } p && Goal((int)goal) ? HumanIK.GetGoalWeightRotation(p, (int)goal) : 0f;
        }

        public void SetIKRotationWeight(AvatarIKGoal goal, float value)
        {
            CheckIfInIKPass();
            if (IKPose is { } p && Goal((int)goal)) HumanIK.SetGoalWeightRotation(p, (int)goal, value);
        }

        public Vector3 GetIKHintPosition(AvatarIKHint hint)
        {
            CheckIfInIKPass();
            return IKPose is { } p && Goal((int)hint) ? V(HumanIK.GetHintPosition(p, HumanIK.GoalOfHint((int)hint))) : Vector3.zero;
        }

        public void SetIKHintPosition(AvatarIKHint hint, Vector3 hintPosition)
        {
            CheckIfInIKPass();
            if (IKPose is { } p && Goal((int)hint)) HumanIK.SetHintPosition(p, HumanIK.GoalOfHint((int)hint), AvatarBuild.V(hintPosition));
        }

        public float GetIKHintPositionWeight(AvatarIKHint hint)
        {
            CheckIfInIKPass();
            return IKPose is { } p && Goal((int)hint) ? HumanIK.GetHintWeightPosition(p, HumanIK.GoalOfHint((int)hint)) : 0f;
        }

        public void SetIKHintPositionWeight(AvatarIKHint hint, float value)
        {
            CheckIfInIKPass();
            if (IKPose is { } p && Goal((int)hint)) HumanIK.SetHintWeightPosition(p, HumanIK.GoalOfHint((int)hint), value);
        }

        public void SetLookAtPosition(Vector3 lookAtPosition)
        {
            CheckIfInIKPass();
            if (IKPose is { } p) HumanIK.SetLookAtPosition(p, AvatarBuild.V(lookAtPosition));
        }

        public void SetLookAtWeight(float weight) => LookAtWeight(weight, 0f, 1f, 0f, 0.5f);

        public void SetLookAtWeight(float weight, float bodyWeight) => LookAtWeight(weight, bodyWeight, 1f, 0f, 0.5f);

        public void SetLookAtWeight(float weight, float bodyWeight, float headWeight) => LookAtWeight(weight, bodyWeight, headWeight, 0f, 0.5f);

        public void SetLookAtWeight(float weight, float bodyWeight, float headWeight, float eyesWeight) =>
            LookAtWeight(weight, bodyWeight, headWeight, eyesWeight, 0.5f);

        public void SetLookAtWeight(float weight, float bodyWeight, float headWeight, float eyesWeight, float clampWeight) =>
            LookAtWeight(weight, bodyWeight, headWeight, eyesWeight, clampWeight);

        void LookAtWeight(float weight, float body, float head, float eyes, float clamp)
        {
            CheckIfInIKPass();
            if (IKPose is { } p) HumanIK.SetLookAtWeight(p, weight, body, head, eyes, clamp);
        }
    }
}
