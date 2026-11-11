using System.Globalization;
using UnityEngine;

/// <summary>The probe's controls of IK goals an Animator Controller blends: Foot IK and the goals an
/// IK pass reads during transitions, in blend trees and under override and additive layers.</summary>
public partial class HumanoidProbe
{
    /// <summary>The HumanGoals* controllers (ProbeRunner.GoalBlends) on HumanA, root motion applied,
    /// each with <see cref="HumanoidGoals"/> reading the goals in the base layer's IK pass; the
    /// transition also on HumanB. The plays with a transition record each layer's states and clips
    /// in detail.</summary>
    static void GoalBlends()
    {
        foreach (var name in new[] { "Transition", "Tree", "TreeTransition", "TreeSource", "BackTransition", "NoGoals", "Override", "OverrideFootIK",
                     "OverrideNoFootIK", "OverrideUpper", "Additive", "AdditiveFootIK" })
        {
            string key = "goals." + char.ToLowerInvariant(name[0]) + name.Substring(1);
            bool detail = name.Contains("Transition") || name == "TreeSource" || name == "NoGoals";
            Guarded(key, () => Play(key, "HumanA", "HumanGoals" + name, GoalSetup, motion: true, locals: false, detail: detail, velocities: false));
        }
        Guarded("goals.transitionB", () => Play("goals.transitionB", "HumanB", "HumanGoalsTransition", GoalSetup, motion: true, locals: false,
            velocities: false));
    }

    static void GoalSetup(Animator animator)
    {
        animator.applyRootMotion = true;
        animator.gameObject.AddComponent<HumanoidGoals>();
    }
}

/// <summary>Records the body and the goals an IK pass reads, without setting any.</summary>
public class HumanoidGoals : MonoBehaviour
{
    static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);
    static string V(Vector3 v) => "[" + F(v.x) + "," + F(v.y) + "," + F(v.z) + "]";

    int m_Calls;

    void OnAnimatorIK(int layerIndex)
    {
        var animator = GetComponent<Animator>();
        var read = "{\"layer\":" + layerIndex + ",\"bodyPosition\":" + V(animator.bodyPosition) + ",\"bodyRotation\":" + HumanoidProbe.Qs(animator.bodyRotation);
        for (var goal = AvatarIKGoal.LeftFoot; goal <= AvatarIKGoal.RightHand; goal++)
            read += ",\"" + goal + "\":[" + V(animator.GetIKPosition(goal)) + "," + HumanoidProbe.Qs(animator.GetIKRotation(goal)) + ","
                + F(animator.GetIKPositionWeight(goal)) + "," + F(animator.GetIKRotationWeight(goal)) + "]";
        HumanoidProbe.PutIK((HumanoidProbe.s_IKKey ?? "goals.frame") + ".goals" + m_Calls++, read + "}");
    }
}
