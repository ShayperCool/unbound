using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// The Editor's exit with Play mode on (EditorApplication.Exit): no Application.quitting, no
    /// Application.unloading and no Play mode message, and its save reported to
    /// OnPostprocessAllAssets (tests/projects/unchangedsaves leaving); leaving Play mode first raises
    /// them (tests/projects/tmpatlas quit.*).
    /// </summary>
    [Collection("EditorState")]
    public sealed class EditorExitTests
    {
        static List<string> Raised(bool editorExit, bool playing)
        {
            var calls = new List<string>();
            Action quitting = () => calls.Add("quitting:" + (EditorSession.IsPlaying ? "play" : "edit"));
            Action unloading = () => calls.Add("unloading");
            Action<PlayModeStateChange> playMode = change => calls.Add(change.ToString());
            Application.quitting += quitting;
            Application.unloading += unloading;
            EditorApplication.playModeStateChanged += playMode;
            bool wasPlaying = EditorSession.IsPlaying;
            try
            {
                EditorSession.IsPlaying = playing;
                PlayerLoopDriver.RaiseQuitCallbacks(editorExit);
            }
            finally
            {
                EditorSession.IsPlaying = wasPlaying;
                Application.quitting -= quitting;
                Application.unloading -= unloading;
                EditorApplication.playModeStateChanged -= playMode;
                // The exit token the callbacks cancelled: a later Play mode gets a new one.
                Application.Internal_PlayModeStarting();
            }
            return calls;
        }

        [Fact]
        public void TheEditorsExitWithPlayModeOnRaisesNoApplicationQuitCallbacks()
        {
            Assert.Empty(Raised(editorExit: true, playing: true));
        }

        [Fact]
        public void LeavingPlayModeRaisesItsMessagesAndApplicationQuittingInPlayMode()
        {
            Assert.Equal(new[] { "ExitingPlayMode", "quitting:play", "unloading" }, Raised(editorExit: false, playing: true));
        }

        [Fact]
        public void TheEditorsExitInEditModeRaisesNoApplicationQuitting()
        {
            Assert.Equal(new[] { "unloading" }, Raised(editorExit: true, playing: false));
        }

        [Fact]
        public void TheExitsSaveIsReportedAsOneCall()
        {
            var calls = new List<string>();
            using (SessionImports.ReportsForTests((imported, deleted, moved, from) => calls.Add(string.Join(",", imported))))
                PlayerLoopDriver.ReportedExitSave(() =>
                {
                    SessionImports.Imported("Assets/P/ExitDirty.mat");
                    SessionImports.Imported("Assets/P/ExitChanged.mat");
                    Assert.Empty(calls);
                });
            Assert.Equal(new[] { "Assets/P/ExitDirty.mat,Assets/P/ExitChanged.mat" }, calls);
        }
    }
}
