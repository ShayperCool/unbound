using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>Records what the humanoid avatars of the probe's rigs give a game: the HumanTrait tables,
/// the imported avatars and their human descriptions, the Animator's humanoid members, poses read
/// and written through HumanPoseHandler, and avatars built at run time with AvatarBuilder.</summary>
public class HumanoidProbe : MonoBehaviour
{
    static readonly StringBuilder s_Output = new StringBuilder();
    static readonly List<string> s_Log = new List<string>();

    static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);
    static string V(Vector3 v) => "[" + F(v.x) + "," + F(v.y) + "," + F(v.z) + "]";
    static string Q(Quaternion q) => "[" + F(q.x) + "," + F(q.y) + "," + F(q.z) + "," + F(q.w) + "]";
    static string S(string s) => s == null ? "null" : "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";
    static string B(bool b) => b ? "true" : "false";
    static string Floats(float[] a)
    {
        var parts = new string[a.Length];
        for (int i = 0; i < a.Length; i++) parts[i] = F(a[i]);
        return "[" + string.Join(",", parts) + "]";
    }

    static void Put(string key, string json) => s_Output.Append("{\"k\":").Append(S(key)).Append(",\"v\":").Append(json).Append("}\n");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("HumanoidProbe").AddComponent<HumanoidProbe>();

    static string Path(Transform t, Transform root)
    {
        if (t == null) return null;
        var parts = new List<string>();
        for (var c = t; c != null && c != root; c = c.parent) parts.Insert(0, c.name);
        return string.Join("/", parts);
    }

    static void Traits()
    {
        Put("trait.counts", "[" + HumanTrait.MuscleCount + "," + HumanTrait.BoneCount + "," + HumanTrait.RequiredBoneCount + "]");
        for (int m = 0; m < HumanTrait.MuscleCount; m++)
            Put("trait.muscle." + m, "[" + S(HumanTrait.MuscleName[m]) + "," + HumanTrait.BoneFromMuscle(m) + ","
                + F(HumanTrait.GetMuscleDefaultMin(m)) + "," + F(HumanTrait.GetMuscleDefaultMax(m)) + "]");
        for (int b = 0; b < HumanTrait.BoneCount; b++)
            Put("trait.bone." + b, "[" + S(HumanTrait.BoneName[b]) + "," + HumanTrait.MuscleFromBone(b, 0) + "," + HumanTrait.MuscleFromBone(b, 1) + ","
                + HumanTrait.MuscleFromBone(b, 2) + "," + B(HumanTrait.RequiredBone(b)) + "," + HumanTrait.GetParentBone(b) + ","
                + F(HumanTrait.GetBoneDefaultHierarchyMass(b)) + "]");
    }

    static string Limit(HumanLimit l) =>
        "{\"min\":" + V(l.min) + ",\"max\":" + V(l.max) + ",\"center\":" + V(l.center) + ",\"axisLength\":" + F(l.axisLength)
        + ",\"useDefaultValues\":" + B(l.useDefaultValues) + "}";

    static void Description(string key, HumanDescription d)
    {
        Put(key + ".params", "{\"upperArmTwist\":" + F(d.upperArmTwist) + ",\"lowerArmTwist\":" + F(d.lowerArmTwist)
            + ",\"upperLegTwist\":" + F(d.upperLegTwist) + ",\"lowerLegTwist\":" + F(d.lowerLegTwist)
            + ",\"armStretch\":" + F(d.armStretch) + ",\"legStretch\":" + F(d.legStretch) + ",\"feetSpacing\":" + F(d.feetSpacing)
            + ",\"hasTranslationDoF\":" + B(d.hasTranslationDoF) + ",\"human\":" + (d.human?.Length ?? -1) + ",\"skeleton\":" + (d.skeleton?.Length ?? -1) + "}");
        if (d.human != null)
            for (int i = 0; i < d.human.Length; i++)
                Put(key + ".human." + i, "[" + S(d.human[i].boneName) + "," + S(d.human[i].humanName) + "," + Limit(d.human[i].limit) + "]");
        if (d.skeleton != null)
            for (int i = 0; i < d.skeleton.Length; i++)
            {
                var s = d.skeleton[i];
                Put(key + ".skeleton." + i, "[" + S(s.name) + "," + V(s.position) + "," + Q(s.rotation) + "," + V(s.scale) + "]");
            }
    }

    static void Avatar(string key, Avatar avatar)
    {
        if (avatar == null) { Put(key, "null"); return; }
        Put(key, "{\"name\":" + S(avatar.name) + ",\"isValid\":" + B(avatar.isValid) + ",\"isHuman\":" + B(avatar.isHuman) + "}");
        Description(key + ".desc", avatar.humanDescription);
    }

    static void Animator(string key, Animator animator)
    {
        var root = animator.transform;
        Put(key, "{\"avatar\":" + S(animator.avatar != null ? animator.avatar.name : null) + ",\"isHuman\":" + B(animator.isHuman)
            + ",\"humanScale\":" + F(animator.humanScale) + ",\"avatarRoot\":" + S(Path(animator.avatarRoot, root))
            + ",\"isOptimizable\":" + B(animator.isOptimizable) + ",\"hasTransformHierarchy\":" + B(animator.hasTransformHierarchy)
            + ",\"leftFeetBottomHeight\":" + F(animator.leftFeetBottomHeight) + ",\"rightFeetBottomHeight\":" + F(animator.rightFeetBottomHeight) + "}");
        var bones = new List<string>();
        for (var b = HumanBodyBones.Hips; b < HumanBodyBones.LastBone; b++) bones.Add(S(Path(animator.GetBoneTransform(b), root)));
        Put(key + ".bones", "[" + string.Join(",", bones) + "]");
    }

    static void Locals(string key, Transform root)
    {
        var parts = new List<string>();
        foreach (var t in root.GetComponentsInChildren<Transform>())
            parts.Add(S(Path(t, root)) + ":[" + V(t.localPosition) + "," + Q(t.localRotation) + "," + V(t.localScale) + "]");
        Put(key, "{" + string.Join(",", parts) + "}");
    }

    /// <summary>Every transform's local rotation, and the local positions of the root and the hips
    /// (the only positions a human pose moves).</summary>
    static void Rotations(string key, Transform root, Transform hips)
    {
        var parts = new List<string>();
        foreach (var t in root.GetComponentsInChildren<Transform>())
            parts.Add(S(Path(t, root)) + ":" + Q(t.localRotation));
        parts.Add("\"root.p\":" + V(root.localPosition));
        parts.Add("\"hips.p\":" + V(hips.localPosition));
        Put(key, "{" + string.Join(",", parts) + "}");
    }

    static void Pose(string key, HumanPose pose) =>
        Put(key, "{\"bodyPosition\":" + V(pose.bodyPosition) + ",\"bodyRotation\":" + Q(pose.bodyRotation) + ",\"muscles\":" + Floats(pose.muscles) + "}");

    /// <summary>The default pose read back, then poses written and read back: no muscles, each
    /// muscle at -1 and 1 alone, and seeded random poses (muscles beyond the limits too).</summary>
    static void Handler(string key, Avatar avatar, Transform root, Transform hips)
    {
        var handler = new HumanPoseHandler(avatar, root);
        var pose = new HumanPose();
        handler.GetHumanPose(ref pose);
        Pose(key + ".default", pose);
        void Write(string name, Vector3 body, Quaternion rotation, float[] muscles)
        {
            var p = new HumanPose { bodyPosition = body, bodyRotation = rotation, muscles = muscles };
            handler.SetHumanPose(ref p);
            Rotations(key + "." + name + ".rotations", root, hips);
            var back = new HumanPose();
            handler.GetHumanPose(ref back);
            Pose(key + "." + name + ".back", back);
        }
        Write("zero", new Vector3(0, 1, 0), Quaternion.identity, new float[HumanTrait.MuscleCount]);
        for (int m = 0; m < HumanTrait.MuscleCount; m++)
            foreach (float v in new[] { -1f, 1f })
            {
                var muscles = new float[HumanTrait.MuscleCount];
                muscles[m] = v;
                Write("m" + m + (v < 0 ? "-" : "+"), new Vector3(0, 1, 0), Quaternion.identity, muscles);
            }
        var random = new System.Random(1234);
        float R(float a) => (float)(random.NextDouble() * 2 - 1) * a;
        for (int i = 0; i < 12; i++)
        {
            var muscles = new float[HumanTrait.MuscleCount];
            for (int m = 0; m < muscles.Length; m++) muscles[m] = R(i < 8 ? 1f : 1.5f);
            Write("r" + i, new Vector3(R(1), 1 + R(0.5f), R(1)), Quaternion.Euler(R(40), R(180), R(40)), muscles);
        }
        handler.Dispose();
    }

    static void Model(string name)
    {
        var avatar = Resources.Load<Avatar>(name);
        Avatar(name + ".avatar", avatar);
        var instance = Instantiate(Resources.Load<GameObject>(name));
        var animator = instance.GetComponent<Animator>();
        Animator(name + ".animator", animator);
        Locals(name + ".rest", instance.transform);
        Handler(name + ".handler", avatar, instance.transform, animator.GetBoneTransform(HumanBodyBones.Hips));
        Destroy(instance);

        // Avatars built at run time from a fresh instance and the imported description.
        var fresh = Instantiate(Resources.Load<GameObject>(name));
        var built = AvatarBuilder.BuildHumanAvatar(fresh, avatar.humanDescription);
        built.name = name + "Built";
        Avatar(name + ".built", built);
        var pose = new HumanPose();
        var handler = new HumanPoseHandler(built, fresh.transform);
        handler.GetHumanPose(ref pose);
        Pose(name + ".built.default", pose);
        handler.Dispose();
        var generic = AvatarBuilder.BuildGenericAvatar(fresh, "");
        Put(name + ".generic", "{\"isValid\":" + B(generic.isValid) + ",\"isHuman\":" + B(generic.isHuman) + "}");
        var genericRoot = AvatarBuilder.BuildGenericAvatar(fresh, "Hips");
        Put(name + ".genericRoot", "{\"isValid\":" + B(genericRoot.isValid) + ",\"isHuman\":" + B(genericRoot.isHuman) + "}");

        // An invalid description: a required bone left out.
        var broken = avatar.humanDescription;
        var human = new List<HumanBone>(broken.human);
        human.RemoveAll(b => b.humanName == "Head");
        broken.human = human.ToArray();
        s_Log.Clear();
        var invalid = AvatarBuilder.BuildHumanAvatar(fresh, broken);
        Put(name + ".invalid", "{\"isValid\":" + B(invalid.isValid) + ",\"isHuman\":" + B(invalid.isHuman) + ",\"log\":[" + string.Join(",", s_Log.ConvertAll(S)) + "]}");
        Destroy(fresh);
    }

    /// <summary>The clip's humanoid properties.</summary>
    static void Clip(string key, AnimationClip clip)
    {
        Put(key, "{\"name\":" + S(clip.name) + ",\"length\":" + F(clip.length) + ",\"frameRate\":" + F(clip.frameRate)
            + ",\"humanMotion\":" + B(clip.humanMotion) + ",\"isHumanMotion\":" + B(clip.isHumanMotion) + ",\"isLooping\":" + B(clip.isLooping)
            + ",\"averageSpeed\":" + V(clip.averageSpeed) + ",\"averageAngularSpeed\":" + F(clip.averageAngularSpeed)
            + ",\"averageDuration\":" + F(clip.averageDuration) + ",\"apparentSpeed\":" + F(clip.apparentSpeed) + "}");
    }

    /// <summary>The Animator's body, root and human pose after a step.</summary>
    static void Step(string key, Animator animator, HumanPoseHandler handler, bool locals = true)
    {
        if (locals) Locals(key + ".locals", animator.transform);
        var pose = new HumanPose();
        handler.GetHumanPose(ref pose);
        Pose(key + ".pose", pose);
        Put(key + ".body", "{\"bodyPosition\":" + V(animator.bodyPosition) + ",\"bodyRotation\":" + Q(animator.bodyRotation)
            + ",\"rootPosition\":" + V(animator.rootPosition) + ",\"rootRotation\":" + Q(animator.rootRotation)
            + ",\"position\":" + V(animator.transform.position) + ",\"pivotWeight\":" + F(animator.pivotWeight)
            + ",\"pivotPosition\":" + V(animator.pivotPosition) + "}");
    }

    /// <summary>Plays a controller on a fresh instance of a rig: Update(0), then 24 steps of 1/30 s
    /// and four of 0.1 s.</summary>
    static void Play(string key, string rig, string controller, Action<Animator> setup = null, bool motion = false, bool locals = true)
    {
        var instance = Instantiate(Resources.Load<GameObject>(rig));
        var animator = instance.GetComponent<Animator>();
        animator.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>(controller);
        setup?.Invoke(animator);
        var handler = new HumanPoseHandler(animator.avatar, instance.transform);
        for (int step = 0; step <= 28; step++)
        {
            s_IKKey = key + ".step" + step;
            animator.Update(step == 0 ? 0f : step <= 24 ? 1f / 30f : 0.1f);
            Step(key + ".step" + step, animator, handler, locals);
            if (motion) Motion(key + ".step" + step, animator);
        }
        s_IKKey = null;
        handler.Dispose();
        Destroy(instance);
    }

    /// <summary>The Animator's root motion of a step and the states and clips that made it.</summary>
    static void Motion(string key, Animator animator)
    {
        Put(key + ".motion", "{\"deltaPosition\":" + V(animator.deltaPosition) + ",\"deltaRotation\":" + Q(animator.deltaRotation)
            + ",\"velocity\":" + V(animator.velocity) + ",\"angularVelocity\":" + V(animator.angularVelocity) + "}");
        var states = new List<string>();
        for (int layer = 0; layer < animator.layerCount; layer++)
        {
            var info = animator.GetCurrentAnimatorStateInfo(layer);
            var clips = animator.GetCurrentAnimatorClipInfo(layer);
            var weights = new List<string>();
            foreach (var clip in clips) weights.Add(F(clip.weight));
            states.Add("[" + F(info.normalizedTime) + "," + B(animator.IsInTransition(layer)) + ",[" + string.Join(",", weights) + "]]");
        }
        Put(key + ".states", "[" + string.Join(",", states) + "]");
    }

    /// <summary>Runs a part of the probe; an exception is recorded under its key and the probe goes
    /// on with the next part.</summary>
    static void Guarded(string key, Action part)
    {
        try { part(); }
        catch (Exception e) { Put(key + ".exception", S(e.GetType().Name + ": " + e.Message)); }
    }

    internal static string s_IKKey;

    internal static void PutIK(string key, string json) => Put(key, json);

    void Start()
    {
        int code = 0;
        Application.logMessageReceived += (message, stack, type) => s_Log.Add(type + ": " + message);
        try
        {
            Traits();
            Model("HumanA");
            Model("HumanB");
            foreach (var clip in Resources.LoadAll<AnimationClip>("HumanA")) Guarded("HumanA.clip." + clip.name, () => Clip("HumanA.clip." + clip.name, clip));
            foreach (var clip in Resources.LoadAll<AnimationClip>("HumanB")) Clip("HumanB.clip." + clip.name, clip);
            Play("playA", "HumanA", "HumanA");
            Play("playB", "HumanB", "HumanB");
            Play("retargetAB", "HumanB", "HumanA");
            Play("retargetBA", "HumanA", "HumanB");
            Play("ik", "HumanA", "HumanAIK", animator => animator.gameObject.AddComponent<HumanoidIK>());
            Play("ikB", "HumanB", "HumanAIK", animator => animator.gameObject.AddComponent<HumanoidIK>());
            Play("blendTransition", "HumanA", "HumanBlendTransition", motion: true);
            Play("blendOverride", "HumanA", "HumanBlendOverride", motion: true);
            Play("blendAdditive", "HumanA", "HumanBlendAdditive", motion: true);
            Play("blendTree", "HumanA", "HumanBlendTree", motion: true);
            Play("blendTreeB", "HumanB", "HumanBlendTree", motion: true);
            foreach (var mask in new[] { "NoRoot", "Upper", "UpperRoot" })
            {
                var asset = Resources.Load<AvatarMask>("Mask" + mask);
                var parts = new List<string>();
                for (var part = AvatarMaskBodyPart.Root; part < AvatarMaskBodyPart.LastBodyPart; part++)
                    parts.Add(B(asset.GetHumanoidBodyPartActive(part)));
                Put("mask" + mask, "{\"parts\":[" + string.Join(",", parts) + "],\"transformCount\":" + asset.transformCount + "}");
                Play("maskOverride" + mask, "HumanA", "HumanMaskOverride" + mask, motion: true);
                Play("maskAdditive" + mask, "HumanA", "HumanMaskAdditive" + mask, motion: true);
            }
            Play("additiveTree", "HumanA", "HumanAdditiveTree", motion: true);
            foreach (var mask in new[] { "UpperFootIK", "NoFootIK" })
            {
                var asset = Resources.Load<AvatarMask>("Mask" + mask);
                var parts = new List<string>();
                for (var part = AvatarMaskBodyPart.Root; part < AvatarMaskBodyPart.LastBodyPart; part++)
                    parts.Add(B(asset.GetHumanoidBodyPartActive(part)));
                Put("mask" + mask, "{\"parts\":[" + string.Join(",", parts) + "],\"transformCount\":" + asset.transformCount + "}");
                Play("maskOverride" + mask, "HumanA", "HumanMaskOverride" + mask, motion: true);
            }
            // HumanA's clip with non-default root settings (ProbeRunner.Settings).
            foreach (var name in new[] { "BakeRot", "BakeY", "BakeXZ", "OrigRot", "OrigXZ", "CoM", "Feet", "Offset", "LoopPose",
                         "Mirror", "Cycle", "BakeAll", "BakeOrig", "BakeOffset", "BakeLoopPose", "OrigOnce", "BakeOnce" })
                Guarded("settings" + name, () => Play("settings" + name, "HumanA", "HumanSet" + name, motion: true, locals: false));
            // Humanoid .anim clips whose start and stop differ (ProbeRunner.MuscleClips).
            foreach (var name in new[] { "LoopMuscles", "LoopMusclesBake", "MirrorMuscles" })
            {
                Guarded("anim." + name, () => Clip("anim." + name, Resources.Load<AnimationClip>(name)));
                Guarded("settings" + name, () => Play("settings" + name, "HumanA", "Human" + name, motion: true, locals: false));
            }
        }
        catch (Exception e)
        {
            Put("exception", S(e.ToString()));
            code = 1;
        }
        string output = Environment.GetEnvironmentVariable("HUMANOID_OUT") ?? "/tmp/humanoid.jsonl";
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(output)));
        File.WriteAllText(output, s_Output.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(code);
#else
        Application.Quit(code);
#endif
    }
}
