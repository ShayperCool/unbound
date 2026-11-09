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
