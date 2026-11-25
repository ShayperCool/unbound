using System;
using System.Collections.Generic;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>EditorGUI's vector, rect and bounds fields as tests/projects/editormultifields measures
    /// them on the measured release, and the not-implemented notice of other families.</summary>
    public class EditorMultiFieldsTests
    {
        sealed class Capture : ILogHandler
        {
            internal readonly List<string> Lines = new List<string>();
            public void LogException(Exception exception, Object context) => Lines.Add(exception.Message);
            public void LogFormat(LogType logType, Object context, string format, params object[] args) => Lines.Add(string.Format(format, args));
        }

        static T Pass<T>(Event e, Func<T> draw, int instance = 11)
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

        static Event Down(Vector2 p) => new Event { type = EventType.MouseDown, mousePosition = p, button = 0, clickCount = 1 };
        static Event Up(Vector2 p) => new Event { type = EventType.MouseUp, mousePosition = p, button = 0, clickCount = 1 };
        static Event Char(char c) => new Event { type = EventType.KeyDown, character = c };

        static void Reset()
        {
            GUIUtility.keyboardControl = 0;
            GUIUtility.hotControl = 0;
            EditorGUI.s_RecycledEditor.EndEditing();
        }

        static T Edit<T>(Vector2 at, char digit, T value, Func<T, T> draw)
        {
            foreach (var e in new[] { Down(at), Up(at), Char(digit), Event.KeyboardEvent("return") })
                value = Pass(e, () => draw(value));
            return value;
        }

        [Fact]
        public void AClickInAColumnEditsThatComponent()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            Reset();
            var r = new Rect(10, 300, 300, 18);
            var v = Edit(new Vector2(160, 309), '5', new Vector3(1, 2, 3), x => EditorGUI.Vector3Field(r, "", x));
            Assert.Equal(new Vector3(1, 5, 3), v);
            v = Edit(new Vector2(300, 309), '6', v, x => EditorGUI.Vector3Field(r, "", x));
            Assert.Equal(new Vector3(1, 5, 6), v);
            Reset();
        }

        [Fact]
        public void ALabelledFieldInNarrowModeHasItsColumnsOnTheSecondLine()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            Reset();
            Assert.False(EditorGUIUtility.wideMode);
            var r = new Rect(10, 330, 300, 40);
            var v = Edit(new Vector2(290, 339), '9', new Vector2(4, 5), x => EditorGUI.Vector2Field(r, "Pos", x));
            Assert.Equal(new Vector2(4, 5), v);
            Reset();
            v = Edit(new Vector2(290, 359), '3', v, x => EditorGUI.Vector2Field(r, "Pos", x));
            Assert.Equal(new Vector2(4, 3), v);
            Reset();
        }

        [Fact]
        public void ARectsSecondRowIsWidthAndHeight()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            Reset();
            var r = new Rect(10, 380, 300, 40);
            var v = Edit(new Vector2(290, 409), '2', new Rect(1, 2, 3, 4), x => EditorGUI.RectField(r, x));
            Assert.Equal(new Rect(1, 2, 3, 2), v);
            Reset();
        }

        [Fact]
        public void OtherFamiliesReportTheMultiFieldsNotImplemented()
        {
            var capture = new Capture();
            var previous = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = capture;
            try
            {
                using var release = TestRelease.Pin(TestRelease.Unmeasured);
                var r = new Rect(0, 0, 300, 40);
                Assert.Equal(new Vector3(1, 2, 3), Pass(new Event { type = EventType.Repaint }, () => EditorGUI.Vector3Field(r, "L", new Vector3(1, 2, 3))));
                var b = new Bounds(Vector3.one, Vector3.one);
                Assert.Equal(b, Pass(new Event { type = EventType.Repaint }, () => EditorGUI.BoundsField(r, b)));
            }
            finally { Debug.unityLogger.logHandler = previous; }
            Assert.Contains(capture.Lines, line => line.Contains("EditorGUI.Vector3Field is not implemented"));
            Assert.Contains(capture.Lines, line => line.Contains("EditorGUI.BoundsField is not implemented"));
        }
    }
}
