using System;
using System.Globalization;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public class RuntimeProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod] static void Install() { new GameObject("RuntimeProbe").AddComponent<RuntimeProbe>(); }
    [Serializable] class Row { public string key; public string value; }
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static Type U(string name) => typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements." + name, true);
    static object Call(Type type, string name, object instance, params object[] args) => type.GetMethod(name, All).Invoke(instance, args);
    StreamWriter output;
    int notifications;
    object lastPanel;
    void Put(string key, object value) => output.WriteLine(JsonUtility.ToJson(new Row { key = key, value = Convert.ToString(value, CultureInfo.InvariantCulture) }));
    void Try(string key, Func<object> action)
    {
        try { Put(key, action()); }
        catch (Exception ex) { while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException; Put(key, ex.GetType().FullName); }
    }
    void Created<T>(T panel) { notifications++; lastPanel = panel; }
    void Start()
    {
        string path = Environment.GetEnvironmentVariable("UITK_RUNTIME_OUT"); if (string.IsNullOrEmpty(path)) return;
        try
        {
            using (output = new StreamWriter(path)) { Record(); Put("complete", 1); }
            EditorApplication.Exit(0);
        }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }
    static string V(Vector3 value) => value.x.ToString("R", CultureInfo.InvariantCulture) + "," + value.y.ToString("R", CultureInfo.InvariantCulture) + "," + value.z.ToString("R", CultureInfo.InvariantCulture);
    void EventValues(string key, EventBase e)
    {
        Put(key+".type", e.GetType().Name); Put(key+".bubbles", e.bubbles); Put(key+".trickles", e.tricklesDown);
        if (e is IPointerEvent p)
        {
            Put(key+".position", V(p.position)); Put(key+".delta", V(p.deltaPosition)); Put(key+".button", p.button);
            Put(key+".pressed", p.pressedButtons); Put(key+".clicks", p.clickCount); Put(key+".modifiers", (int)p.modifiers);
        }
        if (e is IMouseEvent m)
        {
            Put(key+".position", V(m.mousePosition)); Put(key+".local", V(m.localMousePosition)); Put(key+".delta", V(m.mouseDelta));
            Put(key+".button", m.button); Put(key+".pressed", m.pressedButtons); Put(key+".clicks", m.clickCount); Put(key+".modifiers", (int)m.modifiers);
        }
        if (e is IKeyboardEvent k) { Put(key+".key", k.keyCode); Put(key+".character", (int)k.character); Put(key+".modifiers", (int)k.modifiers); }
        if (e is ICommandEvent c) Put(key+".command", c.commandName);
        if (e is WheelEvent w) Put(key+".wheel", V(w.delta));
    }
    void Record()
    {
        var utility = U("UIElementsRuntimeUtility");
        var create = utility.GetMethod("CreateEvent", All);
        var navigation = U("KeyboardEventExtensions").GetMethod("ShouldSendNavigationMoveEventRuntime", All);
        var seenTypes = new HashSet<int>();
        foreach (EventType type in Enum.GetValues(typeof(EventType)))
        {
            if (!seenTypes.Add((int)type)) continue;
            var system = new Event { type = type, mousePosition = new Vector2(13, 27), delta = new Vector2(-2, 3), button = 0,
                clickCount = 2, keyCode = KeyCode.Tab, character = 't', modifiers = EventModifiers.Shift, commandName = "ProbeCommand" };
            using (var e = (EventBase)create.Invoke(null, new object[] { system })) EventValues("event."+(int)type, e);
        }
        foreach (var step in new[] { new[] { EventType.MouseDown, EventType.MouseDown, EventType.MouseUp, EventType.MouseUp },
                                    new[] { EventType.MouseDown, EventType.MouseLeaveWindow, EventType.MouseUp, EventType.MouseMove } })
        {
            string group = step[1] == EventType.MouseDown ? "chord" : "leave";
            for (int i = 0; i < step.Length; i++)
            {
                var system = new Event { type = step[i], button = i == 1 || i == 2 ? 1 : 0 };
                using (var e = (EventBase)create.Invoke(null, new object[] { system })) EventValues(group+"."+i, e);
            }
        }
        // Converting a used event follows its current raw type.
        var used = new Event { type = EventType.KeyDown, keyCode = KeyCode.A }; used.Use();
        using (var e = (EventBase)create.Invoke(null, new object[] { used })) EventValues("event.used-key", e);
        Try("event.null", () => create.Invoke(null, new object[] { null }));
        foreach (var type in new[] { EventType.KeyDown, EventType.KeyUp, EventType.MouseDown, EventType.Used })
            foreach (var code in new[] { KeyCode.Tab, KeyCode.LeftArrow, KeyCode.Return, KeyCode.None })
                foreach (var modifiers in new[] { EventModifiers.None, EventModifiers.Shift, EventModifiers.Control | EventModifiers.Alt | EventModifiers.Command | EventModifiers.FunctionKey })
                    Put("navigation."+(int)type+"."+code+"."+(int)modifiers, navigation.Invoke(null, new object[] { new Event { type = type, keyCode = code, modifiers = modifiers } }));
        Try("navigation.null", () => navigation.Invoke(null, new object[] { null }));
        var poolWheel = typeof(WheelEvent).GetMethod("GetPooled", All, null, new[] { typeof(Vector3), typeof(IPointerEvent) }, null);
        using (var pointer = PointerMoveEvent.GetPooled(new Event { type = EventType.MouseMove, mousePosition = new Vector2(4, 9), delta = new Vector2(2, -7), button = -1, clickCount = 3, modifiers = EventModifiers.Control }))
        {
            pointer.target = new VisualElement { name = "target" };
            using (var wheel = (WheelEvent)poolWheel.Invoke(null, new object[] { new Vector3(1, -3, 5), pointer }))
            {
                EventValues("wheel.pointer", wheel); Put("wheel.target", ReferenceEquals(wheel.target, pointer.target));
            }
        }
        Try("wheel.null", () => { using (var wheel = (WheelEvent)poolWheel.Invoke(null, new object[] { Vector3.one, null })) return wheel.GetType().Name; });
        using (var wheel = WheelEvent.GetPooled()) EventValues("wheel.reset", wheel);
        var detector = U("ClickDetector"); var clickTime = detector.GetProperty("s_DoubleClickTime", All);
        int previous = (int)clickTime.GetValue(null); Put("click.initial", previous);
        clickTime.SetValue(null, 173); Activator.CreateInstance(detector, true); Put("click.custom-retained", clickTime.GetValue(null));
        clickTime.SetValue(null, -1); Activator.CreateInstance(detector, true); Put("click.reinitialized", clickTime.GetValue(null));
        clickTime.SetValue(null, previous);
        var created = utility.GetEvent("onCreatePanel", All);
        var handler = Delegate.CreateDelegate(created.EventHandlerType, this, GetType().GetMethod("Created", All).MakeGenericMethod(U("BaseRuntimePanel")));
        created.AddEventHandler(null, handler);
        var factoryType = utility.GetNestedType("CreateRuntimePanelDelegate", All);
        var factory = Delegate.CreateDelegate(factoryType, U("RuntimePanel").GetMethod("Create", All));
        var owner = ScriptableObject.CreateInstance<PanelSettings>();
        var owner2 = ScriptableObject.CreateInstance<PanelSettings>();
        object panel = Call(utility, "FindOrCreateRuntimePanel", null, owner, factory);
        Put("panel.created", notifications); Put("panel.notification-instance", ReferenceEquals(panel, lastPanel));
        Put("panel.cached", ReferenceEquals(panel, Call(utility, "FindOrCreateRuntimePanel", null, owner, factory))); Put("panel.cached-notifications", notifications);
        var selectable = U("BaseRuntimePanel").GetProperty("selectableGameObject", All);
        Put("panel.selectable-default", selectable.GetValue(panel) == null);
        var go = new GameObject("selectable"); var link = go.AddComponent<PanelControl>(); var link2 = go.AddComponent<PanelControl>();
        var alternate = new GameObject("alternate"); var alternateLink = alternate.AddComponent<PanelControl>();
        selectable.SetValue(panel, go);
        Put("panel.link-assigned", ReferenceEquals(panel, link.panel) && ReferenceEquals(panel, link2.panel)); Put("panel.link-count", link.assignments); Put("panel.selectable-assigned", ReferenceEquals(go, selectable.GetValue(panel)));
        selectable.SetValue(panel, go); Put("panel.selectable-same", ReferenceEquals(go, selectable.GetValue(panel)));
        Put("panel.link-same-count", link.assignments);
        selectable.SetValue(panel, alternate);
        Put("panel.link-old-cleared", link.panel == null && link2.panel == null); Put("panel.link-old-count", link.assignments);
        Put("panel.link-new-assigned", ReferenceEquals(panel, alternateLink.panel));
        selectable.SetValue(panel, null);
        Put("panel.link-new-cleared", alternateLink.panel == null); Put("panel.link-new-count", alternateLink.assignments); Put("panel.selectable-cleared", selectable.GetValue(panel) == null);
        object panel2 = Call(utility, "FindOrCreateRuntimePanel", null, owner2, factory); Put("panel.second-created", notifications);
        U("BaseRuntimePanel").GetProperty("sortingPriority", All).SetValue(panel, 8f);
        U("BaseRuntimePanel").GetProperty("sortingPriority", All).SetValue(panel2, -4f);
        Call(utility, "GetSortedPlayerPanels", null);
        var max = utility.GetField("s_ResolvedSortingIndexMax", All); Put("panel.sort-max", max.GetValue(null));
        var index = U("BaseRuntimePanel").GetField("resolvedSortingIndex", All);
        Put("panel.first-index", index.GetValue(panel)); Put("panel.second-index", index.GetValue(panel2));
        Call(utility, "DisposeRuntimePanel", null, owner); Call(utility, "GetSortedPlayerPanels", null); Put("panel.max-after-dispose", max.GetValue(null));
        Call(utility, "DisposeRuntimePanel", null, owner2); Call(utility, "GetSortedPlayerPanels", null); Put("panel.max-empty", max.GetValue(null));
        created.RemoveEventHandler(null, handler);
        panel = Call(utility, "FindOrCreateRuntimePanel", null, owner, factory); Put("panel.removed-handler", notifications);
        Call(utility, "DisposeRuntimePanel", null, owner);
        DestroyImmediate(go); DestroyImmediate(alternate); DestroyImmediate(owner); DestroyImmediate(owner2);
    }
}
