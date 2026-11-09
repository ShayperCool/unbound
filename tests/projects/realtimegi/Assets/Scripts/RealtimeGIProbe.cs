using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// With <c>REALTIME_GI_OUT</c> set: plays each room scene with <c>DynamicGI.synchronousMode</c>
/// on, changes one input of the realtime global illumination at a time and records, per case, the
/// realtime lightmap atlas texels (<c>&lt;case&gt;.atlas</c>; directional scenes also
/// <c>&lt;case&gt;.dir</c>), light probe SH at fixed points and the API state
/// (<c>values.jsonl</c>), and a frame (<c>&lt;case&gt;.png</c>). Then quits.
/// Atlas files: int32 width, int32 height, then width*height RGBA half floats, row by row as the
/// render target reads back; alpha 0 marks texels no renderer covers.
/// </summary>
public class RealtimeGIProbe : MonoBehaviour
{
    const int Width = 192, Height = 108;
    // Unity updates realtime GI over several frames on worker threads, so the frame at which a
    // change shows is not reproducible; captures wait for DynamicGI.isConverged and then a fixed
    // number of frames, so they record the converged lighting.
    const int BeforeConverged = 30, MaxFrames = 3000, AfterConverged = 60;
    readonly StringBuilder m_Out = new StringBuilder();
    string m_Dir;
    Material m_Dump;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("REALTIME_GI_OUT"))) return;
        var go = new GameObject("RealtimeGIProbe");
        DontDestroyOnLoad(go);
        go.AddComponent<RealtimeGIProbe>();
    }

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    static string B(bool v) => v ? "true" : "false";
    static string V(Vector4 v) => "[" + F(v.x) + "," + F(v.y) + "," + F(v.z) + "," + F(v.w) + "]";

    IEnumerator Start()
    {
        m_Dir = Environment.GetEnvironmentVariable("REALTIME_GI_OUT");
        Directory.CreateDirectory(m_Dir);
        m_Dump = new Material(Shader.Find("Hidden/RealtimeGIDump"));
        DynamicGI.synchronousMode = true;

        yield return Load("Room");
        var s = Scene();
        yield return Converge();
        Capture("room-base", s);
        RecordBakedProbes("room-lightprobes");

        s.point.transform.position = new Vector3(1.5f, 2.5f, -1.5f);
        yield return Converge();
        Capture("room-moved", s);

        s.point.intensity = 4f;
        yield return Converge();
        Capture("room-intensity", s);

        s.point.bounceIntensity = 3f;
        yield return Converge();
        Capture("room-bounce", s);
        s.point.bounceIntensity = 1f;

        float indirect = DynamicGI.indirectScale;
        Record("room-indirectscale-default", "\"indirectScale\":" + F(indirect));
        DynamicGI.indirectScale = 2f;
        yield return Converge();
        Capture("room-indirectscale", s);
        DynamicGI.indirectScale = indirect;
        yield return Converge();

        // Emission: DynamicGI.SetEmissive, then a material change before and after UpdateGIMaterials.
        DynamicGI.SetEmissive(s.panel, new Color(0f, 1f, 1f) * 3f);
        yield return Converge();
        Capture("room-setemissive", s);
        s.panel.sharedMaterial.SetColor("_EmissionColor", new Color(1f, 0f, 1f) * 3f);
        yield return Converge();
        Capture("room-emission-nocall", s);
        s.panel.UpdateGIMaterials();
        yield return Converge();
        Capture("room-emission-call", s);

        // Albedo: the red wall turns blue, before and after UpdateGIMaterials.
        s.leftWall.sharedMaterial.color = new Color(0.1f, 0.1f, 0.8f);
        yield return Converge();
        Capture("room-albedo-nocall", s);
        s.leftWall.UpdateGIMaterials();
        yield return Converge();
        Capture("room-albedo-call", s);

        // Environment: the ambient color, before and after DynamicGI.UpdateEnvironment.
        RenderSettings.ambientLight = new Color(0.6f, 0.5f, 0.2f);
        yield return Converge();
        Capture("room-env-nocall", s);
        DynamicGI.UpdateEnvironment();
        yield return Converge();
        Capture("room-env-call", s);

        // updateThreshold: a 5% intensity change under a 50% threshold, then a 100% one.
        float threshold = DynamicGI.updateThreshold;
        Record("room-threshold-default", "\"updateThreshold\":" + F(threshold));
        DynamicGI.updateThreshold = 50f;
        s.point.intensity = 4.2f;
        yield return Converge();
        Capture("room-threshold-small", s);
        s.point.intensity = 8f;
        yield return Converge();
        Capture("room-threshold-big", s);
        DynamicGI.updateThreshold = threshold;
        s.point.intensity = 2f;

        // The point light off and the spot light on.
        s.point.enabled = false;
        yield return Converge();
        Capture("room-pointoff", s);
        s.spot.enabled = true;
        yield return Converge();
        Capture("room-spot", s);

        foreach (var name in new[] { "RoomScaled", "RoomDirectional" })
        {
            yield return Load(name);
            s = Scene();
            var label = name.ToLowerInvariant();
            yield return Converge();
            Capture(label + "-base", s);
            s.point.transform.position = new Vector3(1.5f, 2.5f, -1.5f);
            yield return Converge();
            Capture(label + "-moved", s);
        }
        Finish();
    }

    IEnumerator Load(string name)
    {
        SceneManager.LoadScene(name);
        yield return null;
        yield return null;
    }

    static IEnumerator Converge()
    {
        for (int i = 0; i < BeforeConverged; i++) yield return null;
        for (int i = 0; i < MaxFrames && !DynamicGI.isConverged; i++) yield return null;
        for (int i = 0; i < AfterConverged; i++) yield return null;
    }

    static RealtimeGIScene Scene() => FindAnyObjectByType<RealtimeGIScene>();

    /// <summary>The scene's light probes as baked: count and every baked coefficient (realtime GI
    /// adds its own lighting at runtime; GetInterpolatedProbe in <see cref="Capture"/> reads both).</summary>
    void RecordBakedProbes(string name)
    {
        var probes = LightmapSettings.lightProbes;
        var body = new StringBuilder("\"count\":").Append(probes == null ? -1 : probes.count).Append(",\"baked\":[");
        if (probes != null)
        {
            var baked = probes.bakedProbes;
            for (int i = 0; i < baked.Length; i++)
                for (int c = 0; c < 3; c++)
                    for (int k = 0; k < 9; k++)
                        body.Append(i + c + k > 0 ? "," : "").Append(F(baked[i][c, k]));
        }
        Record(name, body.Append(']').ToString());
    }

    void Record(string kind, string body) =>
        m_Out.Append("{\"case\":\"").Append(kind).Append("\",").Append(body).Append("}\n");

    void Capture(string name, RealtimeGIScene s)
    {
        var body = new StringBuilder();
        body.Append("\"converged\":").Append(B(DynamicGI.isConverged));
        body.Append(",\"lightmapsMode\":").Append((int)LightmapSettings.lightmapsMode);
        body.Append(",\"renderers\":[");
        for (int i = 0; i < s.lit.Length; i++)
        {
            var r = s.lit[i];
            if (i > 0) body.Append(',');
            body.Append("{\"name\":\"").Append(r.name).Append("\",\"index\":").Append(r.realtimeLightmapIndex)
                .Append(",\"st\":").Append(V(r.realtimeLightmapScaleOffset)).Append('}');
        }
        body.Append("],\"probes\":[");
        for (int i = 0; i < s.samples.Length; i++)
        {
            LightProbes.GetInterpolatedProbe(s.samples[i], null, out var sh);
            if (i > 0) body.Append(',');
            body.Append('[');
            for (int c = 0; c < 3; c++)
                for (int k = 0; k < 9; k++)
                    body.Append(c + k > 0 ? "," : "").Append(F(sh[c, k]));
            body.Append(']');
        }
        body.Append("],\"ambientProbe\":[");
        var ambient = RenderSettings.ambientProbe;
        for (int c = 0; c < 3; c++)
            for (int k = 0; k < 9; k++)
                body.Append(c + k > 0 ? "," : "").Append(F(ambient[c, k]));
        body.Append(']');
        Record(name, body.ToString());

        int size = AtlasSize(s);
        if (size > 0)
        {
            DumpAtlas(name + ".atlas", s, size, 0);
            if (LightmapSettings.lightmapsMode == LightmapsMode.CombinedDirectional)
                DumpAtlas(name + ".dir", s, size, 1);
        }
        else Record(name + "-noatlas", "\"size\":" + size);
        CaptureFrame(name + ".png");
    }

    /// <summary>The realtime lightmap's width, read by the dump shader (all renderers of the
    /// probe's scenes share one atlas); 0 when no renderer has one.</summary>
    int AtlasSize(RealtimeGIScene s)
    {
        const int Probe = 16;
        var texture = RenderDump(s, Probe, 2, TextureFormat.RGBAFloat, RenderTextureFormat.ARGBFloat);
        var pixels = texture.GetPixels();
        Destroy(texture);
        float size = 0f;
        foreach (var p in pixels)
            if (p.a > 0f) size = Mathf.Max(size, p.r);
        return (int)size;
    }

    void DumpAtlas(string file, RealtimeGIScene s, int size, int channel)
    {
        var texture = RenderDump(s, size, channel, TextureFormat.RGBAHalf, RenderTextureFormat.ARGBHalf);
        var raw = texture.GetRawTextureData();
        using (var stream = new BinaryWriter(File.Create(Path.Combine(m_Dir, file))))
        {
            stream.Write(size);
            stream.Write(size);
            stream.Write(raw);
        }
        Destroy(texture);
    }

    /// <summary>Draws every lit renderer with the dump shader into a size*size target through a
    /// camera that sees the whole room, and reads it back.</summary>
    Texture2D RenderDump(RealtimeGIScene s, int size, int channel, TextureFormat format, RenderTextureFormat targetFormat)
    {
        var target = new RenderTexture(size, size, 0, targetFormat);
        target.filterMode = FilterMode.Point;
        var go = new GameObject("Dump Camera", typeof(Camera));
        var camera = go.GetComponent<Camera>();
        camera.enabled = false;
        camera.orthographic = true;
        camera.orthographicSize = 20f;
        camera.transform.position = new Vector3(0f, 2f, -30f);
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 100f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        camera.targetTexture = target;
        camera.allowHDR = true;
        camera.allowMSAA = false;
        m_Dump.SetFloat("_Channel", channel);
        var saved = new Material[s.lit.Length];
        var others = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        var visible = new bool[others.Length];
        for (int i = 0; i < others.Length; i++)
        {
            visible[i] = others[i].enabled;
            if (Array.IndexOf(s.lit, others[i]) < 0) others[i].enabled = false;
        }
        for (int i = 0; i < s.lit.Length; i++)
        {
            saved[i] = s.lit[i].sharedMaterial;
            s.lit[i].sharedMaterial = m_Dump;
        }
        camera.Render();
        for (int i = 0; i < s.lit.Length; i++) s.lit[i].sharedMaterial = saved[i];
        for (int i = 0; i < others.Length; i++) others[i].enabled = visible[i];

        RenderTexture.active = target;
        var texture = new Texture2D(size, size, format, false, true);
        texture.ReadPixels(new Rect(0, 0, size, size), 0, 0);
        texture.Apply();
        RenderTexture.active = null;
        camera.targetTexture = null;
        Destroy(go);
        target.Release();
        return texture;
    }

    void CaptureFrame(string file)
    {
        var camera = Camera.main;
        var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
        camera.targetTexture = target;
        camera.Render();
        RenderTexture.active = target;
        var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
        texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        texture.Apply();
        RenderTexture.active = null;
        camera.targetTexture = null;
        File.WriteAllBytes(Path.Combine(m_Dir, file), texture.EncodeToPNG());
        Destroy(texture);
        target.Release();
    }

    void Finish()
    {
        File.WriteAllText(Path.Combine(m_Dir, "values.jsonl"), m_Out.ToString() + "{\"case\":\"end\"}\n");
#if UNITY_EDITOR
        EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
