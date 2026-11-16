using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Unbound.Tests;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Lightmapping.Bake and BakeAsync from scripts (Scripting API 6000.0; measured in the
    /// Unity 6000.0.71f1 Editor: in Play mode both warn "Computing lightmaps is not available in
    /// play mode." and answer false; otherwise Bake bakes the open scenes, sending bakeStarted and
    /// bakeCompleted, and answers true).</summary>
    public class LightmappingBakeTests
    {
        const string NoBakeOnLoad = "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!157 &3\nLightmapSettings:\n" +
            "  serializedVersion: 13\n  m_BakeOnSceneLoad: 0\n  m_LightingDataAsset: {fileID: 0}\n  m_LightingSettings: {fileID: 0}\n";
        const string WithData = "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!157 &3\nLightmapSettings:\n" +
            "  serializedVersion: 13\n  m_BakeOnSceneLoad: 0\n" +
            "  m_LightingDataAsset: {fileID: 112000000, guid: 0123456789abcdef0123456789abcdef, type: 2}\n  m_LightingSettings: {fileID: 0}\n";

        sealed class Resolver : IReferenceResolver
        {
            public Object Resolve(ObjectRef reference, Type expected) => null;
        }

        sealed class Logs : ILogHandler
        {
            internal readonly List<(LogType Type, string Message)> Messages = new List<(LogType, string)>();
            public void LogException(Exception exception, Object context) => Messages.Add((LogType.Exception, exception.Message));
            public void LogFormat(LogType logType, Object context, string format, params object[] args) =>
                Messages.Add((logType, args.Length == 0 ? format : string.Format(format, args)));
        }

        /// <summary>Runs <paramref name="body"/> with <paramref name="playing"/> as the Editor's play
        /// state, the log captured and the bake events counted ("S" started, "C" completed).</summary>
        static (Logs Logs, string Events) Run(bool playing, Action body)
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
                EditorSession.IsPlaying = playing;
                Debug.unityLogger.logHandler = logs;
                body();
            }
            finally
            {
                Debug.unityLogger.logHandler = handler;
                EditorSession.IsPlaying = wasPlaying;
                Lightmapping.bakeStarted -= started;
                Lightmapping.bakeCompleted -= completed;
            }
            return (logs, events);
        }

        static SceneData Open(string name)
        {
            var scene = SceneRegistry.Create(name, "Assets/ScriptBake/" + name + "-" + Guid.NewGuid().ToString("N") + ".unity", -1);
            scene.OpenedInEditor = true;
            SceneRegistry.Loaded.Add(scene);
            return scene;
        }

        [Fact]
        public void InPlayModeNeitherBakeStartsAndBothWarn()
        {
            var scene = Open("Playing");
            try
            {
                bool bake = true, bakeAsync = true;
                var (logs, events) = Run(playing: true, () =>
                {
                    bake = Lightmapping.Bake();
                    bakeAsync = Lightmapping.BakeAsync();
                });
                Assert.False(bake);
                Assert.False(bakeAsync);
                Assert.Equal("", events);
                Assert.Equal(new[] { (LogType.Warning, "Computing lightmaps is not available in play mode."),
                    (LogType.Warning, "Computing lightmaps is not available in play mode.") }, logs.Messages);
                Assert.False(RealtimeGI.ScriptBakeRequested(scene.Path));
            }
            finally
            {
                SceneRegistry.Forget(scene);
            }
        }

        [Fact]
        public void OutsidePlayModeTheOpenSceneIsBakedWhenItIsRead()
        {
            var requested = Open("Requested");
            var control = SceneRegistry.Create("Control", "Assets/ScriptBake/Control-" + Guid.NewGuid().ToString("N") + ".unity", -1);
            control.OpenedInEditor = true;
            try
            {
                bool result = false;
                var (logs, events) = Run(playing: false, () => result = Lightmapping.Bake());
                Assert.True(result);
                Assert.Equal("SC", events);
                Assert.Empty(logs.Messages);
                Assert.True(RealtimeGI.ScriptBakeRequested(requested.Path));
                // Its LightmapSettings do not ask for a bake on load; the script's bake does.
                SceneSettings.Apply(UnityYaml.Parse(NoBakeOnLoad)[0], new Resolver(), requested);
                Assert.True(requested.BakedOnOpen);
                // Control: a scene that was not open when the script baked.
                SceneSettings.Apply(UnityYaml.Parse(NoBakeOnLoad)[0], new Resolver(), control);
                Assert.False(control.BakedOnOpen);
            }
            finally
            {
                SceneRegistry.Forget(requested);
                SceneRegistry.Forget(control);
            }
        }

        [Fact]
        public void ASceneAlreadyReadOrWithLightingDataKeepsWhatItHasAndSaysSo()
        {
            var read = Open("Read");
            var withData = Open("WithData");
            try
            {
                SceneSettings.Apply(UnityYaml.Parse(NoBakeOnLoad)[0], new Resolver(), read);
                Assert.False(read.BakedOnOpen);
                var (logs, events) = Run(playing: false, () => Assert.True(Lightmapping.Bake()));
                Assert.Equal("SC", events);
                Assert.Contains(logs.Messages, m => m.Type == LogType.Warning && m.Message.Contains(read.Path)
                    && m.Message.Contains("already loaded"));
                Assert.False(RealtimeGI.ScriptBakeRequested(read.Path));
                var (dataLogs, _) = Run(playing: false, () =>
                    SceneSettings.Apply(UnityYaml.Parse(WithData)[0], new Resolver(), withData));
                Assert.False(withData.BakedOnOpen);
                Assert.Contains(dataLogs.Messages, m => m.Type == LogType.Warning && m.Message.Contains(withData.Path)
                    && m.Message.Contains("keeps the data it has"));
            }
            finally
            {
                SceneRegistry.Forget(read);
                SceneRegistry.Forget(withData);
            }
        }
    }
}
