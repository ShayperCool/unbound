using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Render resources and low-level graphics as the Editor renders them: sprite masks and mask
/// interaction, tiled sprites, sprite secondary textures, texture mip chains and formats, render
/// texture sub-elements and shapes, custom render textures, command buffer and Graphics calls, line
/// geometry, screen captures. Images go to <c>RESOURCES_PROBE_OUT</c> as PNGs, values to
/// <c>values.jsonl</c> there; then it quits. The parts are in the ResProbe.*.cs files.
/// </summary>
public partial class ResProbe : MonoBehaviour
{
    const int Width = 160, Height = 160;
    static string s_Only;

    /// <summary>The parts in order: an Action runs at once, a Func&lt;IEnumerator&gt; as a coroutine.</summary>
    List<KeyValuePair<string, object>> Parts() => new List<KeyValuePair<string, object>>
    {
        new KeyValuePair<string, object>("mask", (Action)Masks),
        new KeyValuePair<string, object>("maskgroup", (Func<IEnumerator>)MaskGroups),
        new KeyValuePair<string, object>("tiled", (Action)Tiled),
        new KeyValuePair<string, object>("secondary", (Action)SecondaryTextures),
    };
    readonly List<UnityEngine.Object> m_Case = new List<UnityEngine.Object>();
    readonly StringBuilder m_Values = new StringBuilder();
    string m_Dir;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("ResProbe").AddComponent<ResProbe>();

    // ------------------------------------------------------------------ output

