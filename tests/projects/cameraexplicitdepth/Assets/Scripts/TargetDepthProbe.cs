using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

// Owned targets are initialized by a direct clearing camera before any Nothing render.
// An unrelated HDR render exercises reuse of the engine's intermediate, never the
// undefined initial contents of a user-allocated or discarded RenderTexture.
public class TargetDepthProbe : MonoBehaviour
{
    public static Action<int> finished;
    readonly StringBuilder values = new StringBuilder();
    string output;
    const int W = 96, H = 64;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("TargetDepthProbe").AddComponent<TargetDepthProbe>();

    void Value(string key, string json) => values.Append("{\"k\":\"").Append(key).Append("\",\"v\":").Append(json).Append("}\n");

    static Camera CameraFor(int layer)
    {
        var c = new GameObject("Camera").AddComponent<Camera>();
        c.enabled = false;
        c.transform.position = new Vector3(0, 0, -10);
        c.orthographic = true;
        c.orthographicSize = 1;
        c.aspect = (float)W / H;
        c.nearClipPlane = 0.5f;
        c.farClipPlane = 20;
        c.cullingMask = 1 << layer;
        c.allowHDR = false;
        c.allowMSAA = true;
        c.renderingPath = RenderingPath.Forward;
        c.backgroundColor = Color.black;
        return c;
    }

    static GameObject Quad(int layer, Color color, float x, float y, float w, float h, float distance)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.layer = layer;
        go.transform.position = new Vector3(x, y, distance - 10);
        go.transform.localScale = new Vector3(w, h, 1);
        go.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Unlit/Color")) { color = color };
        return go;
    }

    void Capture(string key, RenderTexture target)
    {
        var old = RenderTexture.active;
        RenderTexture.active = target;
        var shot = new Texture2D(W, H, TextureFormat.RGB24, false);
        shot.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        shot.Apply();
        RenderTexture.active = old;
        File.WriteAllBytes(Path.Combine(output, key + ".png"), shot.EncodeToPNG());
        Value(key + ".size", "[96,64]");
        Destroy(shot);
    }

    IEnumerator Start()
    {
        output = Environment.GetEnvironmentVariable("STACK_PROBE_OUT");
        Directory.CreateDirectory(output);
        Value("colorSpace", "\"" + QualitySettings.activeColorSpace + "\"");
        Quad(8, Color.red, -0.55f, 0, 1.2f, 1.5f, 4);
        Quad(9, Color.green, 0, 0, 2.6f, 1.25f, 8);
        Quad(9, Color.blue, 0.45f, 0.2f, 0.5f, 0.5f, 2);
        Quad(10, Color.yellow, 0, 0, 2.8f, 1.8f, 6);
        var poisonQuad = Quad(11, Color.magenta, 0, 0, 4, 4, 1);
        var seed = CameraFor(8);
        var draw = CameraFor(9);
        var witness = CameraFor(10);
        var poison = CameraFor(11);
        seed.clearFlags = poison.clearFlags = CameraClearFlags.SolidColor;
        witness.clearFlags = CameraClearFlags.Nothing;
        poison.allowHDR = true;
        // Direct byte and HDR targets, then HDR-to-byte intermediates at one/four
        // samples, including quality/target sample disagreement. Full rect only.
        var modes = new[] { "direct", "float", "direct4", "hdr1", "hdr4", "hdrTarget4", "hdrBoth4" };
        foreach (string mode in modes)
        foreach (var clear in new[] { CameraClearFlags.Nothing, CameraClearFlags.Depth, CameraClearFlags.SolidColor })
        foreach (bool near in new[] { true, false })
        {
            QualitySettings.antiAliasing = mode == "hdr4" || mode == "hdrBoth4" ? 4 : 0;
            int samples = mode == "direct4" || mode == "hdrTarget4" || mode == "hdrBoth4" ? 4 : 1;
            var target = new RenderTexture(W, H, 24, mode == "float" ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32);
            target.antiAliasing = samples;
            target.Create();
            var unrelated = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            unrelated.antiAliasing = samples;
            unrelated.Create();
            seed.targetTexture = draw.targetTexture = witness.targetTexture = target;
            poison.targetTexture = unrelated;
            draw.allowHDR = mode == "float" || mode.StartsWith("hdr");
            draw.clearFlags = clear;
            string key = "owned." + mode + "." + clear + (near ? ".near" : ".far");
            seed.Render();
            Capture(key + ".seed", target);
            for (int frame = 1; frame <= 2; frame++)
            {
                poisonQuad.transform.position = new Vector3(0, 0, (near ? 1 : 12) - 10);
                poison.Render();
                draw.Render();
                Capture(key + ".f" + frame, target);
                // A direct camera shows the explicit target's surviving depth,
                // separately from the HDR camera's intermediate depth.
                witness.Render();
                Capture(key + ".w" + frame, target);
                yield return null;
            }
            Value(key + ".owned", (seed.targetTexture == target && draw.targetTexture == target && target.IsCreated()) ? "true" : "false");
            seed.targetTexture = draw.targetTexture = witness.targetTexture = poison.targetTexture = null;
            target.Release();
            unrelated.Release();
            Destroy(target);
            Destroy(unrelated);
        }
        Value("complete", "true");
        File.WriteAllText(Path.Combine(output, "values.jsonl"), values.ToString());
        Debug.Log("TARGET_DEPTH_PROBE_DONE " + output);
        if (finished != null) finished(0);
        else
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(0);
#else
            Application.Quit(0);
#endif
        }
    }
}
