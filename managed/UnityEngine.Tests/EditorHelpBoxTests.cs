using System;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>EditorGUI.HelpBox, EditorGUILayout.HelpBox, EditorGUIUtility.GetHelpIcon and
    /// EditorStyles.helpBox (tests/projects/imguiskin `helpBox/*`, tests/projects/editorstyles
    /// `helpBox/*`, Unity 6000.0.71f1).</summary>
    [Collection("Serial")]
    public sealed class EditorHelpBoxTests
    {
        sealed class Logs : ILogHandler
        {
            internal readonly System.Collections.Generic.List<string> Messages = new System.Collections.Generic.List<string>();
            public void LogFormat(LogType type, Object context, string format, params object[] args) => Messages.Add(string.Format(format, args));
            public void LogException(Exception exception, Object context) => Messages.Add(exception.Message);
        }

        static void Pass(Event current, Action body)
        {
            var previous = Event.current;
            Event.current = current;
            GUIUtility.BeginGUI(0, 701, 1);
            GUI.ResetState();
            try { body(); }
            finally
            {
                GUILayoutUtility.End();
                Event.current = previous;
            }
        }

        [Fact]
        public void HelpBoxOutsideOnGuiThrowsNullReferenceException()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var previous = Event.current;
            Event.current = null;
            try
            {
                Assert.Throws<NullReferenceException>(() => EditorGUI.HelpBox(new Rect(0, 0, 100, 20), "hello", MessageType.None));
                Assert.Throws<NullReferenceException>(() => EditorGUI.HelpBox(new Rect(0, 0, 100, 20), new GUIContent("hello")));
                Assert.Throws<NullReferenceException>(() => EditorGUILayout.HelpBox("hello", MessageType.None));
                Assert.Throws<NullReferenceException>(() => EditorGUILayout.HelpBox("hello", MessageType.None, false));
                Assert.Throws<NullReferenceException>(() => EditorGUILayout.HelpBox(new GUIContent("hello"), true));
            }
            finally { Event.current = previous; }
        }

        [Fact]
        public void GetHelpIconNoneAndUnknownReturnNullAndBlankContentIsSingleSpace()
        {
            Assert.Null(EditorGUIUtility.GetHelpIcon(MessageType.None));
            Assert.Null(EditorGUIUtility.GetHelpIcon((MessageType)99));
            Assert.Equal(" ", EditorGUIUtility.blankContent.text);
            Assert.Null(EditorGUIUtility.blankContent.image);
            Assert.NotSame(GUIContent.none, EditorGUIUtility.blankContent);
        }

        [Fact]
        public void AnotherReleaseFamilysHelpBoxSaysSoAndTakesNoControlId()
        {
            using var release = TestRelease.Pin(TestRelease.Unmeasured);
            var logs = new Logs();
            var previous = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = logs;
            try
            {
                int ids = -1;
                Pass(new Event { type = EventType.Layout }, () =>
                {
                    int before = GUIUtility.GetControlID(FocusType.Passive);
                    EditorGUI.HelpBox(new Rect(0, 0, 100, 20), "hello", MessageType.Info);
                    EditorGUI.HelpBox(new Rect(0, 0, 100, 20), new GUIContent("hello"));
                    EditorGUILayout.HelpBox("hello", MessageType.Warning);
                    EditorGUILayout.HelpBox("hello", MessageType.Error, false);
                    EditorGUILayout.HelpBox(new GUIContent("hello"), false);
                    ids = GUIUtility.GetControlID(FocusType.Passive) - before;
                });
                Assert.Equal(1, ids);
                Assert.Contains(logs.Messages, m => m.Contains("EditorGUI.HelpBox"));
                Assert.Contains(logs.Messages, m => m.Contains("EditorGUILayout.HelpBox"));
            }
            finally { Debug.unityLogger.logHandler = previous; }
        }
    }
}
