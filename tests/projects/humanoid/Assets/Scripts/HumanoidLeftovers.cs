using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>The probe's controls of target matching on humanoid body parts, bone rotations set in
/// the IK pass, translation DoF curves, feet without goal curves, and layers that do not place the
/// body (also with layersAffectMassCenter).</summary>
public partial class HumanoidProbe
{
    static readonly HumanBodyBones[] s_PartBones =
        { HumanBodyBones.Hips, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftHand, HumanBodyBones.RightHand };

    /// <summary>The world position and rotation of the GameObject and of the bones of the match
    /// targets (hips, feet, hands) of a humanoid.</summary>
    static void World(string key, Animator animator)
    {
        var parts = new List<string> { "[" + V(animator.transform.position) + "," + Q(animator.transform.rotation) + "]" };
        foreach (var bone in s_PartBones)
        {
            if (!animator.isHuman) break;
            var t = animator.GetBoneTransform(bone);
            parts.Add(t == null ? "null" : "[" + V(t.position) + "," + Q(t.rotation) + "]");
        }
        Put(key + ".world", "[" + string.Join(",", parts) + "]");
    }

    /// <summary>A controller on a fresh instance with root motion applied: the steps of
    /// <see cref="Play"/>, each with the GameObject's and the target bones' world transforms, whether
    /// it is matching, the base layer's normalized time, the root motion and what the play logged. A
    /// null rig is a plain GameObject with a generic Animator.</summary>
    static void PlayMatch(string key, string controller, Action<Animator, int> beforeStep, string rig = "HumanA", bool rootMotion = true)
    {
        s_Log.Clear();
        var instance = rig != null ? Instantiate(Resources.Load<GameObject>(rig)) : new GameObject("Generic");
        var animator = rig != null ? instance.GetComponent<Animator>() : instance.AddComponent<Animator>();
        animator.applyRootMotion = rootMotion;
        animator.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>(controller);
        for (int step = 0; step <= 28; step++)
        {
            beforeStep?.Invoke(animator, step);
            animator.Update(step == 0 ? 0f : step <= 24 ? 1f / 30f : 0.1f);
            World(key + ".step" + step, animator);
            Put(key + ".step" + step + ".match", "[" + B(animator.isMatchingTarget) + "," + F(animator.GetCurrentAnimatorStateInfo(0).normalizedTime) + ","
                + B(animator.IsInTransition(0)) + "," + V(animator.deltaPosition) + "," + Q(animator.deltaRotation) + "]");
        }
        Destroy(instance);
        Put(key + ".log", "[" + string.Join(",", s_Log.ConvertAll(S)) + "]");
    }

    static readonly Vector3 s_MatchPosition = new Vector3(0.3f, 0.2f, 1.1f);
    static readonly Quaternion s_MatchRotation = Quaternion.Euler(0f, 35f, 0f);

    static Action<Animator, int> MatchAt(int at, AvatarTarget target, Vector3 mask, float rotationWeight, float start, float end, bool complete = true) =>
        (animator, step) =>
        {
            if (step == at) animator.MatchTarget(s_MatchPosition, s_MatchRotation, target, new MatchTargetWeightMask(mask, rotationWeight), start, end, complete);
        };

