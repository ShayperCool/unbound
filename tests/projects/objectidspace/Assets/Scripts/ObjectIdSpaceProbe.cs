using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>
/// Records what Camera.SubmitRenderRequest with an ObjectIdRequest writes into an sRGB and a linear
/// ARGB32 target in the project's colour space (recorded in Gamma and in Linear): the pixels of
/// four cubes and the background, their decoded ids and the target's id histogram.
/// </summary>
public sealed class ObjectIdSpaceProbe : MonoBehaviour
{
    readonly StringBuilder m_Output = new StringBuilder();
    string m_Directory;
    int m_Frame;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void StartProbe()
    {
        var directory = Environment.GetEnvironmentVariable("OBJECT_ID_SPACE_OUT");
        if (string.IsNullOrEmpty(directory)) return;
        var probe = new GameObject("ObjectIdSpaceProbe").AddComponent<ObjectIdSpaceProbe>();
        probe.m_Directory = directory;
    }

    void Put(string key, string value) =>
        m_Output.Append("{\"k\":\"").Append(key).Append("\",\"v\":\"").Append(value.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append("\"}\n");

    void Update()
    {
        if (++m_Frame != 3) return;
        Put("colorSpace", QualitySettings.activeColorSpace.ToString());
        Record();
        m_Output.Append("{\"k\":\"complete\",\"v\":true}\n");
        Directory.CreateDirectory(m_Directory);
        File.WriteAllText(Path.Combine(m_Directory, "values.jsonl"), m_Output.ToString());
        UnityEditor.EditorApplication.Exit(0);
    }

    void Record()
    {
        var root = new GameObject("objectIds");
        root.transform.position = new Vector3(0, 500, 0);
        var cameraObject = new GameObject("idCamera");
        cameraObject.transform.SetParent(root.transform, false);
        cameraObject.transform.localPosition = new Vector3(0, 0, -10);
        var camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false;
        camera.orthographic = true;
        camera.orthographicSize = 4;
        camera.nearClipPlane = 1;
        camera.farClipPlane = 20;
        camera.targetTexture = new RenderTexture(64, 64, 24) { name = "idCameraTarget" };
        var lit = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        for (int i = 0; i < 4; i++)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "id" + (char)('A' + i);
            cube.transform.SetParent(root.transform, false);
            // Distinct depths, far to near: D, C, B, A (ids 1..4 in that order).
            cube.transform.localPosition = new Vector3(-3f + 2f * i, 0, 3 - i);
            cube.GetComponent<Renderer>().sharedMaterial = lit;
        }
        foreach (var (label, readWrite) in new[] { ("srgb", RenderTextureReadWrite.sRGB), ("linear", RenderTextureReadWrite.Linear) })
        {
            var target = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGB32, readWrite) { name = "ids" };
            target.Create();
            var request = new ObjectIdRequest(target);
            camera.SubmitRenderRequest(request);
            Put(label + ".sRGB", target.sRGB.ToString());
            Put(label + ".mapping", request.result == null ? "null" : string.Join(",", request.result.idToObjectMapping.Select(o => o == null ? "null" : o.name)));
            RenderTexture.active = target;
            var read = new Texture2D(64, 64, TextureFormat.RGBA32, false, true);
            read.ReadPixels(new Rect(0, 0, 64, 64), 0, 0);
            foreach (var (point, x, y) in new[] { ("a", 10, 32), ("b", 26, 32), ("c", 42, 32), ("d", 58, 32), ("background", 32, 4) })
            {
                Color32 pixel = read.GetPixel(x, y);
                Put(label + ".pixel." + point, pixel.r + "," + pixel.g + "," + pixel.b + "," + pixel.a + " id " + ObjectIdResult.DecodeIdFromColor(read.GetPixel(x, y)));
            }
            var ids = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var pixel in read.GetPixels32())
            {
                var key = pixel.r + "," + pixel.g + "," + pixel.b + "," + pixel.a;
                ids[key] = ids.TryGetValue(key, out var n) ? n + 1 : 1;
            }
            Put(label + ".colors", string.Join(";", ids.Select(p => p.Key + ":" + p.Value)));
            RenderTexture.active = null;
            Object.DestroyImmediate(read);
            target.Release();
        }
    }
}
