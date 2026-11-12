using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// Measures legacy TextMesh output in Play mode: for each case one new TextMesh, the hidden mesh it
/// generates (found as the one new Mesh object), its renderer's bounds and materials and the logs
/// it caused; then two rendered images. Writes values.jsonl (and PNGs) to $TEXTMESH_PROBE_OUT.
/// </summary>
public sealed class TextMeshProbe : MonoBehaviour
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    readonly StringBuilder m_Out = new StringBuilder();
    readonly List<string> m_Logs = new List<string>();
    string m_Dir;
    Font m_Legacy;
    Font m_Custom;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var dir = Environment.GetEnvironmentVariable("TEXTMESH_PROBE_OUT");
        if (string.IsNullOrEmpty(dir)) return;
        var probe = new GameObject("TextMesh probe").AddComponent<TextMeshProbe>();
        probe.m_Dir = dir;
    }

    static string F(float v) => v.ToString("R", Inv);

    void Put(string key, string json) => m_Out.Append("{\"k\":").Append(Str(key)).Append(",\"v\":").Append(json).Append("}\n");

    static string Str(string s)
    {
        var b = new StringBuilder("\"");
        foreach (var c in s ?? "")
        {
            if (c == '"' || c == '\\') b.Append('\\').Append(c);
            else if (c < 0x20 || c > 0x7e) b.Append("\\u").Append(((int)c).ToString("x4"));
            else b.Append(c);
        }
        return b.Append('"').ToString();
    }

    static string Floats(IEnumerable<float> values) => "[" + string.Join(",", values.Select(F)) + "]";

    static string Vec(Vector3 v) => Floats(new[] { v.x, v.y, v.z });

    void OnLog(string message, string stack, LogType type)
    {
        if (type != LogType.Log) m_Logs.Add(type + ": " + message);
    }

    IEnumerator Start()
    {
        Application.logMessageReceived += OnLog;
        m_Legacy = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        m_Custom = CustomFont();
        Put("font/legacy/name", Str(m_Legacy.name));
        Put("font/legacy/dynamic", m_Legacy.dynamic ? "true" : "false");
        Put("font/legacy/fontSize", m_Legacy.fontSize.ToString(Inv));
        Put("font/legacy/lineHeight", m_Legacy.lineHeight.ToString(Inv));
        Put("font/legacy/ascent", m_Legacy.ascent.ToString(Inv));
        Put("font/legacy/material", Str(m_Legacy.material ? m_Legacy.material.name + "|" + m_Legacy.material.shader.name : "null"));

        // The custom font's own properties, and what GetCharacterInfo answers for glyphs the
        // TextMesh cases below lay out (compared with their quads).
        Put("font/custom/dynamic", m_Custom.dynamic ? "true" : "false");
        Put("font/custom/fontSize", m_Custom.fontSize.ToString(Inv));
        Put("font/custom/lineHeight", m_Custom.lineHeight.ToString(Inv));
        Put("font/custom/ascent", m_Custom.ascent.ToString(Inv));
        m_Legacy.RequestCharactersInTexture("Ab \u00e9W", 0, FontStyle.Normal);
        foreach (var c in "Ab \u00e9W")
        {
            if (!m_Legacy.GetCharacterInfo(c, out var info, 0, FontStyle.Normal)) { Put("characterInfo/" + (int)c, Str("none")); continue; }
            var tex = m_Legacy.material.mainTexture;
            Put("characterInfo/" + (int)c, $"[{info.minX},{info.maxX},{info.minY},{info.maxY},{info.advance},{info.glyphWidth},{info.glyphHeight},{info.bearing},{info.size},{F(Mathf.Round(Mathf.Abs(info.uvTopRight.x - info.uvTopLeft.x) * tex.width))},{F(Mathf.Round(Mathf.Abs(info.uvTopLeft.y - info.uvBottomLeft.y) * tex.height))}]");
        }
        const string multi = "Ab\ncdefg\nhi";
        Case("default", tm => { });
        Case("basic", tm => tm.text = "Ab");
        Case("words", tm => tm.text = "Hello World");
        Case("explicitFont", tm => { tm.font = m_Legacy; tm.text = "Ab"; });
        Case("size13", tm => { tm.fontSize = 13; tm.text = "Ab"; });
        Case("size32", tm => { tm.fontSize = 32; tm.text = "Ab"; });
        Case("charSize05", tm => { tm.characterSize = 0.5f; tm.text = "Ab"; });
        Case("charSize2", tm => { tm.characterSize = 2f; tm.text = "Ab"; });
        Case("multi", tm => tm.text = multi);
        Case("lineSpacing15", tm => { tm.lineSpacing = 1.5f; tm.text = multi; });
        Case("lineSpacing05", tm => { tm.lineSpacing = 0.5f; tm.text = multi; });
        foreach (TextAnchor anchor in Enum.GetValues(typeof(TextAnchor)))
            Case("anchor" + anchor, tm => { tm.anchor = anchor; tm.text = multi; });
        foreach (TextAlignment alignment in Enum.GetValues(typeof(TextAlignment)))
        {
            Case("align" + alignment, tm => { tm.alignment = alignment; tm.text = multi; });
            Case("align" + alignment + "MiddleCenter", tm => { tm.alignment = alignment; tm.anchor = TextAnchor.MiddleCenter; tm.text = multi; });
        }
        Case("tab4", tm => tm.text = "a\tb");
        Case("alignCenterOdd", tm => { tm.alignment = TextAlignment.Center; tm.text = "Ab\nAbc"; });
        Case("anchorCenterOdd", tm => { tm.anchor = TextAnchor.UpperCenter; tm.text = "Abc"; });
        Case("anchorMiddleOddLines", tm => { tm.anchor = TextAnchor.MiddleRight; tm.lineSpacing = 1.3f; tm.text = "Abc\nd"; });
        Case("tab25", tm => { tm.tabSize = 2.5f; tm.text = "a\tb"; });
        Case("tab0", tm => { tm.tabSize = 0f; tm.text = "a\tb"; });
        Case("tab27", tm => { tm.tabSize = 2.7f; tm.text = "a\tb"; });
        Case("tab35", tm => { tm.tabSize = 3.5f; tm.text = "a\tb"; });
        Case("tabTwice", tm => tm.text = "a\t\tb\tc");
        Case("tabLeading", tm => tm.text = "\tx\nab\tc");
        Case("offsetZ", tm => { tm.offsetZ = 0.3f; tm.text = "Ab"; });
        Case("offsetZCharSize2", tm => { tm.offsetZ = 0.3f; tm.characterSize = 2f; tm.text = "Ab"; });
        Case("richBold", tm => tm.text = "<b>Ab</b>c");
        Case("richItalic", tm => tm.text = "<i>Ab</i>c");
        Case("richSize", tm => tm.text = "<size=30>A</size>b");
        Case("richColorName", tm => tm.text = "<color=red>A</color>b");
        Case("richColorHex", tm => tm.text = "<color=#00ff0080>A</color>b");
        Case("richNested", tm => tm.text = "<b><color=blue>A<i>b</i></color></b>");
        Case("richUnclosed", tm => tm.text = "<b>Ab");
        Case("richOff", tm => { tm.richText = false; tm.text = "<b>A</b>"; });
        Case("styleBold", tm => { tm.fontStyle = FontStyle.Bold; tm.text = "Ab"; });
        Case("styleItalic", tm => { tm.fontStyle = FontStyle.Italic; tm.text = "Ab"; });
        Case("styleBoldItalic", tm => { tm.fontStyle = FontStyle.BoldAndItalic; tm.text = "Ab"; });
        Case("color", tm => { tm.color = new Color(1f, 0.5f, 0.25f, 0.5f); tm.text = "Ab"; });
        Case("colorWithTag", tm => { tm.color = new Color(1f, 0.5f, 0.25f, 0.5f); tm.text = "<color=#0000ffff>A</color>b"; });
        Case("empty", tm => tm.text = "");
        Case("space", tm => tm.text = " ");
        Case("spaces", tm => tm.text = "   ");
        Case("innerSpace", tm => tm.text = "a b");
        Case("outerSpace", tm => tm.text = " a ");
        Case("newline", tm => tm.text = "\n");
        Case("trailingNewline", tm => tm.text = "a\n");
        Case("leadingNewline", tm => tm.text = "\na");
        Case("blankLine", tm => tm.text = "a\n\nb");
        Case("carriageReturn", tm => tm.text = "a\r\nb");
        Case("missingPrivateUse", tm => tm.text = "AB");
        Case("nullText", tm => tm.text = null);
        Case("customFont", tm => { tm.font = m_Custom; tm.text = "ABA"; });
        Case("customFontMissing", tm => { tm.font = m_Custom; tm.text = "AxB"; });
        Case("customFontSize", tm => { tm.font = m_Custom; tm.fontSize = 30; tm.text = "AB"; });
        Case("customFontMultiAnchor", tm => { tm.font = m_Custom; tm.anchor = TextAnchor.LowerRight; tm.text = "AB\nA"; });
        Case("rendererFirst", tm => tm.text = "Ab", true);
        Case("fontNull", tm => { tm.font = null; tm.text = "Ab"; });
        Changes();
        Application.logMessageReceived -= OnLog;
        yield return Images();
        yield return AtlasControls();
        m_Out.Append("{\"k\":\"complete\",\"v\":true}\n");
        File.WriteAllText(Path.Combine(m_Dir, "values.jsonl"), m_Out.ToString());
        Exit();
    }

    /// <summary>The dynamic font texture outside the cases: a fresh OS font (its own texture) asked
    /// for characters at growing sizes over three frames, then for a few again. After each request:
    /// the texture size, how often textureRebuilt fired for the font, and for every character asked
    /// so far whether it is in the texture, its uv corners in texels (bottom left, top right) and
    /// whether it is stored flipped.</summary>
    IEnumerator AtlasControls()
    {
        var font = Font.CreateDynamicFontFromOSFont("Liberation Sans", 16);
        int rebuilt = 0;
        Action<Font> count = f => { if (f == font) rebuilt++; };
        Font.textureRebuilt += count;
        var asked = new List<(char c, int size)>();
        var steps = new (string name, string text, int size, bool nextFrame)[]
        {
            ("first", "Hello World", 16, false),
            ("larger", "ABCDEFGHIJKLM", 40, false),
            ("frame2", "abcdefghijklmnopqrstuvwxyz", 64, true),
            ("frame3", "0123456789 W", 90, true),
            ("again", "Hello", 16, false),
        };
        foreach (var step in steps)
        {
            if (step.nextFrame) yield return null;
            font.RequestCharactersInTexture(step.text, step.size, FontStyle.Normal);
            foreach (var c in step.text)
                if (!asked.Contains((c, step.size))) asked.Add((c, step.size));
            var texture = font.material.mainTexture;
            Put("atlas/" + step.name + "/texture", $"[{texture.width},{texture.height}]");
            Put("atlas/" + step.name + "/rebuilt", rebuilt.ToString(Inv));
            var cells = new List<string>();
            foreach (var (c, size) in asked)
            {
                if (!font.GetCharacterInfo(c, out var info, size, FontStyle.Normal)) { cells.Add($"[{(int)c},{size},0]"); continue; }
#pragma warning disable 618
                bool flipped = info.flipped;
#pragma warning restore 618
                cells.Add($"[{(int)c},{size},1,{F(info.uvBottomLeft.x * texture.width)},{F(info.uvBottomLeft.y * texture.height)},{F(info.uvTopRight.x * texture.width)},{F(info.uvTopRight.y * texture.height)},{(flipped ? 1 : 0)}]");
            }
            Put("atlas/" + step.name + "/characters", "[" + string.Join(",", cells) + "]");
        }
        Font.textureRebuilt -= count;
    }

    /// <summary>A script-made custom (non-dynamic) font: 'A' and 'B' as rectangles of a 32x16
    /// texture, with distinct advances, bearings and uv rects.</summary>
    static Font CustomFont()
    {
        var texture = new Texture2D(32, 16, TextureFormat.RGBA32, false) { name = "CustomFontTexture", filterMode = FilterMode.Point };
        var pixels = new Color32[32 * 16];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = (i % 32) < 16 ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 128);
        texture.SetPixels32(pixels);
        texture.Apply();
        var font = new Font("CustomProbeFont");
        var material = new Material(Shader.Find("GUI/Text Shader")) { name = "CustomFontMaterial", mainTexture = texture };
        font.material = material;
        var a = new CharacterInfo { index = 'A', advance = 9, minX = 1, maxX = 8, minY = -2, maxY = 10 };
        a.uvBottomLeft = new Vector2(0f, 0f); a.uvBottomRight = new Vector2(0.5f, 0f);
        a.uvTopRight = new Vector2(0.5f, 1f); a.uvTopLeft = new Vector2(0f, 1f);
        var b = new CharacterInfo { index = 'B', advance = 6, minX = 0, maxX = 6, minY = 0, maxY = 7 };
        b.uvBottomLeft = new Vector2(0.5f, 0f); b.uvBottomRight = new Vector2(1f, 0f);
        b.uvTopRight = new Vector2(1f, 0.5f); b.uvTopLeft = new Vector2(0.5f, 0.5f);
        font.characterInfo = new[] { a, b };
        return font;
    }

    HashSet<int> MeshIds() => new HashSet<int>(Resources.FindObjectsOfTypeAll<Mesh>().Select(m => m.GetInstanceID()));

    /// <summary>The mesh objects created since <paramref name="before"/>.</summary>
    List<Mesh> NewMeshes(HashSet<int> before) =>
        Resources.FindObjectsOfTypeAll<Mesh>().Where(m => !before.Contains(m.GetInstanceID())).ToList();

    void Case(string name, Action<TextMesh> setup, bool rendererFirst = false)
    {
        m_Logs.Clear();
        var before = MeshIds();
        var go = new GameObject("case " + name);
        // A renderer that already has a material when the TextMesh is added.
        if (rendererFirst) go.AddComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("Unlit/Color")) { name = "PresetMaterial" };
        var tm = go.AddComponent<TextMesh>();
        setup(tm);
        var renderer = go.GetComponent<MeshRenderer>();
        var bounds = renderer.bounds;
        string key = "case/" + name + "/";
        Put(key + "font", Str(tm.font ? tm.font.name : "null"));
        Put(key + "materials", "[" + string.Join(",", renderer.sharedMaterials.Select(m => Str(m ? m.name + "|" + m.shader.name : "null"))) + "]");
        Put(key + "bounds", "[" + Vec(bounds.center) + "," + Vec(bounds.extents) + "]");
        var meshes = NewMeshes(before);
        Put(key + "newMeshes", meshes.Count.ToString(Inv));
        if (meshes.Count == 1) Mesh(key, meshes[0], tm);
        Put(key + "logs", "[" + string.Join(",", m_Logs.Select(Str)) + "]");
        go.SetActive(false);
    }

    void Mesh(string key, Mesh mesh, TextMesh tm)
    {
        var font = tm.font;
        Put(key + "mesh/name", Str(mesh.name));
        Put(key + "mesh/hideFlags", Str(mesh.hideFlags.ToString()));
        Put(key + "mesh/subMeshes", mesh.subMeshCount.ToString(Inv));
        Put(key + "mesh/vertexCount", mesh.vertexCount.ToString(Inv));
        Put(key + "mesh/triangles", "[" + string.Join(",", mesh.triangles) + "]");
        Put(key + "mesh/positions", Floats(mesh.vertices.SelectMany(v => new[] { v.x, v.y, v.z })));
        Put(key + "mesh/colors", "[" + string.Join(",", mesh.colors32.Select(c => $"[{c.r},{c.g},{c.b},{c.a}]")) + "]");
        Put(key + "mesh/normals", mesh.normals.Length.ToString(Inv));
        Put(key + "mesh/tangents", mesh.tangents.Length.ToString(Inv));
        Put(key + "mesh/uv2", mesh.uv2.Length.ToString(Inv));
        Put(key + "mesh/bounds", "[" + Vec(mesh.bounds.center) + "," + Vec(mesh.bounds.extents) + "]");
        var uv = mesh.uv;
        Put(key + "mesh/uvCount", uv.Length.ToString(Inv));
        if (font == null || uv.Length != mesh.vertexCount) return;
        if (!font.dynamic)
        {
            Put(key + "mesh/uvs", Floats(uv.SelectMany(p => new[] { p.x, p.y })));
            return;
        }
        // Where the atlas packs (and how it orients) each glyph is the font's business: record
        // each quad's uv size in texels of the font texture and, for plain text, whether its four
        // uvs are the character's own (GetCharacterInfo, top left, top right, bottom right, bottom left).
        var texture = font.material != null ? font.material.mainTexture : null;
        if (texture == null) return;
        var sizes = new List<float>();
        for (int q = 0; q + 3 < uv.Length; q += 4)
        {
            float u0 = Mathf.Min(uv[q].x, uv[q + 1].x, uv[q + 2].x, uv[q + 3].x), u1 = Mathf.Max(uv[q].x, uv[q + 1].x, uv[q + 2].x, uv[q + 3].x);
            float v0 = Mathf.Min(uv[q].y, uv[q + 1].y, uv[q + 2].y, uv[q + 3].y), v1 = Mathf.Max(uv[q].y, uv[q + 1].y, uv[q + 2].y, uv[q + 3].y);
            sizes.Add(Mathf.Round((u1 - u0) * texture.width));
            sizes.Add(Mathf.Round((v1 - v0) * texture.height));
        }
        Put(key + "mesh/uvTexels", Floats(sizes));
        if (tm.richText && tm.text != null && tm.text.IndexOf('<') >= 0) return;
        var matches = new List<string>();
        int quad = 0;
        foreach (var c in tm.text ?? "")
        {
            if (c == ' ' || c == '\t' || c == '\n') continue;
            if (quad * 4 + 3 >= uv.Length) break;
            string match = "none";
            if (font.GetCharacterInfo(c, out var info, tm.fontSize, tm.fontStyle))
                match = uv[quad * 4] == info.uvTopLeft && uv[quad * 4 + 1] == info.uvTopRight && uv[quad * 4 + 2] == info.uvBottomRight && uv[quad * 4 + 3] == info.uvBottomLeft ? "same" : "different";
            matches.Add(Str(c + ":" + match));
            quad++;
        }
        Put(key + "mesh/uvCharacterInfo", "[" + string.Join(",", matches) + "]");
    }

    /// <summary>A TextMesh whose properties change after it was generated: whether its mesh object
    /// stays the same and what it holds afterwards.</summary>
    void Changes()
    {
        m_Logs.Clear();
        var before = MeshIds();
        var go = new GameObject("changes");
        var tm = go.AddComponent<TextMesh>();
        tm.text = "Ab";
        var renderer = go.GetComponent<MeshRenderer>();
        _ = renderer.bounds;
        var first = NewMeshes(before);
        Put("changes/meshesAfterFirst", first.Count.ToString(Inv));
        tm.text = "Abc\nd";
        tm.anchor = TextAnchor.LowerCenter;
        Put("changes/bounds", "[" + Vec(renderer.bounds.center) + "," + Vec(renderer.bounds.extents) + "]");
        var second = NewMeshes(before);
        Put("changes/meshesAfterChange", second.Count.ToString(Inv));
        Put("changes/sameMesh", first.Count == 1 && second.Count == 1 && first[0] == second[0] ? "true" : "false");
        if (second.Count == 1) Mesh("changes/", second[0], tm);
        tm.font = m_Custom;
        Put("changes/materialsAfterFont", "[" + string.Join(",", renderer.sharedMaterials.Select(m => Str(m ? m.name : "null"))) + "]");
        Put("changes/logs", "[" + string.Join(",", m_Logs.Select(Str)) + "]");
        go.SetActive(false);
    }

    IEnumerator Images()
    {
        var camera = new GameObject("probe camera").AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 2f;
        camera.transform.position = new Vector3(0f, 0f, -10f);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.1f, 0.1f, 0.2f, 1f);
        var rt = new RenderTexture(256, 128, 24, RenderTextureFormat.ARGB32);
        camera.targetTexture = rt;

        var text = new GameObject("image text").AddComponent<TextMesh>();
        text.text = "Ab<color=#ff8000>gj</color>\n<b>Text</b> <i>Mesh</i>";
        text.fontSize = 24;
        text.characterSize = 0.15f;
        text.anchor = TextAnchor.MiddleCenter;
        text.alignment = TextAlignment.Center;
        yield return null;
        Capture(camera, rt, "font-material.png");

        // A custom material: unlit solid colour, so each glyph quad shows as a filled box.
        var renderer = text.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = new Material(Shader.Find("Unlit/Color")) { color = new Color(0.2f, 0.9f, 0.3f, 1f) };
        yield return null;
        Capture(camera, rt, "custom-material.png");
        camera.targetTexture = null;
    }

    void Capture(Camera camera, RenderTexture rt, string file)
    {
        camera.Render();
        var previous = RenderTexture.active;
        RenderTexture.active = rt;
        var image = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
        image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        image.Apply();
        RenderTexture.active = previous;
        File.WriteAllBytes(Path.Combine(m_Dir, file), image.EncodeToPNG());
    }

    static void Exit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
