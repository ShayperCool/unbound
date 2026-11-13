using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Terrain heightmap LOD and the base map: images of a bumpy terrain (and the connected tile east of
/// it, at its own settings) from one view at several pixel errors, with heightmapMaximumLOD and
/// heightmapMinimumLODSimplification, from above orthographically, along the tiles' seam, and with
/// base map distances that put part or all of it on the base map.
/// Writes <c>terrainlod.jsonl</c> (the settings each image used, then the base maps the terrains made)
/// and PNGs into
/// <c>TERRAIN_LOD_PROBE_OUT</c>, then quits.
/// </summary>
public class TerrainLodProbe : MonoBehaviour
{
    const int Width = 480, Height = 270;
    readonly StringBuilder m_Out = new StringBuilder();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("TerrainLodProbe").AddComponent<TerrainLodProbe>();

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

    IEnumerator Start()
    {
        var dir = Environment.GetEnvironmentVariable("TERRAIN_LOD_PROBE_OUT") ?? "/tmp/terrainlod";
        Directory.CreateDirectory(dir);
        Terrain terrain = null, east = null;
        foreach (var t in FindObjectsByType<Terrain>(FindObjectsSortMode.None))
            if (t.name == "Bumpy") terrain = t; else east = t;
        yield return null;
        var eye = new Vector3(200f, 55f, -40f);
        var at = new Vector3(200f, 15f, 230f);
        Capture(dir, "pe5", terrain, eye, at, 5f, 1000f, 0, 0);
        Capture(dir, "pe1", terrain, eye, at, 1f, 1000f, 0, 0);
        Capture(dir, "pe40", terrain, eye, at, 40f, 1000f, 0, 0);
        Capture(dir, "pe200", terrain, eye, at, 200f, 1000f, 0, 0);
        Capture(dir, "maxlod2", terrain, eye, at, 1f, 1000f, 2, 0);
        Capture(dir, "minsimpl", terrain, eye, at, 200f, 1000f, 0, 2);
        Capture(dir, "minsimpl1", terrain, eye, at, 200f, 1000f, 0, 1);
        Capture(dir, "maxlod3minsimpl2", terrain, eye, at, 200f, 1000f, 3, 2);
        Capture(dir, "low", terrain, new Vector3(120f, 30f, 60f), new Vector3(260f, 20f, 300f), 8f, 1000f, 0, 0);
        Capture(dir, "ortho", terrain, new Vector3(200f, 200f, 200f), new Vector3(200f, 0f, 200.001f), 20f, 1000f, 0, 0, true);
        // Along the seam between the two tiles: their levels meet across it.
        east.heightmapPixelError = 30f;
        Capture(dir, "tiles", terrain, new Vector3(330f, 45f, -30f), new Vector3(420f, 15f, 200f), 30f, 1000f, 0, 0);
        east.heightmapPixelError = 5f;
        Capture(dir, "base150", terrain, eye, at, 5f, 150f, 0, 0);
        Capture(dir, "base0", terrain, eye, at, 5f, 0f, 0, 0);
        // Orthographic views draw no patch on the base map, whatever the distance.
        Capture(dir, "baseortho", terrain, new Vector3(200f, 200f, 200f), new Vector3(200f, 0f, 200.001f), 20f, 0f, 0, 0, true);
        AppendBaseMaps();
        // Five layers: the base map adds the second group of four onto the first.
        var layered = Layered();
        Capture(dir, "layers5base", layered, new Vector3(5050f, 70f, 4975f), new Vector3(5050f, 0f, 5050f), 5f, 0f, 0, 0);
        Capture(dir, "layers5splat", layered, new Vector3(5050f, 70f, 4975f), new Vector3(5050f, 0f, 5050f), 5f, 1000f, 0, 0);
        AppendBaseMaps();
        // The per-patch height errors and height ranges the levels of detail are worked out from.
        foreach (var tile in new[] { terrain, east })
        {
            var data = tile.terrainData;
            var line = new StringBuilder($"{{\"patches\":\"{tile.name}\",\"errors\":[");
            var errors = data.GetMaximumHeightError();
            for (int i = 0; i < errors.Length; i++) line.Append(i == 0 ? "" : ",").Append(F(errors[i]));
            line.Append("],\"extents\":[");
            var extents = data.GetPatchMinMaxHeights();
            for (int i = 0; i < extents.Length; i++) line.Append(i == 0 ? "" : ",").Append(F(extents[i].min)).Append(',').Append(F(extents[i].max));
            m_Out.Append(line.Append("]}\n"));
        }
        File.WriteAllText(Path.Combine(dir, "terrainlod.jsonl"), m_Out.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    /// <summary>The base maps the terrains' materials made so far: one row per name and
    /// description, with how many.</summary>
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

    /// <summary>A flat terrain far from the others with five plain-coloured layers in stripes,
    /// each stripe blending two neighbouring layers.</summary>
    static Terrain Layered()
    {
        var colors = new[] { new Color(0.8f, 0.2f, 0.2f), new Color(0.2f, 0.7f, 0.2f), new Color(0.2f, 0.3f, 0.8f), new Color(0.9f, 0.8f, 0.2f), new Color(0.9f, 0.9f, 0.9f) };
        var layers = new TerrainLayer[colors.Length];
        for (int i = 0; i < colors.Length; i++)
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, true);
            var pixels = new Color[16];
            for (int p = 0; p < pixels.Length; p++) pixels[p] = (p & 1) == 0 ? colors[i] : colors[i] * 0.8f;
            texture.SetPixels(pixels);
            texture.Apply();
            layers[i] = new TerrainLayer { diffuseTexture = texture, tileSize = new Vector2(8f, 8f), smoothness = 0.2f * i };
        }
        var data = new TerrainData { heightmapResolution = 33, alphamapResolution = 64, baseMapResolution = 128 };
        data.size = new Vector3(100f, 10f, 100f);
        data.terrainLayers = layers;
        var weights = new float[64, 64, colors.Length];
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                float t = x / 64f * colors.Length;
                int a = Mathf.Min((int)t, colors.Length - 1), b = (a + 1) % colors.Length;
                float f = (y & 16) == 0 ? 0f : t - a;
                weights[y, x, a] = 1f - f;
                weights[y, x, b] += f;
            }
        data.SetAlphamaps(0, 0, weights);
        var go = Terrain.CreateTerrainGameObject(data);
        go.transform.position = new Vector3(5000f, 0f, 5000f);
        return go.GetComponent<Terrain>();
    }

    void Capture(string dir, string name, Terrain terrain, Vector3 position, Vector3 target, float pixelError, float basemapDistance, int maximumLOD, int minimumSimplification, bool orthographic = false)
    {
        terrain.heightmapPixelError = pixelError;
        terrain.basemapDistance = basemapDistance;
        terrain.heightmapMaximumLOD = maximumLOD;
        terrain.heightmapMinimumLODSimplification = minimumSimplification;
        var go = new GameObject("camera");
        go.transform.position = position;
        go.transform.LookAt(target);
        var camera = go.AddComponent<Camera>();
        camera.fieldOfView = 55f;
        camera.farClipPlane = 2000f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.4f, 0.5f, 0.7f);
        if (orthographic)
        {
            camera.orthographic = true;
            camera.orthographicSize = 210f;
        }
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
        m_Out.Append($"{{\"image\":\"{name}\",\"pixelError\":{F(terrain.heightmapPixelError)},\"basemapDistance\":{F(terrain.basemapDistance)},\"maximumLOD\":{terrain.heightmapMaximumLOD},\"minimumLODSimplification\":{terrain.heightmapMinimumLODSimplification},\"orthographic\":{(orthographic ? "true" : "false")}}}\n");
        DestroyImmediate(targetTexture);
        DestroyImmediate(go);
        Destroy(texture);
    }
}
