using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>
/// Records, in a game's OnGUI in Play mode, what EditorGUI.DrawPreviewTexture, DrawTextureAlpha
/// and DrawTextureTransparent draw: each call into its own 32x32 cell of the 320x180 game view
/// (a texture of four coloured quadrants with four alphas, one of distinct mip colours, an
/// alpha-only texture, an imported normal map), the pixels read back at the
/// cells' sample points and the whole view (previews.png); the properties a call sets on the
/// material it is given; the errors and exceptions of a missing image and of calls outside OnGUI.
/// </summary>
public sealed class EditorPreviewTextureProbe : MonoBehaviour
{
    const int Cell = 32;
    StringBuilder m_Output;
    string m_Directory;
    int m_Frames;
    bool m_Done;
    readonly List<string> m_Logs = new List<string>();

    Texture2D m_Quads, m_Mips, m_Alpha8, m_Normal;
    Material m_UserMaterial;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void StartProbe()
    {
        var directory = Environment.GetEnvironmentVariable("EDITOR_PREVIEW_TEXTURE_OUT");
        if (string.IsNullOrEmpty(directory)) return;
        var probe = new GameObject("EditorPreviewTextureProbe").AddComponent<EditorPreviewTextureProbe>();
        probe.m_Directory = directory;
        probe.m_Output = new StringBuilder();
        probe.Setup();
    }

    void Put(string key, params object[] values) =>
        m_Output.Append("{\"k\":").Append(Json(key)).Append(",\"v\":").Append(Json(values)).Append("}\n");

    static object Try(Func<object> read)
    {
        try { return read(); }
        catch (Exception ex) { return "throws " + ex.GetType().Name + ": " + ex.Message; }
    }

    void Setup()
    {
        Application.logMessageReceived += (message, stack, type) => m_Logs.Add(type + ": " + message);

        // Four quadrants (red, green, blue, white) with alphas 1, 0.75, 0.5, 0.25, point sampled.
        m_Quads = new Texture2D(4, 4, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "quads" };
        var quads = new Color32[16];
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
            {
                int q = (y < 2 ? 0 : 2) + (x < 2 ? 0 : 1);
                quads[y * 4 + x] = q switch
                {
                    0 => new Color32(255, 0, 0, 255),
                    1 => new Color32(0, 255, 0, 191),
                    2 => new Color32(0, 0, 255, 128),
                    _ => new Color32(255, 255, 255, 64),
                };
            }
        m_Quads.SetPixels32(quads);
        m_Quads.Apply(false);

        // One colour per mip: 8x8 red, 4x4 green, 2x2 blue, 1x1 yellow.
        m_Mips = new Texture2D(8, 8, TextureFormat.RGBA32, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "mips" };
        var mipColors = new[] { new Color32(255, 0, 0, 255), new Color32(0, 255, 0, 255), new Color32(0, 0, 255, 255), new Color32(255, 255, 0, 255) };
        for (int m = 0; m < m_Mips.mipmapCount; m++)
        {
            int size = Math.Max(1, 8 >> m);
            m_Mips.SetPixels32(Enumerable.Repeat(mipColors[m], size * size).ToArray(), m);
        }
        m_Mips.Apply(false);

        // Alpha only: four alphas left to right.
        m_Alpha8 = new Texture2D(4, 1, TextureFormat.Alpha8, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "alpha8" };
        m_Alpha8.LoadRawTextureData(new byte[] { 255, 191, 128, 64 });
        m_Alpha8.Apply(false);

        m_Normal = Resources.Load<Texture2D>("normal");


        m_UserMaterial = new Material(Shader.Find("Unlit/Transparent")) { name = "user" };

        Put("shaders", new[] { "Hidden/Preview Color2D", "Hidden/Preview Alpha", "Hidden/Preview Transparent", "Hidden/Preview Encoded Normals" }
            .Select(n => (object)new object[] { n, Shader.Find(n) != null }).ToArray());
        Put("normal", m_Normal != null ? new object[] { m_Normal.format.ToString(), m_Normal.width, m_Normal.height } : new object[] { "missing" });

        // Outside OnGUI there is no event.
        var r = new Rect(0, 0, Cell, Cell);
        Put("outside",
            Try(() => { EditorGUI.DrawPreviewTexture(r, m_Quads); return "ok"; }),
            Try(() => { EditorGUI.DrawTextureAlpha(r, m_Quads); return "ok"; }),
            Try(() => { EditorGUI.DrawTextureTransparent(r, m_Quads); return "ok"; }));
        Put("outside/logs", m_Logs.ToArray());
        m_Logs.Clear();
    }

    static Rect At(int column, int row) => new Rect(column * Cell, row * Cell, Cell, Cell);

