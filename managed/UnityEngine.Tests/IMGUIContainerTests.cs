using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine.UIElements;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>An IMGUIContainer in a runtime panel (tests/projects/imguicontainer records the
    /// Editor's): the Layout passes that measure it, the events its handler gets, the panel's focus
    /// following its controls, and a container run from inside another GUI pass.</summary>
    public class IMGUIContainerTests
    {
        sealed class Logs : ILogHandler, IDisposable
        {
            internal readonly List<string> Entries = new List<string>();
            readonly ILogHandler m_Previous = Debug.unityLogger.logHandler;

            internal Logs() => Debug.unityLogger.logHandler = this;

            public void LogFormat(LogType logType, Object context, string format, params object[] args) =>
                Entries.Add(logType + ":" + (args != null && args.Length > 0 ? string.Format(format, args) : format));

            public void LogException(Exception exception, Object context) => Entries.Add("Exception:" + exception.Message);

            public void Dispose() => Debug.unityLogger.logHandler = m_Previous;
        }

        sealed class Fixture : IDisposable
        {
            internal readonly PanelSettings Settings = ScriptableObject.CreateInstance<PanelSettings>();
            internal readonly BaseRuntimePanel Panel;
            internal readonly List<string> Events = new List<string>();
            readonly IDisposable m_Release = TestRelease.Pin(TestRelease.Measured);

            internal Fixture()
            {
                Panel = RuntimePanel.Create(Settings);
                GUIUtility.hotControl = 0;
                GUIUtility.keyboardControl = 0;
            }

            internal void Tick()
            {
                Panel.Update();
                new Unbound.UIElements.UaPainter().Paint(Panel, 100f);
            }

            internal string Take()
            {
                var s = string.Join(",", Events);
                Events.Clear();
                return s;
            }

            internal IMGUIContainer Add(Action gui)
            {
                var container = new IMGUIContainer(() =>
                {
                    Events.Add(Event.current.type.ToString());
                    gui();
                });
                // The test panel has no size of its own.
                container.style.width = 300f;
                Panel.visualTree.Add(container);
                return container;
            }

            public void Dispose()
            {
                GUIUtility.hotControl = 0;
                GUIUtility.keyboardControl = 0;
                Panel.Dispose();
                Object.DestroyImmediate(Settings);
                m_Release.Dispose();
            }
        }

        static void Send<T>(IMGUIContainer target, T evt, out bool stopped) where T : EventBase<T>, new()
        {
            using (evt)
            {
                evt.target = target;
                target.SendEvent(evt);
                stopped = evt.isPropagationStopped;
            }
        }

        [Fact]
        public void ALayoutPassMeasuresItAndOneMoreFollowsOnlyBeforeItsFirstPaint()
        {
            using var f = new Fixture();
            int lines = 2;
            var container = f.Add(() =>
            {
                for (int i = 0; i < lines; i++) GUILayout.Label("Line " + i, GUILayout.Height(20f));
            });
            Assert.Equal("", f.Take());
            f.Tick();
            Assert.Equal("Layout", f.Take());
            float height = container.layout.height;
            Assert.True(height > 0f);
            // The first measure changed its size before it was ever painted: one more layout.
            f.Tick();
            Assert.Equal("Layout", f.Take());
            f.Tick();
            f.Tick();
            Assert.Equal("", f.Take());

            // More content does not measure it again until it is marked dirty; then one pass.
            lines = 4;
            f.Tick();
            Assert.Equal("", f.Take());
            Assert.Equal(height, container.layout.height);
            container.MarkDirtyLayout();
            f.Tick();
            f.Tick();
            f.Tick();
            Assert.Equal("Layout", f.Take());
            Assert.True(container.layout.height > height);

            // Removed and added again: painted before, so one pass.
            container.RemoveFromHierarchy();
            f.Tick();
            f.Panel.visualTree.Add(container);
            f.Tick();
            f.Tick();
            Assert.Equal("Layout", f.Take());
        }

        [Fact]
        public void AFixedSizeNeedsNoMeasureAndNoHandlerMeasuresNothing()
        {
            using var f = new Fixture();
            var sized = f.Add(() => GUILayout.Label("Fixed", GUILayout.Height(20f)));
            sized.style.width = 200f;
            sized.style.height = 50f;
            var empty = new IMGUIContainer();
            f.Panel.visualTree.Add(empty);
            f.Tick();
            f.Tick();
            Assert.Equal("", f.Take());
            Assert.Equal(new Vector2(200f, 50f), sized.layout.size);
            Assert.Equal(0f, empty.layout.height);
        }

        [Fact]
        public void AMouseEventReachesTheHandlerInItsCoordinatesAfterALayoutPass()
        {
            using var f = new Fixture();
            var spacer = new VisualElement();
            spacer.style.height = 30f;
            f.Panel.visualTree.Add(spacer);
            Vector2 seen = default;
            bool clicked = false;
            var container = f.Add(() =>
            {
                if (Event.current.type == EventType.MouseDown) seen = Event.current.mousePosition;
                clicked |= GUILayout.Button("Target", GUILayout.Height(20f));
            });
            f.Tick();
            f.Tick();
            f.Take();
            var center = container.worldBound.center;
            Assert.True(center.y > 30f);

            Send(container, MouseDownEvent.GetPooled(new Event { type = EventType.MouseDown, mousePosition = center, button = 0, clickCount = 1 }), out bool stopped);
            Assert.Equal("Layout,mouseDown", f.Take());
            Assert.Equal(container.WorldToLocal(center), seen);
            Assert.True(stopped);
            Assert.NotEqual(0, GUIUtility.hotControl);
            Send(container, MouseUpEvent.GetPooled(new Event { type = EventType.MouseUp, mousePosition = center, button = 0, clickCount = 1 }), out stopped);
            Assert.Equal("Layout,mouseUp", f.Take());
            Assert.True(clicked);
            Assert.Equal(0, GUIUtility.hotControl);

            // A press outside the container does not reach it; a key does, unused; an event
            // without an IMGUI event does not.
            Send(container, MouseDownEvent.GetPooled(new Event { type = EventType.MouseDown, mousePosition = new Vector2(center.x, 5f) }), out stopped);
            Assert.Equal("", f.Take());
            Assert.False(stopped);
            Send(container, KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.A }), out stopped);
            Assert.Equal("Layout,KeyDown", f.Take());
            Assert.False(stopped);
            Send(container, MouseDownEvent.GetPooled(center, 0, 1, Vector2.zero), out stopped);
            Assert.Equal("", f.Take());
            Assert.False(stopped);
        }

        [Fact]
        public void ThePanelsFocusFollowsTheControlThatTakesTheKeyboard()
        {
            using var f = new Fixture();
            string text = "abc";
            var container = f.Add(() =>
            {
                GUILayout.Label("Plain", GUILayout.Height(20f));
                text = GUILayout.TextField(text, GUILayout.Height(20f));
            });
            var other = new Button { focusable = true };
            other.style.width = 100f;
            other.style.height = 20f;
            f.Panel.visualTree.Add(other);
            f.Tick();
            f.Tick();
            // Nothing can take the keyboard before the first event shows its controls.
            Assert.True(container.focusable);
            Assert.False(container.canGrabFocus);

            // A press on the label: no control takes the keyboard, so nothing is focused.
            other.Focus();
            Assert.Same(other, f.Panel.focusController.focusedElement);
            var label = new Vector2(container.worldBound.center.x, container.worldBound.y + 5f);
            Send(container, MouseDownEvent.GetPooled(new Event { type = EventType.MouseDown, mousePosition = label, clickCount = 1 }), out _);
            Assert.Equal(0, GUIUtility.keyboardControl);
            Assert.Null(f.Panel.focusController.focusedElement);
            Assert.True(container.canGrabFocus);

            // A press on the text field: it takes the keyboard and the container the focus.
            var field = new Vector2(container.worldBound.center.x, container.worldBound.yMax - 8f);
            Send(container, MouseDownEvent.GetPooled(new Event { type = EventType.MouseDown, mousePosition = field, clickCount = 1 }), out _);
            int keyboard = GUIUtility.keyboardControl;
            Assert.NotEqual(0, keyboard);
            Assert.Same(container, f.Panel.focusController.focusedElement);
            Assert.Equal(keyboard, f.Panel.focusController.imguiKeyboardControl);
            Send(container, MouseUpEvent.GetPooled(new Event { type = EventType.MouseUp, mousePosition = field, clickCount = 1 }), out _);

            // Focus leaves: the control keeps the keyboard until the handler's next event.
            other.Focus();
            Assert.Same(other, f.Panel.focusController.focusedElement);
            Assert.Equal(keyboard, GUIUtility.keyboardControl);
            Send(container, KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.A }), out _);
            Assert.Equal(0, GUIUtility.keyboardControl);
            Assert.Equal("abc", text);
        }

        [Fact]
        public void AContainerRunFromAnotherGUIPassLeavesThatPassAsItWas()
        {
            using var logs = new Logs();
            using var f = new Fixture();
            var inner = f.Add(() => GUILayout.Label("Inner", GUILayout.Height(20f)));
            bool nested = false;
            Rect second = default;
            var outer = f.Add(() =>
            {
                var current = Event.current;
                var origin = GUIUtility.GUIToScreenPoint(Vector2.zero);
                GUI.BeginGroup(new Rect(5f, 5f, 500f, 500f));
                GUILayout.BeginVertical();
                GUILayout.Label("First", GUILayout.Height(20f));
                if (nested) inner.HandleIMGUIEvent(new Event { type = EventType.KeyDown, keyCode = KeyCode.A }, true);
                Assert.Same(current, Event.current);
                Assert.Equal(origin + new Vector2(5f, 5f), GUIUtility.GUIToScreenPoint(Vector2.zero));
                GUILayout.Label("Second", GUILayout.Height(20f));
                if (current.type != EventType.Layout) second = GUILayoutUtility.GetLastRect();
                GUILayout.EndVertical();
                GUI.EndGroup();
            });
            f.Tick();
            f.Tick();
            Send(outer, KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.B }), out _);
            float plain = second.y;
            Assert.True(plain > 0f);
            f.Take();

            nested = true;
            second = default;
            Send(outer, KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.B }), out _);
            // The outer pass's layout, clips and event survived the inner container's passes.
            Assert.Equal("Layout,Layout,KeyDown,KeyDown,Layout,KeyDown", f.Take());
            Assert.Equal(plain, second.y);
            Assert.DoesNotContain(logs.Entries, e => e.Contains("GUI Error") || e.StartsWith("Exception"));
        }

        [Fact]
        public void AnExceptionInALayoutPassIsLoggedAndUnpoppedClipsAreReported()
        {
            using var logs = new Logs();
            using var f = new Fixture();
            f.Add(() => throw new InvalidOperationException("handler"));
            f.Add(() => GUI.BeginGroup(new Rect(0f, 0f, 10f, 10f)));
            f.Tick();
            Assert.Contains("Exception:handler", logs.Entries);
            Assert.Contains("Error:GUI Error: You are pushing more GUIClips than you are popping. Make sure they are balanced.", logs.Entries);
            // The state after them is a clean one.
            Assert.Equal(1, GUIClip.Depth);
        }

        public class Tagged : PropertyAttribute { }

        public class Nested : ScriptableObject
        {
            [Tagged] public float tagged = 0.5f;
            public float plain = 1f;
        }

        // A drawer that measures and draws its own property again.
        public class TaggedDrawer : PropertyDrawer
        {
            internal static float own;
            internal static Rect position;

            public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
            {
                own = EditorGUI.GetPropertyHeight(property);
                return own + 3f;
            }

            public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
            {
                TaggedDrawer.position = position;
                EditorGUI.PropertyField(position, property, label);
            }
        }

        [Fact]
        public void ADrawerMeasuringAndDrawingItsOwnPropertyGetsTheNextDrawerOfIt()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var target = ScriptableObject.CreateInstance<Nested>();
            using var so = new SerializedObject(target);
            try
            {
                using var registered = new PropertyFieldDrawerTests.DrawerTable((typeof(Tagged), typeof(TaggedDrawer)));
                // Outside any drawer the property's height is its drawer's, which measured the
                // plain field under it: 18 + 3.
                Assert.Equal(21f, EditorGUI.GetPropertyHeight(so.FindProperty("tagged")));
                Assert.Equal(18f, TaggedDrawer.own);
                Assert.Equal(18f, EditorGUI.GetPropertyHeight(so.FindProperty("plain")));
            }
            finally
            {
                Object.DestroyImmediate(target);
            }
        }
    }
}
