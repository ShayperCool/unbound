using UnityEngine;

namespace DoctorCoverageControls
{
    // Uninstantiated scripts for doctor's source analysis; these do not change probe playback.
    public class UnsentIK : StateMachineBehaviour
    {
        public override void OnStateIK(Animator animator, AnimatorStateInfo stateInfo, int layerIndex) { }
    }

    public class InheritedIK : UnsentIK { }

    public class UnsentComponent : MonoBehaviour
    {
        void OnParticleUpdateJobScheduled() { }
        void OnChildRectTransformDimensionsChange() { }
        void OnAnimatorIK(int layer) { }
        // This name belongs to StateMachineBehaviour, not MonoBehaviour.
        void OnStateIK() { }
    }

    public class MessageHost : MonoBehaviour { }

    public class InheritedUnsentComponent : MessageHost
    {
        protected void OnParticleUpdateJobScheduled() { }
    }

    public class InheritedUnsentLeaf : InheritedUnsentComponent { }

    public class Ordinary
    {
        void OnStateIK() { }
        void OnParticleUpdateJobScheduled() { }
    }
}
