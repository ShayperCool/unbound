using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Terrain tree billboards: Soft Occlusion trees (a prototype whose shaders name a BillboardShader)
/// placed in staggered rows from 30 to 380 units ahead of the cameras, of several widths, heights
/// and colors, drawn as meshes near the camera and as billboards past treeBillboardDistance.
/// Images from the ground, from above at two pitches, with a nearer billboard start, a long cross
/// fade and a short tree distance, turned about up, under a brighter tinted sun and with at most
/// three mesh trees, with a second directional light, with a point light, with a third prototype
/// wider than tall and with 2400 trees, and the billboard atlas as the ground view left it; then
/// (later rows) a wider-than-tall prototype saved as an asset, six directional lights and a Tree Creator
/// tree, each with its atlas. Every fourth tree is the tall prototype. Writes <c>treebillboards.jsonl</c> (the settings each image used)
/// and PNGs into <c>TREE_BILLBOARD_PROBE_OUT</c>, then quits.
/// </summary>
public class TreeBillboardProbe : MonoBehaviour
{
    const int Width = 480, Height = 270;
    readonly StringBuilder m_Out = new StringBuilder();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("TreeBillboardProbe").AddComponent<TreeBillboardProbe>();

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

    IEnumerator Start()
    {
        var dir = Environment.GetEnvironmentVariable("TREE_BILLBOARD_PROBE_OUT") ?? "/tmp/treebillboards";
        Directory.CreateDirectory(dir);
        var terrain = FindAnyObjectByType<Terrain>();
        var data = terrain.terrainData;
        m_Out.Append($"{{\"defaults\":{{\"treeDistance\":{F(terrain.treeDistance)},\"treeBillboardDistance\":{F(terrain.treeBillboardDistance)},\"treeCrossFadeLength\":{F(terrain.treeCrossFadeLength)},\"treeMaximumFullLODCount\":{terrain.treeMaximumFullLODCount}}}}}\n");
        var trees = new List<TreeInstance>();
        int row = 0;
        for (float z = 30f; z <= 380f; z += z < 100f ? 14f : 24f, row++)
            for (int k = -3; k <= 3; k++)
            {
                float x = (k + 0.5f * (row & 1)) * (6f + z * 0.26f);
                if (x < -195f || x > 195f) continue;
                int n = trees.Count;
                trees.Add(new TreeInstance
                {
                    prototypeIndex = n % 4 == 3 ? 1 : 0,
                    position = new Vector3((x + 200f) / 400f, 0f, z / 400f),
                    widthScale = 0.8f + 0.1f * (n % 5),
                    heightScale = 0.75f + 0.15f * (n % 4),
                    rotation = 0.7f * n,
                    color = n % 3 == 0 ? Color.white : new Color(1f, 0.85f + 0.05f * (n % 3), 0.7f, 1f),
                    lightmapColor = Color.white,
                });
            }
        data.SetTreeInstances(trees.ToArray(), false);
        terrain.Flush();
        yield return null;
        yield return null;
        Capture(dir, "ground", terrain, new Vector3(0f, 7f, -10f), new Vector3(0f, 4f, 200f), 50f, 5f, 5000f);
        AppendAtlas(dir);
        Capture(dir, "pitch20", terrain, new Vector3(0f, 40f, -60f), new Vector3(0f, 0f, 110f), 50f, 5f, 5000f);
        Capture(dir, "pitch55", terrain, new Vector3(0f, 170f, 30f), new Vector3(0f, 0f, 150f), 50f, 5f, 5000f);
        Capture(dir, "start20", terrain, new Vector3(0f, 7f, -10f), new Vector3(0f, 4f, 200f), 20f, 5f, 5000f);
        Capture(dir, "fade40", terrain, new Vector3(0f, 7f, -10f), new Vector3(0f, 4f, 200f), 25f, 40f, 5000f);
        Capture(dir, "distance150", terrain, new Vector3(0f, 7f, -10f), new Vector3(0f, 4f, 200f), 50f, 5f, 150f);
        // Turned 30 degrees about up: the atlas is drawn from the camera's yaw too.
        Capture(dir, "turned", terrain, new Vector3(0f, 7f, -10f), new Vector3(110f, 4f, 180f), 50f, 5f, 5000f);
        // A brighter, tinted sun: the atlas and the mesh trees are lit by its final color.
        var sun = FindAnyObjectByType<Light>();
        var (color, intensity) = (sun.color, sun.intensity);
        sun.color = new Color(1f, 0.85f, 0.7f);
        sun.intensity = 1.6f;
        Capture(dir, "brightsun", terrain, new Vector3(0f, 7f, -10f), new Vector3(0f, 4f, 200f), 50f, 5f, 5000f);
        sun.color = color;
        sun.intensity = intensity;
        // At most three mesh trees.
        terrain.treeMaximumFullLODCount = 3;
        Capture(dir, "maxmesh3", terrain, new Vector3(0f, 7f, -10f), new Vector3(0f, 4f, 200f), 50f, 5f, 5000f);
        terrain.treeMaximumFullLODCount = 50;
        // The sun alone changed, from the same view: is the atlas drawn again?
        sun.intensity = 1.6f;
        Capture(dir, "sunonly", terrain, new Vector3(0f, 7f, -10f), new Vector3(0f, 4f, 200f), 50f, 5f, 5000f);
        sun.intensity = intensity;
        var ground = (position: new Vector3(0f, 7f, -10f), target: new Vector3(0f, 4f, 200f));
        // A second directional light, bluish, from another side.
        var second = new GameObject("Second Light").AddComponent<Light>();
        second.type = LightType.Directional;
        second.transform.rotation = Quaternion.Euler(30f, -120f, 0f);
        second.color = new Color(0.6f, 0.7f, 1f);
        second.intensity = 0.8f;
        Capture(dir, "twolights", terrain, ground.position, ground.target, 50f, 5f, 5000f);
        DestroyImmediate(second.gameObject);
        // A point light among the near trees.
        var point = new GameObject("Point Light").AddComponent<Light>();
        point.type = LightType.Point;
        point.transform.position = new Vector3(0f, 5f, 40f);
        point.range = 30f;
        point.intensity = 2f;
        point.color = new Color(1f, 0.6f, 0.3f);
        Capture(dir, "pointlight", terrain, ground.position, ground.target, 50f, 5f, 5000f);
        DestroyImmediate(point.gameObject);
        // A prototype wider than tall: the round tree's mesh flattened, every fifth tree.
        var round = data.treePrototypes[0].prefab;
        var flat = Instantiate(round.GetComponent<MeshFilter>().sharedMesh);
        var vertices = flat.vertices;
        for (int i = 0; i < vertices.Length; i++) vertices[i] = Vector3.Scale(vertices[i], new Vector3(3f, 0.4f, 3f));
        flat.vertices = vertices;
        flat.RecalculateBounds();
        var wide = new GameObject("Wide");
        wide.transform.position = new Vector3(0f, -500f, 0f);
        wide.AddComponent<MeshFilter>().sharedMesh = flat;
        wide.AddComponent<MeshRenderer>().sharedMaterials = round.GetComponent<MeshRenderer>().sharedMaterials;
        var prototypes = new List<TreePrototype>(data.treePrototypes) { new TreePrototype { prefab = wide } };
        data.treePrototypes = prototypes.ToArray();
        var widened = new List<TreeInstance>(trees);
        for (int i = 4; i < widened.Count; i += 5)
        {
            var tree = widened[i];
            tree.prototypeIndex = 2;
            widened[i] = tree;
        }
        data.SetTreeInstances(widened.ToArray(), false);
        terrain.Flush();
        Capture(dir, "wide", terrain, ground.position, ground.target, 50f, 5f, 5000f);
        // More than 500 trees, in a dense grid of overlapping billboards.
        var many = new List<TreeInstance>();
        for (int z = 0; z < 48; z++)
            for (int x = 0; x < 50; x++)
            {
                int n = many.Count;
                many.Add(new TreeInstance
                {
                    prototypeIndex = n % 4 == 3 ? 1 : 0,
                    position = new Vector3((x * 7.5f + 12f) / 400f, 0f, (60f + z * 6.5f) / 400f),
                    widthScale = 0.8f + 0.1f * (n % 5),
                    heightScale = 0.75f + 0.15f * (n % 4),
                    rotation = 0.7f * n,
                    color = n % 3 == 0 ? Color.white : new Color(1f, 0.85f + 0.05f * (n % 3), 0.7f, 1f),
                    lightmapColor = Color.white,
                });
            }
        data.SetTreeInstances(many.ToArray(), false);
        terrain.Flush();
        Capture(dir, "many", terrain, ground.position, ground.target, 50f, 5f, 5000f);
        foreach (var rt in Resources.FindObjectsOfTypeAll<RenderTexture>())
            if (rt.name == "Tree Imposter Texture")
                m_Out.Append($"{{\"atlasSampler\":\"{rt.name}\",\"anisoLevel\":{rt.anisoLevel},\"autoGenerateMips\":{(rt.autoGenerateMips ? "true" : "false")},\"mipmapCount\":{rt.mipmapCount}}}\n");
        // Later cases (rows after the ones above). A prototype wider than tall saved as an asset
        // (Resources/Wide), every fifth tree.
        var original = data.treePrototypes;
        data.treePrototypes = new[] { original[0], original[1], new TreePrototype { prefab = Resources.Load<GameObject>("Wide") } };
        data.SetTreeInstances(widened.ToArray(), false);
        terrain.Flush();
        Capture(dir, "wideasset", terrain, ground.position, ground.target, 50f, 5f, 5000f);
        AppendAtlas(dir, "wideassetatlas");
        Capture(dir, "wideasset20", terrain, new Vector3(0f, 40f, -60f), new Vector3(0f, 0f, 110f), 50f, 5f, 5000f);
        // Six directional lights of several colors, intensities and directions, the sun first.
        data.treePrototypes = new[] { original[0], original[1] };
        data.SetTreeInstances(trees.ToArray(), false);
        terrain.Flush();
        var extra = new List<GameObject>();
        for (int i = 0; i < 5; i++)
        {
            var light = new GameObject("Light " + i).AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(20f + 12f * i, -150f + 70f * i, 0f);
            light.color = Color.HSVToRGB(0.15f * i, 0.6f, 1f);
            light.intensity = 0.3f + 0.25f * i;
            extra.Add(light.gameObject);
        }
        Capture(dir, "sixlights", terrain, ground.position, ground.target, 50f, 5f, 5000f);
        AppendAtlas(dir, "sixlightsatlas");
        foreach (var light in extra) DestroyImmediate(light);
        // A Tree Creator tree (Resources/Creator) as the only prototype.
        data.treePrototypes = new[] { new TreePrototype { prefab = Resources.Load<GameObject>("Creator") } };
        var creator = new List<TreeInstance>(trees);
        for (int i = 0; i < creator.Count; i++)
        {
            var tree = creator[i];
            tree.prototypeIndex = 0;
            creator[i] = tree;
        }
        data.SetTreeInstances(creator.ToArray(), false);
        terrain.Flush();
        Capture(dir, "creator", terrain, ground.position, ground.target, 50f, 5f, 5000f);
        AppendAtlas(dir, "creatoratlas");
        Capture(dir, "creator20", terrain, new Vector3(0f, 40f, -60f), new Vector3(0f, 0f, 110f), 50f, 5f, 5000f);
        File.WriteAllText(Path.Combine(dir, "treebillboards.jsonl"), m_Out.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    /// <summary>The atlas the billboards are cut from (the render texture named "Tree Imposter
    /// Texture"), as the last capture left it: a row of its size, format and sampler, and its
    /// first mip as atlas.png.</summary>
    void AppendAtlas(string dir, string image = "atlas")
    {
        RenderTexture atlas = null;
        foreach (var rt in Resources.FindObjectsOfTypeAll<RenderTexture>())
            if (rt.name == "Tree Imposter Texture") atlas = rt;
        if (atlas == null)
        {
            m_Out.Append(image == "atlas" ? "{\"atlas\":null}\n" : $"{{\"atlasImage\":\"{image}\",\"atlas\":null}}\n");
            return;
        }
        if (image != "atlas")
            m_Out.Append($"{{\"atlasImage\":\"{image}\",\"width\":{atlas.width},\"height\":{atlas.height}}}\n");
        else
            m_Out.Append($"{{\"atlas\":\"{atlas.name}\",\"width\":{atlas.width},\"height\":{atlas.height},\"graphicsFormat\":\"{atlas.graphicsFormat}\",\"depth\":{atlas.depth},\"mipmaps\":{(atlas.useMipMap ? "true" : "false")},\"sRGB\":{(atlas.sRGB ? "true" : "false")},\"filterMode\":\"{atlas.filterMode}\",\"wrapMode\":\"{atlas.wrapMode}\",\"mipMapBias\":{F(atlas.mipMapBias)},\"hideFlags\":{(int)atlas.hideFlags}}}\n");
        var previous = RenderTexture.active;
        RenderTexture.active = atlas;
        var texture = new Texture2D(atlas.width, atlas.height, TextureFormat.RGBA32, false);
        texture.ReadPixels(new Rect(0, 0, atlas.width, atlas.height), 0, 0);
        texture.Apply();
        RenderTexture.active = previous;
        File.WriteAllBytes(Path.Combine(dir, image + ".png"), texture.EncodeToPNG());
        Destroy(texture);
    }

    void Capture(string dir, string name, Terrain terrain, Vector3 position, Vector3 target, float billboardDistance, float crossFade, float treeDistance)
    {
        terrain.treeBillboardDistance = billboardDistance;
        terrain.treeCrossFadeLength = crossFade;
        terrain.treeDistance = treeDistance;
        var go = new GameObject("camera");
        go.transform.position = position;
        go.transform.LookAt(target);
        var camera = go.AddComponent<Camera>();
        camera.fieldOfView = 50f;
        camera.farClipPlane = 1000f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.55f, 0.65f, 0.8f);
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
        var euler = go.transform.eulerAngles;
        m_Out.Append($"{{\"image\":\"{name}\",\"treeMaximumFullLODCount\":{terrain.treeMaximumFullLODCount},\"treeBillboardDistance\":{F(terrain.treeBillboardDistance)},\"treeCrossFadeLength\":{F(terrain.treeCrossFadeLength)},\"treeDistance\":{F(terrain.treeDistance)},\"pitch\":{euler.x.ToString("F2", CultureInfo.InvariantCulture)},\"yaw\":{euler.y.ToString("F2", CultureInfo.InvariantCulture)}}}\n");
        DestroyImmediate(targetTexture);
        DestroyImmediate(go);
        Destroy(texture);
    }
}
