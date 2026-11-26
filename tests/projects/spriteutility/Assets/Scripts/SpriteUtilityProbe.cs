using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

/// <summary>
/// Writes values.jsonl in $SPRITEUTILITY_OUT (tests/value_probe.py rows): the Editor's sprite
/// utilities the 2D packages call. UnityEditor.Sprites.SpriteUtility.GeneratePolygonOutlineVerticesOfSize
/// (internal, called by reflection as the Sprite Editor's polygon tool reaches it) for several side
/// counts and sizes, and GetSpriteTexture / GetSpriteUVs of sprites made by script (whole texture,
/// a rectangle of it, a tight mesh, packed data asked for and not). Floats are written with "R"; an
/// exception is written as "throws" with its type and message.
/// </summary>
public class SpriteUtilityProbe : MonoBehaviour
{
    const string Variable = "SPRITEUTILITY_OUT";
    static StreamWriter output;

    static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

    static void Row(string key, string value) => output.Write("{\"k\":\"" + key + "\",\"v\":\"" + Escape(value) + "\"}\n");

    static void Try(string key, Func<string> action)
    {
        string result;
        try { result = action(); }
        catch (TargetInvocationException e) { result = "throws " + e.InnerException.GetType().FullName + ": " + e.InnerException.Message; }
        catch (Exception e) { result = "throws " + e.GetType().FullName + ": " + e.Message; }
        Row(key, result);
    }

    static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);

    static string Points(Vector2[] points)
    {
        if (points == null) return "null";
        var parts = new string[points.Length];
        for (int i = 0; i < points.Length; i++) parts[i] = F(points[i].x) + "," + F(points[i].y);
        return points.Length + ":" + string.Join(";", parts);
    }

    static void Number(string key, float value) => output.Write("{\"k\":\"" + key + "\",\"v\":" + F(value) + "}\n");

    /// <summary>The outline's corner count, then each corner's x and y as numbers (the sine and
    /// cosine of the platform's maths library may differ in their last bit).</summary>
    static void Outline(MethodInfo outline, int sides, int w, int h)
    {
        string key = "outline." + sides + "." + w + "x" + h;
        Vector2[] points;
        try { points = (Vector2[])outline.Invoke(null, new object[] { sides, w, h }); }
        catch (TargetInvocationException e) { Row(key, "throws " + e.InnerException.GetType().FullName); return; }
        Row(key, points == null ? "null" : points.Length.ToString(CultureInfo.InvariantCulture));
        if (points == null) return;
        for (int i = 0; i < points.Length; i++)
        {
            Number(key + "." + i + ".x", points[i].x);
            Number(key + "." + i + ".y", points[i].y);
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
        new GameObject("SpriteUtilityProbe").AddComponent<SpriteUtilityProbe>();
    }

    void Start()
    {
        var dir = Environment.GetEnvironmentVariable(Variable);
        Directory.CreateDirectory(dir);
        output = new StreamWriter(Path.Combine(dir, "values.jsonl"), false, new UTF8Encoding(false));
        try { Run(); }
        catch (Exception e) { Row("aborted", e.GetType().FullName + ": " + e.Message); }
        output.Write("{\"k\":\"complete\",\"v\":true}\n");
        output.Dispose();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#endif
    }

    static void Run()
    {
#if UNITY_EDITOR
        var outline = typeof(UnityEditor.Sprites.SpriteUtility).GetMethod("GeneratePolygonOutlineVerticesOfSize", BindingFlags.NonPublic | BindingFlags.Static);
        Row("outline.found", (outline != null).ToString());
        foreach (int sides in new[] { 0, 1, 2, 3, 4, 5, 6, 8, 12, 33, 128 })
            foreach (var (w, h) in new[] { (64, 64), (100, 50), (7, 13), (1, 1), (0, 10) })
                Outline(outline, sides, w, h);
        // A negative side count crashes the Editor: not recorded.
        Outline(outline, 4, -8, 16);

        var texture = new Texture2D(32, 16) { name = "Probe" };
        var pixels = new Color32[32 * 16];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = (i % 32) < 20 && (i / 32) > 3 ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
        texture.SetPixels32(pixels);
        texture.Apply();
        var sprites = new (string, Sprite)[]
        {
            ("whole", Sprite.Create(texture, new Rect(0, 0, 32, 16), new Vector2(0.5f, 0.5f), 16, 0, SpriteMeshType.FullRect)),
            ("part", Sprite.Create(texture, new Rect(4, 2, 12, 8), new Vector2(0f, 0f), 8, 0, SpriteMeshType.FullRect)),
            ("tight", Sprite.Create(texture, new Rect(0, 0, 32, 16), new Vector2(0.5f, 0.5f), 16, 0, SpriteMeshType.Tight)),
        };
        foreach (var (name, sprite) in sprites)
        {
            foreach (bool atlas in new[] { false, true })
            {
                Try("texture." + name + "." + atlas, () => { var t = UnityEditor.Sprites.SpriteUtility.GetSpriteTexture(sprite, atlas); return t == null ? "null" : t.name + ":" + t.width + "x" + t.height; });
                Try("uvs." + name + "." + atlas, () => Points(UnityEditor.Sprites.SpriteUtility.GetSpriteUVs(sprite, atlas)));
            }
            Try("uv." + name, () => Points(sprite.uv));
        }
        Try("texture.null", () => { var t = UnityEditor.Sprites.SpriteUtility.GetSpriteTexture(null, false); return t == null ? "null" : t.name; });
        Try("uvs.null", () => Points(UnityEditor.Sprites.SpriteUtility.GetSpriteUVs(null, false)));
#endif
    }
}
