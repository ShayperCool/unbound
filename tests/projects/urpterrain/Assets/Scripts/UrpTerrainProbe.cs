using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// A terrain drawn with the Universal Render Pipeline's terrain shaders: five layers in stripes, seen
/// from above on its splat passes (base map distance 1000), on its base map (0), and half on each
/// (120, split into its four patches: the far two), with a smooth metallic sphere (the pipeline's Lit shader) beside it reflecting the scene's
/// default reflection. Writes <c>urpterrain.jsonl</c> (the settings each image used, then the base
/// maps the terrain made) and PNGs into <c>URP_TERRAIN_PROBE_OUT</c>, then quits.
/// </summary>
public class UrpTerrainProbe : MonoBehaviour
{
    const int Width = 480, Height = 270;
    readonly StringBuilder m_Out = new StringBuilder();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("UrpTerrainProbe").AddComponent<UrpTerrainProbe>();

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

    IEnumerator Start()
    {
        var dir = Environment.GetEnvironmentVariable("URP_TERRAIN_PROBE_OUT") ?? "/tmp/urpterrain";
        Directory.CreateDirectory(dir);
        var terrain = FindAnyObjectByType<Terrain>();
        yield return null;
        var eye = new Vector3(0f, 70f, -25f);
        var at = new Vector3(0f, 0f, 50f);
        Capture(dir, "splat", terrain, eye, at, 1000f);
        Capture(dir, "base0", terrain, eye, at, 0f);
        // Split into its four patches (a flat terrain is otherwise drawn as one): the far two
        // past the base map distance.
        terrain.heightmapMinimumLODSimplification = 1;
        Capture(dir, "half", terrain, eye, at, 120f);
        terrain.heightmapMinimumLODSimplification = 0;
        // Low over the ground: the layers' smoothness and metallic in the reflections.
        Capture(dir, "grazing", terrain, new Vector3(0f, 6f, -15f), new Vector3(0f, 0f, 60f), 1000f);
        Capture(dir, "grazingbase", terrain, new Vector3(0f, 6f, -15f), new Vector3(0f, 0f, 60f), 0f);
        AppendBaseMaps();
        File.WriteAllText(Path.Combine(dir, "urpterrain.jsonl"), m_Out.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    /// <summary>The base maps the terrain's materials made: one row per name and description,
    /// with how many.</summary>
    void AppendBaseMaps()
    {
        var maps = new System.Collections.Generic.SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var rt in Resources.FindObjectsOfTypeAll<RenderTexture>())
        {
            if (!rt.name.StartsWith("BaseMap-", StringComparison.Ordinal)) continue;
            var row = $"{{\"basemap\":\"{rt.name}\",\"width\":{rt.width},\"height\":{rt.height},\"graphicsFormat\":\"{rt.graphicsFormat}\",\"sRGB\":{(rt.sRGB ? "true" : "false")},\"useMipMap\":{(rt.useMipMap ? "true" : "false")},\"autoGenerateMips\":{(rt.autoGenerateMips ? "true" : "false")},\"mipmapCount\":{rt.mipmapCount},\"filterMode\":\"{rt.filterMode}\",\"anisoLevel\":{rt.anisoLevel},\"wrapMode\":\"{rt.wrapMode}\",\"hideFlags\":\"{rt.hideFlags}\"";
            maps.TryGetValue(row, out int count);
            maps[row] = count + 1;
        }
        foreach (var pair in maps) m_Out.Append(pair.Key).Append($",\"count\":{pair.Value}}}\n");
    }

    void Capture(string dir, string name, Terrain terrain, Vector3 position, Vector3 target, float basemapDistance)
    {
        terrain.basemapDistance = basemapDistance;
        var go = new GameObject("camera");
        go.transform.position = position;
        go.transform.LookAt(target);
        var camera = go.AddComponent<Camera>();
        camera.fieldOfView = 55f;
        camera.farClipPlane = 1000f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.4f, 0.5f, 0.7f);
        var targetTexture = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
        camera.targetTexture = targetTexture;
        camera.Render();
        RenderTexture.active = targetTexture;
        var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
        texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        texture.Apply();
        RenderTexture.active = null;
        camera.targetTexture = null;
        File.WriteAllBytes(Path.Combine(dir, name + ".png"), texture.EncodeToPNG());
        m_Out.Append($"{{\"image\":\"{name}\",\"basemapDistance\":{F(terrain.basemapDistance)}}}\n");
        DestroyImmediate(targetTexture);
        DestroyImmediate(go);
        Destroy(texture);
    }
}
