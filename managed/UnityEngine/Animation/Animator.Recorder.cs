using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>
    /// The Animator's feet pivot, SetTarget target and recorder values as Unity 6000.0.71f1 answers
    /// them (tests/projects/defaultcontrols animator.*). The feet pivot is state of an initialized
    /// Animator: 1 each time its hierarchy becomes active, any value set while it is, 0 and sets
    /// ignored while it is not (also when the component is disabled it keeps its value). SetTarget,
    /// StartRecording and StartPlayback are not implemented, so the target is the identity at the
    /// origin and there is no recording: the recorder's times read -1 and writing them does
    /// nothing, and reading the playback time warns that no playback runs. Other releases keep the
    /// previous unmeasured answers (not implemented).
    /// </summary>
    public partial class Animator
    {
        /// <summary>The recorder's times and the playback time without a recording.</summary>
        const float NoRecordingTime = -1f;

        float m_FeetPivotActive = 1f;

        public float feetPivotActive
        {
            get
            {
                if (!Compat.DefaultConstructorStateMeasured)
                {
                    Stubs.NotImplemented("UnityEngine.Animator.feetPivotActive");
                    return 0f;
                }
                return m_Initialized ? m_FeetPivotActive : 0f;
            }
            set
            {
                if (!Compat.DefaultConstructorStateMeasured)
                {
                    Stubs.NotImplemented("UnityEngine.Animator.feetPivotActive");
                    return;
                }
                if (!m_Initialized) return;
                m_FeetPivotActive = value;
                if (value != 1f && isHuman)
                    Stubs.Ignored("Animator.feetPivotActive", "is stored, but a humanoid's root motion keeps the feet pivot (1)");
            }
        }

        /// <summary>A hierarchy activation initializes the feet pivot again.</summary>
        void ResetFeetPivot() => m_FeetPivotActive = 1f;

        public Quaternion targetRotation
        {
            get
            {
                if (Compat.DefaultConstructorStateMeasured) return Quaternion.identity;
                Stubs.NotImplemented("UnityEngine.Animator.targetRotation");
                return default;
            }
        }

        public Vector3 targetPosition
        {
            get
            {
                if (Compat.DefaultConstructorStateMeasured) return Vector3.zero;
                Stubs.NotImplemented("UnityEngine.Animator.targetPosition");
                return default;
            }
        }

        public float playbackTime
        {
            get
            {
                if (!Compat.DefaultConstructorStateMeasured)
                {
                    Stubs.NotImplemented("UnityEngine.Animator.playbackTime");
                    return 0f;
                }
                Debug.LogWarning("Can't call GetPlaybackTime while not in playback mode. You must call StartPlayback before.");
                return NoRecordingTime;
            }
            set
            {
                if (!Compat.DefaultConstructorStateMeasured) Stubs.NotImplemented("UnityEngine.Animator.playbackTime");
            }
        }

        public float recorderStartTime
        {
            get
            {
                if (Compat.DefaultConstructorStateMeasured) return NoRecordingTime;
                Stubs.NotImplemented("UnityEngine.Animator.recorderStartTime");
                return 0f;
            }
            set
            {
                if (!Compat.DefaultConstructorStateMeasured) Stubs.NotImplemented("UnityEngine.Animator.recorderStartTime");
            }
        }

        public float recorderStopTime
        {
            get
            {
                if (Compat.DefaultConstructorStateMeasured) return NoRecordingTime;
                Stubs.NotImplemented("UnityEngine.Animator.recorderStopTime");
                return 0f;
            }
            set
            {
                if (!Compat.DefaultConstructorStateMeasured) Stubs.NotImplemented("UnityEngine.Animator.recorderStopTime");
            }
        }
    }
}
