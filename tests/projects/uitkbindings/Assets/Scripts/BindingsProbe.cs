using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>A game binding declared in UXML (`<ProbeCustomBinding prefix="..."/>`): writes the
/// prefix, the resolved source's message and the resolved path into a label.</summary>
[UxmlObject]
public partial class ProbeCustomBinding : CustomBinding
{
    [UxmlAttribute]
    public string prefix { get; set; }

    protected override BindingResult Update(in BindingContext context)
    {
        if (context.targetElement is Label label)
            label.text = prefix + (context.dataSource is BindData data ? data.message : "no source") + " path=" + context.dataSourcePath;
        return new BindingResult(BindingStatus.Success);
    }
}

/// <summary>Counts its updates (every update).</summary>
public class CountingBinding : CustomBinding
{
    public int count;

    public CountingBinding() => updateTrigger = BindingUpdateTrigger.EveryUpdate;

    protected override BindingResult Update(in BindingContext context)
    {
        count++;
        return new BindingResult(BindingStatus.Success);
    }
}

/// <summary>A plain data source for bindings to style properties made in code.</summary>
public class StyleSource
{
    public float width = 30;
    public int count = 7;
    public Length length = new Length(50, LengthUnit.Percent);
    public StyleKeyword keyword = StyleKeyword.Auto;
    public string text = "12px";
    public Color color = Color.blue;
    public Align align = Align.Center;
    public float back = -1;
    public float resolved = -1;
}

/// <summary>A plain data source for bindings into nested style values.</summary>
public class NestedSource
{
    public float number = 12;
    public int whole = 9;
    public Length length = new Length(40, LengthUnit.Percent);
    public Angle angle = new Angle(0.25f, AngleUnit.Turn);
    public Rotate rotate = new Rotate(new Angle(45, AngleUnit.Degree));
    public Vector3 vector = new Vector3(2, 3, 1);
    public Vector2 vector2 = new Vector2(5, 6);
    public Color color = new Color(0.25f, 0.5f, 0.75f, 1);
    public Length backLength = new Length(-1, LengthUnit.Pixel);
    public float backNumber = -1;
    public float backAngle = -1;
    public float backResolved = -1;
}

