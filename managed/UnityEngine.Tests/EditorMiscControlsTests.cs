using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    // Expected values: tests/projects/editorcontrolsb2 (Unity 6000.0.71f1 Game view OnGUI).
    [Collection("Serial")]
    public sealed class EditorMiscControlsTests : IDisposable
    {
        readonly IDisposable m_Release = TestRelease.Pin(TestRelease.Measured);

        public EditorMiscControlsTests() => Reset();

        public void Dispose()
        {
            Reset();
            m_Release.Dispose();
        }

        static void Reset()
        {
            GUIUtility.hotControl = 0;
            GUIUtility.keyboardControl = 0;
            typeof(EditorGUI).GetField("s_FoldoutHeaderGroupActive", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, 0);
        }

        static T Pass<T>(Event e, Func<T> draw)
        {
            var previous = Event.current;
            Event.current = e;
            GUIUtility.BeginGUI(0, 61, 0);
            try { return draw(); }
            finally
            {
                GUIUtility.EndGUI(0);
                Event.current = previous;
            }
        }

        static Event Mouse(EventType type, Vector2 position) => new Event { type = type, mousePosition = position, button = 0, clickCount = 1 };

        static readonly Rect Bar = new Rect(10, 300, 200, 18);
        static readonly Rect Header = new Rect(10, 360, 200, 22);

        [Fact]
        public void TheProgressBarUsesAMouseDownOnIt()
        {
            // script/progress.down.
            var e = Mouse(EventType.MouseDown, Bar.center);
            Pass(e, () => { EditorGUI.ProgressBar(Bar, 0.5f, "Half"); return 0; });
            Assert.Equal(EventType.Used, e.type);
        }

        [Fact]
        public void AFoldoutHeaderFoldsOnAClickAndTheArrowKeysAndCallsItsMenu()
        {
            bool fold = true;
            var calls = new List<Rect>();
            bool Draw()
            {
                fold = EditorGUI.BeginFoldoutHeaderGroup(Header, fold, "Header", null, r => calls.Add(r));
                EditorGUI.EndFoldoutHeaderGroup();
                return fold;
            }
            // script/header.*: a click folds it (on mouse up), right unfolds, left folds the focused one.
            Pass(Mouse(EventType.MouseDown, Header.center), Draw);
            Assert.False(Pass(Mouse(EventType.MouseUp, Header.center), Draw));
            Assert.True(Pass(Event.KeyboardEvent("right"), Draw));
            Assert.False(Pass(Event.KeyboardEvent("left"), Draw));
            // The menu icon: the callback with its rect, the event used.
            var e = Mouse(EventType.MouseDown, new Vector2(Header.xMax - 8, Header.y + 8));
            Pass(e, Draw);
            Assert.Single(calls);
            Assert.Equal(16, calls[0].width);
            Assert.Equal(EventType.Used, e.type);
        }

        static int ActiveHeaders() => (int)typeof(EditorGUI).GetField("s_FoldoutHeaderGroupActive", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);

        [Fact]
        public void OutsideOnGuiAFoldoutHeaderThrowsAfterCountingItself()
        {
            // outside: the Editor's NullReferenceException; its EndFoldoutHeaderGroup then answers ok
            // (the nested header's error box is measured by the probe, rect/*).
            var previous = Event.current;
            Event.current = null;
            try
            {
                var e = Assert.Throws<NullReferenceException>(() => EditorGUI.BeginFoldoutHeaderGroup(Bar, true, "Header"));
                Assert.Equal("Object reference not set to an instance of an object", e.Message);
                Assert.Equal(1, ActiveHeaders());
                EditorGUI.EndFoldoutHeaderGroup();
                Assert.Equal(0, ActiveHeaders());
            }
            finally { Event.current = previous; }
        }

        [Fact]
        public void DisabledGroupsNestAndAnExtraEndKeepsTheState()
        {
            // rect/*: [true, false, false, false, true, true].
            bool before = GUI.enabled;
            try
            {
                var states = new List<bool> { GUI.enabled };
                EditorGUI.BeginDisabled(true);
                states.Add(GUI.enabled);
                EditorGUI.BeginDisabled(false);
                states.Add(GUI.enabled);
                EditorGUI.EndDisabled();
                states.Add(GUI.enabled);
                EditorGUI.EndDisabled();
                states.Add(GUI.enabled);
                EditorGUI.EndDisabled();
                states.Add(GUI.enabled);
                Assert.Equal(new[] { true, false, false, false, true, true }, states);
            }
            finally { GUI.enabled = before; }
        }

        [Fact]
        public void AMissingStyleStretchesVertically()
        {
            // layout/repaint/rects: a layout rect of a missing style fills its area.
            var skin = ScriptableObject.CreateInstance<GUISkin>();
            try
            {
                var style = skin.GetStyle("No Such Style");
                Assert.True(style.stretchHeight);
                Assert.Equal(Color.red, style.normal.textColor);
            }
            finally { Object.DestroyImmediate(skin); }
        }

        [Fact]
        public void ACommandEventIsAnExecuteCommandOfItsName()
        {
            // commandEvent.
            var e = EditorGUIUtility.CommandEvent("Copy");
            Assert.Equal(EventType.ExecuteCommand, e.type);
            Assert.Equal("Copy", e.commandName);
            Assert.Equal(EventModifiers.None, e.modifiers);
            // picker: no open picker.
            Assert.Null(EditorGUIUtility.GetObjectPickerObject());
            Assert.Equal(0, EditorGUIUtility.GetObjectPickerControlID());
        }

        sealed class Capture : ILogHandler
        {
            internal readonly List<string> Lines = new List<string>();
            public void LogException(Exception exception, Object context) => Lines.Add(exception.Message);
            public void LogFormat(LogType logType, Object context, string format, params object[] args) => Lines.Add(string.Format(format, args));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ALinkButtonRepaintReportsItsUnderlineOnce(bool contentOverload)
        {
            const string lineKey = "Handles.DrawLine";
            const string underlineKey = "UnityEditor.EditorGUILayout.LinkButton underline";
            var capture = new Capture();
            var previous = Debug.unityLogger.logHandler;
            var previousColor = Handles.color;
            Unbound.Runtime.Stubs.Forget(lineKey);
            Unbound.Runtime.Stubs.Forget(underlineKey);
            Debug.unityLogger.logHandler = capture;
            try
            {
                Handles.color = Color.red;
                Pass(new Event { type = EventType.Layout }, DrawLink);
                Assert.Empty(capture.Lines.FindAll(IsUnderlineDiagnostic));
                Handles.color = Color.red;
                Pass(new Event { type = EventType.Repaint }, DrawLink);
                Assert.Equal(Color.white, Handles.color);
                Assert.Equal("Unbound: " + underlineKey + " the link's underline is not drawn (Unbound draws no handles)",
                    Assert.Single(capture.Lines.FindAll(IsUnderlineDiagnostic)));
            }
            finally
            {
                Handles.color = previousColor;
                Debug.unityLogger.logHandler = previous;
                Unbound.Runtime.Stubs.Forget(lineKey);
                Unbound.Runtime.Stubs.Forget(underlineKey);
            }

            bool DrawLink() => contentOverload ? EditorGUILayout.LinkButton(new GUIContent("Link")) : EditorGUILayout.LinkButton("Link");
            static bool IsUnderlineDiagnostic(string line) => line.Contains(lineKey) || line.Contains(underlineKey);
        }

        [Fact]
        public void OtherFamiliesReportTheControlsNotImplemented()
        {
            // Each notice is said once per process: forget earlier ones.
            foreach (var member in s_Gated) Unbound.Runtime.Stubs.Forget(member);
            var capture = new Capture();
            var previous = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = capture;
            try
            {
                using var release = TestRelease.Pin(TestRelease.Unmeasured);
                var e = Mouse(EventType.MouseDown, Bar.center);
                Pass(e, () => { EditorGUI.ProgressBar(Bar, 0.5f, "Half"); return 0; });
                Assert.Equal(EventType.MouseDown, e.type);
                Assert.False(Pass(Mouse(EventType.MouseUp, Header.center), () => EditorGUI.BeginFoldoutHeaderGroup(Header, false, "Header")));
                Assert.Equal(0, ActiveHeaders());
                Assert.Equal(7, Pass(new Event { type = EventType.Repaint }, () => EditorGUI.LogarithmicIntSlider(Bar, GUIContent.none, 7, 1, 100, 10, 1, 100)));
                var data = ScriptableObject.CreateInstance<EditorControlsTestData>();
                try
                {
                    var property = new SerializedObject(data).FindProperty("number");
                    Pass(new Event { type = EventType.Repaint }, () => { EditorGUI.DelayedIntField(Bar, property, GUIContent.none); return 0; });
                }
                finally { Object.DestroyImmediate(data); }
                Assert.False(Pass(new Event { type = EventType.Layout }, () => EditorGUILayout.LinkButton("Link")));
                Assert.False(Pass(new Event { type = EventType.Layout }, () => EditorGUILayout.DropdownButton(new GUIContent("Drop"), FocusType.Keyboard)));
                Pass(new Event { type = EventType.Layout }, () => { EditorGUILayout.EndFoldoutHeaderGroup(); return 0; });
                Assert.Equal(0, ActiveHeaders());
            }
            finally { Debug.unityLogger.logHandler = previous; }
            foreach (var member in s_Gated)
                Assert.Contains(capture.Lines, line => line.Contains(member + " is not implemented"));
        }

        static readonly string[] s_Gated =
        {
            "UnityEditor.EditorGUI.ProgressBar",
            "UnityEditor.EditorGUI.BeginFoldoutHeaderGroup",
            "UnityEditor.EditorGUI.LogarithmicIntSlider",
            "UnityEditor.EditorGUI.DelayedIntField",
            "UnityEditor.EditorGUILayout.LinkButton",
            "UnityEditor.EditorGUILayout.DropdownButton",
            "UnityEditor.EditorGUILayout.EndFoldoutHeaderGroup",
        };
    }
}

namespace UnityEngine.Tests
{
    public sealed class EditorControlsTestData : ScriptableObject
    {
        public int number = 7;
    }
}
