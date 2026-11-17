#pragma warning disable CS0618 // the deprecated UxmlFactory/UxmlTraits path is measured too
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TemplatesProbe
{
    /// <summary>A game element of the UXML serialization path.</summary>
    [UxmlElement]
    public partial class NewElement : VisualElement
    {
        [UxmlAttribute] public int myValue { get; set; }
        [UxmlAttribute] public string note { get; set; }
        [UxmlAttribute] public VisualTreeAsset tree { get; set; }
    }

    /// <summary>A game element of the deprecated factory path: one attribute read through its
    /// description (with a former name), one straight from the bag.</summary>
    public class OldElement : VisualElement
    {
        public new class UxmlFactory : UxmlFactory<OldElement, UxmlTraits> { }

        public new class UxmlTraits : VisualElement.UxmlTraits
        {
            readonly UxmlIntAttributeDescription m_Old = new UxmlIntAttributeDescription { name = "old-value", obsoleteNames = new[] { "former-value" } };
            readonly UxmlAssetAttributeDescription<VisualTreeAsset> m_Tree = new UxmlAssetAttributeDescription<VisualTreeAsset> { name = "tree" };

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                var e = (OldElement)ve;
                e.oldValue = m_Old.GetValueFromBag(bag, cc);
                e.tree = m_Tree.GetValueFromBag(bag, cc);
                bag.TryGetAttributeValue("raw-value", out e.rawValue);
            }
        }

        public int oldValue;
        public string rawValue;
        public VisualTreeAsset tree;
    }

    /// <summary>A game template container of the deprecated factory path: its template is cloned by
    /// TemplateContainer's traits.</summary>
    public class OldTemplate : TemplateContainer
    {
        public new class UxmlFactory : UxmlFactory<OldTemplate, TemplateContainer.UxmlTraits> { }
    }

    /// <summary>
    /// Clones UXML templates with attribute overrides and slots: Outer instances Middle, which
    /// instances Leaf; each level overrides attributes of Leaf's elements (built-in, a [UxmlElement]
    /// element and a deprecated-factory element) and fills Leaf's slots. Records every element's
    /// hierarchy and state and the logs of importing and cloning.
    /// </summary>
    public class TemplatesProbeBehaviour : MonoBehaviour
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        readonly StringBuilder m_Out = new StringBuilder();
        readonly HashSet<string> m_Keys = new HashSet<string>();
        readonly List<string> m_Logs = new List<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("UXML_TEMPLATES_OUT"))) return;
            new GameObject("probe").AddComponent<TemplatesProbeBehaviour>();
        }

        void Start()
        {
            var dir = Environment.GetEnvironmentVariable("UXML_TEMPLATES_OUT");
            var imported = Path.Combine(dir, "import-logs.txt");
            if (File.Exists(imported)) m_Logs.AddRange(File.ReadAllLines(imported).Where(l => l.Length > 0).Select(FirstLine));
            Application.logMessageReceived += OnLog;
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

        /// <summary>The logs since the last call, as one value. An element's id in the importer's
        /// messages (`(Type)(id)`) is an internal number, recorded as `#`.</summary>
        string TakeLogs()
        {
            var text = System.Text.RegularExpressions.Regex.Replace(string.Join(" | ", m_Logs), @"\)\(-?[0-9]+\)", ")(#)");
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

        static string S(string text) => text == null ? "null" : "'" + text + "'";

        /// <summary>An element's own state: type, classes and the values the probe's overrides touch.</summary>
        static string State(VisualElement e)
        {
            var sb = new StringBuilder(e.GetType().Name);
            sb.Append(" [").Append(string.Join(" ", e.GetClasses())).Append(']');
            if (!string.IsNullOrEmpty(e.tooltip)) sb.Append(" tooltip ").Append(S(e.tooltip));
            if (e.focusable || e.tabIndex != 0) sb.Append(" focusable ").Append(e.focusable).Append(" tab ").Append(e.tabIndex);
            if (e.style.width.keyword == StyleKeyword.Undefined) sb.Append(" width ").Append(e.style.width.value.value.ToString(Inv));
            switch (e)
            {
                case Toggle t: sb.Append(" label ").Append(S(t.label)).Append(" value ").Append(t.value); break;
                case TextElement t: sb.Append(" text ").Append(S(t.text)); break;
                case NewElement n:
                    sb.Append(" myValue ").Append(n.myValue).Append(" note ").Append(S(n.note));
                    if (n.tree != null) sb.Append(" tree ").Append(S(n.tree.name));
                    break;
                case OldElement o:
                    sb.Append(" oldValue ").Append(o.oldValue).Append(" raw ").Append(S(o.rawValue));
                    if (o.tree != null) sb.Append(" tree ").Append(S(o.tree.name));
                    break;
                case ListView l: sb.Append(" itemTemplate ").Append(l.itemTemplate == null ? "null" : S(l.itemTemplate.name)); break;
                case TemplateContainer c: sb.Append(" template ").Append(S(c.templateId)).Append(" content ").Append(c.contentContainer == c ? "self" : S(c.contentContainer.name)); break;
            }
            return sb.ToString();
        }

        /// <summary>Every element under <paramref name="root"/> in hierarchy order, keyed by its path of
        /// names (or child indices for unnamed elements).</summary>
        void Records(string prefix, VisualElement root)
        {
            void Walk(VisualElement e, string path)
            {
                for (int i = 0; i < e.hierarchy.childCount; i++)
                {
                    var child = e.hierarchy[i];
                    var p = path + "/" + i.ToString(Inv) + ":" + (string.IsNullOrEmpty(child.name) ? "-" : child.name);
                    Put(prefix + p, State(child));
                    Walk(child, p);
                }
            }
            Walk(root, "");
        }

        static readonly string[] s_Documents =
        {
            "Leaf", "Middle", "Outer", "Panel", "Host", "Slots", "DuplicateSlots", "OldTemplateDoc", "OldTemplateHost", "OldTemplateBare", "OldTemplateOther",
            "Row", "Sub/Card", "AssetHost", "NestedOld", "FactoryOnly", "OldFactoryDoc", "OldFactoryHost", "SlotClash", "EarlySlot", "EarlyHost", "Odd",
        };

        void Clone(string doc)
        {
            var asset = Resources.Load<VisualTreeAsset>(doc);
            var root = new VisualElement { name = "root" };
            try
            {
                asset.CloneTree(root);
            }
            catch (Exception e)
            {
                Put(doc + ".exception", e.GetType().Name + ": " + FirstLine(e.Message));
            }
            Put(doc + ".logs", TakeLogs());
            Records(doc, root);
        }

        void Record()
        {
            // Every document is loaded (and so imported, where import happens on load) before the
            // import logs are recorded.
            foreach (var doc in s_Documents) Resources.Load<VisualTreeAsset>(doc);
            Put("import.logs", TakeLogs());
            foreach (var doc in s_Documents)
            {
                var asset = Resources.Load<VisualTreeAsset>(doc);
                Put("import.flags." + doc, "warnings " + asset.importedWithWarnings + " errors " + asset.importedWithErrors);
            }
            foreach (var doc in new[]
            {
                "Leaf", "Middle", "Outer", "Host", "DuplicateSlots", "OldTemplateDoc", "OldTemplateHost", "OldTemplateBare", "OldTemplateOther",
                "AssetHost", "NestedOld", "OldFactoryHost", "SlotClash", "EarlyHost", "Odd",
            })
                Clone(doc);
            // An instance returned by Instantiate holds the document's elements itself.
            var instance = Resources.Load<VisualTreeAsset>("Middle").Instantiate();
            Put("instantiate.logs", TakeLogs());
            Put("instantiate.root", State(instance) + " children " + instance.hierarchy.childCount);
        }
    }
}
