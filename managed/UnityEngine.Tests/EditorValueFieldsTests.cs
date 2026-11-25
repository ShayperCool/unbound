using System;
using System.Collections.Generic;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>EditorGUI's number, text and delayed fields and sliders as tests/projects/editorvaluefields
    /// measures them on the measured release, and the not-implemented notice of other families.</summary>
    public class EditorValueFieldsTests
    {
        sealed class Capture : ILogHandler
        {
            internal readonly List<string> Lines = new List<string>();
            public void LogException(Exception exception, Object context) => Lines.Add(exception.Message);
            public void LogFormat(LogType logType, Object context, string format, params object[] args) => Lines.Add(string.Format(format, args));
        }

        static T Pass<T>(Event e, Func<T> draw, int instance = 1)
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

        static Event Down(Rect r) => new Event { type = EventType.MouseDown, mousePosition = r.center, button = 0, clickCount = 1 };
        static Event Up(Rect r) => new Event { type = EventType.MouseUp, mousePosition = r.center, button = 0, clickCount = 1 };
        static Event Char(char c) => new Event { type = EventType.KeyDown, character = c };

        static void Reset()
        {
            GUIUtility.keyboardControl = 0;
            GUIUtility.hotControl = 0;
            EditorGUI.s_RecycledEditor.EndEditing();
            EditorGUI.s_DelayedTextEditor.EndEditing();
        }

        [Fact]
        public void FieldsReturnTheirValueAndTakeTheirIdsInLayoutAndRepaint()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            Reset();
            var r = new Rect(10, 10, 300, 18);
            foreach (var type in new[] { EventType.Layout, EventType.Repaint })
            {
                var (value, ids) = Pass(new Event { type = type }, () =>
                {
                    int before = GUIUtility.GetControlID(FocusType.Passive);
                    float v = EditorGUI.FloatField(r, "Label", 1.25f);
                    return (v, GUIUtility.GetControlID(FocusType.Passive) - before - 1);
                }, 2);
                Assert.Equal(1.25f, value);
                Assert.Equal(1, ids);
                var (slider, sliderIds) = Pass(new Event { type = type }, () =>
                {
                    int before = GUIUtility.GetControlID(FocusType.Passive);
                    float v = EditorGUI.Slider(r, 3f, 0f, 1f);
                    return (v, GUIUtility.GetControlID(FocusType.Passive) - before - 1);
                }, 3);
                Assert.Equal(3f, slider);
                Assert.Equal(2, sliderIds);
            }
        }

        [Fact]
        public void ANumberFieldParsesWhileTypedAndEvaluatesExpressions()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            Reset();
            var r = new Rect(10, 300, 100, 18);
            float value = 1.5f;
            foreach (var e in new[] { Down(r), Up(r), Char('2'), Char('*') })
                value = Pass(e, () => EditorGUI.FloatField(r, value));
            Assert.Equal(2f, value);
            Assert.True(EditorGUIUtility.editingTextField);
            value = Pass(Char('3'), () => EditorGUI.FloatField(r, value));
            Assert.Equal(6f, value);
            Pass(Event.KeyboardEvent("return"), () => EditorGUI.FloatField(r, value));
            Assert.False(EditorGUIUtility.editingTextField);
            Reset();
        }

        [Fact]
        public void EscapeRestoresAnIntFieldsValue()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            Reset();
            var r = new Rect(10, 320, 100, 18);
            int value = 7;
            foreach (var e in new[] { Down(r), Up(r), Char('9') })
                value = Pass(e, () => EditorGUI.IntField(r, value));
            Assert.Equal(9, value);
            var escape = Event.KeyboardEvent("escape");
            value = Pass(escape, () => EditorGUI.IntField(r, value));
            Assert.Equal(7, value);
            Assert.Equal(EventType.Used, escape.type);
            Reset();
        }

        [Fact]
        public void ADelayedFieldReturnsItsTextOnlyOnReturn()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            Reset();
            var r = new Rect(10, 340, 100, 18);
            float value = 2f;
            foreach (var e in new[] { Down(r), Up(r), Char('4'), Char('.'), Char('5') })
                value = Pass(e, () => EditorGUI.DelayedFloatField(r, value));
            Assert.Equal(2f, value);
            value = Pass(Event.KeyboardEvent("return"), () => EditorGUI.DelayedFloatField(r, value));
            Assert.Equal(4.5f, value);
            Reset();
        }

        [Fact]
        public void OtherFamiliesReportTheFieldsNotImplemented()
        {
            var capture = new Capture();
            var previous = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = capture;
            try
            {
                using var release = TestRelease.Pin(TestRelease.Unmeasured);
                var r = new Rect(0, 0, 100, 18);
                Assert.Equal(1.5f, Pass(new Event { type = EventType.Repaint }, () => EditorGUI.FloatField(r, 1.5f)));
                Assert.Equal(4, Pass(new Event { type = EventType.Repaint }, () => EditorGUI.IntSlider(r, 4, 0, 10)));
            }
            finally { Debug.unityLogger.logHandler = previous; }
            Assert.Contains(capture.Lines, line => line.Contains("EditorGUI.FloatField is not implemented"));
            Assert.Contains(capture.Lines, line => line.Contains("EditorGUI.IntSlider is not implemented"));
        }
    }
}
