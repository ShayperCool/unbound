using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Writes values.jsonl in $ASSETDEPENDENCIES_OUT (tests/value_probe.py rows): AssetDatabase.GetDependencies'
/// answers, in the order given, for this project's assets (a material and its variant, a shader with an
/// include and a default texture, scripts with default references, assets of references in a cycle, a
/// prefab, a scene with a prefab instance), with and without recursion and through each overload, for
/// paths that are not assets, and after a material's change is saved.
/// </summary>
public class AssetDependenciesProbe : MonoBehaviour
{
    const string Variable = "ASSETDEPENDENCIES_OUT";

    static StreamWriter output;
    static readonly List<string> logs = new List<string>();

    static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

    static void Row(string key, string value) => output.Write("{\"k\":\"" + key + "\",\"v\":\"" + Escape(value) + "\"}\n");

    /// <summary>A value of an action, or what it throws, and (as key.logs) the logs it writes.</summary>
    static void Try(string key, Func<string> action)
    {
        logs.Clear();
        string result;
        try { result = action(); }
        catch (Exception e) { result = "throws " + e.GetType().FullName + ": " + e.Message; }
        Row(key, result);
        if (logs.Count > 0) Row(key + ".logs", string.Join("\n---\n", logs));
    }

    static string S(string value) => value == null ? "null" : "\"" + value + "\"";
    static string L(IEnumerable<string> values) => values == null ? "null" : "[" + string.Join(", ", values.Select(S)) + "]";

    /// <summary>The answer in its order, and (as key.set) its paths sorted ordinally: the order of
    /// GetDependencies' answer is not documented.</summary>
    static void Answer(string key, Func<string[]> action)
    {
        string[] answer = null;
        Try(key, () => L(answer = action()).Replace(Path.GetFullPath("."), "<project>"));
        if (answer != null) Row(key + ".set", L(answer.OrderBy(p => p, StringComparer.Ordinal)));
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
        new GameObject("AssetDependenciesProbe").AddComponent<AssetDependenciesProbe>();
    }

    void Start()
    {
        var dir = Environment.GetEnvironmentVariable(Variable);
        Directory.CreateDirectory(dir);
        Application.logMessageReceived += (message, stack, type) => logs.Add(type + ": " + message);
        output = new StreamWriter(Path.Combine(dir, "values.jsonl"), false, new UTF8Encoding(false));
        try { Run(); }
        catch (Exception e) { Row("aborted", e.GetType().FullName + ": " + e.Message); }
        output.Write("{\"k\":\"complete\",\"v\":true}\n");
        output.Dispose();
#if UNITY_EDITOR
        EditorApplication.Exit(0);
#endif
    }

    static readonly string[] Assets =
    {
        "Assets/Deps/Tex.png", "Assets/Deps/Tex2.png", "Assets/Deps/Sec.png", "Assets/Deps/Custom.shader",
        "Assets/Deps/Fall.shader", "Assets/Deps/UsePass.shader", "Assets/Deps/Dependency.shader", "Assets/Deps/DepInclude.cginc", "Assets/Deps/Mat.mat",
        "Assets/Deps/BuiltinMat.mat", "Assets/Deps/Variant.mat", "Assets/Scripts/DepData.cs", "Assets/Scripts/DepBehaviour.cs",
        "Assets/Deps/Data.asset", "Assets/Deps/Other.asset", "Assets/Deps/Text.txt", "Assets/Deps/Prefab.prefab",
        "Assets/Deps/Deps.unity", "Assets/Scenes/Probe.unity", "Assets/Scripts/AssetDependenciesProbe.cs",
    };

    static void Dependencies(string key, string path)
    {
        Answer(key + "/default", () => AssetDatabase.GetDependencies(path));
        Answer(key + "/recursive", () => AssetDatabase.GetDependencies(path, true));
        Answer(key + "/direct", () => AssetDatabase.GetDependencies(path, false));
    }

    static void Run()
    {
        foreach (var path in Assets) Dependencies("deps/" + path, path);

        // Several paths: in the order given, a path given twice, a cycle's members.
        var many = new[] { "Assets/Deps/Prefab.prefab", "Assets/Deps/Mat.mat", "Assets/Deps/Prefab.prefab", "Assets/Deps/Text.txt" };
        Answer("many/default", () => AssetDatabase.GetDependencies(many));
        Answer("many/recursive", () => AssetDatabase.GetDependencies(many, true));
        Answer("many/direct", () => AssetDatabase.GetDependencies(many, false));
        var cycle = new[] { "Assets/Deps/Other.asset", "Assets/Deps/Data.asset" };
        Answer("cycle/recursive", () => AssetDatabase.GetDependencies(cycle, true));
        Answer("cycle/direct", () => AssetDatabase.GetDependencies(cycle, false));
        Answer("many/empty", () => AssetDatabase.GetDependencies(new string[0], true));
        Answer("many/null", () => AssetDatabase.GetDependencies((string[])null, true));
        Answer("many/withMissing", () => AssetDatabase.GetDependencies(new[] { "Assets/Nope.mat", "Assets/Deps/Mat.mat" }, false));

        // Paths that are not (or not exactly) an asset's.
        var odd = new (string Name, string Path)[]
        {
            ("null", null), ("empty", ""), ("folder", "Assets/Deps"), ("assets", "Assets"), ("missing", "Assets/Deps/Nope.mat"),
            ("case", "Assets/Deps/MAT.mat"), ("backslash", "Assets\\Deps\\Mat.mat"), ("slash", "Assets/Deps/Mat.mat/"),
            ("absolute", Path.GetFullPath("Assets/Deps/Mat.mat")),
        };
        foreach (var (name, path) in odd)
        {
            var p = path;
            Answer("odd/" + name + "/recursive", () => AssetDatabase.GetDependencies(p, true));
            Answer("odd/" + name + "/direct", () => AssetDatabase.GetDependencies(p, false));
        }

        // The AssetBundles the assets of a bundle depend on: Prefab (prefab) -> Mat (mat) -> Custom.shader
        // (none) -> Tex (tex), Tex2 (tex.hd); Prefab -> Other.asset (none) -> Data (data) -> Mat.
        Try("bundles/all", () => L(AssetDatabase.GetAllAssetBundleNames()));
        foreach (var bundle in new[] { "prefab", "mat", "data", "tex", "tex.hd", "missing" })
            foreach (var recursive in new[] { false, true })
                Answer("bundles/" + bundle + "/" + recursive, () => AssetDatabase.GetAssetBundleDependencies(bundle, recursive));

        // A material's texture taken away: before and after the save.
        var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Deps/Mat.mat");
        Try("change/set", () => { mat.SetTexture("_MainTex", null); EditorUtility.SetDirty(mat); return "done"; });
        Dependencies("change/dirty", "Assets/Deps/Mat.mat");
        Try("change/save", () => { AssetDatabase.SaveAssets(); return "done"; });
        Try("change/file", () => File.ReadAllText("Assets/Deps/Mat.mat").Replace("\r\n", "\n"));
        Dependencies("change/saved", "Assets/Deps/Mat.mat");
        Dependencies("change/savedPrefab", "Assets/Deps/Prefab.prefab");
    }
}
