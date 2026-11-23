using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

/// <summary>
/// Camera.Render() with the URP 2D renderer into a camera's target texture: what the texture
/// holds at its centre afterwards (as a colour category: the sprite's red, the clear colour's
/// blue, black, or other), which render texture is active after the call (none, the target, or
/// one made active before it), and whether the camera keeps its target. Cases: rendered at once
/// after the objects are made, rendered a frame later, rendered with another texture active, and
/// a clear colour alone, an enabled camera whose global light is made in the same step, and the
/// built-in pipeline (the render pipeline asset taken away) at once and with another texture
/// active. Recorded in Play mode.
/// </summary>
[InitializeOnLoad]
public static class CameraRender2DProbe
{
    const string Variable = "CAMERA_RENDER_2D_OUT";
    static readonly StringBuilder s_Out = new StringBuilder();
    static readonly HashSet<string> s_Keys = new HashSet<string>();
    static readonly List<string> s_Logs = new List<string>();
    static IEnumerator<object> s_Steps;
    static Sprite s_Square;

    static CameraRender2DProbe()
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

    static IEnumerator<object> Steps()
    {
        var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        var white = new Color32[16];
        for (int i = 0; i < white.Length; i++) white[i] = new Color32(255, 255, 255, 255);
        texture.SetPixels32(white);
        texture.Apply();
        s_Square = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4);
        var light = new GameObject("Light").AddComponent<Light2D>();
        light.lightType = Light2D.LightType.Global;

        // At once after the objects are made.
        {
            var (camera, made) = Build(true);
            RenderTexture.active = null;
            Render("immediate", camera);
            Clean(camera, made);
        }
        yield return null;

        // A frame after the objects are made.
        {
            var (camera, made) = Build(true);
            yield return null;
            RenderTexture.active = null;
            Render("afterFrame", camera);
            Clean(camera, made);
        }
        yield return null;

        // With another texture active before the call.
        {
            var (camera, made) = Build(true);
            yield return null;
            var other = new RenderTexture(8, 8, 0, RenderTextureFormat.ARGB32) { name = "Other" };
            other.Create();
            RenderTexture.active = other;
            Render("otherActive", camera);
            RenderTexture.active = null;
            other.Release();
            Object.DestroyImmediate(other);
            Clean(camera, made);
        }
        yield return null;

        // The clear colour alone (no sprite), at once.
        {
            var (camera, made) = Build(false);
            RenderTexture.active = null;
            Render("clearOnly", camera);
            Clean(camera, made);
        }
        yield return null;
        Object.DestroyImmediate(light.gameObject);
        yield return null;

        // An enabled camera, its global light made in the same step as the render.
        {
            var fresh = new GameObject("Fresh Light").AddComponent<Light2D>();
            fresh.lightType = Light2D.LightType.Global;
            var (camera, made) = Build(true);
            made.Add(fresh.gameObject);
            camera.enabled = true;
            RenderTexture.active = null;
            Render("freshLight", camera);
            Clean(camera, made);
        }
        yield return null;

        // The built-in pipeline (no render pipeline asset): at once, and with another texture active.
        var pipeline = GraphicsSettings.defaultRenderPipeline;
        var quality = QualitySettings.renderPipeline;
        GraphicsSettings.defaultRenderPipeline = null;
        QualitySettings.renderPipeline = null;
        try
        {
            {
                var (camera, made) = Build(true);
                RenderTexture.active = null;
                Render("builtin.immediate", camera);
                Clean(camera, made);
            }
            {
                var (camera, made) = Build(true);
                var other = new RenderTexture(8, 8, 0, RenderTextureFormat.ARGB32) { name = "Other" };
                other.Create();
                RenderTexture.active = other;
                Render("builtin.otherActive", camera);
                RenderTexture.active = null;
                other.Release();
                Object.DestroyImmediate(other);
                Clean(camera, made);
            }
        }
        finally
        {
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = quality;
        }
    }

    static (Camera, List<GameObject>) Build(bool sprite)
    {
        var made = new List<GameObject>();
        if (sprite)
        {
            var renderer = new GameObject("Sprite").AddComponent<SpriteRenderer>();
            renderer.sprite = s_Square;
            renderer.color = Color.red;
            made.Add(renderer.gameObject);
        }
        var camera = new GameObject("Camera").AddComponent<Camera>();
        made.Add(camera.gameObject);
        camera.orthographic = true;
        camera.orthographicSize = 0.4f;
        camera.transform.position = new Vector3(0, 0, -10);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.blue;
        // A camera that only renders when asked.
        camera.enabled = false;
        camera.targetTexture = new RenderTexture(16, 16, 24, RenderTextureFormat.ARGB32) { name = "Target" };
        return (camera, made);
    }

    static void Render(string key, Camera camera)
    {
        var target = camera.targetTexture;
        var before = RenderTexture.active;
        camera.Render();
        var after = RenderTexture.active;
        Put(key + ".activeAfter", after == null ? "none" : after == target ? "target" : after == before ? "before" : "other:" + after.name);
        Put(key + ".targetKept", camera.targetTexture == target);
        RenderTexture.active = target;
        var read = new Texture2D(16, 16, TextureFormat.RGBA32, false);
        read.ReadPixels(new Rect(0, 0, 16, 16), 0, 0);
        read.Apply();
        RenderTexture.active = after;
        Put(key + ".centre", Category(read.GetPixel(8, 8)));
        Put(key + ".corner", Category(read.GetPixel(0, 0)));
        Object.DestroyImmediate(read);
        Put(key + ".logs", string.Join(" | ", s_Logs));
        s_Logs.Clear();
    }

    static string Category(Color c) =>
        c.r > 0.25f && c.g < 0.1f && c.b < 0.1f ? "red"
        : c.b > 0.25f && c.r < 0.1f && c.g < 0.1f ? "blue"
        : c.r < 0.02f && c.g < 0.02f && c.b < 0.02f ? "black"
        : "other";

    static void Clean(Camera camera, List<GameObject> made)
    {
        var target = camera.targetTexture;
        if (RenderTexture.active == target) RenderTexture.active = null;
        camera.targetTexture = null;
        target.Release();
        Object.DestroyImmediate(target);
        foreach (var go in made) Object.DestroyImmediate(go);
    }

    static void Put(string key, object value)
    {
        if (!s_Keys.Add(key)) key = key + "#" + s_Keys.Count;
        string json = value switch
        {
            bool b => b ? "true" : "false",
            int i => i.ToString(CultureInfo.InvariantCulture),
            _ => "\"" + Escape(value.ToString()) + "\"",
        };
        s_Out.Append("{\"k\":\"").Append(Escape(key)).Append("\",\"v\":").Append(json).Append("}\n");
    }

    static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
}
