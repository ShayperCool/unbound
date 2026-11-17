using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;
using UnityEngine.UIElements.Experimental;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Rich-text link events (Unity 6000.0.71f1, tests/projects/uitklinks). Controls outside
    /// the probe: the events copy their trigger's pointer data and carry the link, they bubble and
    /// trickle down; and, on text laid out by hand (one 20-point-wide character per link), two spans
    /// of one link id, a move onto an <c>&lt;a&gt;</c> link, the addresses an <c>&lt;a href&gt;</c>
    /// release opens and the link cursor of Editor panels.</summary>
    [Collection("UXML custom controls")]
    public class UitkLinkEventTests
    {
        static PointerMoveEvent Trigger(Vector2 position) =>
            PointerMoveEvent.GetPooled(new Event { type = EventType.MouseMove, mousePosition = position });

        [Fact]
        public void LinkEventsCarryTheLinkAndTheTriggersPointer()
        {
            using var trigger = Trigger(new Vector2(12, 34));
            using var over = PointerOverLinkTagEvent.GetPooled(trigger, "id", "text");
            Assert.Equal("id", over.linkID);
            Assert.Equal("text", over.linkText);
            Assert.Equal(new Vector3(12, 34, 0), over.position);
            Assert.Equal(trigger.pointerId, over.pointerId);
            Assert.True(over.bubbles);
            Assert.True(over.tricklesDown);
            using var outEvent = PointerOutLinkTagEvent.GetPooled(trigger, "ignored");
            Assert.True(outEvent.bubbles && outEvent.tricklesDown);
        }

        // A bubbling event reaches the text element's ancestors after the text element itself (whose
        // callbacks run in the bubble-up phase, as the probe's).
        [Fact]
        public void LinkEventsBubbleThroughAncestors()
        {
            using var panel = new Panel(null, ContextType.Player, EventDispatcher.CreateDefault(), null);
            var parent = new VisualElement();
            var label = new Label("x");
            parent.Add(label);
            panel.visualTree.Add(parent);
            var order = new List<string>();
            parent.RegisterCallback<PointerDownLinkTagEvent>(e => order.Add("parent " + e.propagationPhase), TrickleDown.TrickleDown);
            parent.RegisterCallback<PointerDownLinkTagEvent>(e => order.Add("parent " + e.propagationPhase));
            label.RegisterCallback<PointerDownLinkTagEvent>(e => order.Add("label " + e.propagationPhase));
            using var trigger = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, mousePosition = Vector2.zero });
            using (var down = PointerDownLinkTagEvent.GetPooled(trigger, "a", "b"))
            {
                down.elementTarget = label;
                label.SendEvent(down);
            }
            Assert.Equal(new[] { "parent TrickleDown", "label BubbleUp", "parent BubbleUp" }, order);
        }

        /// <summary>A link of the hand-made text: its id (an <c>&lt;a href&gt;</c> address when
        /// <c>Href</c>), its one character and where that character starts.</summary>
        struct Span
        {
            public string Id;
            public bool Href;
            public char Character;
            public float X0;
        }

        static Span Link(string id, char c, float x0) => new Span { Id = id, Character = c, X0 = x0 };
        static Span Anchor(string href, char c, float x0) => new Span { Id = href, Href = true, Character = c, X0 = x0 };

        /// <summary>A label in a panel whose text information is <paramref name="spans"/> (characters
        /// 20 points wide and high from the label's top), with its link handling set up as after
        /// generation.</summary>
        static Label LaidOut(Panel panel, params Span[] spans)
        {
            var label = new Label("x");
            label.style.position = Position.Absolute;
            label.style.left = 0;
            label.style.top = 0;
            label.style.width = 200;
            label.style.height = 20;
            panel.visualTree.Add(label);
            Generate(panel, label, spans);
            return label;
        }

        /// <summary>Lays out the label in its panel and gives it <paramref name="spans"/> as its
        /// generated text, with the handling generation sets up.</summary>
        static void Generate(Panel panel, Label label, params Span[] spans) => Generate(panel, label, spans, setUp: true);

        static void Generate(Panel panel, Label label, Span[] spans, bool setUp)
        {
            panel.ApplyStyles();
            panel.ValidateLayout();
            var info = new TextInfo
            {
                textElementInfo = new TextElementInfo[spans.Length],
                linkInfo = new LinkInfo[spans.Length],
                characterCount = spans.Length,
                linkCount = spans.Length,
            };
            for (int i = 0; i < spans.Length; i++)
            {
                var s = spans[i];
                // The text's y axis points up from its top: a point 10 down is at -10.
                info.textElementInfo[i] = new TextElementInfo
                {
                    character = s.Character,
                    bottomLeft = new Vector3(s.X0, -20f, 0f),
                    topRight = new Vector3(s.X0 + 20f, 0f, 0f),
                    ascender = 0f,
                    descender = -20f,
                };
                var link = new LinkInfo
                {
                    hashCode = s.Href ? (int)MarkupTag.HREF : s.Id.GetHashCode(),
                    linkTextfirstCharacterIndex = i,
                    linkTextLength = 1,
                };
                link.SetLinkId(s.Id.ToCharArray(), 0, s.Id.Length);
                info.linkInfo[i] = link;
            }
            var handle = label.uitkTextHandle;
            handle.TextInfoNode = new LinkedListNode<TextInfo>(info);
            // Kept as the permanent cache keeps it, so the handler's bookkeeping does not generate it again.
            handle.IsCachedPermanent = true;
            if (!setUp) return;
            var handler = handle.m_TextEventHandler;
            handler.HandleATag();
            handler.HandleLinkTag();
            handler.HandleLinkAndATagCallbacks();
        }

        static void Send(Label label, EventType type, float x)
        {
            var e = new Event { type = type, button = 0, mousePosition = new Vector2(x, 10f), clickCount = type == EventType.MouseDown ? 1 : 0 };
            using EventBase evt = type switch
            {
                EventType.MouseDown => PointerDownEvent.GetPooled(e),
                EventType.MouseUp => PointerUpEvent.GetPooled(e),
                _ => PointerMoveEvent.GetPooled(e),
            };
            evt.target = label;
            label.SendEvent(evt);
        }

        static List<string> Watch(Label label)
        {
            var seen = new List<string>();
            label.RegisterCallback<PointerOverLinkTagEvent>(e => seen.Add("over " + e.linkID + "|" + e.linkText));
            label.RegisterCallback<PointerMoveLinkTagEvent>(e => seen.Add("move " + e.linkID + "|" + e.linkText));
            label.RegisterCallback<PointerOutLinkTagEvent>(e => seen.Add("out"));
            label.RegisterCallback<PointerDownLinkTagEvent>(e => seen.Add("down " + e.linkID));
            label.RegisterCallback<PointerUpLinkTagEvent>(e => seen.Add("up " + e.linkID));
            return seen;
        }

        [Fact]
        public void SpansOfOneLinkMoveAndOtherLinksOrAnchorsSendOut()
        {
            using var panel = new Panel(null, ContextType.Player, EventDispatcher.CreateDefault(), null);
            // The anchor's address is not a URL, so releasing over it opens nothing.
            var label = LaidOut(panel, Link("k", 'A', 0), Link("k", 'B', 40), Link("m", 'C', 80), Anchor("not a url", 'D', 120));
            var seen = Watch(label);
            Send(label, EventType.MouseMove, 10);
            Send(label, EventType.MouseMove, 50);   // the same id's other span: a move
            Send(label, EventType.MouseMove, 90);   // another link: out only
            Send(label, EventType.MouseMove, 95);   // then over it
            Send(label, EventType.MouseMove, 130);  // an <a> link: out
            Send(label, EventType.MouseMove, 135);  // still none
            Send(label, EventType.MouseDown, 10);
            Send(label, EventType.MouseUp, 130);    // over the <a>: no link event
            Send(label, EventType.MouseUp, 30);     // between the spans: no link
            Assert.Equal(new[] { "over k|A", "move k|B", "out", "over m|C", "out", "down k" }, seen);
        }

        // The probe's links.pointerOut leaves the label from link one; here from a second span of
        // one id, and a pointer out off the links sends nothing.
        [Fact]
        public void LeavingTheElementOverALinkSendsOutOnce()
        {
            using var panel = new Panel(null, ContextType.Player, EventDispatcher.CreateDefault(), null);
            var label = LaidOut(panel, Link("k", 'A', 0), Link("k", 'B', 40));
            var seen = Watch(label);
            Send(label, EventType.MouseMove, 50);
            SendOut(label);
            SendOut(label);                         // already off the link: nothing
            Send(label, EventType.MouseMove, 30);   // between the spans: nothing either
            Send(label, EventType.MouseMove, 10);   // back onto the link: over again
            Assert.Equal(new[] { "over k|B", "out", "over k|A" }, seen);
        }

        static void SendOut(Label label)
        {
            using var evt = PointerOutEvent.GetPooled(new Event { type = EventType.MouseMove, mousePosition = new Vector2(500f, 500f) });
            evt.target = label;
            label.SendEvent(evt);
        }

        [Fact]
        public void TextWithoutLinksAfterwardsStopsTheEvents()
        {
            using var panel = new Panel(null, ContextType.Player, EventDispatcher.CreateDefault(), null);
            var label = LaidOut(panel, Link("k", 'A', 0));
            var seen = Watch(label);
            Send(label, EventType.MouseMove, 10);
            var handler = label.uitkTextHandle.m_TextEventHandler;
            label.uitkTextHandle.textInfo.linkCount = 0;
            handler.HandleLinkTag();
            handler.HandleLinkAndATagCallbacks();
            Send(label, EventType.MouseMove, 50);
            Send(label, EventType.MouseDown, 10);
            Assert.Equal(new[] { "over k|A" }, seen);
            Assert.False(handler.hasLinkTag);
        }

        [Fact]
        public void ReleasingOverAnAnchorOpensOnlyAWellFormedAbsoluteAddress()
        {
            var dir = Path.Combine(Path.GetTempPath(), "unbound-openurl-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var log = Path.Combine(dir, "opened.txt");
            var opener = Path.Combine(dir, "opener.sh");
            File.WriteAllText(opener, "#!/bin/sh\nprintf '%s\\n' \"$1\" >> '" + log + "'\n");
            File.SetUnixFileMode(opener, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var previous = Environment.GetEnvironmentVariable("UNBOUND_OPEN_URL");
            Environment.SetEnvironmentVariable("UNBOUND_OPEN_URL", opener);
            try
            {
                using var panel = new Panel(null, ContextType.Player, EventDispatcher.CreateDefault(), null);
                var label = LaidOut(panel, Anchor("https://example.com/a", 'A', 0), Anchor("not a url", 'B', 40),
                    Anchor("relative/page.html", 'C', 80), Link("https://example.com/link", 'D', 120));
                Send(label, EventType.MouseUp, 50);    // not a URL
                Send(label, EventType.MouseUp, 90);    // relative
                Send(label, EventType.MouseUp, 130);   // a <link>, not an <a>
                Send(label, EventType.MouseUp, 170);   // off the links
                Send(label, EventType.MouseDown, 10);  // a press opens nothing
                Send(label, EventType.MouseUp, 10);
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (!File.Exists(log) && DateTime.UtcNow < deadline) Thread.Sleep(20);
                // Time for any other opener to have written.
                Thread.Sleep(300);
                Assert.Equal(new[] { "https://example.com/a" }, File.ReadAllLines(log));
            }
            finally
            {
                Environment.SetEnvironmentVariable("UNBOUND_OPEN_URL", previous);
                Directory.Delete(dir, true);
            }
        }

        sealed class CursorRecorder : ICursorManager
        {
            internal readonly List<string> Calls = new List<string>();
            public void SetCursor(UnityEngine.UIElements.Cursor cursor) => Calls.Add("set " + cursor.defaultCursorId);
            public void ResetCursor() => Calls.Add("reset");
        }

        [Theory]
        [InlineData(ContextType.Editor)]
        [InlineData(ContextType.Player)]
        public void EditorPanelsShowTheLinkCursorOverAnchorsOnly(ContextType context)
        {
            using var panel = new Panel(null, context, EventDispatcher.CreateDefault(), null);
            var cursors = new CursorRecorder();
            panel.cursorManager = cursors;
            var label = LaidOut(panel, Anchor("https://example.com/", 'A', 0), Link("k", 'B', 40));
            Send(label, EventType.MouseMove, 10);   // onto the <a>: the link cursor
            Send(label, EventType.MouseMove, 15);   // still on it: no change
            Send(label, EventType.MouseMove, 50);   // onto a <link>: the element's own cursor
            Send(label, EventType.MouseMove, 55);
            Send(label, EventType.MouseMove, 10);   // back onto the <a>
            Send(label, EventType.MouseMove, 30);   // off the links
            var expected = context == ContextType.Editor ? new[] { "set 4", "set 0", "set 4", "set 0" } : Array.Empty<string>();
            Assert.Equal(expected, cursors.Calls);
        }

        // The cursor handling follows the panel its text is generated in: a label moved from a
        // runtime panel into an Editor panel gets it with its next generation.
        [Fact]
        public void AnAnchorMovedIntoAnEditorPanelGetsTheLinkCursorWithItsNextGeneration()
        {
            using var runtime = new Panel(null, ContextType.Player, EventDispatcher.CreateDefault(), null);
            using var editor = new Panel(null, ContextType.Editor, EventDispatcher.CreateDefault(), null);
            var cursors = new CursorRecorder();
            editor.cursorManager = cursors;
            var anchor = Anchor("https://example.com/", 'A', 0);
            var label = LaidOut(runtime, anchor);
            editor.visualTree.Add(label);
            Send(label, EventType.MouseMove, 10);
            Assert.Empty(cursors.Calls);
            Generate(editor, label, anchor);
            Send(label, EventType.MouseMove, 10);
            Assert.Equal(new[] { "set 4" }, cursors.Calls);
        }

        // Text without <a> links generated in a runtime panel keeps the cursor handling; generated
        // without them in an Editor panel again, it removes it.
        [Fact]
        public void TheLinkCursorHandlingIsRemovedOnlyByAGenerationInAnEditorPanel()
        {
            using var runtime = new Panel(null, ContextType.Player, EventDispatcher.CreateDefault(), null);
            using var editor = new Panel(null, ContextType.Editor, EventDispatcher.CreateDefault(), null);
            var cursors = new CursorRecorder();
            editor.cursorManager = cursors;
            runtime.cursorManager = cursors;
            var anchor = Anchor("https://example.com/", 'A', 0);
            var plain = Link("k", 'A', 0);
            var label = LaidOut(editor, anchor);
            runtime.visualTree.Add(label);
            Generate(runtime, label, plain);
            // The <a> text again without its handling set up: the cursor handling is still there.
            Generate(runtime, label, new[] { anchor }, setUp: false);
            Send(label, EventType.MouseMove, 10);
            Send(label, EventType.MouseMove, 30);
            Assert.Equal(new[] { "set 4", "set 0" }, cursors.Calls);
            cursors.Calls.Clear();
            editor.visualTree.Add(label);
            Generate(editor, label, plain);
            Generate(editor, label, new[] { anchor }, setUp: false);
            Send(label, EventType.MouseMove, 10);
            Assert.Empty(cursors.Calls);
        }
    }
}
