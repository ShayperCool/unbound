using System;
using System.Linq;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    public class ToggleSlidersHolder : ScriptableObject
    {
        public float floatValue = 2f;
        public int intValue = 3;
        public bool boolValue = true;
    }

    // Expected values: tests/projects/editortogglefields (Unity 6000.0.71f1 Game view OnGUI).
    [Collection("Serial")]
    public sealed class EditorTogglesSlidersTests
    {
        static T Pass<T>(Event e, Func<T> draw, int instance = 41)
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

        static Event Mouse(EventType type, float x, float y, int button = 0) =>
            new Event { type = type, mousePosition = new Vector2(x, y), button = button, clickCount = 1 };

        static void Reset()
        {
            GUIUtility.keyboardControl = 0;
            GUIUtility.hotControl = 0;
        }

        static int Hits(string member) =>
            Unbound.Runtime.Stubs.HitReport().Where(h => h.Key == member).Select(h => h.Value).FirstOrDefault();

        // A style of its own (the skins' images come from the native side).
        static readonly GUIStyle s_Toggle = new GUIStyle { fixedWidth = 14, fixedHeight = 14 };
        static readonly Rect At = new Rect(10, 300, 200, 18);

        [Fact]
        public void TheLeftButtonTogglesAndFocusesTheRightDoesNothingSpaceToggles()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            Reset();
            try
            {
                bool value = false;
                bool Draw(Event e) => Pass(e, () => EditorGUI.Toggle(At, value, s_Toggle));
                // script/t1.*: down takes the mouse, up toggles; the toggle keeps the keyboard focus.
                value = Draw(Mouse(EventType.MouseDown, At.x + 5, At.center.y));
                value = Draw(Mouse(EventType.MouseUp, At.x + 5, At.center.y));
                Assert.True(value);
                Assert.NotEqual(0, GUIUtility.keyboardControl);
                // script/t1.right.*: the event is left for others (not used) and nothing changes.
                var right = Mouse(EventType.MouseDown, At.x + 5, At.center.y, 1);
                value = Draw(right);
                Assert.Equal(EventType.MouseDown, right.type);
                Assert.True(value);
                // script/t1.space: Space toggles the focused toggle.
                value = Draw(Event.KeyboardEvent("space"));
                Assert.False(value);
            }
            finally { Reset(); }
        }

        [Fact]
        public void APropertySliderWritesItsPropertyOnlyWhenItChanges()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var holder = ScriptableObject.CreateInstance<ToggleSlidersHolder>();
            Reset();
            try
            {
                var so = new SerializedObject(holder);
                var r = new Rect(10, 360, 300, 18);
                // rect/*/sliders: drawing does not change the value, even outside the limits.
                Pass(new Event { type = EventType.Repaint }, () => { EditorGUI.Slider(r, so.FindProperty("floatValue"), 5f, 10f); return 0; });
                Pass(new Event { type = EventType.Repaint }, () => { EditorGUI.IntSlider(r, so.FindProperty("intValue"), 5, 10); return 0; });
                so.ApplyModifiedProperties();
                Assert.Equal((2f, 3), (holder.floatValue, holder.intValue));
            }
            finally
            {
                Reset();
                Object.DestroyImmediate(holder);
            }
        }

        [Fact]
        public void OutsideOnGuiTogglesAndShadowLabelsThrowMonosNullReferenceWithoutAStyleWarning()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var previous = Event.current;
            Event.current = null;
            var logs = new System.Collections.Generic.List<string>();
            Application.LogCallback capture = (message, stack, type) => logs.Add(message);
            Application.logMessageReceived += capture;
            try
            {
                foreach (var call in new Action[] {
                             () => EditorGUI.Toggle(At, true, s_Toggle), () => EditorGUI.DropShadowLabel(At, "s"),
                             () => EditorGUI.DropShadowLabel(At, "s", s_Toggle) })
                    Assert.Equal("Object reference not set to an instance of an object", Assert.Throws<NullReferenceException>(call).Message);
                // outside/editorGUI, logs: no "Unable to find style" warning outside OnGUI.
                Assert.DoesNotContain(logs, l => l.Contains("Unable to find style"));
            }
            finally
            {
                Application.logMessageReceived -= capture;
                Event.current = previous;
            }
        }

        [Fact]
        public void AnUnmeasuredFamilysSkinStyledTogglesKeepTheirValuesAndSaySo()
        {
            using var release = TestRelease.Pin(TestRelease.Unmeasured);
            int rect = Hits("UnityEditor.EditorGUI.Toggle"), layout = Hits("UnityEditor.EditorGUILayout.Toggle");
            Assert.True(EditorGUI.Toggle(At, true));
            Assert.False(EditorGUI.Toggle(At, "x", false));
            Assert.True(EditorGUILayout.Toggle(true));
            Assert.False(EditorGUILayout.Toggle("x", false));
            Assert.Equal(rect + 2, Hits("UnityEditor.EditorGUI.Toggle"));
            Assert.Equal(layout + 2, Hits("UnityEditor.EditorGUILayout.Toggle"));
        }
    }
}
