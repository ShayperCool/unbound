using UnityEngine;

public class StateMoveObserver : StateMachineBehaviour
{
    public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex) => AnimatorProbe.Event("enter");
    public override void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex) => AnimatorProbe.Event("update");
    public override void OnStateMove(Animator animator, AnimatorStateInfo stateInfo, int layerIndex) => AnimatorProbe.Event("state_move:" + AnimatorProbe.Number(animator.transform.Find("Child").localPosition.x));
}
