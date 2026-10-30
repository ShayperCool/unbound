using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// What the Game view (Display 1) shows of cameras and overlay canvases that target other
/// displays: a red camera on Display 1, a green one on Display 2 over the right half, a blue one on
/// Display 1 over the top-left quarter, a yellow overlay canvas on Display 1 and a magenta one on
/// Display 2. A few frames in, the Game view is captured to <c>DISPLAY_PROBE_OUT</c>/game.png (the
/// Editor; Unbound's window is captured by `--screenshot`) and each camera's render count written
/// to values.jsonl; then the Editor exits.
/// </summary>
public class DisplayProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("DisplayProbe").AddComponent<DisplayProbe>();

    static RenderCounter MakeCamera(string name, int display, Color color, float depth, Rect rect)
    {
        var go = new GameObject(name);
        go.transform.position = new Vector3(0f, 0f, -10f);
        var camera = go.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = color;
        camera.depth = depth;
        camera.rect = rect;
        camera.targetDisplay = display;
        return go.AddComponent<RenderCounter>();
    }

    static Canvas MakeCanvas(string name, int display, Color color, Vector2 min, Vector2 max, int order)
    {
        var go = new GameObject(name);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.targetDisplay = display;
        canvas.sortingOrder = order;
        var image = new GameObject("image").AddComponent<Image>();
        image.transform.SetParent(go.transform, false);
        image.color = color;
        var rt = image.rectTransform;
        rt.anchorMin = min;
        rt.anchorMax = max;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return canvas;
    }

    IEnumerator Start()
    {
        var dir = Environment.GetEnvironmentVariable("DISPLAY_PROBE_OUT");
        var red = MakeCamera("red", 0, Color.red, 0f, new Rect(0f, 0f, 1f, 1f));
        var green = MakeCamera("green", 1, Color.green, 1f, new Rect(0.5f, 0f, 0.5f, 1f));
        var blue = MakeCamera("blue", 0, Color.blue, 2f, new Rect(0f, 0.5f, 0.5f, 0.5f));
        MakeCanvas("yellow", 0, new Color(1f, 1f, 0f, 1f), new Vector2(0.05f, 0.05f), new Vector2(0.3f, 0.3f), 0);
        MakeCanvas("magenta", 1, Color.magenta, new Vector2(0.6f, 0.1f), new Vector2(0.9f, 0.4f), 1);
        for (int i = 0; i < 5; i++) yield return null;
        yield return new WaitForEndOfFrame();
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
            var shot = ScreenCapture.CaptureScreenshotAsTexture();
            if (shot != null) File.WriteAllBytes(Path.Combine(dir, "game.png"), shot.EncodeToPNG());
            var sb = new StringBuilder();
            void Value(string k, string v) => sb.Append("{\"k\":\"").Append(k).Append("\",\"v\":").Append(v).Append("}\n");
            // Rendered at all (counts follow the frame rate).
            Value("rendered.red", red.renders > 0 ? "true" : "false");
            Value("rendered.green", green.renders > 0 ? "true" : "false");
            Value("rendered.blue", blue.renders > 0 ? "true" : "false");
            Value("allCamerasCount", Camera.allCamerasCount.ToString(CultureInfo.InvariantCulture));
            Value("displays", Display.displays.Length.ToString(CultureInfo.InvariantCulture));
            Value("screen", "[" + Screen.width + "," + Screen.height + "]");
            File.WriteAllText(Path.Combine(dir, "values.jsonl"), sb.ToString());
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
