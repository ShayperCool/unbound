using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Command and IME events of text fields (Unity 6000.0.71f1, tests/projects/textcommands
    /// for the commands, the IME mode and IMEEvent's pooling and propagation; the composition
    /// preview follows the release's text editing with a stand-in input method, as the Editor
    /// probe cannot compose). Controls outside the probe: a paste with Windows line ends into a
    /// single-line and a multiline field, and a read-only field leaving an IME mode set before.</summary>
    public sealed class UitkTextCommandTests : IDisposable
    {
        readonly Panel m_Panel;
        readonly VisualElement m_Container;
        readonly int m_Frame = Time.s_FrameCount;
        readonly string m_Clipboard = GUIUtility.systemCopyBuffer;
        readonly List<string> m_ImeRequests = new List<string>();
        string m_Composition = "";
        int m_Reached;

        public UitkTextCommandTests()
        {
            m_Panel = new Panel(null, ContextType.Player, EventDispatcher.CreateDefault(), null);
            m_Panel.visualTree.style.width = 400;
            m_Panel.visualTree.style.height = 300;
            m_Container = new VisualElement();
            m_Panel.visualTree.Add(m_Container);
            m_Panel.visualTree.RegisterCallback<ValidateCommandEvent>(_ => m_Reached++);
            m_Panel.visualTree.RegisterCallback<ExecuteCommandEvent>(_ => m_Reached++);
            Input.ResetIme();
            Input.ImeHost = ImeHost;
        }

        public void Dispose()
        {
            Input.ResetIme();
            Time.s_FrameCount = m_Frame;
            GUIUtility.systemCopyBuffer = m_Clipboard;
            m_Panel.Dispose();
        }

        string ImeHost(string method, string request)
        {
            if (method == "ime-request") m_ImeRequests.Add(request);
            return method == "ime-state"
                ? "{\"available\":true,\"composition\":" + System.Text.Json.JsonSerializer.Serialize(m_Composition) + ",\"cursor\":null,\"selected\":true}"
                : "{}";
        }

        void Tick()
        {
            m_Panel.uaScheduler.UpdateScheduledEvents();
            m_Panel.ApplyStyles();
            m_Panel.ValidateLayout();
        }

        TextField Field(string value, bool multiline = false)
        {
            var field = new TextField("Field") { value = value, multiline = multiline };
            m_Container.Add(field);
            Tick();
            return field;
        }

        static TextElement Text(TextField field) => field.Q(className: TextField.inputUssClassName).Q<TextElement>();

        void Focus(TextField field, int cursor, int select)
        {
            field.Focus();
            Tick();
            field.SelectRange(cursor, select);
            Tick();
        }

        int Validate(TextField field, string command)
        {
            m_Reached = 0;
            using (var e = ValidateCommandEvent.GetPooled(command)) { e.target = Text(field); Text(field).SendEvent(e); }
            return m_Reached;
        }

        int Execute(TextField field, string command)
        {
            m_Reached = 0;
            using (var e = ExecuteCommandEvent.GetPooled(command)) { e.target = Text(field); Text(field).SendEvent(e); }
            return m_Reached;
        }

        void Compose(TextField field, string composition)
        {
            m_Composition = composition;
            Time.s_FrameCount++;
            using (var e = IMEEvent.GetPooled(composition)) { e.target = Text(field); Text(field).SendEvent(e); }
        }

        [Fact]
        public void TheInterestDeclarationListsTheCommandAndImeEvents()
        {
            var method = typeof(TextElement).GetMethod("HandleEventBubbleUp", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var interest = (EventInterestAttribute)Attribute.GetCustomAttribute(method, typeof(EventInterestAttribute), inherit: false);
            Assert.Contains(typeof(ValidateCommandEvent), interest.eventTypes);
            Assert.Contains(typeof(ExecuteCommandEvent), interest.eventTypes);
            Assert.Contains(typeof(IMEEvent), interest.eventTypes);
        }

        [Fact]
        public void AFocusedFieldValidatesTheCommandsItCanPerform()
        {
            GUIUtility.systemCopyBuffer = "CLIP";
            var field = Field("hello world");
            Assert.Equal(1, Validate(field, "SelectAll"));
            Focus(field, 0, 0);
            Assert.Equal(1, Validate(field, "Copy"));
            Assert.Equal(1, Validate(field, "Cut"));
            Assert.Equal(0, Validate(field, "Paste"));
            Assert.Equal(0, Validate(field, "Delete"));
            Assert.Equal(0, Validate(field, "SelectAll"));
            Assert.Equal(0, Validate(field, "Find"));
            GUIUtility.systemCopyBuffer = "";
            Assert.Equal(1, Validate(field, "Paste"));
            field.SelectRange(0, 5);
            Assert.Equal(0, Validate(field, "Copy"));
            Assert.Equal(0, Validate(field, "Cut"));
        }

        [Fact]
        public void ExecutedCommandsEditTheTextAndTheClipboard()
        {
            GUIUtility.systemCopyBuffer = "CLIP";
            var field = Field("hello world");
            var changes = new List<string>();
            field.RegisterValueChangedCallback(e => changes.Add(e.previousValue + ">" + e.newValue));
            Focus(field, 6, 11);
            Assert.Equal(0, Execute(field, "Cut"));
            Assert.Equal(("hello ", "world", 6, 6), (field.value, GUIUtility.systemCopyBuffer, field.cursorIndex, field.selectIndex));
            Assert.Equal(0, Execute(field, "Paste"));
            Assert.Equal("hello world", field.value);
            Assert.Equal(0, Execute(field, "SelectAll"));
            Assert.Equal((0, 11), (field.cursorIndex, field.selectIndex));
            // Delete removes the selection, or the character after the cursor, without copying.
            field.SelectRange(0, 6);
            GUIUtility.systemCopyBuffer = "kept";
            Assert.Equal(0, Execute(field, "Delete"));
            Assert.Equal(("world", "kept"), (field.value, GUIUtility.systemCopyBuffer));
            field.SelectRange(1, 1);
            Assert.Equal(0, Execute(field, "Delete"));
            Assert.Equal("wrld", field.value);
            // Cut without a selection changes nothing but is still taken; an unknown command passes.
            Assert.Equal(0, Execute(field, "Cut"));
            Assert.Equal(("wrld", "kept"), (field.value, GUIUtility.systemCopyBuffer));
            Assert.Equal(1, Execute(field, "Duplicate"));
            Assert.Equal(new[] { "hello world>hello ", "hello >hello world", "hello world>world", "world>wrld" }, changes);
        }

        [Fact]
        public void PasteReplacesLineBreaksOnlyInASingleLineField()
        {
            GUIUtility.systemCopyBuffer = "a\r\nb\nc";
            var single = Field("");
            Focus(single, 0, 0);
            Execute(single, "Paste");
            Assert.Equal("a b c", single.value);
            var multi = Field("", multiline: true);
            Focus(multi, 0, 0);
            Execute(multi, "Paste");
            Assert.Equal("a\r\nb\nc", multi.value);
        }

        [Fact]
        public void AReadOnlyFieldSelectsAndCopiesButDoesNotEdit()
        {
            GUIUtility.systemCopyBuffer = "CLIP";
            var field = Field("locked text");
            field.isReadOnly = true;
            Input.imeCompositionMode = IMECompositionMode.Off;
            Focus(field, 0, 6);
            Assert.Equal(IMECompositionMode.Off, Input.imeCompositionMode);
            Assert.Equal(0, Validate(field, "Copy"));
            Assert.Equal(1, Validate(field, "Cut"));
            Assert.Equal(1, Validate(field, "Paste"));
            Assert.Equal(0, Validate(field, "Find"));
            Assert.Equal(0, Execute(field, "Copy"));
            Assert.Equal("locked", GUIUtility.systemCopyBuffer);
            Assert.Equal(1, Execute(field, "Cut"));
            Assert.Equal(1, Execute(field, "Paste"));
            Assert.Equal("locked text", field.value);
        }

        [Fact]
        public void APooledCommandEventIsResetAndCanReadItsNameFromAnImguiEvent()
        {
            var first = ExecuteCommandEvent.GetPooled("SelectAll");
            first.StopPropagation();
            first.Dispose();
            var second = ExecuteCommandEvent.GetPooled();
            Assert.Same(first, second);
            Assert.Null(second.commandName);
            Assert.False(second.isPropagationStopped);
            second.Dispose();
            using var imgui = ExecuteCommandEvent.GetPooled(new Event { type = EventType.ExecuteCommand, commandName = "Copy" });
            Assert.Equal("Copy", imgui.commandName);
        }

        [Fact]
        public void FocusTurnsTheInputMethodOnAndBlurBackToAuto()
        {
            var field = Field("hello");
            Assert.Equal(IMECompositionMode.Auto, Input.imeCompositionMode);
            Focus(field, 0, 0);
            Assert.Equal(IMECompositionMode.On, Input.imeCompositionMode);
            Assert.Contains("\"enabled\":true", m_ImeRequests[m_ImeRequests.Count - 1]);
            field.Blur();
            Tick();
            Assert.Equal(IMECompositionMode.Auto, Input.imeCompositionMode);
        }

        [Fact]
        public void ACompositionIsPreviewedAtTheCursorUntilItEnds()
        {
            var field = Field("hello");
            var changes = new List<string>();
            field.RegisterValueChangedCallback(e => changes.Add(e.newValue));
            Focus(field, 2, 4);
            // A composition replaces the selection and shows at the cursor, which moves after it.
            Compose(field, "かな");
            Assert.Equal("heかなo", field.value);
            Assert.Equal((4, 4), (field.cursorIndex, field.selectIndex));
            Compose(field, "かなu");
            Assert.Equal("heかなuo", field.value);
            Assert.Equal(5, field.cursorIndex);
            // The composition ends (the input method commits its text through the typed characters).
            Compose(field, "");
            Assert.Equal("heo", field.value);
            Assert.Equal((2, 2), (field.cursorIndex, field.selectIndex));
            Assert.Equal(new[] { "heかなo", "heかなuo", "heo" }, changes);
            // An IME event without a composition changes nothing.
            Compose(field, "");
            Assert.Equal(3, changes.Count);
        }

        [Fact]
        public void AnImeEventIsPooledAndPassesTheContainer()
        {
            var field = Field("hello");
            int bubbled = 0;
            m_Container.RegisterCallback<IMEEvent>(_ => bubbled++);
            Focus(field, 1, 1);
            var first = IMEEvent.GetPooled("ka");
            Assert.Equal("ka", first.compositionString);
            first.target = Text(field);
            Text(field).SendEvent(first);
            Assert.False(first.isPropagationStopped);
            first.Dispose();
            Assert.Equal(1, bubbled);
            var reused = IMEEvent.GetPooled();
            Assert.Same(first, reused);
            Assert.Null(reused.compositionString);
            reused.Dispose();
        }
    }
}
