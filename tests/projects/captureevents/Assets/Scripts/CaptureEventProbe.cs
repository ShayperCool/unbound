using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

// Pointer capture events in a runtime panel: the pointer id each carries through its public
// property and through the internal IPointerCaptureEventInternal interface, for the mouse and a
// touch, for events the panel sends when an element captures or releases a pointer and for pooled
// events a script makes; the related target and reuse of pooled events.
public class CaptureEventProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod] static void Install() { new GameObject("CaptureEventProbe").AddComponent<CaptureEventProbe>(); }
    [Serializable] class Row { public string key; public string value; }
    StreamWriter output;
    void Put(string key, object value) => output.WriteLine(JsonUtility.ToJson(new Row { key = key, value = value == null ? "<null>" : Convert.ToString(value, CultureInfo.InvariantCulture) }));

    static readonly Type s_Internal = typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.IPointerCaptureEventInternal");
    string log = "";

    IEnumerator Start()
    {
        string path = Environment.GetEnvironmentVariable("CAPTURE_EVENTS_OUT"); if (string.IsNullOrEmpty(path)) yield break;
        output = new StreamWriter(path);
        bool failed = false;
        var settings = ScriptableObject.CreateInstance<PanelSettings>();
        if (Screen.dpi > 0) settings.scale = 96f / Screen.dpi;
        var document = new GameObject("Document").AddComponent<UIDocument>();
        document.panelSettings = settings;
        var root = document.rootVisualElement;
        var a = new VisualElement { name = "A" };
        var b = new VisualElement { name = "B" };
        foreach (var e in new[] { a, b })
        {
            e.style.width = 100;
            e.style.height = 50;
            root.Add(e);
            e.RegisterCallback<PointerCaptureEvent>(Record);
            e.RegisterCallback<PointerCaptureOutEvent>(Record);
        }
        yield return null;
        var steps = Steps(a, b);
        while (true)
        {
            bool more;
            try { more = steps.MoveNext(); }
            catch (Exception ex) { Debug.LogException(ex); failed = true; break; }
            if (!more) break;
            yield return steps.Current;
        }
        if (!failed) Put("complete", 1);
        output.Dispose();
        EditorApplication.Exit(failed ? 1 : 0);
    }

    static string Name(IEventHandler handler) => handler == null ? "<null>" : handler is VisualElement v ? v.name : handler.GetType().Name;

    static string Describe(EventBase evt)
    {
        var capture = (IPointerCaptureEvent)evt;
        int publicId = evt is PointerCaptureEvent c ? c.pointerId : ((PointerCaptureOutEvent)evt).pointerId;
        var related = evt is PointerCaptureEvent c2 ? c2.relatedTarget : ((PointerCaptureOutEvent)evt).relatedTarget;
        object internalId = s_Internal != null && s_Internal.IsInstanceOfType(capture) ? s_Internal.GetProperty("pointerId").GetValue(capture) : "<none>";
        return evt.GetType().Name + " target " + Name(evt.target) + " id " + publicId + " internal " + internalId + " related " + Name(related);
    }

    void Record(EventBase evt) => log += (log.Length > 0 ? "; " : "") + Describe(evt) + " at " + Name(evt.currentTarget);

    string Take()
    {
        var result = log.Length == 0 ? "<none>" : log;
        log = "";
        return result;
    }

    static IEnumerator Frames() { for (int i = 0; i < 3; i++) yield return null; }

    // The panel reports capture changes while it processes a pointer's events.
    static void Move(VisualElement root, int pointerId)
    {
        var at = root.worldBound.center;
        EventBase e;
        if (pointerId == PointerId.mousePointerId)
            e = PointerMoveEvent.GetPooled(new Event { type = EventType.MouseMove, mousePosition = at });
        else
            e = PointerMoveEvent.GetPooled(new Touch { fingerId = pointerId - PointerId.touchPointerIdBase, phase = TouchPhase.Moved,
                position = new Vector2(at.x, Screen.height - at.y), rawPosition = new Vector2(at.x, Screen.height - at.y) });
        using (e) root.panel.visualTree.SendEvent(e);
    }

    IEnumerator Steps(VisualElement a, VisualElement b)
    {
        Put("interfaces", s_Internal == null ? "<missing>" : string.Join(",", typeof(PointerCaptureEvent).GetInterfaces()
            .Where(i => i.Name.StartsWith("IPointerCapture", StringComparison.Ordinal)).Select(i => i.Name).OrderBy(n => n, StringComparer.Ordinal)));
        Put("interfaces.out", string.Join(",", typeof(PointerCaptureOutEvent).GetInterfaces()
            .Where(i => i.Name.StartsWith("IPointerCapture", StringComparison.Ordinal)).Select(i => i.Name).OrderBy(n => n, StringComparer.Ordinal)));

        // Events a script makes and sends.
        int touch = PointerId.touchPointerIdBase + 3;
        using (var e = PointerCaptureEvent.GetPooled(a, b, touch)) a.SendEvent(e);
        Put("script.capture", Take());
        using (var e = PointerCaptureOutEvent.GetPooled(b, a, PointerId.mousePointerId)) b.SendEvent(e);
        Put("script.out", Take());
        var first = PointerCaptureEvent.GetPooled(a, b, touch);
        first.Dispose();
        var reused = PointerCaptureEvent.GetPooled();
        Put("reuse", ReferenceEquals(first, reused) + " " + Describe(reused));
        reused.Dispose();

        // Captures and releases the panel reports.
        a.CapturePointer(touch);
        Move(a.parent, touch);
        yield return Frames();
        Put("panel.captureTouch", Take() + " | has " + a.HasPointerCapture(touch));
        b.CapturePointer(touch);
        Move(a.parent, touch);
        yield return Frames();
        Put("panel.moveTouch", Take() + " | has " + a.HasPointerCapture(touch) + " " + b.HasPointerCapture(touch));
        b.ReleasePointer(touch);
        Move(a.parent, touch);
        yield return Frames();
        Put("panel.releaseTouch", Take() + " | has " + b.HasPointerCapture(touch));
        a.CapturePointer(PointerId.mousePointerId);
        Move(a.parent, PointerId.mousePointerId);
        yield return Frames();
        Put("panel.captureMouse", Take());
        a.ReleasePointer(PointerId.mousePointerId);
        Move(a.parent, PointerId.mousePointerId);
        yield return Frames();
        Put("panel.releaseMouse", Take());
    }
}
