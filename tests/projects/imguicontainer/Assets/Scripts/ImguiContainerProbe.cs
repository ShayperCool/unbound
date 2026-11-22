using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.UIElements;
#endif

// IMGUIContainers in a runtime panel: the size measured from their IMGUI layout, the events
// their handlers get, a mouse event sent to one, and a PropertyField drawing a drawer's IMGUI.
public class ImguiContainerProbe : MonoBehaviour
{
    readonly StringBuilder m_Out = new StringBuilder();
    readonly List<string> m_Logs = new List<string>();
    VisualElement m_Root;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var go = new GameObject("ImguiContainerProbe");
        var doc = go.AddComponent<UIDocument>();
        var settings = ScriptableObject.CreateInstance<PanelSettings>();
        settings.scaleMode = PanelScaleMode.ConstantPixelSize;
        settings.scale = 1f;
        doc.panelSettings = settings;
        go.AddComponent<ImguiContainerProbe>();
    }

    IEnumerator Start()
    {
        yield return null;
        var dir = Environment.GetEnvironmentVariable("IMGUI_CONTAINER_OUT");
        Application.logMessageReceived += (msg, _, type) =>
        {
            if (type == LogType.Warning || type == LogType.Error || type == LogType.Exception)
                m_Logs.Add($"{type}:{msg}");
        };
        var document = GetComponent<UIDocument>();
        m_Target = new RenderTexture(800, 600, 24, RenderTextureFormat.ARGB32) { name = "probeTarget" };
        m_Target.Create();
        document.panelSettings.targetTexture = m_Target;
        document.panelSettings.clearColor = true;
        document.panelSettings.colorClearValue = Color.black;
        m_Root = document.rootVisualElement;
        var run = Run();
        while (true)
        {
            object current;
            try
            {
                if (!run.MoveNext()) break;
                current = run.Current;
            }
            catch (Exception e)
            {
                Put("error", e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace);
                break;
            }
            yield return current;
        }
        m_Logs.Sort(StringComparer.Ordinal);
        m_Out.Append("{\"k\":\"logs\",\"v\":[").Append(string.Join(",", m_Logs.Distinct().Select(Json))).Append("]}\n");
        m_Out.Append("{\"k\":\"complete\",\"v\":true}\n");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "values.jsonl"), m_Out.ToString());
#if UNITY_EDITOR
        EditorApplication.Exit(0);
#else
        Application.Quit(0);
