using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.Experimental;

namespace LinksProbe
{
    /// <summary>
    /// Pointer events over the rich-text links of labels on a runtime panel (800x600 target): a label
    /// "&lt;link=one&gt;AAAA&lt;/link&gt;BBBBBBBB&lt;link=two&gt;CCCC&lt;/link&gt;" is entered at its
    /// left edge (link one), middle (no link) and right edge (link two), pressed and released over
    /// links and left; an &lt;a href&gt; label and a label whose links are removed by a text change.
    /// After each step: the link events the label and its parent received (type, id, text, target,
    /// phase), in order.
    /// </summary>
    public class LinksProbeBehaviour : MonoBehaviour
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        readonly StringBuilder m_Out = new StringBuilder();
        readonly HashSet<string> m_Keys = new HashSet<string>();
        readonly List<string> m_Events = new List<string>();
        VisualElement m_Root;
        readonly Dictionary<string, Label> m_Labels = new Dictionary<string, Label>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("UITK_LINKS_OUT"))) return;
            var go = new GameObject("probe");
            var document = go.AddComponent<UIDocument>();
            document.panelSettings = Resources.Load<PanelSettings>("ProbePanelSettings");
            go.AddComponent<LinksProbeBehaviour>();
        }

        IEnumerator Start()
        {
            var dir = Environment.GetEnvironmentVariable("UITK_LINKS_OUT");
            var document = GetComponent<UIDocument>();
            var settings = document.panelSettings;
            var target = new RenderTexture(800, 600, 24, RenderTextureFormat.ARGB32);
            target.Create();
            settings.targetTexture = target;
            settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            settings.scale = 1;
            m_Root = document.rootVisualElement;
            Build();
            // Layout and text generation (the links are set up when the text is generated).
            for (int i = 0; i < 4; i++) yield return null;
            bool failed = false;
            var steps = Record();
            while (true)
            {
                object current;
                try
                {
                    if (!steps.MoveNext()) break;
                    current = steps.Current;
                }
                catch (Exception e)
                {
                    Put("error", e.GetType().Name + ": " + e.Message);
                    failed = true;
                    break;
                }
                yield return current;
            }
            if (!failed) Put("complete", true);
            File.WriteAllText(Path.Combine(dir, "values.jsonl"), m_Out.ToString());
            EditorApplication.Exit(0);
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

        Label AddLabel(string name, string text, float top)
        {
            var parent = new VisualElement { name = name + "Parent" };
            parent.style.position = Position.Absolute;
            parent.style.left = 20;
            parent.style.top = top;
            var label = new Label(text) { name = name };
            label.style.fontSize = 24;
            label.style.paddingLeft = 0;
            label.style.paddingRight = 0;
            label.style.marginLeft = 0;
            label.style.marginRight = 0;
            parent.Add(label);
            m_Root.Add(parent);
            m_Labels[name] = label;
            Watch(label, "label");
            Watch(parent, "parent");
            return label;
        }

        void Watch(VisualElement element, string who)
        {
            element.RegisterCallback<PointerDownLinkTagEvent>(e => Record("down", e.linkID, e.linkText, e, who));
            element.RegisterCallback<PointerUpLinkTagEvent>(e => Record("up", e.linkID, e.linkText, e, who));
            element.RegisterCallback<PointerOverLinkTagEvent>(e => Record("over", e.linkID, e.linkText, e, who));
            element.RegisterCallback<PointerMoveLinkTagEvent>(e => Record("move", e.linkID, e.linkText, e, who));
            element.RegisterCallback<PointerOutLinkTagEvent>(e => Record("out", null, null, e, who));
        }

        void Record(string type, string id, string text, EventBase e, string who) =>
            m_Events.Add(type + "(" + (id ?? "-") + "|" + (text ?? "-") + ") at " + who + " target " + ((VisualElement)e.target).name + " " + e.propagationPhase);

        void Build()
        {
            AddLabel("links", "<link=\"one\">AAAA</link>BBBBBBBB<link=\"two\">CCCC</link>", 20);
            AddLabel("anchor", "<a href=\"not a url\">DDDDDDDD</a>", 120);
            AddLabel("changing", "<link=\"three\">EEEEEEEE</link>", 220);
            var plain = new VisualElement { name = "outside" };
            plain.style.position = Position.Absolute;
            plain.style.left = 500;
            plain.style.top = 400;
            plain.style.width = 50;
            plain.style.height = 50;
            m_Root.Add(plain);
        }

        static Event At(EventType type, Vector2 position) =>
            new Event { type = type, button = 0, mousePosition = position, delta = Vector2.zero, clickCount = type == EventType.MouseDown ? 1 : 0 };

        /// <summary>A point of a label: <paramref name="fraction"/> of its width from the left, at mid height.</summary>
        Vector2 Point(string label, float fraction)
        {
            var bound = m_Labels[label].worldBound;
            return new Vector2(bound.xMin + bound.width * fraction, bound.center.y);
        }

        void Send(EventBase evt, VisualElement target)
        {
            using (evt)
            {
                evt.target = target;
                target.SendEvent(evt);
            }
        }

        void Move(string label, float fraction) => Send(PointerMoveEvent.GetPooled(At(EventType.MouseMove, Point(label, fraction))), m_Labels[label]);

        void Step(string key, Action action)
        {
            m_Events.Clear();
            action();
            Put(key, string.Join(" ; ", m_Events));
        }

        IEnumerator Record()
        {
            Step("links.ontoOne", () => Move("links", 0.04f));
            Step("links.withinOne", () => Move("links", 0.08f));
            Step("links.offLinks", () => Move("links", 0.5f));
            Step("links.ontoTwo", () => Move("links", 0.96f));
            Step("links.twoToOne", () => Move("links", 0.04f));
            Step("links.downOnOne", () => Send(PointerDownEvent.GetPooled(At(EventType.MouseDown, Point("links", 0.04f))), m_Labels["links"]));
            Step("links.upOnTwo", () => Send(PointerUpEvent.GetPooled(At(EventType.MouseUp, Point("links", 0.96f))), m_Labels["links"]));
            Step("links.downOffLinks", () => Send(PointerDownEvent.GetPooled(At(EventType.MouseDown, Point("links", 0.5f))), m_Labels["links"]));
            Step("links.upOffLinks", () => Send(PointerUpEvent.GetPooled(At(EventType.MouseUp, Point("links", 0.5f))), m_Labels["links"]));
            Step("links.leave", () =>
            {
                var outside = m_Root.Q("outside");
                Send(PointerMoveEvent.GetPooled(At(EventType.MouseMove, outside.worldBound.center)), outside);
            });
            // The pointer leaves the label while it is over a link, then comes back onto the link.
            Step("links.overAgain", () => Move("links", 0.04f));
            Step("links.pointerOut", () =>
            {
                var outside = m_Root.Q("outside");
                Send(PointerOutEvent.GetPooled(At(EventType.MouseMove, outside.worldBound.center)), m_Labels["links"]);
            });
            Step("links.overAfterOut", () => Move("links", 0.04f));
            Step("anchor.over", () => Move("anchor", 0.5f));
            Step("anchor.down", () => Send(PointerDownEvent.GetPooled(At(EventType.MouseDown, Point("anchor", 0.5f))), m_Labels["anchor"]));
            Step("anchor.up", () => Send(PointerUpEvent.GetPooled(At(EventType.MouseUp, Point("anchor", 0.5f))), m_Labels["anchor"]));
            Step("changing.over", () => Move("changing", 0.5f));
            Step("changing.textChanged", () => m_Labels["changing"].text = "EEEEEEEE");
            yield return null;
            yield return null;
            Step("changing.downAfter", () => Send(PointerDownEvent.GetPooled(At(EventType.MouseDown, Point("changing", 0.5f))), m_Labels["changing"]));
            Step("changing.moveAfter", () => Move("changing", 0.4f));
            Step("changing.leaveAfter", () =>
            {
                var outside = m_Root.Q("outside");
                Send(PointerMoveEvent.GetPooled(At(EventType.MouseMove, outside.worldBound.center)), outside);
            });
        }
    }
}
