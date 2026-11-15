using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// VectorImage assets imported by com.unity.vectorgraphics (svgType UIToolkit) and drawn by UI
/// Toolkit: each asset's geometry, gradient settings and atlas, then a runtime panel rendered into a
/// render texture at scale 1 and 1.25 with the images as backgrounds (scale modes, size, repeat,
/// position, slices, tint, radius, opacity, a mask), as Image elements (scale modes, uv, tint) and
/// through MeshGenerationContext.DrawVectorImage. Writes values.jsonl and PNGs to $UITK_VECTOR_OUT.
/// Synchronous (the Editor in batch mode never reaches the end of a frame): panels are updated and
/// rendered through UIElementsRuntimeUtility.
/// </summary>
public class VectorProbe : MonoBehaviour
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static readonly string[] Assets = { "shapes", "strokes", "gradients", "clip", "frame" };
    const int Width = 1024, Height = 640, Columns = 8;

    static readonly List<string> s_Logs = new List<string>();
    // What GUIUtility.AlignRectToDevice gives inside a panel repaint, per content generation.
    static readonly List<string> s_Aligned = new List<string>();

    struct Case
    {
        public string name;
        public Func<VisualElement> make;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("UITK_VECTOR_OUT"))) return;
        Application.logMessageReceived += (message, stack, type) => { if (type != LogType.Log) s_Logs.Add(type + ": " + message); };
        new GameObject("VectorProbeRunner").AddComponent<VectorProbe>();
    }

    // The probe runs in the first frame's Start, after the graphics device rendered once.
    void Start()
    {
        var dir = Environment.GetEnvironmentVariable("UITK_VECTOR_OUT");
        var output = new StringBuilder();
        try
        {
            Record(dir, output);
        }
        catch (Exception e)
        {
            Value(output, "error", Str(e.ToString()));
        }
        var logs = new List<string>(s_Logs);
        var imported = Path.Combine(dir, "import-logs.txt");
        if (File.Exists(imported)) logs.AddRange(File.ReadAllLines(imported));
        logs.Sort(StringComparer.Ordinal);
        var sb = new StringBuilder("[");
        for (int i = 0; i < logs.Count; i++) sb.Append(i == 0 ? "" : ",").Append(Str(logs[i]));
        Value(output, "logs", sb.Append("]").ToString());
        VisualControls(dir, output);
        AtlasControls(dir, output);
        output.Append("{\"k\":\"complete\",\"v\":true}\n");
        File.WriteAllText(Path.Combine(dir, "values.jsonl"), output.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    static void Record(string dir, StringBuilder output)
    {
        var images = new Dictionary<string, VectorImage>();
        foreach (var name in Assets)
        {
            var vi = Resources.Load<VectorImage>("Vector/" + name);
            images[name] = vi;
            Asset(output, name, vi);
        }
        var painted = Painted();
        Asset(output, "painted", painted);
        images["painted"] = painted;

        var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
        target.Create();
        var settings = ScriptableObject.CreateInstance<PanelSettings>();
        settings.targetTexture = target;
        settings.scaleMode = PanelScaleMode.ConstantPixelSize;
        settings.scale = 1;
        settings.clearColor = true;
        settings.colorClearValue = new Color(0.1f, 0.1f, 0.1f, 1);
        var go = new GameObject("VectorProbe");
        go.SetActive(false);
        var document = go.AddComponent<UIDocument>();
        document.panelSettings = settings;
        go.SetActive(true);
        var root = document.rootVisualElement;
        root.styleSheets.Add(Resources.Load<StyleSheet>("VectorProbe"));

        var cases = Cases(images);
        for (int i = 0; i < cases.Count; i++)
        {
            var e = cases[i].make();
            e.name = cases[i].name;
            e.style.position = Position.Absolute;
            e.style.left = 8 + (i % Columns) * 98 + (i % 3 == 1 ? 0.5f : 0f);
            e.style.top = 8 + (i / Columns) * 72;
            if (e.style.width.keyword == StyleKeyword.Null) e.style.width = 90;
            if (e.style.height.keyword == StyleKeyword.Null) e.style.height = 60;
            root.Add(e);
        }
        Api(output, images);
        foreach (float scale in new[] { 1f, 1.25f })
        {
            settings.scale = scale;
            Update(); Update();
            Render(target, Path.Combine(dir, "vector-" + (scale == 1 ? "1" : "1.25") + ".png"));
            Value(output, "align-content/" + (scale == 1 ? "1" : "1.25"), "[" + string.Join(",", s_Aligned) + "]");
            s_Aligned.Clear();
        }
        for (int i = 0; i < cases.Count; i++)
        {
            var e = root.Q(cases[i].name);
            Value(output, "layout/" + cases[i].name, Rect(e.layout));
        }
    }

    static void VisualControls(string dir, StringBuilder output)
    {
        var tree = Resources.Load<VisualTreeAsset>("VectorRefs").CloneTree();
        var direct = tree.Q<Image>("direct");
        var relative = tree.Q<Image>("relative");
        var button = tree.Q<Button>("icon");
        var source = Resources.Load<VectorImage>("Vector/shapes");
        var target = new RenderTexture(320, 80, 24, RenderTextureFormat.ARGB32);
        target.Create();
        var settings = ScriptableObject.CreateInstance<PanelSettings>();
        settings.targetTexture = target;
        settings.scaleMode = PanelScaleMode.ConstantPixelSize;
        settings.clearColor = true;
        settings.colorClearValue = Color.black;
        var go = new GameObject("VectorControls");
        go.SetActive(false);
        var doc = go.AddComponent<UIDocument>();
        doc.panelSettings = settings;
        go.SetActive(true);
        var root = doc.rootVisualElement;
        var vi = ScriptableObject.CreateInstance<VectorImage>();
        void Paint(bool flipped)
        {
            var painter = new Painter2D();
            painter.fillColor = Color.red;
            painter.BeginPath();
            painter.MoveTo(new Vector2(flipped ? 64 : 0, flipped ? 64 : 0));
            painter.LineTo(new Vector2(64, 0));
            painter.LineTo(new Vector2(0, 64));
            painter.ClosePath();
            painter.Fill();
            painter.SaveToVectorImage(vi);
            painter.Dispose();
        }
        Paint(false);
        var mask = new VisualElement();
        mask.style.position = Position.Absolute;
        mask.style.left = 0; mask.style.top = 0; mask.style.width = 64; mask.style.height = 64;
        mask.style.backgroundImage = Background.FromVectorImage(vi);
        mask.style.unityBackgroundScaleMode = ScaleMode.StretchToFill;
        mask.style.overflow = Overflow.Hidden;
        var child = new VisualElement();
        child.style.width = 64; child.style.height = 64; child.style.backgroundColor = Color.green;
        mask.Add(child); root.Add(mask);
        tree.style.position = Position.Absolute;
        tree.style.left = 80; tree.style.top = 0; tree.style.width = 240; tree.style.height = 80;
        int index = 0;
        foreach (var element in new VisualElement[] { direct, relative, button })
        {
            element.style.position = Position.Absolute;
            element.style.left = index++ * 72;
            element.style.top = 0; element.style.width = 64; element.style.height = 64;
        }
        root.Add(tree);
        Update(); Update();
        Value(output, "uxml/vector-references", "[" + (direct.vectorImage == source ? "true" : "false") + "," +
            (relative.vectorImage == source ? "true" : "false") + "," + (button.iconImage.vectorImage == source ? "true" : "false") + "]");
        Render(target, Path.Combine(dir, "vector-control-before.png"));
        Paint(true);
        Update();
        Render(target, Path.Combine(dir, "vector-control-unmarked.png"));
        mask.MarkDirtyRepaint();
        Update();
        Render(target, Path.Combine(dir, "vector-control-repaint.png"));
        mask.style.overflow = Overflow.Visible;
        Update();
        Render(target, Path.Combine(dir, "vector-control-visible-overflow.png"));
        Paint(false);
        mask.style.overflow = Overflow.Hidden;
        Update();
        Render(target, Path.Combine(dir, "vector-control-reclip.png"));
        mask.style.visibility = Visibility.Hidden;
        child.style.visibility = Visibility.Visible;
        Update();
        Render(target, Path.Combine(dir, "vector-control-hidden-parent.png"));
        mask.style.display = DisplayStyle.None;
        tree.style.display = DisplayStyle.None;
        var holder = new VisualElement();
        holder.style.position = Position.Absolute;
        holder.style.width = 100; holder.style.height = 80;
        root.Add(holder);
        var rounded = new VisualElement[2];
        for (int i = 0; i < rounded.Length; i++)
        {
            var e = new VisualElement();
            e.style.position = Position.Absolute;
            e.style.left = i * 80; e.style.top = 0;
            e.style.width = 64; e.style.height = 64;
            e.style.backgroundColor = Color.red;
            e.style.overflow = Overflow.Hidden;
            e.style.unityOverflowClipBox = i == 0 ? OverflowClipBox.ContentBox : OverflowClipBox.PaddingBox;
            e.style.borderTopLeftRadius = 12; e.style.borderTopRightRadius = 12;
            e.style.borderBottomLeftRadius = 12; e.style.borderBottomRightRadius = 12;
            e.style.paddingLeft = Length.Percent(10); e.style.paddingRight = Length.Percent(10);
            e.style.paddingTop = Length.Percent(10); e.style.paddingBottom = Length.Percent(10);
            var fill = new VisualElement();
            fill.style.position = Position.Absolute;
            fill.style.left = -100; fill.style.top = -100;
            fill.style.width = 300; fill.style.height = 300;
            fill.style.backgroundColor = Color.green;
            e.Add(fill); holder.Add(e); rounded[i] = e;
        }
        void PaddingSnapshot(string stage)
        {
            Update(); Update();
            var e = rounded[0];
            Value(output, "padding/" + stage, "{\"layout\":" + Rect(e.layout) + ",\"content\":" + Rect(e.contentRect) + "}");
            Render(target, Path.Combine(dir, "vector-control-padding-" + stage + ".png"));
        }
        PaddingSnapshot("before");
        holder.style.width = 140;
        PaddingSnapshot("percent");
        foreach (var e in rounded)
        {
            e.style.paddingLeft = 20; e.style.paddingRight = 20;
            e.style.paddingTop = 20; e.style.paddingBottom = 20;
        }
        PaddingSnapshot("after");
        UnityEngine.Object.DestroyImmediate(go);
        UnityEngine.Object.DestroyImmediate(settings);
        target.Release();
        UnityEngine.Object.DestroyImmediate(target);
        UnityEngine.Object.DestroyImmediate(vi);
    }

    static void AtlasControls(string dir, StringBuilder output)
    {
        var target = new RenderTexture(320, 192, 24, RenderTextureFormat.ARGB32);
        target.Create();
        var settings = ScriptableObject.CreateInstance<PanelSettings>();
        var atlas = DynamicAtlasSettings.defaults;
        atlas.maxAtlasSize = 256;
        atlas.activeFilters &= ~DynamicAtlasFilters.Readability;
        settings.dynamicAtlasSettings = atlas;
        settings.targetTexture = target;
        settings.scaleMode = PanelScaleMode.ConstantPixelSize;
        settings.clearColor = true;
        settings.colorClearValue = new Color(0.1f, 0.1f, 0.1f, 1);
        var go = new GameObject("AtlasControls"); go.SetActive(false);
        var doc = go.AddComponent<UIDocument>(); doc.panelSettings = settings; go.SetActive(true);
        var root = doc.rootVisualElement;
        var textures = new List<Texture2D>();
        var elements = new List<VisualElement>();
        Texture2D Make(int w, int h, FilterMode filter, TextureWrapMode wrap, string name)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = name, filterMode = filter, wrapMode = wrap };
            var pixels = new Color32[w*h];
            for (int y=0;y<h;y++) for(int x=0;x<w;x++) pixels[y*w+x]=new Color32((byte)(x*255/Math.Max(1,w-1)),(byte)(y*255/Math.Max(1,h-1)),(byte)(255-x*127/Math.Max(1,w-1)),255);
            t.SetPixels32(pixels); t.Apply(false,false); textures.Add(t);return t;
        }
        VisualElement Add(Texture2D t,int x,int y)
        {
            var e=new Image { image=t, scaleMode=ScaleMode.StretchToFill };
            e.style.position=Position.Absolute;e.style.left=x;e.style.top=y;e.style.width=53.5f;e.style.height=29.5f;
            root.Add(e);elements.Add(e);return e;
        }
        void Shot(string stage)
        {
            Update();Update(); Render(target,Path.Combine(dir,"atlas-"+stage+".png"));
        }
        var a=Make(5,3,FilterMode.Bilinear,TextureWrapMode.Clamp,"control-a");
        var b=Make(17,9,FilterMode.Point,TextureWrapMode.Repeat,"control-b");
        var c=Make(5,3,FilterMode.Bilinear,TextureWrapMode.Repeat,"control-c");
        var first=Add(a,8,8); var shared=Add(a,70,8);Add(b,132,8);Add(c,194,8);
        int generations=0;first.generateVisualContent+=ctx=>generations++;
        Shot("initial");
        Value(output,"atlas/generation-initial",generations.ToString(Inv));
        var red=new Color32[a.width*a.height];for(int i=0;i<red.Length;i++)red[i]=new Color32(255,0,0,255);
        a.SetPixels32(red);a.Apply(false,false);
        Shot("unmarked");
        root.panel.SetTextureDirty(a);
        Shot("dirty");
        Value(output,"atlas/generation-dirty",generations.ToString(Inv));
        for(int i=0;i<10;i++)Add(Make(60,31,FilterMode.Bilinear,TextureWrapMode.Clamp,"growth-"+i),8+(i%5)*62,52+(i/5)*36);
        Shot("growth");
        first.RemoveFromHierarchy();Shot("one-user");
        shared.RemoveFromHierarchy();Shot("no-user");
        var replacement=Add(Make(5,3,FilterMode.Bilinear,TextureWrapMode.Clamp,"replacement"),8,8);
        Shot("reuse");
        foreach(var e in elements) if(e!=replacement)e.RemoveFromHierarchy();
        int beforeReset=generations;
        root.Add(first);
        Shot("readd");
        Value(output,"atlas/generation-readd",(generations-beforeReset).ToString(Inv));
        int before=generations;
        root.panel.ResetDynamicAtlas();
        Shot("reset");
        Value(output,"atlas/generation-reset",(generations-before).ToString(Inv));
        settings.dynamicAtlasSettings.customFilter=(Texture2D texture,ref DynamicAtlasFilters filters)=>texture!=a;
        Shot("filter");
        root.panel.SetTextureDirty(null);root.panel.SetTextureDirty(b);
        Shot("absent-dirty");
        root.Clear();
        settings.dynamicAtlasSettings.customFilter=null;
        int resizeGenerations=0;
        var resized0=Add(a,8,8);Add(a,70,8);
        resized0.generateVisualContent+=ctx=>resizeGenerations++;
        Shot("resize-before");
        Value(output,"atlas/resize-generation-before",resizeGenerations.ToString(Inv));
        a.Reinitialize(9,7);
        var green=new Color32[9*7];
        for(int y=0;y<7;y++)for(int x=0;x<9;x++)green[y*9+x]=new Color32((byte)(x*25),(byte)(y*35),127,255);
        a.SetPixels32(green);a.Apply(false,false);
        Shot("resize-unmarked");
        Value(output,"atlas/resize-generation-unmarked",resizeGenerations.ToString(Inv));
        resized0.MarkDirtyRepaint();
        Shot("resize-repaint");
        Value(output,"atlas/resize-generation-repaint",resizeGenerations.ToString(Inv));
        root.panel.SetTextureDirty(a);
        Shot("resize-dirty");
        Value(output,"atlas/resize-generation-dirty",resizeGenerations.ToString(Inv));
        root.Clear();
        settings.dynamicAtlasSettings.activeFilters=DynamicAtlasFilters.None;
        var font=Resources.Load<Font>("AtlasControl");
        var fontAsset=UnityEngine.TextCore.Text.FontAsset.CreateFontAsset(font,32,0,UnityEngine.TextCore.LowLevel.GlyphRenderMode.RASTER,128,128);
        var label=new TextElement { text="AV" };
        label.style.position=Position.Absolute;label.style.left=12;label.style.top=12;label.style.width=200;label.style.height=64;
        label.style.fontSize=32;label.style.color=Color.white;
        label.style.unityFontDefinition=FontDefinition.FromSDFFont(fontAsset);
        root.Add(label);Add(a,225,8);
        Shot("bitmap-font-initial");
        Value(output,"atlas/bitmap-initial","["+fontAsset.glyphTable.Count+","+fontAsset.atlasTexture.width+","+fontAsset.atlasTexture.height+","+(int)fontAsset.atlasRenderMode+"]");
        label.text="AVa";
        Shot("bitmap-font-added");
        Value(output,"atlas/bitmap-added","["+fontAsset.glyphTable.Count+","+fontAsset.atlasTexture.width+","+fontAsset.atlasTexture.height+","+(int)fontAsset.atlasRenderMode+"]");
        UnityEngine.Object.DestroyImmediate(fontAsset);
        UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(settings);
        target.Release();UnityEngine.Object.DestroyImmediate(target);
        foreach(var t in textures)UnityEngine.Object.DestroyImmediate(t);
    }

    static List<Case> Cases(Dictionary<string, VectorImage> vi)
    {
        var list = new List<Case>();
        void Bg(string name, VectorImage image, Action<IStyle> style)
        {
            list.Add(new Case { name = name, make = () => { var e = new VisualElement(); e.style.backgroundImage = new StyleBackground(image); style?.Invoke(e.style); return e; } });
        }
        foreach (var svg in new[] { "shapes", "strokes", "gradients", "clip" })
        {
            Bg("bg-" + svg + "-stretch", vi[svg], s => s.unityBackgroundScaleMode = ScaleMode.StretchToFill);
            Bg("bg-" + svg + "-crop", vi[svg], s => s.unityBackgroundScaleMode = ScaleMode.ScaleAndCrop);
            Bg("bg-" + svg + "-fit", vi[svg], s => s.unityBackgroundScaleMode = ScaleMode.ScaleToFit);
        }
        Bg("bg-shapes-repeat", vi["shapes"], s => { s.backgroundSize = new BackgroundSize(new Length(28), new Length(14)); s.backgroundRepeat = new BackgroundRepeat(Repeat.Repeat, Repeat.Repeat); });
        Bg("bg-shapes-repeat-center", vi["shapes"], s => { s.backgroundSize = new BackgroundSize(new Length(28), new Length(14)); s.backgroundRepeat = new BackgroundRepeat(Repeat.Repeat, Repeat.Repeat); s.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Center); s.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Center); });
        Bg("bg-gradients-cover", vi["gradients"], s => s.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover));
        Bg("bg-gradients-contain", vi["gradients"], s => s.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain));
        Bg("bg-shapes-right-bottom", vi["shapes"], s => { s.backgroundSize = new BackgroundSize(new Length(40), new Length(20)); s.backgroundRepeat = new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat); s.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Right, new Length(4)); s.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Bottom, new Length(4)); });
        Bg("bg-strokes-space", vi["strokes"], s => { s.backgroundSize = new BackgroundSize(new Length(24), new Length(24)); s.backgroundRepeat = new BackgroundRepeat(Repeat.Space, Repeat.Round); });
        Bg("bg-clip-tint", vi["clip"], s => s.unityBackgroundImageTintColor = new Color(1f, 0.5f, 0.25f, 0.75f));
        Bg("bg-shapes-radius-border", vi["shapes"], s =>
        {
            s.borderTopLeftRadius = 14; s.borderTopRightRadius = 6; s.borderBottomRightRadius = 20; s.borderBottomLeftRadius = 0;
            s.borderLeftWidth = 3; s.borderTopWidth = 3; s.borderRightWidth = 3; s.borderBottomWidth = 3;
            s.borderLeftColor = Color.white; s.borderTopColor = Color.white; s.borderRightColor = Color.white; s.borderBottomColor = Color.white;
        });
        Bg("bg-gradients-opacity", vi["gradients"], s => s.opacity = 0.5f);
        Bg("bg-gradients-color", vi["gradients"], s => { s.backgroundColor = new Color(0.2f, 0.6f, 0.2f, 1f); s.unityBackgroundScaleMode = ScaleMode.ScaleToFit; });
        Bg("bg-frame-slice", vi["frame"], s => { s.unitySliceLeft = 9; s.unitySliceTop = 9; s.unitySliceRight = 9; s.unitySliceBottom = 9; });
        Bg("bg-frame-slice-uneven", vi["frame"], s => { s.unitySliceLeft = 4; s.unitySliceTop = 10; s.unitySliceRight = 12; s.unitySliceBottom = 6; s.unitySliceScale = 2f; });
        Bg("bg-frame-slice-small", vi["frame"], s => { s.unitySliceLeft = 9; s.unitySliceTop = 9; s.unitySliceRight = 9; s.unitySliceBottom = 9; s.width = 14; s.height = 12; });
        Bg("bg-frame-slice-fit", vi["frame"], s => { s.unitySliceLeft = 9; s.unitySliceTop = 9; s.unitySliceRight = 9; s.unitySliceBottom = 9; s.unityBackgroundScaleMode = ScaleMode.ScaleToFit; });
        Bg("bg-painted", vi["painted"], null);
        Bg("bg-painted-fit", vi["painted"], s => s.unityBackgroundScaleMode = ScaleMode.ScaleToFit);
        Bg("bg-clip-rotated", vi["clip"], s => { s.rotate = new Rotate(new Angle(20f)); s.scale = new Scale(new Vector2(0.8f, 0.8f)); });
        list.Add(new Case { name = "bg-uss-shapes", make = () => { var e = new VisualElement(); e.AddToClassList("uss-shapes"); return e; } });
        // A vector background on an element that clips its children: the image is the mask.
        list.Add(new Case { name = "bg-clip-mask", make = () =>
        {
            var e = new VisualElement();
            e.style.backgroundImage = new StyleBackground(vi["clip"]);
            e.style.overflow = Overflow.Hidden;
            var child = new VisualElement();
            child.style.position = Position.Absolute;
            child.style.left = -10; child.style.top = 10; child.style.width = 110; child.style.height = 20;
            child.style.backgroundColor = new Color(0.1f, 1f, 0.1f, 1f);
            e.Add(child);
            return e;
        } });
        void Img(string name, VectorImage image, ScaleMode mode, Action<Image> setup = null)
        {
            list.Add(new Case { name = name, make = () => { var e = new Image { vectorImage = image, scaleMode = mode }; setup?.Invoke(e); return e; } });
        }
        foreach (var svg in new[] { "shapes", "gradients" })
        {
            Img("img-" + svg + "-stretch", vi[svg], ScaleMode.StretchToFill);
            Img("img-" + svg + "-crop", vi[svg], ScaleMode.ScaleAndCrop);
            Img("img-" + svg + "-fit", vi[svg], ScaleMode.ScaleToFit);
        }
        Img("img-clip-crop-tall", vi["clip"], ScaleMode.ScaleAndCrop, e => { e.style.width = 30; e.style.height = 60; });
        Img("img-shapes-uv", vi["shapes"], ScaleMode.StretchToFill, e => e.uv = new UnityEngine.Rect(0.25f, 0.25f, 0.5f, 0.5f));
        Img("img-gradients-uv-fit", vi["gradients"], ScaleMode.ScaleToFit, e => e.uv = new UnityEngine.Rect(0.1f, 0.5f, 0.8f, 0.4f));
        Img("img-strokes-tint", vi["strokes"], ScaleMode.ScaleToFit, e => e.tintColor = new Color(0.5f, 1f, 1f, 0.5f));
        Img("img-frame-padding", vi["frame"], ScaleMode.StretchToFill, e => { e.style.paddingLeft = 10; e.style.paddingTop = 4; e.style.backgroundColor = new Color(0.3f, 0.3f, 0.6f, 1f); });
        // More than 255 gradient settings rows on one panel: 60 copies of gradients.svg (5 rows
        // each) after the images above.
        list.Add(new Case { name = "gradient-rows", make = () =>
        {
            var e = new VisualElement();
            for (int k = 0; k < 60; k++)
            {
                var copy = new VisualElement();
                copy.style.position = Position.Absolute;
                copy.style.left = (k % 10) * 9;
                copy.style.top = (k / 10) * 10;
                copy.style.width = 9;
                copy.style.height = 10;
                copy.style.backgroundImage = new StyleBackground(UnityEngine.Object.Instantiate(vi["gradients"]));
                e.Add(copy);
            }
            return e;
        } });
        // Content generation at a fractional position and size: the aligned content rect.
        list.Add(new Case { name = "align-content", make = () =>
        {
            var e = new VisualElement();
            e.style.width = 50.3f;
            e.style.height = 30.7f;
            e.generateVisualContent += ctx => s_Aligned.Add(Rect(GUIUtility.AlignRectToDevice(ctx.visualElement.contentRect)));
            return e;
        } });
        list.Add(new Case { name = "draw-vector-image", make = () =>
        {
            var e = new VisualElement();
            e.generateVisualContent += ctx =>
            {
                ctx.DrawVectorImage(vi["gradients"], new Vector2(4, 4), Angle.Degrees(0), new Vector2(0.75f, 0.75f));
                ctx.DrawVectorImage(vi["shapes"], new Vector2(86, 10), Angle.Degrees(15), new Vector2(-0.5f, 0.75f));
                ctx.DrawVectorImage(vi["strokes"], new Vector2(40, 30), Angle.Degrees(-30), new Vector2(0.5f, -0.5f));
            };
            return e;
        } });
        return list;
    }

    // A VectorImage made by a detached Painter2D (no import): a filled polygon, a stroked half
    // circle (arc anti-aliasing data) and a line.
    static VectorImage Painted()
    {
        var p = new Painter2D();
        p.fillColor = new Color(0.25f, 0.75f, 1f, 1f);
        p.strokeColor = new Color(1f, 0.25f, 0.5f, 1f);
        p.lineWidth = 4;
        p.BeginPath();
        p.MoveTo(new Vector2(12, 4));
        p.LineTo(new Vector2(36, 4));
        p.LineTo(new Vector2(30, 28));
        p.LineTo(new Vector2(6, 22));
        p.ClosePath();
        p.Fill();
        p.BeginPath();
        p.Arc(new Vector2(24, 16), 12, 180, 360);
        p.Stroke();
        p.BeginPath();
        p.MoveTo(new Vector2(40, 4));
        p.LineTo(new Vector2(60, 28));
        p.Stroke();
        var vi = ScriptableObject.CreateInstance<VectorImage>();
        p.SaveToVectorImage(vi);
        p.Dispose();
        return vi;
    }

    static void Api(StringBuilder output, Dictionary<string, VectorImage> vi)
    {
        var image = new Image { image = Texture2D.whiteTexture };
        image.vectorImage = vi["shapes"];
        Value(output, "api/image-vector-clears-texture", "{\"image\":" + (image.image == null ? "null" : "\"set\"") + ",\"vector\":" + Str(image.vectorImage != null ? image.vectorImage.name : "null") + "}");
        var background = Background.FromVectorImage(vi["clip"]);
        Value(output, "api/background-from-vector", "{\"vector\":" + Str(background.vectorImage.name) + ",\"asset\":" + Str(background.GetSelectedImage().GetType().Name) + "}");
        var loaded = Resources.LoadAll("Vector/gradients");
        var names = new List<string>();
        foreach (var o in loaded) names.Add(o.GetType().Name + ":" + o.name);
        names.Sort(StringComparer.Ordinal);
        var sb = new StringBuilder("[");
        for (int i = 0; i < names.Count; i++) sb.Append(i == 0 ? "" : ",").Append(Str(names[i]));
        Value(output, "api/load-all-gradients", sb.Append("]").ToString());
    }

    // An asset's size, geometry, gradient settings and atlas (internal fields, read by reflection).
    static void Asset(StringBuilder output, string name, VectorImage vi)
    {
        if (vi == null)
        {
            Value(output, "asset/" + name, "null");
            return;
        }
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        var t = typeof(VectorImage);
        var vertices = (Array)t.GetField("vertices", Any).GetValue(vi);
        var indices = (ushort[])t.GetField("indices", Any).GetValue(vi);
        var settings = (Array)t.GetField("settings", Any).GetValue(vi);
        var atlas = (Texture2D)t.GetField("atlas", Any).GetValue(vi);
        var sb = new StringBuilder();
        sb.Append("{\"name\":").Append(Str(vi.name)).Append(",\"width\":").Append(F(vi.width)).Append(",\"height\":").Append(F(vi.height));
        sb.Append(",\"vertices\":").Append(vertices.Length).Append(",\"indices\":").Append(indices.Length);
        sb.Append(",\"settings\":[");
        for (int i = 0; settings != null && i < settings.Length; i++)
        {
            var s = settings.GetValue(i);
            var st = s.GetType();
            var loc = (RectInt)st.GetField("location", Any).GetValue(s);
            var focus = (Vector2)st.GetField("radialFocus", Any).GetValue(s);
            sb.Append(i == 0 ? "" : ",").Append("{\"type\":").Append(Convert.ToInt32(st.GetField("gradientType", Any).GetValue(s)))
                .Append(",\"address\":").Append(Convert.ToInt32(st.GetField("addressMode", Any).GetValue(s)))
                .Append(",\"focus\":[").Append(F(focus.x)).Append(",").Append(F(focus.y)).Append("]")
                .Append(",\"location\":[").Append(loc.x).Append(",").Append(loc.y).Append(",").Append(loc.width).Append(",").Append(loc.height).Append("]}");
        }
        sb.Append("],\"atlas\":");
        if (atlas == null) sb.Append("null");
        else
        {
            sb.Append("{\"name\":").Append(Str(atlas.name)).Append(",\"width\":").Append(atlas.width).Append(",\"height\":").Append(atlas.height)
                .Append(",\"format\":").Append(Str(atlas.format.ToString())).Append(",\"filter\":").Append(Str(atlas.filterMode.ToString()))
                .Append(",\"wrap\":").Append(Str(atlas.wrapMode.ToString())).Append(",\"mips\":").Append(atlas.mipmapCount)
                .Append(",\"readable\":").Append(atlas.isReadable ? "true" : "false").Append(",\"pixels\":\"");
            foreach (var c in Pixels(atlas)) sb.Append(c.r.ToString("x2")).Append(c.g.ToString("x2")).Append(c.b.ToString("x2")).Append(c.a.ToString("x2"));
            sb.Append("\"}");
        }
        sb.Append("}");
        Value(output, "asset/" + name, sb.ToString());
        // Every vertex: position, tint, uv, setting index; then the indices.
        var vb = new StringBuilder("[");
        for (int i = 0; i < vertices.Length; i++)
        {
            var v = vertices.GetValue(i);
            var vt = v.GetType();
            var pos = (Vector3)vt.GetField("position", Any).GetValue(v);
            var tint = (Color32)vt.GetField("tint", Any).GetValue(v);
            var uv = (Vector2)vt.GetField("uv", Any).GetValue(v);
            var setting = Convert.ToUInt32(vt.GetField("settingIndex", Any).GetValue(v));
            vb.Append(i == 0 ? "[" : ",[").Append(F(pos.x)).Append(",").Append(F(pos.y)).Append(",").Append(tint.r).Append(",").Append(tint.g).Append(",")
                .Append(tint.b).Append(",").Append(tint.a).Append(",").Append(F(uv.x)).Append(",").Append(F(uv.y)).Append(",").Append(setting).Append("]");
        }
        Value(output, "asset/" + name + "/vertices", vb.Append("]").ToString());
        var ib = new StringBuilder("[");
        for (int i = 0; i < indices.Length; i++) ib.Append(i == 0 ? "" : ",").Append(indices[i]);
        Value(output, "asset/" + name + "/indices", ib.Append("]").ToString());
    }

    // A texture's texels, read back through a copy into a render texture of its colour space
    // (the importer's atlas is not readable on the CPU).
    static Color32[] Pixels(Texture2D texture)
    {
        var rt = new RenderTexture(texture.width, texture.height, 0, RenderTextureFormat.ARGB32,
            texture.isDataSRGB ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear);
        rt.Create();
        var old = RenderTexture.active;
        Graphics.Blit(texture, rt);
        RenderTexture.active = rt;
        var copy = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false, !texture.isDataSRGB);
        copy.ReadPixels(new UnityEngine.Rect(0, 0, texture.width, texture.height), 0, 0);
        copy.Apply();
        RenderTexture.active = old;
        var pixels = copy.GetPixels32();
        UnityEngine.Object.DestroyImmediate(copy);
        rt.Release();
        UnityEngine.Object.DestroyImmediate(rt);
        return pixels;
    }

    static void Update()
    {
        var t = typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.UIElementsRuntimeUtility");
        t.GetMethod("UpdatePanels", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Invoke(null, null);
    }

    static void Render(RenderTexture target, string path)
    {
        var t = typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.UIElementsRuntimeUtility");
        t.GetMethod("RepaintPanels", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Invoke(null, new object[] { false });
        t.GetMethod("RenderOffscreenPanels", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Invoke(null, null);
        var old = RenderTexture.active;
        RenderTexture.active = target;
        var tex = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        tex.ReadPixels(new UnityEngine.Rect(0, 0, target.width, target.height), 0, 0);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
        RenderTexture.active = old;
    }

    static void Value(StringBuilder output, string key, string json) => output.Append("{\"k\":").Append(Str(key)).Append(",\"v\":").Append(json).Append("}\n");

    static string Rect(UnityEngine.Rect r) => "[" + F(r.x) + "," + F(r.y) + "," + F(r.width) + "," + F(r.height) + "]";

    static string F(float f) => float.IsNaN(f) || float.IsInfinity(f) ? Str(f.ToString(Inv)) : f.ToString("R", Inv);

    static string Str(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in s)
        {
            if (c == '"' || c == '\\') sb.Append('\\').Append(c);
            else if (c == '\n') sb.Append("\\n");
            else if (c == '\r') sb.Append("\\r");
            else if (c == '\t') sb.Append("\\t");
            else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
            else sb.Append(c);
        }
        return sb.Append('"').ToString();
    }
}
