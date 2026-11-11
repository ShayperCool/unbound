using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>The probe's controls of humanoid PlayableGraphs (clips and layer mixers with humanoid
/// avatar masks, override and additive, Foot IK, animation jobs reading and writing the human
/// stream) and of empty states without write defaults.</summary>
public partial class HumanoidProbe
{
    /// <summary>The key a job records under (the current step).</summary>
    internal static string s_JobKey;

    static AnimationClip ControllerClip(string controller) =>
        Resources.Load<RuntimeAnimatorController>(controller).animationClips[0];

    /// <summary>A PlayableGraph (manual updates) on a fresh instance of a rig, its output's source
    /// built by <paramref name="build"/>: Evaluate(0), then the steps of <see cref="Play"/>, each
    /// with the body, the human pose and the root motion (its velocities unless
    /// <paramref name="velocities"/> is off).</summary>
    static void PlayGraph(string key, Func<PlayableGraph, Playable> build, bool rootMotion, string rig = "HumanA", Action<Animator> setup = null,
        bool velocities = true, Action<int> before = null, int localsEvery = 0)
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
            s_JobKey = s_IKKey = key + ".step" + step;
            before?.Invoke(step);
            graph.Evaluate(step == 0 ? 0f : step <= 24 ? 1f / 30f : 0.1f);
            Step(key + ".step" + step, animator, handler, localsEvery > 0 && step % localsEvery == 0);
            Put(key + ".step" + step + ".motion", "{\"deltaPosition\":" + V(animator.deltaPosition) + ",\"deltaRotation\":" + Q(animator.deltaRotation)
                + (velocities ? ",\"velocity\":" + V(animator.velocity) + ",\"angularVelocity\":" + V(animator.angularVelocity) : "") + "}");
        }
        s_JobKey = s_IKKey = null;
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

    /// <summary>GenericMove (root curves, no humanoid curves) on HumanA, root motion applied: alone on
    /// a controller and in a PlayableGraph.</summary>
    static void GenericOnHuman()
    {
        Guarded("genericOnHuman", () => Play("genericOnHuman", "HumanA", "HumanGenericMove", animator => animator.applyRootMotion = true,
            motion: true, locals: false));
        Guarded("genericOnHuman.graph", () => PlayGraph("genericOnHuman.graph",
            g => AnimationClipPlayable.Create(g, Resources.Load<AnimationClip>("GenericMove")), true));
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

    static readonly MuscleHandle[] s_JobMuscles =
    {
        new MuscleHandle(BodyDof.SpineFrontBack), new MuscleHandle(HeadDof.HeadLeftRight),
        new MuscleHandle(HumanPartDof.LeftLeg, LegDof.UpperLegFrontBack), new MuscleHandle(HumanPartDof.RightArm, ArmDof.ArmDownUp),
        new MuscleHandle(HumanPartDof.LeftIndex, FingerDof.ProximalDownUp),
    };

    /// <summary>What a job reads from the human stream: validity, the human scale, the feet heights,
    /// the body (local and in the stream's space), some muscles, the root motion and the feet's
    /// velocities, each goal (position, rotation, local position and rotation, from the pose,
    /// weights) and each hint (position, weight).</summary>
    internal static void JobRecord(string tag, AnimationStream stream)
    {
        string key = (s_JobKey ?? "job") + ".job." + tag;
        if (!stream.isValid || !stream.isHumanStream)
        {
            Put(key, "[" + B(stream.isValid) + "," + B(stream.isValid && stream.isHumanStream) + "]");
            return;
        }
        var h = stream.AsHuman();
        var muscles = new List<string>();
        foreach (var m in s_JobMuscles) muscles.Add(F(h.GetMuscle(m)));
        var json = "{\"valid\":" + B(h.isValid) + ",\"scale\":" + F(h.humanScale) + ",\"feet\":[" + F(h.leftFootHeight) + "," + F(h.rightFootHeight) + "]"
            + ",\"bodyLocal\":[" + V(h.bodyLocalPosition) + "," + Q(h.bodyLocalRotation) + "]"
            + ",\"body\":[" + V(h.bodyPosition) + "," + Q(h.bodyRotation) + "]"
            + ",\"muscles\":[" + string.Join(",", muscles) + "]"
            + ",\"velocity\":[" + V(stream.velocity) + "," + V(stream.angularVelocity) + "]"
            + ",\"footVelocity\":[" + V(h.leftFootVelocity) + "," + V(h.rightFootVelocity) + "]";
        for (var goal = AvatarIKGoal.LeftFoot; goal <= AvatarIKGoal.RightHand; goal++)
            json += ",\"" + goal + "\":[" + V(h.GetGoalPosition(goal)) + "," + Q(h.GetGoalRotation(goal)) + "," + V(h.GetGoalLocalPosition(goal)) + ","
                + Q(h.GetGoalLocalRotation(goal)) + "," + V(h.GetGoalPositionFromPose(goal)) + "," + Q(h.GetGoalRotationFromPose(goal)) + ","
                + F(h.GetGoalWeightPosition(goal)) + "," + F(h.GetGoalWeightRotation(goal)) + "]";
        for (var hint = AvatarIKHint.LeftKnee; hint <= AvatarIKHint.RightElbow; hint++)
            json += ",\"" + hint + "\":[" + V(h.GetHintPosition(hint)) + "," + F(h.GetHintWeightPosition(hint)) + "]";
        Put(key, json + "}");
    }

    /// <summary>Human stream jobs (an AnimationScriptPlayable, root motion applied) above HumanA's
    /// clip, one per <see cref="HumanStreamJob.Mode"/>; the read job without root motion; two
    /// chained jobs (muscles, then IK); the IK job with an IK pass script (HumanoidIK, OnAnimatorIK
    /// after the job); the read job above a mixer of HumanA's and HumanB's clips (0.5 each); and the
    /// muscle handles (count, each one's part, dof and name).</summary>
    static void Streams()
    {
        Playable Job(PlayableGraph g, Playable input, HumanStreamJob.Mode mode, string tag)
        {
            var job = AnimationScriptPlayable.Create(g, new HumanStreamJob { mode = mode, tag = tag == "job" ? 0 : 1 }, 1);
            g.Connect(input, 0, job, 0);
            job.SetInputWeight(0, 1f);
            return job;
        }
        foreach (HumanStreamJob.Mode mode in Enum.GetValues(typeof(HumanStreamJob.Mode)))
        {
            var key = "stream." + char.ToLowerInvariant(mode.ToString()[0]) + mode.ToString().Substring(1);
            Guarded(key, () => PlayGraph(key, g => Job(g, AnimationClipPlayable.Create(g, ControllerClip("HumanA")), mode, "job"), true));
        }
        Guarded("stream.readNoMotion", () => PlayGraph("stream.readNoMotion",
            g => Job(g, AnimationClipPlayable.Create(g, ControllerClip("HumanA")), HumanStreamJob.Mode.Read, "job"), false));
        Guarded("stream.muscleHandles", () =>
        {
            var handles = new MuscleHandle[MuscleHandle.muscleHandleCount];
            MuscleHandle.GetMuscleHandles(handles);
            var list = new List<string>();
            foreach (var m in handles) list.Add("[" + (int)m.humanPartDof + "," + m.dof + "," + S(m.name) + "]");
            Put("stream.muscleHandles", "{\"count\":" + MuscleHandle.muscleHandleCount + ",\"handles\":[" + string.Join(",", list) + "]}");
        });
        Guarded("stream.chained", () => PlayGraph("stream.chained",
            g => Job(g, Job(g, AnimationClipPlayable.Create(g, ControllerClip("HumanA")), HumanStreamJob.Mode.Muscles, "job"),
                HumanStreamJob.Mode.Ik, "job2"), true));
        Guarded("stream.ikPass", () => PlayGraph("stream.ikPass",
            g => Job(g, AnimationClipPlayable.Create(g, ControllerClip("HumanA")), HumanStreamJob.Mode.Ik, "job"), true,
            setup: animator => animator.gameObject.AddComponent<HumanoidIK>()));
        Guarded("stream.mixer", () => PlayGraph("stream.mixer", g =>
        {
            var m = AnimationMixerPlayable.Create(g, 2);
            g.Connect(AnimationClipPlayable.Create(g, ControllerClip("HumanA")), 0, m, 0);
            g.Connect(AnimationClipPlayable.Create(g, ControllerClip("HumanB")), 0, m, 1);
            m.SetInputWeight(0, 0.5f);
            m.SetInputWeight(1, 0.5f);
            return Job(g, m, HumanStreamJob.Mode.Read, "job");
        }, true));
    }

    static void PlayableControls()
    {
        NoWriteDefaults();
        Mixers();
        GenericOnHuman();
        Streams();
    }
}

