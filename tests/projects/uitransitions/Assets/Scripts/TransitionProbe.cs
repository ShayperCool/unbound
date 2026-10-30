using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

public class TransitionProbe : MonoBehaviour
{
    static readonly BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static long clock;
    static readonly List<string> rows = new List<string>();
    static readonly List<string> events = new List<string>();
    static string scenario;
    static object panel;
    static VisualElement root, box;
    static Type panelType;
    static string N(float x) => x.ToString("0.######", CultureInfo.InvariantCulture);
    void Start() { Run(); Application.Quit(); }
    public static void Run()
    {
        rows.Clear();
        panelType = typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.Panel");
        var clockProperty = panelType.GetProperty("TimeSinceStartup", F);
        var saved = clockProperty.GetGetMethod(true)?.Invoke(null, null);
        clockProperty.SetValue(null, Delegate.CreateDelegate(clockProperty.PropertyType, typeof(TransitionProbe).GetMethod("Clock", F)));
        try
        {
            foreach (int delay in new[] { 0, 200, -250, -1000, -1500 })
            {
                Setup("delay" + delay, "width", 1000, delay);
                box.AddToClassList("changed");
                Tick(0); Tick(100); Tick(250); Tick(500); Tick(1000); Tick(1200); Tick(1500);
                Done();
            }
            foreach (int duration in new[] { 0, -100 })
            {
                Setup("duration" + duration, "width", duration, 200);
                box.AddToClassList("changed"); Tick(0); Tick(199); Tick(200); Tick(201); Done();
            }
            Setup("reverse", "width", 1000, 0);
            box.AddToClassList("changed"); Tick(0); Tick(400);
            box.RemoveFromClassList("changed"); Tick(400); Tick(600); Tick(800); Tick(1400); Done();
            Setup("interrupt", "width", 1000, 0);
            box.AddToClassList("changed"); Tick(0); Tick(400);
            box.AddToClassList("third"); Tick(400); Tick(900); Tick(1400); Done();
            Setup("delay-cancel", "width", 1000, 500);
            box.AddToClassList("changed"); Tick(0); Tick(200);
            box.RemoveFromClassList("changed"); Tick(200); Tick(800); Done();
            Setup("remove-property", "width", 1000, 0);
            box.AddToClassList("changed"); Tick(0); Tick(300);
            box.style.transitionProperty = new List<StylePropertyName> { "none" }; Tick(300); Tick(1100); Done();
            Setup("detach", "width", 1000, 0);
            box.AddToClassList("changed"); Tick(0); Tick(300);
            box.RemoveFromHierarchy(); Tick(300); root.Add(box); Tick(400); Tick(1400); Done();
            Setup("display", "width", 1000, 0);
            box.AddToClassList("changed"); Tick(0); Tick(300);
            box.style.display = DisplayStyle.None; Tick(300); Tick(1100); Done();
            foreach (string property in new[] { "all", "margin", "opacity, width", "width, all, width", "unknown" })
            {
                Setup("match-" + property, property, 1000, 0);
                box.AddToClassList("changed"); Tick(0); Tick(250); Tick(1000); Done();
            }
            Setup("lists", "width, opacity, background-color", 1000, 0);
            box.style.transitionDuration = new List<TimeValue> { new TimeValue(1000, TimeUnit.Millisecond), new TimeValue(500, TimeUnit.Millisecond) };
            box.style.transitionDelay = new List<TimeValue> { new TimeValue(-200, TimeUnit.Millisecond), new TimeValue(100, TimeUnit.Millisecond) };
            box.AddToClassList("changed"); Tick(0); Tick(250); Tick(600); Tick(800); Done();
            foreach (EasingMode mode in Enum.GetValues(typeof(EasingMode)))
            {
                Setup("ease-" + mode, "width", 1000, 0);
                box.style.transitionTimingFunction = new List<EasingFunction> { mode };
                box.AddToClassList("changed"); Tick(0); Tick(250); Tick(500); Tick(750); Tick(1000); Done();
            }
        }
        finally { clockProperty.SetValue(null, saved); }
        var output = Environment.GetEnvironmentVariable("UITRANSITIONS_OUT") ?? "Reference";
        Directory.CreateDirectory(output);
        File.WriteAllLines(Path.Combine(output, "transitions.jsonl"), rows);
        Debug.Log("UITRANSITIONS: " + rows.Count + " samples -> " + output);
    }
    static long Clock() => clock;
    static void Setup(string name, string properties, int duration, int delay)
    {
        clock = 10000; scenario = name; events.Clear();
        var ctor = panelType.GetConstructors(F).First(c => c.GetParameters().Length == 4);
        var dispatcherType = typeof(EventDispatcher);
        var dispatcher = dispatcherType.GetMethod("CreateDefault", F)?.Invoke(null, null) ?? Activator.CreateInstance(dispatcherType, true);
        panel = ctor.Invoke(new object[] { null, ContextType.Player, dispatcher, null });
        root = ((IPanel)panel).visualTree;
        root.style.width = 400; root.style.height = 400;
        box = new VisualElement { name = "box" };
        box.AddToClassList("box");
        root.styleSheets.Add(Resources.Load<StyleSheet>("Transitions"));
        root.Add(box);
        Update();
        box.style.transitionProperty = properties.Split(',').Select(p => new StylePropertyName(p.Trim())).ToList();
        box.style.transitionDuration = new List<TimeValue> { new TimeValue(duration, TimeUnit.Millisecond) };
        box.style.transitionDelay = new List<TimeValue> { new TimeValue(delay, TimeUnit.Millisecond) };
        box.style.transitionTimingFunction = new List<EasingFunction> { EasingMode.Linear };
        Update();
        Watch(root, "root", TrickleDown.TrickleDown); Watch(root, "root", TrickleDown.NoTrickleDown);
        Watch(box, "box", TrickleDown.TrickleDown); Watch(box, "box", TrickleDown.NoTrickleDown);
        events.Clear();
    }
    static void Watch(VisualElement ve, string who, TrickleDown td)
    {
        ve.RegisterCallback<TransitionRunEvent>(e => Event("run", e, who, td), td);
        ve.RegisterCallback<TransitionStartEvent>(e => Event("start", e, who, td), td);
        ve.RegisterCallback<TransitionEndEvent>(e => Event("end", e, who, td), td);
        ve.RegisterCallback<TransitionCancelEvent>(e => Event("cancel", e, who, td), td);
    }
    static void Event<T>(string kind, TransitionEventBase<T> e, string who, TrickleDown td) where T : TransitionEventBase<T>, new()
    {
        events.Add(kind + ":" + string.Join(",", e.stylePropertyNames.Select(p => p.ToString())) + ":" + e.elapsedTime.ToString("0.######", CultureInfo.InvariantCulture) + ":" + who + ":" + td + ":" + e.propagationPhase + ":" + e.bubbles + ":" + e.tricklesDown + ":" + e.AffectsProperty("width"));
    }
    static void Update()
    {
        panelType.GetMethod("ApplyStyles", F).Invoke(panel, null);
        panelType.GetMethod("UpdateAnimations", F).Invoke(panel, null);
        panelType.GetMethod("ValidateLayout", F).Invoke(panel, null);
    }
    [Serializable] class Row { public string scenario; public long ms; public float[] values; public string[] events; }
    static void Tick(long ms)
    {
        clock = 10000 + ms; Update();
        var s = box.resolvedStyle;
        rows.Add(JsonUtility.ToJson(new Row { scenario = scenario, ms = ms, values = new[] { s.width, s.opacity, s.backgroundColor.r, s.backgroundColor.g, s.marginLeft, s.translate.x, s.scale.value.x, s.rotate.angle.value }, events = events.ToArray() }));
        events.Clear();
    }
    static void Done() { ((IDisposable)panel).Dispose(); events.Clear(); }
}
