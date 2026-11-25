using System.Collections.Generic;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    public class ProbeWindow : EditorWindow
    {
        public static readonly List<string> Calls = new List<string>();

        void OnEnable() => Calls.Add("OnEnable");
        void OnFocus() => Calls.Add("OnFocus");
        void OnDestroy() => Calls.Add("OnDestroy");
    }

    public class EditorWindowTests
    {
        [Fact]
        public void WindowsOpenOncePerTypeAndCloseWithTheirLifecycle()
        {
            ProbeWindow.Calls.Clear();
            var window = EditorWindow.GetWindow<ProbeWindow>("Probe");
            Assert.Same(window, EditorWindow.GetWindow<ProbeWindow>());
            Assert.True(EditorWindow.HasOpenInstances<ProbeWindow>());
            Assert.Same(window, EditorWindow.focusedWindow);
            Assert.Equal("Probe", window.titleContent.text);
            window.position = new Rect(10, 20, 300, 200);
            Assert.Equal(new Rect(10, 20, 300, 200), window.position);
            window.Close();
            Assert.False(EditorWindow.HasOpenInstances<ProbeWindow>());
            Assert.Null(EditorWindow.focusedWindow);
            Assert.Equal(new[] { "OnEnable", "OnFocus", "OnDestroy" }, ProbeWindow.Calls);
        }

        [Fact]
        public void TheToolbarSitsOnTheMainWindow()
        {
            MainView.Open();
            Assert.NotNull(Toolbar.get);
            Assert.Equal(0f, Toolbar.get.parent.screenPosition.x);
            Assert.Equal(Screen.width, Toolbar.get.parent.screenPosition.width);
        }
    }
}
