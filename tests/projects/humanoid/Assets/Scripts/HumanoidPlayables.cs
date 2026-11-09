using UnityEngine;

/// <summary>The probe's controls of states without a motion and without write defaults after a
/// motion, on HumanA and on a generic Animator.</summary>
public partial class HumanoidProbe
{
    /// <summary>An extra Animator.Update(0) before step 12 (during the hold).</summary>
    static void UpdateZeroAt12(Animator animator, int step)
    {
        if (step == 12) animator.Update(0f);
    }

    /// <summary>Empty states without write defaults after a motion: HumanA's clip, then Hold after an
    /// exit time of 0.3 at once (also under B on a layer without Root, HumanNoRootNoWD, with an extra
    /// Update(0) during the hold, and back to the clip after 0.5 of the hold over 0.2 s) and over
    /// 0.2 s, and GenericMove on a generic Animator likewise, root motion applied. The humanoid
    /// plays record the root motion's deltas without the velocities (a velocity is its delta over
    /// the step's delta time, recorded by the other plays).</summary>
    static void NoWriteDefaults()
    {
        foreach (var name in new[] { "NoRootNoWD", "NoWDAlone", "NoWDFade", "NoWDBack" })
        {
            var key = char.ToLowerInvariant(name[0]) + name.Substring(1);
            Guarded(key, () => Play(key, "HumanA", "Human" + name, animator => animator.applyRootMotion = true, motion: true, locals: false,
                velocities: false, beforeStep: name == "NoWDAlone" ? UpdateZeroAt12 : null));
        }
        Guarded("generic.noWD", () => PlayGeneric("generic.noWD", "GenericNoWD", UpdateZeroAt12));
        Guarded("generic.noWDBack", () => PlayGeneric("generic.noWDBack", "GenericNoWDBack"));
    }

    static void PlayableControls()
    {
        NoWriteDefaults();
    }
}
