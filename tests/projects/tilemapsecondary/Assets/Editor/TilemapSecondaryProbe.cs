using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

/// <summary>
/// The colour a white Sprite-Lit square gets from a 2D point light that uses normal maps
/// (NormalMapQuality.Accurate), drawn by a SpriteRenderer and by a Tilemap whose one tile shows the
/// same sprite: without a normal map, and with the secondary texture _NormalMap flat, tilted
/// towards -y and tilted towards +x (Assets/Normals); the +x one also on a cell rotated 90 degrees
/// and on a cell mirrored along x (and the sprite turned and scaled alike); and with the secondary texture _MaskTex half red under a light of
/// the masked blend style 3 (Additive with Mask, mask channel R). A dim global light lights them
/// all. Each case's frames draw its camera into a render texture; the colours at five places are
/// recorded (0-255; the Editor on different GPUs rounds them 1 apart). Recorded in Play mode.
/// </summary>
[InitializeOnLoad]
public static class TilemapSecondaryProbe
{
    const string Variable = "TILEMAP_SECONDARY_OUT";
    static readonly StringBuilder s_Out = new StringBuilder();
    static readonly HashSet<string> s_Keys = new HashSet<string>();
    static readonly List<string> s_Logs = new List<string>();
    static IEnumerator<object> s_Steps;

    static TilemapSecondaryProbe()
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

    static Sprite Square(Texture2D white, string secondary, string path)
    {
        if (secondary == null) return Sprite.Create(white, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 1);
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        Put("loaded." + Path.GetFileNameWithoutExtension(path), texture != null);
        return Sprite.Create(white, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 1, 0, SpriteMeshType.FullRect,
            Vector4.zero, false, new[] { new SecondarySpriteTexture { name = secondary, texture = texture } });
    }

    static IEnumerator<object> Steps()
    {
        var white = new Texture2D(4, 4, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        var pixels = new Color32[16];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
        white.SetPixels32(pixels);
        white.Apply();
        var sprites = new List<(string Name, Sprite Sprite)>
        {
            ("none", Square(white, null, null)),
            ("flat", Square(white, "_NormalMap", "Assets/Normals/Flat.png")),
            ("tiltDown", Square(white, "_NormalMap", "Assets/Normals/TiltDown.png")),
            ("tiltRight", Square(white, "_NormalMap", "Assets/Normals/TiltRight.png")),
        };
        foreach (var (name, sprite) in sprites)
        {
            foreach (var step in Case("sprite." + name, sprite, false, 0f, 0)) yield return step;
            foreach (var step in Case("tile." + name, sprite, true, 0f, 0)) yield return step;
        }
        var tiltRight = sprites[3].Sprite;
        foreach (var step in Case("sprite.tiltRight.rot90", tiltRight, false, 90f, 0)) yield return step;
        foreach (var step in Case("tile.tiltRight.rot90", tiltRight, true, 90f, 0)) yield return step;
        foreach (var step in Case("sprite.tiltRight.flipX", tiltRight, false, 0f, 0, true)) yield return step;
        foreach (var step in Case("tile.tiltRight.flipX", tiltRight, true, 0f, 0, true)) yield return step;
        var mask = Square(white, "_MaskTex", "Assets/Normals/HalfMask.png");
        foreach (var step in Case("sprite.mask", mask, false, 0f, 3)) yield return step;
        foreach (var step in Case("tile.mask", mask, true, 0f, 3)) yield return step;
    }

    static readonly (int X, int Y)[] k_Places = { (16, 16), (16, 24), (16, 8), (8, 16), (24, 16) };

    static IEnumerable<object> Case(string key, Sprite sprite, bool tilemap, float rotation, int style, bool flipX = false)
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
            if (tilemap)
            {
                // One 1-unit cell whose center is the origin; the tile's sprite is 4 units square.
                var grid = Make("Grid").AddComponent<Grid>();
                grid.transform.position = new Vector3(-0.5f, -0.5f, 0);
                var map = Make("Tilemap");
                map.transform.SetParent(grid.transform, false);
                var tiles = map.AddComponent<Tilemap>();
                map.AddComponent<TilemapRenderer>();
                var tile = ScriptableObject.CreateInstance<Tile>();
                tile.sprite = sprite;
                tile.flags = TileFlags.None;
                tiles.SetTile(Vector3Int.zero, tile);
                if (rotation != 0 || flipX)
                    tiles.SetTransformMatrix(Vector3Int.zero, Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0, 0, rotation), new Vector3(flipX ? -1 : 1, 1, 1)));
            }
            else
            {
                var renderer = Make("Square").AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.transform.rotation = Quaternion.Euler(0, 0, rotation);
                if (flipX) renderer.transform.localScale = new Vector3(-1, 1, 1);
            }
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
            foreach (var (x, y) in k_Places) Put(key + "." + x + "_" + y, Rgb(read.GetPixel(x, y)));
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
