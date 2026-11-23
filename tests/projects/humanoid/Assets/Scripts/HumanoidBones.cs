using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>Stream handles of a humanoid's bones in an animation job (the probe's part of
/// docs/COMPATIBILITY_GAPS.md "optimizeGameObjects / exposed transforms ... stream handles").</summary>
public partial class HumanoidProbe
{
    static readonly string[] s_BoneNames = { "Hips", "Spine", "LeftLowerArm", "Head", "LeftHand" };

    static Transform FindBone(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>())
            if (t.name == name) return t;
        return null;
    }

    /// <summary>What a job reads through the bones' stream handles: validity, whether resolved,
    /// the local position (times 100, the inverse of the rig's root scale, so that the
    /// comparison's relative tolerance applies), rotation and scale, and the world position and
    /// rotation.</summary>
    internal static void BoneRecord(string tag, AnimationStream stream, ref HumanBoneJob job)
    {
        var parts = new List<string>();
        for (int i = 0; i < s_BoneNames.Length; i++)
        {
            var h = job.Handle(i);
            bool valid = h.IsValid(stream);
            if (!valid)
            {
                parts.Add("\"" + s_BoneNames[i] + "\":{\"valid\":false}");
                continue;
            }
            parts.Add("\"" + s_BoneNames[i] + "\":{\"valid\":true,\"local\":[" + V(h.GetLocalPosition(stream) * 100f) + "," + Q(h.GetLocalRotation(stream)) + ","
                + V(h.GetLocalScale(stream)) + "],\"world\":[" + V(h.GetPosition(stream)) + "," + Q(h.GetRotation(stream)) + "]}");
        }
        Put((s_JobKey ?? "job") + ".bones." + tag, "{" + string.Join(",", parts) + "}");
    }

    internal static void CallbackRoot(string phase, Transform root)
    {
        Put((s_JobKey ?? "callback") + ".moved." + phase, "[" + V(root.position) + "," + Q(root.rotation) + "]");
    }

    static MuscleHandle[] s_AllMuscleHandles;

    internal static void RecordAllMuscles(AnimationStream stream)
    {
        var pose = stream.AsHuman();
        var handles = s_AllMuscleHandles;
        var muscles = new float[handles.Length];
        for (int i = 0; i < handles.Length; i++) muscles[i] = pose.GetMuscle(handles[i]);
        Put((s_JobKey ?? "job") + ".allMuscles", Floats(muscles));
    }

    internal static void ElbowHintRecord(string tag, AnimationStream stream)
    {
        Put((s_JobKey ?? "job") + ".elbowHint." + tag, V(stream.AsHuman().GetHintPosition(AvatarIKHint.LeftElbow)));
    }

    internal static void BoneMaskRecord(AnimationStream stream, ref HumanBoneJob job)
    {
        var values = new List<string>();
        string Bool(bool b) => b ? "true" : "false";
        for (int i = 0; i < s_BoneNames.Length; i++)
        {
            var handle = job.Handle(i);
            values.Add(S(s_BoneNames[i]) + ":[" + Bool(handle.GetPositionReadMask(stream)) + ","
                + Bool(handle.GetRotationReadMask(stream)) + "," + Bool(handle.GetScaleReadMask(stream)) + "]");
        }
        Put((s_JobKey ?? "job") + ".boneMasks", "{" + string.Join(",", values) + "}");
    }

    /// <summary>What a job reads through the stream handle of an object added under the left
    /// hand (bone.prop): validity, the local position (times 100), rotation, and the world
    /// position and rotation.</summary>
    internal static void PropRecord(AnimationStream stream, TransformStreamHandle prop)
    {
        bool valid = prop.IsValid(stream);
        Put((s_JobKey ?? "job") + ".prop", !valid ? "{\"valid\":false}" : "{\"valid\":true,\"local\":[" + V(prop.GetLocalPosition(stream) * 100f) + ","
            + Q(prop.GetLocalRotation(stream)) + "],\"world\":[" + V(prop.GetPosition(stream)) + "," + Q(prop.GetRotation(stream)) + "]}");
    }

    /// <summary>The stance job of the stream controls (<c>stream.stance</c>: a job resets the
    /// stream to the stance pose each update above HumanA's clip playable, whose Foot IK then
    /// pulls the legs to the clip's foot goals) without root motion applied, so the Animator
    /// stays at the origin: the control beside stream.stance, where it moves away from it.
    /// The transforms' locals are recorded every fourth step. It runs after the other
    /// controls of the probe, which is why it is built here and not with the stream jobs.</summary>
    static void StanceInPlace()
    {
        Guarded("stream.stanceInPlace", () => PlayGraph("stream.stanceInPlace", g =>
        {
            var playable = AnimationScriptPlayable.Create(g, new HumanStreamJob { mode = HumanStreamJob.Mode.Stance, tag = 0 }, 1);
            g.Connect(AnimationClipPlayable.Create(g, ControllerClip("HumanA")), 0, playable, 0);
            playable.SetInputWeight(0, 1f);
            return playable;
        }, false, localsEvery: 4));
    }

    /// <summary>Bone handle jobs (an AnimationScriptPlayable, root motion applied) above HumanA's
    /// clip, one per <see cref="HumanBoneJob.Mode"/>, each step with what the job reads before and
    /// after its change, the human stream's body and muscles after it, and the final pose with
    /// the transforms' locals every fourth step. RotateThenHuman turns the lower arm, reads the
    /// human stream and the bones again (humanBetween, between), then turns the head. Prop only
    /// reads, also the handle of an object added under the left hand (<see cref="PropRecord"/>).
    /// Solve moves the left hand's goal and solves the IK; RotateThenSolve turns the lower arm,
    /// reads the human stream, then moves the left hand's goal and solves the IK; SolveTwice
    /// moves the goal and solves, reads the human stream and solves again: a solve after the
    /// pose was read back on the rig's root scaled by 100.</summary>
    static void BoneExternalEditRecord(Transform bone) =>
        Put(s_JobKey + ".externalArmRotation", Q(bone.localRotation));

    static void BoneHandles()
    {
        s_AllMuscleHandles = new MuscleHandle[MuscleHandle.muscleHandleCount];
        MuscleHandle.GetMuscleHandles(s_AllMuscleHandles);
        foreach (HumanBoneJob.Mode mode in System.Enum.GetValues(typeof(HumanBoneJob.Mode)))
        {
            bool noInput = mode >= HumanBoneJob.Mode.NoInputNoop;
            var key = "bone." + char.ToLowerInvariant(mode.ToString()[0]) + mode.ToString().Substring(1);
            Guarded(key, () =>
            {
                Animator bound = null;
                PlayGraph(key, g =>
                {
                    var job = new HumanBoneJob { mode = mode };
                    for (int i = 0; i < s_BoneNames.Length; i++) job.Bind(i, bound.BindStreamTransform(FindBone(bound.transform, s_BoneNames[i])));
                    if (mode == HumanBoneJob.Mode.Prop)
                    {
                        // An object under the left hand that is not in the avatar, offset in the
                        // hand's frame (rig units are 0.01 of the world's).
                        var prop = new GameObject("Prop").transform;
                        prop.SetParent(FindBone(bound.transform, "LeftHand"), false);
                        prop.localPosition = new Vector3(0.001f, 0.002f, 0f);
                        prop.localRotation = Quaternion.Euler(0f, 0f, 30f);
                        job.BindProp(bound.BindStreamTransform(prop));
                    }
                    if (noInput)
                        return AnimationScriptPlayable.Create(g, job);
                    var playable = AnimationScriptPlayable.Create(g, job, 1);
                    var clip = AnimationClipPlayable.Create(g, ControllerClip("HumanA"));
                    if (mode == HumanBoneJob.Mode.MasksLayerWithoutHead)
                    {
                        var mixer = AnimationLayerMixerPlayable.Create(g, 2);
                        g.Connect(clip, 0, mixer, 1);
                        mixer.SetInputWeight(1, 1f);
                        var mask = new AvatarMask();
                        for (int part = 0; part < (int)AvatarMaskBodyPart.LastBodyPart; part++)
                            mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)part, part != (int)AvatarMaskBodyPart.Head);
                        mixer.SetLayerMaskFromAvatarMask(1, mask);
                        g.Connect(mixer, 0, playable, 0);
                    }
                    else if (mode == HumanBoneJob.Mode.LayerNoRootRead || mode == HumanBoneJob.Mode.LayerNoRootTurn
                        || mode == HumanBoneJob.Mode.LayerNoRootBody || mode == HumanBoneJob.Mode.LayerNoRootMuscles
                        || mode == HumanBoneJob.Mode.LayerNoRootMuscleEdit)
                        g.Connect(LayerMixer(g, true, "NoRoot"), 0, playable, 0);
                    else if (mode == HumanBoneJob.Mode.LayerFullRootRead)
                        g.Connect(LayerMixer(g, true), 0, playable, 0);
                    else
                        g.Connect(clip, 0, playable, 0);
                    playable.SetInputWeight(0, 1f);
                    return playable;
                }, !noInput && mode != HumanBoneJob.Mode.ScriptedRootBuiltinWithApplyOff, setup: animator =>
                {
                    bound = animator;
                    switch (mode)
                    {
                        case HumanBoneJob.Mode.ScriptedRootOffset:
                        case HumanBoneJob.Mode.ScriptedRootOffsetAndTurnBone:
                            animator.gameObject.AddComponent<HumanoidScriptedJobRoot>().action = 1;
                            break;
                        case HumanBoneJob.Mode.ScriptedRootBuiltin:
                        case HumanBoneJob.Mode.ScriptedRootBuiltinWithApplyOff:
                            animator.gameObject.AddComponent<HumanoidScriptedJobRoot>().action = 2;
                            break;
                        case HumanBoneJob.Mode.ScriptedRootUnmoved:
                            animator.gameObject.AddComponent<HumanoidScriptedJobRoot>();
                            break;
                    }
                    if (mode == HumanBoneJob.Mode.KinematicRoot || mode == HumanBoneJob.Mode.KinematicRootArmEdit)
                    {
                        var body = animator.gameObject.AddComponent<Rigidbody>();
                        body.isKinematic = true;
                        body.useGravity = false;
                    }
                }, velocities: false, before: noInput ? step =>
                {
                    if (mode == HumanBoneJob.Mode.NoInputHumanRead && step == 2)
                    {
                        var arm = FindBone(bound.transform, "LeftLowerArm");
                        arm.localRotation *= Quaternion.Euler(0f, 0f, 45f);
                        BoneExternalEditRecord(arm);
                    }
                } : null, localsEvery: 4);
            });
        }
    }
}