    /// <summary>Target matching on HumanA's clip (1 s, looping) and others: every target, masks, a
    /// match spanning a loop, starts already passed, interruptions (by the call and by a
    /// transition), a call in a transition, without root motion, on a clip without loop time, a
    /// second call while matching (also changing completeMatch), an invalid target, on HumanB
    /// (another human scale), a target time before the start time, two layers, and a generic
    /// Animator.</summary>
    static void Matches()
    {
        var all = Vector3.one;
        foreach (var target in new[] { AvatarTarget.Root, AvatarTarget.Body, AvatarTarget.LeftFoot, AvatarTarget.RightFoot, AvatarTarget.LeftHand, AvatarTarget.RightHand })
        {
            var name = "match." + target;
            Guarded(name, () => PlayMatch(name, "HumanA", MatchAt(2, target, all, 1f, 0.2f, 0.6f)));
        }
        Guarded("match.maskXZ", () => PlayMatch("match.maskXZ", "HumanA", MatchAt(2, AvatarTarget.RightHand, new Vector3(1f, 0f, 1f), 0.5f, 0.2f, 0.6f)));
        Guarded("match.maskY", () => PlayMatch("match.maskY", "HumanA", MatchAt(2, AvatarTarget.LeftHand, new Vector3(0f, 1f, 0f), 0f, 0.2f, 0.6f)));
        Guarded("match.loop", () => PlayMatch("match.loop", "HumanA", MatchAt(2, AvatarTarget.Body, all, 1f, 0.1f, 1.3f)));
        Guarded("match.lateStart", () => PlayMatch("match.lateStart", "HumanA", MatchAt(10, AvatarTarget.LeftFoot, all, 1f, 0.1f, 0.6f)));
        Guarded("match.pastTarget", () => PlayMatch("match.pastTarget", "HumanA", MatchAt(20, AvatarTarget.Body, all, 1f, 0.1f, 0.5f)));
        Guarded("match.interrupt", () => PlayMatch("match.interrupt", "HumanA", (animator, step) =>
        {
            MatchAt(2, AvatarTarget.Body, all, 1f, 0.1f, 0.8f)(animator, step);
            if (step == 12) animator.InterruptMatchTarget(true);
        }));
        Guarded("match.interruptCancel", () => PlayMatch("match.interruptCancel", "HumanA", (animator, step) =>
        {
            MatchAt(2, AvatarTarget.Body, all, 1f, 0.1f, 0.8f)(animator, step);
            if (step == 12) animator.InterruptMatchTarget(false);
        }));
        Guarded("match.transition", () => PlayMatch("match.transition", "HumanBlendTransition", MatchAt(1, AvatarTarget.LeftHand, all, 1f, 0.05f, 0.9f)));
        Guarded("match.transitionCancel", () => PlayMatch("match.transitionCancel", "HumanBlendTransition",
            MatchAt(1, AvatarTarget.LeftHand, all, 1f, 0.05f, 0.9f, complete: false)));
        Guarded("match.inTransition", () => PlayMatch("match.inTransition", "HumanBlendTransition", MatchAt(10, AvatarTarget.Body, all, 1f, 0.5f, 0.9f)));
        Guarded("match.noRootMotion", () => PlayMatch("match.noRootMotion", "HumanA", MatchAt(2, AvatarTarget.Body, all, 1f, 0.2f, 0.6f), rootMotion: false));
        Guarded("match.once", () => PlayMatch("match.once", "HumanSetOrigOnce", MatchAt(20, AvatarTarget.Body, all, 1f, 0.1f, 0.5f)));
        Guarded("match.onceLate", () => PlayMatch("match.onceLate", "HumanSetOrigOnce", MatchAt(10, AvatarTarget.RightFoot, all, 1f, 0.1f, 0.6f)));
        Guarded("match.second", () => PlayMatch("match.second", "HumanA", (animator, step) =>
        {
            MatchAt(2, AvatarTarget.Body, all, 1f, 0.2f, 0.6f)(animator, step);
            if (step == 5) animator.MatchTarget(Vector3.zero, Quaternion.identity, AvatarTarget.LeftFoot, new MatchTargetWeightMask(all, 1f), 0.3f, 0.5f);
        }));
        Guarded("match.invalid", () => PlayMatch("match.invalid", "HumanA", MatchAt(2, (AvatarTarget)7, all, 1f, 0.2f, 0.6f)));
        Guarded("match.humanB", () => PlayMatch("match.humanB", "HumanB", MatchAt(2, AvatarTarget.LeftHand, all, 1f, 0.2f, 0.9f), rig: "HumanB"));
        Guarded("match.interruptB", () => PlayMatch("match.interruptB", "HumanB", (animator, step) =>
        {
            MatchAt(2, AvatarTarget.Body, all, 1f, 0.1f, 0.8f)(animator, step);
            if (step == 12) animator.InterruptMatchTarget(true);
        }, rig: "HumanB"));
        Guarded("match.wrap", () => PlayMatch("match.wrap", "HumanA", MatchAt(2, AvatarTarget.Body, all, 1f, 0.8f, 0.2f)));
        // A second call while matching: its completeMatch decides what the transition does.
        foreach (var (name, first) in new[] { ("match.secondCancels", true), ("match.secondCompletes", false) })
            Guarded(name, () => PlayMatch(name, "HumanBlendTransition", (animator, step) =>
            {
                MatchAt(1, AvatarTarget.LeftHand, all, 1f, 0.05f, 0.9f, complete: first)(animator, step);
                if (step == 4) animator.MatchTarget(Vector3.zero, Quaternion.identity, AvatarTarget.Body, new MatchTargetWeightMask(all, 1f), 0.1f, 0.5f, !first);
            }));
        // Two layers: B on an override layer of weight 0.5 above the base layer's A.
        Guarded("match.layers", () => PlayMatch("match.layers", "HumanBlendOverride", MatchAt(2, AvatarTarget.Body, all, 1f, 0.2f, 0.6f)));
        // The same with B (2/3 s) a loop ahead of the base layer during the match.
        Guarded("match.layersLoop", () => PlayMatch("match.layersLoop", "HumanBlendOverride", MatchAt(20, AvatarTarget.Body, all, 1f, 0.7f, 0.9f)));
        // A generic Animator (GenericMove, root motion applied): a body part target and the root.
        foreach (var target in new[] { AvatarTarget.Root, AvatarTarget.LeftHand })
        {
            var name = "match.generic" + target;
            Guarded(name, () => PlayMatch(name, "GenericTwoStates", MatchAt(2, target, all, 1f, 0.2f, 0.6f), rig: null));
        }
    }

