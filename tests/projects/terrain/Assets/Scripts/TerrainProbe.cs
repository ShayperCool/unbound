using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Records the scene's terrain (Assets/Terrain/ProbeTerrain.asset, made by ProbeRunner.Create):
/// its TerrainData as read from the asset (sizes, heights, steepness, normals, alphamaps, holes,
/// layers), Terrain queries, raycasts against its TerrainCollider and spheres dropped on it, into
/// <c>terrain.jsonl</c>; and images from a few cameras (built-in renderer, the default terrain
/// material) as PNGs. All in the directory <c>TERRAIN_PROBE_OUT</c>; then it quits.
/// </summary>
public class TerrainProbe : MonoBehaviour
{
    const int Width = 200, Height = 150;
    readonly StringBuilder m_Out = new StringBuilder();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("TerrainProbe").AddComponent<TerrainProbe>();

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    static string V(Vector3 v) => $"[{F(v.x)},{F(v.y)},{F(v.z)}]";
    static string V(Vector2 v) => $"[{F(v.x)},{F(v.y)}]";

    void Record(string kind, string body) => m_Out.Append("{\"case\":\"").Append(kind).Append("\",").Append(body).Append("}\n");

    IEnumerator Start()
    {
        var dir = System.Environment.GetEnvironmentVariable("TERRAIN_PROBE_OUT") ?? "/tmp/terrain";
        Directory.CreateDirectory(dir);
        var terrain = Terrain.activeTerrain;
        var data = terrain.terrainData;

        Record("data", $"\"heightmapResolution\":{data.heightmapResolution},\"size\":{V(data.size)},\"heightmapScale\":{V(data.heightmapScale)}," +
            $"\"alphamapResolution\":{data.alphamapResolution},\"alphamapLayers\":{data.alphamapLayers},\"alphamapWidth\":{data.alphamapWidth}," +
            $"\"baseMapResolution\":{data.baseMapResolution},\"holesResolution\":{data.holesResolution},\"enableHolesTextureCompression\":{(data.enableHolesTextureCompression ? "true" : "false")}," +
            $"\"bounds\":[{V(data.bounds.center)},{V(data.bounds.size)}],\"detailResolution\":{data.detailResolution},\"treeInstanceCount\":{data.treeInstanceCount}");
        foreach (var layer in data.terrainLayers)
            Record("layer", $"\"name\":\"{layer.name}\",\"texture\":\"{(layer.diffuseTexture != null ? layer.diffuseTexture.name : "")}\",\"tileSize\":{V(layer.tileSize)},\"tileOffset\":{V(layer.tileOffset)}," +
                $"\"metallic\":{F(layer.metallic)},\"smoothness\":{F(layer.smoothness)},\"specular\":[{F(layer.specular.r)},{F(layer.specular.g)},{F(layer.specular.b)},{F(layer.specular.a)}]");

        for (int y = 0; y < data.heightmapResolution; y += 8)
        {
            var row = new StringBuilder();
            for (int x = 0; x < data.heightmapResolution; x += 8) row.Append(x == 0 ? "" : ",").Append(F(data.GetHeight(x, y)));
            Record("height_row", $"\"y\":{y},\"heights\":[{row}]");
        }
        var block = data.GetHeights(10, 20, 3, 2);
        Record("get_heights", $"\"values\":[{F(block[0, 0])},{F(block[0, 1])},{F(block[0, 2])},{F(block[1, 0])},{F(block[1, 1])},{F(block[1, 2])}]");
        for (int j = 0; j <= 8; j++)
            for (int i = 0; i <= 8; i++)
            {
                float u = i / 8f * 0.97f + 0.013f, v = j / 8f * 0.95f + 0.021f;
                Record("interpolated", $"\"uv\":[{F(u)},{F(v)}],\"height\":{F(data.GetInterpolatedHeight(u, v))},\"steepness\":{F(data.GetSteepness(u, v))},\"normal\":{V(data.GetInterpolatedNormal(u, v))}");
            }
        var alphas = data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight);
        for (int y = 3; y < data.alphamapHeight; y += 12)
            for (int x = 5; x < data.alphamapWidth; x += 12)
                Record("alphamap", $"\"texel\":[{x},{y}],\"weights\":[{F(alphas[y, x, 0])},{F(alphas[y, x, 1])},{F(alphas[y, x, 2])}]");
        foreach (var (x, y) in new[] { (5, 50), (6, 50), (11, 55), (12, 55), (8, 53), (30, 30) })
            Record("hole", $"\"texel\":[{x},{y}],\"solid\":{(data.GetHoles(x, y, 1, 1)[0, 0] ? "true" : "false")}");

