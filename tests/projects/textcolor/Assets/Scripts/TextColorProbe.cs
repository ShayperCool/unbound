using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;
using UnityEngine.UI;
using UnityEngine.UIElements;
using FontAsset = UnityEngine.TextCore.Text.FontAsset;

/// <summary>
/// Colour glyphs and legacy font cases: a bitmap-only colour emoji face (three glyphs of Noto Color
/// Emoji, one 109 ppem strike) through the font engine (face info at several sizes, glyph loads, a
/// font asset per render mode with its atlas pixels) and as a legacy dynamic font (character info and
/// texture pixels); a font imported with and without character spacing (character info and
/// TextGenerator layout); the sizes a canvas with a scale factor requests; a legacy font texture
/// driven to its largest size; and a UI Toolkit panel drawing emoji labels. Writes values.jsonl and
/// PNGs to $TEXTCOLOR_OUT.
/// </summary>
public class TextColorProbe : MonoBehaviour
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    const string Emoji = "\U0001F600❤\U0001F44D";
    static readonly List<string> s_Logs = new List<string>();
    readonly StringBuilder m_Out = new StringBuilder();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TEXTCOLOR_OUT"))) return;
        Application.logMessageReceived += (message, stack, type) => { if (type != LogType.Log) s_Logs.Add(type + ": " + message); };
        new GameObject("TextColorProbe").AddComponent<TextColorProbe>();
    }

    IEnumerator Start()
    {
        var dir = Environment.GetEnvironmentVariable("TEXTCOLOR_OUT");
        Directory.CreateDirectory(dir);
        void Step(string name, Action a)
        {
            try { a(); }
            catch (Exception e) { Value("error/" + name, Str(e.GetType().Name + ": " + e.Message)); }
        }
        var emoji = Resources.Load<Font>("Fonts/EmojiProbe");
        var spacing0 = Resources.Load<Font>("Fonts/Spacing0");
        var spacing6 = Resources.Load<Font>("Fonts/Spacing6");
        Step("faces", () => Faces(emoji));
        Step("assets", () => Assets(emoji, Emoji, 24, "asset", new[] {
            GlyphRenderMode.COLOR, GlyphRenderMode.COLOR_HINTED, GlyphRenderMode.SMOOTH, GlyphRenderMode.SDFAA,
        }));
        Step("legacy", () => LegacyEmoji(emoji));
        Step("spacing", () => { Spacing("spacing0", spacing0); Spacing("spacing6", spacing6); });
        Step("canvas", () => ScaledCanvas(spacing0));
        Step("panel", () => Panel(emoji, dir));
        string atlasCharacters = null;
        Step("full", () => atlasCharacters = FullTexture());
        var logs = new List<string>(s_Logs);
        logs.Sort(StringComparer.Ordinal);
        var sb = new StringBuilder("[");
        for (int i = 0; i < logs.Count; i++) sb.Append(i == 0 ? "" : ",").Append(Str(logs[i]));
        Value("logs", sb.Append("]").ToString());
        Step("generator-controls", () => GeneratorControls(spacing0));
        Step("legacy-exact-strike", () => LegacyEmoji(Resources.Load<Font>("Fonts/EmojiStrikeProbe"), 109, 109, "legacy/exact-strike"));
        Step("colr", () => VectorColor(Resources.Load<Font>("Fonts/LayerProbe"), "colr/plain"));
        Step("colr-hinted", () => VectorColor(Resources.Load<Font>("Fonts/HintedLayerProbe"), "colr/with-instructions"));
        int initialFrame = Time.frameCount;
        yield return null;
        Step("full/next", () => FullTextureNextFrame(atlasCharacters, Time.frameCount - initialFrame));
        m_Out.Append("{\"k\":\"complete\",\"v\":true}\n");
        File.WriteAllText(Path.Combine(dir, "values.jsonl"), m_Out.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    void GeneratorControls(Font source)
    {
        // Fresh font instances isolate texture history from the earlier observations.
        foreach (var update in new[] { false, true })
        foreach (var overflow in new[] { false, true })
        foreach (var anchor in new[] { TextAnchor.UpperLeft, TextAnchor.MiddleCenter, TextAnchor.LowerRight })
        foreach (var pivot in new[] { Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.one })
        {
            var font = Instantiate(source);
            foreach (var height in new[] { 100f, 173f })
            {
                var settings = new TextGenerationSettings {
                    font = font, fontSize = 32, color = Color.white, lineSpacing = 1, scaleFactor = 1,
                    textAnchor = anchor, updateBounds = update,
                    horizontalOverflow = overflow ? HorizontalWrapMode.Overflow : HorizontalWrapMode.Wrap,
                    verticalOverflow = overflow ? VerticalWrapMode.Overflow : VerticalWrapMode.Truncate,
                    generationExtents = new Vector2(400, height), pivot = pivot,
                    generateOutOfBounds = true,
                };
                var gen = new TextGenerator();
                gen.Populate("AVWa fig\nHi", settings);
                var sb = new StringBuilder("{\"rect\":[").Append(F(gen.rectExtents.x)).Append(',').Append(F(gen.rectExtents.y))
                    .Append(',').Append(F(gen.rectExtents.width)).Append(',').Append(F(gen.rectExtents.height)).Append("],\"lines\":[");
                for (int i = 0; i < gen.lines.Count; i++) {
                    var l = gen.lines[i];
                    sb.Append(i == 0 ? "" : ",").Append('[').Append(l.startCharIdx).Append(',').Append(l.height).Append(',').Append(F(l.topY)).Append(',').Append(F(l.leading)).Append(']');
                }
                sb.Append("],\"positions\":[");
                for (int i = 0; i < gen.verts.Count; i++)
                    sb.Append(i == 0 ? "" : ",").Append('[').Append(F(gen.verts[i].position.x)).Append(',').Append(F(gen.verts[i].position.y)).Append(']');
                Value("generator/" + update + "/" + overflow + "/" + anchor + "/" + F(pivot.x) + "/" + F(height), sb.Append("]}").ToString());
            }
            DestroyImmediate(font);
        }
        // A generator caches its space before the requested text, including when it has no space.
        foreach (var text in new[] { "Hi", "iH", "AVWa fig", "<size=24>Hi</size>", "" })
        {
            var font = Instantiate(source);
            var gen = new TextGenerator();
            gen.Populate(text, new TextGenerationSettings {
                font = font, fontSize = 32, color = Color.white, lineSpacing = 1, scaleFactor = 1, richText = true,
                updateBounds = true, verticalOverflow = VerticalWrapMode.Overflow, horizontalOverflow = HorizontalWrapMode.Overflow,
                generationExtents = new Vector2(400, 100), pivot = Vector2.zero,
            });
            foreach (var size in new[] { 24, 32 }) {
                var sb = new StringBuilder("[");
                foreach (var c in " HiAVWafg") {
                    if (sb.Length > 1) sb.Append(',');
                    sb.Append(font.GetCharacterInfo(c, out var info, size) ? Info(info) : "null");
                }
                Value("generator/packing/" + text + "/" + size, sb.Append(']').ToString());
            }
            DestroyImmediate(font);
        }
    }

    // The bitmap-only face at several sizes: the load's result, the face info, and the smiling
    // face's glyph loaded with and without colour.
    void Faces(Font emoji)
    {
        foreach (var size in new[] { 12f, 24f, 64f, 109f, 109.5f, 150f })
        {
            var error = FontEngine.LoadFontFace(emoji, size, 0);
            var f = FontEngine.GetFaceInfo();
            var sb = new StringBuilder("{\"error\":").Append(Str(error.ToString())).Append(",\"face\":").Append(Face(f));
            foreach (var flags in new[] { GlyphLoadFlags.LOAD_DEFAULT, GlyphLoadFlags.LOAD_COLOR, GlyphLoadFlags.LOAD_RENDER | GlyphLoadFlags.LOAD_COLOR, GlyphLoadFlags.LOAD_NO_SCALE })
            {
                bool found = FontEngine.TryGetGlyphWithUnicodeValue(0x1F600, flags, out var g);
                sb.Append(",\"").Append((int)flags).Append("\":").Append(found ? Glyph(g) : "null");
            }
            Value("face/" + F(size), sb.Append("}").ToString());
        }
    }

    static string Face(FaceInfo f) =>
        "{\"family\":" + Str(f.familyName) + ",\"pointSize\":" + F(f.pointSize) + ",\"scale\":" + F(f.scale) +
        ",\"lineHeight\":" + F(f.lineHeight) + ",\"ascentLine\":" + F(f.ascentLine) + ",\"capLine\":" + F(f.capLine) + ",\"meanLine\":" + F(f.meanLine) +
        ",\"baseline\":" + F(f.baseline) + ",\"descentLine\":" + F(f.descentLine) + ",\"underlineOffset\":" + F(f.underlineOffset) +
        ",\"underlineThickness\":" + F(f.underlineThickness) + ",\"strikethroughOffset\":" + F(f.strikethroughOffset) + ",\"tabWidth\":" + F(f.tabWidth) + "}";

    static string Glyph(UnityEngine.TextCore.Glyph g) =>
        "{\"index\":" + g.index + ",\"metrics\":[" + F(g.metrics.width) + "," + F(g.metrics.height) + "," + F(g.metrics.horizontalBearingX) + "," +
        F(g.metrics.horizontalBearingY) + "," + F(g.metrics.horizontalAdvance) + "],\"rect\":[" + g.glyphRect.x + "," + g.glyphRect.y + "," +
        g.glyphRect.width + "," + g.glyphRect.height + "],\"scale\":" + F(g.scale) + "}";

    void VectorColor(Font font, string key)
    {
        foreach (var size in new[] { 19, 37 })
        {
            var error = FontEngine.LoadFontFace(font, size, 0);
            Value($"{key}/face/{size}", "{\"error\":" + Str(error.ToString()) + ",\"face\":" + Face(FontEngine.GetFaceInfo()) + "}");
            foreach (var character in "AB")
            foreach (var flags in new[] {
                GlyphLoadFlags.LOAD_DEFAULT, GlyphLoadFlags.LOAD_COLOR, GlyphLoadFlags.LOAD_RENDER,
                GlyphLoadFlags.LOAD_RENDER | GlyphLoadFlags.LOAD_COLOR,
            })
            {
                bool found = FontEngine.TryGetGlyphWithUnicodeValue(character, flags, out var glyph);
                Value($"{key}/glyph/{size}/{character}/{flags}", found ? Glyph(glyph) : "null");
            }
            Assets(font, "AB", size, $"{key}/asset/{size}", new[] {
                GlyphRenderMode.DEFAULT, GlyphRenderMode.COLOR, GlyphRenderMode.COLOR_HINTED, GlyphRenderMode.SMOOTH,
            });
        }
    }

    // A font asset per render mode (padding 4, 256 x 256): the atlas format, the face's point
    // size, and each glyph's atlas pixels (RGBA for colour atlases, else alpha).
    void Assets(Font font, string characters, int size, string key, GlyphRenderMode[] modes)
    {
        foreach (var mode in modes)
        {
            var asset = FontAsset.CreateFontAsset(font, size, 4, mode, 256, 256, AtlasPopulationMode.Dynamic, false);
            if (asset == null)
            {
                Value(key + "/" + (int)mode, "null");
                continue;
            }
            if (mode == GlyphRenderMode.DEFAULT)
                Value(key + "/resolved-default-mode", Str(asset.atlasRenderMode.ToString()));
            bool added = asset.TryAddCharacters(characters, out string missing);
            var texture = asset.atlasTextures[0];
            bool rgba = texture.format != TextureFormat.Alpha8;
            var pixels = texture.GetPixels32();
            var sb = new StringBuilder("{\"added\":").Append(added ? "true" : "false").Append(",\"missing\":").Append(Str(missing ?? ""))
                .Append(",\"format\":").Append(Str(texture.format.ToString())).Append(",\"pointSize\":").Append(F(asset.faceInfo.pointSize))
                .Append(",\"glyphs\":[");
            bool first = true;
            foreach (var g in asset.glyphTable)
            {
                var r = g.glyphRect;
                int p = asset.atlasPadding;
                int x = Math.Max(0, r.x - p), y = Math.Max(0, r.y - p);
                int w = Math.Min(texture.width - x, r.width + 2 * p), h = Math.Min(texture.height - y, r.height + 2 * p);
                var bytes = new byte[Math.Max(0, w * h * (rgba ? 4 : 1))];
                int k = 0;
                for (int row = 0; row < h; row++)
                    for (int col = 0; col < w; col++)
                    {
                        var px = pixels[(y + row) * texture.width + x + col];
                        if (rgba)
                        {
                            bytes[k++] = px.r;
                            bytes[k++] = px.g;
                            bytes[k++] = px.b;
                        }
                        bytes[k++] = px.a;
                    }
                sb.Append(first ? "" : ",").Append("{\"glyph\":").Append(Glyph(g)).Append(",\"region\":[").Append(x).Append(',').Append(y).Append(',')
                    .Append(w).Append(',').Append(h).Append("],\"pixels\":").Append(Str(Convert.ToBase64String(bytes))).Append('}');
                first = false;
            }
            Value(key + "/" + (int)mode, sb.Append("]}").ToString());
        }
    }

    // The bitmap font as a legacy dynamic font: request a heart at both sizes and capture
    // its character info and the texture's format and pixels around it.
    void LegacyEmoji(Font emoji, int firstSize = 32, int secondSize = 64, string key = "legacy/emoji")
    {
        emoji.RequestCharactersInTexture("❤", firstSize);
        emoji.RequestCharactersInTexture("❤", secondSize);
        bool heart = emoji.GetCharacterInfo('❤', out var info, firstSize);
        bool large = emoji.GetCharacterInfo('❤', out var info64, secondSize);
        var texture = emoji.material != null ? emoji.material.mainTexture as Texture2D : null;
        var sb = new StringBuilder("{\"heart\":").Append(heart ? Info(info) : "null").Append(",\"heart64\":").Append(large ? Info(info64) : "null")
            .Append(",\"texture\":");
        if (texture == null) sb.Append("null");
        else
        {
            sb.Append("{\"format\":").Append(Str(texture.format.ToString())).Append(",\"size\":[").Append(texture.width).Append(',').Append(texture.height).Append(']');
            if (heart)
            {
                var pixels = Pixels(texture);
                int x0 = Mathf.FloorToInt(Mathf.Min(info.uvBottomLeft.x, info.uvTopRight.x) * texture.width) - 1;
                int x1 = Mathf.CeilToInt(Mathf.Max(info.uvBottomLeft.x, info.uvTopRight.x) * texture.width) + 1;
                int y0 = Mathf.FloorToInt(Mathf.Min(info.uvBottomLeft.y, info.uvTopRight.y) * texture.height) - 1;
                int y1 = Mathf.CeilToInt(Mathf.Max(info.uvBottomLeft.y, info.uvTopRight.y) * texture.height) + 1;
                x0 = Math.Max(0, x0); y0 = Math.Max(0, y0); x1 = Math.Min(texture.width, x1); y1 = Math.Min(texture.height, y1);
                var bytes = new List<byte>();
                for (int y = y0; y < y1; y++)
                    for (int x = x0; x < x1; x++)
                    {
                        var px = pixels[y * texture.width + x];
                        bytes.Add(px.r); bytes.Add(px.g); bytes.Add(px.b); bytes.Add(px.a);
                    }
                sb.Append(",\"region\":[").Append(x0).Append(',').Append(y0).Append(',').Append(x1 - x0).Append(',').Append(y1 - y0)
                    .Append("],\"pixels\":").Append(Str(Convert.ToBase64String(bytes.ToArray())));
            }
            sb.Append('}');
        }
        Value(key, sb.Append('}').ToString());
    }

    static string Info(CharacterInfo c) =>
        "{\"advance\":" + c.advance + ",\"glyph\":[" + c.minX + "," + c.maxX + "," + c.minY + "," + c.maxY + "],\"bearing\":" + c.bearing +
        ",\"size\":" + c.size + ",\"style\":" + Str(c.style.ToString()) + ",\"uv\":[" + F(c.uvBottomLeft.x) + "," + F(c.uvBottomLeft.y) + "," +
        F(c.uvTopRight.x) + "," + F(c.uvTopRight.y) + "],\"flipped\":" + (c.flipped ? "true" : "false") + "}";

    // A font imported with character spacing 0 and 6: character info, and a TextGenerator's layout.
    void Spacing(string key, Font font)
    {
        const string text = "AVWa fig";
        font.RequestCharactersInTexture(text, 32);
        var sb = new StringBuilder("{\"chars\":[");
        bool first = true;
        foreach (var c in "AVWafig ")
        {
            sb.Append(first ? "" : ",").Append(font.GetCharacterInfo(c, out var info, 32) ? Info(info) : "null");
            first = false;
        }
        var gen = new TextGenerator();
        var settings = new TextGenerationSettings
        {
            font = font, fontSize = 32, color = Color.white, lineSpacing = 1, richText = false, scaleFactor = 1,
            fontStyle = FontStyle.Normal, textAnchor = TextAnchor.UpperLeft, alignByGeometry = false, resizeTextForBestFit = false,
            updateBounds = true, verticalOverflow = VerticalWrapMode.Overflow, horizontalOverflow = HorizontalWrapMode.Overflow,
            generationExtents = new Vector2(400, 100), pivot = Vector2.zero, generateOutOfBounds = true,
        };
        Value("spacing/" + key, sb.Append("]}").ToString());
        // The layout of a TextGenerator called directly (pivot 0, upper left, 400 x 100).
        gen.Populate(text, settings);
        var lb = new StringBuilder("{\"preferredWidth\":").Append(F(gen.GetPreferredWidth(text, settings))).Append(",\"quads\":[");
        var verts = gen.verts;
        for (int i = 0; i + 3 < verts.Count; i += 4)
            lb.Append(i == 0 ? "" : ",").Append('[').Append(F(verts[i].position.x)).Append(',').Append(F(verts[i].position.y)).Append(',')
                .Append(F(verts[i + 2].position.x)).Append(',').Append(F(verts[i + 2].position.y)).Append(']');
        Value("spacing/" + key + "/layout", lb.Append("]}").ToString());
    }

    // A screen-space canvas with scale factor 1.5 and a Text of size 20: which sizes the font then
    // holds for its characters, and the text's vertices.
    void ScaledCanvas(Font font)
    {
        var go = new GameObject("Canvas");
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.scaleFactor = 1.5f;
        var textGo = new GameObject("Text");
        textGo.transform.SetParent(go.transform, false);
        var text = textGo.AddComponent<Text>();
        text.font = font;
        text.fontSize = 20;
        text.text = "Hi";
        ((RectTransform)textGo.transform).sizeDelta = new Vector2(100, 40);
        Canvas.ForceUpdateCanvases();
        var sb = new StringBuilder("{");
        foreach (var size in new[] { 20, 30, 40 })
            sb.Append(size == 20 ? "" : ",").Append("\"").Append(size).Append("\":").Append(font.GetCharacterInfo('H', out var info, size) ? "true" : "false");
        if (font.GetCharacterInfo('H', out var held, 30)) Value("canvas/scaled/info", Info(held));
        var verts = text.cachedTextGenerator.verts;
        sb.Append(",\"pixelsPerUnit\":").Append(F(text.pixelsPerUnit)).Append(",\"generatorFontSize\":").Append(text.cachedTextGenerator.fontSizeUsedForBestFit)
            .Append(",\"verts\":[");
        for (int i = 0; i < verts.Count; i++)
            sb.Append(i == 0 ? "" : ",").Append('[').Append(F(verts[i].position.x)).Append(',').Append(F(verts[i].position.y)).Append(']');
        Value("canvas/scaled", sb.Append("]}").ToString());
        DestroyImmediate(go);
    }

    // A UI Toolkit panel (256 x 64) drawing the emoji with a colour font asset, with an SDFAA one and
    // with the font itself.
    void Panel(Font emoji, string dir)
    {
        var target = new RenderTexture(256, 64, 24, RenderTextureFormat.ARGB32);
        target.Create();
        var settings = ScriptableObject.CreateInstance<PanelSettings>();
        settings.targetTexture = target;
        settings.scaleMode = PanelScaleMode.ConstantPixelSize;
        settings.clearColor = true;
        settings.colorClearValue = new Color(0.2f, 0.2f, 0.2f, 1f);
        var go = new GameObject("Panel");
        go.SetActive(false);
        var doc = go.AddComponent<UIDocument>();
        doc.panelSettings = settings;
        go.SetActive(true);
        // Atlases that hold all three glyphs (no fallback to OS fonts, which differ between machines).
        var color = FontAsset.CreateFontAsset(emoji, 24, 4, GlyphRenderMode.COLOR, 512, 512, AtlasPopulationMode.Dynamic, false);
        var sdf = FontAsset.CreateFontAsset(emoji, 24, 4, GlyphRenderMode.SDFAA, 512, 512, AtlasPopulationMode.Dynamic, false);
        int i = 0;
        foreach (var font in new object[] { color, sdf, emoji })
        {
            var label = new Label(Emoji);
            label.style.position = Position.Absolute;
            label.style.left = 4 + 84 * i++;
            label.style.top = 8;
            label.style.fontSize = 24;
            label.style.color = Color.white;
            if (font is FontAsset fa) label.style.unityFontDefinition = FontDefinition.FromSDFFont(fa);
            else label.style.unityFontDefinition = FontDefinition.FromFont((Font)font);
            doc.rootVisualElement.Add(label);
        }
        var t = typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.UIElementsRuntimeUtility");
        t.GetMethod("UpdatePanels", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Invoke(null, null);
        t.GetMethod("UpdatePanels", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Invoke(null, null);
        t.GetMethod("RepaintPanels", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Invoke(null, new object[] { false });
        t.GetMethod("RenderOffscreenPanels", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Invoke(null, null);
        var old = RenderTexture.active;
        RenderTexture.active = target;
        var tex = new Texture2D(256, 64, TextureFormat.RGBA32, false);
        tex.ReadPixels(new UnityEngine.Rect(0, 0, 256, 64), 0, 0);
        tex.Apply();
        RenderTexture.active = old;
        File.WriteAllBytes(Path.Combine(dir, "emoji-panel.png"), tex.EncodeToPNG());
        DestroyImmediate(go);
    }

    // The built-in legacy font driven to its largest texture: 800 characters asked for at size 300 in
    // batches of 100, the texture size and rebuild count after each, then whether the first and last
    // characters are held.
    string FullTexture()
    {
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        int logged = s_Logs.Count;
        int rebuilt = 0;
        Action<Font> onRebuilt = f => { if (f == font) rebuilt++; };
        Font.textureRebuilt += onRebuilt;
        var chars = new StringBuilder();
        for (int c = 0x21; chars.Length < 800 && c < 0x2000; c++)
            if (font.HasCharacter((char)c)) chars.Append((char)c);
        var all = chars.ToString();
        var sb = new StringBuilder("{\"count\":").Append(all.Length).Append(",\"batches\":[");
        for (int b = 0; b * 100 < all.Length; b++)
        {
            font.RequestCharactersInTexture(all.Substring(b * 100, Math.Min(100, all.Length - b * 100)), 300);
            var tex = font.material.mainTexture;
            sb.Append(b == 0 ? "" : ",").Append('[').Append(tex.width).Append(',').Append(tex.height).Append(',').Append(rebuilt).Append(']');
        }
        sb.Append("],\"held\":[");
        foreach (var i in new[] { 0, 99, 400, all.Length - 100, all.Length - 1 })
            sb.Append(i == 0 ? "" : ",").Append(font.GetCharacterInfo(all[i], out var info, 300) ? "true" : "false");
        Font.textureRebuilt -= onRebuilt;
        // The messages logged by these requests.
        sb.Append("],\"logged\":[");
        for (int i = logged; i < s_Logs.Count; i++) sb.Append(i == logged ? "" : ",").Append(Str(s_Logs[i]));
        Value("legacy/full", sb.Append("]}").ToString());
        return all;
    }

    void FullTextureNextFrame(string characters, int elapsedFrames)
    {
        const int size = 300;
        const int newSize = 500;
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        char requested = characters[0];
        const char read = 'm';
        char untouched = characters[400];
        const char newcomer = 'W';
        var texture = font.material.mainTexture;
        int logged = s_Logs.Count;
        font.RequestCharactersInTexture(requested.ToString(), size);
        bool wasPresent = font.GetCharacterInfo(read, out var before, size);

        int rebuilt = 0;
        var callbacks = new List<string>();
        Action<Font> onRebuilt = f => {
            if (f != font) return;
            rebuilt++;
            var current = f.material.mainTexture;
            callbacks.Add("[" + current.width + "," + current.height + "," + Read(f, newcomer, newSize) + "]");
        };
        Font.textureRebuilt += onRebuilt;
        try { font.RequestCharactersInTexture(newcomer.ToString(), newSize); }
        finally { Font.textureRebuilt -= onRebuilt; }
        var after = font.material.mainTexture;
        bool readSurvives = font.GetCharacterInfo(read, out var updated, size);
        Value("legacy/full/next", "{\"frames\":" + elapsedFrames + ",\"sameTexture\":" +
            (texture == after ? "true" : "false") + ",\"size\":[" + after.width + "," + after.height + "]" +
            ",\"rebuilds\":" + rebuilt + ",\"callbacks\":[" + string.Join(",", callbacks) + "]" +
            ",\"requested\":" + Read(font, requested, size) +
            ",\"readBefore\":" + (wasPresent ? Info(before) : "null") +
            ",\"readAfter\":" + (readSurvives ? Info(updated) : "null") +
            ",\"untouched\":" + Read(font, untouched, size) +
            ",\"new\":" + Read(font, newcomer, newSize) + ",\"logs\":[" +
            string.Join(",", s_Logs.GetRange(logged, s_Logs.Count - logged).ConvertAll(Str)) + "]}");
    }

    static string Read(Font font, char character, int size) =>
        font.GetCharacterInfo(character, out var info, size) ? Info(info) : "null";

    // A texture's texels, read back through a copy into a render texture of its colour space.
    static Color32[] Pixels(Texture2D texture)
    {
        var rt = new RenderTexture(texture.width, texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        rt.Create();
        var old = RenderTexture.active;
        Graphics.Blit(texture, rt);
        RenderTexture.active = rt;
        var copy = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false, true);
        copy.ReadPixels(new UnityEngine.Rect(0, 0, texture.width, texture.height), 0, 0);
        copy.Apply();
        RenderTexture.active = old;
        var pixels = copy.GetPixels32();
        DestroyImmediate(copy);
        rt.Release();
        DestroyImmediate(rt);
        return pixels;
    }

    void Value(string key, string json) => m_Out.Append("{\"k\":").Append(Str(key)).Append(",\"v\":").Append(json).Append("}\n");

    static string F(float f) => float.IsNaN(f) || float.IsInfinity(f) ? Str(f.ToString(Inv)) : f.ToString("R", Inv);

    static string Str(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in s)
        {
            if (c == '"' || c == '\\') sb.Append('\\').Append(c);
            else if (c == '\n') sb.Append("\\n");
            else if (c < ' ' || c > 0x7E) sb.Append("\\u").Append(((int)c).ToString("x4"));
            else sb.Append(c);
        }
        return sb.Append('"').ToString();
    }
}