    /// <summary>Mode of <see cref="HumanoidBoneRotation"/>: 0 rotations in every IK pass, 1 one
    /// rotation per layer (HumanAIK2).</summary>
    internal static int s_BoneMode;

    /// <summary>SetBoneLocalRotation in the IK pass (<see cref="HumanoidBoneRotation"/>) on HumanA and
    /// HumanB, on two IK layers, and called outside the IK pass.</summary>
    static void BoneRotations()
    {
        foreach (var (key, rig, controller, mode) in new[] { ("boneRot", "HumanA", "HumanAIK", 0), ("boneRotB", "HumanB", "HumanAIK", 0),
                     ("boneRotLayers", "HumanA", "HumanAIK2", 1) })
        {
            s_Log.Clear();
            s_BoneMode = mode;
            Guarded(key, () => Play(key, rig, controller, animator => animator.gameObject.AddComponent<HumanoidBoneRotation>(),
                localsEvery: 4));
            Put(key + ".log", "[" + string.Join(",", s_Log.ConvertAll(S)) + "]");
        }
        s_Log.Clear();
        Guarded("boneRotOutside", () => Play("boneRotOutside", "HumanA", "HumanAIK", locals: false, beforeStep: (animator, step) =>
        {
            if (step >= 3) animator.SetBoneLocalRotation(HumanBodyBones.Spine, Quaternion.Euler(0f, 30f, 0f));
        }));
        Put("boneRotOutside.log", "[" + string.Join(",", s_Log.ConvertAll(S)) + "]");
    }

    /// <summary>HumanA's description again with translation DoF (built at run time).</summary>
    static Avatar TDoFAvatar(GameObject instance)
    {
        var description = instance.GetComponent<Animator>().avatar.humanDescription;
        description.hasTranslationDoF = true;
        var avatar = AvatarBuilder.BuildHumanAvatar(instance, description);
        avatar.name = "HumanATDoF";
        return avatar;
    }

    /// <summary>Translation DoF: clips imported with it (HumanT), TDoFMuscles.anim (muscles and translation DoF curves) and
    /// TDoFMirror.anim (the same, mirrored) on HumanA with an avatar with translation DoF (built at
    /// run time) and with its own (without).</summary>
    static void TranslationDoFCurves()
    {
        // HumanT: imported with translation DoF from a rig whose take translates bones; its clips
        // played on HumanA (no translation DoF) and HumanA's clip on HumanT.
        Guarded("tdof.avatarT", () => Avatar("tdof.avatarT", Resources.Load<GameObject>("HumanT").GetComponent<Animator>().avatar));
        foreach (var clip in Resources.LoadAll<AnimationClip>("HumanT")) Guarded("HumanT.clip." + clip.name, () => Clip("HumanT.clip." + clip.name, clip));
        Guarded("tdof.playTOnA", () => Play("tdof.playTOnA", "HumanA", "HumanT", localsEvery: 4));
        Guarded("tdof.playTMirrorOnA", () => Play("tdof.playTMirrorOnA", "HumanA", "HumanTMirror", localsEvery: 4));
        Guarded("tdof.playAOnT", () => Play("tdof.playAOnT", "HumanT", "HumanA", localsEvery: 4));
        foreach (var name in new[] { "TDoFMuscles", "TDoFMirror" })
        {
            Guarded("anim." + name, () => Clip("anim." + name, Resources.Load<AnimationClip>(name)));
            Guarded("tdof." + name, () => Play("tdof." + name, "HumanA", "Human" + name, animator => animator.avatar = TDoFAvatar(animator.gameObject),
                motion: true, localsEvery: 4));
            Guarded("tdof." + name + ".noTDoF", () => Play("tdof." + name + ".noTDoF", "HumanA", "Human" + name, motion: true, localsEvery: 4));
        }
    }

