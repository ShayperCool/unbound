using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// What mipmap streaming answers (see README.md): each texture's streaming members and the
/// global counters, as the scene loads, after frames with a camera looking at the textures, with
/// QualitySettings.streamingMipmapsActive turned on, and with a requested mip level; keyed values
/// into MIP_STREAMING_OUT/values.jsonl (tests/value_probe.py).
/// </summary>
public sealed class MipStreamingProbe : MonoBehaviour
{
    readonly StringBuilder m_Out = new StringBuilder();
    Texture2D m_Stream, m_Plain, m_Runtime, m_Big, m_Tiny, m_NoMip;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() => new GameObject("MipStreamingProbe").AddComponent<MipStreamingProbe>();

    void Rec(string key, string value) => m_Out.Append("{\"k\":\"").Append(key).Append("\",\"v\":\"").Append(value).Append("\"}\n");

    static string Try(Func<object> read)
    {
        try { return Convert.ToString(read(), CultureInfo.InvariantCulture); }
        catch (Exception e) { return "throws " + e.GetType().Name + ": " + e.Message; }
    }

    void Texture(string stage, string name, Texture2D t)
    {
        string k = stage + "/" + name + "/";
        Rec(k + "streamingMipmaps", Try(() => t.streamingMipmaps));
        Rec(k + "streamingMipmapsPriority", Try(() => t.streamingMipmapsPriority));
        Rec(k + "requestedMipmapLevel", Try(() => t.requestedMipmapLevel));
        Rec(k + "minimumMipmapLevel", Try(() => t.minimumMipmapLevel));
        Rec(k + "calculatedMipmapLevel", Try(() => t.calculatedMipmapLevel));
        Rec(k + "desiredMipmapLevel", Try(() => t.desiredMipmapLevel));
        Rec(k + "loadingMipmapLevel", Try(() => t.loadingMipmapLevel));
        Rec(k + "loadedMipmapLevel", Try(() => t.loadedMipmapLevel));
        Rec(k + "IsRequestedMipmapLevelLoaded", Try(() => t.IsRequestedMipmapLevelLoaded()));
        Rec(k + "mipmapCount", Try(() => t.mipmapCount));
    }

    void Globals(string stage)
    {
        string k = stage + "/";
        Rec(k + "streamingMipmapsActive", Try(() => QualitySettings.streamingMipmapsActive));
        Rec(k + "streamingMipmapsMemoryBudget", Try(() => QualitySettings.streamingMipmapsMemoryBudget));
        Rec(k + "streamingMipmapsRenderersPerFrame", Try(() => QualitySettings.streamingMipmapsRenderersPerFrame));
        Rec(k + "streamingMipmapsMaxLevelReduction", Try(() => QualitySettings.streamingMipmapsMaxLevelReduction));
        Rec(k + "streamingMipmapsAddAllCameras", Try(() => QualitySettings.streamingMipmapsAddAllCameras));
        Rec(k + "streamingMipmapsMaxFileIORequests", Try(() => QualitySettings.streamingMipmapsMaxFileIORequests));
        Rec(k + "streamingTextureForceLoadAll", Try(() => UnityEngine.Texture.streamingTextureForceLoadAll));
        Rec(k + "streamingTextureDiscardUnusedMips", Try(() => UnityEngine.Texture.streamingTextureDiscardUnusedMips));
        // Counts and memory depend on every texture the Editor holds: only whether they are 0.
        Rec(k + "streamingTextureCount>0", Try(() => UnityEngine.Texture.streamingTextureCount > 0));
        Rec(k + "nonStreamingTextureCount>0", Try(() => UnityEngine.Texture.nonStreamingTextureCount > 0));
        Rec(k + "streamingRendererCount", Try(() => UnityEngine.Texture.streamingRendererCount));
        Rec(k + "streamingTextureLoadingCount", Try(() => UnityEngine.Texture.streamingTextureLoadingCount));
        Rec(k + "streamingTexturePendingLoadCount", Try(() => UnityEngine.Texture.streamingTexturePendingLoadCount));
        Rec(k + "totalTextureMemory>0", Try(() => UnityEngine.Texture.totalTextureMemory > 0));
        Rec(k + "currentTextureMemory>0", Try(() => UnityEngine.Texture.currentTextureMemory > 0));
        Rec(k + "desiredTextureMemory>0", Try(() => UnityEngine.Texture.desiredTextureMemory > 0));
        Rec(k + "targetTextureMemory>0", Try(() => UnityEngine.Texture.targetTextureMemory > 0));
        Rec(k + "nonStreamingTextureMemory>0", Try(() => UnityEngine.Texture.nonStreamingTextureMemory > 0));
    }

