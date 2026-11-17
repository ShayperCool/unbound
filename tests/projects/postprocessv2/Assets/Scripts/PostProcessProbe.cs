using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.PostProcessing;

/// <summary>
/// A camera of the built-in pipeline draws a quad of three colours over a solid background into a
/// 64x64 render texture: without a Post-processing Stack v2 layer (`plain`), with a layer and no
/// volume (`layer`), and with a layer and a global volume whose only effect is a Vignette
/// (`vignette`). Writes the texture's pixels on an 8x8 grid (bytes) per case into values.jsonl in
/// POST_PROCESS_V2_OUT and exits. Then, for a texture bound only globally
/// (Shader.SetGlobalTexture), the colour a fullscreen draw samples with a shader that declares
/// the property in its Properties block and with one that does not, for `_MainTex` and another
/// name (`global.<shader>`, the centre pixel's bytes).
/// </summary>
public sealed class PostProcessProbe : MonoBehaviour
{
    const int Size = 64, Grid = 8, VolumeLayer = 8;
    readonly StringBuilder m_Out = new StringBuilder();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() => new GameObject("PostProcessProbe").AddComponent<PostProcessProbe>();

    void Rec(string key, string json) => m_Out.Append("{\"k\":\"").Append(key).Append("\",\"v\":").Append(json).Append("}\n");

    IEnumerator Start()
    {
        // Nonzero unless every case was recorded: an exception ends the coroutine through the
        // finally block, which exits with it.
        int code = 1;
        string dir = Environment.GetEnvironmentVariable("POST_PROCESS_V2_OUT");
        var target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { name = "ProbeTarget" };
        var camera = new GameObject("ProbeCamera").AddComponent<Camera>();
        camera.transform.position = new Vector3(0, 0, -5);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.2f, 0.4f, 0.6f, 1);
        camera.fieldOfView = 40;
        camera.targetTexture = target;
        Quad(new Vector3(-0.6f, 0, 0), new Color(0.9f, 0.3f, 0.1f));
        Quad(new Vector3(0.6f, 0, 0), new Color(0.2f, 0.8f, 0.3f));
        Quad(new Vector3(0, 0.9f, -0.1f), new Color(0.95f, 0.95f, 0.9f));
        var readback = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
        PostProcessVolume volume = null;
        try
        {
            yield return Capture("plain", target, readback);
            var layer = camera.gameObject.AddComponent<PostProcessLayer>();
#if UNITY_EDITOR
            layer.Init(UnityEditor.AssetDatabase.LoadAssetAtPath<PostProcessResources>("Packages/com.unity.postprocessing/PostProcessing/PostProcessResources.asset"));
#endif
            layer.volumeTrigger = camera.transform;
            layer.volumeLayer = 1 << VolumeLayer;
            yield return Capture("layer", target, readback);
            var vignette = ScriptableObject.CreateInstance<Vignette>();
            vignette.enabled.Override(true);
            vignette.intensity.Override(0.5f);
            vignette.smoothness.Override(0.4f);
            volume = PostProcessManager.instance.QuickVolume(VolumeLayer, 100, vignette);
            yield return Capture("vignette", target, readback);
            Globals(target, readback);
            m_Out.Append("{\"k\":\"complete\",\"v\":true}\n");
            if (string.IsNullOrEmpty(dir)) Debug.Log(m_Out.ToString());
            else
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "values.jsonl"), m_Out.ToString());
            }
            code = 0;
        }
        finally
        {
            if (volume != null) RuntimeUtilities.DestroyVolume(volume, true, true);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(code);
#else
            Application.Quit(code);
#endif
        }
    }

    static readonly string[] GlobalShaders = { "MainUndeclared", "MainDeclared", "OtherUndeclared", "OtherDeclared" };

    void Globals(RenderTexture target, Texture2D readback)
    {
        var source = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        var texels = new Color32[16];
        for (int i = 0; i < texels.Length; i++) texels[i] = new Color32(64, 128, 192, 255);
        source.SetPixels32(texels);
        source.Apply();
        var triangle = new Mesh { vertices = new[] { new Vector3(-1, -1, 0), new Vector3(3, -1, 0), new Vector3(-1, 3, 0) }, triangles = new[] { 0, 1, 2 } };
        foreach (var name in GlobalShaders)
        {
            string property = name.StartsWith("Main", StringComparison.Ordinal) ? "_MainTex" : "_OtherTex";
            Shader.SetGlobalTexture(property, source);
            var cmd = new CommandBuffer { name = "global " + name };
            cmd.SetRenderTarget(target);
            cmd.ClearRenderTarget(false, true, Color.black);
            cmd.DrawMesh(triangle, Matrix4x4.identity, new Material(Shader.Find("Hidden/PostProcessProbe/" + name)), 0, 0);
            Graphics.ExecuteCommandBuffer(cmd);
            Shader.SetGlobalTexture(property, null);
            var active = RenderTexture.active;
            RenderTexture.active = target;
            readback.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            readback.Apply();
            RenderTexture.active = active;
            var p = readback.GetPixel(Size / 2, Size / 2);
            Color32 c = p;
            Rec("global." + name, "[" + c.r + "," + c.g + "," + c.b + "]");
        }
    }

    static void Quad(Vector3 position, Color color)
    {
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.transform.position = position;
        quad.transform.localScale = new Vector3(1.1f, 1.6f, 1);
        var material = new Material(Shader.Find("Unlit/Color"));
        material.color = color;
        quad.GetComponent<Renderer>().sharedMaterial = material;
    }

    /// <summary>The camera's image after two frames with the case's setup (the volume
    /// blending and the layer's resources settle in the first).</summary>
    IEnumerator Capture(string name, RenderTexture target, Texture2D readback)
    {
        yield return null;
        yield return new WaitForEndOfFrame();
        yield return null;
        yield return new WaitForEndOfFrame();
        var active = RenderTexture.active;
        RenderTexture.active = target;
        readback.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
        readback.Apply();
        RenderTexture.active = active;
        var pixels = readback.GetPixels32();
        var json = new StringBuilder("[");
        for (int y = 0; y < Grid; y++)
            for (int x = 0; x < Grid; x++)
            {
                var p = pixels[(y * Size / Grid + Size / Grid / 2) * Size + x * Size / Grid + Size / Grid / 2];
                if (json.Length > 1) json.Append(',');
                json.Append('[').Append(p.r).Append(',').Append(p.g).Append(',').Append(p.b).Append(']');
            }
        Rec(name, json.Append(']').ToString());
    }
}