    /// <summary>The calls, in cells of the game view; the same in every event.</summary>
    void Draw()
    {
        // An opaque backdrop: the game view is not cleared under the GUI, so a draw that blends
        // would otherwise build up from frame to frame.
        if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(new Rect(0, 0, 9 * Cell, 3 * Cell), Color.black);
        EditorGUI.DrawPreviewTexture(At(0, 0), m_Quads);
        EditorGUI.DrawPreviewTexture(At(1, 0), m_Mips, null, ScaleMode.StretchToFill, 0, 1);
        EditorGUI.DrawPreviewTexture(At(2, 0), m_Mips, null, ScaleMode.StretchToFill, 0, 2);
        EditorGUI.DrawPreviewTexture(At(3, 0), m_Mips, null, ScaleMode.StretchToFill, 0, 0.5f);
        EditorGUI.DrawPreviewTexture(At(4, 0), m_Quads, null, ScaleMode.StretchToFill, 0, -1, ColorWriteMask.Red);
        EditorGUI.DrawPreviewTexture(At(5, 0), m_Quads, null, ScaleMode.StretchToFill, 0, -1, ColorWriteMask.Red | ColorWriteMask.Green | ColorWriteMask.Blue);
        EditorGUI.DrawPreviewTexture(At(6, 0), m_Quads, null, ScaleMode.StretchToFill, 0, -1, ColorWriteMask.All, 1);
        EditorGUI.DrawPreviewTexture(At(7, 0), m_Quads, null, ScaleMode.ScaleToFit, 2);
        EditorGUI.DrawPreviewTexture(At(8, 0), m_Quads, null, ScaleMode.ScaleAndCrop, 0.5f);

        EditorGUI.DrawTextureAlpha(At(0, 1), m_Quads);
        EditorGUI.DrawTextureAlpha(At(1, 1), m_Mips, ScaleMode.StretchToFill, 0, 2);
        EditorGUI.DrawTextureTransparent(At(2, 1), m_Quads);
        EditorGUI.DrawTextureTransparent(At(3, 1), m_Quads, ScaleMode.ScaleAndCrop, 0.5f);
        EditorGUI.DrawTextureTransparent(At(4, 1), m_Quads, ScaleMode.StretchToFill, 0, -1, ColorWriteMask.Alpha);
        EditorGUI.DrawTextureTransparent(At(5, 1), m_Quads, ScaleMode.StretchToFill, 0, -1, ColorWriteMask.All, 1);
        EditorGUI.DrawPreviewTexture(At(6, 1), m_Alpha8);
        if (m_Normal != null) EditorGUI.DrawPreviewTexture(At(7, 1), m_Normal);
        var color = GUI.color;
        GUI.color = new Color(1, 1, 1, 0.5f);
        EditorGUI.DrawPreviewTexture(At(8, 1), m_Quads);
        GUI.color = color;

        // A material of the caller's: drawn with it, its properties set.
        EditorGUI.DrawPreviewTexture(At(0, 2), m_Quads, m_UserMaterial, ScaleMode.StretchToFill, 0, 3, ColorWriteMask.Green, 2);
    }

    /// <summary>Sample points of every cell: the middles of its four quadrants.</summary>
    void RecordPixels(Texture2D view)
    {
        for (int row = 0; row < 3; row++)
            for (int column = 0; column < (row < 2 ? 9 : 1); column++)
            {
                var samples = new List<object>();
                foreach (var (x, y) in new[] { (8, 8), (24, 8), (8, 24), (24, 24) })
                {
                    // From the top of the view; ReadPixels rows start at the bottom.
                    Color32 c = view.GetPixel(column * Cell + x, view.height - 1 - (row * Cell + y));
                    samples.Add(new object[] { c.r, c.g, c.b, c.a });
                }
                Put($"pixels/{column},{row}", samples.ToArray());
            }
    }

    void OnGUI()
    {
        var type = Event.current.type;
        if (m_Done || (type != EventType.Layout && type != EventType.Repaint)) return;
        if (type == EventType.Layout) { Draw(); return; }
        Draw();
        if (++m_Frames == 10)
        {
            m_Done = true;
            Put("material", m_UserMaterial.GetColor("_ColorMask").ToString("F3"), m_UserMaterial.GetFloat("_Mip"),
                m_UserMaterial.GetFloat("_Exposure"), m_UserMaterial.GetInt("_SNormFormat"));
            Put("missing", Try(() => { EditorGUI.DrawTextureTransparent(At(9, 4), null); return "ok"; }));
            Put("drawn/logs", m_Logs.ToArray());
            StartCoroutine(Finish());
        }
    }

    IEnumerator Finish()
    {
        yield return new WaitForEndOfFrame();
        var view = new Texture2D(Screen.width, Screen.height, TextureFormat.RGBA32, false);
        view.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
        view.Apply();
        Put("screen", Screen.width, Screen.height);
        RecordPixels(view);
        Directory.CreateDirectory(m_Directory);
        File.WriteAllBytes(Path.Combine(m_Directory, "previews.png"), view.EncodeToPNG());
        m_Output.Append("{\"k\":\"complete\",\"v\":true}\n");
        File.WriteAllText(Path.Combine(m_Directory, "values.jsonl"), m_Output.ToString());
        EditorApplication.Exit(0);
    }

    static string Json(object value) => value switch
    {
        null => "null",
        string text => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r") + "\"",
        bool flag => flag ? "true" : "false",
        float f => f.ToString("R", CultureInfo.InvariantCulture),
        IEnumerable items => "[" + string.Join(",", items.Cast<object>().Select(Json)) + "]",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture),
    };
}