    /// <summary>Feet without foot goal curves (height from feet), a layer without Root over an empty
    /// base state and over a generic clip, layersAffectMassCenter (the API, and layers without Root
    /// that then place the body), and loop pose on an additive layer's clip (LoopMuscles and
    /// HumanA's LoopPose clip).</summary>
    static void FeetAndMassCenter()
    {
        foreach (var name in new[] { "AdditiveLoopPose", "AdditiveLoopPoseModel" })
        {
            var key = char.ToLowerInvariant(name[0]) + name.Substring(1);
            Guarded(key, () => Play(key, "HumanA", "Human" + name, locals: false));
        }
        foreach (var name in new[] { "FeetNoGoals", "FeetLeftGoal", "NoRootOverEmpty", "NoRootOverGeneric" })
        {
            var key = char.ToLowerInvariant(name[0]) + name.Substring(1);
            Guarded(key, () => Play(key, "HumanA", "Human" + name, motion: true, locals: false));
        }
        foreach (var name in new[] { "FeetNoGoals", "FeetLeftGoal" })
            Guarded("anim." + name, () => Clip("anim." + name, Resources.Load<AnimationClip>(name)));
        Guarded("massCenter.api", () =>
        {
            var instance = Instantiate(Resources.Load<GameObject>("HumanA"));
            var animator = instance.GetComponent<Animator>();
            bool before = animator.layersAffectMassCenter;
            animator.layersAffectMassCenter = true;
            bool after = animator.layersAffectMassCenter;
            var plain = new GameObject("Plain").AddComponent<Animator>();
            plain.layersAffectMassCenter = true;
            Put("massCenter.api", "[" + B(before) + "," + B(after) + "," + B(plain.layersAffectMassCenter) + "]");
            Destroy(plain.gameObject);
            Destroy(instance);
        });
        foreach (var name in new[] { "MaskOverrideNoRoot", "MaskAdditiveNoRoot", "MaskOverrideUpper", "NoRootOverEmpty" })
        {
            var key = "massCenter." + char.ToLowerInvariant(name[0]) + name.Substring(1);
            Guarded(key, () => Play(key, "HumanA", "Human" + name, animator => animator.layersAffectMassCenter = true, locals: false));
        }
    }

    internal static string Qs(Quaternion q) => Q(q);

    static void Leftovers()
    {
        Matches();
        BoneRotations();
        TranslationDoFCurves();
        FeetAndMassCenter();
        PlayableControls();
    }


