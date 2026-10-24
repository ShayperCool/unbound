using System;
using System.IO;
using UnityEngine;

/// <summary>Renders the main camera into a 320x180 render texture, reads it back and writes it to
/// <c>URP_PROBE_OUT</c> (a PNG; with <c>URP_PROBE_JSON</c> also sample pixels), then quits.</summary>
public class UrpProbe : MonoBehaviour
{
    const int Width = 320, Height = 180;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("UrpProbe").AddComponent<UrpProbe>();

    // Synchronous: the Editor in batch mode never reaches the end of a frame.
    void Start()
    {
        var output = Environment.GetEnvironmentVariable("URP_PROBE_OUT");
        var camera = Camera.main;
        var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
        camera.targetTexture = target;
        camera.Render();
        camera.Render();
        RenderTexture.active = target;
        var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
        texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        texture.Apply();
        RenderTexture.active = null;
        camera.targetTexture = null;
        if (!string.IsNullOrEmpty(output))
        {
            File.WriteAllBytes(output, texture.EncodeToPNG());
            Debug.Log("urp probe written to " + output);
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
