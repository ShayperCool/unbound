using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;
using TextElement = UnityEngine.UIElements.TextElement;
using UnityEngine.TextCore.LowLevel;

// Runs alongside the unchanged UI fixture in a disposable cache copy. Observations of the
// inherited label do not measure it: MeasureTextSize itself changes the handle's saved sizes.
public static class TextMetricsProbe
{
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static readonly List<string> Rows = new List<string>();
    [Serializable] public class Row { public string key, text, font; public float[] values; }
    static void Add(string key, string text, string font, params float[] values) =>
        Rows.Add(JsonUtility.ToJson(new Row { key = key, text = text, font = font, values = values }));
    static FontAsset Asset(TextElement e)
    {
        if (e.resolvedStyle.unityFontDefinition.fontAsset != null) return e.resolvedStyle.unityFontDefinition.fontAsset;
        foreach (string name in new[] { "UnityEngine.UIElements.TextUtilities", "UnityEngine.UIElements.UITKTextHandle" })
        {
            var type = typeof(TextElement).Assembly.GetType(name);
            var method = type?.GetMethod("GetFontAsset", Flags, null, new[] { typeof(VisualElement) }, null);
            if (method != null)
            {
                var result = (FontAsset)method.Invoke(null, new object[] { e });
                if (result != null) return result;
            }
        }
        throw new InvalidOperationException("No font asset resolver for the measured runtime.");
    }
    static List<GlyphPairAdjustmentRecord> Pairs(FontAsset asset) => (List<GlyphPairAdjustmentRecord>)
        typeof(FontFeatureTable).GetProperty("glyphPairAdjustmentRecords", Flags).GetValue(asset.fontFeatureTable);
    static void Tick()
    {
        var utility = typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.UIElementsRuntimeUtility");
        utility.GetMethod("UpdatePanels", Flags).Invoke(null, null);
        utility.GetMethod("UpdatePanels", Flags).Invoke(null, null);
    }
    public static void Observe(string phase, UIDocument document)
    {
        var label = document.rootVisualElement.Q<Foldout>("foldout").Q<Label>();
        var font = Asset(label); var f = font.faceInfo; var s = label.resolvedStyle;
        Add("fixture/" + phase, label.text, font.name, label.layout.width, label.layout.height,
            label.contentRect.width, s.fontSize, (float)s.unityFontStyleAndWeight, label.scaledPixelsPerPoint,
            f.pointSize, f.scale, f.ascentLine, f.descentLine, f.lineHeight);
        foreach (var p in Pairs(font))
        {
            var a = p.firstAdjustmentRecord; var b = p.secondAdjustmentRecord;
            // Record the full font feature table with stable glyph-pair keys.
            Add("pairs/" + phase + "/" + a.glyphIndex + "/" + b.glyphIndex, "", "",
                a.glyphValueRecord.xPlacement, a.glyphValueRecord.xAdvance, b.glyphValueRecord.xPlacement, b.glyphValueRecord.xAdvance);
        }
    }
    public static void Run(UIDocument document)
    {
        var root = document.rootVisualElement;
        root.Clear();
        var parent = new VisualElement(); parent.style.alignItems = Align.FlexStart; root.Add(parent);
        var label = new Label(); label.style.marginLeft = label.style.marginRight = label.style.marginTop = label.style.marginBottom = 0;
        label.style.paddingLeft = label.style.paddingRight = label.style.paddingTop = label.style.paddingBottom = 0;
        label.style.flexShrink = 0; parent.Add(label); Tick();
        foreach (float scale in new[] { 1f, 1.25f, 1.5f, 2f })
        {
            document.panelSettings.scale = scale; Tick();
            foreach (FontStyle style in new[] { FontStyle.Normal, FontStyle.Bold, FontStyle.Italic, FontStyle.BoldAndItalic })
            foreach (float size in new[] { 10f, 12f, 13.9f, 14f, 14.1f, 16f, 24f })
            {
                label.style.fontSize = size; label.style.unityFontStyleAndWeight = style;
                foreach (string text in new[] { "Foldout", "Toggle", "AVATAR", "To WA", "iiilll", "mmmm", "", " ", "   ", "A ", "A\nB", "A longer label wraps here", "<b>AV</b> text", "<size=18>To</size>" })
                foreach (bool wrap in new[] { false, true })
                {
                    label.style.whiteSpace = wrap ? WhiteSpace.Normal : WhiteSpace.NoWrap;
                    label.text = text; Tick();
                    string key = "measure/" + scale.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/" + style + "/" + size.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/" + Array.IndexOf(Texts, text) + "/" + wrap;
                    Add(key + "/layout", text, Asset(label).name, label.layout.width, label.layout.height);
                    foreach (float width in new[] { float.NaN, 0f, 20f, 44.8f, 45f, 45.2f, 100f })
                    foreach (VisualElement.MeasureMode mode in new[] { VisualElement.MeasureMode.Undefined, VisualElement.MeasureMode.AtMost, VisualElement.MeasureMode.Exactly })
                    {
                        var measured = label.MeasureTextSize(text, width, mode, float.NaN, VisualElement.MeasureMode.Undefined);
                        Add(key + "/" + (float.IsNaN(width) ? "nan" : width.ToString(System.Globalization.CultureInfo.InvariantCulture)) + "/" + mode, text, "", measured.x, measured.y);
                    }
                }
            }
        }
        // Independent dynamic assets expose the cold preferred measurement and the update after
        // generation without depending on which theme glyphs previous controls happened to load.
        var source = label.resolvedStyle.unityFont;
        if (source == null) source = Asset(label).sourceFontFile;
        for (int repeat = 0; repeat < 2; repeat++)
        {
            var asset = FontAsset.CreateFontAsset(source);
            label.style.unityFontDefinition = FontDefinition.FromSDFFont(asset);
            label.style.fontSize = 14; label.style.unityFontStyleAndWeight = FontStyle.Normal;
            label.style.whiteSpace = WhiteSpace.NoWrap; label.text = "Foldout AV To"; Tick();
            for (int phase = 0; phase < 3; phase++)
            {
                var m = label.MeasureTextSize(label.text, float.NaN, VisualElement.MeasureMode.Undefined, float.NaN, VisualElement.MeasureMode.Undefined);
                Add("dynamic/" + repeat + "/" + phase, label.text, source.name, m.x, m.y, label.layout.width, Pairs(asset).Count);
                typeof(FontAsset).GetMethod("UpdateFontAssetsInUpdateQueue", Flags).Invoke(null, null);
                label.MarkDirtyRepaint(); label.style.fontSize = phase == 0 ? 14.001f : 14; Tick();
            }
        }
        string output = Environment.GetEnvironmentVariable("UITEXTMETRICS_OUT");
        Directory.CreateDirectory(output);
        File.WriteAllLines(Path.Combine(output, "metrics.jsonl"), Rows);
    }
    static readonly string[] Texts = { "Foldout", "Toggle", "AVATAR", "To WA", "iiilll", "mmmm", "", " ", "   ", "A ", "A\nB", "A longer label wraps here", "<b>AV</b> text", "<size=18>To</size>" };
}