    /// <summary>A clip made at run time whose humanoid curves are set through the API: a muscle
    /// of the spine (rising), of the left arm and of the left hand (constant), the body's height
    /// and a turn of the body, through AnimationClip.SetCurve ("setCurve"), through
    /// AnimationUtility.SetEditorCurve ("editorCurve"), and through SetCurve with the clip's
    /// Mirror setting on ("mirror"; "mirrorFootIK" with foot IK). Each clip's humanMotion, length
    /// and curve bindings, then three evaluations of a manual graph (0, 0.5 and 0.25 s) of the
    /// clip, without foot IK but for "mirrorFootIK", on HumanA without root motion, each with the
    /// transforms' rotations and the human pose.</summary>
    static void ApiCurves()
    {
        foreach (var mode in new[] { "setCurve", "editorCurve", "mirror", "mirrorFootIK" })
        {
            string key = "apiCurves." + mode;
            var clip = new AnimationClip { name = key };
            void Set(string name, AnimationCurve curve)
            {
#if UNITY_EDITOR
                if (mode == "editorCurve")
                {
                    UnityEditor.AnimationUtility.SetEditorCurve(clip, UnityEditor.EditorCurveBinding.FloatCurve("", typeof(Animator), name), curve);
                    return;
                }
#endif
                clip.SetCurve("", typeof(Animator), name, curve);
            }
            Set("Spine Front-Back", AnimationCurve.Linear(0f, -0.5f, 1f, 0.5f));
            Set("Left Arm Down-Up", AnimationCurve.Constant(0f, 1f, 0.8f));
            Set("LeftHand.Thumb.1 Stretched", AnimationCurve.Constant(0f, 1f, -0.6f));
            Set("RootT.x", AnimationCurve.Constant(0f, 1f, 0f));
            Set("RootT.y", AnimationCurve.Constant(0f, 1f, 0.9f));
            Set("RootT.z", AnimationCurve.Constant(0f, 1f, 0f));
            var turn = Quaternion.Euler(0f, 20f, 0f);
            Set("RootQ.x", AnimationCurve.Constant(0f, 1f, turn.x));
            Set("RootQ.y", AnimationCurve.Constant(0f, 1f, turn.y));
            Set("RootQ.z", AnimationCurve.Constant(0f, 1f, turn.z));
            Set("RootQ.w", AnimationCurve.Constant(0f, 1f, turn.w));
#if UNITY_EDITOR
            if (mode.StartsWith("mirror", StringComparison.Ordinal))
            {
                var settings = UnityEditor.AnimationUtility.GetAnimationClipSettings(clip);
                settings.mirror = true;
                UnityEditor.AnimationUtility.SetAnimationClipSettings(clip, settings);
            }
            var bindings = UnityEditor.AnimationUtility.GetCurveBindings(clip)
                .Select(b => b.path + "|" + b.type.Name + "|" + b.propertyName).OrderBy(b => b, StringComparer.Ordinal).Select(S);
            Put(key + ".clip", "{\"humanMotion\":" + B(clip.humanMotion) + ",\"length\":" + F(clip.length)
                + ",\"bindings\":[" + string.Join(",", bindings) + "]}");
#endif
            var instance = Instantiate(Resources.Load<GameObject>("HumanA"));
            var animator = instance.GetComponent<Animator>();
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
            var graph = PlayableGraph.Create(key);
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            // Without foot IK: the legs follow their (absent) muscle curves, not goals.
            var playable = AnimationClipPlayable.Create(graph, clip);
            playable.SetApplyFootIK(mode == "mirrorFootIK");
            AnimationPlayableOutput.Create(graph, "out", animator).SetSourcePlayable(playable);
            graph.Play();
            var handler = new HumanPoseHandler(animator.avatar, instance.transform);
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            for (int step = 0; step < 3; step++)
            {
                graph.Evaluate(step == 0 ? 0f : step == 1 ? 0.5f : 0.25f);
                Rotations(key + ".step" + step + ".rotations", instance.transform, hips);
                var pose = new HumanPose();
                handler.GetHumanPose(ref pose);
                Pose(key + ".step" + step + ".pose", pose);
            }
            handler.Dispose();
            graph.Destroy();
            Destroy(instance);
        }
    }
}

/// <summary>Sets human bones' local rotations in OnAnimatorIK (<see cref="HumanoidProbe.s_BoneMode"/>)
/// and records the bones' local rotations it saw there.</summary>
public class HumanoidBoneRotation : MonoBehaviour
{
    int m_Calls;

    void OnAnimatorIK(int layerIndex)
    {
        var animator = GetComponent<Animator>();
        var spine = animator.GetBoneTransform(HumanBodyBones.Spine);
        var head = animator.GetBoneTransform(HumanBodyBones.Head);
        HumanoidProbe.PutIK((HumanoidProbe.s_IKKey ?? "boneRot.frame") + ".onAnimatorIK" + m_Calls++,
            "[" + layerIndex + "," + HumanoidProbe.Qs(spine.localRotation) + "," + HumanoidProbe.Qs(head.localRotation) + "]");
        if (HumanoidProbe.s_BoneMode == 1)
        {
            if (layerIndex == 0) animator.SetBoneLocalRotation(HumanBodyBones.Spine, Quaternion.Euler(0f, 30f, 10f));
            else animator.SetBoneLocalRotation(HumanBodyBones.Head, Quaternion.Euler(20f, -10f, 0f));
            return;
        }
        animator.SetBoneLocalRotation(HumanBodyBones.Spine, Quaternion.Euler(0f, 30f, 10f));
        animator.SetBoneLocalRotation(HumanBodyBones.Hips, Quaternion.Euler(10f, 0f, 0f));
        animator.SetBoneLocalRotation(HumanBodyBones.Head, Quaternion.Euler(-15f, 25f, 0f));
        animator.SetBoneLocalRotation(HumanBodyBones.UpperChest, Quaternion.Euler(5f, 5f, 5f));
        animator.SetBoneLocalRotation(HumanBodyBones.LeftLowerArm, Quaternion.Euler(10f, 60f, 20f));
        animator.SetBoneLocalRotation(HumanBodyBones.LeftUpperLeg, Quaternion.Euler(-30f, 0f, 10f));
        animator.SetBoneLocalRotation(HumanBodyBones.LeftIndexProximal, Quaternion.Euler(0f, 0f, 40f));
    }
}