    void Stage(string stage)
    {
        Globals(stage);
        Texture(stage, "stream", m_Stream);
        Texture(stage, "plain", m_Plain);
        Texture(stage, "runtime", m_Runtime);
        // Streaming textures of other sizes (1024x1024, 4x4).
        Texture(stage, "big", m_Big);
        Texture(stage, "tiny", m_Tiny);
    }

    IEnumerator Start()
    {
        m_Stream = Resources.Load<Texture2D>("stream");
        m_Plain = Resources.Load<Texture2D>("plain");
        m_Runtime = new Texture2D(64, 64, TextureFormat.RGBA32, true);
        m_Big = Resources.Load<Texture2D>("big");
        m_Tiny = Resources.Load<Texture2D>("tiny");
        Stage("load");
        // A streaming texture without mipmaps, with a requested level, before streaming is active
        // (while it is loaded with streaming active the Editor's calculated levels of it and of
        // plain.png swap between runs), then unloaded.
        m_NoMip = Resources.Load<Texture2D>("nomip");
        m_NoMip.requestedMipmapLevel = 3;
        Texture("load", "nomip", m_NoMip);
        Resources.UnloadAsset(m_NoMip);

        // A camera looking at two quads, one for each imported texture, five units away.
        var camera = new GameObject("eye", typeof(Camera)).GetComponent<Camera>();
        camera.transform.position = new Vector3(0f, 0f, -5f);
        camera.targetTexture = new RenderTexture(64, 64, 24);
        foreach (var (texture, x) in new[] { (m_Stream, -1f), (m_Plain, 1f) })
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.transform.position = new Vector3(x, 0f, 0f);
            var material = new Material(Shader.Find("Unlit/Texture")) { mainTexture = texture };
            quad.GetComponent<MeshRenderer>().sharedMaterial = material;
        }
        for (int i = 0; i < 10; i++) yield return null;
        Stage("viewed");

        QualitySettings.streamingMipmapsActive = true;
        for (int i = 0; i < 10; i++) yield return null;
        Stage("active");

        m_Stream.requestedMipmapLevel = 3;
        m_Plain.requestedMipmapLevel = 3;
        m_Runtime.requestedMipmapLevel = 3;
        m_Big.requestedMipmapLevel = 0;
        m_Tiny.requestedMipmapLevel = 3;
        for (int i = 0; i < 10; i++) yield return null;
        Stage("requested");

        // A copy of a streaming texture with a requested level, and a negative request.
        var copy = Instantiate(m_Stream);
        Texture("requested", "copy", copy);
        Destroy(copy);
        m_Tiny.requestedMipmapLevel = -4;
        Rec("negative/tiny/requestedMipmapLevel", Try(() => m_Tiny.requestedMipmapLevel));

        // A larger maximum level reduction.
        m_Big.ClearRequestedMipmapLevel();
        m_Stream.ClearRequestedMipmapLevel();
        QualitySettings.streamingMipmapsMaxLevelReduction = 5;
        for (int i = 0; i < 10; i++) yield return null;
        foreach (var (name, texture) in new[] { ("stream", m_Stream), ("big", m_Big), ("tiny", m_Tiny) })
        {
            Rec("reduction5/" + name + "/calculatedMipmapLevel", Try(() => texture.calculatedMipmapLevel));
            Rec("reduction5/" + name + "/desiredMipmapLevel", Try(() => texture.desiredMipmapLevel));
        }
        QualitySettings.streamingMipmapsMaxLevelReduction = 2;

        m_Stream.ClearRequestedMipmapLevel();
        m_Stream.minimumMipmapLevel = 2;
        QualitySettings.streamingMipmapsActive = false;
        for (int i = 0; i < 5; i++) yield return null;
        Stage("cleared");

        m_Out.Append("{\"k\": \"complete\", \"v\": true}\n");
        var dir = Environment.GetEnvironmentVariable("MIP_STREAMING_OUT");
        if (!string.IsNullOrEmpty(dir)) File.WriteAllText(Path.Combine(dir, "values.jsonl"), m_Out.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.ExitPlaymode();
        UnityEditor.EditorApplication.Exit(0);
#endif
    }
}