/// <summary>An animation job on the human stream: records what it reads before and after its
/// change (<see cref="HumanoidProbe.JobRecord"/>).</summary>
public struct HumanStreamJob : IAnimationJob
{
    public enum Mode { Read, Body, BodyLocal, Muscles, Ik, IkNoSolve, LocalGoals, LookAt, Stance, HintNoSolve, WeightsNoSolve }

    public Mode mode;
    public int tag;

    public void ProcessRootMotion(AnimationStream stream) { }

    public void ProcessAnimation(AnimationStream stream)
    {
        string name = tag == 0 ? "before" : "before2";
        HumanoidProbe.JobRecord(name, stream);
        if (!stream.isHumanStream) return;
        var h = stream.AsHuman();
        switch (mode)
        {
            case Mode.Body:
                h.bodyPosition += new Vector3(0.1f, -0.05f, 0.2f);
                h.bodyRotation = h.bodyRotation * Quaternion.Euler(0f, 20f, 0f);
                break;
            case Mode.BodyLocal:
                h.bodyLocalPosition += new Vector3(0.05f, 0.02f, 0.1f);
                h.bodyLocalRotation = Quaternion.Euler(0f, 0f, 15f) * h.bodyLocalRotation;
                break;
            case Mode.Muscles:
                h.SetMuscle(new MuscleHandle(BodyDof.SpineFrontBack), 0.5f);
                h.SetMuscle(new MuscleHandle(HumanPartDof.LeftLeg, LegDof.UpperLegFrontBack), -0.4f);
                h.SetMuscle(new MuscleHandle(HumanPartDof.RightArm, ArmDof.ArmDownUp), 0.7f);
                h.SetMuscle(new MuscleHandle(HumanPartDof.LeftIndex, FingerDof.ProximalDownUp), -0.6f);
                break;
            case Mode.Ik:
            case Mode.IkNoSolve:
                h.SetGoalPosition(AvatarIKGoal.LeftHand, h.GetGoalPosition(AvatarIKGoal.LeftHand) + new Vector3(0.1f, 0.2f, 0f));
                h.SetGoalRotation(AvatarIKGoal.LeftHand, Quaternion.Euler(10f, 40f, -20f));
                h.SetGoalWeightPosition(AvatarIKGoal.LeftHand, 1f);
                h.SetGoalWeightRotation(AvatarIKGoal.LeftHand, 0.75f);
                h.SetGoalPosition(AvatarIKGoal.RightFoot, h.GetGoalPosition(AvatarIKGoal.RightFoot) + new Vector3(0f, 0.15f, 0.1f));
                h.SetGoalWeightPosition(AvatarIKGoal.RightFoot, 1f);
                h.SetHintPosition(AvatarIKHint.RightKnee, h.bodyPosition + new Vector3(-0.2f, -0.4f, 1f));
                h.SetHintWeightPosition(AvatarIKHint.RightKnee, 1f);
                if (mode == Mode.Ik) h.SolveIK();
                break;
            case Mode.LocalGoals:
                h.SetGoalLocalPosition(AvatarIKGoal.LeftFoot, h.GetGoalLocalPosition(AvatarIKGoal.LeftFoot) + new Vector3(0.05f, 0.1f, 0f));
                h.SetGoalLocalRotation(AvatarIKGoal.RightHand, Quaternion.Euler(0f, 30f, 0f) * h.GetGoalLocalRotation(AvatarIKGoal.RightHand));
                h.SetGoalWeightPosition(AvatarIKGoal.LeftFoot, 1f);
                h.SetGoalWeightRotation(AvatarIKGoal.RightHand, 1f);
                h.SolveIK();
                break;
            case Mode.LookAt:
                h.SetLookAtPosition(h.bodyPosition + new Vector3(1f, 0.8f, 2f));
                h.SetLookAtClampWeight(0.5f);
                h.SetLookAtBodyWeight(0.3f);
                h.SetLookAtHeadWeight(0.8f);
                h.SetLookAtEyesWeight(1f);
                h.SolveIK();
                break;
            case Mode.Stance:
                h.ResetToStancePose();
                break;
            case Mode.HintNoSolve:
                h.SetHintPosition(AvatarIKHint.RightKnee, h.bodyPosition + new Vector3(-0.2f, -0.4f, 1f));
                h.SetHintWeightPosition(AvatarIKHint.RightKnee, 1f);
                break;
            case Mode.WeightsNoSolve:
                h.SetGoalWeightPosition(AvatarIKGoal.LeftHand, 1f);
                h.SetGoalWeightRotation(AvatarIKGoal.LeftHand, 0.75f);
                h.SetGoalWeightPosition(AvatarIKGoal.RightFoot, 1f);
                break;
        }
        HumanoidProbe.JobRecord(tag == 0 ? "after" : "after2", stream);
    }
}