/// <summary>An animation job that reads the stream handles of five bones of a humanoid and changes
/// one of them.</summary>
public struct HumanBoneJob : IAnimationJob
{
    public enum Mode
    {
        Read, RotateLocal, RotateWorld, MoveHips, MoveArm, Scale, RotateThenHuman,
        HumanThenRead, Prop, Solve, RotateThenSolve, SolveTwice, ScaleHips, ScaleHipsThenHuman,
        HipsTrs, ScaleHipsWorldWrite, ScaleHipsHints, ScaleHipsThenSolve, ScaleSpineThenSameHips,
        ScaleHipsFirstHint, ScaleHipsUniformThenSolve, ScaleHipsUniformFirstHint, ScaleHipsReadTurnThenHint,
        MaskedHipsTrs, MaskedArmTrs, MaskedHeadGlobal, MasksLayerWithoutHead,
        KinematicRoot, KinematicRootArmEdit, ScriptedRootOffset, ScriptedRootBuiltin, ScriptedRootUnmoved,
        ScriptedRootOffsetAndTurnBone, ScriptedRootBuiltinWithApplyOff,
        LayerNoRootRead, LayerNoRootTurn, LayerNoRootBody, LayerFullRootRead,
        BaseMuscles, LayerNoRootMuscles, LayerNoRootMuscleEdit,
        NoInputNoop, NoInputHumanRead, NoInputHumanWrite, NoInputRootPhaseHuman,
        NoInputBoneWrite, NoInputWriteThenRead
    }

