using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

/// <summary>
/// Records what importing SpeedTree 9 trees gives under the Universal Render Pipeline: each
/// material (shader, keywords, queue, enabled passes, every shader property), the materials of each
/// level's renderer, and each tree texture's sRGB state and format (what the import post-processing
/// of a tree's textures decides). Writes values.jsonl into SPEEDTREE_PROBE_OUT (floats as the hex of
/// their bits) and exits.
/// </summary>
public sealed class SpeedTreeUrpProbe : MonoBehaviour
{
    readonly StringBuilder m_Out = new StringBuilder();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() => new GameObject("SpeedTreeUrpProbe").AddComponent<SpeedTreeUrpProbe>();

    void Rec(string key, string value) =>
        m_Out.Append("{\"k\":\"").Append(Escape(key)).Append("\",\"v\":\"").Append(Escape(value)).Append("\"}\n");

    static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
    static string F(float f) => BitConverter.ToInt32(BitConverter.GetBytes(f), 0).ToString("x8");
    static string V4(Vector4 v) => F(v.x) + " " + F(v.y) + " " + F(v.z) + " " + F(v.w);

    static readonly string[] k_Passes = { "UniversalForward", "ShadowCaster", "DepthOnly", "DepthNormals", "MotionVectors", "Meta", "SceneSelectionPass", "Picking" };

    IEnumerator Start()
    {
        yield return null;
        int code = 0;
        string dir = Environment.GetEnvironmentVariable("SPEEDTREE_PROBE_OUT");
        try
        {
            Rec("pipeline", GraphicsSettings.currentRenderPipeline == null ? "builtin" : GraphicsSettings.currentRenderPipeline.GetType().Name);
            Rec("colorSpace", QualitySettings.activeColorSpace.ToString());
            foreach (var path in Directory.GetFiles("Assets/Trees", "*.st9").OrderBy(p => p, StringComparer.Ordinal))
                DumpTree(path.Replace('\\', '/'));
            DumpShader(Shader.Find("Universal Render Pipeline/Nature/SpeedTree9_URP"));
            foreach (var path in Directory.GetFiles("Assets/Trees", "*.png").OrderBy(p => p, StringComparer.Ordinal))
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path.Replace('\\', '/'));
                Rec("texture/" + Path.GetFileName(path), tex == null ? "null"
                    : "srgb " + GraphicsFormatUtility.IsSRGBFormat(tex.graphicsFormat) + " format " + tex.format + " mips " + tex.mipmapCount);
            }
            m_Out.Append("{\"k\":\"complete\",\"v\":true}\n");
            if (string.IsNullOrEmpty(dir)) Debug.Log(m_Out.ToString());
            else
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "values.jsonl"), m_Out.ToString());
            }
        }
        catch (Exception e)
        {
            Debug.LogError("speedtree URP probe failed: " + e);
            code = 1;
        }
        EditorApplication.Exit(code);
    }

    void DumpTree(string path)
    {
        string t = Path.GetFileNameWithoutExtension(path);
        if (!(AssetDatabase.LoadMainAssetAtPath(path) is GameObject root))
        {
            Rec(t, "no prefab");
            return;
        }
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true).OrderBy(r => r.name, StringComparer.Ordinal))
            Rec(t + "/renderer/" + r.name, string.Join(",", r.sharedMaterials.Select(m => m == null ? "null" : m.name)));
        foreach (var m in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>().OrderBy(m => m.name, StringComparer.Ordinal))
            DumpMaterial(t + "/material/" + m.name, m);
    }

    /// <summary>The shader the materials use: its queue and each sub-shader's tags, and a material
    /// made from it at run time (controls for the imported materials' queue, tags and passes).</summary>
    void DumpShader(Shader shader)
    {
        if (shader == null)
        {
            Rec("shader", "null");
            return;
        }
        Rec("shader", shader.name + " queue " + shader.renderQueue + " subshaders " + shader.subshaderCount + " passes " + shader.passCount);
        for (int i = 0; i < shader.subshaderCount; i++)
            Rec("shader/subshader" + i, "RenderType=" + shader.FindSubshaderTagValue(i, new ShaderTagId("RenderType")).name
                + " Queue=" + shader.FindSubshaderTagValue(i, new ShaderTagId("Queue")).name
                + " RenderPipeline=" + shader.FindSubshaderTagValue(i, new ShaderTagId("RenderPipeline")).name);
        var fresh = new Material(shader);
        Rec("shader/fresh", "queue " + fresh.renderQueue + " raw " + fresh.rawRenderQueue);
        Rec("shader/fresh/passes", string.Join(" ", k_Passes.Select(n => n + ":" + (fresh.GetShaderPassEnabled(n) ? 1 : 0))));
        Rec("shader/fresh/tags", "RenderType=" + fresh.GetTag("RenderType", false, "") + " Queue=" + fresh.GetTag("Queue", false, ""));
        fresh.SetOverrideTag("RenderType", "");
        Rec("shader/fresh/clearedOverride", "RenderType=" + fresh.GetTag("RenderType", false, "") + " fallbacks=" + fresh.GetTag("RenderType", true, ""));
        Destroy(fresh);
    }

    void DumpMaterial(string p, Material m)
    {
        var shader = m.shader;
        Rec(p, (shader == null ? "null" : shader.name) + " queue " + m.renderQueue + " instancing " + m.enableInstancing
            + " doubleSidedGI " + m.doubleSidedGI + " flags " + m.globalIlluminationFlags);
        Rec(p + "/keywords", string.Join(" ", m.shaderKeywords.OrderBy(k => k, StringComparer.Ordinal)));
        Rec(p + "/passes", string.Join(" ", k_Passes.Select(n => n + ":" + (m.GetShaderPassEnabled(n) ? 1 : 0))));
        Rec(p + "/tags", "RenderType=" + m.GetTag("RenderType", false, "") + " Queue=" + m.GetTag("Queue", false, ""));
        Rec(p + "/rawQueue", m.rawRenderQueue.ToString(CultureInfo.InvariantCulture));
        if (shader == null) return;
        for (int i = 0; i < shader.GetPropertyCount(); i++)
        {
            string name = shader.GetPropertyName(i);
            string value;
            switch (shader.GetPropertyType(i))
            {
                case ShaderPropertyType.Color: value = V4(m.GetColor(name)); break;
                case ShaderPropertyType.Vector: value = V4(m.GetVector(name)); break;
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range: value = F(m.GetFloat(name)); break;
                case ShaderPropertyType.Int: value = m.GetInteger(name).ToString(CultureInfo.InvariantCulture); break;
                case ShaderPropertyType.Texture:
                    var tex = m.GetTexture(name);
                    value = (tex == null ? "null" : tex.name) + " " + V4(m.GetTextureScale(name)) + " " + V4(m.GetTextureOffset(name));
                    break;
                default: value = "?"; break;
            }
            Rec(p + "/prop/" + name, value);
        }
    }
}
