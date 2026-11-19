using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace UssValidationProbe
{
    /// <summary>
    /// USS declarations with values their property's syntax does not take (Resources/Invalid.uss):
    /// what the importer reports (<c>import.logs</c>), then on a runtime panel, for an element of each
    /// rule, the resolved value of the rule's property and what the style pass logs
    /// (<c>style.&lt;class&gt;</c>, <c>.logs</c>), one element at a time. Resources/Broken.uss has a value the parser
    /// cannot read beside a valid rule (<c>broken.*</c>, <c>style.broken-*</c>), Resources/Errors.uss
    /// values the importer reports as errors (<c>errors.*</c>, <c>style.errors-*</c>).
    /// </summary>
    public class UssValidationBehaviour : MonoBehaviour
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        readonly StringBuilder m_Out = new StringBuilder();
        readonly HashSet<string> m_Keys = new HashSet<string>();
        readonly List<string> m_Logs = new List<string>();

        static readonly (string Class, Func<IResolvedStyle, string> Read)[] s_Cases =
        {
            ("len-unit", s => F(s.width)), ("len-word", s => F(s.width)),
            ("len-many", s => F(s.width)), ("len-ok", s => F(s.width)), ("override", s => F(s.width)),
            ("color-bad", s => C(s.color)), ("color-fn", s => C(s.color)), ("color-ok", s => C(s.color)),
            ("enum-bad", s => s.flexDirection.ToString()), ("enum-ok", s => s.flexDirection.ToString()),
            ("kw-bad", s => s.whiteSpace.ToString()), ("kw-display", s => s.display.ToString()),
            ("url-bad", s => Image(s.backgroundImage)), ("url-ok", s => Image(s.backgroundImage)),
            ("time-unit", s => Join(s.transitionDuration.Select(t => t.value.ToString("R", Inv) + t.unit))),
            ("trans-prop", s => Join(s.transitionProperty.Select(p => p.ToString()))),
            ("trans-ease", s => Join(s.transitionTimingFunction.Select(e => e.mode.ToString()))),
            ("trans-short", s => Join(s.transitionProperty.Select(p => p.ToString())) + " / " + Join(s.transitionTimingFunction.Select(e => e.mode.ToString()))),
            ("var-ok", s => F(s.width)), ("var-fallback", s => F(s.width)),
            ("unknown", s => C(s.color)), ("int-bad", s => s.unitySliceLeft.ToString(Inv)), ("num-bad", s => F(s.opacity)),
            ("font-bad", s => s.unityFontStyleAndWeight.ToString()),
            ("many-colors", s => C(s.borderTopColor) + " " + C(s.borderLeftColor)),
            ("broken-ok", s => F(s.width)), ("broken-hex", s => C(s.color)),
            ("errors-ok", s => F(s.width)), ("errors-unit", s => F(s.width)),
            ("errors-time", s => Join(s.transitionDuration.Select(t => t.value.ToString("R", Inv) + t.unit))), ("errors-var", s => C(s.color)),
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("USS_VALIDATION_OUT"))) return;
            var go = new GameObject("probe");
            var document = go.AddComponent<UIDocument>();
            document.panelSettings = Resources.Load<PanelSettings>("ProbePanelSettings");
            go.AddComponent<UssValidationBehaviour>();
        }

        static string F(float f) => f.ToString("R", Inv);
        static string C(Color c) => "(" + F(c.r) + ", " + F(c.g) + ", " + F(c.b) + ", " + F(c.a) + ")";
        static string Join(IEnumerable<string> parts) => "[" + string.Join(", ", parts) + "]";
        static string Image(Background b) => b.texture != null ? b.texture.name : b.sprite != null ? b.sprite.name : "none";

        IEnumerator Start()
        {
            var dir = Environment.GetEnvironmentVariable("USS_VALIDATION_OUT");
            var imported = Path.Combine(dir, "import-logs.txt");
            if (File.Exists(imported)) m_Logs.AddRange(File.ReadAllLines(imported).Where(l => l.Length > 0));
            // With what loading the sheets logs (an engine that imports them as they load).
            Application.logMessageReceived += OnImportLog;
            var invalid = Resources.Load<StyleSheet>("Invalid");
            var broken = Resources.Load<StyleSheet>("Broken");
            var errors = Resources.Load<StyleSheet>("Errors");
            Application.logMessageReceived -= OnImportLog;
            Put("import.logs", TakeLogs());
            Application.logMessageReceived += OnLog;
            var document = GetComponent<UIDocument>();
            var target = new RenderTexture(400, 300, 24, RenderTextureFormat.ARGB32);
            target.Create();
            document.panelSettings.targetTexture = target;
            var root = document.rootVisualElement;
            root.styleSheets.Add(invalid);
            Put("broken.loaded", broken != null);
            if (broken != null) root.styleSheets.Add(broken);
            Put("errors.loaded", errors != null);
            if (errors != null) root.styleSheets.Add(errors);
            for (int i = 0; i < 3; i++) yield return null;
            Put("start.logs", TakeLogs());
            // One case at a time: a value the style pass throws on stops that pass.
            foreach (var (cssClass, read) in s_Cases)
            {
                var e = new VisualElement { name = cssClass };
                e.AddToClassList("cell");
                e.AddToClassList(cssClass);
                root.Add(e);
                for (int i = 0; i < 2; i++) yield return null;
                try { Put("style." + cssClass, read(e.resolvedStyle)); }
                catch (Exception ex) { Put("style." + cssClass, ex.GetType().Name); }
                Put("style." + cssClass + ".logs", TakeLogs());
                e.RemoveFromHierarchy();
                yield return null;
                TakeLogs();
            }
            Application.logMessageReceived -= OnLog;
            Put("complete", true);
            File.WriteAllText(Path.Combine(dir, "values.jsonl"), m_Out.ToString());
            EditorApplication.Exit(0);
        }

        /// <summary>As the import runner writes the importer's logs: their lines.</summary>
        void OnImportLog(string message, string stack, LogType type)
        {
            if (type == LogType.Log) return;
            var lines = (type + ": " + message).Replace("\r", "").Split('\n');
            m_Logs.AddRange(lines.Where(l => l.Length > 0));
        }

        void OnLog(string message, string stack, LogType type)
        {
            if (type != LogType.Log) m_Logs.Add(type + ": " + message.Replace("\r", "").Replace("\n", " / "));
        }

        string TakeLogs()
        {
            var text = string.Join(" | ", m_Logs);
            m_Logs.Clear();
            return text;
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
    }
}
