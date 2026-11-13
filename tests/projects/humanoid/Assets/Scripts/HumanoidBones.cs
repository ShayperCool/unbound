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
    /// Solve moves the left hand's goal and solves the IK; RotateThenSolve turns the lower arm, reads the human stream, then moves the left hand's
    /// goal and solves the IK; SolveTwice moves the goal and solves, reads the human stream and
    /// solves again: a solve after the pose was read back on the rig's root scaled by 100.</summary>
    static void BoneHandles()
    {
        foreach (HumanBoneJob.Mode mode in System.Enum.GetValues(typeof(HumanBoneJob.Mode)))
        {
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
                    var playable = AnimationScriptPlayable.Create(g, job, 1);
                    g.Connect(AnimationClipPlayable.Create(g, ControllerClip("HumanA")), 0, playable, 0);
                    playable.SetInputWeight(0, 1f);
                    return playable;
                }, true, setup: animator => bound = animator, velocities: false, localsEvery: 4);
            });
        }
    }
}

/// <summary>An animation job that reads the stream handles of five bones of a humanoid and changes
/// one of them.</summary>
public struct HumanBoneJob : IAnimationJob
{
    public enum Mode { Read, RotateLocal, RotateWorld, MoveHips, MoveArm, Scale, RotateThenHuman, HumanThenRead, Prop, Solve, RotateThenSolve, SolveTwice }

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

    public void ProcessRootMotion(AnimationStream stream) { }

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
        if (mode == Mode.HumanThenRead && stream.isHumanStream)
            stream.AsHuman().SetMuscle(new MuscleHandle(BodyDof.SpineFrontBack), 0.5f);
        HumanoidProbe.BoneRecord("before", stream, ref this);
        switch (mode)
        {
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
        HumanoidProbe.BoneRecord("after", stream, ref this);
        if (mode == Mode.Prop) HumanoidProbe.PropRecord(stream, prop);
        HumanoidProbe.JobRecord("human", stream);
    }
}
