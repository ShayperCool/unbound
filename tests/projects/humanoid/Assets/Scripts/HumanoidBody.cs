using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>The probe's controls of the humanoid body (RootT/RootQ) where clips blend: clips whose
/// body rotation curves have the other sign, only some of the four, or none, in blend trees,
/// transitions, layers, Direct blend trees (also with weights that do not sum to one) and
/// AnimationMixerPlayables.</summary>
public partial class HumanoidProbe
{
    /// <summary>The HumanBody* controllers (ProbeRunner.BodyBlends) on HumanA, root motion applied,
    /// with <see cref="HumanoidGoals"/> reading the body and the goals in the base layer's IK pass,
    /// and the same clips in PlayableGraphs (<see cref="BodyGraph"/>).</summary>
    static void BodyBlends()
    {
        foreach (var name in new[] { "PosTree", "NegTree", "PartialTree", "WTree", "NoneTree", "NegTransition", "NoneTransition", "NegOverride",
                     "PartialOverride", "DirectNeg", "DirectLow", "DirectHigh", "DirectNormalized", "DirectLowFootIK",
                     "DirectHighFootIK", "DirectHighTransition", "DirectLowNeg", "DirectHalfNegGoal", "DirectZeroB", "RotTree", "TTree", "EmptyLayerTransition" })
        {
            string key = "body." + char.ToLowerInvariant(name[0]) + name.Substring(1);
            Guarded(key, () => Play(key, "HumanA", "HumanBody" + name, GoalSetup, motion: true, locals: false, detail: name.Contains("Transition"),
                velocities: false));
        }
        BodyGraph("neg", ("BodyNeg", 0.35f), ("A", 0.65f));
        BodyGraph("pos", ("BodyPos", 0.35f), ("A", 0.65f));
        BodyGraph("partial", ("BodyPartial", 0.35f), ("A", 0.65f));
        BodyGraph("w", ("BodyW", 0.35f), ("A", 0.65f));
        BodyGraph("none", ("BodyNone", 0.35f), ("A", 0.65f));
        BodyGraph("low", ("BodyPos", 0.3f), ("A", 0.4f));
        BodyGraph("high", ("BodyPos", 0.8f), ("A", 0.6f));
        BodyGraph("lowNeg", ("BodyNeg", 0.3f), ("A", 0.4f));
        BodyGraph("three", ("BodyNeg", 0.25f), ("A", 0.45f), ("BodyPartial", 0.3f));
        Guarded("bodyGraph.nested", () => PlayGraph("bodyGraph.nested", g =>
        {
            var inner = Mixer(g, ("BodyNeg", 0.5f), ("BodyPos", 0.5f));
            var outer = AnimationMixerPlayable.Create(g, 2);
            g.Connect(inner, 0, outer, 0);
            g.Connect(BodyClip(g, "A"), 0, outer, 1);
            outer.SetInputWeight(0, 0.35f);
            outer.SetInputWeight(1, 0.65f);
            return outer;
        }, true, setup: GoalReader, velocities: false));
        Guarded("bodyGraph.layerPartial", () => PlayGraph("bodyGraph.layerPartial", g =>
        {
            var mixer = AnimationLayerMixerPlayable.Create(g, 2);
            g.Connect(BodyClip(g, "A"), 0, mixer, 0);
            g.Connect(BodyClip(g, "BodyPartial"), 0, mixer, 1);
            mixer.SetInputWeight(0, 1f);
            mixer.SetInputWeight(1, 0.5f);
            return mixer;
        }, true, setup: GoalReader, velocities: false));
    }

    static void GoalReader(Animator animator) => animator.gameObject.AddComponent<HumanoidGoals>();

    /// <summary>A clip playable without Foot IK: "A" is HumanA's clip, the others the .anim clips
    /// of that name.</summary>
    static AnimationClipPlayable BodyClip(PlayableGraph graph, string name)
    {
        var clip = AnimationClipPlayable.Create(graph, name == "A" ? ControllerClip("HumanA") : Resources.Load<AnimationClip>(name));
        clip.SetApplyFootIK(false);
        return clip;
    }

    static AnimationMixerPlayable Mixer(PlayableGraph graph, params (string clip, float weight)[] inputs)
    {
        var mixer = AnimationMixerPlayable.Create(graph, inputs.Length);
        for (int i = 0; i < inputs.Length; i++)
        {
            graph.Connect(BodyClip(graph, inputs[i].clip), 0, mixer, i);
            mixer.SetInputWeight(i, inputs[i].weight);
        }
        return mixer;
    }

    /// <summary>An AnimationMixerPlayable of the clips at the weights given on HumanA, root motion
    /// applied (<c>bodyGraph.{name}</c>).</summary>
    static void BodyGraph(string name, params (string clip, float weight)[] inputs) =>
        Guarded("bodyGraph." + name, () => PlayGraph("bodyGraph." + name, g => Mixer(g, inputs), true, setup: GoalReader, velocities: false));
}
