using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>Legacy dynamic-font metrics and alpha readbacks, independent of atlas placement.</summary>
public sealed class GlyphRasterProbe : MonoBehaviour
{
    readonly StringBuilder rows = new StringBuilder();
    string output;
    const string Characters = " AHilWgjéñ0@";
    // Missing from Liberation Sans; FreeSans, DejaVu Sans and CJK faces hold them.
    const string FallbackCharacters = "\u2200\u2318\u76f4";
    static readonly int[] Sizes = { 8, 11, 12, 13, 16, 24, 32, 48, 64, 96 };
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var path = Environment.GetEnvironmentVariable("GLYPH_RASTER_OUT");
        if (!string.IsNullOrEmpty(path)) new GameObject("Glyph raster probe").AddComponent<GlyphRasterProbe>().output = path;
    }
    void Value(string key, object value) => rows.Append("{\"k\":\"").Append(key).Append("\",\"v\":").Append(Json(value)).Append("}\n");
    static string Json(object value)
    {
        if (value is string s) return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        if (value is bool b) return b ? "true" : "false";
        return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
    }
    static string Metrics(CharacterInfo c) => string.Join(",", c.minX, c.maxX, c.minY, c.maxY, c.advance,
        c.glyphWidth, c.glyphHeight, c.bearing, c.size, (int)c.style);
    static Texture2D ReadAtlas(Texture source)
    {
        var old = RenderTexture.active;
        var rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, true);
        Graphics.Blit(source, rt); RenderTexture.active = rt;
        copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0); copy.Apply();
        RenderTexture.active = old; RenderTexture.ReleaseTemporary(rt);
        return copy;
    }
    static byte[] GlyphPixels(CharacterInfo c, Texture2D atlas, Color32[] pixels)
    {
        var u = c.uvTopRight - c.uvTopLeft; var v = c.uvBottomLeft - c.uvTopLeft;
        if (u.sqrMagnitude == 0 || v.sqrMagnitude == 0) return new byte[0];
        int width = c.glyphWidth, height = c.glyphHeight;
        var result = new byte[width * height];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            var uv = c.uvTopLeft + u * ((x + 0.5f) / width) + v * ((y + 0.5f) / height);
            int ax = Mathf.Clamp(Mathf.FloorToInt(uv.x * atlas.width), 0, atlas.width - 1);
            int ay = Mathf.Clamp(Mathf.FloorToInt(uv.y * atlas.height), 0, atlas.height - 1);
            result[y * width + x] = pixels[ay * atlas.width + ax].a;
        }
        return result;
    }
    void Glyph(string key, Font font, char ch, int size, FontStyle style, Texture2D atlas, Color32[] pixels)
    {
        bool found = font.GetCharacterInfo(ch, out var info, size, style);
        Value(key + "/found", found);
        if (!found) return;
        Value(key + "/metrics", Metrics(info));
#pragma warning disable 618
        Value(key + "/raw", string.Join(",", info.vert.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture), info.vert.y.ToString("R", System.Globalization.CultureInfo.InvariantCulture), info.vert.width.ToString("R", System.Globalization.CultureInfo.InvariantCulture), info.vert.height.ToString("R", System.Globalization.CultureInfo.InvariantCulture), info.width.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
#pragma warning restore 618
        Value(key + "/alpha", Convert.ToBase64String(GlyphPixels(info, atlas, pixels)));
        CharacterInfo? entry = null;
        foreach (var item in font.characterInfo) if (item.index == ch && item.size == size && item.style == style) { entry = item; break; }
        Value(key + "/array", entry.HasValue ? Metrics(entry.Value) : "absent");
    }
    IEnumerator Start()
    {
        Directory.CreateDirectory(output);
        // A fresh size/character pair changes from absent to present after the documented request.
        var requested = Resources.Load<Font>("Synthetic");
        Value("request/before", requested.GetCharacterInfo('Ω', out var unused, 37, FontStyle.Normal));
        requested.RequestCharactersInTexture("Ω", 37, FontStyle.Normal);
        yield return null;
        Value("request/after", requested.GetCharacterInfo('Ω', out var requestedInfo, 37, FontStyle.Normal));
        Value("request/advance", requestedInfo.advance);
        foreach (string name in new[] { "Legacy", "Sans", "Serif", "Styled", "Hinted", "Raster", "OS", "Synthetic", "SyntheticHinted", "SyntheticRaster" })
        {
            Font font = name == "Legacy" ? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") : Resources.Load<Font>(name);
            Value(name + "/present", font != null);
            Value(name + "/fontNames", string.Join("|", font.fontNames));
            Value(name + "/fontSize", font.fontSize); Value(name + "/ascent", font.ascent); Value(name + "/lineHeight", font.lineHeight);
            foreach (int size in Sizes) foreach (FontStyle style in new[] { FontStyle.Normal, FontStyle.Bold, FontStyle.Italic, FontStyle.BoldAndItalic })
            {
                font.RequestCharactersInTexture(Characters, size, style);
                // Finish the texture update before its GPU readback.
                yield return null;
                var atlas = ReadAtlas(font.material.mainTexture); var pixels = atlas.GetPixels32();
                foreach (char ch in Characters) Glyph(name + "/" + size + "/" + (int)style + "/" + (int)ch, font, ch, size, style, atlas, pixels);
                if (size == 96 && style == FontStyle.BoldAndItalic)
                {
                    string raw = Path.Combine(output, "../raw-atlases"); Directory.CreateDirectory(raw);
                    File.WriteAllBytes(Path.Combine(raw, name + "-atlas.png"), atlas.EncodeToPNG());
                }
                Destroy(atlas);
            }
        }
        // Characters the faces lack: per-character fallback through the font's names and the
        // OS fallback families, in the requesting font's rendering mode and style.
        foreach (string name in new[] { "Legacy", "Synthetic", "Hinted" })
        {
            Font font = name == "Legacy" ? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") : Resources.Load<Font>(name);
            foreach (int c in new[] { 0x1f, 0x20, 'A', 0x2200, 0x2318, 0x76f4, 0xe000, 0xfffe }) Value("fallback/" + name + "/has/" + c, font.HasCharacter((char)c));
            foreach (int size in new[] { 13, 32 }) foreach (FontStyle style in new[] { FontStyle.Normal, FontStyle.Bold })
            {
                font.RequestCharactersInTexture(FallbackCharacters, size, style);
                yield return null;
                var atlas = ReadAtlas(font.material.mainTexture); var pixels = atlas.GetPixels32();
                foreach (char ch in FallbackCharacters) Glyph("fallback/" + name + "/" + size + "/" + (int)style + "/" + (int)ch, font, ch, size, style, atlas, pixels);
                Destroy(atlas);
            }
        }
        Value("complete", true); File.WriteAllText(Path.Combine(output, "values.jsonl"), rows.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