    public Mode mode;
    TransformStreamHandle hips, spine, arm, head, hand, prop;

    /// <summary>Mode Prop: the handle of an object added under the left hand.</summary>
    public void BindProp(TransformStreamHandle handle) => prop = handle;

    public void Bind(int i, TransformStreamHandle handle)
    {
        switch (i)
        {
            case 0: hips = handle; break;
            case 1: spine = handle; break;
            case 2: arm = handle; break;
            case 3: head = handle; break;
            default: hand = handle; break;
        }
    }

    public TransformStreamHandle Handle(int i) => i == 0 ? hips : i == 1 ? spine : i == 2 ? arm : i == 3 ? head : hand;

    public void ProcessRootMotion(AnimationStream stream)
    {
        if (mode == Mode.NoInputRootPhaseHuman) HumanoidProbe.JobRecord("rootPhase", stream);
    }

    /// <summary>The left hand's goal moved by (10, 20, 0) world units (a tenth and a fifth of the
    /// rig's local unit of 100) at full weight, and the IK solved.</summary>
    static void SolveLeftHand(AnimationStream stream)
    {
        var h = stream.AsHuman();
        h.SetGoalPosition(AvatarIKGoal.LeftHand, h.GetGoalPosition(AvatarIKGoal.LeftHand) + new Vector3(10f, 20f, 0f));
        h.SetGoalWeightPosition(AvatarIKGoal.LeftHand, 1f);
        h.SolveIK();
    }

