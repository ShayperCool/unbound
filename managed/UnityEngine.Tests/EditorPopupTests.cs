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
    }
}
