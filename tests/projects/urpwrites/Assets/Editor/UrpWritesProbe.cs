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
        FirstUse(materials);
        EditorApplication.update += Tick;
    }

    /// <summary>What validates a loaded material on its first use: reads of its keywords, queue,
    /// tag and passes do not, nor a keyword change, a copy or a draw (LitStaleDrawn, read when Play
    /// mode ends); a property value read, SetPass, a shader set and a write do, the write before
    /// the validation (LitStaleSrcBlend's blend factor, as its data holds it and as it reads).</summary>
    static void FirstUse(Material[] materials)
    {
        Material Named(string name) => Array.Find(materials, m => m.name == name);
        var meta = Named("LitStaleMeta");
        Put("meta.LitStaleMeta.reads", meta.renderQueue + " " + meta.GetTag("RenderType", false) + " "
            + meta.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT") + " " + meta.enabledKeywords.Length
            + " " + meta.GetShaderPassEnabled("DepthOnly"));
        Put("meta.LitStaleMeta.passCount", meta.passCount);
        State("meta", meta);
        var keyword = Named("LitStaleKeyword");
        keyword.SetKeyword(new UnityEngine.Rendering.LocalKeyword(keyword.shader, "_RECEIVE_SHADOWS_OFF"), true);
        State("keyword", keyword);
        foreach (var name in new[] { "LitStaleRead", "LitCleanRead" })
        {
            var read = Named(name);
            Put("read." + name + ".smoothness", read.GetFloat("_Smoothness"));
            State("read", read);
        }
        var setPass = Named("LitStaleSetPass");
        setPass.SetPass(0);
        State("setPass", setPass);
        var shader = Named("LitStaleShader");
        shader.shader = shader.shader;
        State("shader", shader);
        var source = Named("LitStaleCopy");
        var copy = new Material(source);
        var copied = copy.shaderKeywords;
        Array.Sort(copied, string.CompareOrdinal);
        Put("copy.LitStaleCopy.copyKeywords", string.Join(" ", copied));
        UnityEngine.Object.DestroyImmediate(copy);
        State("copy", source);
        var blend = Named("LitStaleSrcBlend");
        blend.SetFloat("_SrcBlend", 7);
        State("edit", blend);
        Put("edit.LitStaleSrcBlend.srcBlend", Saved(blend, "_SrcBlend"));
        Put("edit.LitStaleSrcBlend.srcBlendRead", blend.GetFloat("_SrcBlend"));
        // A clean material that is validated already, written with the value it holds.
        var same = Named("LitCleanSame");
        same.SetFloat("_Smoothness", 0.5f);
        State("same", same);
        var camera = new GameObject("Camera").AddComponent<Camera>();
        camera.transform.position = new Vector3(0, 0, -5);
        GameObject.CreatePrimitive(PrimitiveType.Cube).GetComponent<MeshRenderer>().sharedMaterial = Named("LitStaleDrawn");
        s_Rendered = Time.renderedFrameCount;
    }

    static int s_Rendered;

    /// <summary>A float as the material's data holds it (no Material API access).</summary>
    static float Saved(Material m, string name)
    {
        var floats = new SerializedObject(m).FindProperty("m_SavedProperties.m_Floats");
        for (int i = 0; i < floats.arraySize; i++)
        {
            var entry = floats.GetArrayElementAtIndex(i);
            if (entry.FindPropertyRelative("first").stringValue == name) return entry.FindPropertyRelative("second").floatValue;
        }
        return float.NaN;
    }

    static void Tick()
    {
        if (++s_Frames < Frames) return;
        EditorApplication.update -= Tick;
        Put("drawn.frames", Time.renderedFrameCount > s_Rendered);
        foreach (var m in Resources.LoadAll<Material>("M")) State("played", m);
        Put("complete", "1");
        var lines = s_Out.ToString().TrimEnd('\n').Split('\n');
        Array.Sort(lines, string.CompareOrdinal);
        File.WriteAllText(Environment.GetEnvironmentVariable(Variable), string.Join("\n", lines) + "\n");
        EditorApplication.ExitPlaymode();
    }
}
