using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.TextCore.Text;

/// <summary>Which OS faces characters missing from a font come from, and their metrics: the
/// legacy font system (CharacterInfo) and TextCore font assets with their OS fallback table.</summary>
public sealed class OsFontFallbackProbe : MonoBehaviour
{
    readonly StringBuilder rows = new StringBuilder();
    string output;
    static readonly int[] Characters = {
        0x0041, 0x00E9, 0x2200, 0x2318, 0x76F4, 0x231A, 0x23F0, 0x2615, 0x2B50, 0x2603,
        0x1780, 0x17A2, 0x1200, 0x12A0, 0x1820, 0x1843, 0x20000, 0x20B9F, 0x2A6D6,
        0x0F00, 0x0D85, 0x1000, 0x13A0, 0x1401, 0xA000, 0x0780, 0x1680, 0x16A0, 0x2D30, 0x07C0, 0xA500,
        0xAC00, 0x0E01, 0x0915, 0x05D0, 0x0628, 0x0531, 0x10D0, 0x0B95, 0x0C15, 0x0C95, 0x0D15,
        0x0A15, 0x0A95, 0x0B15, 0x0995, 0x4E00, 0x3042, 0x30A2,
        0x1E900, 0x10400, 0x1F600, 0x1F44D, 0xFFFE,
    };
    static readonly int[] Sizes = { 16, 32 };
    // Families whose style-less system font lookup (the face a family name stands for) is
    // recorded: the OS fallback families, and installed families with and without a Regular face.
    static readonly string[] Families = { "LastResort", "Apple Color Emoji", "Arial", "Arial Unicode MS", "Helvetica Neue", "Microsoft Sans Serif",
        "Microsoft YaHei", "Nirmala UI", "Al Bayan", "Marker Felt", "Apple Braille", "Party LET", "Hiragino Sans", "PingFang SC",
        "Avenir", "Avenir Next", "Optima", "Futura", "Gill Sans", "Menlo", "Kefa", "Khmer Sangam MN", "Noto Sans Mongolian", "Al Nile" };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var path = Environment.GetEnvironmentVariable("OS_FONT_FALLBACK_OUT");
        if (!string.IsNullOrEmpty(path)) new GameObject("OS font fallback probe").AddComponent<OsFontFallbackProbe>().output = path;
    }

    void Value(string key, object value) => rows.Append("{\"k\":\"").Append(key).Append("\",\"v\":").Append(Json(value)).Append("}\n");

    static string Json(object value)
    {
        if (value is string s) return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        if (value is bool b) return b ? "true" : "false";
        if (value is float f) return f.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    static string Code(int c) => "U+" + c.ToString("X4");

    void Legacy(string name, Font font)
    {
        var text = new StringBuilder();
        foreach (int c in Characters) if (c <= 0xFFFF) text.Append((char)c);
        foreach (int size in Sizes)
        {
            font.RequestCharactersInTexture(text.ToString(), size, FontStyle.Normal);
            foreach (int c in Characters)
            {
                if (c > 0xFFFF) continue; // the legacy font system takes UTF-16 code units
                string key = "legacy/" + name + "/" + size + "/" + Code(c);
                bool found = font.GetCharacterInfo((char)c, out var info, size, FontStyle.Normal);
                Value(key + "/found", found);
                if (found)
                    Value(key + "/metrics", string.Join(",", info.minX, info.maxX, info.minY, info.maxY, info.advance,
                        info.glyphWidth, info.glyphHeight, info.bearing, info.size, (int)info.style));
            }
        }
    }

    void TextCore(string name, Font font)
    {
        var asset = FontAsset.CreateFontAsset(font);
        string key = "textcore/" + name;
        Value(key + "/asset", asset == null ? "null" : asset.faceInfo.familyName + "/" + asset.faceInfo.styleName);
        if (asset == null) return;
        var chain = new System.Collections.Generic.List<FontAsset> { asset };
        var table = asset.fallbackFontAssetTable;
        Value(key + "/fallbacks", table == null ? -1 : table.Count);
        if (table != null)
            for (int i = 0; i < table.Count; i++)
            {
                var fallback = table[i];
                Value(key + "/fallback/" + i, fallback == null ? "null" : fallback.faceInfo.familyName + "/" + fallback.faceInfo.styleName + "/" + fallback.faceInfo.pointSize);
                chain.Add(fallback);
            }
        foreach (int c in Characters)
        {
            string chosen = "none";
            for (int i = 0; i < chain.Count; i++)
            {
                var candidate = chain[i];
                if (candidate == null || !candidate.HasCharacter((uint)c, false, true)) continue;
                var metrics = candidate.characterLookupTable[(uint)c].glyph.metrics;
                chosen = i + "/" + candidate.faceInfo.familyName + "/" + string.Join(",", Json(metrics.width), Json(metrics.height),
                    Json(metrics.horizontalBearingX), Json(metrics.horizontalBearingY), Json(metrics.horizontalAdvance));
                break;
            }
            Value(key + "/" + Code(c), chosen);
        }
    }

    IEnumerator Start()
    {
        yield return null;
        try
        {
            var get = typeof(Font).GetMethod("GetOSFallbacks", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            Value("osfallbacks", get == null ? "missing" : string.Join("|", (string[])get.Invoke(null, null)));
            var engine = typeof(UnityEngine.TextCore.LowLevel.FontEngine);
            var reference = engine.GetMethod("TryGetSystemFontReference", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            if (reference != null)
                foreach (var family in Families)
                {
                    var args = new object[] { family, null, null };
                    bool found = (bool)reference.Invoke(null, args);
                    Value("enginefont/" + family, found ? args[2].GetType().GetField("filePath").GetValue(args[2]) + "#" + args[2].GetType().GetField("faceIndex").GetValue(args[2]) : "none");
                }
            var legacy = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var sans = Resources.Load<Font>("Sans");
            Legacy("LegacyRuntime", legacy);
            Legacy("Sans", sans);
            TextCore("LegacyRuntime", legacy);
            TextCore("Sans", sans);
        }
        catch (Exception e)
        {
            Value("exception", e.GetType().Name + ": " + e.Message);
        }
        Value("complete", true);
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "values.jsonl"), rows.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
