using System;
using Unbound.Runtime;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    public sealed class DefaultEditorWindowStateTests : IDisposable
    {
        readonly string release = ProjectInfo.UnityVersion;
        public DefaultEditorWindowStateTests() { ProjectInfo.UnityVersion = "6000.0.71f1"; }
        public void Dispose() { ProjectInfo.UnityVersion = release; }

        sealed class Window : EditorWindow
        {
            internal Vector2 ConstructorMinimum;
            internal Rect ConstructorPosition;
            internal int ConstructorDepth;
            internal string ConstructorTitle;
            internal HideFlags AwakeFlags;
            public Window()
            {
                ConstructorMinimum = minSize; ConstructorPosition = position;
                ConstructorDepth = depthBufferBits; ConstructorTitle = titleContent.text;
            }
            void Awake() { AwakeFlags = hideFlags; }
            internal void WriteValues()
            {
                minSize = new Vector2(70, 90); position = new Rect(30, 40, 100, 120);
                depthBufferBits = 16; titleContent = new GUIContent("changed", "hint");
                saveChangesMessage = "changed-save";
            }
        }

        [Fact]
        public void DefaultsExistInTheConstructorAndWindowFlagsPrecedeUserAwake()
        {
            using var engine = MainThreadCheck.Engine();
            var window = ScriptableObject.CreateInstance<Window>();
            try
            {
                Assert.Equal(new Vector2(50, 50), window.ConstructorMinimum);
                Assert.Equal(new Rect(0, 0, 320, 550), window.ConstructorPosition);
                Assert.Equal(24, window.ConstructorDepth);
                Assert.Equal(typeof(Window).ToString(), window.ConstructorTitle);
                Assert.Equal(HideFlags.DontSave, window.AwakeFlags);
                Assert.Equal(typeof(Window) + " has unsaved changes.", window.saveChangesMessage);
                window.WriteValues();
                Assert.Equal(new Vector2(70, 90), window.minSize); Assert.Equal(new Rect(30, 40, 100, 120), window.position);
                Assert.Equal(16, window.depthBufferBits); Assert.Equal("changed", window.titleContent.text);
                Assert.Equal("changed-save", window.saveChangesMessage);
                var aa = typeof(EditorWindow).GetProperty("antiAlias");
                aa.SetValue(window, 4); Assert.Equal(1, aa.GetValue(window));
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Fact]
        public void OtherReleasesRetainTheirPriorWindowState()
        {
            using var engine = MainThreadCheck.Engine();
            ProjectInfo.UnityVersion = "6000.0.70f1";
            var window = ScriptableObject.CreateInstance<Window>();
            try
            {
                Assert.Equal(new Vector2(100, 100), window.minSize);
                Assert.Equal(new Rect(100, 100, 400, 300), window.position);
                Assert.Equal(0, window.depthBufferBits); Assert.Equal(HideFlags.None, window.AwakeFlags);
                var aa = typeof(EditorWindow).GetProperty("antiAlias"); aa.SetValue(window, 4); Assert.Equal(4, aa.GetValue(window));
            }
            finally { Object.DestroyImmediate(window); }
        }
    }
}
