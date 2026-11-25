using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Writes values.jsonl in $MISSINGSHADER_OUT (tests/value_probe.py rows): what a material asset
/// whose shader reference does not resolve reports (a shader whose guid is no asset, a shader
/// graph's sub-asset id of no asset, no shader, a project shader under a wrong file id, a
/// built-in id that is no shader), beside a control whose
/// shader is present; what a copy, a shader change and a save of such a material give.
/// Values never contain instance ids.
/// </summary>
public class MissingShaderProbe : MonoBehaviour
{
    const string Variable = "MISSINGSHADER_OUT";
    static readonly string[] Names =
        { "Present", "NoProperties", "DanglingGuid", "DanglingGraph", "DanglingQueue", "NullShader", "WrongFileId", "BuiltinUnknown" };

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

    static string F(float f) => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    static string V(Vector2 v) => F(v.x) + " " + F(v.y);
    static string C(Color c) => F(c.r) + " " + F(c.g) + " " + F(c.b) + " " + F(c.a);

    static string ShaderOf(Material m)
    {
        var s = m.shader;
        if (ReferenceEquals(s, null)) return "null";
        if (s == null) return "destroyed";
        var error = Shader.Find("Hidden/InternalErrorShader");
        return s.name + " error " + (s == error) + " passes " + s.passCount
            + " properties " + s.GetPropertyCount() + " queue " + s.renderQueue;
    }

    /// <summary>The values the save rows set (_Probe is declared by no shader here, _Color by Probe/Transparent).</summary>
    static string Scripted(Material m) =>
        "probe " + F(m.GetFloat("_Probe")) + " has " + m.HasProperty("_Probe") + " color " + C(m.GetColor("_Color"))
        + " names " + string.Join(" ", m.GetPropertyNames(MaterialPropertyType.Float));