/// <summary>
/// Bindings declared in UXML (`<Bindings>` of DataBinding and a game [UxmlObject] binding) and
/// bindings to style properties (`style.*`, `resolvedStyle.*`) on a runtime panel. Clones the probe
/// documents (one main document, three with malformed bindings), then steps: each step changes
/// sources or fields and runs two frames (panel update, then repaint: bindings update on repaint).
/// Records the elements' texts, values, styles and binding results, the bindings' descriptions,
/// the sources, and the import and clone logs. Writes values.jsonl to $UITK_BINDINGS_OUT.
/// </summary>
public class BindingsProbe : MonoBehaviour
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    readonly StringBuilder m_Out = new StringBuilder();
    readonly List<string> m_Logs = new List<string>();
    VisualElement m_Root;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("UITK_BINDINGS_OUT"))) return;
        var go = new GameObject("probe");
        var document = go.AddComponent<UIDocument>();
        document.panelSettings = Resources.Load<PanelSettings>("ProbePanelSettings");
        go.AddComponent<BindingsProbe>();
    }

    void Start()
    {
        var dir = Environment.GetEnvironmentVariable("UITK_BINDINGS_OUT");
        var imported = Path.Combine(dir, "import-logs.txt");
        if (File.Exists(imported)) m_Logs.AddRange(File.ReadAllLines(imported).Where(l => l.Length > 0));
        try
        {
            Run();
        }
        catch (Exception e)
        {
            Put("error", e.GetType().Name + ": " + e.Message);
        }
        m_Logs.Sort(StringComparer.Ordinal);
        m_Out.Append("{\"k\":\"logs\",\"v\":[").Append(string.Join(",", m_Logs.Select(Json))).Append("]}\n");
        m_Out.Append("{\"k\":\"complete\",\"v\":true}\n");
        File.WriteAllText(Path.Combine(dir, "values.jsonl"), m_Out.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    // ---- panel driving ----

    static Type Runtime => typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.UIElementsRuntimeUtility");

    /// <summary>Two frames: a panel update, then a repaint (when bindings update).</summary>
    static void Frames()
    {
        var update = Runtime.GetMethod("UpdatePanels", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        var repaint = Runtime.GetMethod("RepaintPanels", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        for (int i = 0; i < 2; i++)
        {
            update.Invoke(null, null);
            repaint.Invoke(null, new object[] { false });
        }
    }

    // Logs are recorded without the step that logged them: the Editor's importer reports a document
    // when the runner imports it, the engine's when the document is loaded.
    void Logged(string context, Action act)
    {
        Application.LogCallback capture = (message, stack, type) =>
        {
            if (type != LogType.Log) m_Logs.Add(type + ": " + message);
        };
        Application.logMessageReceived += capture;
        try
        {
            act();
        }
        catch (Exception e)
        {
            m_Logs.Add(context + " threw " + e.GetType().Name + ": " + e.Message);
        }
        finally
        {
            Application.logMessageReceived -= capture;
        }
    }

    // ---- recording ----

    void Put(string key, string value) => m_Out.Append("{\"k\":").Append(Json(key)).Append(",\"v\":").Append(Json(value)).Append("}\n");

    static string F(float v) => v.ToString("R", Inv);
    static string Color(Color c) => F(c.r) + "," + F(c.g) + "," + F(c.b) + "," + F(c.a);
    static string Name(object o) => o == null ? "null" : o is UnityEngine.Object u ? (u ? u.GetType().Name + ":" + u.name : "destroyed") : o.GetType().Name;
    static string Length(StyleLength s) => s.keyword + " " + F(s.value.value) + " " + s.value.unit;

    static string Result(VisualElement e, BindingId id)
    {
        string ui = e.TryGetLastBindingToUIResult(id, out var r) ? r.status + (string.IsNullOrEmpty(r.message) ? "" : " " + r.message) : "none";
        string src = e.TryGetLastBindingToSourceResult(id, out var r2) ? r2.status + (string.IsNullOrEmpty(r2.message) ? "" : " " + r2.message) : "none";
        return "ui=" + ui + " | source=" + src;
    }

    static string Describe(Binding binding)
    {
        var b = new StringBuilder(binding.GetType().Name).Append(" trigger=").Append(binding.updateTrigger);
        if (binding is DataBinding data)
        {
            b.Append(" path=").Append(data.dataSourcePath.ToString()).Append(" mode=").Append(data.bindingMode)
                .Append(" source=").Append(Name(data.dataSource)).Append(" sourceType=").Append(data.dataSourceType == null ? "null" : data.dataSourceType.FullName);
        }
        if (binding is ProbeCustomBinding custom) b.Append(" prefix=").Append(custom.prefix ?? "null");
        return b.ToString();
    }

    void RecordElement(string key, VisualElement e)
    {
        if (e == null)
        {
            Put(key + "exists", "false");
            return;
        }
        if (e is TextField field) Put(key + "value", field.value);
        else if (e is Slider slider) Put(key + "value", F(slider.value));
        else if (e is Label label) Put(key + "text", label.text);
        var infos = e.GetBindingInfos().ToList();
        Put(key + "bindings", string.Join(",", infos.Select(i => i.bindingId.ToString())));
        foreach (var info in infos) Put(key + "result/" + info.bindingId, Result(e, info.bindingId));
    }

    void RecordStyles(string key, VisualElement e)
    {
        Put(key + "style.width", Length(e.style.width));
        Put(key + "style.height", Length(e.style.height));
        Put(key + "style.backgroundColor", e.style.backgroundColor.keyword + " " + Color(e.style.backgroundColor.value));
        Put(key + "style.opacity", e.style.opacity.keyword + " " + F(e.style.opacity.value));
        Put(key + "style.display", e.style.display.keyword + " " + e.style.display.value);
        Put(key + "style.borderLeftWidth", e.style.borderLeftWidth.keyword + " " + F(e.style.borderLeftWidth.value));
        Put(key + "style.alignSelf", e.style.alignSelf.keyword + " " + e.style.alignSelf.value);
        Put(key + "resolved", F(e.resolvedStyle.width) + " x " + F(e.resolvedStyle.height) + " opacity " + F(e.resolvedStyle.opacity)
            + " display " + e.resolvedStyle.display + " color " + Color(e.resolvedStyle.backgroundColor) + " border " + F(e.resolvedStyle.borderLeftWidth));
    }

    static readonly string[] s_Main =
    {
        "box", "to-target", "default-mode", "inner-parent", "hier", "once", "dirty", "every", "own-source", "conv", "conv-order", "conv-late",
        "two-way", "to-source", "slider", "styled", "custom", "bad-path", "bad-target", "two-same", "child-after-bindings", "nested-child",
    };

    static readonly string[] s_Code = { "code-width", "code-length", "code-keyword", "code-string", "code-uss", "code-enum", "code-count", "code-tosource", "code-resolved", "code-sub", "code-nosuch" };

    BindData m_Data, m_Other;
    StyleSource m_StyleSource;

    void Record(string step)
    {
        foreach (var name in s_Main) RecordElement(step + "/" + name + "/", m_Root.Q(name));
        var styled = m_Root.Q("styled");
        if (styled != null) RecordStyles(step + "/styled/", styled);
        Put(step + "/data", m_Data.message + " " + m_Data.count + " " + F(m_Data.width) + " " + m_Data.inner.value);
        Put(step + "/other", m_Other.message + " " + m_Other.count);
        if (m_StyleSource == null) return;
        foreach (var name in s_Code)
        {
            var e = m_Root.Q(name);
            RecordElement(step + "/" + name + "/", e);
            if (e != null) RecordStyles(step + "/" + name + "/", e);
        }
        Put(step + "/style-source", F(m_StyleSource.back) + " " + F(m_StyleSource.resolved));
    }

    void Run()
    {
        var document = GetComponent<UIDocument>();
        var settings = document.panelSettings;
        // A fixed-size target: the panel's size (and so the layout) does not depend on the screen.
        var target = new RenderTexture(800, 600, 24, RenderTextureFormat.ARGB32);
        target.Create();
        settings.targetTexture = target;
        settings.scaleMode = PanelScaleMode.ConstantPixelSize;
        settings.scale = 1;
        m_Root = document.rootVisualElement;
        m_Data = Resources.Load<BindData>("BindData");
        m_Other = Resources.Load<BindData>("BindOther");

        ConverterGroups.RegisterConverterGroup(Group("probe-count", (ref int v) => "n=" + v));
        ConverterGroups.RegisterConverterGroup(Group("probe-hash", (ref int v) => "#" + v));
        var length = new ConverterGroup("probe-length");
        length.AddConverter((ref string s) => s == null ? -1 : s.Length);
        ConverterGroups.RegisterConverterGroup(length);

        Logged("clone", () =>
        {
            var asset = Resources.Load<VisualTreeAsset>("Bindings");
            asset.CloneTree(m_Root);
        });
        // Registered after the documents were cloned: a binding naming it does not get it if names
        // are resolved when the binding is made.
        ConverterGroups.RegisterConverterGroup(Group("probe-late", (ref int v) => "late " + v));

        foreach (var doc in new[] { "NoProperty", "NotBinding", "UnknownType" })
        {
            var container = new VisualElement { name = "doc-" + doc };
            m_Root.Add(container);
            Logged("clone " + doc, () => Resources.Load<VisualTreeAsset>(doc).CloneTree(container));
            Put("doc/" + doc + "/children", string.Join(",", container.Children().Select(c => c.name)));
            foreach (var child in container.Children())
            {
                Put("doc/" + doc + "/" + child.name + "/bindings", string.Join(",", child.GetBindingInfos().Select(i => i.bindingId + "=" + Describe(i.binding))));
                child.dataSource = m_Data;
            }
        }

        // Elements keep their sizes: none shrinks when the column overflows the panel.
        m_Root.Query<VisualElement>().ForEach(e => e.style.flexShrink = 0);

        foreach (var name in s_Main)
        {
            var e = m_Root.Q(name);
            if (e == null) continue;
            Put("describe/" + name + "/source", Name(e.dataSource) + " path=" + e.dataSourcePath + " type=" + (e.dataSourceType == null ? "null" : e.dataSourceType.FullName));
            Put("describe/" + name + "/bindings", string.Join(" ; ", e.GetBindingInfos().Select(i => i.bindingId + "=" + Describe(i.binding))));
        }

        Binding.SetPanelLogLevel(m_Root.panel, BindingLogLevel.None);
        Logged("frames", Frames);
        Record("initial");
        foreach (var doc in new[] { "NoProperty", "NotBinding", "UnknownType" })
            foreach (var child in m_Root.Q("doc-" + doc).Children())
                if (child is Label label) Put("doc/" + doc + "/" + child.name + "/text", label.text);

        Logged("frames", () =>
        {
            m_Data.message = "changed";
            m_Data.count = 12;
            m_Data.width = 55;
            m_Data.opacity = 0.25f;
            m_Data.color = new Color(0, 1, 0, 1);
            m_Data.inner.value = "inner2";
            m_Other.message = "other2";
            Frames();
        });
        Record("changed");

        Logged("frames", () =>
        {
            ((DataBinding)m_Root.Q("dirty").GetBinding("text")).MarkDirty();
            ((DataBinding)m_Root.Q("once").GetBinding("text")).MarkDirty();
            Frames();
        });
        Record("marked");

        Logged("frames", () =>
        {
            m_Root.Q<TextField>("two-way").value = "typed";
            m_Root.Q<TextField>("to-source").value = "abcdef";
            m_Root.Q<Slider>("slider").value = 12.5f;
            Frames();
        });
        Record("ui");

        Logged("frames", () =>
        {
            m_Data.display = DisplayStyle.None;
            Frames();
        });
        Record("hidden");

        // Bindings to style properties made in code, on elements with a plain data source.
        m_StyleSource = new StyleSource();
        var codeBox = new VisualElement { name = "code-box", dataSource = m_StyleSource };
        codeBox.style.flexDirection = FlexDirection.Row;
        m_Root.Add(codeBox);
        VisualElement Code(string name, string property, string path, BindingMode mode = BindingMode.ToTarget)
        {
            var e = new VisualElement { name = name };
            e.style.height = 10;
            e.style.flexShrink = 0;
            e.SetBinding(property, new DataBinding { dataSourcePath = new PropertyPath(path), bindingMode = mode });
            codeBox.Add(e);
            return e;
        }
        Code("code-width", "style.width", "width");
        Code("code-length", "style.width", "length");
        Code("code-keyword", "style.width", "keyword");
        Code("code-string", "style.width", "text");
        Code("code-uss", "style.background-color", "color");
        Code("code-enum", "style.alignSelf", "align");
        Code("code-count", "style.height", "count");
        Code("code-tosource", "style.width", "back", BindingMode.ToSource).style.width = 33;
        Code("code-resolved", "resolvedStyle.width", "resolved", BindingMode.ToSource).style.width = 21;
        Code("code-sub", "style.width.value", "length");
        Code("code-nosuch", "style.nosuch", "width");
        Logged("frames", Frames);
        Record("code");

        Logged("frames", () =>
        {
            m_StyleSource.width = 45;
            m_StyleSource.count = 3;
            m_StyleSource.length = new Length(25, LengthUnit.Pixel);
            m_StyleSource.keyword = StyleKeyword.Initial;
            m_StyleSource.color = new Color(0.5f, 0.25f, 1, 1);
            m_StyleSource.align = Align.FlexEnd;
            m_Root.Q("code-tosource").style.width = 44;
            m_Root.Q("code-resolved").style.width = 31;
            Frames();
        });
        Record("code-changed");
        Requests();
        LogLevels();
        Logged("viewdata", () => ViewData());
        NestedStyleValues();
    }

    // ---- paths into nested style values ----

    static string L(Length l) => F(l.value) + l.unit;
    static string V(Vector3 v) => F(v.x) + "," + F(v.y) + "," + F(v.z);
    static string V(Vector2 v) => F(v.x) + "," + F(v.y);
    static string A(Angle a) => F(a.value) + a.unit;

    static string Fmt(object v)
    {
        switch (v)
        {
            case null: return "null";
            case float f: return F(f);
            case Length l: return L(l);
            case Angle a: return A(a);
            case Vector3 v3: return V(v3);
            case Vector2 v2: return V(v2);
            case Color c: return Color(c);
            case TimeValue t: return F(t.value) + t.unit;
            case UnityEngine.Object o: return Name(o);
            case string str: return "'" + str + "'";
            default: return v.ToString();
        }
    }

    /// <summary>An element's nested style values, in one line.</summary>
    static string Nested(VisualElement e)
    {
        var st = e.style;
        var b = new StringBuilder();
        var t = st.translate;
        b.Append("translate ").Append(t.keyword).Append(' ').Append(L(t.value.x)).Append(' ').Append(L(t.value.y)).Append(' ').Append(F(t.value.z));
        var r = st.rotate;
        b.Append(" | rotate ").Append(r.keyword).Append(' ').Append(A(r.value.angle));
        var sc = st.scale;
        b.Append(" | scale ").Append(sc.keyword).Append(' ').Append(V(sc.value.value));
        var o = st.transformOrigin;
        b.Append(" | origin ").Append(o.keyword).Append(' ').Append(L(o.value.x)).Append(' ').Append(L(o.value.y)).Append(' ').Append(F(o.value.z));
        var px = st.backgroundPositionX;
        b.Append(" | positionX ").Append(px.keyword).Append(' ').Append(px.value.keyword).Append(' ').Append(L(px.value.offset));
        var rp = st.backgroundRepeat;
        b.Append(" | repeat ").Append(rp.keyword).Append(' ').Append(rp.value.x).Append(' ').Append(rp.value.y);
        var sz = st.backgroundSize;
        b.Append(" | size ").Append(sz.keyword).Append(' ').Append(sz.value.sizeType).Append(' ').Append(L(sz.value.x)).Append(' ').Append(L(sz.value.y));
        var img = st.backgroundImage;
        b.Append(" | image ").Append(img.keyword).Append(' ').Append(Name(img.value.texture));
        var cu = st.cursor;
        b.Append(" | cursor ").Append(cu.keyword).Append(' ').Append(Name(cu.value.texture)).Append(' ').Append(V(cu.value.hotspot));
        var sh = st.textShadow;
        b.Append(" | shadow ").Append(sh.keyword).Append(' ').Append(V(sh.value.offset)).Append(' ').Append(F(sh.value.blurRadius)).Append(' ').Append(Color(sh.value.color));
        var du = st.transitionDuration;
        b.Append(" | duration ").Append(du.keyword).Append(' ').Append(du.value == null ? "null" : string.Join(";", du.value.Select(x => F(x.value) + x.unit)));
        var pr = st.transitionProperty;
        b.Append(" | property ").Append(pr.keyword).Append(' ').Append(pr.value == null ? "null" : string.Join(";", pr.value.Select(x => x.ToString())));
        var tf = st.transitionTimingFunction;
        b.Append(" | timing ").Append(tf.keyword).Append(' ').Append(tf.value == null ? "null" : string.Join(";", tf.value.Select(x => x.mode.ToString())));
        var rs = e.resolvedStyle;
        b.Append(" | resolved translate ").Append(V(rs.translate)).Append(" rotate ").Append(A(rs.rotate.angle)).Append(" scale ").Append(V(rs.scale.value))
            .Append(" origin ").Append(V(rs.transformOrigin));
        return b.ToString();
    }

    string Read<T>(VisualElement e, string path)
    {
        try
        {
            bool ok = PropertyContainer.TryGetValue(ref e, new PropertyPath(path), out T value, out VisitReturnCode code);
            return (ok ? "ok " : "fail ") + code + " " + Fmt(value);
        }
        catch (Exception x)
        {
            return "threw " + x.GetType().Name;
        }
    }

    string Write<T>(VisualElement e, string path, T value)
    {
        try
        {
            bool ok = PropertyContainer.TrySetValue(ref e, new PropertyPath(path), value, out VisitReturnCode code);
            return (ok ? "ok " : "fail ") + code;
        }
        catch (Exception x)
        {
            return "threw " + x.GetType().Name;
        }
    }

    // Reads and writes through property paths into the values of style properties (translate,
    // rotate, scale, transform origin, background position, repeat and size, cursor, text shadow,
    // font definition, transition lists), and bindings to such paths. Its logs are recorded under
    // their own key.
    void NestedStyleValues()
    {
        var logs = new List<string>();
        Application.LogCallback capture = (message, stack, type) => { if (type != LogType.Log) logs.Add(type + ": " + message); };
        Application.logMessageReceived += capture;
        try
        {
            NestedReads();
            NestedWrites();
            NestedBindings();
        }
        catch (Exception e)
        {
            Put("nested/error", e.GetType().Name + ": " + e.Message);
        }
        finally
        {
            Application.logMessageReceived -= capture;
        }
        logs.Sort(StringComparer.Ordinal);
        Put("nested/logs", string.Join(" || ", logs));
    }

    VisualElement NestedElement(string name, VisualElement parent)
    {
        var e = new VisualElement { name = name };
        e.style.height = 4;
        e.style.width = 40;
        e.style.flexShrink = 0;
        parent.Add(e);
        return e;
    }

    void NestedReads()
    {
        var box = new VisualElement { name = "nested-box" };
        m_Root.Add(box);
        var texture = new Texture2D(2, 2) { name = "probe-tex" };
        var e = NestedElement("nested-read", box);
        e.style.translate = new Translate(new Length(10, LengthUnit.Percent), new Length(4, LengthUnit.Pixel), 3);
        e.style.rotate = new Rotate(new Angle(90, AngleUnit.Degree));
        e.style.scale = new Scale(new Vector3(2, 0.5f, 1));
        e.style.transformOrigin = new TransformOrigin(new Length(25, LengthUnit.Percent), new Length(6, LengthUnit.Pixel), 2);
        e.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Right, new Length(8, LengthUnit.Pixel));
        e.style.backgroundRepeat = new BackgroundRepeat(Repeat.Space, Repeat.Round);
        e.style.backgroundSize = new BackgroundSize(new Length(30, LengthUnit.Percent), new Length(12, LengthUnit.Pixel));
        e.style.backgroundImage = texture;
        e.style.cursor = new UnityEngine.UIElements.Cursor { texture = texture, hotspot = new Vector2(1, 2) };
        e.style.textShadow = new TextShadow { offset = new Vector2(3, 4), blurRadius = 1.5f, color = new Color(1, 0.5f, 0.25f, 1) };
        e.style.transitionDuration = new List<TimeValue> { new TimeValue(2, TimeUnit.Second), new TimeValue(300, TimeUnit.Millisecond) };
        e.style.transitionProperty = new List<StylePropertyName> { new StylePropertyName("opacity"), new StylePropertyName("width") };
        e.style.transitionTimingFunction = new List<EasingFunction> { new EasingFunction(EasingMode.EaseInOut) };
        // An element without transitions that the style pass below reaches (see NestedWrites).
        NestedElement("nested-plain", box);
        // A control the theme's rules style (see NestedWrites).
        box.Add(new Button { name = "nested-themed", text = "themed" });
        Frames();
        Put("nested/read/state", Nested(e));
        void R<T>(string path) => Put("nested/read/" + path + " as " + typeof(T).Name, Read<T>(e, path));
        R<Length>("style.translate.value.x");
        R<float>("style.translate.value.x.value");
        R<LengthUnit>("style.translate.value.y.unit");
        R<float>("style.translate.value.z");
        R<float>("style.translate.value.w");
        R<Length>("style.translate.x");
        R<Angle>("style.rotate.value.angle");
        R<float>("style.rotate.value.angle.value");
        R<AngleUnit>("style.rotate.value.angle.unit");
        R<Vector3>("style.rotate.value.axis");
        R<float>("style.rotate.value.axis.z");
        R<Vector3>("style.scale.value.value");
        R<float>("style.scale.value.value.y");
        R<Length>("style.transformOrigin.value.x");
        R<Length>("style.transform-origin.value.y");
        R<float>("style.transformOrigin.value.z");
        R<BackgroundPositionKeyword>("style.backgroundPositionX.value.keyword");
        R<Length>("style.backgroundPositionX.value.offset");
        R<float>("style.backgroundPositionX.value.offset.value");
        R<Repeat>("style.backgroundRepeat.value.x");
        R<Repeat>("style.backgroundRepeat.value.y");
        R<BackgroundSizeType>("style.backgroundSize.value.sizeType");
        R<Length>("style.backgroundSize.value.x");
        R<Texture2D>("style.backgroundImage.value.texture");
        R<Sprite>("style.backgroundImage.value.sprite");
        R<RenderTexture>("style.backgroundImage.value.renderTexture");
        R<VectorImage>("style.backgroundImage.value.vectorImage");
        R<Texture2D>("style.cursor.value.texture");
        R<Vector2>("style.cursor.value.hotspot");
        R<float>("style.cursor.value.hotspot.y");
        R<int>("style.cursor.value.defaultCursorId");
        R<Vector2>("style.textShadow.value.offset");
        R<float>("style.text-shadow.value.blurRadius");
        R<Color>("style.textShadow.value.color");
        R<float>("style.textShadow.value.color.g");
        R<Font>("style.unityFontDefinition.value.font");
        R<UnityEngine.TextCore.Text.FontAsset>("style.unityFontDefinition.value.fontAsset");
        R<TimeValue>("style.transitionDuration.value[0]");
        R<float>("style.transitionDuration.value[0].value");
        R<TimeUnit>("style.transitionDuration.value[1].unit");
        R<float>("style.transitionDuration.value[5].value");
        R<string>("style.transitionProperty.value[1].name");
        R<EasingMode>("style.transitionTimingFunction.value[0].mode");
        R<float>("resolvedStyle.translate.x");
        R<float>("resolvedStyle.rotate.angle.value");
        R<float>("resolvedStyle.scale.value.y");
        R<float>("resolvedStyle.transformOrigin.x");
        R<Color>("resolvedStyle.backgroundColor");
        R<float>("resolvedStyle.backgroundColor.a");
        // A wrong value type for the path's value.
        R<string>("style.translate.value.x");
        R<int>("style.translate.value.z");
        R<float>("style.translate.value.x");
    }

    void NestedWrites()
    {
        var box = m_Root.Q("nested-box");
        var e = NestedElement("nested-write", box);
        void W<T>(string path, T value) => Put("nested/write/" + path + "=" + Fmt(value), Write(e, path, value) + " -> " + Nested(e));
        // Into values that were never set.
        W("style.translate.value.x", new Length(7, LengthUnit.Pixel));
        W("style.translate.value.y.value", 3f);
        W("style.translate.value.z", 5f);
        W("style.translate.value.x", 6f);
        W("style.translate.value.x", 8);
        W("style.rotate.value.angle.value", 30f);
        W("style.rotate.value.angle.unit", AngleUnit.Turn);
        W("style.rotate.value.axis", new Vector3(0, 1, 0));
        W("style.scale.value.value", new Vector3(2, 3, 1));
        W("style.scale.value.value.x", 4f);
        W("style.transformOrigin.value.z", 2f);
        W("style.transformOrigin.value.x", new Length(20, LengthUnit.Percent));
        W("style.backgroundPositionX.value.keyword", BackgroundPositionKeyword.Right);
        W("style.backgroundPositionX.value.offset.value", 9f);
        W("style.backgroundRepeat.value.y", Repeat.Space);
        W("style.backgroundSize.value.x", new Length(15, LengthUnit.Pixel));
        W("style.backgroundSize.value.sizeType", BackgroundSizeType.Contain);
        W("style.cursor.value.hotspot", new Vector2(3, 4));
        W("style.textShadow.value.blurRadius", 2f);
        W("style.textShadow.value.color", new Color(0, 1, 0, 1));
        W("style.textShadow.value.offset.x", 6f);
        W("style.transitionDuration.value[0].value", 2f);
        W("style.transitionProperty.value[0].name", "width");
        W("resolvedStyle.translate.x", 1f);
        W("style.translate.keyword", StyleKeyword.Initial);
        W("style.translate.value.z", 1f);
        // Into a list that holds values.
        var listed = NestedElement("nested-write-list", box);
        listed.style.transitionDuration = new List<TimeValue> { new TimeValue(1, TimeUnit.Second), new TimeValue(2, TimeUnit.Second) };
        listed.style.transitionProperty = new List<StylePropertyName> { new StylePropertyName("opacity") };
        void WL<T>(string path, T value) => Put("nested/write-list/" + path + "=" + Fmt(value), Write(listed, path, value) + " -> " + Nested(listed));
        WL("style.transitionDuration.value[1].value", 5f);
        WL("style.transitionDuration.value[0].unit", TimeUnit.Millisecond);
        WL("style.transitionDuration.value[0]", new TimeValue(7, TimeUnit.Second));
        WL("style.transitionDuration.value[4].value", 5f);
        WL("style.transitionProperty.value[0].name", "width");
        // What resolvedStyle answers right after an inline value is set on an element no style
        // pass has reached yet (in the panel, and outside any panel), and after it is unset again.
        string Resolved(VisualElement x) => F(x.resolvedStyle.opacity) + " " + Color(x.resolvedStyle.backgroundColor) + " " + V(x.resolvedStyle.translate)
            + " " + x.resolvedStyle.display + " " + F(x.resolvedStyle.width) + " " + F(x.resolvedStyle.borderLeftWidth) + " " + x.resolvedStyle.unityFontStyleAndWeight;
        void Fresh(string name, VisualElement x)
        {
            Put("nested/fresh/" + name + "/before", Resolved(x));
            x.style.opacity = 0.5f;
            x.style.backgroundColor = new Color(1, 0, 0, 1);
            x.style.translate = new Translate(new Length(3, LengthUnit.Pixel), new Length(4, LengthUnit.Pixel), 0);
            x.style.display = DisplayStyle.None;
            x.style.width = 25;
            x.style.borderLeftWidth = 2;
            x.style.unityFontStyleAndWeight = FontStyle.Bold;
            Put("nested/fresh/" + name + "/set", Resolved(x));
            x.style.opacity = StyleKeyword.Null;
            x.style.translate = StyleKeyword.Null;
            x.style.display = StyleKeyword.Null;
            x.style.unityFontStyleAndWeight = StyleKeyword.Null;
            Put("nested/fresh/" + name + "/unset", Resolved(x));
        }
        Fresh("in-panel", NestedElement("nested-fresh", box));
        Fresh("detached", new VisualElement());
        var child = new VisualElement();
        var parent = new VisualElement();
        parent.Add(child);
        parent.style.unityFontStyleAndWeight = FontStyle.Italic;
        parent.style.opacity = 0.25f;
        Put("nested/fresh/child-of-detached", Resolved(child));
        Fresh("styled", m_Root.Q("nested-plain"));
        Fresh("themed", m_Root.Q("nested-themed"));
        Frames();
        Put("nested/fresh/in-panel/after-frames", Resolved(m_Root.Q("nested-fresh")));
        Put("nested/fresh/styled/after-frames", Resolved(m_Root.Q("nested-plain")));
        Put("nested/fresh/themed/after-frames", Resolved(m_Root.Q("nested-themed")));
        m_Root.Q("nested-themed").style.backgroundColor = StyleKeyword.Null;
        m_Root.Q("nested-themed").style.borderLeftWidth = StyleKeyword.Null;
        Put("nested/fresh/themed/unset-rest", Resolved(m_Root.Q("nested-themed")));
        Put("nested/write/after-frames", Nested(e));
        Put("nested/write-list/after-frames", Nested(listed));
    }

    static readonly string[] s_NestedBound =
    {
        "nb-x-length", "nb-x-float", "nb-x-int", "nb-x-value", "nb-angle", "nb-angle-value", "nb-rotate", "nb-scale", "nb-scale-x", "nb-shadow-color",
        "nb-shadow-blur", "nb-origin", "nb-position", "nb-hotspot", "nb-duration", "nb-duration-unset", "nb-back-x", "nb-back-angle", "nb-back-resolved", "nb-nosuch",
    };

    void NestedBindings()
    {
        var source = new NestedSource();
        var box = new VisualElement { name = "nested-bound", dataSource = source };
        m_Root.Add(box);
        VisualElement B(string name, string property, string path, BindingMode mode = BindingMode.ToTarget)
        {
            var e = NestedElement(name, box);
            e.SetBinding(property, new DataBinding { dataSourcePath = new PropertyPath(path), bindingMode = mode });
            return e;
        }
        B("nb-x-length", "style.translate.value.x", "length");
        B("nb-x-float", "style.translate.value.x", "number");
        B("nb-x-int", "style.translate.value.x", "whole");
        B("nb-x-value", "style.translate.value.x.value", "number");
        B("nb-angle", "style.rotate.value.angle", "angle");
        B("nb-angle-value", "style.rotate.value.angle.value", "number");
        B("nb-rotate", "style.rotate", "rotate");
        B("nb-scale", "style.scale.value.value", "vector");
        B("nb-scale-x", "style.scale.value.value.x", "number");
        B("nb-shadow-color", "style.textShadow.value.color", "color");
        B("nb-shadow-blur", "style.text-shadow.value.blurRadius", "number");
        B("nb-origin", "style.transformOrigin.value.x", "length");
        B("nb-position", "style.backgroundPositionX.value.offset.value", "number");
        B("nb-hotspot", "style.cursor.value.hotspot", "vector2");
        B("nb-duration", "style.transitionDuration.value[0].value", "number").style.transitionDuration = new List<TimeValue> { new TimeValue(1, TimeUnit.Second) };
        B("nb-duration-unset", "style.transitionDuration.value[0].value", "number");
        var backX = B("nb-back-x", "style.translate.value.x", "backLength", BindingMode.ToSource);
        backX.style.translate = new Translate(new Length(11, LengthUnit.Pixel), new Length(0, LengthUnit.Pixel), 0);
        var backAngle = B("nb-back-angle", "style.rotate.value.angle.value", "backAngle", BindingMode.ToSource);
        backAngle.style.rotate = new Rotate(new Angle(15, AngleUnit.Degree));
        var backResolved = B("nb-back-resolved", "resolvedStyle.translate.x", "backResolved", BindingMode.ToSource);
        backResolved.style.translate = new Translate(new Length(13, LengthUnit.Pixel), new Length(0, LengthUnit.Pixel), 0);
        B("nb-nosuch", "style.translate.value.w", "number");
        void Record(string step)
        {
            foreach (var name in s_NestedBound)
            {
                var e = box.Q(name);
                foreach (var info in e.GetBindingInfos()) Put("nested/" + step + "/" + name + "/result", Result(e, info.bindingId));
                Put("nested/" + step + "/" + name + "/state", Nested(e));
            }
            Put("nested/" + step + "/source", L(source.backLength) + " " + F(source.backNumber) + " " + F(source.backAngle) + " " + F(source.backResolved));
        }
        Frames();
        Record("bound");
        source.number = 20;
        source.whole = 4;
        source.length = new Length(18, LengthUnit.Pixel);
        source.angle = new Angle(2, AngleUnit.Radian);
        source.rotate = new Rotate(new Angle(10, AngleUnit.Degree));
        source.vector = new Vector3(1, 1, 2);
        source.vector2 = new Vector2(7, 8);
        source.color = new Color(1, 0, 0, 0.5f);
        backX.style.translate = new Translate(new Length(22, LengthUnit.Percent), new Length(0, LengthUnit.Pixel), 0);
        backAngle.style.rotate = new Rotate(new Angle(0.5f, AngleUnit.Turn));
        backResolved.style.translate = new Translate(new Length(17, LengthUnit.Pixel), new Length(0, LengthUnit.Pixel), 0);
        Frames();
        Record("changed");
    }

    // The order GetBindingInfos lists an element's bindings in: set in a panel (before and after the
    // update processes them), one set again, the element removed, a binding set while it is out of
    // the panel, and added back.
    void Requests()
    {
        var e = new VisualElement { name = "requests", dataSource = m_StyleSource };
        m_Root.Add(e);
        string Ids() => string.Join(",", e.GetBindingInfos().Select(i => i.bindingId.ToString()));
        DataBinding Make() => new DataBinding { dataSourcePath = new PropertyPath("width"), bindingMode = BindingMode.ToTarget };
        e.SetBinding("style.width", Make());
        e.SetBinding("style.height", Make());
        e.SetBinding("style.left", Make());
        Put("requests/set", Ids());
        Frames();
        Put("requests/processed", Ids());
        e.SetBinding("style.width", Make());
        e.SetBinding("style.top", Make());
        Put("requests/set-again", Ids());
        Frames();
        Put("requests/processed-again", Ids());
        e.RemoveFromHierarchy();
        Put("requests/removed", Ids());
        e.SetBinding("style.right", Make());
        Put("requests/removed-set", Ids());
        m_Root.Add(e);
        Put("requests/readded", Ids());
        Frames();
        Put("requests/readded-processed", Ids());
        e.RemoveFromHierarchy();
    }

    // Binding warnings by log level: a label whose binding fails on every update (its path names no
    // source property) and a binding counting its updates, over two frames per setting: the panel's
    // level reset, set to All, then the PanelSettings level set to All and to Once, and back to the
    // asset's None. Counts the failing label's warnings and the other warnings (other failing
    // bindings); neither is listed in the logs.
    void LogLevels()
    {
        var settings = GetComponent<UIDocument>().panelSettings;
        var counting = new CountingBinding();
        var label = new Label { name = "log-level", dataSource = m_Data };
        label.SetBinding("text", new DataBinding { dataSourcePath = new PropertyPath("nosuch-log"), bindingMode = BindingMode.ToTarget });
        label.SetBinding("tooltip", counting);
        m_Root.Add(label);
        int own = 0, others = 0;
        Application.LogCallback count = (message, stack, type) =>
        {
            if (type != LogType.Warning) return;
            if (message.Contains("nosuch-log")) own++;
            else others++;
        };
        var steps = new (string name, Action set)[]
        {
            ("reset", () => Binding.ResetPanelLogLevel(m_Root.panel)),
            ("panel-All", () => Binding.SetPanelLogLevel(m_Root.panel, BindingLogLevel.All)),
            ("settings-All", () => settings.bindingLogLevel = BindingLogLevel.All),
            ("settings-Once", () => settings.bindingLogLevel = BindingLogLevel.Once),
            ("settings-None", () => settings.bindingLogLevel = BindingLogLevel.None),
        };
        Application.logMessageReceived += count;
        try
        {
            foreach (var (name, set) in steps)
            {
                set();
                own = others = 0;
                counting.count = 0;
                Frames();
                Put("loglevel/" + name, "updates " + counting.count + " warnings " + own + " others " + others);
            }
        }
        finally
        {
            Application.logMessageReceived -= count;
        }
        label.RemoveFromHierarchy();
    }

    // View data persistence on a runtime panel: elements with a viewDataKey change their state, are
    // removed, and new elements with the same keys are added (the Editor restores such state in
    // Editor windows). A removed element added back is the control.
    void ViewData()
    {
        Put("viewdata/contextType", m_Root.panel.contextType.ToString());
        var box = new VisualElement { name = "viewdata-box" };
        box.style.position = Position.Absolute;
        box.style.left = 600;
        box.style.top = 0;
        box.style.width = 150;
        m_Root.Add(box);
        ScrollView Scroll()
        {
            var scroll = new ScrollView { viewDataKey = "probe-scroll" };
            scroll.style.height = 50;
            scroll.style.flexShrink = 0;
            for (int i = 0; i < 20; i++) scroll.Add(new Label("row " + i) { style = { height = 20, flexShrink = 0 } });
            return scroll;
        }
        var items = new List<string> { "a", "b", "c", "d" };
        ListView List() => new ListView(items, 16, () => new Label(), (e, i) => ((Label)e).text = items[i])
            { viewDataKey = "probe-list", style = { height = 70, flexShrink = 0 } };
        var scroll1 = Scroll();
        var fold1 = new Foldout { viewDataKey = "probe-foldout", text = "fold" };
        var list1 = List();
        box.Add(scroll1);
        box.Add(fold1);
        box.Add(list1);
        Frames();
        scroll1.scrollOffset = new Vector2(0, 120);
        fold1.value = false;
        list1.SetSelection(2);
        Frames();
        Put("viewdata/changed", F(scroll1.scrollOffset.y) + " " + fold1.value + " " + list1.selectedIndex);
        box.Clear();
        Frames();
        var scroll2 = Scroll();
        var fold2 = new Foldout { viewDataKey = "probe-foldout", text = "fold" };
        var list2 = List();
        box.Add(scroll2);
        box.Add(fold2);
        box.Add(list2);
        Frames();
        Put("viewdata/recreated", F(scroll2.scrollOffset.y) + " " + fold2.value + " " + list2.selectedIndex);
        box.Clear();
        box.Add(scroll1);
        box.Add(fold1);
        box.Add(list1);
        Frames();
        Put("viewdata/readded", F(scroll1.scrollOffset.y) + " " + fold1.value + " " + list1.selectedIndex);
    }

    static ConverterGroup Group(string id, TypeConverter<int, string> converter)
    {
        var group = new ConverterGroup(id);
        group.AddConverter(converter);
        return group;
    }

    static string Json(string s)
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
}
