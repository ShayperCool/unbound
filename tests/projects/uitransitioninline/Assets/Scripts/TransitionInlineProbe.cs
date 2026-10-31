using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

public class TransitionInlineProbe : MonoBehaviour
{
    static readonly BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static Type panelType;
    static object panel;
    static VisualElement a, b;
    static long now;
    static string scenario, trigger;
    static bool acted, other;
    static readonly List<Row> rows = new List<Row>();
    static readonly List<Observation> events = new List<Observation>();
    [Serializable] public class Observation
    {
        public string kind, target, who, phase, properties;
        public double elapsed;
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
    static long Clock() => now;
    static float[] Values() => new[] { a.resolvedStyle.width, a.resolvedStyle.opacity, b.resolvedStyle.width, b.resolvedStyle.opacity };
    static void Sample()
    {
        rows.Add(new Row { scenario = scenario, ms = now - 10000, values = Values(), events = events.ToArray() });
        events.Clear();
    }
    static void Tick(long ms)
    {
        now = 10000 + ms;
        panelType.GetMethod("ApplyStyles", F).Invoke(panel, null);
        panelType.GetMethod("UpdateAnimations", F).Invoke(panel, null);
        panelType.GetMethod("ValidateLayout", F).Invoke(panel, null);
        Sample();
    }
    static VisualElement Element(string name)
    {
        var e = new VisualElement { name = name };
        e.style.width = 100; e.style.height = 50; e.style.opacity = 1;
        return e;
    }
    static void Configure(VisualElement e)
    {
        e.style.transitionProperty = new List<StylePropertyName> { "width", "opacity" };
        e.style.transitionDuration = new List<TimeValue> { new TimeValue(1000, TimeUnit.Millisecond) };
        e.style.transitionTimingFunction = new List<EasingFunction> { EasingMode.Linear };
    }
    static void Observe<T>(string kind, TransitionEventBase<T> e) where T : TransitionEventBase<T>, new()
    {
        events.Add(new Observation { kind = kind, target = ((VisualElement)e.target).name, who = ((VisualElement)e.currentTarget).name,
            phase = e.propagationPhase.ToString(), properties = string.Join(",", e.stylePropertyNames), elapsed = e.elapsedTime, values = Values() });
        if (acted || kind != trigger || e.target != a) return;
        acted = true;
        var target = other ? b : a;
        target.style.opacity = .5f;
        events.Add(new Observation { kind = "after-opacity", target = target.name, who = "mutation", phase = e.propagationPhase.ToString(), properties = "", values = Values() });
    }
    public static void Run()
    {
        rows.Clear();
        panelType = typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.Panel");
        var time = panelType.GetProperty("TimeSinceStartup", F); var saved = time.GetValue(null);
        time.SetValue(null, Delegate.CreateDelegate(time.PropertyType, typeof(TransitionInlineProbe).GetMethod("Clock", F)));
        try
        {
            foreach (string kind in new[] { "idle", "run", "start", "cancel", "end" })
            foreach (bool mutateOther in new[] { false, true })
            {
                scenario = kind + "/" + mutateOther; trigger = kind; other = mutateOther; acted = false; now = 10000; events.Clear();
                var dispatcher = typeof(EventDispatcher).GetMethod("CreateDefault", F).Invoke(null, null);
                panel = panelType.GetConstructors(F).First(c => c.GetParameters().Length == 4).Invoke(new object[] { null, ContextType.Player, dispatcher, null });
                var root = ((IPanel)panel).visualTree; root.style.width = 800; root.style.height = 800;
                a = Element("a"); b = Element("b"); root.Add(a); root.Add(b);
                Tick(0); Configure(a); Configure(b); Tick(0);
                foreach (var e in new[] { a, b })
                {
                    e.RegisterCallback<TransitionRunEvent>(x => Observe("run", x));
                    e.RegisterCallback<TransitionStartEvent>(x => Observe("start", x));
                    e.RegisterCallback<TransitionCancelEvent>(x => Observe("cancel", x));
                    e.RegisterCallback<TransitionEndEvent>(x => Observe("end", x));
                }
                a.style.width = 300; a.style.opacity = 0; b.style.width = 300; b.style.opacity = 0;
                if (kind == "idle") (other ? b : a).style.opacity = .5f;
                Sample(); Tick(0); Tick(250);
                if (kind == "cancel") a.style.opacity = .25f;
                Tick(250); Tick(500); Tick(1000); Tick(1000); Tick(1250); Tick(2000);
                ((IDisposable)panel).Dispose(); events.Clear();
            }
        }
        finally { time.SetValue(null, saved); }
        string output = Environment.GetEnvironmentVariable("UITRANSITIONINLINE_OUT") ?? Path.Combine("Library", "Unbound", "transitioninline");
        Directory.CreateDirectory(output);
        File.WriteAllLines(Path.Combine(output, "inline.jsonl"), rows.Select(r => JsonUtility.ToJson(r)));
    }
}