    public void ProcessAnimation(AnimationStream stream)
    {
        if (mode == Mode.NoInputNoop || mode == Mode.NoInputRootPhaseHuman) return;
        if (mode == Mode.MasksLayerWithoutHead)
        {
            HumanoidProbe.BoneMaskRecord(stream, ref this);
            return;
        }
        if (mode == Mode.HumanThenRead && stream.isHumanStream)
            stream.AsHuman().SetMuscle(new MuscleHandle(BodyDof.SpineFrontBack), 0.5f);
        HumanoidProbe.BoneRecord("before", stream, ref this);
        switch (mode)
        {
            case Mode.KinematicRootArmEdit:
            case Mode.RotateLocal:
                arm.SetLocalRotation(stream, arm.GetLocalRotation(stream) * Quaternion.Euler(0f, 0f, 40f));
                break;
            case Mode.RotateThenHuman:
                // A bone, then the pose read back, then the bones again and another bone.
                arm.SetLocalRotation(stream, arm.GetLocalRotation(stream) * Quaternion.Euler(0f, 0f, 40f));
                HumanoidProbe.JobRecord("humanBetween", stream);
                HumanoidProbe.BoneRecord("between", stream, ref this);
                head.SetRotation(stream, Quaternion.Euler(0f, 45f, 0f));
                break;
            case Mode.ScriptedRootOffsetAndTurnBone:
            case Mode.LayerNoRootTurn:
            case Mode.RotateWorld:
                head.SetRotation(stream, Quaternion.Euler(0f, 45f, 0f));
                break;
            case Mode.MoveHips:
                hips.SetLocalPosition(stream, hips.GetLocalPosition(stream) + new Vector3(0.001f, 0.002f, 0f));
                break;
            case Mode.MoveArm:
                arm.SetPosition(stream, arm.GetPosition(stream) + new Vector3(10f, 10f, 0f));
                break;
            case Mode.Scale:
                spine.SetLocalScale(stream, new Vector3(1.2f, 1.2f, 1.2f));
                break;
            case Mode.ScaleHips:
            case Mode.ScaleHipsThenHuman:
                hips.SetLocalScale(stream, Vector3.Scale(hips.GetLocalScale(stream), new Vector3(.8f, 1.25f, 1.1f)));
                if (mode == Mode.ScaleHipsThenHuman)
                {
                    HumanoidProbe.BoneRecord("afterWrite", stream, ref this);
                    HumanoidProbe.JobRecord("humanBetween", stream);
                }
                break;
            case Mode.HipsTrs:
                hips.GetLocalTRS(stream, out var position, out var rotation, out var scale);
                hips.SetLocalTRS(stream, position + new Vector3(.001f, 0, 0), rotation,
                    Vector3.Scale(scale, new Vector3(.8f, 1.25f, 1.1f)), false);
                break;
            case Mode.ScaleHipsWorldWrite:
                hips.SetLocalScale(stream, Vector3.Scale(hips.GetLocalScale(stream), new Vector3(.8f, 1.25f, 1.1f)));
                HumanoidProbe.BoneRecord("scaled", stream, ref this);
                hand.SetPosition(stream, hand.GetPosition(stream) + new Vector3(10, 20, -5));
                break;
            case Mode.ScaleHipsHints:
                HumanoidProbe.ElbowHintRecord("before", stream);
                hips.SetLocalScale(stream, Vector3.Scale(hips.GetLocalScale(stream), new Vector3(.8f, 1.25f, 1.1f)));
                HumanoidProbe.ElbowHintRecord("after", stream);
                break;
            case Mode.ScaleHipsFirstHint:
                hips.SetLocalScale(stream, Vector3.Scale(hips.GetLocalScale(stream), new Vector3(.8f, 1.25f, 1.1f)));
                HumanoidProbe.ElbowHintRecord("first", stream);
                break;
            case Mode.ScaleHipsUniformThenSolve:
                hips.SetLocalScale(stream, hips.GetLocalScale(stream) * 1.25f);
                HumanoidProbe.BoneRecord("scaled", stream, ref this);
                SolveLeftHand(stream);
                break;
            case Mode.ScaleHipsUniformFirstHint:
                hips.SetLocalScale(stream, hips.GetLocalScale(stream) * 1.25f);
                HumanoidProbe.ElbowHintRecord("first", stream);
                break;
            case Mode.ScaleHipsReadTurnThenHint:
                hips.SetLocalScale(stream, Vector3.Scale(hips.GetLocalScale(stream), new Vector3(.8f, 1.25f, 1.1f)));
                var body = stream.AsHuman().bodyPosition;
                arm.SetLocalRotation(stream, arm.GetLocalRotation(stream) * Quaternion.Euler(0f, 0f, 25f));
                HumanoidProbe.BoneRecord("scaledAndTurned", stream, ref this);
                HumanoidProbe.ElbowHintRecord("first", stream);
                break;
            case Mode.ScaleHipsThenSolve:
                hips.SetLocalScale(stream, Vector3.Scale(hips.GetLocalScale(stream), new Vector3(.8f, 1.25f, 1.1f)));
                HumanoidProbe.BoneRecord("scaled", stream, ref this);
                SolveLeftHand(stream);
                break;
            case Mode.ScaleSpineThenSameHips:
                spine.SetLocalScale(stream, new Vector3(1, 2, 1));
                HumanoidProbe.BoneRecord("beforeSameHips", stream, ref this);
                hips.GetLocalTRS(stream, out var samePosition, out var sameRotation, out var sameScale);
                hips.SetLocalTRS(stream, samePosition, sameRotation, sameScale, false);
                break;
            case Mode.MaskedHipsTrs:
                HumanoidProbe.BoneMaskRecord(stream, ref this);
                hips.GetLocalTRS(stream, out var hp, out var hr, out var hs);
                hips.SetLocalTRS(stream, hp + new Vector3(.001f, .002f, 0), hr * Quaternion.Euler(0, 15, 0),
                    Vector3.Scale(hs, new Vector3(.8f, 1.25f, 1.1f)), true);
                break;
            case Mode.MaskedArmTrs:
                HumanoidProbe.BoneMaskRecord(stream, ref this);
                arm.GetLocalTRS(stream, out var ap, out var ar, out var armScale);
                arm.SetLocalTRS(stream, ap + new Vector3(.001f, 0, 0), ar * Quaternion.Euler(0, 0, 30),
                    Vector3.Scale(armScale, new Vector3(.8f, 1.25f, 1.1f)), true);
                break;
            case Mode.MaskedHeadGlobal:
                HumanoidProbe.BoneMaskRecord(stream, ref this);
                head.GetGlobalTR(stream, out var wp, out var wr);
                head.SetGlobalTR(stream, wp + new Vector3(10, 0, 0), wr * Quaternion.Euler(0, 0, 30), true);
                break;
            case Mode.NoInputBoneWrite:
                arm.SetLocalRotation(stream, arm.GetLocalRotation(stream) * Quaternion.Euler(0f, 0f, 40f));
                break;
            case Mode.NoInputWriteThenRead when HumanoidProbe.s_JobKey.EndsWith(".step0", System.StringComparison.Ordinal):
            case Mode.NoInputHumanWrite:
                var noInputPose = stream.AsHuman();
                noInputPose.bodyLocalPosition += new Vector3(.01f, -.02f, .03f);
                noInputPose.SetMuscle(new MuscleHandle(HumanPartDof.RightArm, ArmDof.ArmDownUp), -.5f);
                break;
            case Mode.LayerNoRootMuscleEdit:
                stream.AsHuman().SetMuscle(new MuscleHandle(BodyDof.SpineFrontBack), .25f);
                stream.AsHuman().SetMuscle(new MuscleHandle(HumanPartDof.RightArm, ArmDof.ArmDownUp), -.5f);
                break;
            case Mode.LayerNoRootBody:
                var human = stream.AsHuman();
                human.bodyLocalPosition += new Vector3(.01f, -.02f, .03f);
                break;
            case Mode.Solve:
                SolveLeftHand(stream);
                break;
            case Mode.RotateThenSolve:
                // A bone, then the pose read back, then the left hand's goal moved and solved.
                arm.SetLocalRotation(stream, arm.GetLocalRotation(stream) * Quaternion.Euler(0f, 0f, 40f));
                HumanoidProbe.JobRecord("humanBetween", stream);
                SolveLeftHand(stream);
                break;
            case Mode.SolveTwice:
                // The left hand's goal moved and solved, the pose read back, solved again.
                SolveLeftHand(stream);
                HumanoidProbe.JobRecord("humanBetween", stream);
                stream.AsHuman().SolveIK();
                break;
        }
        if (mode == Mode.BaseMuscles || mode == Mode.LayerNoRootMuscles || mode == Mode.LayerNoRootMuscleEdit)
            HumanoidProbe.RecordAllMuscles(stream);
        HumanoidProbe.BoneRecord("after", stream, ref this);
        if (mode == Mode.Prop) HumanoidProbe.PropRecord(stream, prop);
        HumanoidProbe.JobRecord("human", stream);
    }
}

public sealed class HumanoidScriptedJobRoot : MonoBehaviour
{
    public int action;

    void OnAnimatorMove()
    {
        HumanoidProbe.CallbackRoot("before", transform);
        if (action == 1)
            transform.SetPositionAndRotation(transform.position + new Vector3(.04f, 0f, .2f),
                transform.rotation * Quaternion.Euler(0f, 2f, 0f));
        else if (action == 2)
            GetComponent<Animator>().ApplyBuiltinRootMotion();
        HumanoidProbe.CallbackRoot("after", transform);
    }
}
