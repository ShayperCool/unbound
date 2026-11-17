using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CursorsProbe
{
    /// <summary>
    /// USS cursors on a runtime panel: elements with every cursor keyword, texture cursors (with and
    /// without a hotspot) and an inline cursor; the pointer moves over them, out of them and onto a
    /// child without a cursor, with and without a pointer capture. After each step: whether the
    /// panel's cursor manager has set a texture cursor (internal state, by reflection) and the logs.
    /// </summary>
    public class CursorsProbeBehaviour : MonoBehaviour
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;
        static readonly string[] s_Keywords =
        {
            "arrow", "text", "resize-vertical", "resize-horizontal", "link", "slide-arrow", "resize-up-right", "resize-up-left",
            "move-arrow", "rotate-arrow", "scale-arrow", "arrow-plus", "arrow-minus", "pan", "orbit", "zoom", "fps", "custom-cursor",
            "split-resize-up-down", "split-resize-left-right", "not-allowed", "not-a-cursor",
        };
        readonly StringBuilder m_Out = new StringBuilder();
        readonly HashSet<string> m_Keys = new HashSet<string>();
        readonly List<string> m_Logs = new List<string>();
        VisualElement m_Root;
        readonly Dictionary<string, VisualElement> m_Cells = new Dictionary<string, VisualElement>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("UITK_CURSORS_OUT"))) return;
            var go = new GameObject("probe");
            var document = go.AddComponent<UIDocument>();
            document.panelSettings = Resources.Load<PanelSettings>("ProbePanelSettings");
            go.AddComponent<CursorsProbeBehaviour>();
        }

        IEnumerator Start()
        {
            var dir = Environment.GetEnvironmentVariable("UITK_CURSORS_OUT");
            var imported = Path.Combine(dir, "import-logs.txt");
            if (File.Exists(imported)) m_Logs.AddRange(File.ReadAllLines(imported).Where(l => l.Length > 0));
            Application.logMessageReceived += OnLog;
            var document = GetComponent<UIDocument>();
            var settings = document.panelSettings;
            var target = new RenderTexture(800, 600, 24, RenderTextureFormat.ARGB32);
            target.Create();
            settings.targetTexture = target;
            settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            settings.scale = 1;
            m_Root = document.rootVisualElement;
            m_Root.styleSheets.Add(Resources.Load<StyleSheet>("Cursors"));
            Put("import.logs", TakeLogs());
            Build();
            for (int i = 0; i < 3; i++) yield return null;
            bool failed = false;
            try
            {
                Record();
            }
            catch (Exception e)
            {
                Put("error", e.GetType().Name + ": " + FirstLine(e.Message));
                failed = true;
            }
            Application.logMessageReceived -= OnLog;
            if (!failed) Put("complete", true);
            File.WriteAllText(Path.Combine(dir, "values.jsonl"), m_Out.ToString());
            EditorApplication.Exit(0);
        }

        void OnLog(string message, string stack, LogType type)
        {
            if (type != LogType.Log) m_Logs.Add(type + ": " + FirstLine(message));
        }

        string TakeLogs()
        {
            var text = string.Join(" | ", m_Logs);
            m_Logs.Clear();
            return text;
        }

        static string FirstLine(string text)
        {
            if (text == null) return "null";
            int newline = text.IndexOf('\n');
            return (newline < 0 ? text : text.Substring(0, newline)).TrimEnd('\r');
        }

        void Put(string key, object value)
        {
            if (!m_Keys.Add(key)) throw new InvalidOperationException("duplicate key " + key);
            m_Out.Append("{\"k\":").Append(Json(key)).Append(",\"v\":").Append(value is bool b ? (b ? "true" : "false") : Json(Convert.ToString(value, Inv))).Append("}\n");
        }

        static string Json(string text)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in text)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", Inv));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        VisualElement Cell(string name, string cssClass, float x, float y, VisualElement parent = null)
        {
            var e = new VisualElement { name = name };
            e.AddToClassList("cell");
            if (cssClass != null) e.AddToClassList(cssClass);
            e.style.left = x;
            e.style.top = y;
            (parent ?? m_Root).Add(e);
            m_Cells[name] = e;
            return e;
        }

        void Build()
        {
            for (int i = 0; i < s_Keywords.Length; i++) Cell(s_Keywords[i], "c-" + s_Keywords[i], 10 + 30 * (i % 10), 10 + 30 * (i / 10));
            Cell("texture", "c-texture", 10, 200);
            Cell("texturePlain", "c-texture-plain", 40, 200);
            Cell("plain", null, 70, 200);
            Cell("inline", null, 100, 200).style.cursor = new StyleCursor(new UnityEngine.UIElements.Cursor { texture = Resources.Load<Texture2D>("cursor"), hotspot = new Vector2(2, 3) });
            var parent = Cell("parent", "c-texture", 10, 300);
            parent.style.width = 60;
            parent.style.height = 60;
            var child = new VisualElement { name = "child" };
            child.style.position = Position.Absolute;
            child.style.left = 30;
            child.style.top = 30;
            child.style.width = 20;
            child.style.height = 20;
            parent.Add(child);
            m_Cells["child"] = child;
        }

        /// <summary>Whether the panel's cursor manager has a texture cursor set (internal state).</summary>
        bool Overridden()
        {
            var manager = m_Root.panel.GetType().GetProperty("cursorManager", Any).GetValue(m_Root.panel);
            return (bool)manager.GetType().GetProperty("isCursorOverriden", Any).GetValue(manager);
        }

        static Event At(EventType type, Vector2 position) =>
            new Event { type = type, button = 0, mousePosition = position, delta = Vector2.zero };

        /// <summary>The pointer moves to <paramref name="name"/>'s centre (or a free spot).</summary>
        void MoveTo(string name)
        {
            // The parent is hovered at a corner its child does not cover.
            var position = name == null ? new Vector2(700, 500)
                : name == "parent" ? m_Cells[name].worldBound.position + new Vector2(5, 5)
                : m_Cells[name].worldBound.center;
            var target = name == null ? m_Root : m_Cells[name];
            using (var e = PointerMoveEvent.GetPooled(At(EventType.MouseMove, position)))
            {
                e.target = target;
                target.SendEvent(e);
            }
        }

        void Step(string key, Action action)
        {
            action();
            Put(key, "overridden " + Overridden() + " logs [" + TakeLogs() + "]");
        }

        void Record()
        {
            Put("start", "overridden " + Overridden());
            foreach (var keyword in s_Keywords) Step("hover." + keyword, () => MoveTo(keyword));
            Step("hover.texture", () => MoveTo("texture"));
            Step("hover.plain", () => MoveTo("plain"));
            Step("hover.texturePlain", () => MoveTo("texturePlain"));
            Step("hover.inline", () => MoveTo("inline"));
            Step("hover.empty", () => MoveTo(null));
            Step("hover.parent", () => MoveTo("parent"));
            Step("hover.child", () => MoveTo("child"));
            Step("hover.parentAgain", () => MoveTo("parent"));
            Step("hover.resizeFromTexture", () => MoveTo("resize-horizontal"));
            // A captured pointer: other elements do not change the cursor; releasing the capture
            // takes the cursor of the element under the pointer.
            Step("capture.texture.over", () => MoveTo("texture"));
            Step("capture.texture.capture", () => m_Cells["texture"].CaptureMouse());
            Step("capture.texture.toPlain", () => MoveTo("plain"));
            Step("capture.texture.release", () => m_Cells["texture"].ReleaseMouse());
            Step("capture.plain.capture", () => m_Cells["plain"].CaptureMouse());
            Step("capture.plain.toTexture", () => MoveTo("texture"));
            Step("capture.plain.release", () => m_Cells["plain"].ReleaseMouse());
            Step("capture.empty.capture", () => m_Cells["texture"].CaptureMouse());
            Step("capture.empty.toEmpty", () => MoveTo(null));
            Step("capture.empty.release", () => m_Cells["texture"].ReleaseMouse());
            // A cursor style changed under a resting pointer.
            Step("restyle.over", () => MoveTo("plain"));
            Step("restyle.set", () => m_Cells["plain"].AddToClassList("c-texture"));
            Step("restyle.away", () => MoveTo(null));
            Step("restyle.back", () => MoveTo("plain"));
        }
    }
}
