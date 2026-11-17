using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using Unbound.UIElements;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>USS cursors on runtime panels (Unity 6000.0.71f1, tests/projects/uitkcursors). Controls
    /// outside the probe: the keyword ids read from USS, the manager on its own, a capture end with
    /// nothing under the pointer, and the cursor of an element that is not the one under the pointer.</summary>
    [Collection("UXML custom controls")]
    public sealed class UitkCursorTests : IDisposable
    {
        readonly List<string> m_Logs = new List<string>();

        public UitkCursorTests() => Application.logMessageReceived += Record;

        public void Dispose() => Application.logMessageReceived -= Record;

        // Warnings only: the test host has no native cursor to set (Cursor.SetCursor reports that).
        void Record(string message, string stack, LogType type)
        {
            if (type == LogType.Warning) m_Logs.Add(type + ": " + message);
        }

        const string k_Warning = "Warning: Runtime cursors other than the default cursor need to be defined using a texture.";

        static Texture2D Image() => new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "cursor" };

        // Every keyword but `arrow` names a cursor other than the default; a keyword USS does not
        // know names none.
        [Fact]
        public void KeywordsReadFromUssHaveIds()
        {
            using var panel = new Panel(null, ContextType.Player, EventDispatcher.CreateDefault(), null);
            var sheet = ScriptableObject.CreateInstance<StyleSheet>();
            new UssImporter("cursors.uss").Import(sheet, ".a { cursor: arrow; } .b { cursor: not-allowed; } .c { cursor: resize-up-left; } .d { cursor: no-such-cursor; }");
            panel.visualTree.styleSheets.Add(sheet);
            var elements = new Dictionary<string, VisualElement>();
            foreach (var name in new[] { "a", "b", "c", "d" })
            {
                var e = new VisualElement();
                e.AddToClassList(name);
                panel.visualTree.Add(e);
                elements[name] = e;
            }
            panel.ApplyStyles();
            Assert.Equal(0, elements["a"].computedValues.cursor.defaultCursorId);
            Assert.NotEqual(0, elements["b"].computedValues.cursor.defaultCursorId);
            Assert.NotEqual(0, elements["c"].computedValues.cursor.defaultCursorId);
            Assert.NotEqual(elements["b"].computedValues.cursor.defaultCursorId, elements["c"].computedValues.cursor.defaultCursorId);
            Assert.Equal(0, elements["d"].computedValues.cursor.defaultCursorId);
        }

        // A texture cursor is set and later restored once; a keyword cursor is reported and restores
        // the default; the default cursor restores it without a report.
        [Fact]
        public void TheManagerSetsTexturesAndRestoresTheDefaultOnce()
        {
            var manager = new CursorManager();
            manager.ResetCursor();
            Assert.False(manager.isCursorOverriden);
            manager.SetCursor(new UnityEngine.UIElements.Cursor { texture = Image(), hotspot = new Vector2(1, 2) });
            Assert.True(manager.isCursorOverriden);
            m_Logs.Clear();
            manager.SetCursor(new UnityEngine.UIElements.Cursor { defaultCursorId = 4 });
            Assert.False(manager.isCursorOverriden);
            Assert.Equal(new[] { k_Warning }, m_Logs);
            m_Logs.Clear();
            manager.SetCursor(default);
            Assert.Empty(m_Logs);
        }

        // An element entered that is not the top one under the pointer (an ancestor receiving the
        // event) does not change the cursor; the end of a capture with nothing under the pointer
        // restores the default.
        [Fact]
        public void OnlyTheElementUnderThePointerSetsItsCursor()
        {
            using var panel = new Panel(null, ContextType.Player, EventDispatcher.CreateDefault(), null);
            var element = new VisualElement();
            element.style.cursor = new StyleCursor(new UnityEngine.UIElements.Cursor { texture = Image() });
            panel.visualTree.Add(element);
            panel.ApplyStyles();
            var manager = (CursorManager)panel.cursorManager;
            element.UpdateCursorStyle(EventBase<MouseOverEvent>.TypeId());
            Assert.False(manager.isCursorOverriden);
            manager.SetCursor(element.computedValues.cursor);
            Assert.True(manager.isCursorOverriden);
            element.UpdateCursorStyle(EventBase<MouseCaptureOutEvent>.TypeId());
            Assert.False(manager.isCursorOverriden);
        }
    }
}
