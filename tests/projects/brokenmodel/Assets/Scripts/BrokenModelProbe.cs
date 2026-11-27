using System;
using System.IO;
using System.Linq;
using System.Text;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

/// <summary>
/// Writes values.jsonl in $BROKENMODEL_OUT (tests/value_probe.py rows): what the AssetDatabase
/// answers for model files that do not import (a text .fbx, an empty .fbx, a binary .fbx cut after its
/// header, an ASCII .fbx cut inside a block, an ASCII .fbx with
/// two models cut inside its connections, text .fbx files whose .meta asks for a Generic or
/// Humanoid avatar or a Legacy animation, a text .obj, an empty .obj, and a text .fbx created without a .meta in
/// the session), the callbacks and log lines their ImportAsset gives,
/// and a model that imports as the control.
/// </summary>
public class BrokenModelProbe : MonoBehaviour
{
    const string Variable = "BROKENMODEL_OUT";

    static StreamWriter output;

    static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

    static void Row(string key, string value) => output.Write("{\"k\":\"" + key + "\",\"v\":\"" + Escape(value) + "\"}\n");

    static void Try(string key, Func<string> action)
    {
        string result;
        try { result = action(); }
        catch (Exception e) { result = "throws " + e.GetType().FullName; }
        Row(key, result);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
        new GameObject("BrokenModelProbe").AddComponent<BrokenModelProbe>();
    }

    void Start()
    {
        var dir = Environment.GetEnvironmentVariable(Variable);
        Directory.CreateDirectory(dir);
        output = new StreamWriter(Path.Combine(dir, "values.jsonl"), false, new UTF8Encoding(false));
#if UNITY_EDITOR
        try { Run(); }
        catch (Exception e) { Row("aborted", e.GetType().FullName + ": " + e.Message); }
#endif
        output.Write("{\"k\":\"complete\",\"v\":true}\n");
        output.Dispose();
#if UNITY_EDITOR
        EditorApplication.Exit(0);
#endif
    }

#if UNITY_EDITOR
    /// <summary>The callbacks and log lines noted since the last call, sorted.</summary>
    static string Events()
    {
        var events = ModelEvents.Events.OrderBy(e => e, StringComparer.Ordinal).ToList();
        ModelEvents.Events.Clear();
        return events.Count == 0 ? "<none>" : string.Join("\n", events);
    }

    static void OnLog(string message, string stack, LogType type)
    {
        // The project's own directory is the run's: written <project>.
        var project = Path.GetDirectoryName(Application.dataPath).Replace('\\', '/');
        ModelEvents.Events.Add("log " + type + " " + message.Replace(project, "<project>"));
    }

    static string Describe(UnityEngine.Object o)
    {
        if (o == null) return "null";
        var text = o.GetType().FullName + " '" + o.name + "'";
        if (o is GameObject go)
        {
            text += " children " + go.transform.childCount + " components "
                + string.Join(",", go.GetComponents<Component>().Select(c => c.GetType().Name));
            foreach (Transform child in go.transform) text += " child '" + child.name + "'";
        }
        return text;
    }

    static void Queries(string key, string path)
    {
        Try(key + "/exists", () => File.Exists(path) + " meta " + File.Exists(path + ".meta"));
        Try(key + "/guid", () => AssetDatabase.AssetPathToGUID(path).Length == 32 ? "set" : "'" + AssetDatabase.AssetPathToGUID(path) + "'");
        Try(key + "/maintype", () => "" + AssetDatabase.GetMainAssetTypeAtPath(path));
        Try(key + "/importer", () => { var i = AssetImporter.GetAtPath(path); return i == null ? "null" : i.GetType().FullName; });
        Try(key + "/main", () => Describe(AssetDatabase.LoadMainAssetAtPath(path)));
        Try(key + "/gameobject", () => Describe(AssetDatabase.LoadAssetAtPath<GameObject>(path)));
        Try(key + "/mesh", () => Describe(AssetDatabase.LoadAssetAtPath<Mesh>(path)));
        Try(key + "/all", () => string.Join(",", AssetDatabase.LoadAllAssetsAtPath(path).Select(o => o == null ? "null" : o.GetType().Name).OrderBy(n => n, StringComparer.Ordinal)));
        Try(key + "/representations", () => AssetDatabase.LoadAllAssetRepresentationsAtPath(path).Length.ToString());
        Try(key + "/isloaded", () => "" + AssetDatabase.IsMainAssetAtPathLoaded(path));
        Try(key + "/fileids", () =>
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (root == null) return "null";
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(root, out _, out long go);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(root.transform, out _, out long transform);
            return go + " " + transform;
        });
    }

    static void Import(string key, string path, ImportAssetOptions options)
    {
        ModelEvents.Events.Clear();
        Application.logMessageReceived += OnLog;
        try { Try(key + "/import", () => { AssetDatabase.ImportAsset(path, options); return "ok"; }); }
        finally { Application.logMessageReceived -= OnLog; }
        Row(key + "/import.events", Events());
    }

    static void Run()
    {
        // What the project's opening imported is not this probe's (the Editor's domain reload at
        // Play forgets it).
        ModelEvents.Events.Clear();

        foreach (var name in new[] { "Garbage.fbx", "Empty.fbx", "Truncated.fbx", "CutAscii.fbx", "CutModel.fbx", "RigGeneric.fbx", "RigHumanoid.fbx", "RigLegacy.fbx", "Garbage.obj", "Empty.obj", "Tri.obj" })
        {
            var path = "Assets/Models/" + name;
            var key = name.Replace('.', '_');
            Queries(key, path);
            Import(key, path, ImportAssetOptions.ForceUpdate);
            Queries(key + "/after", path);
        }

        // A model file new in the session, without a .meta: ImportAsset, then Refresh of a change.
        const string fresh = "Assets/Models/Fresh.fbx";
        File.WriteAllText(fresh, "not a model either\n");
        Import("fresh", fresh, ImportAssetOptions.Default);
        Queries("fresh", fresh);
        ModelEvents.Events.Clear();
        Application.logMessageReceived += OnLog;
        try
        {
            File.WriteAllText(fresh, "still not a model\n");
            File.SetLastWriteTimeUtc(fresh, DateTime.UtcNow.AddSeconds(5));
            Try("fresh/refresh", () => { AssetDatabase.Refresh(); return "ok"; });
        }
        finally { Application.logMessageReceived -= OnLog; }
        Row("fresh/refresh.events", Events());
        Queries("fresh/refreshed", fresh);

        // Instantiating what a broken model's load gives.
        Try("garbage/instantiate", () =>
        {
            var o = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Garbage.fbx");
            if (o == null) return "nothing to instantiate";
            var copy = (GameObject)UnityEngine.Object.Instantiate(o);
            var text = Describe(copy);
            UnityEngine.Object.DestroyImmediate(copy);
            return text;
        });
    }
#endif
}
