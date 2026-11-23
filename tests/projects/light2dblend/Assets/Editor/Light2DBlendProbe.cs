using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

/// <summary>
/// What 2D point lights add to a Sprite-Lit square under a dim global light (blend style 0) with
/// the 2D renderer's four light blend styles (Assets/Settings/Probe2DRenderer.asset: 0 Multiply,
/// mask channel R; 1 Additive, no mask; 2 Multiply with Mask, R; 3 Additive with Mask, R). The
/// light pass draws into one light texture per blend style active in the frame, and a light
/// writes the texture of its style's place among the active styles. Cases: a point light of each
/// style on a white sprite without a mask texture and on one whose mask texture (secondary texture
/// _MaskTex) is black, the point light alone (its style the only active one), the global light
/// alone, and on a grey sprite point lights of several styles at once at separate places (styles
/// 1 and 3: style 3's light writes the third texture; styles 1, 2 and 3: the fourth). Each case's
/// frames draw its camera into a render texture; the colour at each light's centre (and for the
/// single lights halfway to its edge) is recorded (0-255; the Editor on different GPUs rounds them
/// 1 apart). Recorded in Play mode.
/// </summary>
[InitializeOnLoad]
public static class Light2DBlendProbe
{
    const string Variable = "LIGHT_2D_BLEND_OUT";
    static readonly StringBuilder s_Out = new StringBuilder();
    static readonly HashSet<string> s_Keys = new HashSet<string>();
    static readonly List<string> s_Logs = new List<string>();
    static IEnumerator<object> s_Steps;

    static Light2DBlendProbe()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            Application.logMessageReceived += (message, stack, type) => { if (type != LogType.Log) s_Logs.Add(type + ": " + message.Split('\n')[0]); };
            EditorApplication.delayCall += () =>
            {
                s_Steps = Steps();
                EditorApplication.update += Tick;
            };
        };
    }

    public static void Record() => EditorApplication.EnterPlaymode();

    static void Tick()
    {
        bool more;
        try { more = s_Steps.MoveNext(); }
        catch (Exception e) { Put("run.threw", e.GetType().FullName + ": " + e.Message.Split('\n')[0]); more = false; }
        if (more) return;
        EditorApplication.update -= Tick;
        Put("logs", string.Join(" | ", s_Logs));
        Put("complete", true);
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable(Variable), "values.jsonl"), s_Out.ToString());
        EditorApplication.Exit(0);
    }

    static Texture2D Solid(Color32 colour)
    {
        var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        var pixels = new Color32[16];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = colour;
        texture.SetPixels32(pixels);
        texture.Apply();
        return texture;
    }

    static IEnumerator<object> Steps()
    {
        var white = Solid(new Color32(255, 255, 255, 255));
        var black = Solid(new Color32(0, 0, 0, 255));
        var grey = Solid(new Color32(128, 128, 128, 255));
        var plain = Sprite.Create(white, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 1);
        var masked = Sprite.Create(white, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 1, 0, SpriteMeshType.FullRect,
            Vector4.zero, false, new[] { new SecondarySpriteTexture { name = "_MaskTex", texture = black } });
        var greySprite = Sprite.Create(grey, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 1);
        // The global light alone.
        foreach (var step in Case("globalOnly", plain, new int[0])) yield return step;
        for (int style = 0; style < 4; style++)
        {
            foreach (var step in Case("plain.style" + style, plain, new[] { style })) yield return step;
            foreach (var step in Case("blackMask.style" + style, masked, new[] { style })) yield return step;
            // The point light alone (no global light): its blend style is the only one drawn.
            foreach (var step in Case("alone.style" + style, plain, new[] { style }, false)) yield return step;
        }
        // Several styles active at once: each light writes its style's place among them.
        foreach (var step in Case("grey.styles13", greySprite, new[] { 1, 3 })) yield return step;
        foreach (var step in Case("grey.styles123", greySprite, new[] { 1, 2, 3 })) yield return step;
    }

    // Where the point lights of a case with several are (world units): apart by more than twice
    // their radius.
    static readonly Vector2[] k_Places = { new Vector2(-1, 1), new Vector2(1, 1), new Vector2(0, -1) };

    static IEnumerable<object> Case(string key, Sprite sprite, int[] styles, bool withGlobal = true)
    {
        var made = new List<GameObject>();
        GameObject Make(string n)
        {
            var go = new GameObject(n);
            made.Add(go);
            return go;
        }
        bool single = styles.Length == 1;
        try
        {
            if (withGlobal)
            {
                var global = Make("Global").AddComponent<Light2D>();
                global.lightType = Light2D.LightType.Global;
                global.intensity = 0.2f;
            }
            for (int i = 0; i < styles.Length; i++)
            {
                var point = Make("Point" + i).AddComponent<Light2D>();
                point.lightType = Light2D.LightType.Point;
                point.blendStyleIndex = styles[i];
                point.intensity = 1f;
                point.pointLightOuterRadius = single ? 2f : 0.75f;
                point.pointLightInnerRadius = 0f;
                point.color = new Color(1f, 0.8f, 0.4f);
                if (!single) point.transform.position = k_Places[i];
            }
            var renderer = Make("Square").AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.transform.localScale = new Vector3(4, 4, 1);
            var camera = Make("Camera").AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 2f;
            camera.transform.position = new Vector3(0, 0, -10);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.targetTexture = new RenderTexture(32, 32, 24, RenderTextureFormat.ARGB32);
        }
        catch (Exception e)
        {
            Put(key + ".threw", e.GetType().Name + ": " + e.Message.Split('\n')[0]);
        }
        yield return null;
        yield return null;
        yield return null;
        var cameraObject = made.Find(g => g.name == "Camera");
        if (cameraObject != null)
        {
            var camera = cameraObject.GetComponent<Camera>();
            var target = camera.targetTexture;
            RenderTexture.active = target;
            var read = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            read.ReadPixels(new Rect(0, 0, 32, 32), 0, 0);
            read.Apply();
            RenderTexture.active = null;
            if (styles.Length <= 1)
            {
                Put(key + ".centre", Rgb(read.GetPixel(16, 16)));
                Put(key + ".half", Rgb(read.GetPixel(24, 16)));
            }
            else
            {
                // 8 pixels a unit, the camera's centre at pixel 16.
                for (int i = 0; i < styles.Length; i++)
                    Put(key + ".style" + styles[i], Rgb(read.GetPixel(16 + (int)k_Places[i].x * 8, 16 + (int)k_Places[i].y * 8)));
            }
            Object.DestroyImmediate(read);
            camera.targetTexture = null;
            target.Release();
            Object.DestroyImmediate(target);
        }
        foreach (var go in made) Object.DestroyImmediate(go);
        Put(key + ".logs", string.Join(" | ", s_Logs));
        s_Logs.Clear();
        yield return null;
    }

    static int[] Rgb(Color c)
    {
        Color32 b = c;
        return new int[] { b.r, b.g, b.b };
    }

    static void Put(string key, object value)
    {
        if (!s_Keys.Add(key)) key = key + "#" + s_Keys.Count;
        string json = value switch
        {
            bool b => b ? "true" : "false",
            int i => i.ToString(CultureInfo.InvariantCulture),
            int[] a => "[" + string.Join(",", Array.ConvertAll(a, x => x.ToString(CultureInfo.InvariantCulture))) + "]",
            _ => "\"" + Escape(value.ToString()) + "\"",
        };
        s_Out.Append("{\"k\":\"").Append(Escape(key)).Append("\",\"v\":").Append(json).Append("}\n");
    }

    static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
}
