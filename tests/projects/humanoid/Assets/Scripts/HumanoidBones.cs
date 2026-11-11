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

    /// <summary>Bone handle jobs (an AnimationScriptPlayable, root motion applied) above HumanA's
    /// clip, one per <see cref="HumanBoneJob.Mode"/>, each step with what the job reads before and
    /// after its change, the human stream's body and muscles after it, and the final pose with
    /// the transforms' locals every fourth step.</summary>
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
    public enum Mode { Read, RotateLocal, RotateWorld, MoveHips, MoveArm, Scale, RotateThenHuman, HumanThenRead }

    public Mode mode;
    TransformStreamHandle hips, spine, arm, head, hand;

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

    public void ProcessAnimation(AnimationStream stream)
    {
        if (mode == Mode.HumanThenRead && stream.isHumanStream)
            stream.AsHuman().SetMuscle(new MuscleHandle(BodyDof.SpineFrontBack), 0.5f);
        HumanoidProbe.BoneRecord("before", stream, ref this);
        switch (mode)
        {
            case Mode.RotateLocal:
            case Mode.RotateThenHuman:
                arm.SetLocalRotation(stream, arm.GetLocalRotation(stream) * Quaternion.Euler(0f, 0f, 40f));
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
        }
        HumanoidProbe.BoneRecord("after", stream, ref this);
        HumanoidProbe.JobRecord("human", stream);
    }
}