    static void Describe(string key, Material m)
    {
        Try(key + "/shader", () => ShaderOf(m));
        Try(key + "/queue", () => m.renderQueue + " raw " + m.rawRenderQueue);
        Try(key + "/passes", () => m.passCount.ToString());
        Try(key + "/keywords", () => string.Join(" ", m.shaderKeywords.OrderBy(k => k, StringComparer.Ordinal)));
        Try(key + "/enabled_keywords", () => string.Join(" ", m.enabledKeywords.Select(k => k.name).OrderBy(k => k, StringComparer.Ordinal)));
        Try(key + "/has", () => string.Join(" ", new[] { "_Color", "_MainTex", "_Glossiness", "_Probe" }.Select(n => n + "=" + m.HasProperty(n))));
        Try(key + "/color", () => C(m.GetColor("_Color")));
        Try(key + "/main_color", () => C(m.color));
        Try(key + "/glossiness", () => F(m.GetFloat("_Glossiness")));
        Try(key + "/probe_float", () => F(m.GetFloat("_Probe")));
        Try(key + "/main_tex_scale", () => V(m.GetTextureScale("_MainTex")) + " offset " + V(m.GetTextureOffset("_MainTex")));
        Try(key + "/texture_names", () => string.Join(" ", m.GetTexturePropertyNames()));
        Try(key + "/float_names", () => string.Join(" ", m.GetPropertyNames(MaterialPropertyType.Float)));
        Try(key + "/tag", () => "RenderType=" + m.GetTag("RenderType", false, "none") + " fallback " + m.GetTag("RenderType", true, "none"));
        Try(key + "/flags", () => "instancing " + m.enableInstancing + " gi " + m.globalIlluminationFlags + " doubleSidedGI " + m.doubleSidedGI);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
        new GameObject("MissingShaderProbe").AddComponent<MissingShaderProbe>();
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

    static void Run()
    {
        var loaded = new Dictionary<string, Material>();
        foreach (var name in Names)
        {
            string path = "Assets/Materials/" + name + ".mat";
            Try("load/" + name, () =>
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                loaded[name] = m;
                return m == null ? "null" : "material '" + m.name + "'";
            });
            if (loaded.TryGetValue(name, out var material) && material != null) Describe("material/" + name, material);
        }
        Try("error_shader/find_same", () => (Shader.Find("Hidden/InternalErrorShader") == Shader.Find("Hidden/InternalErrorShader")).ToString());

        // A copy made with new Material(source).
        foreach (var name in new[] { "DanglingGuid", "NullShader" })
            if (loaded.TryGetValue(name, out var source) && source != null)
            {
                var copy = new Material(source);
                Describe("copy/" + name, copy);
            }

        // A material made with a shader, then given none.
        Try("assign/null", () =>
        {
            var m = new Material(Shader.Find("Unlit/Color"));
            m.shader = null;
            return ShaderOf(m) + " queue " + m.renderQueue;
        });
        // A value set from script for a name the shader does not declare: under the error shader,
        // under a shader that declares no properties and under one that declares others.
        foreach (var shaderName in new[] { "Hidden/InternalErrorShader", "Probe/NoProperties", "Probe/Transparent" })
        {
            var m = new Material(Shader.Find(shaderName));
            Try("scripted/" + shaderName + "/before", () => F(m.GetFloat("_New")) + " has " + m.HasProperty("_New"));
            m.SetFloat("_New", 5);
            m.SetColor("_NewColor", new Color(0.25f, 0.5f, 0.75f, 1f));
            Try("scripted/" + shaderName + "/after", () => F(m.GetFloat("_New")) + " has " + m.HasProperty("_New") + " " + m.HasFloat("_New")
                + " color " + C(m.GetColor("_NewColor")) + " names " + string.Join(" ", m.GetPropertyNames(MaterialPropertyType.Float)));
        }

        // A missing-shader material given a shader, then the error shader by name.
        if (loaded.TryGetValue("DanglingQueue", out var dangling) && dangling != null)
        {
            var m = new Material(dangling);
            Try("assign/unlit", () => { m.shader = Shader.Find("Unlit/Color"); return ShaderOf(m) + " queue " + m.renderQueue + " raw " + m.rawRenderQueue; });
            Try("assign/error", () => { m.shader = Shader.Find("Hidden/InternalErrorShader"); return ShaderOf(m) + " queue " + m.renderQueue + " raw " + m.rawRenderQueue; });
        }

        // A custom queue under a present shader, then no shader or the error shader.
        Try("assign/null_queue", () =>
        {
            var m = new Material(Shader.Find("Probe/Transparent")) { renderQueue = 2450 };
            m.shader = null;
            return ShaderOf(m) + " queue " + m.renderQueue + " raw " + m.rawRenderQueue;
        });
        Try("assign/error_queue", () =>
        {
            var m = new Material(Shader.Find("Probe/Transparent")) { renderQueue = 2450 };
            m.shader = Shader.Find("Hidden/InternalErrorShader");
            return ShaderOf(m) + " queue " + m.renderQueue + " raw " + m.rawRenderQueue;
        });

        // The reference a save writes after an assignment or a property copy: a copy of
        // DanglingGuid given its own shader, no shader, the error shader, or Present's properties;
        // a copy of Present given DanglingGuid's properties.
        var actions = new (string Name, string Source, Action<Material> Act)[]
        {
            ("same", "DanglingGuid", m => m.shader = m.shader),
            ("null", "DanglingGuid", m => m.shader = null),
            ("error", "DanglingGuid", m => m.shader = Shader.Find("Hidden/InternalErrorShader")),
            ("copy_from_present", "DanglingGuid", m => m.CopyPropertiesFromMaterial(loaded["Present"])),
            ("copy_from_dangling", "Present", m => m.CopyPropertiesFromMaterial(loaded["DanglingGuid"])),
        };
        AssetDatabase.CreateFolder("Assets", "Assigned");
        foreach (var (actionName, source, act) in actions)
        {
            string copyPath = "Assets/Assigned/" + actionName + ".mat";
            Try("saved_after/" + actionName, () =>
            {
                if (!AssetDatabase.CopyAsset("Assets/Materials/" + source + ".mat", copyPath)) return "not copied";
                var m = AssetDatabase.LoadAssetAtPath<Material>(copyPath);
                act(m);
                EditorUtility.SetDirty(m);
                AssetDatabase.SaveAssetIfDirty(m);
                string line = File.ReadAllLines(copyPath).First(l => l.StartsWith("  m_Shader:", StringComparison.Ordinal));
                return ShaderOf(m) + " | " + line.Trim();
            });
        }

        // A save of each missing-shader material after a change (a copy of the file, so the
        // project's own materials stay as they are): the file it writes.
        AssetDatabase.CreateFolder("Assets", "Saved");
        foreach (var name in new[] { "Present", "DanglingGuid", "DanglingGraph", "NullShader", "WrongFileId", "BuiltinUnknown" })
        {
            string copyPath = "Assets/Saved/" + name + ".mat";
            Try("save/" + name, () =>
            {
                if (!AssetDatabase.CopyAsset("Assets/Materials/" + name + ".mat", copyPath)) return "not copied";
                var m = AssetDatabase.LoadAssetAtPath<Material>(copyPath);
                m.SetFloat("_Probe", 2);
                m.SetColor("_Color", new Color(0.125f, 0.5f, 1f, 0.5f));
                string set = Scripted(m);
                EditorUtility.SetDirty(m);
                AssetDatabase.SaveAssetIfDirty(m);
                return ShaderOf(m) + " set " + set + " saved " + Scripted(m);
            });
            Try("save/" + name + "/file", () => File.ReadAllText(copyPath));
        }
    }
}
