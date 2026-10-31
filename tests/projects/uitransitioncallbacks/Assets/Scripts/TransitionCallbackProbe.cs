using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

// Each panel has its own explicit millisecond timeline. No Editor frames or sleeps enter the oracle.
public class TransitionCallbackProbe : MonoBehaviour
{
    static readonly BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static long clock;
    static Type panelType;
    static object panel;
    static VisualElement root, a, b, holder;
    static string scenario, trigger, action;
    static bool acted;
    static readonly List<Row> rows = new List<Row>();
    static readonly List<Observation> events = new List<Observation>();
    static EventCallback<TransitionEndEvent> lateEnd;
    [Serializable] public class Observation
    {
        public string kind, target, who, phase, properties;
        public double elapsed;
        public bool bubbles, trickles, width;
        public float[] values;
    }
    [Serializable] public class Row
    {
        public string scenario;
        public long ms;
        public float[] values;
        public Observation[] events;
    }
    void Start() { Run(); Application.Quit(); }
    static long Clock() => clock;
    public static void Run()
    {
        rows.Clear();
        panelType = typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.Panel");
        var time = panelType.GetProperty("TimeSinceStartup", F);
        var saved = time.GetValue(null);
        time.SetValue(null, Delegate.CreateDelegate(time.PropertyType, typeof(TransitionCallbackProbe).GetMethod("Clock", F)));
        try
        {
            foreach (bool child in new[] { false, true })
            foreach (bool reverseCreate in new[] { false, true })
            foreach (bool reverseStart in new[] { false, true })
            foreach (string types in new[] { "width", "opacity", "mixed", "split" })
            {
                Setup("order/" + child + "/" + reverseCreate + "/" + reverseStart + "/" + types, child, reverseCreate);
                Configure(a, types == "split" ? "opacity" : types == "mixed" ? "width, opacity, background-color, translate" : types);
                Configure(b, types == "split" ? "width" : types == "mixed" ? "opacity, width, translate, background-color" : types);
                Update(); events.Clear();
                if (reverseStart) { b.AddToClassList("changed"); a.AddToClassList("changed"); }
                else { a.AddToClassList("changed"); b.AddToClassList("changed"); }
                Tick(0); Tick(250);
                a.AddToClassList("third"); b.AddToClassList("third");
                Tick(250); Tick(500); Tick(1000); Tick(1250); Tick(1500); Done();
            }
            // Unlike reversing class assignment order before one style pass, these start actual
            // animation groups on separate updates and distinguish per-type owner registration.
            foreach (bool otherFirst in new[] { false, true })
            {
                Setup("stagger/" + otherFirst, false, false);
                Configure(a, "width, opacity"); Configure(b, "width, opacity"); Update(); events.Clear();
                var first = otherFirst ? b : a; var second = otherFirst ? a : b;
                first.style.width = 300; Tick(0);
                second.style.opacity = 0; Tick(0);
                first.style.opacity = 0; second.style.width = 300;
                Tick(0); Tick(250); Tick(500); Tick(1000); Tick(1250); Done();
            }
            foreach (string kind in new[] { "run", "start", "cancel", "end" })
            foreach (string mutation in new[] { "self", "other", "reverse", "properties", "detach-self", "detach-other", "reparent", "clear", "register", "unregister", "layout" })
            {
                Setup("callback/" + kind + "/" + mutation, false, false);
                Configure(a, "width, opacity"); Configure(b, "width, opacity"); Update(); events.Clear();
                trigger = kind; action = mutation;
                a.AddToClassList("changed"); b.AddToClassList("changed");
                Tick(0); Tick(100); Tick(250);
                if (kind == "cancel") a.AddToClassList("third");
                Tick(250); Tick(500); Tick(1000); Tick(1000); Tick(1250); Tick(1500); Tick(2000); Done();
            }
        }
        finally { time.SetValue(null, saved); }
        string output = Environment.GetEnvironmentVariable("UITRANSITIONCALLBACKS_OUT") ?? Path.Combine("Library", "Unbound", "transitioncallbacks");
        Directory.CreateDirectory(output);
        File.WriteAllLines(Path.Combine(output, "callbacks.jsonl"), rows.Select(r => JsonUtility.ToJson(r)).ToArray());
        Debug.Log("UITRANSITIONCALLBACKS: " + rows.Count + " samples");
    }
    static VisualElement Element(string name)
    {
        var e = new VisualElement { name = name }; e.AddToClassList("box"); return e;
    }
    static void Setup(string name, bool child, bool reverseCreate)
    {
        clock = 10000; scenario = name; trigger = action = null; acted = false; events.Clear();
        var dispatcherType = typeof(EventDispatcher);
        var dispatcher = dispatcherType.GetMethod("CreateDefault", F)?.Invoke(null, null) ?? Activator.CreateInstance(dispatcherType, true);
        panel = panelType.GetConstructors(F).First(c => c.GetParameters().Length == 4).Invoke(new object[] { null, ContextType.Player, dispatcher, null });
        root = ((IPanel)panel).visualTree; root.name = "root"; root.style.width = 800; root.style.height = 800;
        root.styleSheets.Add(Resources.Load<StyleSheet>("Transitions"));
        if (reverseCreate) { b = Element("b"); a = Element("a"); } else { a = Element("a"); b = Element("b"); }
        holder = new VisualElement { name = "holder" }; holder.style.width = 400; holder.style.height = 200;
        root.Add(holder); root.Add(a); if (child) a.Add(b); else root.Add(b);
        Update();
        foreach (var e in new[] { root, a, b, holder })
        { Watch(e, TrickleDown.TrickleDown); Watch(e, TrickleDown.NoTrickleDown); }
        lateEnd = e => Record("late-end", e, "late");
        a.RegisterCallback(lateEnd);
        events.Clear();
    }
    static void Configure(VisualElement e, string properties)
    {
        e.style.transitionProperty = properties.Split(',').Select(p => new StylePropertyName(p.Trim())).ToList();
        e.style.transitionDuration = new List<TimeValue> { new TimeValue(1000, TimeUnit.Millisecond) };
        e.style.transitionTimingFunction = new List<EasingFunction> { EasingMode.Linear };
    }
    static void Watch(VisualElement ve, TrickleDown td)
    {
        string who = ve.name + "/" + td;
        ve.RegisterCallback<TransitionRunEvent>(e => Record("run", e, who), td);
        ve.RegisterCallback<TransitionStartEvent>(e => Record("start", e, who), td);
        ve.RegisterCallback<TransitionCancelEvent>(e => Record("cancel", e, who), td);
        ve.RegisterCallback<TransitionEndEvent>(e => Record("end", e, who), td);
    }
    static float[] Values()
    {
        var x = a.resolvedStyle; var y = b.resolvedStyle;
        return new[] { x.width, x.opacity, x.backgroundColor.r, x.translate.x, y.width, y.opacity, y.backgroundColor.r, y.translate.x,
            a.panel == null ? 0f : 1f, b.panel == null ? 0f : 1f, a.parent == holder ? 1f : 0f };
    }
    static void Record<T>(string kind, TransitionEventBase<T> e, string who) where T : TransitionEventBase<T>, new()
    {
        events.Add(new Observation { kind = kind, target = ((VisualElement)e.target).name, who = who, phase = e.propagationPhase.ToString(),
            properties = string.Join(",", e.stylePropertyNames.Select(p => p.ToString())), elapsed = e.elapsedTime,
            bubbles = e.bubbles, trickles = e.tricklesDown, width = e.AffectsProperty("width"), values = Values() });
        if (!acted && kind == trigger && e.target == a && who == "a/NoTrickleDown")
        {
            acted = true;
            switch (action)
            {
                case "self": a.style.width = 200; break;
                case "other": b.style.width = 200; break;
                case "reverse": a.RemoveFromClassList("changed"); break;
                case "properties": a.style.transitionProperty = new List<StylePropertyName> { "opacity" }; break;
                case "detach-self": a.RemoveFromHierarchy(); break;
                case "detach-other": b.RemoveFromHierarchy(); break;
                case "reparent": holder.Add(a); break;
                case "clear": root.Clear(); break;
                case "register": a.RegisterCallback<TransitionEndEvent>(x => Record("added-end", x, "added")); break;
                case "unregister": a.UnregisterCallback(lateEnd); break;
                case "layout": b.style.width = 200; var bounds = root.worldBound; break;
            }
            events.Add(new Observation { kind = "after-" + action, target = "a", who = who, phase = e.propagationPhase.ToString(), properties = "", values = Values() });
        }
    }
    static void Update()
    {
        panelType.GetMethod("ApplyStyles", F).Invoke(panel, null);
        panelType.GetMethod("UpdateAnimations", F).Invoke(panel, null);
        panelType.GetMethod("ValidateLayout", F).Invoke(panel, null);
    }
    static void Tick(long ms)
    {
        clock = 10000 + ms; Update(); rows.Add(new Row { scenario = scenario, ms = ms, values = Values(), events = events.ToArray() }); events.Clear();
    }
    static void Done() { ((IDisposable)panel).Dispose(); events.Clear(); }
}
