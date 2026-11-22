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
        internal sealed class Logs : ILogHandler, IDisposable
        {
            internal readonly List<string> Entries = new List<string>();
            readonly ILogHandler m_Previous = Debug.unityLogger.logHandler;

            internal Logs() => Debug.unityLogger.logHandler = this;

            public void LogFormat(LogType logType, Object context, string format, params object[] args) =>
                Entries.Add(logType + ":" + (args != null && args.Length > 0 ? string.Format(format, args) : format));

            public void LogException(Exception exception, Object context) => Entries.Add("Exception:" + exception.Message);

            public void Dispose() => Debug.unityLogger.logHandler = m_Previous;
        }

        internal sealed class Fixture : IDisposable
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
                Unbound.Runtime.GuiEvents.MousePosition = Vector2.zero;
                // Other tests' Editor GUI passes leave the Editor's styles loaded.
                EditorStyles.Loaded = false;
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
                Unbound.Runtime.GuiEvents.MousePosition = Vector2.zero;
                EditorStyles.Loaded = false;
                Panel.Dispose();
                Object.DestroyImmediate(Settings);
                m_Release.Dispose();
            }
        }

        internal static void Send<T>(IMGUIContainer target, T evt, out bool stopped) where T : EventBase<T>, new()
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
        public void InsideFoldoutsItHasTheClassOfItsDepth()
        {
            using var f = new Fixture();
            var container = f.Add(() => { });
            Assert.DoesNotContain(container.GetClasses(), c => c.Contains("--depth-"));
            var outer = new Foldout();
            var inner = new Foldout();
            f.Panel.visualTree.Add(outer);
            outer.Add(inner);
            inner.Add(container);
            Assert.Contains("unity-foldout__unity-imgui-container--depth-2", container.GetClasses());
            VisualElement deepest = inner;
            for (int i = 0; i < 4; i++)
            {
                var next = new Foldout();
                deepest.Add(next);
                deepest = next;
            }
            deepest.Add(container);
            Assert.Contains("unity-foldout__unity-imgui-container--depth-max", container.GetClasses());
            Assert.DoesNotContain("unity-foldout__unity-imgui-container--depth-2", container.GetClasses());
            f.Panel.visualTree.Add(container);
            Assert.DoesNotContain(container.GetClasses(), c => c.Contains("--depth-"));
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
        public void AMouseEventReachesTheHandlerAfterALayoutPassWithTheMouseWhereTheGUISawIt()
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

            // The GUI has seen no mouse (it is at the corner): the handler gets the event, with the
            // mouse there and not where the sent event says, so the button does not take it.
            Send(container, MouseDownEvent.GetPooled(new Event { type = EventType.MouseDown, mousePosition = center, button = 0, clickCount = 1 }), out bool stopped);
            Assert.Equal("Layout,mouseDown", f.Take());
            Assert.Equal(container.WorldToLocal(Vector2.zero), seen);
            Assert.False(stopped);
            Assert.Equal(0, GUIUtility.hotControl);
            Send(container, MouseUpEvent.GetPooled(new Event { type = EventType.MouseUp, mousePosition = center, button = 0, clickCount = 1 }), out stopped);
            Assert.Equal("Layout,mouseUp", f.Take());
            Assert.False(clicked);

            // With the mouse over the button the press is the button's.
            Unbound.Runtime.GuiEvents.MousePosition = center;
            Send(container, MouseDownEvent.GetPooled(new Event { type = EventType.MouseDown, mousePosition = center, button = 0, clickCount = 1 }), out stopped);
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
            Unbound.Runtime.GuiEvents.MousePosition = label;
            Send(container, MouseDownEvent.GetPooled(new Event { type = EventType.MouseDown, mousePosition = label, clickCount = 1 }), out _);
            Assert.Equal(0, GUIUtility.keyboardControl);
            Assert.Null(f.Panel.focusController.focusedElement);
            Assert.True(container.canGrabFocus);

            // A press on the text field: it takes the keyboard and the container the focus.
            var field = new Vector2(container.worldBound.center.x, container.worldBound.yMax - 8f);
            Unbound.Runtime.GuiEvents.MousePosition = field;
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

        // A pass starts with the skin of its container's context: the Editor's for the Editor
        // context, the game's for the Player context (tests/projects/imguicontainer auto.* measures
        // the Editor context's). Without an installation the Editor's skin is an empty one, still
        // its own object.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void AnEditorContainerRunFromAGamePassHasTheEditorsSkinAndGivesTheGamesBack(bool userSkin)
        {
            using var logs = new Logs();
            using var f = new Fixture();
            var editorSkin = Unbound.Runtime.EditorGuiSkins.Current;
            var gameSkin = GUIUtility.GetDefaultSkin(0);
            Assert.NotSame(gameSkin, editorSkin);
            var own = userSkin ? ScriptableObject.CreateInstance<GUISkin>() : null;
            GUISkin inside = null, insideMode0 = null, before = null, after = null;
            int modeInside = -1, modeAfter = -1;
            var inner = f.Add(() =>
            {
                inside = GUI.skin;
                modeInside = GUIUtility.s_SkinMode;
                insideMode0 = GUIUtility.GetDefaultSkin(0);
            });
            bool nested = false;
            var outer = f.Add(() =>
            {
                if (own != null) GUI.skin = own;
                before = GUI.skin;
                if (nested) inner.HandleIMGUIEvent(new Event { type = EventType.KeyDown, keyCode = KeyCode.A }, true);
                after = GUI.skin;
                modeAfter = GUIUtility.s_SkinMode;
            });
            outer.contextType = ContextType.Player;
            f.Tick();
            nested = true;
            Send(outer, KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.B }), out _);

            Assert.Same(editorSkin, inside);
            Assert.Equal(1, modeInside);
            Assert.Same(gameSkin, insideMode0);
            Assert.Same(own ?? gameSkin, before);
            Assert.Same(own ?? gameSkin, after);
            Assert.Equal(0, modeAfter);
            // After the game's pass its skin is still the one it had: the default, or its own.
            Assert.Equal(0, GUIUtility.s_SkinMode);
            Assert.Same(own ?? gameSkin, GUI.skin);
            GUI.skin = null;
            if (own != null) Object.DestroyImmediate(own);
        }

        [Fact]
        public void AnEditorContainerThatThrowsInsideAGamePassGivesTheGamesSkinBack()
        {
            using var logs = new Logs();
            using var f = new Fixture();
            var gameSkin = GUIUtility.GetDefaultSkin(0);
            var inner = f.Add(() =>
            {
                if (Event.current.type != EventType.Layout) throw new InvalidOperationException("inner");
            });
            GUISkin after = null;
            int modeAfter = -1;
            bool nested = false, thrown = false;
            var outer = f.Add(() =>
            {
                if (!nested) return;
                try
                {
                    inner.HandleIMGUIEvent(new Event { type = EventType.KeyDown, keyCode = KeyCode.A }, true);
                }
                catch (InvalidOperationException)
                {
                    thrown = true;
                }
                after = GUI.skin;
                modeAfter = GUIUtility.s_SkinMode;
            });
            outer.contextType = ContextType.Player;
            f.Tick();
            nested = true;
            Send(outer, KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.B }), out _);
            Assert.True(thrown);
            Assert.Same(gameSkin, after);
            Assert.Equal(0, modeAfter);
        }

        [Fact]
        public void AfterAnEditorContainersOwnPassTheGamesSkinIsTheDefaultAgain()
        {
            using var logs = new Logs();
            using var f = new Fixture();
            GUISkin inside = null;
            var container = f.Add(() => inside = GUI.skin);
            Assert.Equal(ContextType.Editor, container.contextType);
            f.Tick();
            Assert.Same(Unbound.Runtime.EditorGuiSkins.Current, inside);
            Assert.Equal(0, GUIUtility.s_SkinMode);
            Assert.Same(GUIUtility.GetDefaultSkin(0), GUI.skin);

            // A container of the Player context has the game's skin in its pass.
            inside = null;
            container.contextType = ContextType.Player;
            container.MarkDirtyLayout();
            f.Tick();
            Assert.Same(GUIUtility.GetDefaultSkin(0), inside);
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

        public class UiOnly : PropertyAttribute { }

        // A drawer with UI Toolkit GUI only.
        public class UiOnlyDrawer : PropertyDrawer
        {
            public override VisualElement CreatePropertyGUI(SerializedProperty property) => new Label("ui");
        }

        public class BuiltIn : ScriptableObject
        {
            [Header("Stats")] public int headed = 1;
            [Header("Two\nLines")] public int headedTwice = 1;
            [TextArea] public string note = "a";
            [TextArea(2, 5)] public string longNote = "one\ntwo\nthree\nfour";
            [TextArea(2, 5)] public string shortNote = "";
            [Space(12f)] public bool flag;
            public bool plainFlag;
            public int[] numbers = { 1, 2 };
            public Vector2 point;
            [Range(0f, 1f)] public float ratio = 0.5f;
            [Multiline(3)] public string multi = "m";
            [UiOnly] public float uiOnly = 1f;
        }

        // The measured heights are tests/projects/imguicontainer builtin.*.
        [Fact]
        public void TheDefaultInspectorsIMGUIDrawsTheEditorsAttributesAndTypesWithoutFailing()
        {
            using var logs = new Logs();
            using var f = new Fixture();
            using var registered = new PropertyFieldDrawerTests.DrawerTable((typeof(UiOnly), typeof(UiOnlyDrawer)));
            var target = ScriptableObject.CreateInstance<BuiltIn>();
            using var so = new SerializedObject(target);
            var editor = Editor.CreateEditor(target);
            var heights = new Dictionary<string, float>();
            var rects = new Dictionary<string, float>();
            try
            {
                // Before any Editor GUI pass a header's height is not there.
                Assert.Throws<NullReferenceException>(() => EditorGUI.GetPropertyHeight(so.FindProperty("headed")));
                Assert.Throws<NullReferenceException>(() => EditorGUI.GetPropertyHeight(so.FindProperty("note")));
                var drawn = f.Add(() => editor.DrawDefaultInspector());
                var measured = f.Add(() =>
                {
                    foreach (var name in new[] { "headed", "note", "longNote", "shortNote", "flag", "plainFlag", "numbers", "ratio", "multi" })
                    {
                        var property = so.FindProperty(name);
                        heights[name] = EditorGUI.GetPropertyHeight(property);
                        EditorGUILayout.PropertyField(property, true);
                        if (Event.current.type == EventType.KeyDown) rects[name] = GUILayoutUtility.GetLastRect().height;
                    }
                });
                f.Tick();
                f.Tick();
                Send(drawn, KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.A }), out _);
                Send(measured, KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.A }), out _);
                Assert.DoesNotContain(logs.Entries, e => e.StartsWith("Exception") || e.Contains("GUI Error"));
                Assert.True(drawn.layout.height > 200f);
                Assert.Equal(45f, heights["headed"]);
                Assert.Equal(66f, heights["note"]);
                Assert.Equal(51f, heights["shortNote"]);
                Assert.Equal(30f, heights["flag"]);
                Assert.Equal(18f, heights["plainFlag"]);
                Assert.Equal(18f, heights["numbers"]);
                Assert.Equal(18f, heights["ratio"]);
                Assert.Equal(62f, heights["multi"]);
                // Each takes a rect of its height; a checkbox with a decorator too.
                foreach (var pair in heights) Assert.Equal(pair.Value, rects[pair.Key]);
                // After an Editor GUI pass the heights are there outside one as well.
                Assert.Equal(45f, EditorGUI.GetPropertyHeight(so.FindProperty("headed")));
                Assert.Equal(66f, EditorGUI.GetPropertyHeight(so.FindProperty("note")));
            }
            finally
            {
                Object.DestroyImmediate(editor);
                Object.DestroyImmediate(target);
            }
        }

        public class Nested : ScriptableObject
        {
            [Tagged] public float tagged = 0.5f;
            public float plain = 1f;
            [Space(20f)] [Tagged] public float spaced = 2f;
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
        public void DecoratorsTakeTheTopOfTheRectUntilAPropertyFieldShowsThemItself()
        {
            using var f = new Fixture();
            var target = ScriptableObject.CreateInstance<Nested>();
            using var so = new SerializedObject(target);
            using var registered = new PropertyFieldDrawerTests.DrawerTable((typeof(Tagged), typeof(TaggedDrawer)));
            try
            {
                // The space, then the drawer's 18 + 3.
                Assert.Equal(41f, EditorGUI.GetPropertyHeight(so.FindProperty("spaced")));
                var drawn = f.Add(() => EditorGUI.PropertyField(new Rect(3f, 7f, 100f, 41f), so.FindProperty("spaced")));
                f.Tick();
                Assert.Equal(new Rect(3f, 27f, 100f, 21f), TaggedDrawer.position);
                drawn.RemoveFromHierarchy();

                // A PropertyField shows the space itself and its IMGUI leaves it out, also in
                // every later IMGUI call for the property.
                var field = new UnityEditor.UIElements.PropertyField(so.FindProperty("spaced"));
                field.style.width = 300f;
                f.Panel.visualTree.Add(field);
                UnityEditor.UIElements.BindingExtensions.Bind(field, so);
                f.Tick();
                f.Tick();
                Assert.Equal(2, field.hierarchy.childCount);
                Assert.Equal(20f, field.hierarchy[0].layout.height);
                Assert.Equal(21f, TaggedDrawer.position.height);
                Assert.Equal(21f, EditorGUI.GetPropertyHeight(so.FindProperty("spaced")));
            }
            finally
            {
                Object.DestroyImmediate(target);
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
