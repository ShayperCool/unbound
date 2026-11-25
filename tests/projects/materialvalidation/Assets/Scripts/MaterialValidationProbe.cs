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

    /// <summary>The keywords, render queue and RenderType of a material: no property value read,
    /// so no validation.</summary>
    void Metadata(string name, Material m)
    {
        var keywords = new List<string>(m.shaderKeywords);
        keywords.Sort(StringComparer.Ordinal);
        Write(name + ".keywords", string.Join(" ", keywords));
        Write(name + ".renderQueue", m.renderQueue);
        Write(name + ".renderType", m.GetTag("RenderType", false, "<none>"));
    }

    /// <summary>Loaded materials used before any property read: drawn for three frames, written
    /// to (then read), activated by SetPass.</summary>
    IEnumerator Later()
    {
        var drawn = Resources.Load<Material>("LaterDrawnCutout");
        var drawnProject = Resources.Load<Material>("LaterProjectDrawn");
        var camera = new GameObject("LaterCamera").AddComponent<Camera>();
        camera.transform.position = new Vector3(0, 0, -5);
        GameObject.CreatePrimitive(PrimitiveType.Cube).GetComponent<Renderer>().sharedMaterial = drawn;
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.transform.position = new Vector3(1.5f, 0, 0);
        sphere.GetComponent<Renderer>().sharedMaterial = drawnProject;
        var written = Resources.Load<Material>("LaterWrittenCutout");
        written.SetFloat("_SrcBlend", 7);
        Metadata("LaterWrittenCutout.written", written);
        Write("LaterWrittenCutout.written._SrcBlend", written.GetFloat("_SrcBlend"));
#if UNITY_EDITOR
        // The value the material's data holds (what is saved).
        var floats = new UnityEditor.SerializedObject(written).FindProperty("m_SavedProperties.m_Floats");
        for (int i = 0; i < floats.arraySize; i++)
        {
            var entry = floats.GetArrayElementAtIndex(i);
            if (entry.FindPropertyRelative("first").stringValue == "_SrcBlend")
                Write("LaterWrittenCutout.written._SrcBlendSaved", entry.FindPropertyRelative("second").floatValue);
        }
#endif
        var writtenProject = Resources.Load<Material>("LaterProjectWritten");
        writtenProject.SetFloat("_Value", 5);
        Metadata("LaterProjectWritten.written", writtenProject);
        Write("LaterProjectWritten.written._Value", writtenProject.GetFloat("_Value"));
        var setPass = Resources.Load<Material>("LaterSetPassCutout");
        setPass.SetPass(0);
        Metadata("LaterSetPassCutout.setPass", setPass);
        int rendered = Time.renderedFrameCount;
        for (int i = 0; i < 3; i++) yield return null;
        Write("Later.drawnFrames", Time.renderedFrameCount > rendered);
        Metadata("LaterDrawnCutout.drawn", drawn);
        Metadata("LaterProjectDrawn.drawn", drawnProject);
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
        yield return Later();
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
