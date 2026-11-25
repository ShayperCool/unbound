using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Xunit;

namespace UnityEngine.Tests
{
    // Expected values: the windowed Unity 6000.0.71f1 Editor before any GUI has run
    // (tests/projects/editorui EditorStyles.*, EditorGUI.PrefixLabel, Handles.*;
    // tests/projects/editorobjectfields outside/editorGUILayout).
    [Collection("Serial")]
    public sealed class EditorOutsideGuiTests
    {
        sealed class Logs : ILogHandler, IDisposable
        {
            readonly ILogHandler m_Previous = Debug.unityLogger.logHandler;
            internal readonly List<string> Lines = new List<string>();
            internal Logs() => Debug.unityLogger.logHandler = this;
            public void LogException(Exception exception, Object context) => Lines.Add("Exception: " + exception.GetType().Name + ": " + exception.Message);
            public void LogFormat(LogType logType, Object context, string format, params object[] args) => Lines.Add(logType + ": " + string.Format(format, args));
            public void Dispose() => Debug.unityLogger.logHandler = m_Previous;
        }

        sealed class OutsideGui : IDisposable
        {
            readonly Event m_Event = Event.current;
            readonly Color m_Color = GUI.color;
            readonly GUIClip.State m_Clips = GUIClip.Save();
            internal OutsideGui()
            {
                Event.current = null;
                GUIClip.ClearForTests();
            }
            public void Dispose()
            {
                // A pending label left by a test would be drawn by the next control.
                Event.current = new Event { type = EventType.Layout };
                GUIUtility.GetControlID(FocusType.Passive);
                Event.current = m_Event;
                GUI.color = m_Color;
                GUIClip.Restore(m_Clips);
            }
        }

        [Fact]
        public void TheEditorsStylesAreThereBeforeAnyGuiHasRun()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            using var outside = new OutsideGui();
            // (The styles' values are the Editor skin's, which the test host lacks: editorui covers them.)
            using var logs = new Logs();
            foreach (var read in new Func<GUIStyle>[] { () => EditorStyles.miniLabel, () => EditorStyles.foldout, () => EditorStyles.foldoutHeader,
                         () => EditorStyles.toolbarButton, () => EditorStyles.radioButton, () => EditorStyles.label, () => EditorStyles.textArea })
                Assert.NotNull(read());
            Assert.Equal(TextAnchor.MiddleCenter, EditorStyles.centeredGreyMiniLabel.alignment);
            Assert.Equal(Color.grey, EditorStyles.centeredGreyMiniLabel.normal.textColor);
            Assert.Equal(new RectOffset(5, 4, 0, 0).ToString(), EditorStyles.inspectorFullWidthMargins.padding.ToString());
        }

        [Fact]
        public void TheSkinAndANumericLayoutRectNeedAGuiPass()
        {
            using var outside = new OutsideGui();
            var skin = Assert.Throws<ArgumentException>(() => GUI.skin);
            Assert.Equal("You can only call GUI functions from inside OnGUI.", skin.Message);
            var rect = Assert.Throws<NullReferenceException>(() => GUILayoutUtility.GetRect(10f, 20f, 10f, 20f, GUIStyle.none));
            Assert.Equal("Object reference not set to an instance of an object", rect.Message);
        }

        [Fact]
        public void APrefixLabelForTheNextControlWaitsAndEachIdTakenOutsideOnGuiLogsIt()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            using var logs = new Logs();
            using var outside = new OutsideGui();
            EditorGUIUtility.labelWidth = 0f;
            var field = EditorGUI.PrefixLabel(new Rect(0, 0, 300, 18), new GUIContent("L"));
            Assert.Equal(new Rect(152, 0, 148, 18), field);
            Assert.Empty(logs.Lines);
            Assert.Throws<NullReferenceException>(() => Handles.PositionHandle(Vector3.one, Quaternion.identity));
            Assert.Equal(7, logs.Lines.Count);
            Assert.All(logs.Lines, l => Assert.Equal("Exception: NullReferenceException: Object reference not set to an instance of an object", l));
            logs.Lines.Clear();
            Assert.Throws<NullReferenceException>(() => Handles.Button(Vector3.zero, Quaternion.identity, 1, 1, Handles.CubeHandleCap));
            Assert.Single(logs.Lines);
        }

        [Fact]
        public void AnEmptyPrefixLabelNeedsNoEvent()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            using var outside = new OutsideGui();
            Assert.Equal(new Rect(0, 0, 300, 18), EditorGUI.PrefixLabel(new Rect(0, 0, 300, 18), GUIContent.none));
            Assert.Throws<NullReferenceException>(() => EditorGUI.PrefixLabel(new Rect(0, 0, 300, 18), 5, new GUIContent("L")));
        }

        [Fact]
        public void AFreshViewHasTheFocus()
        {
            // (defaultstate: a fresh GUIView and Toolbar have the focus, whatever the application's.)
            var view = (GUIView)ScriptableObject.CreateInstance(typeof(GUIView));
            Application.Internal_SetFocusState(false);
            try { Assert.True(view.hasFocus); }
            finally
            {
                Application.Internal_SetFocusState(true);
                Object.DestroyImmediate(view);
            }
        }
    }
}
