using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Reads the render queues of materials of a transparent URP Shader Graph saved with a queue
/// offset and Auto or User queue control, and one saved with a RenderType override tag and its
/// DepthOnly pass disabled, and a URP Lit material saved in that state (a control outside Shader
/// Graph): their queues, tag and pass states right after <c>Resources.Load</c>
/// and two frames later, the scene quad's material, and the shader's own queue. Writes values.jsonl into
/// GRAPH_QUEUE_OUT and exits.
/// </summary>
public sealed class GraphQueueProbe : MonoBehaviour
{
    static readonly string[] Materials = { "OffsetStored", "OffsetStale", "NoOffsetStale", "UserQueue", "StrayState", "LitStray" };

    readonly StringBuilder m_Out = new StringBuilder();
    static string s_Early = "";

    /// <summary>What a shader's queue and the pipeline are before the first scene loads, when the
    /// scene's materials load: the active pipeline's tag, whether a pipeline exists, and the queue
    /// of URP shaders whose SubShaders only that pipeline runs (Terrain/Lit: Geometry-100).</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Early()
    {
        s_Early = $"tag {Shader.globalRenderPipeline} pipeline {(UnityEngine.Rendering.RenderPipelineManager.currentPipeline != null)}"
            + $" terrain {Shader.Find("Universal Render Pipeline/Terrain/Lit")?.renderQueue} lit {Shader.Find("Universal Render Pipeline/Lit")?.renderQueue}"
            + $" particles {Shader.Find("Universal Render Pipeline/Particles/Unlit")?.renderQueue}";
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() => new GameObject("GraphQueueProbe").AddComponent<GraphQueueProbe>();

    void Rec(string key, string value) => m_Out.Append("{\"k\":\"").Append(key).Append("\",\"v\":\"").Append(value).Append("\"}\n");

    static string Queues(Material m) => m == null ? "null"
        : $"queue {m.renderQueue} raw {m.rawRenderQueue} tag {m.GetTag("RenderType", false, "none")} depthOnly {m.GetShaderPassEnabled("DepthOnly")} motion {m.GetShaderPassEnabled("MotionVectors")}";

    IEnumerator Start()
    {
        int code = 0;
        var loaded = new Material[Materials.Length];
        try
        {
            var quad = GameObject.Find("SceneQuad").GetComponent<Renderer>().sharedMaterial;
            Rec("early", s_Early);
            Rec("start", $"tag {Shader.globalRenderPipeline} pipeline {(UnityEngine.Rendering.RenderPipelineManager.currentPipeline != null)} terrain {Shader.Find("Universal Render Pipeline/Terrain/Lit")?.renderQueue}");
            Rec("shader", quad.shader.name + " queue " + quad.shader.renderQueue);
            Rec("scene.start", Queues(quad));
            for (int i = 0; i < Materials.Length; i++)
            {
                loaded[i] = Resources.Load<Material>(Materials[i]);
                Rec(Materials[i] + ".load", Queues(loaded[i]));
            }
        }
        catch (Exception e) { Debug.LogError("graph queue probe failed: " + e); code = 1; }
        yield return null;
        yield return null;
        try
        {
            for (int i = 0; i < Materials.Length; i++) Rec(Materials[i] + ".later", Queues(loaded[i]));
            Rec("scene.later", Queues(GameObject.Find("SceneQuad").GetComponent<Renderer>().sharedMaterial));
            m_Out.Append("{\"k\":\"complete\",\"v\":true}\n");
            var dir = Environment.GetEnvironmentVariable("GRAPH_QUEUE_OUT");
            if (string.IsNullOrEmpty(dir)) Debug.Log(m_Out.ToString());
            else { Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, "values.jsonl"), m_Out.ToString()); }
        }
        catch (Exception e) { Debug.LogError("graph queue probe failed: " + e); code = 1; }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(code);
#else
        Application.Quit(code);
#endif
    }
}
