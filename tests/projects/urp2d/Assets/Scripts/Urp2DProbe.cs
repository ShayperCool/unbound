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
            var litMaterial = GameObject.Find("Lit Circle").GetComponent<SpriteRenderer>().sharedMaterial;
            var circle = GameObject.Find("Lit Circle").GetComponent<SpriteRenderer>().sprite;
            var square = GameObject.Find("Backdrop").GetComponent<SpriteRenderer>().sprite;
            GameObject extra = null;
            SpriteRenderer Add(Sprite sprite, Vector3 position, Vector3 scale, Color color, int order, Transform parent = null)
            {
                var go = new GameObject("extra", typeof(SpriteRenderer));
                go.transform.SetParent(parent, false);
                go.transform.localPosition = position;
                go.transform.localScale = scale;
                var r = go.GetComponent<SpriteRenderer>();
                r.sprite = sprite;
                r.sharedMaterial = litMaterial;
                r.color = color;
                r.sortingOrder = order;
                return r;
            }
            // A sprite with a normal map and a mask (its secondary textures), lit by a point light
            // that uses normal maps.
            void Bumpy()
            {
                Only("Normal Light");
                extra = new GameObject("extras");
                Add(Resources.Load<Sprite>("Bumpy"), new Vector3(-0.3f, -0.5f, 0f), new Vector3(2.5f, 2.5f, 1f), Color.white, 3, extra.transform);
            }
            // Sprite masks drawn by the 2D renderer: lit sprites inside and outside a circle.
            void Masked()
            {
                Only("Point Light");
                extra = new GameObject("extras");
                var mask = new GameObject("mask", typeof(SpriteMask));
                mask.transform.SetParent(extra.transform, false);
                mask.transform.localPosition = new Vector3(-0.3f, 0f, 0f);
                mask.transform.localScale = new Vector3(2.5f, 2.5f, 1f);
                mask.GetComponent<SpriteMask>().sprite = circle;
                Add(square, new Vector3(-1.5f, 0f, 0f), new Vector3(2.4f, 4f, 1f), new Color(1f, 0.4f, 0.4f), 3, extra.transform).maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
                Add(square, new Vector3(0.9f, 0f, 0f), new Vector3(2.4f, 4f, 1f), new Color(0.4f, 1f, 0.4f), 3, extra.transform).maskInteraction = SpriteMaskInteraction.VisibleOutsideMask;
            }
            void Remove()
            {
                if (extra != null) Destroy(extra);
                extra = null;
            }
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
                ("secondary_textures", Bumpy, Remove),
                ("sprite_mask", Masked, Remove),
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
