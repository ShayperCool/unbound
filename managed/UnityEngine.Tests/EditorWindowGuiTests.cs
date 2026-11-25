using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine.UIElements;
using Xunit;

namespace UnityEngine.Tests
{
    public class GuiProbeWindow : EditorWindow
    {
        public static readonly List<string> Calls = new List<string>();

        void OnEnable() => Calls.Add("OnEnable");
        void OnFocus() => Calls.Add("OnFocus");
        public void CreateGUI() => Calls.Add("CreateGUI:" + (rootVisualElement.panel != null));
    }

    /// <summary>Editor windows' roots, object icons and list drawing as tests/projects/editorwindowgui
    /// measures them on the measured release, and the not-implemented notice of other families.</summary>
    public class EditorWindowGuiTests
    {
        sealed class Capture : ILogHandler
        {
            internal readonly List<string> Lines = new List<string>();
            public void LogException(Exception exception, Object context) => Lines.Add(exception.Message);
            public void LogFormat(LogType logType, Object context, string format, params object[] args) => Lines.Add(string.Format(format, args));
        }

        [Fact]
        public void AShownWindowsRootJoinsAnEditorPanelAndCreateGuiRunsBeforeTheFocus()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            GuiProbeWindow.Calls.Clear();
            var window = ScriptableObject.CreateInstance<GuiProbeWindow>();
            var root = window.rootVisualElement;
            Assert.Same(root, window.rootVisualElement);
            Assert.StartsWith("rootVisualContainer", root.name);
            Assert.Null(root.panel);
            root.Add(new VisualElement { name = "child" });
            window.Show();
            Assert.Equal(new[] { "OnEnable", "CreateGUI:True", "OnFocus" }, GuiProbeWindow.Calls);
            Assert.Equal(ContextType.Editor, root.panel.contextType);
            // A focused window gets no OnFocus again (window.laterFrame).
            window.Focus();
            Assert.Equal(new[] { "OnEnable", "CreateGUI:True", "OnFocus" }, GuiProbeWindow.Calls);
            window.Close();
            Assert.Null(root.panel);
            Assert.Null(root.parent);
            Assert.Equal(1, root.childCount);
        }

        [Fact]
        public void ATexturedPolyLineReadsTheTextureInEveryEvent()
        {
            var previous = Event.current;
            Event.current = new Event { type = EventType.Layout };
            try
            {
                // Unity's C# reads lineTex.height before its repaint check.
                Assert.Throws<NullReferenceException>(() => Handles.DrawAAPolyLine((Texture2D)null, Vector3.zero, Vector3.one));
                Handles.DrawAAPolyLine((Texture2D)null, 2f, Vector3.zero, Vector3.one);
            }
            finally { Event.current = previous; }
        }

        [Fact]
        public void IconsAreKeptForGameObjectsAndScriptableObjectsOnly()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var go = new GameObject("icons");
            var icon = new Texture2D(2, 2);
            try
            {
                Assert.Throws<ArgumentNullException>(() => EditorGUIUtility.GetIconForObject(null));
                Assert.Throws<ArgumentNullException>(() => EditorGUIUtility.SetIconForObject(null, icon));
                EditorGUIUtility.SetIconForObject(go, icon);
                Assert.Same(icon, EditorGUIUtility.GetIconForObject(go));
                EditorGUIUtility.SetIconForObject(go.transform, icon);
                Assert.Null(EditorGUIUtility.GetIconForObject(go.transform));
                EditorGUIUtility.SetIconForObject(go, null);
                Assert.Null(EditorGUIUtility.GetIconForObject(go));
                var target = new GameObject("destroyed");
                Object.DestroyImmediate(target);
                Assert.Throws<MissingReferenceException>(() => EditorGUIUtility.GetIconForObject(target));
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(icon);
            }
        }

        [Fact]
        public void OtherFamiliesReportIconsWindowsAndListDrawingNotImplemented()
        {
            var capture = new Capture();
            var previous = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = capture;
            GuiProbeWindow.Calls.Clear();
            try
            {
                using var release = TestRelease.Pin(TestRelease.Unmeasured);
                var go = new GameObject("unmeasured");
                Assert.Null(EditorGUIUtility.GetIconForObject(go));
                Object.DestroyImmediate(go);
                var window = ScriptableObject.CreateInstance<GuiProbeWindow>();
                window.Show();
                Assert.Null(window.rootVisualElement.panel);
                window.Close();
                var list = new ReorderableList(new List<int> { 1 }, typeof(int));
                bool drawn = false;
                list.drawHeaderCallback = rect => drawn = true;
                list.DoList(new Rect(0, 0, 100, 100));
                Assert.False(drawn);
            }
            finally { Debug.unityLogger.logHandler = previous; }
            Assert.Equal(new[] { "OnEnable" }, GuiProbeWindow.Calls);
            Assert.Contains(capture.Lines, line => line.Contains("EditorGUIUtility.GetIconForObject is not implemented"));
            Assert.Contains(capture.Lines, line => line.Contains("ReorderableList.DoList is not implemented"));
        }
    }
}