        Record("terrain", $"\"position\":{V(terrain.GetPosition())},\"heightmapPixelError\":{F(terrain.heightmapPixelError)},\"basemapDistance\":{F(terrain.basemapDistance)}," +
            $"\"drawInstanced\":{(terrain.drawInstanced ? "true" : "false")},\"material\":\"{(terrain.materialTemplate != null ? terrain.materialTemplate.shader.name : "")}\"," +
            $"\"castShadows\":{(terrain.shadowCastingMode)},\"collider\":\"{terrain.GetComponent<TerrainCollider>().terrainData.name}\"");
        for (int j = 0; j <= 6; j++)
            for (int i = 0; i <= 6; i++)
            {
                var p = new Vector3(-20f + i * 40f / 6f + 0.37f, 50f, -20f + j * 40f / 6f + 0.29f);
                float sample = terrain.SampleHeight(p);
                bool hit = Physics.Raycast(p, Vector3.down, out var info, 100f);
                Record("ray", $"\"from\":{V(p)},\"sample\":{F(sample)},\"hit\":{(hit ? "true" : "false")}" +
                    (hit ? $",\"point\":{V(info.point)},\"normal\":{V(info.normal)},\"distance\":{F(info.distance)},\"collider\":\"{info.collider.name}\"" : ""));
            }
        // Through the hole (texels 6..11 x 50..55 of 64 over 40 units from -20).
        foreach (var p in new[] { new Vector3(-14.4f, 50f, 12.2f), new Vector3(-16.5f, 50f, 11.6f) })
        {
            bool hit = Physics.Raycast(p, Vector3.down, out var info, 100f);
            Record("ray_hole", $"\"from\":{V(p)},\"hit\":{(hit ? "true" : "false")}" + (hit ? $",\"point\":{V(info.point)}" : ""));
        }

        // Spheres dropped onto the slope and the flat.
        var spheres = new Rigidbody[3];
        var starts = new[] { new Vector3(4f, 12f, 2f), new Vector3(-10f, 6f, -12f), new Vector3(12f, 9f, -6f) };
        for (int k = 0; k < spheres.Length; k++)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.transform.position = starts[k];
            spheres[k] = go.AddComponent<Rigidbody>();
        }
        for (int step = 1; step <= 150; step++)
        {
            yield return new WaitForFixedUpdate();
            if (step == 10 || step == 30 || step == 60 || step == 150)
                for (int k = 0; k < spheres.Length; k++)
                    Record("drop", $"\"sphere\":{k},\"step\":{step},\"position\":{V(spheres[k].position)},\"velocity\":{V(spheres[k].linearVelocity)}");
        }
        foreach (var s in spheres) Destroy(s.gameObject);
        yield return null;

        File.WriteAllText(Path.Combine(dir, "terrain.jsonl"), m_Out.ToString());

        // Images: the default pixel error and basemap distance, full detail, near the basemap.
        Capture(dir, "overview", new Vector3(0f, 22f, -38f), new Vector3(0f, 2f, 0f), terrain, 5f, 1000f);
        Capture(dir, "overview_detail", new Vector3(0f, 22f, -38f), new Vector3(0f, 2f, 0f), terrain, 1f, 1000f);
        Capture(dir, "close", new Vector3(-6f, 5f, -12f), new Vector3(4f, 3f, 4f), terrain, 1f, 1000f);
        Capture(dir, "basemap", new Vector3(0f, 22f, -38f), new Vector3(0f, 2f, 0f), terrain, 1f, 30f);
        Capture(dir, "top", new Vector3(0f, 60f, 0f), Vector3.zero, terrain, 1f, 1000f, true);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    void Capture(string dir, string name, Vector3 position, Vector3 target, Terrain terrain, float pixelError, float basemapDistance, bool top = false)
    {
        terrain.heightmapPixelError = pixelError;
        terrain.basemapDistance = basemapDistance;
        var go = new GameObject("camera");
        go.transform.position = position;
        if (top) go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        else go.transform.LookAt(target);
        var camera = go.AddComponent<Camera>();
        camera.fieldOfView = 50f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.4f, 0.5f, 0.7f);
        if (top)
        {
            camera.orthographic = true;
            camera.orthographicSize = 21f;
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
        DestroyImmediate(targetTexture);
        DestroyImmediate(go);
    }
}
