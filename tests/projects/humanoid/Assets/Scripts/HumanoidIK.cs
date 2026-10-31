using System.Globalization;
using UnityEngine;

/// <summary>Drives the IK pass of the probe's HumanAIK controller: reads the goals the clip gives,
/// sets the hands' and feet's goals, a knee hint and the look-at, and records what it read.</summary>
public class HumanoidIK : MonoBehaviour
{
    static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);
    static string V(Vector3 v) => "[" + F(v.x) + "," + F(v.y) + "," + F(v.z) + "]";
    static string Q(Quaternion q) => "[" + F(q.x) + "," + F(q.y) + "," + F(q.z) + "," + F(q.w) + "]";

    int m_Calls;

    void OnAnimatorIK(int layerIndex)
    {
        var animator = GetComponent<Animator>();
        string key = (HumanoidProbe.s_IKKey ?? "ik.frame") + ".onAnimatorIK" + m_Calls++;
        var read = "{\"layer\":" + layerIndex + ",\"bodyPosition\":" + V(animator.bodyPosition) + ",\"bodyRotation\":" + Q(animator.bodyRotation);
        for (var goal = AvatarIKGoal.LeftFoot; goal <= AvatarIKGoal.RightHand; goal++)
            read += ",\"" + goal + "\":[" + V(animator.GetIKPosition(goal)) + "," + Q(animator.GetIKRotation(goal)) + ","
                + F(animator.GetIKPositionWeight(goal)) + "," + F(animator.GetIKRotationWeight(goal)) + "]";
        read += ",\"hintLeftKnee\":" + V(animator.GetIKHintPosition(AvatarIKHint.LeftKnee)) + "}";
        HumanoidProbe.PutIK(key, read);

        var root = transform.position;
        animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 1f);
        animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, 0.75f);
        animator.SetIKPosition(AvatarIKGoal.LeftHand, root + new Vector3(0.35f, 1.3f, 0.35f));
        animator.SetIKRotation(AvatarIKGoal.LeftHand, Quaternion.Euler(10f, 40f, -20f));
        animator.SetIKPositionWeight(AvatarIKGoal.RightHand, 0.5f);
        animator.SetIKPosition(AvatarIKGoal.RightHand, root + new Vector3(-0.3f, 0.9f, 0.4f));
        animator.SetIKPositionWeight(AvatarIKGoal.RightFoot, 1f);
        animator.SetIKRotationWeight(AvatarIKGoal.RightFoot, 1f);
        animator.SetIKPosition(AvatarIKGoal.RightFoot, root + new Vector3(-0.12f, 0.25f, 0.3f));
        animator.SetIKRotation(AvatarIKGoal.RightFoot, Quaternion.Euler(-20f, 0f, 0f));
        animator.SetIKHintPositionWeight(AvatarIKHint.RightKnee, 1f);
        animator.SetIKHintPosition(AvatarIKHint.RightKnee, root + new Vector3(-0.2f, 0.6f, 1f));
        animator.SetLookAtWeight(1f, 0.3f, 0.8f, 1f, 0.5f);
        animator.SetLookAtPosition(root + new Vector3(1f, 1.8f, 2f));
        if (m_Calls == 3) animator.bodyPosition += new Vector3(0f, -0.05f, 0.02f);
    }
}
