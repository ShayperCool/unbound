using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

/// <summary>
/// Optimize Game Objects: what an imported model with the option looks like and how it animates,
/// and what AnimatorUtility.OptimizeTransformHierarchy / DeoptimizeTransformHierarchy do at
/// runtime. For every import variant: the prefab asset's hierarchy (paths, local poses,
/// components); an instance's Animator flags, what Transform.Find and GetComponentsInChildren see,
/// its SkinnedMeshRenderer's bones, root bone and bounds; the instance playing its take through
/// <c>Animator.Update</c> (every transform's local pose and the skinned vertices after each step).
/// Runtime controls start from the unoptimized model (with objects added under bones), from an
/// optimized one, and from objects without an Animator. Every log message is recorded in order.
/// </summary>
public class AnimOptimizeProbe : MonoBehaviour
{
    public static bool Finished;
    static readonly StringBuilder s_Output = new StringBuilder();
    static int s_Log;

    static readonly string[] Variants = { "Rig", "RigOpt", "RigExposed", "RigExposedRoot", "TurnedOpt", "Human", "HumanOpt" };

    /// <summary>Played again away from the origin (moved, turned): what the exposed transforms take.</summary>
    static readonly string[] Placed = { "RigExposed", "TurnedOpt", "Human", "HumanOpt" };

