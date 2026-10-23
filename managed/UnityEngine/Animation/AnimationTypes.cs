using System;

namespace UnityEngine
{
    /// <summary>When the legacy Animation component updates: with the frame or with physics.</summary>
    public enum AnimationUpdateMode
    {
        Normal = 0,
        Fixed = 1,
    }

    public enum AnimationPlayMode
    {
        Stop = 0,
        Queue = 1,
        Mix = 2,
    }

    /// <summary>Which component fired an AnimationEvent.</summary>
    internal enum AnimationEventSource
    {
        NoSource = 0,
        Legacy = 1,
        Animator = 2,
    }

    /// <summary>A reference to an object the engine tracks (AnimationState): compares equal to null
    /// once the engine forgot it.</summary>
    public partial class TrackedReference
    {
        protected TrackedReference() { }

        /// <summary>The engine object still exists (an AnimationState not removed).</summary>
        internal virtual bool IsTrackedAlive => true;

        public static bool operator ==(TrackedReference x, TrackedReference y)
        {
            bool xNull = (object)x == null || !x.IsTrackedAlive;
            bool yNull = (object)y == null || !y.IsTrackedAlive;
            if (xNull && yNull) return true;
            if (xNull || yNull) return false;
            return ReferenceEquals(x, y);
        }

        public static bool operator !=(TrackedReference x, TrackedReference y) => !(x == y);

        public override bool Equals(object o) => o is TrackedReference other && this == other;

        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);

        public static implicit operator bool(TrackedReference exists) => exists != null;
    }

    /// <summary>A StateMachineBehaviour type marked with this is used as one instance shared by
    /// every Animator, instead of one instance per Animator.</summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed partial class SharedBetweenAnimatorsAttribute : Attribute
    {
    }

    /// <summary>A function an animation clip calls at a point of its timeline (by SendMessage to the
    /// animated GameObject).</summary>
    [Serializable]
    public sealed partial class AnimationEvent
    {
        internal float m_Time;
        internal string m_FunctionName = "";
        internal string m_StringParameter = "";
        internal Object m_ObjectReferenceParameter;
        internal float m_FloatParameter;
        internal int m_IntParameter;
        internal int m_MessageOptions;
        internal AnimationEventSource m_Source;
        internal AnimationState m_StateSender;
        internal AnimatorStateInfo m_AnimatorStateInfo;
        internal AnimatorClipInfo m_AnimatorClipInfo;

        public AnimationEvent() { }

        [Obsolete("Use stringParameter instead")]
        public string data
        {
            get => m_StringParameter;
            set => m_StringParameter = value;
        }

        public string stringParameter
        {
            get => m_StringParameter;
            set => m_StringParameter = value;
        }

        public float floatParameter
        {
            get => m_FloatParameter;
            set => m_FloatParameter = value;
        }

        public int intParameter
        {
            get => m_IntParameter;
            set => m_IntParameter = value;
        }

        public Object objectReferenceParameter
        {
            get => m_ObjectReferenceParameter;
            set => m_ObjectReferenceParameter = value;
        }

        public string functionName
        {
            get => m_FunctionName;
            set => m_FunctionName = value;
        }

        public float time
        {
            get => m_Time;
            set => m_Time = value;
        }

        public SendMessageOptions messageOptions
        {
            get => (SendMessageOptions)m_MessageOptions;
            set => m_MessageOptions = (int)value;
        }

        public bool isFiredByLegacy => m_Source == AnimationEventSource.Legacy;

        public bool isFiredByAnimator => m_Source == AnimationEventSource.Animator;

        public AnimationState animationState
        {
            get
            {
                if (!isFiredByLegacy)
                    Debug.LogError("AnimationEvent was not fired by Animation component, you shouldn't use AnimationEvent.animationState");
                return m_StateSender;
            }
        }

        public AnimatorStateInfo animatorStateInfo
        {
            get
            {
                if (!isFiredByAnimator)
                    Debug.LogError("AnimationEvent was not fired by Animator component, you shouldn't use AnimationEvent.animatorStateInfo");
                return m_AnimatorStateInfo;
            }
        }

        public AnimatorClipInfo animatorClipInfo
        {
            get
            {
                if (!isFiredByAnimator)
                    Debug.LogError("AnimationEvent was not fired by Animator component, you shouldn't use AnimationEvent.animatorClipInfo");
                return m_AnimatorClipInfo;
            }
        }

        internal AnimationEvent Copy() => (AnimationEvent)MemberwiseClone();
    }
}
