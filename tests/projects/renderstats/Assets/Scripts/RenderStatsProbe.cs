using System;
using System.Collections;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Records the render counters a script reads through ProfilerRecorder ("Draw Calls Count",
/// "Batches Count", "SetPass Calls Count", "Triangles Count", "Vertices Count") for frames in
/// which a disabled camera is rendered by hand into a render texture, with scenes of known
/// content. A frame's values are read in the next frame (LastValue). Writes values.jsonl into
/// RENDER_STATS_OUT and exits.
/// </summary>
public sealed class RenderStatsProbe : MonoBehaviour
{
    static readonly string[] Counters = { "Draw Calls Count", "Batches Count", "SetPass Calls Count", "Triangles Count", "Vertices Count" };
    readonly StringBuilder m_Out = new StringBuilder();
    ProfilerRecorder[] m_Recorders;
    Camera m_Camera;
    RenderTexture m_Target;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RENDER_STATS_OUT"))) return;
        new GameObject("RenderStatsProbe").AddComponent<RenderStatsProbe>();
    }

    void Rec(string key, long value) =>
        m_Out.Append("{\"k\":\"").Append(key).Append("\",\"v\":").Append(value).Append("}\n");

    static GameObject Shape(PrimitiveType type, Material material, Vector3 position)
    {
        var go = GameObject.CreatePrimitive(type);
        go.transform.position = position;
        go.GetComponent<Renderer>().sharedMaterial = material;
        return go;
    }

    /// <summary>Renders the camera <paramref name="renders"/> times this frame and records the
    /// frame's counters as read in the next frame.</summary>
    IEnumerator Case(string name, int renders = 1)
    {
        yield return null;
        for (int i = 0; i < renders; i++) m_Camera.Render();
        yield return null;
        for (int i = 0; i < Counters.Length; i++)
            Rec(name + "." + Counters[i], m_Recorders[i].Valid ? m_Recorders[i].LastValue : -1);
    }

    IEnumerator Start()
    {
        m_Recorders = new ProfilerRecorder[Counters.Length];
        for (int i = 0; i < Counters.Length; i++) m_Recorders[i] = ProfilerRecorder.StartNew(ProfilerCategory.Render, Counters[i]);
        m_Target = new RenderTexture(64, 64, 24);
        m_Camera = new GameObject("ProbeCamera").AddComponent<Camera>();
        m_Camera.enabled = false;
        m_Camera.targetTexture = m_Target;
        m_Camera.clearFlags = CameraClearFlags.SolidColor;
        m_Camera.backgroundColor = Color.black;
        m_Camera.transform.position = new Vector3(0f, 0f, -10f);
        foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None)) light.enabled = false;

        yield return Case("empty");
        var unlitA = new Material(Shader.Find("Unlit/Color"));
        var unlitB = new Material(Shader.Find("Unlit/Texture"));
        var cubes = new[] { Shape(PrimitiveType.Cube, unlitA, new Vector3(-2f, 0f, 0f)), Shape(PrimitiveType.Cube, unlitA, new Vector3(0f, 0f, 0f)), Shape(PrimitiveType.Cube, unlitA, new Vector3(2f, 0f, 0f)) };
        yield return Case("cubesOneMaterial");
        var spheres = new[] { Shape(PrimitiveType.Sphere, unlitB, new Vector3(-1f, 2f, 0f)), Shape(PrimitiveType.Sphere, unlitB, new Vector3(1f, 2f, 0f)) };
        yield return Case("twoMaterials");
        yield return Case("twoRenders", 2);
        m_Camera.clearFlags = CameraClearFlags.Skybox;
        yield return Case("skybox");
        m_Camera.clearFlags = CameraClearFlags.SolidColor;
        // Lit: a directional light, then a point light over two shapes (forward add passes).
        var lit = new Material(Shader.Find("Standard"));
        foreach (var go in cubes) go.GetComponent<Renderer>().sharedMaterial = lit;
        foreach (var go in spheres) go.GetComponent<Renderer>().sharedMaterial = lit;
        var sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.shadows = LightShadows.None;
        sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        yield return Case("litDirectional");
        var point = new GameObject("Point").AddComponent<Light>();
        point.type = LightType.Point;
        point.range = 2.5f;
        point.renderMode = LightRenderMode.ForcePixel;
        point.shadows = LightShadows.None;
        point.transform.position = new Vector3(-1.5f, 1f, -1f);
        yield return Case("litPoint");
        yield return Case("nothingRendered", 0);

        foreach (var recorder in m_Recorders) recorder.Dispose();
        var dir = Environment.GetEnvironmentVariable("RENDER_STATS_OUT");
        Directory.CreateDirectory(dir);
        m_Out.Append("{\"k\":\"complete\",\"v\":true}\n");
        File.WriteAllText(Path.Combine(dir, "values.jsonl"), m_Out.ToString());
        EditorApplication.Exit(0);
    }
}
