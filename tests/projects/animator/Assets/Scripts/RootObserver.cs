using UnityEngine;

public class RootObserver : MonoBehaviour
{
    public int Mode;
    void OnAnimatorMove()
    {
        var animator = GetComponent<Animator>();
        AnimatorProbe.Event("move:" + AnimatorProbe.Number(transform.Find("Child").localPosition.x));
        if (Mode == 1 || Mode == 2)
        {
            animator.ApplyBuiltinRootMotion();
            AnimatorProbe.Event("applied:" + AnimatorProbe.Number(transform.position.x));
        }
        if (Mode == 2) animator.ApplyBuiltinRootMotion();
        if (Mode == 6)
        {
            animator.rootPosition += animator.deltaPosition;
            animator.rootRotation *= animator.deltaRotation;
        }
        if (Mode == 3)
        {
            transform.position += animator.deltaPosition;
            transform.rotation *= animator.deltaRotation;
        }
    }
}