#endif
    }

    void Put(string key, string value) => m_Out.Append("{\"k\":").Append(Json(key)).Append(",\"v\":").Append(Json(value)).Append("}\n");

    static string Json(string s)
    {
        if (s == null) return "null";
        var sb = new StringBuilder(s.Length + 2).Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.Append('"').ToString();
    }

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    static string R(Rect r) => $"{F(r.x)},{F(r.y)},{F(r.width)},{F(r.height)}";
    static string P(Vector2 p) => $"{F(p.x)},{F(p.y)}";

    static IEnumerator Frames(int count = 3)
    {
        for (int i = 0; i < count; i++) yield return null;
    }

    // A handler's events, cleared when read.
    sealed class Log
    {
        public readonly List<string> events = new List<string>();
        public string Take()
        {
            var s = string.Join(",", events);
            events.Clear();
            return s;
        }
    }

    int m_Lines = 2;
    RenderTexture m_Target;
    Texture2D m_Read;

    string Pixel(int x, int y)
    {
        Color32 c = m_Read.GetPixel(x, m_Target.height - 1 - y);
        return c.r + " " + c.g + " " + c.b + " " + c.a;
    }

    void ReadTarget()
    {
        var old = RenderTexture.active;
        RenderTexture.active = m_Target;
        if (m_Read == null) m_Read = new Texture2D(m_Target.width, m_Target.height, TextureFormat.RGBA32, false);
        m_Read.ReadPixels(new Rect(0, 0, m_Target.width, m_Target.height), 0, 0);
        m_Read.Apply();
        RenderTexture.active = old;
    }

    static void Fill(Rect r, Color c)
    {
        var old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = old;
    }

    IEnumerator Run()
    {
        var spacer = new VisualElement { name = "spacer" };
        spacer.style.height = 30f;
        m_Root.Add(spacer);

        // 1. Sized by its IMGUI layout.
        var autoLog = new Log();
        string lastRect = "", origin = "";
        var auto = new IMGUIContainer(() =>
        {
            var e = Event.current;
            autoLog.events.Add(e.type.ToString());
            for (int i = 0; i < m_Lines; i++) GUILayout.Label("Line " + i);
            GUILayout.Button("Press");
            if (e.type == EventType.Repaint)
            {
                lastRect = R(GUILayoutUtility.GetLastRect());
                origin = P(GUIUtility.GUIToScreenPoint(Vector2.zero));
            }
        }) { name = "auto" };
        auto.style.marginLeft = 10f;
        Put("auto.beforeAttach", autoLog.Take());
        m_Root.Add(auto);
        Put("auto.attached", autoLog.Take() + "|layout=" + R(auto.layout));
        yield return null;
        Put("auto.frame1", autoLog.Take() + "|layout=" + R(auto.layout));
        yield return null;
        Put("auto.frame2", autoLog.Take() + "|layout=" + R(auto.layout) + "|lastRect=" + lastRect + "|origin=" + origin);
        yield return Frames();
        Put("auto.settled", autoLog.Take() + "|layout=" + R(auto.layout));

        // More lines: the layout follows once it is marked dirty.
        m_Lines = 4;
        yield return Frames();
        Put("auto.moreLines", autoLog.Take() + "|layout=" + R(auto.layout));
        auto.MarkDirtyLayout();
        yield return Frames();
        Put("auto.markedDirty", autoLog.Take() + "|layout=" + R(auto.layout));

        // 2. A fixed size.
        var fixedLog = new Log();
        var fixedSize = new IMGUIContainer(() =>
        {
            fixedLog.events.Add(Event.current.type.ToString());
            GUILayout.Label("Fixed");
        }) { name = "fixed" };
        fixedSize.style.height = 50f;
        fixedSize.style.width = 200f;
        m_Root.Add(fixedSize);
        yield return Frames();
        Put("fixed", fixedLog.Take() + "|layout=" + R(fixedSize.layout));

        // 3. Without a handler.
        var empty = new IMGUIContainer { name = "empty" };
        m_Root.Add(empty);
        yield return Frames();
        Put("empty", R(empty.layout));

        // A container added once the panel has painted: its first measures.
        var lateLog = new Log();
        var late = new IMGUIContainer(() =>
        {
            lateLog.events.Add(Event.current.type.ToString());
            GUILayout.Label("Late");
        }) { name = "late" };
        m_Root.Add(late);
        yield return null;
        Put("late.frame1", lateLog.Take());
        yield return null;
        Put("late.frame2", lateLog.Take());
        yield return Frames();
        Put("late.settled", lateLog.Take());
        // Marked dirty with the same content, and removed and added again.
        late.MarkDirtyLayout();
        yield return Frames();
        Put("late.sameDirty", lateLog.Take());
        late.RemoveFromHierarchy();
        yield return Frames();
        m_Root.Add(late);
        yield return null;
        Put("late.readded1", lateLog.Take());
        yield return Frames();
        Put("late.readded", lateLog.Take());
        late.RemoveFromHierarchy();
        yield return null;

        // 4. A mouse event on the button.
        autoLog.Take();
        string mouse = "";
        var mouseLog = new Log();
        var probe = new IMGUIContainer(() =>
        {
            var e = Event.current;
            if (e.type != EventType.Layout && e.type != EventType.Repaint) mouse += e.type + "@" + P(e.mousePosition) + ";";
            mouseLog.events.Add(e.type.ToString());
            GUILayout.Button("Target");
        }) { name = "mouse" };
        m_Root.Add(probe);
        yield return Frames();
        var center = probe.worldBound.center;
        var down = new Event { type = EventType.MouseDown, mousePosition = center, button = 0, clickCount = 1 };
        using (var evt = MouseDownEvent.GetPooled(down))
        {
            evt.target = probe;
            mouseLog.Take();
            probe.SendEvent(evt);
            Put("mouse.down.events", mouseLog.Take());
            Put("mouse.down", $"{mouse}|stopped={evt.isPropagationStopped}|hot={(GUIUtility.hotControl != 0)}|center={P(center)}");
        }
        yield return Frames();
        Put("mouse.later", mouse + "|hot=" + (GUIUtility.hotControl != 0));
        var up = new Event { type = EventType.MouseUp, mousePosition = center, button = 0, clickCount = 1 };
        using (var evt = MouseUpEvent.GetPooled(up))
        {
            evt.target = probe;
            probe.SendEvent(evt);
        }
        yield return Frames();
        Put("mouse.up", mouse + "|hot=" + (GUIUtility.hotControl != 0));
        // A key event, and an event without an IMGUI event.
        mouseLog.Take();
        using (var evt = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.A }))
        {
            evt.target = probe;
            probe.SendEvent(evt);
            Put("key.down", mouseLog.Take() + "|stopped=" + evt.isPropagationStopped);
        }
        using (var evt = MouseDownEvent.GetPooled(center, 0, 1, Vector2.zero))
        {
            evt.target = probe;
            probe.SendEvent(evt);
            Put("mouse.noImgui", mouseLog.Take() + "|stopped=" + evt.isPropagationStopped);
        }
        Put("focus", $"focusable={probe.focusable}|canGrabFocus={probe.canGrabFocus}|focused={(probe.focusController?.focusedElement == probe)}");

        // 5. Drawing: in its box, past its box, and under an overflow-hidden parent.
        var paintLog = new Log();
        string paintOrigin = "";
        var paint = new IMGUIContainer(() =>
        {
            paintLog.events.Add(Event.current.type.ToString());
            if (Event.current.type != EventType.Repaint) return;
            paintOrigin = P(GUIUtility.GUIToScreenPoint(Vector2.zero));
            Fill(new Rect(0, 0, 50, 20), Color.red);
            Fill(new Rect(80, 30, 60, 30), Color.green);
        }) { name = "paint" };
        paint.style.position = Position.Absolute;
        paint.style.left = 100f;
        paint.style.top = 400f;
        paint.style.width = 100f;
        paint.style.height = 40f;
        m_Root.Add(paint);
        var clipParent = new VisualElement { name = "clip" };
        clipParent.style.position = Position.Absolute;
        clipParent.style.left = 400f;
        clipParent.style.top = 400f;
        clipParent.style.width = 60f;
        clipParent.style.height = 30f;
        clipParent.style.overflow = Overflow.Hidden;
        var clipped = new IMGUIContainer(() =>
        {
            if (Event.current.type == EventType.Repaint) Fill(new Rect(0, 0, 100, 50), Color.blue);
        }) { name = "clipped" };
        clipped.style.width = 60f;
        clipped.style.height = 30f;
        clipParent.Add(clipped);
        m_Root.Add(clipParent);
        yield return Frames();
        paintLog.Take();
        yield return null;
        Put("paint.frame", paintLog.Take() + "|origin=" + paintOrigin);
        yield return null;
        ReadTarget();
        Put("paint.pixels", string.Join("|", new[] { Pixel(110, 405), Pixel(160, 405), Pixel(190, 435), Pixel(220, 450), Pixel(450, 420), Pixel(480, 420), Pixel(450, 445) }));

