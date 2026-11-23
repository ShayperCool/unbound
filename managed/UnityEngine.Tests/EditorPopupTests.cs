using System;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>EditorGUI.Popup's control and its menu's answer (tests/projects/imguiskin `popup/*`,
    /// Unity 6000.0.71f1).</summary>
    [Collection("Serial")]
    public sealed class EditorPopupTests
    {
        static readonly string[] s_Options = { "First", "Second", "Third" };
        // (A style of its own: the skin's popup images come from the native side.)
        static readonly GUIStyle s_Style = new GUIStyle();

        sealed class Logs : ILogHandler
        {
            internal readonly System.Collections.Generic.List<string> Messages = new System.Collections.Generic.List<string>();
            public void LogFormat(LogType type, Object context, string format, params object[] args) => Messages.Add(string.Format(format, args));
            public void LogException(Exception exception, Object context) => Messages.Add(exception.Message);
        }


        static int Pass(Event current, Func<int> body)
        {
            var previous = Event.current;
            Event.current = current;
            GUIUtility.BeginGUI(0, 7, 1);
            GUI.ResetState();
            try { return body(); }
            finally
            {
                GUILayoutUtility.End();
                Event.current = previous;
            }
        }

        [Fact]
        public void APopupTakesOneControlIdAndKeepsItsSelection()
        {
            int ids = 0, value = 0;
            Pass(new Event { type = EventType.Repaint }, () =>
            {
                int before = GUIUtility.GetControlID(FocusType.Passive);
                value = EditorGUI.Popup(new Rect(10, 10, 200, 18), "Pick", 7, s_Options, s_Style);
                ids = GUIUtility.GetControlID(FocusType.Passive) - before;
                return 0;
            });
            Assert.Equal(7, value);
            Assert.Equal(2, ids);
            // A missing list throws once the control's ID is taken.
            Assert.Throws<NullReferenceException>(() => Pass(new Event { type = EventType.Repaint }, () => EditorGUI.Popup(new Rect(10, 10, 200, 18), 0, (string[])null, s_Style)));
        }

        [Fact]
        public void TheMenusChoiceComesBackAsTheCommandForItsControl()
        {
            foreach (var (chosen, mixed, changed) in new[] { (2, false, true), (1, false, false), (1, true, true) })
            {
                var command = new Event { type = EventType.ExecuteCommand, commandName = "PopupMenuChanged" };
                bool reported = false;
                int value = Pass(command, () =>
                {
                    int before = GUIUtility.GetControlID(FocusType.Passive);
                    var info = new EditorGUI.PopupCallbackInfo(before + 1);
                    info.SetEnumValueDelegate(null, null, chosen);
                    EditorGUI.PopupCallbackInfo.instance = info;
                    GUI.changed = false;
                    EditorGUI.showMixedValue = mixed;
                    try { return EditorGUI.Popup(new Rect(10, 10, 200, 18), 1, s_Options, s_Style); }
                    finally
                    {
                        EditorGUI.showMixedValue = false;
                        reported = GUI.changed;
                    }
                });
                Assert.Equal(chosen, value);
                Assert.Equal(changed, reported);
                Assert.Equal(EventType.Used, command.type);
                Assert.Null(EditorGUI.PopupCallbackInfo.instance);
            }
        }
    

        [Fact]
        public void AClickGivesThePopupKeyboardFocusAndUsesTheEvent()
        {
            try
            {
                var click = new Event { type = EventType.MouseDown, button = 0, mousePosition = new Vector2(100, 15) };
                int id = 0;
                Pass(click, () =>
                {
                    id = GUIUtility.GetControlID(FocusType.Passive) + 1;
                    return EditorGUI.Popup(new Rect(10, 10, 200, 18), 1, s_Options, s_Style);
                });
                Assert.Equal(EventType.Used, click.type);
                Assert.Equal(id, GUIUtility.keyboardControl);
                // Elsewhere, or with another button: neither.
                foreach (var other in new[]
                {
                    new Event { type = EventType.MouseDown, button = 0, mousePosition = new Vector2(100, 60) },
                    new Event { type = EventType.MouseDown, button = 1, mousePosition = new Vector2(100, 15) },
                })
                {
                    GUIUtility.keyboardControl = 0;
                    Pass(other, () => EditorGUI.Popup(new Rect(10, 10, 200, 18), 1, s_Options, s_Style));
                    Assert.Equal(EventType.MouseDown, other.type);
                    Assert.Equal(0, GUIUtility.keyboardControl);
                }
            }
            finally
            {
                EditorGUI.PopupCallbackInfo.instance = null;
                GUIUtility.keyboardControl = 0;
            }
        }

        [Fact]
        public void AnotherReleaseFamilysPopupSaysSoAndKeepsItsSelection()
        {
            // Its inspector styles and control rects are not measured: no style, no layout.
            using var release = TestRelease.Pin(TestRelease.Unmeasured);
            var logs = new Logs();
            var previous = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = logs;
            try
            {
                int value = Pass(new Event { type = EventType.Layout }, () => EditorGUILayout.Popup(2, s_Options));
                Assert.Equal(2, value);
                Assert.Equal(1, Pass(new Event { type = EventType.Repaint }, () => EditorGUI.Popup(new Rect(0, 0, 100, 18), "Pick", 1, s_Options)));
                Assert.Contains(logs.Messages, m => m.Contains("EditorGUILayout.Popup"));
            }
            finally { Debug.unityLogger.logHandler = previous; }
        }
    }
}
