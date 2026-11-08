using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Which node of a model with a single top-level node the importer makes the model's root, and
/// what its clip then animates. For every model of Resources/Models: the instantiated hierarchy
/// (each transform's path, local pose and components), the take's curve bindings and their values
/// at 0, 0.5 and 1 s, and an instance placed away from the origin playing the take through
/// <c>Animator.Update</c> (0, then 0.5 s), with root motion off and on: where the root and every
/// node end up.
/// </summary>
public class TopRootProbe : MonoBehaviour
{
    public static bool Finished;
    static readonly StringBuilder s_Output = new StringBuilder();

    static readonly string[] Models =
    {
        "NullTop", "NullTopDai0", "NullTopAnim", "BareTop", "SkelTopNoDai", "SkelTopDaiM1", "SkelTop",
        "SkelTopNullKids", "SkelTopTurned", "SkelTopTurnedAvatar", "SkelTopAnim", "SkelTopCustom", "NullTopCustom",
    };

    static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);
    static string V(Vector3 v) => "[" + F(v.x) + "," + F(v.y) + "," + F(v.z) + "]";
    static string Q(Quaternion q) => "[" + F(q.x) + "," + F(q.y) + "," + F(q.z) + "," + F(q.w) + "]";
    static string S(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    static void Record(string key, string value) =>
        s_Output.Append("{\"k\":").Append(S(key)).Append(",\"v\":").Append(value).Append("}\n");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        if (Environment.GetEnvironmentVariable("MODEL_TOP_ROOT_OUT") == null) return;
        Application.runInBackground = true;
        DontDestroyOnLoad(new GameObject("TopRootProbe").AddComponent<TopRootProbe>().gameObject);
        Application.logMessageReceived += (message, stack, kind) =>
        {
            if ((kind == LogType.Error || kind == LogType.Exception) && !stack.Contains("UnityEditor."))
                Record("error", S(message));
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

    static string Pose(Transform t) =>
        "{\"p\":" + V(t.localPosition) + ",\"r\":" + Q(t.localRotation) + ",\"s\":" + V(t.localScale) + "}";

    static string Hierarchy(GameObject root)
    {
        var fields = Nodes(root).Select(n =>
        {
            var components = n.T.GetComponents<Component>().Select(c => S(c.GetType().Name));
            return S(n.Path) + ":{\"p\":" + V(n.T.localPosition) + ",\"r\":" + Q(n.T.localRotation) + ",\"s\":" +
                   V(n.T.localScale) + ",\"c\":[" + string.Join(",", components) + "]}";
        });
        return "{\"name\":" + S(root.name) + "," + string.Join(",", fields) + "}";
    }

    static string Clip(AnimationClip clip)
    {
        var fields = new List<string> { "\"name\":" + S(clip.name), "\"length\":" + F(clip.length) };
#if UNITY_EDITOR
        var bindings = UnityEditor.AnimationUtility.GetCurveBindings(clip)
            .OrderBy(b => b.path, StringComparer.Ordinal).ThenBy(b => b.propertyName, StringComparer.Ordinal).ToArray();
        fields.Add("\"bindings\":[" + string.Join(",", bindings.Select(b => S(b.path + "|" + b.type.Name + "." + b.propertyName))) + "]");
        foreach (var b in bindings)
        {
            var curve = UnityEditor.AnimationUtility.GetEditorCurve(clip, b);
            fields.Add(S(b.path + "|" + b.propertyName) + ":[" + F(curve.Evaluate(0f)) + "," + F(curve.Evaluate(.5f)) + "," +
                       F(curve.Evaluate(1f)) + "]");
        }
#endif
        return "{" + string.Join(",", fields) + "}";
    }

    static string Placed(GameObject instance)
    {
        var fields = Nodes(instance).Select(n => S(n.Path) + ":" + Pose(n.T));
        return "{\"world.p\":" + V(instance.transform.position) + ",\"world.r\":" + Q(instance.transform.rotation) + "," +
               string.Join(",", fields) + "}";
    }

    void Start()
    {
        try
        {
            foreach (var name in Models) Probe(name);
        }
        catch (Exception e)
        {
            Record("error", S(e.ToString()));
        }
        Record("complete", "true");
        File.WriteAllText(Environment.GetEnvironmentVariable("MODEL_TOP_ROOT_OUT"), s_Output.ToString());
        Finished = true;
    }

    static void Probe(string name)
    {
        var prefab = Resources.Load<GameObject>("Models/" + name);
        if (prefab == null)
        {
            Record("missing." + name, "true");
            return;
        }
        var rest = Object.Instantiate(prefab);
        rest.name = prefab.name;
        Record("rest." + name, Hierarchy(rest));
        Object.Destroy(rest);

        var controller = Resources.Load<RuntimeAnimatorController>("C_" + name);
        foreach (var clip in controller.animationClips) Record("clip." + name, Clip(clip));

        foreach (var rootMotion in new[] { false, true })
        {
            var instance = Object.Instantiate(prefab);
            var animator = instance.GetComponent<Animator>();
            Record("play." + name + "." + (rootMotion ? "motion" : "still") + ".animator", animator != null ? "true" : "false");
            if (animator == null) animator = instance.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = rootMotion;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            instance.transform.SetPositionAndRotation(new Vector3(3f, 4f, 5f), Quaternion.Euler(0f, 30f, 0f));
            var key = "play." + name + "." + (rootMotion ? "motion" : "still");
            animator.Update(0f);
            Record(key + ".0", Placed(instance));
            animator.Update(.5f);
            Record(key + ".1", Placed(instance));
            Object.Destroy(instance);
        }
    }
}