#if UNITY_EDITOR
        // 6. A PropertyField drawing a drawer's IMGUI.
        var target = ScriptableObject.CreateInstance<ImguiContainerTarget>();
        var so = new SerializedObject(target);
        var calls = (List<string>)AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("ImguiDrawnDrawer")).First(t => t != null)
            .GetField("calls", BindingFlags.Static | BindingFlags.Public).GetValue(null);
        calls.Clear();
        var field = new PropertyField(so.FindProperty("drawn"));
        m_Root.Add(field);
        field.Bind(so);
        yield return Frames(5);
        var container = field.Q<IMGUIContainer>();
        Put("property", $"container={(container != null)}|field={R(field.layout)}|imgui={(container == null ? "" : R(container.layout))}");
        Put("property.calls", string.Join(";", calls.Distinct()));

        // 7. A drawer that measures and draws its own property again gets the next drawer of it.
        calls.Clear();
        var nested = new PropertyField(so.FindProperty("nested"));
        m_Root.Add(nested);
        nested.Bind(so);
        yield return Frames(5);
        var nestedContainer = nested.Q<IMGUIContainer>();
        Put("nested", $"container={(nestedContainer != null)}|field={F(nested.layout.height)}");
        Put("nested.calls", string.Join(";", calls.Distinct()));
        calls.Clear();
        var ranged = new PropertyField(so.FindProperty("ranged"));
        m_Root.Add(ranged);
        ranged.Bind(so);
        yield return Frames(5);
        Put("ranged", $"container={(ranged.Q<IMGUIContainer>() != null)}|field={F(ranged.layout.height)}");
        Put("ranged.calls", string.Join(";", calls.Distinct()));

        // 8. Decorators: a PropertyField shows them itself, above its drawer's IMGUI; an IMGUI
        // PropertyField gives them the top of the property's rect.
        calls.Clear();
        var spaced = new PropertyField(so.FindProperty("spaced"));
        m_Root.Add(spaced);
        spaced.Bind(so);
        yield return Frames(5);
        var spacedContainer = spaced.Q<IMGUIContainer>();
        Put("spaced", $"field={F(spaced.layout.height)}|children={spaced.hierarchy.childCount}|decorators={F(spaced.hierarchy[0].layout.height)}|imgui={(spacedContainer == null ? "" : F(spacedContainer.layout.height))}");
        Put("spaced.calls", string.Join(";", calls.Distinct()));
        spaced.RemoveFromHierarchy();
        // A property no PropertyField has drawn: the drawer's rect is below the decorator.
        calls.Clear();
        var drawnOnly = new IMGUIContainer(() =>
        {
            so.Update();
            var rect = EditorGUILayout.GetControlRect(true, EditorGUI.GetPropertyHeight(so.FindProperty("spacedImgui")));
            rect.x = 3f;
            rect.y = 7f;
            EditorGUI.PropertyField(rect, so.FindProperty("spacedImgui"));
        }) { name = "drawnOnly" };
        m_Root.Add(drawnOnly);
        yield return Frames(5);
        Put("imgui.spacedOnly", F(drawnOnly.layout.height));
        Put("imgui.spacedOnly.calls", string.Join(";", calls.Distinct()));
        // The property the PropertyField above drew: its decorator stays left out.
        calls.Clear();
        var drawnSpaced = new IMGUIContainer(() =>
        {
            so.Update();
            EditorGUILayout.PropertyField(so.FindProperty("spaced"));
        }) { name = "drawnSpaced" };
        m_Root.Add(drawnSpaced);
        yield return Frames(5);
        Put("imgui.spaced", F(drawnSpaced.layout.height));
        Put("imgui.spaced.calls", string.Join(";", calls.Distinct()));
        var drawnPlain = new IMGUIContainer(() =>
        {
            so.Update();
            EditorGUILayout.PropertyField(so.FindProperty("plainSpaced"));
            EditorGUILayout.PropertyField(so.FindProperty("plainSpaced"), GUIContent.none);
        }) { name = "drawnPlain" };
        m_Root.Add(drawnPlain);
        yield return Frames(5);
        Put("imgui.plainSpaced", F(drawnPlain.layout.height));
#endif
    }
}
