using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

/// <summary>
/// Sprites with secondary textures packed by a Sprite Atlas V2 (Assets/Atlases/Packed.spriteatlasv2,
/// Sprite Packer mode "Sprite Atlas V2 - Enabled"; Assets/Art): A (16 x 16, white, `_NormalMap`
/// and `_MaskTex`), B (8 x 8, `_NormalMap` only) and C (12 x 12, none). In Play mode it records each
/// sprite's texture (page name without the import hash, size), texture rect and its secondary
/// textures (name, texture, size, format, readability, and texels at the texture rect's centre and
/// corners when readable); then the colour each sprite gets, drawn by a SpriteRenderer, under a
/// dim global light and a point light that uses normal maps (NormalMapQuality.Accurate), and A
/// under a light of the masked blend style 3 (Additive with Mask, mask channel R) (0-255; the
/// Editor on different GPUs rounds them 1 apart).
/// </summary>
[InitializeOnLoad]
public static class SpriteAtlasSecondaryProbe
{
    const string Variable = "SPRITE_ATLAS_SECONDARY_OUT";
    static readonly StringBuilder s_Out = new StringBuilder();
    static readonly HashSet<string> s_Keys = new HashSet<string>();
    static readonly List<string> s_Logs = new List<string>();
    static IEnumerator<object> s_Steps;
    static readonly Regex k_Hash = new Regex("[0-9a-f]{8}");

    static SpriteAtlasSecondaryProbe()
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

    /// <summary>A texture's name with the Editor's import hash (8 hex digits) of a page left out.</summary>
    static string Name(Texture t) => t == null ? "<null>" : k_Hash.Replace(t.name, "#");

    static IEnumerator<object> Steps()
    {
        var names = new[] { "A", "B", "C", "D", "F" };
        var sprites = new Dictionary<string, Sprite>();
        foreach (var n in names)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/Art/{n}.png");
            sprites[n] = sprite;
            Describe(n, sprite);
        }
        foreach (var n in new[] { "G", "H", "I", "J" })
            Describe(n, AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/Art/{n}.png"));
        yield return null;
        foreach (var n in names)
            foreach (var step in Lit("lit." + n, sprites[n], 0)) yield return step;
        foreach (var step in Lit("masked.A", sprites["A"], 3)) yield return step;
    }

    static void Describe(string key, Sprite sprite)
    {
        if (sprite == null)
        {
            Put(key + ".missing", true);
            return;
        }
        var texture = sprite.texture;
        Put(key + ".texture", Name(texture));
        Put(key + ".textureSize", new[] { texture.width, texture.height });
        Put(key + ".textureFormat", texture.graphicsFormat.ToString());
        Put(key + ".textureSrgb", texture.isDataSRGB);
        Put(key + ".packed", sprite.packed);
        var r = sprite.textureRect;
        var rect = new[] { (int)r.x, (int)r.y, (int)r.width, (int)r.height };
        Put(key + ".textureRect", rect);
        int count = sprite.GetSecondaryTextureCount();
        Put(key + ".secondaryCount", count);
        var secondary = new SecondarySpriteTexture[count];
        sprite.GetSecondaryTextures(secondary);
        for (int i = 0; i < count; i++)
        {
            var s = secondary[i];
            string k = key + ".secondary." + s.name;
            var t = s.texture;
            Put(k + ".texture", Name(t));
            if (t == null) continue;
            Put(k + ".size", new[] { t.width, t.height });
            Put(k + ".format", t.graphicsFormat.ToString());
            Put(k + ".srgb", t.isDataSRGB);
            Put(k + ".readable", t.isReadable);
            Put(k + ".same", ReferenceEquals(t, texture));
            // A compressed page's texels are its encoder's (not compared).
            if (!t.isReadable || GraphicsFormatUtility.IsCompressedFormat(t.graphicsFormat)) continue;
            // Texels at the sprite's texture rect: its centre and its four corners (inside).
            var places = new[] { ("centre", rect[2] / 2, rect[3] / 2), ("bottomLeft", 0, 0), ("bottomRight", rect[2] - 1, 0),
                ("topLeft", 0, rect[3] - 1), ("topRight", rect[2] - 1, rect[3] - 1) };
            foreach (var (place, x, y) in places)
            {
                Color32 c = t.GetPixel(rect[0] + x, rect[1] + y);
                Put(k + ".texel." + place, new int[] { c.r, c.g, c.b, c.a });
            }
        }
    }

    static IEnumerable<object> Lit(string key, Sprite sprite, int style)
    {
        var made = new List<GameObject>();
        GameObject Make(string n)
        {
            var go = new GameObject(n);
            made.Add(go);
            return go;
        }
        try
        {
            var global = Make("Global").AddComponent<Light2D>();
            global.lightType = Light2D.LightType.Global;
            global.intensity = 0.2f;
            var point = Make("Point").AddComponent<Light2D>();
            point.lightType = Light2D.LightType.Point;
            point.blendStyleIndex = style;
            point.intensity = 1f;
            point.pointLightOuterRadius = 3f;
            point.pointLightInnerRadius = 0f;
            point.color = new Color(1f, 0.8f, 0.4f);
            point.transform.position = new Vector3(0, 1, 0);
            // The normal map settings have no setters.
            var fields = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(Light2D).GetField("m_NormalMapQuality", fields).SetValue(point, Light2D.NormalMapQuality.Accurate);
            typeof(Light2D).GetField("m_NormalMapDistance", fields).SetValue(point, 0.5f);
            var renderer = Make("Sprite").AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            // Every sprite drawn 4 units square.
            renderer.transform.localScale = Vector3.one * (4f / sprite.bounds.size.x);
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
            foreach (var (x, y) in new[] { (12, 12), (20, 12), (12, 20), (20, 20), (16, 4), (4, 16), (28, 16) })
            {
                Color32 c = read.GetPixel(x, y);
                Put(key + "." + x + "_" + y, new int[] { c.r, c.g, c.b });
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
