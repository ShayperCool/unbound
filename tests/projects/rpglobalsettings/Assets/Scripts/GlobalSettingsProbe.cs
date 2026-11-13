using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>
/// On the third frame (the pipeline exists), writes what the project's URP global settings hold
/// for settings its asset lacks or stores without a resource, into values.jsonl in
/// $RP_GLOBAL_SETTINGS_OUT (tests/value_probe.py). The asset has no
/// ScreenSpaceAmbientOcclusionPersistentResources entry and a null m_FallbackErrorShader in
/// UniversalRenderPipelineRuntimeShaders; ScreenSpaceAmbientOcclusionDynamicResources is stored
/// whole (a control).
/// </summary>
public class GlobalSettingsProbe : MonoBehaviour
{
    const string Assembly = "Unity.RenderPipelines.Universal.Runtime";
    static string s_Directory;
    static StreamWriter output;

    static void Row(string key, string value) =>
        output.Write("{\"k\":\"" + key + "\",\"v\":\"" + value + "\"}\n");

    static readonly System.Collections.Generic.List<string> s_Errors = new System.Collections.Generic.List<string>();

    /// <summary>The errors logged from the start of Play mode on (the pipeline is made meanwhile).</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Listen()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RP_GLOBAL_SETTINGS_OUT"))) return;
        Application.logMessageReceived += (message, stack, type) =>
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                s_Errors.Add(type + ": " + message.Split('\n')[0]);
        };
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        s_Directory = Environment.GetEnvironmentVariable("RP_GLOBAL_SETTINGS_OUT");
        if (string.IsNullOrEmpty(s_Directory)) return;
        var camera = new GameObject("camera").AddComponent<Camera>();
        camera.gameObject.AddComponent<GlobalSettingsProbe>();
    }

    static string Name(object value) => value is Object o ? (o != null ? o.name : "null") : value == null ? "null" : value.ToString();

    /// <summary>GraphicsSettings.TryGetRenderPipelineSettings for a type named by its full name.</summary>
    static object Settings(string typeName, out string found)
    {
        var type = Type.GetType(typeName + ", " + Assembly);
        if (type == null) { found = "no type"; return null; }
        var method = typeof(GraphicsSettings).GetMethod(nameof(GraphicsSettings.TryGetRenderPipelineSettings)).MakeGenericMethod(type);
        var arguments = new object[] { null };
        found = ((bool)method.Invoke(null, arguments)).ToString();
        return arguments[0];
    }

    static string Field(object settings, string field)
    {
        if (settings == null) return "no settings";
        var info = settings.GetType().GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (info == null) return "no field";
        var value = info.GetValue(settings);
        if (value is Array array)
        {
            var names = new string[array.Length];
            for (int i = 0; i < array.Length; i++) names[i] = Name(array.GetValue(i));
            return array.Length + " " + string.Join(",", names);
        }
        return Name(value);
    }

    void Update()
    {
        if (Time.frameCount < 3) return;
        enabled = false;
        try
        {
            Directory.CreateDirectory(s_Directory);
            using (output = new StreamWriter(Path.Combine(s_Directory, "values.jsonl")))
            {
                Row("pipeline", Name(GraphicsSettings.currentRenderPipeline));
                var ssao = Settings("UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusionPersistentResources", out var found);
                Row("ssaoPersistent.found", found);
                Row("ssaoPersistent.m_Shader", Field(ssao, "m_Shader"));
                Row("ssaoPersistent.m_Version", Field(ssao, "m_Version"));
                var shaders = Settings("UnityEngine.Rendering.Universal.UniversalRenderPipelineRuntimeShaders", out found);
                Row("runtimeShaders.found", found);
                Row("runtimeShaders.m_FallbackErrorShader", Field(shaders, "m_FallbackErrorShader"));
                Row("runtimeShaders.m_BlitHDROverlay", Field(shaders, "m_BlitHDROverlay"));
                var dynamic = Settings("UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusionDynamicResources", out found);
                Row("ssaoDynamic.found", found);
                Row("ssaoDynamic.m_BlueNoise256Textures", Field(dynamic, "m_BlueNoise256Textures"));
                // The core package's model URP's global settings reference (ProbeVolumeDebugResources):
                // its slot without a material of its own has the pipeline's default.
                var debugMesh = AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.unity.render-pipelines.core/Runtime/Debug/ProbeSamplingDebugMesh.fbx");
                var debugRenderer = debugMesh != null ? debugMesh.GetComponentInChildren<Renderer>() : null;
                Row("debugMesh.material", debugRenderer != null ? Name(debugRenderer.sharedMaterial) : "no renderer");
                Row("errors", s_Errors.Count + (s_Errors.Count > 0 ? " " + string.Join(" | ", s_Errors).Replace("\"", "'") : ""));
                output.Write("{\"k\":\"complete\",\"v\":true}\n");
            }
            EditorApplication.Exit(0);
        }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
    }
}
