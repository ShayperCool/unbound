using UnityEngine;

/// <summary>The probe's controls of states without a motion and without write defaults after a
/// motion, on HumanA and on a generic Animator.</summary>
public partial class HumanoidProbe
{
    /// <summary>Empty states without write defaults after a motion: HumanA's clip, then Hold after an
    /// exit time of 0.3 at once (also under B on a layer without Root, HumanNoRootNoWD) and over
    /// 0.2 s, and GenericMove on a generic Animator likewise, root motion applied. The humanoid
    /// plays record the root motion's deltas without the velocities (a velocity is its delta over
    /// the step's delta time, recorded by the other plays).</summary>
    static void NoWriteDefaults()
    {
        foreach (var name in new[] { "NoRootNoWD", "NoWDAlone", "NoWDFade" })
        {
            var key = char.ToLowerInvariant(name[0]) + name.Substring(1);
            Guarded(key, () => Play(key, "HumanA", "Human" + name, animator => animator.applyRootMotion = true, motion: true, locals: false,
                velocities: false));
        }
        Guarded("generic.noWD", () => PlayGeneric("generic.noWD", "GenericNoWD"));
    }

    static void PlayableControls()
    {
        NoWriteDefaults();
    }
}
