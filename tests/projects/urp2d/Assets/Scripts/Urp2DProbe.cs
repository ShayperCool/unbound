using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>Renders the main camera into a 320x180 render texture per scenario (which 2D lights are
/// on) and writes each image to <c>URP2D_PROBE_DIR</c>/&lt;scenario&gt;.png, then quits.</summary>
public class Urp2DProbe : MonoBehaviour
{
    const int Width = 320, Height = 180;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("Urp2DProbe").AddComponent<Urp2DProbe>();

    // Each scenario renders a frame after its setup: Light2D builds its mesh and bounds in
    // LateUpdate. (WaitForEndOfFrame never comes in the Editor's batch mode; a null yield does.)
    IEnumerator Start()
    {
        var dir = Environment.GetEnvironmentVariable("URP2D_PROBE_DIR");
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
            var lights = FindObjectsByType<Light2D>(FindObjectsInactive.Include, FindObjectsSortMode.None).ToDictionary(l => l.name);
            var global = lights["Global Light"];
            void Only(params string[] names)
            {
                foreach (var l in lights.Values) l.enabled = l == global || names.Contains(l.name);
            }
            var point = lights["Point Light"];
            var scenarios = new (string Name, Action Setup, Action Teardown)[]
            {
                ("global", () => { Only(); global.intensity = 1f; }, () => global.intensity = 0.25f),
                ("ambient", () => Only(), () => { }),
                ("point", () => Only("Point Light"), () => { }),
                ("spot", () => Only("Spot Light"), () => { }),
                ("freeform", () => Only("Freeform Light"), () => { }),
                ("sprite_light", () => Only("Sprite Light"), () => { }),
                ("shadows", () => { Only("Point Light"); point.shadowsEnabled = true; point.shadowIntensity = 0.8f; }, () => point.shadowsEnabled = false),
                ("all", () => Only("Point Light", "Spot Light", "Freeform Light", "Sprite Light"), () => { }),
            };
            foreach (var (name, setup, teardown) in scenarios)
            {
                setup();
                yield return null;
                Record(Path.Combine(dir, name + ".png"));
                teardown();
            }
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    /// <summary>Renders the main camera (twice: the first render may still compile) and writes the
    /// image.</summary>
    static void Record(string output)
    {
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
        target.Release();
        File.WriteAllBytes(output, texture.EncodeToPNG());
        Debug.Log("urp2d probe written to " + output);
    }
}
