using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;

/// <summary>Checks native-backed features at start, for the fixture test: a texture through PNG
/// bytes and back, a model asset loaded as its prefab, and TextCore's font engine rendering a
/// glyph into an SDF atlas as TextMeshPro's dynamic fonts do.</summary>
static class RuntimeChecks
{
    [RuntimeInitializeOnLoadMethod]
    static void Run()
    {
        var texture = new Texture2D(3, 2, TextureFormat.RGBA32, false);
        texture.SetPixel(0, 0, Color.red);
        texture.SetPixel(2, 1, Color.blue);
        texture.Apply();
        var png = texture.EncodeToPNG();
        var loaded = new Texture2D(1, 1);
        bool ok = loaded.LoadImage(png);
        Debug.Log($"image {ok} {loaded.width}x{loaded.height} {loaded.GetPixel(0, 0)} {loaded.GetPixel(2, 1)} {loaded.format}");

        // A model asset loads as its prefab, with its mesh.
        var model = Resources.Load<GameObject>("Quad");
        var filter = model != null ? model.GetComponentInChildren<MeshFilter>() : null;
        var mesh = filter != null ? filter.sharedMesh : null;
        Debug.Log(mesh != null
            ? $"model {model.name} {mesh.vertexCount} vertices {mesh.triangles.Length / 3} triangles {mesh.bounds.size}"
            : "model missing");

        CheckFontEngine();
    }

    static void CheckFontEngine()
    {
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var error = FontEngine.LoadFontFace(font, 90);
        var face = FontEngine.GetFaceInfo();
        FontEngine.TryGetGlyphWithUnicodeValue('A', GlyphLoadFlags.LOAD_NO_HINTING, out var glyph);
        Debug.Log($"fontengine {error} {face.familyName} {face.styleName} {face.pointSize} " +
                  $"ascent {face.ascentLine:F3} cap {face.capLine} glyph {glyph.index} advance {glyph.metrics.horizontalAdvance} rect {glyph.glyphRect.width}x{glyph.glyphRect.height}");

        // TextMeshPro adds glyphs through internal members; reach them as it does (InternalsVisibleTo).
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
        var atlas = new Texture2D(256, 256, TextureFormat.Alpha8, false);
        typeof(FontEngine).GetMethod("ResetAtlasTexture", flags).Invoke(null, new object[] { atlas });
        var free = new List<GlyphRect> { new GlyphRect(0, 0, 255, 255) };
        var used = new List<GlyphRect>();
        var add = typeof(FontEngine).GetMethod("TryAddGlyphToTexture", flags);
        var arguments = new object[] { glyph.index, 9, GlyphPackingMode.BestShortSideFit, free, used, GlyphRenderMode.SDFAA, atlas, null };
        bool added = (bool)add.Invoke(null, arguments);
        var rect = ((Glyph)arguments[7]).glyphRect;
        // Inside the crossbar's middle, at the padding's outer corner, and the edge band between.
        float Alpha(int x, int y) => atlas.GetPixel(x, y).a;
        var kerning = typeof(FontEngine).GetMethod("GetPairAdjustmentRecords", flags, null, new[] { typeof(List<uint>) }, null);
        FontEngine.TryGetGlyphIndex('V', out var v);
        var records = (GlyphPairAdjustmentRecord[])kerning.Invoke(null, new object[] { new List<uint> { glyph.index } });
        float av = 0f;
        foreach (var r in records)
            if (r.secondAdjustmentRecord.glyphIndex == v) av = r.firstAdjustmentRecord.glyphValueRecord.xAdvance;
        Debug.Log($"atlas {added} at {rect.x},{rect.y} used {used.Count} corner {Alpha(rect.x - 9, rect.y - 9):F2} " +
                  $"base {Alpha(rect.x + 1, rect.y):F2} kern AV {av}");
    }
}
