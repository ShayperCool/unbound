using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>The probe's controls of humanoid PlayableGraphs (clips and layer mixers with humanoid
/// avatar masks, override and additive, Foot IK) and of empty states without write defaults.</summary>
public partial class HumanoidProbe
{
    static AnimationClip ControllerClip(string controller) =>
        Resources.Load<RuntimeAnimatorController>(controller).animationClips[0];

    /// <summary>A PlayableGraph (manual updates) on a fresh instance of a rig, its output's source
    /// built by <paramref name="build"/>: Evaluate(0), then the steps of <see cref="Play"/>, each
    /// with the body, the human pose and the root motion.</summary>
    static void PlayGraph(string key, Func<PlayableGraph, Playable> build, bool rootMotion, string rig = "HumanA", Action<Animator> setup = null)
    {
        var instance = Instantiate(Resources.Load<GameObject>(rig));
        var animator = instance.GetComponent<Animator>();
        animator.applyRootMotion = rootMotion;
        setup?.Invoke(animator);
        var graph = PlayableGraph.Create(key);
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        AnimationPlayableOutput.Create(graph, "out", animator).SetSourcePlayable(build(graph));
        graph.Play();
        var handler = new HumanPoseHandler(animator.avatar, instance.transform);
        for (int step = 0; step <= 28; step++)
        {
            graph.Evaluate(step == 0 ? 0f : step <= 24 ? 1f / 30f : 0.1f);
            Step(key + ".step" + step, animator, handler, false);
            Put(key + ".step" + step + ".motion", "{\"deltaPosition\":" + V(animator.deltaPosition) + ",\"deltaRotation\":" + Q(animator.deltaRotation)
                + ",\"velocity\":" + V(animator.velocity) + ",\"angularVelocity\":" + V(animator.angularVelocity) + "}");
        }
        handler.Dispose();
        graph.Destroy();
        Destroy(instance);
    }

    /// <summary>A layer mixer of HumanA's clip (layer 0, weight 1) and HumanB's (layer 1, weight
    /// <paramref name="weight"/>, the mask and additive mode given); one layer alone without B.</summary>
    static Playable LayerMixer(PlayableGraph graph, bool withB, string mask = null, bool additive = false, float weight = 0.5f)
    {
        var mixer = AnimationLayerMixerPlayable.Create(graph, withB ? 2 : 1);
        graph.Connect(AnimationClipPlayable.Create(graph, ControllerClip("HumanA")), 0, mixer, 0);
        mixer.SetInputWeight(0, 1f);
        if (!withB) return mixer;
        graph.Connect(AnimationClipPlayable.Create(graph, ControllerClip("HumanB")), 0, mixer, 1);
        mixer.SetInputWeight(1, weight);
        if (mask != null) mixer.SetLayerMaskFromAvatarMask(1, Resources.Load<AvatarMask>("Mask" + mask));
        mixer.SetLayerAdditive(1, additive);
        return mixer;
    }

    /// <summary>HumanA's clip on a state with Foot IK; humanoid clips in PlayableGraphs on HumanA,
    /// root motion applied unless said: HumanA's clip alone (also without applyFootIK), HumanB's
    /// alone, a layer mixer with HumanA's clip alone, HumanB's clip above it on an override layer of
    /// weight 0, 0.5 (also without root motion and without Foot IK) and 1, an AnimationMixerPlayable
    /// of both at 0.5, B through the humanoid masks Upper, NoRoot and NoFootIK (0.5), and additive
    /// (0.6) through Upper and NoRoot.</summary>
    static void Mixers()
    {
        Guarded("footIK", () => Play("footIK", "HumanA", "HumanFootIK", animator => animator.applyRootMotion = true, motion: true, locals: false,
            velocities: false));
        Guarded("mixer.clip", () => PlayGraph("mixer.clip", g => AnimationClipPlayable.Create(g, ControllerClip("HumanA")), true));
        Guarded("mixer.clipNoFootIK", () => PlayGraph("mixer.clipNoFootIK", g =>
        {
            var clip = AnimationClipPlayable.Create(g, ControllerClip("HumanA"));
            clip.SetApplyFootIK(false);
            return clip;
        }, true));
        Guarded("mixer.one", () => PlayGraph("mixer.one", g => LayerMixer(g, false), true));
        Guarded("mixer.clipB", () => PlayGraph("mixer.clipB", g => AnimationClipPlayable.Create(g, ControllerClip("HumanB")), true));
        Guarded("mixer.weight0", () => PlayGraph("mixer.weight0", g => LayerMixer(g, true, weight: 0f), true));
        Guarded("mixer.weight1", () => PlayGraph("mixer.weight1", g => LayerMixer(g, true, weight: 1f), true));
        Guarded("mixer.noIK", () => PlayGraph("mixer.noIK", g =>
        {
            var m = (AnimationLayerMixerPlayable)LayerMixer(g, true);
            for (int i = 0; i < 2; i++) ((AnimationClipPlayable)m.GetInput(i)).SetApplyFootIK(false);
            return m;
        }, true));
        foreach (var (name, footB) in new[] { ("mixer.plain", true), ("mixer.plainFootIKA", false) })
            Guarded(name, () => PlayGraph(name, g =>
            {
                var m = AnimationMixerPlayable.Create(g, 2);
                g.Connect(AnimationClipPlayable.Create(g, ControllerClip("HumanA")), 0, m, 0);
                var clipB = AnimationClipPlayable.Create(g, ControllerClip("HumanB"));
                clipB.SetApplyFootIK(footB);
                g.Connect(clipB, 0, m, 1);
                m.SetInputWeight(0, 0.5f);
                m.SetInputWeight(1, 0.5f);
                return m;
            }, true));
        Guarded("footIKTransition", () => Play("footIKTransition", "HumanA", "HumanFootIKTransition", animator => animator.applyRootMotion = true,
            motion: true, locals: false, velocities: false));
        Guarded("mixer.feetNoGoals", () => PlayGraph("mixer.feetNoGoals", g => AnimationClipPlayable.Create(g, Resources.Load<AnimationClip>("FeetNoGoals")), true));
        Guarded("mixer.noRootMassCenter", () => PlayGraph("mixer.noRootMassCenter", g => LayerMixer(g, true, "NoRoot"), true,
            setup: animator => animator.layersAffectMassCenter = true));
        Guarded("mixer.noneMotion", () => PlayGraph("mixer.noneMotion", g => LayerMixer(g, true), true));
        Guarded("mixer.none", () => PlayGraph("mixer.none", g => LayerMixer(g, true), false));
        foreach (var mask in new[] { "Upper", "NoRoot", "NoFootIK" })
            Guarded("mixer." + char.ToLowerInvariant(mask[0]) + mask.Substring(1),
                () => PlayGraph("mixer." + char.ToLowerInvariant(mask[0]) + mask.Substring(1), g => LayerMixer(g, true, mask), true));
        foreach (var mask in new[] { "Upper", "NoRoot" })
            Guarded("mixer.additive" + mask, () => PlayGraph("mixer.additive" + mask, g => LayerMixer(g, true, mask, true, 0.6f), true));
    }

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
        Mixers();
    }
}
