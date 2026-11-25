using System;
using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using UnityEngine.SceneManagement;
using Xunit;
using UObject = UnityEngine.Object;

namespace UnityEngine.Tests
{
    [Collection("Frames")]
    public sealed class EditorShutdownTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ThrowingUnloadHandlerDoesNotInterruptOtherScenesOrFinalization(bool saveThrows)
        {
            using var scenes = new Unbound.Tests.LightProbesTests.Scenes();
            var persistent = SceneRegistry.DontDestroyOnLoad;
            var previousRoots = persistent.Roots.ToArray();
            var detached = SceneRegistry.Detached;
            var previousDetached = detached.Roots.ToArray();
            persistent.Roots.Clear();
            detached.Roots.Clear();
            var first = scenes.Loaded("FirstQuit");
            var firstObject = new GameObject("first");
            var second = scenes.Loaded("SecondQuit");
            SceneRegistry.Active = second;
            var secondObject = new GameObject("second");
            var keep = new GameObject("persistent");
            UObject.DontDestroyOnLoad(keep);
            var calls = new List<string>();
            UnityEngine.Events.UnityAction<Scene> throws = scene =>
            {
                Assert.True(firstObject == null && secondObject == null && keep == null);
                Assert.True(scene.IsValid());
                Assert.False(scene.isLoaded);
                calls.Add(scene.name);
                throw new InvalidOperationException("shutdown control");
            };
            UnityEngine.Events.UnityAction<Scene> afterThrow = scene => calls.Add("after throw");
            SceneManager.sceneUnloaded += throws;
            SceneManager.sceneUnloaded += afterThrow;
            try
            {
                void Save()
                {
                    Assert.Empty(SceneRegistry.Loaded);
                    calls.Add("save");
                    if (saveThrows) throw new InvalidOperationException("save control");
                }
                void Finish() => PlayerLoopDriver.FinishQuit(true, Save, () => calls.Add("plugins"));
                if (saveThrows) Assert.Throws<InvalidOperationException>(Finish);
                else Finish();
                Assert.Equal(new[] { "FirstQuit", "SecondQuit", "save", "plugins" }, calls);
                Assert.Empty(SceneRegistry.Loaded);
                Assert.Null(SceneRegistry.Active);
                Assert.Null(SceneRegistry.Get(first.Handle));
                Assert.Null(SceneRegistry.Get(second.Handle));
            }
            finally
            {
                SceneManager.sceneUnloaded -= throws;
                SceneManager.sceneUnloaded -= afterThrow;
                UObject.DestroyImmediate(firstObject);
                UObject.DestroyImmediate(secondObject);
                UObject.DestroyImmediate(keep);
                persistent.Roots.Clear();
                persistent.Roots.AddRange(previousRoots);
                detached.Roots.Clear();
                detached.Roots.AddRange(previousDetached);
            }
        }