    static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);
    static string V(Vector3 v) => "[" + F(v.x) + "," + F(v.y) + "," + F(v.z) + "]";
    static string Q(Quaternion q) => "[" + F(q.x) + "," + F(q.y) + "," + F(q.z) + "," + F(q.w) + "]";
    static string S(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    static string B(bool b) => b ? "true" : "false";

    static void Record(string key, string value) =>
        s_Output.Append("{\"k\":").Append(S(key)).Append(",\"v\":").Append(value).Append("}\n");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        if (Environment.GetEnvironmentVariable("ANIM_OPTIMIZE_OUT") == null) return;
        Application.runInBackground = true;
        DontDestroyOnLoad(new GameObject("AnimOptimizeProbe").AddComponent<AnimOptimizeProbe>().gameObject);
        Application.logMessageReceived += (message, stack, kind) =>
        {
            if (stack.Contains("UnityEditor.")) return;
            Record("log." + (s_Log++).ToString("D3"), "{\"type\":" + S(kind.ToString()) + ",\"message\":" + S(message) + "}");
        };
    }

    static void Walk(Transform t, string path, List<(string Path, Transform T)> into)
    {
        into.Add((path, t));
        foreach (Transform child in t)
            Walk(child, path.Length == 0 ? child.name : path + "/" + child.name, into);
    }

    static List<(string Path, Transform T)> Nodes(GameObject root)
    {
        var nodes = new List<(string, Transform)>();
        Walk(root.transform, "", nodes);
        return nodes;
    }

    /// <summary>Every transform in hierarchy order: its path, local pose and components.</summary>
    static string Hierarchy(GameObject root)
    {
        var fields = Nodes(root).Select((n, i) =>
        {
            var components = n.T.GetComponents<Component>().Select(c => S(c.GetType().Name));
            var key = i.ToString("D2") + " " + n.Path;
            return S(key + ".p") + ":" + V(n.T.localPosition) + "," + S(key + ".r") + ":" + Q(n.T.localRotation) + "," +
                   S(key + ".s") + ":" + V(n.T.localScale) + "," + S(key + ".c") + ":[" + string.Join(",", components) + "]";
        });
        return "{" + string.Join(",", fields) + "}";
    }

    static string Name(Object o) => o == null ? "null" : S(o.name);

    /// <summary>The Animator's flags, what lookups find, and the skinned renderer's references.</summary>
    static string Facts(GameObject root)
    {
        var animator = root.GetComponent<Animator>();
        var fields = new List<string>();
        if (animator != null)
        {
            fields.Add("\"hasTransformHierarchy\":" + B(animator.hasTransformHierarchy));
            fields.Add("\"isOptimizable\":" + B(animator.isOptimizable));
        }
        fields.Add("\"transforms\":" + root.GetComponentsInChildren<Transform>(true).Length);
        fields.Add("\"renderers\":[" + string.Join(",", root.GetComponentsInChildren<Renderer>(true).Select(r => Name(r))) + "]");
        foreach (var path in new[] { "Hand", "Hips", "Rig", "Body", "Prop", "Leg", "Rig/Hips/Spine/Hand", "Rig/Hips" })
            fields.Add(S("find:" + path) + ":" + Name(root.transform.Find(path)));
        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var bones = smr.bones;
            fields.Add(S("smr:" + smr.name) + ":{\"bones\":[" + string.Join(",", bones.Select(b => Name(b))) + "],\"rootBone\":" +
                       Name(smr.rootBone) + ",\"mesh\":" + Name(smr.sharedMesh) + ",\"bindposes\":" +
                       (smr.sharedMesh != null ? smr.sharedMesh.bindposes.Length : -1) + "}");
        }
        return "{" + string.Join(",", fields) + "}";
    }

    /// <summary>Every transform's world position.</summary>
    static string World(GameObject root) =>
        "{" + string.Join(",", Nodes(root).Select((n, i) => S(i.ToString("D2") + " " + n.Path + ".w") + ":" + V(n.T.position))) + "}";

    /// <summary>Every transform's local pose and every skinned renderer's world-space vertices (in
    /// coordinate order: the vertex order is the importer's, not what this probe measures).</summary>
    static string Pose(GameObject root)
    {
        var fields = Nodes(root).Select((n, i) =>
            S(i.ToString("D2") + " " + n.Path + ".p") + ":" + V(n.T.localPosition) + "," +
            S(i.ToString("D2") + " " + n.Path + ".r") + ":" + Q(n.T.localRotation)).ToList();
        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var baked = new Mesh();
            smr.BakeMesh(baked);
            var world = smr.transform.localToWorldMatrix;
            var points = baked.vertices.Select(v => world.MultiplyPoint3x4(v))
                .OrderBy(v => Math.Round(v.x, 3)).ThenBy(v => Math.Round(v.y, 3)).ThenBy(v => Math.Round(v.z, 3));
            fields.Add(S("skin:" + smr.name) + ":[" + string.Join(",", points.Select(V)) + "]");
            Object.Destroy(baked);
        }
        return "{" + string.Join(",", fields) + "}";
    }

    void Start()
    {
        try
        {
            foreach (var name in Variants) Imported(name);
            foreach (var name in Placed) PlacedPlay(name);
            Runtime();
            Humanoid();
            foreach (var name in new[] { "Rig", "RigExposed" })
            foreach (string mode in new[] { "job", "jobRead", "jobScene", "jobNone" })
                Handles(name, mode);
            foreach (string mode in new[] { "jobResolve", "jobResolveAll", "jobEarly", "jobTop", "jobIdle", "jobIdleResolveAll", "jobProp", "jobReoptimize", "jobDestroyed" })
                Handles("Rig", mode);
            Handles("Rig", "jobDestroyedRead");
            foreach (var name in new[] { "Rig", "RigOpt", "Human", "HumanOpt" }) GraphOnly(name, "graphOnly");
            foreach (string mode in new[] { "graphJob", "graphJobHand" })
            foreach (var name in new[] { "Human", "HumanOpt" })
                GraphOnly(name, mode);
        }
        catch (Exception e)
        {
            Record("exception", S(e.ToString()));
        }
        Record("complete", "true");
        File.WriteAllText(Environment.GetEnvironmentVariable("ANIM_OPTIMIZE_OUT"), s_Output.ToString());
        Finished = true;
    }

    static GameObject Spawn(string name, out Animator animator)
    {
        var instance = Object.Instantiate(Resources.Load<GameObject>("Models/" + name));
        instance.name = name;
        animator = instance.GetComponent<Animator>();
        animator.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>("C_" + name);
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        return instance;
    }

    static void Imported(string name)
    {
        var prefab = Resources.Load<GameObject>("Models/" + name);
        Record("asset." + name, Hierarchy(prefab));
        var instance = Spawn(name, out var animator);
        Record("facts." + name, Facts(instance));
        animator.Update(0f);
        Record("play." + name + ".0", Pose(instance));
        animator.Update(.5f);
        Record("play." + name + ".1", Pose(instance));
        animator.Update(.25f);
        Record("play." + name + ".2", Pose(instance));
        Object.DestroyImmediate(instance);
    }

    static void PlacedPlay(string name)
    {
        var instance = Spawn(name, out var animator);
        instance.transform.SetPositionAndRotation(new Vector3(3f, 4f, 5f), Quaternion.Euler(0f, 30f, 0f));
        animator.Update(0f);
        animator.Update(.5f);
        Record("placed." + name, Pose(instance));
        Record("placed." + name + ".world", World(instance));
        Object.DestroyImmediate(instance);
    }

    static void Runtime()
    {
        // From the unoptimized model, with objects added under bones: an empty chain under Hand
        // (not in the avatar's skeleton, no components) and a collider under Leg.
        var go = Spawn("Rig", out var animator);
        var extra = new GameObject("Extra").transform;
        extra.SetParent(go.transform.Find("Rig/Hips/Spine/Hand"), false);
        extra.localPosition = new Vector3(0f, 0.3f, 0f);
        new GameObject("ExtraKid").transform.SetParent(extra, false);
        var marker = new GameObject("Marker", typeof(BoxCollider)).transform;
        marker.SetParent(go.transform.Find("Rig/Hips/Leg"), false);
        marker.localPosition = new Vector3(0f, -0.5f, 0f);
        animator.Update(0f);
        animator.Update(.5f);
        Record("rt.exposed.before", Pose(go));
        AnimatorUtility.OptimizeTransformHierarchy(go, new[] { "Rig/Hips/Spine/Hand" });
        Record("rt.exposed.optimized", Hierarchy(go));
        Record("rt.exposed.optimized.facts", Facts(go));
        animator.Update(.25f);
        Record("rt.exposed.played", Pose(go));
        AnimatorUtility.DeoptimizeTransformHierarchy(go);
        Record("rt.exposed.deoptimized", Hierarchy(go));
        Record("rt.exposed.deoptimized.facts", Facts(go));
        animator.Update(.125f);
        Record("rt.exposed.replayed", Pose(go));
        Object.DestroyImmediate(go);

        // Nothing exposed, from a played pose.
        go = Spawn("Rig", out animator);
        animator.Update(0f);
        animator.Update(.5f);
        AnimatorUtility.OptimizeTransformHierarchy(go, new string[0]);
        Record("rt.none.optimized", Hierarchy(go));
        Record("rt.none.optimized.facts", Facts(go));
        animator.Update(0f);
        Record("rt.none.played", Pose(go));
        Object.DestroyImmediate(go);

        // Optimizing an optimized instance again; deoptimizing an imported optimized model.
        go = Spawn("RigOpt", out animator);
        animator.Update(0f);
        AnimatorUtility.OptimizeTransformHierarchy(go, new[] { "Rig/Hips/Spine/Hand" });
        Record("rt.twice", Hierarchy(go));
        Object.DestroyImmediate(go);
        go = Spawn("RigExposed", out animator);
        animator.Update(0f);
        animator.Update(.5f);
        AnimatorUtility.DeoptimizeTransformHierarchy(go);
        Record("rt.deoptImported", Hierarchy(go));
        Record("rt.deoptImported.facts", Facts(go));
        animator.Update(.25f);
        Record("rt.deoptImported.played", Pose(go));
        Object.DestroyImmediate(go);

        // Deoptimizing an unoptimized instance; a missing exposed path; no Animator.
        go = Spawn("Rig", out animator);
        AnimatorUtility.DeoptimizeTransformHierarchy(go);
        Record("rt.deoptPlain", Hierarchy(go));
        Record("rt.deoptPlain.facts", Facts(go));
        Object.DestroyImmediate(go);
        go = Spawn("Rig", out animator);
        AnimatorUtility.OptimizeTransformHierarchy(go, new[] { "Rig/Nope", "Rig/Hips/Leg" });
        Record("rt.missing", Hierarchy(go));
        Object.DestroyImmediate(go);
        // The skin's root bone exposed by its name rather than its path.
        go = Spawn("Rig", out animator);
        AnimatorUtility.OptimizeTransformHierarchy(go, new[] { "Hips" });
        Record("rt.rootByName", Hierarchy(go));
        Record("rt.rootByName.facts", Facts(go));
        Object.DestroyImmediate(go);
        go = new GameObject("Plain");
        new GameObject("Child").transform.SetParent(go.transform, false);
        AnimatorUtility.OptimizeTransformHierarchy(go, new string[0]);
        Record("rt.noAnimator", Hierarchy(go));
        Object.DestroyImmediate(go);
    }

    /// <summary>What a HumanPoseHandler on the instance's root reads.</summary>
    static string HumanPoseOf(Animator animator, GameObject go)
    {
        var handler = new HumanPoseHandler(animator.avatar, go.transform);
        var pose = new HumanPose();
        handler.GetHumanPose(ref pose);
        handler.Dispose();
        return "{\"bodyPosition\":" + V(pose.bodyPosition) + ",\"bodyRotation\":" + Q(pose.bodyRotation) + ",\"muscles\":[" +
               string.Join(",", pose.muscles.Select(F)) + "]}";
    }

    static string PathOf(Transform t, Transform root) =>
        t == root ? "" : t.parent == root ? t.name : PathOf(t.parent, root) + "/" + t.name;

    /// <summary>The humanoid controls: an optimized humanoid deoptimized after playing; the
    /// unoptimized one optimized at runtime with its left hand exposed, played, deoptimized and
    /// played on; and a HumanPoseHandler on both, reading the played pose and setting a pose with
    /// one arm muscle and the body's height changed.</summary>
    static void Humanoid()
    {
        var go = Spawn("HumanOpt", out var animator);
        animator.Update(0f);
        animator.Update(.5f);
        Record("h.opt.pose", HumanPoseOf(animator, go));
        AnimatorUtility.DeoptimizeTransformHierarchy(go);
        Record("h.deopt", Hierarchy(go));
        Record("h.deopt.facts", Facts(go));
        Record("h.deopt.pose", HumanPoseOf(animator, go));
        animator.Update(.25f);
        Record("h.deopt.played", Pose(go));
        Object.DestroyImmediate(go);

        go = Spawn("Human", out animator);
        animator.Update(0f);
        animator.Update(.5f);
        Record("h.human.pose", HumanPoseOf(animator, go));
        string hand = PathOf(animator.GetBoneTransform(HumanBodyBones.LeftHand), go.transform);
        AnimatorUtility.OptimizeTransformHierarchy(go, new[] { hand });
        Record("h.rt.optimized", Hierarchy(go));
        Record("h.rt.optimized.facts", Facts(go));
        Record("h.rt.pose", HumanPoseOf(animator, go));
        animator.Update(.25f);
        Record("h.rt.played", Pose(go));
        AnimatorUtility.DeoptimizeTransformHierarchy(go);
        Record("h.rt.deoptimized", Hierarchy(go));
        animator.Update(.125f);
        Record("h.rt.replayed", Pose(go));
        Object.DestroyImmediate(go);

        foreach (var name in new[] { "Human", "HumanOpt" })
        {
            go = Spawn(name, out animator);
            animator.Update(0f);
            var handler = new HumanPoseHandler(animator.avatar, go.transform);
            var pose = new HumanPose();
            handler.GetHumanPose(ref pose);
            int muscle = Array.IndexOf(HumanTrait.MuscleName, "Left Arm Down-Up");
            pose.muscles[muscle] = 0.5f;
            pose.bodyPosition += new Vector3(0f, 0.1f, 0f);
            handler.SetHumanPose(ref pose);
            var read = new HumanPose();
            handler.GetHumanPose(ref read);
            handler.Dispose();
            Record("h.set." + name, Pose(go));
            Record("h.set." + name + ".read", "{\"bodyPosition\":" + V(read.bodyPosition) + ",\"muscle\":" + F(read.muscles[muscle]) + "}");
            Object.DestroyImmediate(go);
        }
    }

    static readonly List<string> s_Job = new List<string>();

    internal static void JobRecord(string value) => s_Job.Add(value);

    /// <summary>An animation job over the model's take with a stream handle and a scene handle of
    /// the rig's hand (exposed in the optimized model): what the handles read in the job, then,
    /// in mode "job", the job turns the bone through its stream handle. "jobRead": it only reads;
    /// "jobScene": only the scene handle is bound; "jobNone": neither; "jobResolve": the job
    /// calls Resolve before its first read; "jobResolveAll": Animator.ResolveAllStreamHandles
    /// before the first evaluation; "jobIdle": the job reads nothing, it reports the handle's
    /// state and that of a custom stream property's handle (a float no clip animates);
    /// "jobIdleResolveAll": the same, with Animator.ResolveAllStreamHandles between the first
    /// and the second evaluation; "jobProp": the idle job reads the property after its report.
    /// "jobReoptimize": the job reads the handle, then the hierarchy is optimized with the hand
    /// exposed between the first and the second evaluation; "jobDestroyed": the hand is
    /// destroyed there instead (the record's bone is then null), and the job reads the stream
    /// handle only while it is unresolved; "jobDestroyedRead": the same, and the job goes on
    /// reading the handle after the hand is gone.
    /// On the plain rig only: "jobEarly" binds the hand's stream
    /// handle, then optimizes the hierarchy exposing the hand; "jobTop" optimizes exposing the
    /// top node Rig and the hand, and binds the stream handle of the exposed Rig, which the job
    /// turns (the record adds the exposed hand). Three evaluations (0, 0.5 and 0.25 s) of a
    /// manual graph without the controller.</summary>
    static void Handles(string name, string mode)
    {
        string key = mode + "." + name;
        var go = Spawn(name, out var animator);
        var clip = animator.runtimeAnimatorController.animationClips[0];
        animator.runtimeAnimatorController = null;
        Transform Hand() => go.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Hand");
        bool idle = mode == "jobIdle" || mode == "jobIdleResolveAll" || mode == "jobProp";
        var job = new HandleJob { write = mode == "job" || mode == "jobEarly" || mode == "jobTop", resolve = mode == "jobResolve",
            idle = idle, readProperty = mode == "jobProp", readUnresolved = mode == "jobDestroyed" };
        var bone = Hand();
        if (idle) job.property = animator.BindCustomStreamProperty("probeValue", CustomStreamPropertyType.Float);
        if (mode == "jobEarly")
        {
            job.stream = animator.BindStreamTransform(bone);
            AnimatorUtility.OptimizeTransformHierarchy(go, new[] { "Rig/Hips/Spine/Hand" });
            bone = Hand();
        }
        else if (mode == "jobTop")
        {
            AnimatorUtility.OptimizeTransformHierarchy(go, new[] { "Rig", "Rig/Hips/Spine/Hand" });
            bone = go.transform.Find("Rig");
            job.stream = animator.BindStreamTransform(bone);
        }
        else
        {
            if (mode != "jobScene" && mode != "jobNone") job.stream = animator.BindStreamTransform(bone);
            if (mode != "jobNone") job.scene = animator.BindSceneTransform(bone);
        }
        Record(key + ".bone", bone == null ? "null" : S(PathOf(bone, go.transform)));
        if (mode == "jobResolveAll") animator.ResolveAllStreamHandles();
        var graph = PlayableGraph.Create("handles." + key);
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        var playable = AnimationScriptPlayable.Create(graph, job, 1);
        playable.ConnectInput(0, AnimationClipPlayable.Create(graph, clip), 0, 1f);
        AnimationPlayableOutput.Create(graph, "out", animator).SetSourcePlayable(playable);
        graph.Play();
        for (int step = 0; step < 3; step++)
        {
            s_Job.Clear();
            if (mode == "jobIdleResolveAll" && step == 1) animator.ResolveAllStreamHandles();
            if (mode == "jobReoptimize" && step == 1)
            {
                AnimatorUtility.OptimizeTransformHierarchy(go, new[] { "Rig/Hips/Spine/Hand" });
                bone = Hand();
            }
            if ((mode == "jobDestroyed" || mode == "jobDestroyedRead") && step == 1)
            {
                Object.DestroyImmediate(bone.gameObject);
                bone = null;
            }
            graph.Evaluate(step == 0 ? 0f : step == 1 ? .5f : .25f);
            string record = "{\"job\":[" + string.Join(",", s_Job) + "],\"bone\":" + (bone == null ? "null" : "{\"p\":" + V(bone.localPosition) +
                ",\"r\":" + Q(bone.localRotation) + ",\"w\":" + V(bone.position) + "}");
            if (mode == "jobTop")
            {
                var hand = go.transform.Find("Hand");
                record += ",\"hand\":{\"p\":" + V(hand.localPosition) + ",\"r\":" + Q(hand.localRotation) + "}";
            }
            Record(key + "." + step, record + "}");
        }
        graph.Destroy();
        Object.DestroyImmediate(go);
    }

    /// <summary>The model's take in a manual graph on a rig without its controller: three
    /// evaluations (0, 0.5 and 0.25 s), each with the pose; the plain and the optimized generic
    /// rig and the plain and the optimized humanoid (its human pose after the last one). The
    /// clip is the output's source ("graphOnly"), or the input of a job that does nothing
    /// ("graphJob") or reads a stream handle of the humanoid's left hand, a bone of the plain
    /// rig and the exposed transform of the optimized one ("graphJobHand"). The messages the
    /// Editor logs meanwhile are in the log records.</summary>
    static void GraphOnly(string name, string mode)
    {
        string key = mode + "." + name;
        var go = Spawn(name, out var animator);
        var clip = animator.runtimeAnimatorController.animationClips[0];
        animator.runtimeAnimatorController = null;
        var graph = PlayableGraph.Create(key);
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        Playable source = AnimationClipPlayable.Create(graph, clip);
        if (mode != "graphOnly")
        {
            var job = new HandJob { read = mode == "graphJobHand" };
            if (job.read)
                job.hand = animator.BindStreamTransform(go.GetComponentsInChildren<Transform>(true).First(t => t.name == "LeftHand"));
            var playable = AnimationScriptPlayable.Create(graph, job, 1);
            playable.ConnectInput(0, source, 0, 1f);
            source = playable;
        }
        AnimationPlayableOutput.Create(graph, "out", animator).SetSourcePlayable(source);
        graph.Play();
        for (int step = 0; step < 3; step++)
        {
            s_Job.Clear();
            graph.Evaluate(step == 0 ? 0f : step == 1 ? .5f : .25f);
            Record(key + "." + step, Pose(go));
            if (mode != "graphOnly") Record(key + "." + step + ".job", "[" + string.Join(",", s_Job) + "]");
        }
        if (animator.isHuman) Record(key + ".human", HumanPoseOf(animator, go));
        graph.Destroy();
        Object.DestroyImmediate(go);
    }

    struct HandJob : IAnimationJob
    {
        public TransformStreamHandle hand;
        public bool read;

        public void ProcessRootMotion(AnimationStream s) { }

        public void ProcessAnimation(AnimationStream s)
        {
            var json = "{\"human\":" + B(s.isHumanStream);
            if (read)
            {
                bool valid = hand.IsValid(s);
                json += ",\"valid\":" + B(valid);
                if (valid)
                    json += ",\"p\":" + V(hand.GetLocalPosition(s)) + ",\"r\":" + Q(hand.GetLocalRotation(s)) + ",\"w\":" + V(hand.GetPosition(s)) +
                        ",\"resolved\":" + B(hand.IsResolved(s));
            }
            JobRecord(json + "}");
        }
    }

    struct HandleJob : IAnimationJob
    {
        public TransformStreamHandle stream;
        public TransformSceneHandle scene;
        public PropertyStreamHandle property;
        public bool write, resolve, idle, readProperty, readUnresolved;

        public void ProcessRootMotion(AnimationStream s) { }

        public void ProcessAnimation(AnimationStream s)
        {
            bool valid = stream.IsValid(s), resolved = valid && stream.IsResolved(s), sceneValid = scene.IsValid(s);
            var json = "{\"valid\":" + B(valid) + ",\"resolved\":" + B(resolved) + ",\"scene\":" + B(sceneValid);
            if (valid && resolve)
            {
                stream.Resolve(s);
                json += ",\"afterResolve\":" + B(stream.IsResolved(s));
            }
            if (idle)
            {
                bool propertyValid = property.IsValid(s);
                json += ",\"property\":" + B(propertyValid) + ",\"propertyResolved\":" + B(propertyValid && property.IsResolved(s));
                if (propertyValid && readProperty) json += ",\"x\":" + F(property.GetFloat(s)) + ",\"propertyAfter\":" + B(property.IsResolved(s));
                JobRecord(json + "}");
                return;
            }
            if (valid && !(readUnresolved && resolved))
                json += ",\"p\":" + V(stream.GetLocalPosition(s)) + ",\"r\":" + Q(stream.GetLocalRotation(s)) + ",\"w\":" + V(stream.GetPosition(s));
            if (sceneValid) json += ",\"sp\":" + V(scene.GetLocalPosition(s)) + ",\"sw\":" + V(scene.GetPosition(s));
            JobRecord(json + "}");
            if (valid && write) stream.SetLocalRotation(s, stream.GetLocalRotation(s) * Quaternion.Euler(0f, 0f, 30f));
        }
    }
}
