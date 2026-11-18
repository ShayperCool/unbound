using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

// The element's internal style-property animation interface (UnityEngine.UIElements.IStylePropertyAnimations,
// reached through reflection) on a runtime panel driven by an explicit millisecond clock, as in
// tests/projects/uitransitionkinds: each typed start and what its value does over time, which starts
// are refused, retargeting and reversing a running animation, the running and completed counts and the
// animated property list, cancelling one or all animations (values and events), how a restyle treats an
// animation its transition list does not name, USS transitions seen through the interface, and an
// element outside a panel.
public class StyleAnimationsProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod] static void Install() { new GameObject("StyleAnimationsProbe").AddComponent<StyleAnimationsProbe>(); }
    [Serializable] class Entry { public string key; public string value; }
    static readonly BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static readonly Type Animations = typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.IStylePropertyAnimations");
    static readonly Type IdType = typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.StyleSheets.StylePropertyId");
    static readonly Func<float, float> Linear = t => t;
    static readonly Func<float, float> Quad = t => t * t;
    static long clock, origin;
    static long Clock() => clock;
    static Type panelType;
    static object panel;
    static VisualElement root, parent, box;
    static readonly List<string> events = new List<string>();
    StreamWriter output;

    void Put(string key, string value) => output.WriteLine(JsonUtility.ToJson(new Entry { key = key, value = value }));

    static string N(float v) => v.ToString("R", Inv);
    static string N(double v) => v.ToString("R", Inv);
    static string L(Length l) => N(l.value) + (l.unit == LengthUnit.Percent ? "%" : "px");
    static string C(Color c) => N(c.r) + "," + N(c.g) + "," + N(c.b) + "," + N(c.a);

    void Start()
    {
        string path = Environment.GetEnvironmentVariable("STYLE_ANIMATIONS_OUT"); if (string.IsNullOrEmpty(path)) return;
        output = new StreamWriter(path);
        bool failed = false;
        panelType = typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.Panel");
        var clockProperty = panelType.GetProperty("TimeSinceStartup", F);
        var saved = clockProperty.GetValue(null);
        clockProperty.SetValue(null, Delegate.CreateDelegate(clockProperty.PropertyType, typeof(StyleAnimationsProbe).GetMethod("Clock", F)));
        try { Record(); }
        catch (Exception ex) { Debug.LogException(ex); failed = true; }
        finally { clockProperty.SetValue(null, saved); }
        if (!failed) Put("complete", "1");
        output.Dispose();
        EditorApplication.Exit(failed ? 1 : 0);
    }

    static void Invoke(string name) => panelType.GetMethod(name, F).Invoke(panel, null);
    static void Update() { Invoke("ApplyStyles"); Invoke("UpdateAnimations"); Invoke("ValidateLayout"); }

    static void Watch(VisualElement e)
    {
        e.RegisterCallback<TransitionRunEvent>(t => events.Add("run:" + Names(t) + "@" + N(t.elapsedTime)));
        e.RegisterCallback<TransitionStartEvent>(t => events.Add("start:" + Names(t) + "@" + N(t.elapsedTime)));
        e.RegisterCallback<TransitionEndEvent>(t => events.Add("end:" + Names(t) + "@" + N(t.elapsedTime)));
        e.RegisterCallback<TransitionCancelEvent>(t => events.Add("cancel:" + Names(t) + "@" + N(t.elapsedTime)));
    }

    static string Names<T>(TransitionEventBase<T> t) where T : TransitionEventBase<T>, new() =>
        string.Join(",", t.stylePropertyNames.Select(n => n.ToString()));

    static string Events()
    {
        var result = "[" + string.Join(" ", events) + "]";
        events.Clear();
        return result;
    }

    // A fresh panel: root 400x300, a parent filling it, a 200x100 box watched for transition events.
    static void NewPanel()
    {
        clock = origin = 10000;
        events.Clear();
        var ctor = panelType.GetConstructors(F).First(c => c.GetParameters().Length == 4);
        var dispatcher = typeof(EventDispatcher).GetMethod("CreateDefault", F).Invoke(null, null);
        panel = ctor.Invoke(new object[] { null, ContextType.Player, dispatcher, null });
        root = ((IPanel)panel).visualTree;
        root.style.width = 400;
        root.style.height = 300;
        root.styleSheets.Add(Resources.Load<StyleSheet>("Anims"));
        parent = new VisualElement { name = "parent" };
        parent.style.width = 400;
        parent.style.height = 300;
        box = new VisualElement { name = "box" };
        box.style.width = 200;
        box.style.height = 100;
        box.style.flexShrink = 0;
        parent.Add(box);
        root.Add(parent);
        Update();
        Watch(box);
    }

    static void Close() => ((IDisposable)panel).Dispose();

    static object Id(string name) => Enum.Parse(IdType, name);

    static object Call(VisualElement e, string method, params object[] args)
    {
        var m = Animations.GetMethod(method, args.Select(a => a.GetType()).ToArray());
        if (m == null) throw new MissingMethodException(method + "(" + string.Join(",", args.Select(a => a.GetType().Name)) + ")");
        try { return m.Invoke(e, args); }
        catch (TargetInvocationException ex) { throw ex.InnerException; }
    }

    static string Try(Func<object> action)
    {
        try { var r = action(); return r == null ? "ok" : r is bool b ? (b ? "true" : "false") : r.ToString(); }
        catch (Exception ex) { return ex.GetType().Name; }
    }

    static string Start<T>(VisualElement e, string id, T from, T to, int durationMs, int delayMs, Func<float, float> easing = null, string method = "Start") =>
        Try(() => Call(e, method, Id(id), from, to, durationMs, delayMs, easing ?? Linear));
    static string Start<T>(string id, T from, T to, int durationMs, int delayMs, Func<float, float> easing = null, string method = "Start") =>
        Start(box, id, from, to, durationMs, delayMs, easing, method);

    static int Count(VisualElement e, string name) => (int)Animations.GetProperty(name).GetValue(e);
    static void SetCount(VisualElement e, string name, int value) => Animations.GetProperty(name).SetValue(e, value);

    static string All(VisualElement e)
    {
        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(IdType));
        Call(e, "GetAllAnimations", list);
        // The interface leaves the order unspecified; the probe records the set.
        return string.Join(",", list.Cast<object>().Select(o => o.ToString()).OrderBy(s => s, StringComparer.Ordinal));
    }

    static string State(VisualElement e, string id) =>
        "run " + Count(e, "runningAnimationCount") + " done " + Count(e, "completedAnimationCount")
        + " has " + Try(() => Call(e, "HasRunningAnimation", Id(id))) + " all [" + All(e) + "]";

    void Row(string key, string id, Func<string> read) => Put(key, read() + " | " + State(box, id) + " hints " + (int)box.usageHints + " " + Events());

    // Advances the clock to `ms` after the case's origin, runs a panel update and records a row.
    void At(string key, long ms, string id, Func<string> read)
    {
        clock = origin + ms;
        Update();
        Row(key + "." + ms, id, read);
    }

    static string Opacity() => "opacity " + N(box.resolvedStyle.opacity);
    static string Width() => "width " + N(box.resolvedStyle.width);

    void Kind<T>(string name, string id, T from, T to, Func<string> read, string method = "Start")
    {
        NewPanel();
        Put(name + ".before", read());
        Put(name + ".start", Start(id, from, to, 1000, 0, Linear, method) + " " + read() + " | " + State(box, id) + " " + Events());
        foreach (var ms in new long[] { 0, 250, 500, 1000 }) At(name, ms, id, read);
        Close();
    }

    void Record()
    {
        // A float start with a custom easing curve: nothing changes before the next animation update;
        // UpdateAnimation applies the current (start) value at once.
        NewPanel();
        Put("float.start", Start("Opacity", 0.5f, 0.1f, 1000, 0, Quad) + " " + Opacity() + " | " + State(box, "Opacity") + " " + Events());
        Put("float.update", Try(() => Call(box, "UpdateAnimation", Id("Opacity"))) + " " + Opacity());
        foreach (var ms in new long[] { 0, 250, 500, 1000, 1100 }) At("float", ms, "Opacity", Opacity);
        Put("float.sameEndAfterEnd", Start("Opacity", 0.5f, 0.1f, 1000, 0) + " | " + State(box, "Opacity"));
        Put("float.newEndAfterEnd", Start("Opacity", 0.1f, 0.9f, 1000, 0) + " | " + State(box, "Opacity"));
        At("float.newEnd", 1100, "Opacity", Opacity);
        Put("float.cancel", Try(() => Call(box, "CancelAnimation", Id("Opacity"))) + " " + Opacity() + " | " + State(box, "Opacity") + " " + Events());
        At("float.afterCancel", 1100, "Opacity", Opacity);
        Close();

        // Each typed start.
        Kind("length", "Width", new Length(200), Length.Percent(25), Width);
        Kind("color", "BackgroundColor", Color.red, Color.blue, () => "color " + C(box.resolvedStyle.backgroundColor));
        Kind("int", "UnitySliceLeft", 0, 5, () => "slice " + box.resolvedStyle.unitySliceLeft);
        Kind("enum", "FlexDirection", (int)FlexDirection.Column, (int)FlexDirection.Row, () => "direction " + box.resolvedStyle.flexDirection, "StartEnum");
        Kind("intOnEnum", "AlignItems", (int)Align.Auto, (int)Align.Stretch, () => "align " + box.resolvedStyle.alignItems);
        Kind("scale", "Scale", new Scale(Vector3.one), new Scale(new Vector3(2, 3, 1)), () => "scale " + box.resolvedStyle.scale.value);
        Kind("rotate", "Rotate", new Rotate(new Angle(0)), new Rotate(new Angle(90)), () => "rotate " + N(box.resolvedStyle.rotate.angle.value));
        Kind("translate", "Translate", new Translate(0, 0), new Translate(100, Length.Percent(50)),
            () => { var t = box.resolvedStyle.translate; return "translate " + N(t.x) + "," + N(t.y); });
        Kind("origin", "TransformOrigin", new TransformOrigin(0, 0), new TransformOrigin(100, 50),
            () => { var o = box.resolvedStyle.transformOrigin; return "origin " + N(o.x) + "," + N(o.y); });
        Kind("bgPosition", "BackgroundPositionX", new BackgroundPosition(BackgroundPositionKeyword.Left, 0), new BackgroundPosition(BackgroundPositionKeyword.Right, 10),
            () => { var p = box.resolvedStyle.backgroundPositionX; return "bgpos " + p.keyword + " " + L(p.offset); });
        Kind("bgRepeat", "BackgroundRepeat", new BackgroundRepeat(Repeat.Repeat, Repeat.Repeat), new BackgroundRepeat(Repeat.Space, Repeat.Round),
            () => { var r = box.resolvedStyle.backgroundRepeat; return "repeat " + r.x + "," + r.y; });
        Kind("bgSize", "BackgroundSize", new BackgroundSize(10, 20), new BackgroundSize(30, 60),
            () => { var s = box.resolvedStyle.backgroundSize; return "bgsize " + L(s.x) + " " + L(s.y); });
        Kind("background", "BackgroundImage", new Background(), Background.FromTexture2D(Texture2D.whiteTexture),
            () => "image " + (box.resolvedStyle.backgroundImage.texture != null ? box.resolvedStyle.backgroundImage.texture.name : "none"));

        // Starts that are refused.
        NewPanel();
        Put("refuse.sameValue", Start("Opacity", 0.5f, 0.5f, 1000, 0) + " | " + State(box, "Opacity"));
        Put("refuse.zeroDuration", Start("Opacity", 1f, 0f, 0, 0) + " | " + State(box, "Opacity"));
        Put("refuse.delayAtLeastDuration", Start("Opacity", 1f, 0f, 1000, -1000) + " | " + State(box, "Opacity"));
        Put("refuse.nan", Start("Width", new Length(float.NaN), new Length(100), 1000, 0) + " | " + State(box, "Width"));
        Put("refuse.fontSizeUnit", Start("FontSize", new Length(10), Length.Percent(50), 1000, 0) + " | " + State(box, "FontSize"));
        At("refuse", 0, "Opacity", Opacity);
        Close();

        // Delays: a zero duration with a positive delay ends without starting; a negative delay starts part way.
        NewPanel();
        Put("delayOnly.start", Start("Opacity", 1f, 0f, 0, 100) + " | " + State(box, "Opacity"));
        foreach (var ms in new long[] { 0, 50, 100, 150 }) At("delayOnly", ms, "Opacity", Opacity);
        Close();
        NewPanel();
        Put("negativeDelay.start", Start("Opacity", 1f, 0f, 1000, -500) + " | " + State(box, "Opacity"));
        foreach (var ms in new long[] { 0, 250, 500 }) At("negativeDelay", ms, "Opacity", Opacity);
        Close();

        // A running animation: the same end keeps it; its current value or a zero duration cancels it;
        // its own start value reverses it, shortened; another end retargets from the current value.
        foreach (var variant in new[] { "sameEnd", "toCurrent", "zero", "reverse", "other" })
        {
            NewPanel();
            Start("Opacity", 1f, 0f, 1000, 0);
            At("running." + variant, 0, "Opacity", Opacity);
            At("running." + variant, 500, "Opacity", Opacity);
            string result = variant switch
            {
                "sameEnd" => Start("Opacity", 1f, 0f, 1000, 0),
                "toCurrent" => Start("Opacity", 0.9f, 0.5f, 1000, 0),
                "zero" => Start("Opacity", 0.9f, 0.2f, 0, 0),
                "reverse" => Start("Opacity", 0.3f, 1f, 1000, 0),
                _ => Start("Opacity", 0.3f, 0.8f, 1000, 0),
            };
            Row("running." + variant + ".restart", "Opacity", () => result + " " + Opacity());
            foreach (var ms in new long[] { 500, 750, 1000, 1500 }) At("running." + variant + ".after", ms, "Opacity", Opacity);
            Close();
        }

        // Cancelling: one animation queues its Cancel for the next update; all of them send Cancel at once.
        // Both put the end values in place and clear the completed animations too.
        NewPanel();
        Start("Opacity", 1f, 0f, 1000, 0);
        Start("Width", new Length(200), new Length(100), 1000, 0);
        Start("BackgroundColor", Color.red, Color.blue, 100, 0);
        At("cancel", 0, "Opacity", Opacity);
        At("cancel", 250, "Opacity", () => Opacity() + " " + Width());
        Row("cancel.one", "Opacity", () => Try(() => Call(box, "CancelAnimation", Id("Opacity"))) + " " + Opacity() + " " + Width());
        Row("cancel.unknown", "Opacity", () => Try(() => Call(box, "CancelAnimation", Id("Height"))));
        Row("cancel.all", "Width", () => Try(() => Call(box, "CancelAllAnimations")) + " " + Opacity() + " " + Width());
        At("cancel.after", 250, "Width", () => Opacity() + " " + Width());
        Close();

        // USS transitions through the interface.
        NewPanel();
        box.style.transitionProperty = new List<StylePropertyName> { new StylePropertyName("opacity") };
        box.style.transitionDuration = new List<TimeValue> { new TimeValue(1000, TimeUnit.Millisecond) };
        box.style.transitionTimingFunction = new List<EasingFunction> { EasingMode.Linear };
        Update();
        Events();
        box.style.opacity = 0;
        At("uss", 0, "Opacity", Opacity);
        At("uss", 500, "Opacity", Opacity);
        Row("uss.cancel", "Opacity", () => Try(() => Call(box, "CancelAnimation", Id("Opacity"))) + " " + Opacity());
        At("uss.afterCancel", 500, "Opacity", Opacity);
        box.style.opacity = 1;
        At("uss.again", 500, "Opacity", Opacity);
        At("uss.again", 1500, "Opacity", Opacity);
        // A completed transition ends at 1. A zero duration leaves opacity without a transition, which
        // forgets the completed one: going to 0 at once and back to 1 with a duration runs again.
        box.style.transitionDuration = new List<TimeValue> { new TimeValue(0, TimeUnit.Millisecond) };
        box.style.opacity = 0;
        At("uss.instant", 1500, "Opacity", Opacity);
        box.style.transitionDuration = new List<TimeValue> { new TimeValue(1000, TimeUnit.Millisecond) };
        box.style.opacity = 1;
        At("uss.backToCompleted", 1500, "Opacity", Opacity);
        At("uss.backToCompleted", 2000, "Opacity", Opacity);
        box.style.opacity = 0.5f;
        At("uss.otherEnd", 2000, "Opacity", Opacity);
        At("uss.otherEnd", 2500, "Opacity", Opacity);
        Row("uss.cancelAll", "Opacity", () => Try(() => Call(box, "CancelAllAnimations")) + " " + Opacity());
        Close();

        // A restyle with no transitions keeps an animation its list does not name: the style value
        // shows until the next animation update (or UpdateAnimation). With transitions declared the
        // animated value stays; changing the transition list cancels it.
        foreach (var variant in new[] { "none", "listed", "listChanged" })
        {
            NewPanel();
            if (variant != "none") { box.AddToClassList("tw"); Update(); }
            Start("Opacity", 1f, 0.2f, 1000, 0);
            At("restyle." + variant, 0, "Opacity", Opacity);
            At("restyle." + variant, 500, "Opacity", Opacity);
            box.AddToClassList("shade");
            if (variant == "listChanged") { box.RemoveFromClassList("tw"); box.AddToClassList("th"); }
            Invoke("ApplyStyles");
            Row("restyle." + variant + ".styled", "Opacity", () => Opacity() + " border " + N(box.resolvedStyle.borderLeftWidth));
            Row("restyle." + variant + ".updateAnimation", "Opacity", () => Try(() => Call(box, "UpdateAnimation", Id("Opacity"))) + " " + Opacity());
            At("restyle." + variant + ".after", 500, "Opacity", Opacity);
            At("restyle." + variant + ".after", 750, "Opacity", Opacity);
            Close();
        }

        // The counts are plain settable values the animations add to and take from; HasRunningAnimation,
        // GetAllAnimations and CancelAllAnimations only look further while one of them is positive.
        NewPanel();
        SetCount(box, "runningAnimationCount", 3);
        Row("counts.setIdle", "Opacity", () => "set");
        SetCount(box, "runningAnimationCount", 0);
        Start("Opacity", 1f, 0f, 1000, 0);
        At("counts", 0, "Opacity", Opacity);
        SetCount(box, "runningAnimationCount", 0);
        Row("counts.zeroedWhileRunning", "Opacity", () => "zeroed");
        Row("counts.cancelAllWhileZeroed", "Opacity", () => Try(() => Call(box, "CancelAllAnimations")) + " " + Opacity());
        At("counts.afterZeroed", 1000, "Opacity", Opacity);
        // Leaving the panel cancels the element's animations, which checks that both counts reach zero.
        SetCount(box, "runningAnimationCount", 0);
        Close();

        // An element outside a panel.
        var loose = new VisualElement();
        Put("detached.start", Start(loose, "Opacity", 1f, 0f, 1000, 0));
        Put("detached.has", Try(() => Call(loose, "HasRunningAnimation", Id("Opacity"))));
        Put("detached.update", Try(() => Call(loose, "UpdateAnimation", Id("Opacity"))));
        Put("detached.cancel", Try(() => Call(loose, "CancelAnimation", Id("Opacity"))));
        Put("detached.cancelAll", Try(() => Call(loose, "CancelAllAnimations")));
        Put("detached.all", Try(() => All(loose)));
        Put("detached.counts", Count(loose, "runningAnimationCount") + " " + Count(loose, "completedAnimationCount"));
    }
}
