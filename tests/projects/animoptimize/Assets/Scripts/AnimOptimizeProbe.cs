using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
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
}
