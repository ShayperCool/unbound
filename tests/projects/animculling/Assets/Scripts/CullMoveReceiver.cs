using UnityEngine;

/// <summary>Counts OnAnimatorMove calls, keeps the last root delta and applies the builtin root motion.</summary>
public class CullMoveReceiver : MonoBehaviour
{
    public int calls;
    public Vector3 lastDelta;

    void OnAnimatorMove()
    {
        var animator = GetComponent<Animator>();
        calls++;
        lastDelta = animator.deltaPosition;
        animator.ApplyBuiltinRootMotion();
    }
}
