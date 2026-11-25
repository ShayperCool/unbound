using System;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using Xunit;

namespace UnityEngine.Tests
{
    // Expected values: tests/projects/editorsearchfields (Unity 6000.0.71f1 Game view OnGUI).
    [Collection("Serial")]
    public sealed class SearchFieldTests
    {
        static T Pass<T>(Event e, Func<T> draw, int instance = 37)
        {
            var previous = Event.current;
            Event.current = e;
            GUIUtility.BeginGUI(0, instance, 0);
            try { return draw(); }
            finally
            {
                GUIUtility.EndGUI(0);
                Event.current = previous;
            }
        }

        static Event Mouse(EventType type, float x, float y) => new Event { type = type, mousePosition = new Vector2(x, y), button = 0, clickCount = 1 };

        static void Reset()
        {
            GUIUtility.keyboardControl = 0;
            GUIUtility.hotControl = 0;
            EditorGUIUtility.editingTextField = false;
        }

        static readonly Rect At = new Rect(10, 300, 200, 18);

        // Styles of their own (the skins' images come from the native side); the cancel button is
        // 16 points wide.
        static readonly GUIStyle s_Field = new GUIStyle();
        static readonly GUIStyle s_Cancel = new GUIStyle { fixedWidth = 16 };

        static string Draw(SearchField field, Event e, string text) => Pass(e, () => field.OnGUI(At, text, s_Field, s_Cancel, s_Cancel));

        static int Hits(string member) =>
            Unbound.Runtime.Stubs.HitReport().Where(h => h.Key == member).Select(h => h.Value).FirstOrDefault();

        [Fact]
        public void AClickFocusesTheFieldTypingAddsTextAndTheCancelButtonClearsIt()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            Reset();
            try
            {
                var field = new SearchField();
                int arrows = 0;
                field.downOrUpArrowKeyPressed += () => arrows++;
                string text = "";
                // script/search.click.*, search.type.*: the click takes the focus, keys type.
                text = Draw(field, Mouse(EventType.MouseDown, At.x + 50, At.center.y), text);
                text = Draw(field, Mouse(EventType.MouseUp, At.x + 50, At.center.y), text);
                Assert.True(field.HasFocus());
                text = Draw(field, Event.KeyboardEvent("a"), text);
                text = Draw(field, Event.KeyboardEvent("b"), text);
                Assert.Equal("ab", text);
                // script/search.down, search.up: the arrows call back while it has the focus.
                var down = Event.KeyboardEvent("down");
                Draw(field, down, text);
                Assert.Equal(1, arrows);
                Assert.Equal(EventType.Used, down.type);
                // script/search.cancel.up: a mouse up on the cancel button clears it and drops the focus.
                text = Draw(field, Mouse(EventType.MouseUp, At.xMax - 4, At.center.y), text);
                Assert.Equal("", text);
                Assert.False(field.HasFocus());
            }
            finally { Reset(); }
        }

