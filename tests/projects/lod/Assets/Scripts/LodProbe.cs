using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Which level of a LODGroup a camera draws: a group of three cubes (red, green, blue: LOD 0, 1, 2,
/// culled past the last) seen from distances, fields of view, LOD biases, group scales and sizes,
/// orthographic sizes, with QualitySettings.maximumLODLevel and LODGroup.ForceLOD; the drawn level
/// is the color at the center of a small render (9x9: its center pixel on the axis). Distances
/// go from 0.5 in steps of 0.02, orthographic sizes from 0.2 in steps of 0.01. Writes <c>lod.jsonl</c> into <c>LOD_PROBE_OUT</c>,
/// then quits. Also the group's API values (GetLODs, size, localReferencePoint, RecalculateBounds).
/// </summary>
public class LodProbe : MonoBehaviour
{
    readonly StringBuilder m_Out = new StringBuilder();
    RenderTexture m_Target;
    Texture2D m_Read;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("LodProbe").AddComponent<LodProbe>();

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

    GameObject Cube(Transform parent, Color color, Vector3 position, float scale)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localScale = Vector3.one * scale;
        var m = new Material(Shader.Find("Unlit/Color"));
        m.color = color;
        go.GetComponent<Renderer>().sharedMaterial = m;
        return go;
    }

    /// <summary>The level drawn at the center: 0 red, 1 green, 2 blue, -1 none.</summary>
    int Drawn(Camera camera)
    {
        camera.targetTexture = m_Target;
        camera.Render();
        RenderTexture.active = m_Target;
        m_Read.ReadPixels(new Rect(0, 0, 9, 9), 0, 0);
        RenderTexture.active = null;
        camera.targetTexture = null;
        var c = m_Read.GetPixel(4, 4);
        if (c.r > 0.5f && c.g < 0.5f && c.b < 0.5f) return 0;
        if (c.g > 0.5f && c.r < 0.5f && c.b < 0.5f) return 1;
        if (c.b > 0.5f && c.r < 0.5f && c.g < 0.5f) return 2;
        return -1;
    }

    IEnumerator Start()
    {
        var dir = System.Environment.GetEnvironmentVariable("LOD_PROBE_OUT") ?? "/tmp/lod";
        Directory.CreateDirectory(dir);
        m_Target = new RenderTexture(9, 9, 24, RenderTextureFormat.ARGB32);
        m_Read = new Texture2D(9, 9, TextureFormat.RGBA32, false);
        float startBias = QualitySettings.lodBias;
        int startMax = QualitySettings.maximumLODLevel;

        var root = new GameObject("Group");
        var r0 = Cube(root.transform, Color.red, Vector3.zero, 1f);
        var r1 = Cube(root.transform, Color.green, Vector3.zero, 1f);
        var r2 = Cube(root.transform, Color.blue, Vector3.zero, 1f);
        var group = root.AddComponent<LODGroup>();
        group.SetLODs(new[]
        {
            new LOD(0.6f, new Renderer[] { r0.GetComponent<Renderer>() }),
            new LOD(0.3f, new Renderer[] { r1.GetComponent<Renderer>() }),
            new LOD(0.1f, new Renderer[] { r2.GetComponent<Renderer>() }),
        });
        group.RecalculateBounds();
        m_Out.Append($"{{\"case\":\"api\",\"lodCount\":{group.lodCount},\"size\":{F(group.size)},\"reference\":[{F(group.localReferencePoint.x)},{F(group.localReferencePoint.y)},{F(group.localReferencePoint.z)}],\"fadeMode\":\"{group.fadeMode}\",\"animateCrossFading\":{(group.animateCrossFading ? "true" : "false")},\"startBias\":{F(startBias)},\"startMax\":{startMax}}}\n");
        foreach (var lod in group.GetLODs())
            m_Out.Append($"{{\"case\":\"lod\",\"height\":{F(lod.screenRelativeTransitionHeight)},\"fade\":{F(lod.fadeTransitionWidth)},\"renderers\":{lod.renderers.Length}}}\n");

        var cameraObject = new GameObject("Camera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.enabled = false;

        // Each sample is drawn in a frame of its own, the settings made the frame before.
        IEnumerator Sweep(string name, float fov, float bias, float scale, int maxLevel, int forced, Vector3 offset)
        {
            QualitySettings.lodBias = bias;
            QualitySettings.maximumLODLevel = maxLevel;
            root.transform.localScale = Vector3.one * scale;
            group.ForceLOD(forced);
            camera.fieldOfView = fov;
            yield return null;
            var levels = new List<string>();
            for (int k = 0; k <= 1200; k++)
            {
                float d = 0.5f + k * 0.02f;
                cameraObject.transform.position = offset + new Vector3(0f, 0f, -d);
                cameraObject.transform.LookAt(Vector3.zero);
                yield return null;
                levels.Add(Drawn(camera).ToString());
            }
            m_Out.Append($"{{\"case\":\"sweep\",\"name\":\"{name}\",\"fov\":{F(fov)},\"bias\":{F(bias)},\"scale\":{F(scale)},\"max\":{maxLevel},\"forced\":{forced},\"levels\":[{string.Join(",", levels)}]}}\n");
        }

        yield return Sweep("base", 60f, 1f, 1f, 0, -1, Vector3.zero);
        yield return Sweep("fov30", 30f, 1f, 1f, 0, -1, Vector3.zero);
        yield return Sweep("fov90", 90f, 1f, 1f, 0, -1, Vector3.zero);
        yield return Sweep("bias2", 60f, 2f, 1f, 0, -1, Vector3.zero);
        yield return Sweep("bias05", 60f, 0.5f, 1f, 0, -1, Vector3.zero);
        yield return Sweep("scale2", 60f, 1f, 2f, 0, -1, Vector3.zero);
        yield return Sweep("max1", 60f, 1f, 1f, 1, -1, Vector3.zero);
        yield return Sweep("forced2", 60f, 1f, 1f, 0, 2, Vector3.zero);
        yield return Sweep("forced0", 60f, 1f, 1f, 0, 0, Vector3.zero);
        // Seen from aside: the distance is the camera's to the group's reference point.
        yield return Sweep("offaxis", 60f, 1f, 1f, 0, -1, new Vector3(3f, 0f, 0f));
        group.ForceLOD(-1);

        // A group whose size was set.
        group.size = 3f;
        group.localReferencePoint = new Vector3(0f, 0f, 0f);
        yield return Sweep("size3", 60f, 1f, 1f, 0, -1, Vector3.zero);
        group.RecalculateBounds();
        m_Out.Append($"{{\"case\":\"recalculated\",\"size\":{F(group.size)},\"reference\":[{F(group.localReferencePoint.x)},{F(group.localReferencePoint.y)},{F(group.localReferencePoint.z)}]}}\n");

        // Orthographic: by orthographic size.
        camera.orthographic = true;
        QualitySettings.lodBias = 1f;
        QualitySettings.maximumLODLevel = 0;
        root.transform.localScale = Vector3.one;
        var ortho = new List<string>();
        for (int k = 0; k <= 600; k++)
        {
            camera.orthographicSize = 0.2f + k * 0.01f;
            cameraObject.transform.position = new Vector3(0f, 0f, -5f);
            cameraObject.transform.rotation = Quaternion.identity;
            yield return null;
            ortho.Add(Drawn(camera).ToString());
        }
        m_Out.Append($"{{\"case\":\"ortho\",\"levels\":[{string.Join(",", ortho)}]}}\n");

        QualitySettings.lodBias = startBias;
        QualitySettings.maximumLODLevel = startMax;
        File.WriteAllText(Path.Combine(dir, "lod.jsonl"), m_Out.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
