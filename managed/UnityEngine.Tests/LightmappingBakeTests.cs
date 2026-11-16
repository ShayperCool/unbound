using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Editor code that bakes from the domain load callbacks (only while a test asks).</summary>
    static class BakeFromDomainLoad
    {
        internal static bool Active;
        internal static readonly List<string> Results = new List<string>();

        static void Bake(string from)
        {
            if (!Active) return;
            var events = "";
            Action started = () => events += "S", completed = () => events += "C";
            Lightmapping.bakeStarted += started;
            Lightmapping.bakeCompleted += completed;
            try { Results.Add(from + ":" + Lightmapping.Bake() + ":" + events); }
            finally
            {
                Lightmapping.bakeStarted -= started;
                Lightmapping.bakeCompleted -= completed;
            }
        }

        [InitializeOnLoadMethod]
        static void OnLoad() => Bake("load");

        [UnityEditor.Callbacks.DidReloadScripts]
        static void OnReload() => Bake("reload");

        internal static void OnPlayModeState(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode) Bake("exiting");
        }
    }

    /// <summary>Lightmapping.Bake and BakeAsync from scripts in each phase of the Editor (Scripting
    /// API 6000.0; measured in the Unity 6000.0.71f1 Editor, tests/projects/realtimegi
    /// `script_bake.py`): refused with a warning in Play mode, a bake with its events as Play mode
    /// is entered.</summary>
    public class LightmappingBakeTests
    {
        const string PlayMode = "Computing lightmaps is not available in play mode.";

        sealed class Logs : ILogHandler
        {
            internal readonly List<(LogType Type, string Message)> Messages = new List<(LogType, string)>();
            public void LogException(Exception exception, Object context) => Messages.Add((LogType.Exception, exception.Message));
            public void LogFormat(LogType logType, Object context, string format, params object[] args) =>
                Messages.Add((logType, args.Length == 0 ? format : string.Format(format, args)));
        }

        enum Phase { Edit, Entering, Play }

        static readonly System.Reflection.PropertyInfo s_PlayModeDomainLoading =
            typeof(EditorInitialization).GetProperty(nameof(EditorInitialization.PlayModeDomainLoading),
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

        /// <summary>Runs <paramref name="body"/> in <paramref name="phase"/> with the log captured
        /// and the bake events listed ("S" started, "C" completed).</summary>
        static (Logs Logs, string Events) Run(Phase phase, Action body)
        {
            var logs = new Logs();
            var events = "";
            Action started = () => events += "S", completed = () => events += "C";
            var handler = Debug.unityLogger.logHandler;
            bool wasPlaying = EditorSession.IsPlaying;
            Lightmapping.bakeStarted += started;
            Lightmapping.bakeCompleted += completed;
            try
            {
                if (phase == Phase.Entering) s_PlayModeDomainLoading.SetValue(null, true);
                if (phase == Phase.Entering) EditorSession.BeginEnteringPlayMode();
                else EditorSession.IsPlaying = phase == Phase.Play;
                Debug.unityLogger.logHandler = logs;
                body();
            }
            finally
            {
                Debug.unityLogger.logHandler = handler;
                if (phase == Phase.Entering) EditorSession.EndEnteringPlayMode();
                if (phase == Phase.Entering) s_PlayModeDomainLoading.SetValue(null, false);
                EditorSession.IsPlaying = wasPlaying;
                Lightmapping.bakeStarted -= started;
                Lightmapping.bakeCompleted -= completed;
            }
            return (logs, events);
        }

        [Fact]
        public void InPlayModeNeitherBakeStartsAndBothWarn()
        {
            bool bake = true, bakeAsync = true;
            var (logs, events) = Run(Phase.Play, () =>
            {
                bake = Lightmapping.Bake();
                bakeAsync = Lightmapping.BakeAsync();
            });
            Assert.False(bake);
            Assert.False(bakeAsync);
            Assert.Equal("", events);
            Assert.Equal(new[] { (LogType.Warning, PlayMode), (LogType.Warning, PlayMode) }, logs.Messages);
        }

        [Fact]
        public void AsPlayModeIsEnteredBakeSendsItsEventsAndAnswersTrue()
        {
            bool result = false;
            var (logs, events) = Run(Phase.Entering, () => result = Lightmapping.Bake());
            Assert.True(result);
            Assert.Equal("SC", events);
            Assert.DoesNotContain(logs.Messages, m => m.Message == PlayMode);
            // Control: the same call once Play mode has started is refused.
            Assert.Equal("", Run(Phase.Play, () => Assert.False(Lightmapping.Bake())).Events);
        }

        [Fact]
        public void InEditModeABakeIsNotMadeAndSaysSo()
        {
            for (int call = 0; call < 2; call++)
            {
                bool result = true;
                var (logs, events) = Run(Phase.Edit, () => result = Lightmapping.Bake());
                Assert.False(result);
                Assert.Equal("", events);
                Assert.Contains(logs.Messages, m => m.Type == LogType.Warning && m.Message.Contains("Edit mode is not implemented"));
            }
        }

        [Fact]
        public void EditCodeThatAsksForPlayModeOrRunsAsItIsLeftStillBakesInEditMode()
        {
            // Play mode requested in Edit mode, and ExitingEditMode (sent before the domain
            // reloads): the bake would be an Edit-mode one, which Unbound does not make.
            bool wasPlaying = EditorSession.IsPlaying, wasEditMode = EditorSession.StartedInEditMode;
            try
            {
                EditorSession.StartedInEditMode = true;
                EditorSession.IsPlaying = false;
                EditorSession.Request(true);
                Assert.True(EditorSession.WillChange);
                var (logs, events) = Run(Phase.Edit, () => Assert.False(Lightmapping.Bake()));
                Assert.Equal("", events);
                Assert.Contains(logs.Messages, m => m.Message.Contains("Edit mode is not implemented"));
            }
            finally
            {
                EditorSession.Request(false);
                EditorSession.StartedInEditMode = wasEditMode;
                EditorSession.IsPlaying = wasPlaying;
            }
            var exiting = Run(Phase.Edit, () =>
            {
                EditorSession.BeginEnteringPlayMode();
                try { Assert.False(Lightmapping.Bake()); }
                finally { EditorSession.EndEnteringPlayMode(); }
            });
            Assert.Equal("", exiting.Events);
            Assert.Contains(exiting.Logs.Messages, m => m.Type == LogType.Warning && m.Message.Contains("Edit mode is not implemented"));
        }

        [Fact]
        public void TheDomainLoadForPlayModeBakesAndExitingEditModeDoesNot()
        {
            // The domain loads for Play mode with this assembly's editor code: its load callbacks
            // bake; ExitingEditMode, sent between them, is an Edit-mode bake (not made).
            var assemblies = (List<System.Reflection.Assembly>)typeof(GameDomain)
                .GetField("s_Assemblies", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetValue(null);
            var own = typeof(BakeFromDomainLoad).Assembly;
            bool added = !assemblies.Contains(own);
            bool wasPlaying = EditorSession.IsPlaying;
            var logs = new Logs();
            var handler = Debug.unityLogger.logHandler;
            if (added) assemblies.Add(own);
            BakeFromDomainLoad.Results.Clear();
            BakeFromDomainLoad.Active = true;
            EditorApplication.playModeStateChanged += BakeFromDomainLoad.OnPlayModeState;
            try
            {
                Debug.unityLogger.logHandler = logs;
                EditorSession.BeginEnteringPlayMode();
                EditorInitialization.LoadEditorDomain(enteringPlayMode: true);
            }
            finally
            {
                EditorSession.EndEnteringPlayMode();
                EditorSession.IsPlaying = wasPlaying;
                Debug.unityLogger.logHandler = handler;
                EditorApplication.playModeStateChanged -= BakeFromDomainLoad.OnPlayModeState;
                BakeFromDomainLoad.Active = false;
                if (added) assemblies.Remove(own);
            }
            Assert.Equal(new[] { "load:True:SC", "exiting:False:", "reload:True:SC" }, BakeFromDomainLoad.Results);
            Assert.Contains(logs.Messages, m => m.Type == LogType.Warning && m.Message.Contains("Edit mode is not implemented"));
            Assert.False(EditorInitialization.PlayModeDomainLoading);
        }

        [Fact]
        public void StaticEditorFlagsAreTheGameObjectsOwn()
        {
            var go = new GameObject("flags");
            try
            {
                Assert.Equal((StaticEditorFlags)0, GameObjectUtility.GetStaticEditorFlags(go));
                go.m_StaticEditorFlags = (uint)(StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic);
                Assert.Equal(StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic, GameObjectUtility.GetStaticEditorFlags(go));
                Assert.Throws<ArgumentNullException>(() => GameObjectUtility.GetStaticEditorFlags(null));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
            // A destroyed GameObject is null to Unity's check.
            Assert.Throws<ArgumentNullException>(() => GameObjectUtility.GetStaticEditorFlags(go));
        }
    }
}
