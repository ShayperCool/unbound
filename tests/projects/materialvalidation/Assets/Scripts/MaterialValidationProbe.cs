using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>
/// Records, per material of Assets/Resources loaded in Play mode, the state its shader's editor
/// leaves it in (ShaderGUI.ValidateMaterial "when the user loads a Material using this ShaderGUI
/// into memory"): keywords, render queue, GI flags, the RenderType tag, the blend floats and the
/// project editors' validation counts (<c>_Value</c>), right after the load (<c>.load</c>) and two
/// frames later, and the messages logged while it loads. Controls: a material made from script
/// and the built-in default material.
/// </summary>
public class MaterialValidationProbe : MonoBehaviour
{
    public static readonly string[] Cases =
    {
        "EmissionRealtimeNoKeyword", "EmissionBakedNoKeyword", "EmissionNoneNoKeyword", "EmissionBothNoKeyword",
        "BlackBakedKeyword", "BlackRealtimeKeyword", "EmissiveIsBlackKeyword", "BlackBakedFlagsSix",
        "CutoutLowQueue", "FadeQueueInRange", "TransparentShaderQueue", "OpaqueHighQueue",
        "NormalMapNoKeyword", "DetailNormalNoKeyword", "StrayKeywords", "MetallicMapNoKeyword", "SmoothnessAlbedo",
        "SpecularMapNoKeyword", "ProjectGui", "ProjectGuiNamespaced", "NoGui",
    };

    static string s_Out;
    readonly List<string> m_Lines = new List<string>();
    readonly List<string> m_Logs = new List<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        s_Out = Environment.GetEnvironmentVariable("MATERIAL_VALIDATION_OUT");
        if (!string.IsNullOrEmpty(s_Out)) new GameObject("MaterialValidationProbe").AddComponent<MaterialValidationProbe>();
    }

    void Write(string key, object value) =>
        m_Lines.Add("{\"key\":\"" + key + "\",\"value\":\"" + Convert.ToString(value, CultureInfo.InvariantCulture).Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"}");

    void OnLog(string message, string stack, LogType type) => m_Logs.Add(type + ": " + message.Replace("\n", " | "));

    void Record(string name, Material m)
    {
        if (m == null) { Write(name + ".missing", true); return; }
        var keywords = new List<string>(m.shaderKeywords);
        keywords.Sort(StringComparer.Ordinal);
        Write(name + ".keywords", string.Join(" ", keywords));
        Write(name + ".emissionEnabled", m.IsKeywordEnabled("_EMISSION"));
        Write(name + ".renderQueue", m.renderQueue);
        Write(name + ".flags", (int)m.globalIlluminationFlags);
        Write(name + ".renderType", m.GetTag("RenderType", false, "<none>"));
        foreach (var p in new[] { "_SrcBlend", "_DstBlend", "_ZWrite", "_Value" })
            if (m.HasProperty(p)) Write(name + "." + p, m.GetFloat(p));
    }

    IEnumerator Start()
    {
        Application.logMessageReceived += OnLog;
        var loaded = new List<Material>();
        foreach (var name in Cases)
        {
            var material = Resources.Load<Material>(name);
            loaded.Add(material);
            Record(name + ".load", material);
            Write(name + ".log", string.Join(" || ", m_Logs));
            m_Logs.Clear();
        }
        // What the materials have once loaded: two frames later.
        yield return null;
        yield return null;
        for (int i = 0; i < Cases.Length; i++) Record(Cases[i], loaded[i]);
        var created = new Material(Shader.Find("Standard"));
        created.SetColor("_EmissionColor", Color.red);
        created.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        Record("Created", created);
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Record("BuiltinDefault", cube.GetComponent<Renderer>().sharedMaterial);
        Write("Controls.log", string.Join(" || ", m_Logs));
        Application.logMessageReceived -= OnLog;
        Write("complete", 1);
        File.WriteAllLines(s_Out + ".tmp", m_Lines);
        File.Move(s_Out + ".tmp", s_Out);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
