using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
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
                if (phase == Phase.Entering) EditorSession.BeginEnteringPlayMode();
                else EditorSession.IsPlaying = phase == Phase.Play;
                Debug.unityLogger.logHandler = logs;
                body();
            }
            finally
            {
                Debug.unityLogger.logHandler = handler;
                if (phase == Phase.Entering) EditorSession.EndEnteringPlayMode();
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
        }
    }
}
