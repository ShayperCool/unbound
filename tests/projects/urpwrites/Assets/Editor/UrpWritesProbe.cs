using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Which materials a Play mode session leaves dirty: each material of Assets/Resources/M as it
/// loads (its shader's editor validates it), the Standard one after a property read (its editor's
/// deferred validation), and one edited by script. run.py records which files the session wrote.
/// The rows are written before Play mode ends; the Editor then quits, Unbound's run ends.
/// </summary>
[InitializeOnLoad]
public static class UrpWritesProbe
{
    const string Variable = "URP_WRITES_OUT";
    const int Frames = 30;
    static readonly StringBuilder s_Out = new StringBuilder();
    static int s_Frames;
    static readonly string[] s_PassNames =
    {
        "UniversalForward", "Meta", "ShadowCaster", "Always", "DepthOnly", "ForwardBase", "Universal2D", "ForwardAdd",
        "Deferred", "Vertex", "MotionVectors", "SRPDefaultUnlit", "DepthNormals", "GrabPass", "lowercase", "MiXeD",
    };

    static UrpWritesProbe()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode) Loaded();
            if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.Exit(0);
        };
    }

    public static void Record() => EditorApplication.EnterPlaymode();

    static void Put(string key, object value)
    {
        var text = value is bool b ? (b ? "true" : "false") : Convert.ToString(value, CultureInfo.InvariantCulture);
        s_Out.Append("{\"key\": \"").Append(key).Append("\", \"value\": \"").Append(text).Append("\"}\n");
    }

    /// <summary>Whether the material is dirty, its keywords and its disabled passes in their
    /// stored order (when the Editor validates it).</summary>
    static void State(string phase, Material m)
    {
        Put(phase + "." + m.name + ".dirty", EditorUtility.IsDirty(m));
        var keywords = m.shaderKeywords;
        Array.Sort(keywords, string.CompareOrdinal);
        Put(phase + "." + m.name + ".keywords", string.Join(" ", keywords));
        var passes = new SerializedObject(m).FindProperty("disabledShaderPasses");
        var names = new string[passes.arraySize];
        for (int i = 0; i < names.Length; i++) names[i] = passes.GetArrayElementAtIndex(i).stringValue;
        Put(phase + "." + m.name + ".passes", string.Join(" ", names));
    }

    static void Loaded()
    {
        var materials = Resources.LoadAll<Material>("M");
        Array.Sort(materials, (a, b) => string.CompareOrdinal(a.name, b.name));
        foreach (var m in materials) State("load", m);
        var standard = Array.Find(materials, m => m.name == "StandardStale");
        Put("access.StandardStale.cutoff", standard.GetFloat("_Cutoff"));
        Put("access.StandardStale.dirty", EditorUtility.IsDirty(standard));
        // Passes disabled by script, in this order, one enabled again under another spelling.
        var named = Array.Find(materials, m => m.name == "PassNamesEdited");
        foreach (var pass in s_PassNames) named.SetShaderPassEnabled(pass, false);
        named.SetShaderPassEnabled("universal2d", true);
        Put("passes.PassNamesEdited.depthonly", named.GetShaderPassEnabled("DEPTHONLY"));
        foreach (var edited in materials)
        {
            if (!edited.name.EndsWith("Edited", StringComparison.Ordinal)) continue;
            edited.SetFloat("_Smoothness", 0.25f);
            Put("edit." + edited.name + ".dirty", EditorUtility.IsDirty(edited));
        }
        EditorApplication.update += Tick;
    }

    static void Tick()
    {
        if (++s_Frames < Frames) return;
        EditorApplication.update -= Tick;
        foreach (var m in Resources.LoadAll<Material>("M")) State("played", m);
        Put("complete", "1");
        var lines = s_Out.ToString().TrimEnd('\n').Split('\n');
        Array.Sort(lines, string.CompareOrdinal);
        File.WriteAllText(Environment.GetEnvironmentVariable(Variable), string.Join("\n", lines) + "\n");
        EditorApplication.ExitPlaymode();
    }
}
