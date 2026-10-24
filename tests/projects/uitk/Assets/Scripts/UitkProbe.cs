using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Records what UI Toolkit does with <c>Assets/UI/Probe.uxml</c> (USS-styled controls, text, images, a
/// scroll view and a list view) on a runtime panel that renders into a render texture: the whole
/// visual tree with layout rects and resolved styles, the rendered image, and the events, values and
/// layouts a scripted pointer and keyboard sequence produces. Writes JSON lines to
/// <c>UITK_PROBE_OUT</c> and PNGs next to it, then quits. Synchronous (the Editor in batch mode never
/// reaches the end of a frame): panels are updated and rendered through UIElementsRuntimeUtility.
/// </summary>
public class UitkProbe : MonoBehaviour
{
    public UIDocument document;
    public Texture2D picture;

    const int Width = 800, Height = 600;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    readonly StringBuilder m_Out = new StringBuilder();
    readonly List<string> m_Events = new List<string>();
    string m_Base;
    RenderTexture m_Target;
    int m_Clicks;

    void Start()
    {
        var output = Environment.GetEnvironmentVariable("UITK_PROBE_OUT");
        if (string.IsNullOrEmpty(output)) output = Path.Combine(Application.temporaryCachePath, "uitk-probe.jsonl");
        m_Base = Path.Combine(Path.GetDirectoryName(output), Path.GetFileNameWithoutExtension(output));
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            try
            {
                Run();
            }
            catch (Exception e)
            {
                Line("error", "\"message\":" + Str(e.ToString()));
                Debug.LogException(e);
            }
            File.WriteAllText(output, m_Out.ToString());
            Debug.Log("uitk probe written to " + output);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    void Run()
    {
        m_Target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
        m_Target.Create();
        var settings = document.panelSettings;
        settings.targetTexture = m_Target;
        settings.scaleMode = PanelScaleMode.ConstantPixelSize;
        settings.scale = 1;
        settings.clearColor = true;
        settings.colorClearValue = new Color(0, 0, 0, 1);

        var root = document.rootVisualElement;
        Populate(root);
        HookEvents(root);
        Update2();

        Line("panel", "\"scaledPixelsPerPoint\":" + F(root.panel.scaledPixelsPerPoint) + ",\"visualTree\":" + Rect(root.panel.visualTree.layout) +
            ",\"sortingOrder\":" + F(settings.sortingOrder));
        RecordTree("initial", root.panel.visualTree);
        Render("initial");

        RunSteps(root);
        RecordTree("final", root.panel.visualTree);
        Render("final");

        // The same document at a fractional scale: Yoga rounds to the pixel grid of 1.25 pixels per point.
        settings.scale = 1.25f;
        Update2();
        RecordTree("scaled", root.panel.visualTree);
        Render("scaled");
    }

    void Populate(VisualElement root)
    {
        var scroll = root.Q<ScrollView>("scroll");
        for (int i = 0; i < 20; i++)
        {
            var label = new Label("Scroll item " + i) { name = "item" + i };
            label.AddToClassList("scroll-item");
            if (i % 2 == 0) label.AddToClassList("even");
            scroll.Add(label);
        }
        var items = new List<string>();
        for (int i = 0; i < 50; i++) items.Add("List row " + i);
        var list = root.Q<ListView>("list");
        list.makeItem = () => new Label();
        list.bindItem = (e, i) => ((Label)e).text = items[i];
        list.itemsSource = items;
        list.selectionType = SelectionType.Single;
        list.selectionChanged += s => m_Events.Add("{\"type\":\"selectionChanged\",\"index\":" + list.selectedIndex + "}");

        var image = root.Q<Image>("image");
        image.image = picture;
        image.scaleMode = ScaleMode.ScaleToFit;

        root.Q<Button>("button").clicked += () => { m_Clicks++; m_Events.Add("{\"type\":\"clicked\",\"target\":\"button\"}"); };
        root.Q<Button>("button2").clicked += () => m_Events.Add("{\"type\":\"clicked\",\"target\":\"button2\"}");
    }

    void HookEvents(VisualElement root)
    {
        void Log<T>(VisualElement on, string extra = null) where T : EventBase<T>, new()
        {
            on.RegisterCallback<T>(e =>
            {
                var s = new StringBuilder("{\"type\":\"").Append(typeof(T).Name).Append("\",\"target\":").Append(Str(Describe(e.target as VisualElement)));
                if (e is IPointerEvent p)
                    s.Append(",\"pos\":").Append(V2(p.position)).Append(",\"local\":").Append(V2(p.localPosition)).Append(",\"button\":").Append(p.button)
                        .Append(",\"pressed\":").Append(p.pressedButtons).Append(",\"pointerId\":").Append(p.pointerId);
                if (e is IKeyboardEvent k)
                    s.Append(",\"char\":").Append((int)k.character).Append(",\"key\":\"").Append(k.keyCode).Append("\"");
                if (e is ChangeEvent<string> cs) s.Append(",\"previous\":").Append(Str(cs.previousValue)).Append(",\"new\":").Append(Str(cs.newValue));
                if (e is ChangeEvent<bool> cb) s.Append(",\"previous\":").Append(cb.previousValue ? "true" : "false").Append(",\"new\":").Append(cb.newValue ? "true" : "false");
                if (e is ChangeEvent<float> cf) s.Append(",\"previous\":").Append(F(cf.previousValue)).Append(",\"new\":").Append(F(cf.newValue));
                if (e is WheelEvent w) s.Append(",\"delta\":").Append(V3(w.delta));
                if (e is FocusEventBase<FocusEvent> || e is FocusEventBase<BlurEvent> || e is FocusEventBase<FocusInEvent> || e is FocusEventBase<FocusOutEvent>)
                    s.Append(",\"related\":").Append(Str(Describe(((IFocusEvent)e).relatedTarget as VisualElement)));
                s.Append("}");
                m_Events.Add(s.ToString());
            }, TrickleDown.TrickleDown);
        }
        Log<PointerDownEvent>(root);
        Log<PointerUpEvent>(root);
        Log<PointerMoveEvent>(root);
        Log<PointerEnterEvent>(root);
        Log<PointerLeaveEvent>(root);
        Log<PointerOverEvent>(root);
        Log<PointerOutEvent>(root);
        Log<PointerCancelEvent>(root);
        Log<ClickEvent>(root);
        Log<MouseDownEvent>(root);
        Log<MouseUpEvent>(root);
        Log<MouseMoveEvent>(root);
        Log<MouseEnterEvent>(root);
        Log<MouseLeaveEvent>(root);
        Log<MouseOverEvent>(root);
        Log<MouseOutEvent>(root);
        Log<WheelEvent>(root);
        Log<FocusInEvent>(root);
        Log<FocusOutEvent>(root);
        Log<FocusEvent>(root);
        Log<BlurEvent>(root);
        Log<KeyDownEvent>(root);
        Log<KeyUpEvent>(root);
        Log<InputEvent>(root);
        Log<ChangeEvent<string>>(root);
        Log<ChangeEvent<bool>>(root);
        Log<ChangeEvent<float>>(root);
        Log<NavigationMoveEvent>(root);
        Log<NavigationSubmitEvent>(root);
        Log<NavigationCancelEvent>(root);
    }

    // ---- the scripted sequence ----

    void RunSteps(VisualElement root)
    {
        var panelRoot = root.panel.visualTree;
        var button = root.Q<Button>("button");
        var toggle = root.Q<Toggle>("toggle");
        var field = root.Q<TextField>("field");
        var slider = root.Q<Slider>("slider");
        var foldout = root.Q<Foldout>("foldout");
        var scroll = root.Q<ScrollView>("scroll");
        var list = root.Q<ListView>("list");

        Vector2 Center(VisualElement e) => e.worldBound.center;

        Step("hover button", root, () => Pointer(panelRoot, EventType.MouseMove, Center(button)));
        Step("press button", root, () => Pointer(panelRoot, EventType.MouseDown, Center(button)), button);
        Step("release button", root, () => Pointer(panelRoot, EventType.MouseUp, Center(button)), button);
        var checkmark = toggle.Q(className: "unity-toggle__checkmark");
        Step("click toggle", root, () =>
        {
            Pointer(panelRoot, EventType.MouseMove, Center(checkmark));
            Pointer(panelRoot, EventType.MouseDown, Center(checkmark));
            Pointer(panelRoot, EventType.MouseUp, Center(checkmark));
        }, toggle);
        var input = field.Q(className: "unity-text-field__input");
        Step("click field", root, () =>
        {
            var p = new Vector2(input.worldBound.xMax - 4, input.worldBound.center.y);
            Pointer(panelRoot, EventType.MouseMove, p);
            Pointer(panelRoot, EventType.MouseDown, p);
            Pointer(panelRoot, EventType.MouseUp, p);
        }, field);
        Step("type", root, () =>
        {
            foreach (var c in "AB!")
                Key(panelRoot, c, KeyCode.None);
            Key(panelRoot, '\0', KeyCode.Backspace);
        }, field);
        var tracker = slider.Q(className: "unity-base-slider__tracker");
        Step("click slider", root, () =>
        {
            var p = new Vector2(tracker.worldBound.x + tracker.worldBound.width * 0.75f, tracker.worldBound.center.y);
            Pointer(panelRoot, EventType.MouseMove, p);
            Pointer(panelRoot, EventType.MouseDown, p);
            Pointer(panelRoot, EventType.MouseUp, p);
        }, slider);
        var foldoutToggle = foldout.Q<Toggle>();
        Step("collapse foldout", root, () =>
        {
            var p = Center(foldoutToggle);
            Pointer(panelRoot, EventType.MouseMove, p);
            Pointer(panelRoot, EventType.MouseDown, p);
            Pointer(panelRoot, EventType.MouseUp, p);
        }, foldout);
        Step("wheel scroll", root, () =>
        {
            var p = Center(scroll);
            Pointer(panelRoot, EventType.MouseMove, p);
            Wheel(panelRoot, p, new Vector2(0, 3));
        }, scroll);
        Step("select list row", root, () =>
        {
            var row = list.Query<Label>().ToList()[2];
            var p = Center(row);
            Pointer(panelRoot, EventType.MouseMove, p);
            Pointer(panelRoot, EventType.MouseDown, p);
            Pointer(panelRoot, EventType.MouseUp, p);
        }, list);
        Step("wheel list", root, () =>
        {
            var p = Center(list);
            Wheel(panelRoot, p, new Vector2(0, 2));
        }, list);
    }

    void Step(string name, VisualElement root, Action act, VisualElement watch = null)
    {
        m_Events.Clear();
        act();
        Update2();
        var s = new StringBuilder();
        s.Append("\"step\":").Append(Str(name)).Append(",\"events\":[").Append(string.Join(",", m_Events)).Append("]");
        var focused = root.panel.focusController.focusedElement as VisualElement;
        s.Append(",\"focused\":").Append(Str(Describe(focused)));
        s.Append(",\"clicks\":").Append(m_Clicks);
        s.Append(",\"toggle\":").Append(root.Q<Toggle>("toggle").value ? "true" : "false");
        var field = root.Q<TextField>("field");
        s.Append(",\"field\":").Append(Str(field.value)).Append(",\"fieldText\":").Append(Str(field.text))
            .Append(",\"cursor\":[").Append(field.cursorIndex).Append(",").Append(field.selectIndex).Append("]");
        s.Append(",\"slider\":").Append(F(root.Q<Slider>("slider").value));
        s.Append(",\"foldout\":").Append(root.Q<Foldout>("foldout").value ? "true" : "false");
        s.Append(",\"scroll\":").Append(V2(root.Q<ScrollView>("scroll").scrollOffset));
        s.Append(",\"listScroll\":").Append(V2(root.Q<ListView>("list").Q<ScrollView>().scrollOffset));
        s.Append(",\"listSelected\":").Append(root.Q<ListView>("list").selectedIndex);
        if (watch != null)
        {
            s.Append(",\"watch\":[");
            bool first = true;
            foreach (var e in Flatten(watch))
            {
                if (!first) s.Append(",");
                first = false;
                s.Append("{\"e\":").Append(Str(Describe(e))).Append(",\"world\":").Append(Rect(e.worldBound))
                    .Append(",\"bg\":").Append(Col(e.resolvedStyle.backgroundColor)).Append(",\"display\":\"").Append(e.resolvedStyle.display).Append("\"}");
            }
            s.Append("]");
        }
        Line("step", s.ToString());
    }

    static IEnumerable<VisualElement> Flatten(VisualElement e)
    {
        yield return e;
        foreach (var c in e.Children())
            foreach (var d in Flatten(c))
                yield return d;
    }

    static void Pointer(VisualElement panelRoot, EventType type, Vector2 position)
    {
        var imgui = new Event { type = type, mousePosition = position, button = 0, clickCount = type == EventType.MouseMove ? 0 : 1 };
        EventBase evt = type == EventType.MouseDown ? PointerDownEvent.GetPooled(imgui)
            : type == EventType.MouseUp ? PointerUpEvent.GetPooled(imgui)
            : (EventBase)PointerMoveEvent.GetPooled(imgui);
        using (evt) panelRoot.SendEvent(evt);
    }

    static void Wheel(VisualElement panelRoot, Vector2 position, Vector2 delta)
    {
        var imgui = new Event { type = EventType.ScrollWheel, mousePosition = position, delta = delta };
        using (var evt = WheelEvent.GetPooled(imgui)) panelRoot.SendEvent(evt);
    }

    /// <summary>A key press as the event system sends it: no target, so the panel's leaf focused
    /// element receives it.</summary>
    static void Key(VisualElement panelRoot, char c, KeyCode key)
    {
        using (var down = KeyDownEvent.GetPooled(c, key, EventModifiers.None))
            panelRoot.SendEvent(down);
        if (key != KeyCode.None)
        {
            using (var up = KeyUpEvent.GetPooled(c, key, EventModifiers.None))
                panelRoot.SendEvent(up);
        }
    }

    // ---- panel update and rendering (internal API: what the player loop calls) ----

    static readonly Type s_Utility = typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.UIElementsRuntimeUtility");

    static void Invoke(string method, params object[] args)
    {
        var m = s_Utility.GetMethod(method, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        m.Invoke(null, args);
    }

    /// <summary>Two panel updates: list views fill their pool after the first layout.</summary>
    static void Update2()
    {
        Invoke("UpdatePanels");
        Invoke("UpdatePanels");
    }

    void Render(string name)
    {
        Invoke("RepaintPanels", false);
        Invoke("RenderOffscreenPanels");
        var previous = RenderTexture.active;
        RenderTexture.active = m_Target;
        var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
        texture.ReadPixels(new UnityEngine.Rect(0, 0, Width, Height), 0, 0);
        texture.Apply();
        RenderTexture.active = previous;
        File.WriteAllBytes(m_Base + "-" + name + ".png", texture.EncodeToPNG());
    }

    // ---- the tree ----

    void RecordTree(string scenario, VisualElement top)
    {
        int index = 0;
        void Visit(VisualElement e, int depth)
        {
            var s = new StringBuilder();
            s.Append("\"scenario\":\"").Append(scenario).Append("\",\"i\":").Append(index++).Append(",\"d\":").Append(depth)
                .Append(",\"type\":\"").Append(e.GetType().Name).Append("\",\"name\":").Append(Str(e.name))
                .Append(",\"classes\":[");
            bool first = true;
            foreach (var c in e.GetClasses())
            {
                if (!first) s.Append(",");
                first = false;
                s.Append(Str(c));
            }
            s.Append("],\"layout\":").Append(Rect(e.layout)).Append(",\"world\":").Append(Rect(e.worldBound));
            if (e is TextElement t) s.Append(",\"text\":").Append(Str(t.text));
            var r = e.resolvedStyle;
            s.Append(",\"style\":{")
                .Append("\"display\":\"").Append(r.display).Append("\"")
                .Append(",\"visibility\":\"").Append(r.visibility).Append("\"")
                .Append(",\"position\":\"").Append(r.position).Append("\"")
                .Append(",\"opacity\":").Append(F(r.opacity))
                .Append(",\"flexDirection\":\"").Append(r.flexDirection).Append("\"")
                .Append(",\"flexGrow\":").Append(F(r.flexGrow))
                .Append(",\"flexShrink\":").Append(F(r.flexShrink))
                .Append(",\"alignItems\":\"").Append(r.alignItems).Append("\"")
                .Append(",\"justifyContent\":\"").Append(r.justifyContent).Append("\"")
                .Append(",\"margin\":[").Append(F(r.marginLeft)).Append(",").Append(F(r.marginTop)).Append(",").Append(F(r.marginRight)).Append(",").Append(F(r.marginBottom)).Append("]")
                .Append(",\"padding\":[").Append(F(r.paddingLeft)).Append(",").Append(F(r.paddingTop)).Append(",").Append(F(r.paddingRight)).Append(",").Append(F(r.paddingBottom)).Append("]")
                .Append(",\"border\":[").Append(F(r.borderLeftWidth)).Append(",").Append(F(r.borderTopWidth)).Append(",").Append(F(r.borderRightWidth)).Append(",").Append(F(r.borderBottomWidth)).Append("]")
                .Append(",\"radius\":[").Append(F(r.borderTopLeftRadius)).Append(",").Append(F(r.borderTopRightRadius)).Append(",").Append(F(r.borderBottomRightRadius)).Append(",").Append(F(r.borderBottomLeftRadius)).Append("]")
                .Append(",\"color\":").Append(Col(r.color))
                .Append(",\"backgroundColor\":").Append(Col(r.backgroundColor))
                .Append(",\"borderColors\":[").Append(Col(r.borderLeftColor)).Append(",").Append(Col(r.borderTopColor)).Append(",").Append(Col(r.borderRightColor)).Append(",").Append(Col(r.borderBottomColor)).Append("]")
                .Append(",\"fontSize\":").Append(F(r.fontSize))
                .Append(",\"fontStyle\":\"").Append(r.unityFontStyleAndWeight).Append("\"")
                .Append(",\"textAlign\":\"").Append(r.unityTextAlign).Append("\"")
                .Append(",\"whiteSpace\":\"").Append(r.whiteSpace).Append("\"")
                .Append(",\"font\":").Append(Str(r.unityFontDefinition.fontAsset != null ? r.unityFontDefinition.fontAsset.name : r.unityFontDefinition.font != null ? r.unityFontDefinition.font.name : r.unityFont != null ? r.unityFont.name : null))
                .Append(",\"backgroundImage\":").Append(Str(r.backgroundImage.texture != null ? r.backgroundImage.texture.name : null))
                .Append(",\"unityBackgroundImageTintColor\":").Append(Col(r.unityBackgroundImageTintColor))
                .Append(",\"minWidth\":").Append(Str(e.resolvedStyle.minWidth.ToString()))
                .Append(",\"minHeight\":").Append(Str(e.resolvedStyle.minHeight.ToString()))
                .Append(",\"pickingMode\":\"").Append(e.pickingMode).Append("\"")
                .Append("}");
            Line("tree", s.ToString());
            foreach (var c in e.hierarchy.Children()) Visit(c, depth + 1);
        }
        Visit(top, 0);
    }

    // ---- JSON ----

    void Line(string kind, string body) => m_Out.Append("{\"k\":\"").Append(kind).Append("\",").Append(body).Append("}\n");

    static string Describe(VisualElement e)
    {
        if (e == null) return null;
        return e.GetType().Name + (string.IsNullOrEmpty(e.name) ? "" : "#" + e.name);
    }

    static string F(float f) => float.IsNaN(f) ? "\"NaN\"" : float.IsInfinity(f) ? (f > 0 ? "\"Inf\"" : "\"-Inf\"") : f.ToString("R", Inv);
    static string V2(Vector2 v) => "[" + F(v.x) + "," + F(v.y) + "]";
    static string V3(Vector3 v) => "[" + F(v.x) + "," + F(v.y) + "," + F(v.z) + "]";
    static string Rect(UnityEngine.Rect r) => "[" + F(r.x) + "," + F(r.y) + "," + F(r.width) + "," + F(r.height) + "]";
    static string Col(Color c) => "[" + F(c.r) + "," + F(c.g) + "," + F(c.b) + "," + F(c.a) + "]";

    static string Str(string s)
    {
        if (s == null) return "null";
        var b = new StringBuilder("\"");
        foreach (var ch in s)
        {
            if (ch == '"' || ch == '\\') b.Append('\\').Append(ch);
            else if (ch < 32) b.Append("\\u").Append(((int)ch).ToString("x4"));
            else b.Append(ch);
        }
        return b.Append('"').ToString();
    }
}
