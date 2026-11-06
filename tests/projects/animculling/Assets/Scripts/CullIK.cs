using UnityEngine;

/// <summary>Counts OnAnimatorIK calls and pulls the left hand to a goal that moves every call.</summary>
public class CullIK : MonoBehaviour
{
    public int calls;

    void OnAnimatorIK(int layerIndex)
    {
        var animator = GetComponent<Animator>();
        calls++;
        animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 1f);
        animator.SetIKPosition(AvatarIKGoal.LeftHand, transform.position + new Vector3(0.4f, 1.2f + 0.01f * (calls % 20), 0.3f));
    }
}