    static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);
    static string V(Vector4 v) => "[" + F(v.x) + "," + F(v.y) + "," + F(v.z) + "," + F(v.w) + "]";
    static string V3(Vector3 v) => "[" + F(v.x) + "," + F(v.y) + "," + F(v.z) + "]";
    static string C(Color c) => "[" + F(c.r) + "," + F(c.g) + "," + F(c.b) + "," + F(c.a) + "]";
    static string S(string s) => s == null ? "null" : "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    static string B(bool b) => b ? "true" : "false";
    static string I(int i) => i.ToString(CultureInfo.InvariantCulture);
    static string N(UnityEngine.Object o) => o == null ? "null" : S(o.name);

    static string M(Matrix4x4 m)
    {
        var sb = new StringBuilder("[");
        for (int r = 0; r < 4; r++)
            for (int c = 0; c < 4; c++)
                sb.Append(r + c == 0 ? "" : ",").Append(F(m[r, c]));
        return sb.Append(']').ToString();
    }

    static string Floats(float[] values)
    {
        var sb = new StringBuilder("[");
        for (int i = 0; i < values.Length; i++) sb.Append(i == 0 ? "" : ",").Append(F(values[i]));
        return sb.Append(']').ToString();
    }

    void Value(string key, string json) => m_Values.Append("{\"k\":").Append(S(key)).Append(",\"v\":").Append(json).Append("}\n");

    /// <summary>Records a value; an exception records its type.</summary>
    void Try(string key, Func<string> json)
    {
        try { Value(key, json()); }
        catch (Exception e) { Value(key, S("exception " + e.GetType().Name)); }
    }

    T Add<T>(T o) where T : UnityEngine.Object
    {
        m_Case.Add(o);
        return o;
    }

    void Clear()
    {
        foreach (var o in m_Case) if (o != null) DestroyImmediate(o);
        m_Case.Clear();
    }

    // ------------------------------------------------------------------ scene pieces

    Material Standard(Color color, float smoothness = 0.5f, float metallic = 0f)
    {
        var m = Add(new Material(Shader.Find("Standard")));
        m.color = color;
        m.SetFloat("_Glossiness", smoothness);
        m.SetFloat("_Metallic", metallic);
        return m;
    }

    Material Unlit(Color color, string shader = "Probe/Color")
    {
        var m = Add(new Material(Shader.Find(shader)));
        m.color = color;
        return m;
    }

    GameObject Primitive(PrimitiveType type, Vector3 position, Material material, float scale = 1f)
    {
        var go = Add(GameObject.CreatePrimitive(type));
        go.transform.position = position;
        go.transform.localScale = Vector3.one * scale;
        go.GetComponent<Renderer>().sharedMaterial = material;
        return go;
    }

    /// <summary>A ground plane, two spheres and a cube.</summary>
    GameObject[] Stage(Material material)
    {
        var ground = Primitive(PrimitiveType.Plane, Vector3.zero, material);
        ground.transform.localScale = new Vector3(0.6f, 1f, 0.6f);
        return new[]
        {
            ground,
            Primitive(PrimitiveType.Sphere, new Vector3(-1f, 0.5f, 0f), material),
            Primitive(PrimitiveType.Sphere, new Vector3(1f, 0.5f, 0f), material),
            Primitive(PrimitiveType.Cube, new Vector3(0f, 0.5f, 1f), material),
        };
    }

    /// <summary>The stage in colors: a gray ground, a red and a green sphere, a blue cube.</summary>
    GameObject[] ColorStage()
    {
        var s = Stage(Standard(new Color(0.7f, 0.7f, 0.7f)));
        s[1].GetComponent<Renderer>().sharedMaterial = Standard(new Color(1f, 0.3f, 0.3f));
        s[2].GetComponent<Renderer>().sharedMaterial = Standard(new Color(0.3f, 1f, 0.3f));
        s[3].GetComponent<Renderer>().sharedMaterial = Standard(new Color(0.3f, 0.3f, 1f));
        return s;
    }

    Light Directional(Vector3 euler, Color color, float intensity)
    {
        var go = Add(new GameObject("directional"));
        go.transform.rotation = Quaternion.Euler(euler);
        var l = go.AddComponent<Light>();
        l.type = LightType.Directional;
        l.color = color;
        l.intensity = intensity;
        return l;
    }

    Light Point(Vector3 position, Color color, float range, float intensity)
    {
        var go = Add(new GameObject("point"));
        go.transform.position = position;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = color;
        l.range = range;
        l.intensity = intensity;
        return l;
    }

    RenderTexture Target(int width = Width, int height = Height, RenderTextureFormat format = RenderTextureFormat.ARGB32) =>
        Add(new RenderTexture(width, height, 24, format));

    Camera MakeCamera(RenderTexture target = null)
    {
        var go = Add(new GameObject("camera"));
        go.transform.position = new Vector3(0f, 2.2f, -4.5f);
        go.transform.LookAt(new Vector3(0f, 0.3f, 0.3f));
        var camera = go.AddComponent<Camera>();
        camera.fieldOfView = 45f;
        camera.farClipPlane = 20f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.05f, 0.05f, 0.08f);
        camera.targetTexture = target != null ? target : Target();
        camera.enabled = false;
        return camera;
    }

    static Texture2D Read(RenderTexture rt, TextureFormat format = TextureFormat.RGBA32)
    {
        var active = RenderTexture.active;
        RenderTexture.active = rt;
        var texture = new Texture2D(rt.width, rt.height, format, false);
        texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        texture.Apply();
        RenderTexture.active = active;
        return texture;
    }

    void Save(RenderTexture rt, string name)
    {
        var texture = Read(rt);
        File.WriteAllBytes(Path.Combine(m_Dir, name + ".png"), texture.EncodeToPNG());
        DestroyImmediate(texture);
    }

    void Save(Texture2D texture, string name) => File.WriteAllBytes(Path.Combine(m_Dir, name + ".png"), texture.EncodeToPNG());

    /// <summary>Renders the camera and saves its image.</summary>
    void Capture(Camera camera, string name)
    {
        camera.Render();
        Save(camera.targetTexture, name);
    }

    /// <summary>A pixel of a render texture (as floats).</summary>
    static Color Pixel(RenderTexture rt, int x = 0, int y = 0)
    {
        var t = Read(rt, TextureFormat.RGBAFloat);
        var c = t.GetPixel(x, y);
        DestroyImmediate(t);
        return c;
    }

    Texture2D Checker(int size = 32, int cell = 8)
    {
        var t = Add(new Texture2D(size, size, TextureFormat.RGBA32, false));
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                bool on = ((x / cell) + (y / cell)) % 2 == 0;
                t.SetPixel(x, y, on ? Color.white : new Color(0.2f, 0.2f, 0.2f, 0f));
            }
        t.wrapMode = TextureWrapMode.Repeat;
        t.Apply();
        return t;
    }

    void Environment(Color ambient)
    {
        GL.invertCulling = false;
        GL.wireframe = false;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = ambient;
        RenderSettings.reflectionIntensity = 0f;
        RenderSettings.skybox = null;
        RenderSettings.fog = false;
    }

    // ------------------------------------------------------------------ 2D pieces

    /// <summary>An orthographic camera looking down +z at the origin, 2.5 units half-height (32
    /// pixels per unit on the 160x160 target).</summary>
    Camera Camera2D(Color background, RenderTexture target = null)
    {
        var go = Add(new GameObject("camera2d"));
        go.transform.position = new Vector3(0f, 0f, -10f);
        var camera = go.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 2.5f;
        camera.nearClipPlane = 0.3f;
        camera.farClipPlane = 50f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = background;
        camera.targetTexture = target != null ? target : Target();
        camera.enabled = false;
        return camera;
    }

    static Sprite LoadSprite(string name) => Resources.Load<Sprite>(name);

    SpriteRenderer SpriteAt(string sprite, Vector3 position, Color color, int order = 0, string layer = "Default",
        SpriteMaskInteraction interaction = SpriteMaskInteraction.None, Vector3? scale = null, Transform parent = null)
    {
        var go = Add(new GameObject(sprite));
        if (parent != null) go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        if (scale.HasValue) go.transform.localScale = scale.Value;
        var r = go.AddComponent<SpriteRenderer>();
        r.sprite = LoadSprite(sprite);
        r.color = color;
        r.sortingOrder = order;
        r.sortingLayerName = layer;
        r.maskInteraction = interaction;
        return r;
    }

    SpriteMask MaskAt(string sprite, Vector3 position, int order = 0, string layer = "Default", Vector3? scale = null, Transform parent = null)
    {
        var go = Add(new GameObject("mask " + sprite));
        if (parent != null) go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        if (scale.HasValue) go.transform.localScale = scale.Value;
        var m = go.AddComponent<SpriteMask>();
        m.sprite = LoadSprite(sprite);
        m.sortingOrder = order;
        m.sortingLayerName = layer;
        return m;
    }

    IEnumerator Start()
    {
        m_Dir = System.Environment.GetEnvironmentVariable("RESOURCES_PROBE_OUT");
        s_Only = System.Environment.GetEnvironmentVariable("RESOURCES_PROBE_ONLY");
        if (s_Only == "") s_Only = null;
        if (!string.IsNullOrEmpty(m_Dir))
        {
            Directory.CreateDirectory(m_Dir);
            Environment(new Color(0.1f, 0.1f, 0.1f));
            foreach (var part in Parts())
            {
                if (s_Only != null && !part.Key.StartsWith(s_Only)) continue;
                if (part.Value is Action action) Section(part.Key, action);
                else yield return SectionRoutine(part.Key, ((Func<IEnumerator>)part.Value)());
            }
            File.WriteAllText(Path.Combine(m_Dir, "values.jsonl"), m_Values.ToString());
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    /// <summary>A part of the probe: an exception is recorded and the next part runs.</summary>
    void Section(string name, Action part)
    {
        try { part(); }
        catch (Exception e)
        {
            Value(name + ".exception", S(e.GetType().Name + ": " + e.Message));
            Debug.LogException(e);
        }
        Clear();
        Environment(new Color(0.1f, 0.1f, 0.1f));
    }

    IEnumerator SectionRoutine(string name, IEnumerator part)
    {
        while (true)
        {
            object current;
            try
            {
                if (!part.MoveNext()) break;
                current = part.Current;
            }
            catch (Exception e)
            {
                Value(name + ".exception", S(e.GetType().Name + ": " + e.Message));
                Debug.LogException(e);
                break;
            }
            yield return current;
        }
        Clear();
        Environment(new Color(0.1f, 0.1f, 0.1f));
    }
}
