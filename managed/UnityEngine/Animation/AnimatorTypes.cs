using System;
using UnityEngine.Animations;

namespace UnityEngine
{
    /// <summary>The state of an Animator layer's current (or next) state.</summary>
    public partial struct AnimatorStateInfo
    {
        internal int m_Name;
        internal int m_Path;
        internal int m_FullPath;
        internal float m_NormalizedTime;
        internal float m_Length;
        internal float m_Speed;
        internal float m_SpeedMultiplier;
        internal int m_Tag;
        internal int m_Loop;

        public bool IsName(string name)
        {
            int hash = Animator.StringToHash(name);
            return hash == m_FullPath || hash == m_Name || hash == m_Path;
        }

        public int fullPathHash => m_FullPath;

        [Obsolete("AnimatorStateInfo.nameHash has been deprecated. Use AnimatorStateInfo.fullPathHash instead.")]
        public int nameHash => m_Path;

        public int shortNameHash => m_Name;

        public float normalizedTime => m_NormalizedTime;

        public float length => m_Length;

        public float speed => m_Speed;

        public float speedMultiplier => m_SpeedMultiplier;

        public int tagHash => m_Tag;

        public bool IsTag(string tag) => Animator.StringToHash(tag) == m_Tag;

        public bool loop => m_Loop != 0;
    }

    /// <summary>A clip playing in an Animator layer and its blend weight.</summary>
    public partial struct AnimatorClipInfo
    {
        internal AnimationClip m_Clip;
        internal float m_Weight;

        public AnimationClip clip => m_Clip;

        public float weight => m_Weight;
    }

    /// <summary>The transition an Animator layer is in.</summary>
    public partial struct AnimatorTransitionInfo
    {
        internal int m_FullPath;
        internal int m_UserName;
        internal int m_Name;
        internal bool m_HasFixedDuration;
        internal float m_Duration;
        internal float m_NormalizedTime;
        internal bool m_AnyState;
        internal int m_TransitionType;

        public bool IsName(string name)
        {
            int hash = Animator.StringToHash(name);
            return hash == m_Name || hash == m_FullPath;
        }

        public bool IsUserName(string name) => Animator.StringToHash(name) == m_UserName;

        public int fullPathHash => m_FullPath;

        public int nameHash => m_Name;

        public int userNameHash => m_UserName;

        public DurationUnit durationUnit => m_HasFixedDuration ? DurationUnit.Fixed : DurationUnit.Normalized;

        public float duration => m_Duration;

        public float normalizedTime => m_NormalizedTime;

        public bool anyState => m_AnyState;
    }

    /// <summary>A parameter of an animator controller.</summary>
    public partial class AnimatorControllerParameter
    {
        internal string m_Name = "";
        internal AnimatorControllerParameterType m_Type;
        internal float m_DefaultFloat;
        internal int m_DefaultInt;
        internal bool m_DefaultBool;

        public AnimatorControllerParameter() { }

        public string name
        {
            get => m_Name;
            set => m_Name = value;
        }

        public int nameHash => Animator.StringToHash(m_Name);

        public AnimatorControllerParameterType type
        {
            get => m_Type;
            set => m_Type = value;
        }

        public float defaultFloat
        {
            get => m_DefaultFloat;
            set => m_DefaultFloat = value;
        }

        public int defaultInt
        {
            get => m_DefaultInt;
            set => m_DefaultInt = value;
        }

        public bool defaultBool
        {
            get => m_DefaultBool;
            set => m_DefaultBool = value;
        }

        public override bool Equals(object o) =>
            o is AnimatorControllerParameter other && m_Name == other.m_Name && m_Type == other.m_Type &&
            m_DefaultFloat == other.m_DefaultFloat && m_DefaultInt == other.m_DefaultInt && m_DefaultBool == other.m_DefaultBool;

        public override int GetHashCode() => m_Name.GetHashCode();
    }

    /// <summary>A script attached to animator states or state machines: gets callbacks as the
    /// Animator enters, updates and leaves them.</summary>
    public abstract partial class StateMachineBehaviour : ScriptableObject
    {
        protected StateMachineBehaviour() { }

        public virtual void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex) { }
        public virtual void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex) { }
        public virtual void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex) { }
        public virtual void OnStateMove(Animator animator, AnimatorStateInfo stateInfo, int layerIndex) { }
        public virtual void OnStateIK(Animator animator, AnimatorStateInfo stateInfo, int layerIndex) { }
        public virtual void OnStateMachineEnter(Animator animator, int stateMachinePathHash) { }
        public virtual void OnStateMachineExit(Animator animator, int stateMachinePathHash) { }
        public virtual void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex, AnimatorControllerPlayable controller) { }
        public virtual void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex, AnimatorControllerPlayable controller) { }
        public virtual void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex, AnimatorControllerPlayable controller) { }
        public virtual void OnStateMove(Animator animator, AnimatorStateInfo stateInfo, int layerIndex, AnimatorControllerPlayable controller) { }
        public virtual void OnStateIK(Animator animator, AnimatorStateInfo stateInfo, int layerIndex, AnimatorControllerPlayable controller) { }
        public virtual void OnStateMachineEnter(Animator animator, int stateMachinePathHash, AnimatorControllerPlayable controller) { }
        public virtual void OnStateMachineExit(Animator animator, int stateMachinePathHash, AnimatorControllerPlayable controller) { }
    }

    /// <summary>An original clip of a controller and the clip that replaces it.</summary>
    public partial class AnimationClipPair
    {
        public AnimationClip originalClip;
        public AnimationClip overrideClip;

        public AnimationClipPair() { }
    }

    /// <summary>Obsolete clip-and-weight pair of the old Animator API.</summary>
    public partial struct AnimationInfo
    {
        internal AnimationClip m_Clip;
        internal float m_Weight;

        public AnimationClip clip => m_Clip;

        public float weight => m_Weight;
    }
}

