using UnityEngine;

public class MatchReviewMove : MonoBehaviour
{
    public bool Apply;
    public int Count;
    public Vector3 LastDelta, LastPosition;
    public bool LastMatching;

    void OnAnimatorMove()
    {
        var animator = GetComponent<Animator>();
        Count++; LastDelta = animator.deltaPosition; LastPosition = transform.position;
        LastMatching = animator.isMatchingTarget;
        if (Apply) animator.ApplyBuiltinRootMotion();
    }
}