        /// <summary>A session that ends with Play mode leaves it, then the Editor quits:
        /// EnteredEditMode after Play mode's objects are gone, then EditorApplication.quitting, then
        /// the dirty assets are saved; the Editor's own exit raises EditorApplication.quitting while
        /// its scenes are open (matches the Unity 6000.0.71f1 Editor, tests/projects/tmpatlas quit.*,
        /// exit.Assets/Fonts/*: TextMeshPro's quit handler's changes are saved). A release without
        /// the measurement raises no EditorApplication.quitting.</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TheEditorsQuitCallbacksRunBeforeTheDirtyAssetsAreSaved(bool editorExit)
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            using var scenes = new Unbound.Tests.LightProbesTests.Scenes();
            var persistent = SceneRegistry.DontDestroyOnLoad;
            var previousRoots = persistent.Roots.ToArray();
            persistent.Roots.Clear();
            bool playing = EditorSession.IsPlaying;
            var calls = new List<string>();
            Action<UnityEditor.PlayModeStateChange> state = s => calls.Add(s + " playing=" + EditorSession.IsPlaying);
            Action quitting = () => calls.Add("quitting scenes=" + SceneRegistry.Loaded.Count);
            UnityEditor.EditorApplication.playModeStateChanged += state;
            UnityEditor.EditorApplication.quitting += quitting;
            try
            {
                scenes.Loaded("Open");
                EditorSession.IsPlaying = !editorExit;
                PlayerLoopDriver.FinishQuit(editorExit, () => calls.Add("preferences"), () => calls.Add("plugins"), () => calls.Add("save"), leftPlayMode: !editorExit);
                Assert.Equal(editorExit
                    ? new[] { "quitting scenes=1", "save", "preferences", "plugins" }
                    : new[] { "EnteredEditMode playing=False", "quitting scenes=1", "save", "preferences", "plugins" }, calls);
                calls.Clear();
                using (TestRelease.Pin(TestRelease.Unmeasured))
                {
                    scenes.Loaded("Again");
                    PlayerLoopDriver.FinishQuit(true, () => { }, () => { }, () => calls.Add("save"));
                }
                Assert.Equal(new[] { "save" }, calls);
            }
            finally
            {
                UnityEditor.EditorApplication.playModeStateChanged -= state;
                UnityEditor.EditorApplication.quitting -= quitting;
                EditorSession.IsPlaying = playing;
                persistent.Roots.Clear();
                persistent.Roots.AddRange(previousRoots);
            }
        }

        /// <summary>A quitting handler that throws is logged, the others run, and Play mode still ends.</summary>
        [Fact]
        public void AThrowingQuittingHandlerDoesNotStopLeavingPlayMode()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            using var scenes = new Unbound.Tests.LightProbesTests.Scenes();
            scenes.Loaded("Played");
            var persistent = SceneRegistry.DontDestroyOnLoad;
            var previousRoots = persistent.Roots.ToArray();
            persistent.Roots.Clear();
            bool playing = EditorSession.IsPlaying, startedInEdit = EditorSession.StartedInEditMode;
            var calls = new List<string>();
            Action<UnityEditor.PlayModeStateChange> state = s => calls.Add(s.ToString());
            Action throws = () => throw new InvalidOperationException("quitting control");
            Action after = () => calls.Add("after");
            var logs = new List<string>();
            var handler = Debug.unityLogger.logHandler;
            UnityEditor.EditorApplication.playModeStateChanged += state;
            Application.quitting += throws;
            Application.quitting += after;
            try
            {
                Debug.unityLogger.logHandler = new CapturingLog(logs);
                EditorSession.StartedInEditMode = true;
                EditorSession.IsPlaying = true;
                EditorSession.Request(false);
                EditorSession.ApplyRequest();
                Assert.Equal(new[] { "ExitingPlayMode", "after", "EnteredEditMode" }, calls);
                Assert.False(EditorSession.IsPlaying);
                Assert.Contains(logs, l => l.Contains("quitting control"));
            }
            finally
            {
                Debug.unityLogger.logHandler = handler;
                UnityEditor.EditorApplication.playModeStateChanged -= state;
                Application.quitting -= throws;
                Application.quitting -= after;
                Application.Internal_PlayModeStarting();
                if (SceneRegistry.Active != null && SceneRegistry.Active.Path == "") SceneRegistry.Forget(SceneRegistry.Active);
                EditorSession.IsPlaying = playing;
                EditorSession.StartedInEditMode = startedInEdit;
                persistent.Roots.Clear();
                persistent.Roots.AddRange(previousRoots);
            }
        }

        sealed class CapturingLog : ILogHandler
        {
            readonly List<string> m_Logs;
            internal CapturingLog(List<string> logs) => m_Logs = logs;
            public void LogFormat(LogType logType, Object context, string format, params object[] args) =>
                m_Logs.Add(args.Length == 0 ? format : string.Format(format, args));
            public void LogException(Exception exception, Object context) => m_Logs.Add(exception.Message);
        }

        /// <summary>Leaving Play mode within a session raises Application.quitting between
        /// ExitingPlayMode and EnteredEditMode and cancels the exit token, which the next Play mode
        /// gets anew (tests/projects/tmpatlas quit.*).</summary>
        [Fact]
        public void LeavingPlayModeRaisesApplicationQuittingAndTheNextPlayModeGetsANewExitToken()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            using var scenes = new Unbound.Tests.LightProbesTests.Scenes();
            scenes.Loaded("Played");
            var persistent = SceneRegistry.DontDestroyOnLoad;
            var previousRoots = persistent.Roots.ToArray();
            persistent.Roots.Clear();
            bool playing = EditorSession.IsPlaying, startedInEdit = EditorSession.StartedInEditMode;
            var calls = new List<string>();
            Action<UnityEditor.PlayModeStateChange> state = s => calls.Add(s.ToString());
            Action quitting = () => calls.Add("Application.quitting");
            UnityEditor.EditorApplication.playModeStateChanged += state;
            Application.quitting += quitting;
            try
            {
                EditorSession.StartedInEditMode = true;
                EditorSession.IsPlaying = true;
                var token = Application.exitCancellationToken;
                EditorSession.Request(false);
                EditorSession.ApplyRequest();
                Assert.Equal(new[] { "ExitingPlayMode", "Application.quitting", "EnteredEditMode" }, calls);
                Assert.True(token.IsCancellationRequested);
                Application.Internal_PlayModeStarting();
                Assert.False(Application.exitCancellationToken.IsCancellationRequested);
            }
            finally
            {
                UnityEditor.EditorApplication.playModeStateChanged -= state;
                Application.quitting -= quitting;
                if (SceneRegistry.Active != null && SceneRegistry.Active.Path == "") SceneRegistry.Forget(SceneRegistry.Active);
                EditorSession.IsPlaying = playing;
                EditorSession.StartedInEditMode = startedInEdit;
                persistent.Roots.Clear();
                persistent.Roots.AddRange(previousRoots);
            }
        }
    }
}
