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
/// fade and a short tree distance. Writes <c>treebillboards.jsonl</c> (the settings each image used)
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
                    prototypeIndex = 0,
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
        Capture(dir, "pitch20", terrain, new Vector3(0f, 40f, -60f), new Vector3(0f, 0f, 110f), 50f, 5f, 5000f);
        Capture(dir, "pitch55", terrain, new Vector3(0f, 170f, 30f), new Vector3(0f, 0f, 150f), 50f, 5f, 5000f);
        Capture(dir, "start20", terrain, new Vector3(0f, 7f, -10f), new Vector3(0f, 4f, 200f), 20f, 5f, 5000f);
        Capture(dir, "fade40", terrain, new Vector3(0f, 7f, -10f), new Vector3(0f, 4f, 200f), 25f, 40f, 5000f);
        Capture(dir, "distance150", terrain, new Vector3(0f, 7f, -10f), new Vector3(0f, 4f, 200f), 50f, 5f, 150f);
        File.WriteAllText(Path.Combine(dir, "treebillboards.jsonl"), m_Out.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
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
        m_Out.Append($"{{\"image\":\"{name}\",\"treeBillboardDistance\":{F(terrain.treeBillboardDistance)},\"treeCrossFadeLength\":{F(terrain.treeCrossFadeLength)},\"treeDistance\":{F(terrain.treeDistance)},\"pitch\":{F(euler.x)},\"yaw\":{F(euler.y)}}}\n");
        DestroyImmediate(targetTexture);
        DestroyImmediate(go);
        Destroy(texture);
    }
}