        [Fact]
        public void FindFocusesTheFieldAtTheNextRepaint()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            Reset();
            try
            {
                var field = new SearchField();
                // script/find.*: validated and executed (used both times), focused at the next Repaint.
                var validate = new Event { type = EventType.ValidateCommand, commandName = "Find" };
                Draw(field, validate, "");
                Assert.Equal(EventType.Used, validate.type);
                Draw(field, new Event { type = EventType.ExecuteCommand, commandName = "Find" }, "");
                Assert.False(field.HasFocus());
                Draw(field, new Event { type = EventType.Repaint }, "");
                Assert.True(field.HasFocus());
                Assert.True(EditorGUIUtility.editingTextField);
                // Without autoSetFocusOnFindCommand the command is left alone.
                var other = new SearchField { autoSetFocusOnFindCommand = false };
                var ignored = new Event { type = EventType.ValidateCommand, commandName = "Find" };
                Pass(ignored, () => other.OnGUI(At, "", s_Field, s_Cancel, s_Cancel));
                Assert.Equal(EventType.ValidateCommand, ignored.type);
            }
            finally { Reset(); }
        }

        [Fact]
        public void OutsideOnGuiASearchFieldThrowsMonosNullReference()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var previous = Event.current;
            Event.current = null;
            try
            {
                // outside/searchField: Mono's message, without a final period.
                var field = new SearchField();
                Assert.Equal("Object reference not set to an instance of an object",
                    Assert.Throws<NullReferenceException>(() => field.OnGUI(At, "x", s_Field, s_Cancel, s_Cancel)).Message);
            }
            finally { Event.current = previous; }
        }

        sealed class Logs : ILogHandler, IDisposable
        {
            readonly ILogHandler m_Previous = Debug.unityLogger.logHandler;
            internal readonly System.Collections.Generic.List<string> Lines = new System.Collections.Generic.List<string>();
            internal Logs() => Debug.unityLogger.logHandler = this;
            public void LogException(Exception exception, Object context) => Lines.Add(exception.Message);
            public void LogFormat(LogType logType, Object context, string format, params object[] args) => Lines.Add(string.Format(format, args));
            public void Dispose() => Debug.unityLogger.logHandler = m_Previous;
        }

        [Fact]
        public void ATooLongSearchStringIsCutToTheLimitAsTheCancelledDialogDoes()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            Reset();
            try
            {
                int id = 0;
                string text = new string('x', 1024);
                string Field(Event e) => Pass(e, () =>
                {
                    id = GUIUtility.GetControlID(FocusType.Keyboard, At);
                    return EditorGUI.ToolbarSearchField(id, At, text, s_Field, s_Cancel);
                });
                text = Field(Mouse(EventType.MouseDown, At.x + 50, At.center.y));
                text = Field(Mouse(EventType.MouseUp, At.x + 50, At.center.y));
                Assert.Equal(id, GUIUtility.keyboardControl);
                // (The click selects the whole text: End puts the cursor after it.)
                text = Field(Event.KeyboardEvent("end"));
                using var logs = new Logs();
                // The 1025th character: Unity asks; Unbound's dialog is cancelled (said), so the text is cut.
                text = Field(Event.KeyboardEvent("y"));
                Assert.Equal(1024, text.Length);
                Assert.Contains(logs.Lines, l => l.Contains("Search String Character Limit Exceeded"));
            }
            finally { Reset(); }
        }

        [Fact]
        public void ASearchModesPressSaysTheMenuIsNotShownAndKeepsTheMode()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            Reset();
            try
            {
                const string member = "EditorGUI.ToolbarSearchField's search mode menu";
                int before = Hits(member);
                int mode = 1;
                var down = Mouse(EventType.MouseDown, At.x + 5, At.center.y);
                Pass(down, () => EditorGUI.ToolbarSearchField(At, new[] { "All", "Name" }, ref mode, ""));
                Assert.Equal(1, mode);
                Assert.Equal(before + 1, Hits(member));
            }
            finally { Reset(); }
        }

        [Fact]
        public void AnUnmeasuredFamilysSkinStyledSearchFieldsKeepTheirTextAndSaySo()
        {
            using var release = TestRelease.Pin(TestRelease.Unmeasured);
            var field = new SearchField();
            int gui = Hits("UnityEditor.IMGUI.Controls.SearchField.OnGUI"), toolbar = Hits("UnityEditor.IMGUI.Controls.SearchField.OnToolbarGUI");
            Assert.Equal("x", field.OnGUI(At, "x"));
            Assert.Equal("y", field.OnGUI("y"));
            Assert.Equal("z", field.OnToolbarGUI(At, "z"));
            Assert.Equal("w", field.OnToolbarGUI("w"));
            Assert.Equal(gui + 2, Hits("UnityEditor.IMGUI.Controls.SearchField.OnGUI"));
            Assert.Equal(toolbar + 2, Hits("UnityEditor.IMGUI.Controls.SearchField.OnToolbarGUI"));
        }
    }
}
