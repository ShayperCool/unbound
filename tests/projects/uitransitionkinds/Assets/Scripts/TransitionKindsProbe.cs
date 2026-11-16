using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

// How USS transitions interpolate each kind of style value, on a runtime panel driven by an explicit
// millisecond clock (as in tests/projects/uitransitions): discrete values (background position,
// text-overflow, white-space), rotate between angle units, translate and transform-origin depth under an
// overshooting easing, lengths whose unit changes (against the parent's width or height, the
// parent's flex direction, the element's own width, a parent of no width, the text lengths that do
// not convert, background-size), percent font sizes from rules and inline, integer slices, and the usage hints a transition adds when it
// starts (inline, from a USS class and through `all`).
public class TransitionKindsProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod] static void Install() { new GameObject("TransitionKindsProbe").AddComponent<TransitionKindsProbe>(); }
    [Serializable] class Row { public string key; public string value; }
    static readonly BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static long clock;
    static long Clock() => clock;
    static Type panelType;
    static object panel;
    static VisualElement root, parent, box;
    static readonly List<string> events = new List<string>();
    StreamWriter output;

    void Put(string key, string value) => output.WriteLine(JsonUtility.ToJson(new Row { key = key, value = value }));

    static string N(float v) => v.ToString("R", Inv);
    static string L(Length l) => N(l.value) + (l.unit == LengthUnit.Percent ? "%" : "px");
    static string Hints(VisualElement e) => "hints " + (int)e.usageHints;

    void Start()
    {
        string path = Environment.GetEnvironmentVariable("TRANSITION_KINDS_OUT"); if (string.IsNullOrEmpty(path)) return;
        output = new StreamWriter(path);
        bool failed = false;
        panelType = typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.Panel");
        var clockProperty = panelType.GetProperty("TimeSinceStartup", F);
        var saved = clockProperty.GetValue(null);
        clockProperty.SetValue(null, Delegate.CreateDelegate(clockProperty.PropertyType, typeof(TransitionKindsProbe).GetMethod("Clock", F)));
        try { Record(); }
        catch (Exception ex) { Debug.LogException(ex); failed = true; }
        finally { clockProperty.SetValue(null, saved); }
        if (!failed) Put("complete", "1");
        output.Dispose();
        EditorApplication.Exit(failed ? 1 : 0);
    }

    static void Update()
    {
        panelType.GetMethod("ApplyStyles", F).Invoke(panel, null);
        panelType.GetMethod("UpdateAnimations", F).Invoke(panel, null);
        panelType.GetMethod("ValidateLayout", F).Invoke(panel, null);
    }

    static void Watch(VisualElement e)
    {
        e.RegisterCallback<TransitionRunEvent>(t => events.Add("run:" + Names(t.stylePropertyNames)));
        e.RegisterCallback<TransitionEndEvent>(t => events.Add("end:" + Names(t.stylePropertyNames)));
        e.RegisterCallback<TransitionCancelEvent>(t => events.Add("cancel:" + Names(t.stylePropertyNames)));
    }

    static string Names(StylePropertyNameCollection names) => string.Join(",", names.Select(n => n.ToString()));

    // A fresh panel: root 400x300 (column), a parent filling it unless configured, a 200x100 box.
    static void NewPanel(Action<VisualElement> configureParent = null)
    {
        clock = 10000;
        events.Clear();
        var ctor = panelType.GetConstructors(F).First(c => c.GetParameters().Length == 4);
        var dispatcher = typeof(EventDispatcher).GetMethod("CreateDefault", F).Invoke(null, null);
        panel = ctor.Invoke(new object[] { null, ContextType.Player, dispatcher, null });
        root = ((IPanel)panel).visualTree;
        root.style.width = 400;
        root.style.height = 300;
        root.styleSheets.Add(Resources.Load<StyleSheet>("Kinds"));
        parent = new VisualElement { name = "parent" };
        parent.style.width = 400;
        parent.style.height = 300;
        configureParent?.Invoke(parent);
        box = new VisualElement { name = "box" };
        box.style.width = 200;
        box.style.height = 100;
        box.style.flexShrink = 0;
        parent.Add(box);
        root.Add(parent);
        Update();
        Watch(box);
    }

    static void Transition(string properties, EasingMode easing = EasingMode.Linear)
    {
        box.style.transitionProperty = properties.Split(',').Select(p => new StylePropertyName(p.Trim())).ToList();
        box.style.transitionDuration = new List<TimeValue> { new TimeValue(1000, TimeUnit.Millisecond) };
        box.style.transitionDelay = new List<TimeValue> { new TimeValue(0, TimeUnit.Millisecond) };
        box.style.transitionTimingFunction = new List<EasingFunction> { easing };
        Update();
        events.Clear();
    }

    // Samples at 0, 250, 500, 750 and 1000 ms after the change, each with the events since the last.
    void Samples(string name, Func<string> read)
    {
        foreach (long ms in new long[] { 0, 250, 500, 750, 1000 })
        {
            clock = 10000 + ms;
            Update();
            Put(name + "." + ms, read() + " " + Hints(box) + " [" + string.Join(" ", events) + "]");
            events.Clear();
        }
        ((IDisposable)panel).Dispose();
    }

    void Case(string name, string properties, Action start, Action change, Func<string> read, EasingMode easing = EasingMode.Linear, Action<VisualElement> configureParent = null)
    {
        NewPanel(configureParent);
        start?.Invoke();
        Update();
        Transition(properties, easing);
        Put(name + ".before", read() + " " + Hints(box));
        change();
        Samples(name, read);
    }

    static string Width() => "width " + N(box.resolvedStyle.width);

    void Record()
    {
        // Discrete kinds.
        Case("bgposX", "background-position-x",
            () => box.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Left, 0),
            () => box.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Left, 100),
            () => { var p = box.resolvedStyle.backgroundPositionX; return "bgpos " + p.keyword + " " + L(p.offset); });
        Case("bgposY", "background-position-y",
            () => box.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Top, Length.Percent(10)),
            () => box.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Bottom, Length.Percent(60)),
            () => { var p = box.resolvedStyle.backgroundPositionY; return "bgpos " + p.keyword + " " + L(p.offset); });
        Case("textOverflow", "text-overflow",
            () => box.style.textOverflow = TextOverflow.Clip,
            () => box.style.textOverflow = TextOverflow.Ellipsis,
            () => "textOverflow " + box.resolvedStyle.textOverflow);
        Case("whiteSpace", "white-space",
            () => box.style.whiteSpace = WhiteSpace.Normal,
            () => box.style.whiteSpace = WhiteSpace.NoWrap,
            () => "whiteSpace " + box.resolvedStyle.whiteSpace);
        Case("sliceLeft", "-unity-slice-left",
            () => box.style.unitySliceLeft = 0,
            () => box.style.unitySliceLeft = 3,
            () => "slice " + box.resolvedStyle.unitySliceLeft);

        // Transforms.
        Case("rotateTurn", "rotate",
            () => box.style.rotate = new Rotate(new Angle(10)),
            () => box.style.rotate = new Rotate(new Angle(0.5f, AngleUnit.Turn)),
            () => { var r = box.resolvedStyle.rotate; return "rotate " + N(r.angle.value) + " " + r.angle.unit; });
        Case("translateBack", "translate",
            () => box.style.translate = new Translate(0, 0, 0),
            () => box.style.translate = new Translate(100, 0, 100),
            () => { var t = box.resolvedStyle.translate; return "translate " + N(t.x) + "," + N(t.y) + "," + N(t.z); },
            EasingMode.EaseOutBack);
        Case("originBack", "transform-origin",
            () => box.style.transformOrigin = new TransformOrigin(0, 0, 0),
            () => box.style.transformOrigin = new TransformOrigin(100, 50, 50),
            () => { var o = box.resolvedStyle.transformOrigin; return "origin " + N(o.x) + "," + N(o.y) + "," + N(o.z); },
            EasingMode.EaseInBack);

        // Lengths whose unit changes.
        Case("widthToPercent", "width", null, () => box.style.width = Length.Percent(75), Width);
        Case("widthToPixels", "width", () => box.style.width = Length.Percent(25), () => box.style.width = 300, Width);
        Case("zeroParentToPixels", "width", () => box.style.width = Length.Percent(50), () => box.style.width = 100, Width,
            configureParent: p => p.style.width = 0);
        Case("zeroParentToPercent", "width", () => box.style.width = 100, () => box.style.width = Length.Percent(50), Width,
            configureParent: p => p.style.width = 0);
        Case("heightToPercent", "height", null, () => box.style.height = Length.Percent(50), () => "height " + N(box.resolvedStyle.height));
        Case("marginTopToPercent", "margin-top", () => box.style.marginTop = 40, () => box.style.marginTop = Length.Percent(20),
            () => "marginTop " + N(box.resolvedStyle.marginTop));
        Case("flexBasisColumn", "flex-basis", () => box.style.flexBasis = 60, () => box.style.flexBasis = Length.Percent(50),
            () => "basis " + N(box.resolvedStyle.flexBasis.value) + " height " + N(box.resolvedStyle.height),
            configureParent: p => p.style.flexDirection = FlexDirection.Column);
        Case("flexBasisRow", "flex-basis", () => box.style.flexBasis = 60, () => box.style.flexBasis = Length.Percent(50),
            () => "basis " + N(box.resolvedStyle.flexBasis.value) + " width " + N(box.resolvedStyle.width),
            configureParent: p => p.style.flexDirection = FlexDirection.Row);
        Case("radiusToPercent", "border-top-left-radius", () => box.style.borderTopLeftRadius = 10, () => box.style.borderTopLeftRadius = Length.Percent(25),
            () => "radius " + N(box.resolvedStyle.borderTopLeftRadius));
        Case("fontSizeToPercent", "font-size", () => box.style.fontSize = 10, () => box.style.fontSize = Length.Percent(50),
            () => "fontSize " + N(box.resolvedStyle.fontSize));
        Case("letterSpacingToPercent", "letter-spacing", () => box.style.letterSpacing = 2, () => box.style.letterSpacing = Length.Percent(10),
            () => "letterSpacing " + N(box.resolvedStyle.letterSpacing));
        Case("translateToPercent", "translate", () => box.style.translate = new Translate(20, 10, 0), () => box.style.translate = new Translate(Length.Percent(50), Length.Percent(50), 0),
            () => { var t = box.resolvedStyle.translate; return "translate " + N(t.x) + "," + N(t.y); });
        Func<string> size = () => { var s = box.resolvedStyle.backgroundSize; return "bgsize " + s.sizeType + " " + L(s.x) + " " + L(s.y); };
        Case("bgSizeToPercent", "background-size", () => box.style.backgroundSize = new BackgroundSize(10, 10), () => box.style.backgroundSize = new BackgroundSize(Length.Percent(50), Length.Percent(50)), size);
        Case("bgSizeSameUnit", "background-size", () => box.style.backgroundSize = new BackgroundSize(10, 10), () => box.style.backgroundSize = new BackgroundSize(30, 50), size);
        Case("bgSizeFromAuto", "background-size", null, () => box.style.backgroundSize = new BackgroundSize(100, 50), size);

        // Percent font sizes: a USS rule's is a share of the parent's font size; an inline one (from
        // code or a UXML style attribute) keeps its number.
        NewPanel();
        box.style.fontSize = 20;
        var fromClass = new Label("Hg");
        fromClass.AddToClassList("font-half");
        var inline = new Label("Hg");
        inline.style.fontSize = Length.Percent(50);
        var pixels = new Label("Hg");
        pixels.style.fontSize = 50;
        box.Add(fromClass);
        box.Add(inline);
        box.Add(pixels);
        Resources.Load<VisualTreeAsset>("KindsFont").CloneTree(box);
        Update();
        Update();
        Func<VisualElement, string> font = e => N(e.resolvedStyle.fontSize);
        Put("fontPercent", "class " + font(fromClass) + " | inline " + font(inline) + " | uxml " + font(box.Q("uxmlPercent")) + " | pixels " + font(pixels) + " | uxmlPx " + font(box.Q("uxmlPixels")));
        // A child inherits the computed value: the number of an inline percentage, the pixels of a rule's.
        var inheritsInline = new VisualElement();
        inline.Add(inheritsInline);
        var inheritsClass = new VisualElement();
        fromClass.Add(inheritsClass);
        var halfOfInline = new VisualElement();
        halfOfInline.AddToClassList("font-half");
        inline.Add(halfOfInline);
        Update();
        Put("fontPercentChildren", "inline " + font(inheritsInline) + " | class " + font(inheritsClass) + " | classUnderInline " + font(halfOfInline));
        ((IDisposable)panel).Dispose();

        // Usage hints added when a transition starts.
        Case("hintColor", "color", () => box.style.color = Color.red, () => box.style.color = Color.blue,
            () => "color " + N(box.resolvedStyle.color.r));
        Case("hintBorderColor", "border-left-color", () => box.style.borderLeftColor = Color.red, () => box.style.borderLeftColor = Color.green,
            () => "border " + N(box.resolvedStyle.borderLeftColor.g));
        Case("hintScale", "scale", null, () => box.style.scale = new Scale(new Vector2(2, 2)),
            () => "scale " + N(box.resolvedStyle.scale.value.x));
        Case("hintWidth", "width", null, () => box.style.width = 300, Width);
        Case("hintNoStart", "background-color", () => box.style.backgroundColor = Color.red, () => box.style.backgroundColor = Color.red,
            () => "bg " + N(box.resolvedStyle.backgroundColor.r));
        Case("hintPreset", "translate", () => box.usageHints = UsageHints.GroupTransform, () => box.style.translate = new Translate(30, 0, 0),
            () => "translate " + N(box.resolvedStyle.translate.x));
        // From USS classes: a class with the transitions, then classes with the new values.
        NewPanel();
        box.AddToClassList("kinds");
        Update();
        events.Clear();
        Put("hintClass.before", Hints(box));
        box.AddToClassList("kinds-target");
        Samples("hintClass", () => "bg " + N(box.resolvedStyle.backgroundColor.b) + " scale " + N(box.resolvedStyle.scale.value.x));
        NewPanel();
        box.AddToClassList("kinds-all");
        Update();
        events.Clear();
        Put("hintAll.before", Hints(box));
        box.AddToClassList("kinds-all-target");
        Samples("hintAll", () => "border " + N(box.resolvedStyle.borderLeftColor.r) + " rotate " + N(box.resolvedStyle.rotate.angle.value));
    }
}
