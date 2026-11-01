using UnityEngine;

/// <summary>Overrides bone rotations in the IK pass of the probe's BoneLocal controller.</summary>
public class HumanoidBoneLocal : MonoBehaviour
{
    void OnAnimatorIK(int layerIndex)
    {
        var animator = GetComponent<Animator>();
        animator.SetBoneLocalRotation(HumanBodyBones.LeftLowerArm, Quaternion.Euler(0f, 0f, 60f));
        animator.SetBoneLocalRotation(HumanBodyBones.Head, Quaternion.Euler(20f, -30f, 0f));
        animator.SetBoneLocalRotation(HumanBodyBones.RightUpperLeg, Quaternion.Euler(-35f, 10f, 5f));
    }
}
