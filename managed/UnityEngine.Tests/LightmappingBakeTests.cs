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
    /// is entered and in Edit mode.</summary>
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

        /// <summary>The bake events with Lightmapping.isRunning at each: "S" bakeStarted, "U"
        /// lightingDataUpdated, "C" bakeCompleted.</summary>
        static List<string> BakeEvents(Action body)
        {
            var events = new List<string>();
            Action started = () => events.Add("S:" + Lightmapping.isRunning),
                updated = () => events.Add("U:" + Lightmapping.isRunning),
                completed = () => events.Add("C:" + Lightmapping.isRunning);
            Lightmapping.bakeStarted += started;
            Lightmapping.lightingDataUpdated += updated;
            Lightmapping.bakeCompleted += completed;
            try { body(); }
            finally
            {
                Lightmapping.bakeStarted -= started;
                Lightmapping.lightingDataUpdated -= updated;
                Lightmapping.bakeCompleted -= completed;
            }
            return events;
        }

        [Fact]
        public void InEditModeBakeRunsWithItsEventsAndAnswersTrue()
        {
            // Measured (script_bake.py --edit): bakeStarted with isRunning still false,
            // lightingDataUpdated while it runs, bakeCompleted with isRunning false again; twice.
            for (int call = 0; call < 2; call++)
            {
                bool result = false;
                List<string> events = null;
                var (logs, started) = Run(Phase.Edit, () => events = BakeEvents(() => result = Lightmapping.Bake()));
                Assert.True(result);
                Assert.Equal("SC", started);
                Assert.Equal(new[] { "S:False", "U:True", "C:False" }, events);
                Assert.False(Lightmapping.isRunning);
                Assert.DoesNotContain(logs.Messages, m => m.Message == PlayMode);
            }
        }

        [Fact]
        public void AnEditModeBakeBakesTheOpenScenesAndKeepsTheBakeForTheSession()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.CreateScene("EditBaked");
            var data = scene.Data;
            data.Path = "Assets/EditBaked.unity";
            var settings = new LightingSettings { bakedGI = true, realtimeGI = true };
            data.LightingSettings = settings;
            var file = new LoadedFile { Path = data.Path };
            var made = new GameObject("Made");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(made, scene);
            var renderer = made.AddComponent<MeshRenderer>();
            file.Objects[1] = renderer;
            RealtimeGI.SceneLoaded(data.Handle.Value, default, file, settings, true);
            // A renderer a script adds after the load is not baked: named.
            var added = new GameObject("Added");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(added, scene);
            added.AddComponent<MeshRenderer>();
            RealtimeGI.s_Warned.Clear();
            try
            {
                Assert.False(RealtimeGI.ScriptBakedThisSession(data.Path));
                Assert.False(data.IsDirty);
                var environment = RenderSettings.BakedEnvironment(file);
                bool result = false;
                var (logs, events) = Run(Phase.Edit, () => result = Lightmapping.Bake());
                Assert.True(result);
                Assert.Equal("SC", events);
                // The scene is dirty and keeps the bake (its lighting data asset, which the Editor
                // writes and names, is not written: lightingDataAsset stays null, said once).
                Assert.True(data.IsDirty);
                Assert.True(RealtimeGI.ScriptBakedThisSession(data.Path));
                Assert.True(data.BakedOnOpen);
                Assert.Contains(logs.Messages, m => m.Type == LogType.Warning && m.Message.Contains("LightingData.asset"));
                Assert.Contains(logs.Messages, m => m.Type == LogType.Warning && m.Message.Contains("Q677"));
                Assert.Contains(logs.Messages, m => m.Type == LogType.Warning && m.Message.Contains("'Added'"));
                Assert.DoesNotContain(logs.Messages, m => m.Message.Contains("'Made'"));
                // Where the bake makes no environment lighting (here: without the native side)
                // the scene keeps the one it had, also when it loads again.
                Assert.True(RealtimeGI.BakedEnvironmentKept(data.Path, environment.Probe, environment.Reflection));
                // Said once: a second bake says nothing again.
                var again = Run(Phase.Edit, () => Assert.True(Lightmapping.Bake()));
                Assert.DoesNotContain(again.Logs.Messages, m => m.Message.Contains("LightingData.asset") || m.Message.Contains("Q677"));
            }
            finally
            {
                RealtimeGI.s_Warned.Clear();
                RealtimeGI.ForgetEnvironment(data.Path);
                RealtimeGI.SceneUnloaded(data.Handle.Value);
                UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(scene);
            }
        }

        [Fact]
        public void TheLightingDataIsWrittenForACompleteEnvironmentBakeOfAProjectScene()
        {
            var environment = new LightingSettings { bakedGI = false, realtimeGI = false };
            // (The project's own assets: Assets/ here.)
            System.Func<string, bool> own = path => path.StartsWith("Assets/", System.StringComparison.Ordinal);
            string Unwritten(LightingSettings settings, string folder, bool made) => RealtimeGI.Unwritten(settings, folder, made, own);
            try
            {
                using (TestRelease.Pin(TestRelease.Measured))
                {
                    // Control: an environment bake that made all of it, of a scene in Assets/.
                    Assert.Null(Unwritten(environment, "Assets/Scenes/Lit", made: true));
                    // Baked or realtime GI on (or the defaults, which have it): not written.
                    Assert.Contains("environment alone", Unwritten(null, "Assets/Scenes/Lit", true));
                    environment.bakedGI = true;
                    Assert.Contains("environment alone", Unwritten(environment, "Assets/Scenes/Lit", true));
                    environment.bakedGI = false;
                    environment.realtimeGI = true;
                    Assert.Contains("environment alone", Unwritten(environment, "Assets/Scenes/Lit", true));
                    environment.realtimeGI = false;
                    // An environment kept in part (several scenes, gamma, Custom sources, no device),
                    // an untitled scene, a scene of a package the project does not hold: not written.
                    Assert.Contains("did not make", Unwritten(environment, "Assets/Scenes/Lit", false));
                    Assert.Contains("untitled", Unwritten(environment, null, true));
                    Assert.Contains("own assets", Unwritten(environment, "Packages/com.unity.render-pipelines.core/Samples/Lit", true));
                }
                // Another release family: not measured, not written.
                using (TestRelease.Pin(TestRelease.Unmeasured))
                    Assert.Contains("not measured", Unwritten(environment, "Assets/Scenes/Lit", true));
            }
            finally
            {
                Object.DestroyImmediate(environment);
            }
        }

        [Fact]
        public void ABakeThatWritesNothingNamesNoWrittenLightingData()
        {
            // A scene whose earlier bake wrote its lighting data, baked again with baked GI on:
            // nothing is written and the scene no longer counts as written.
            var data = new Unbound.Runtime.SceneData { Path = "Assets/Scenes/Lit.unity", LightingDataWritten = true };
            var settings = new LightingSettings { bakedGI = true };
            RealtimeGI.s_Warned.Clear();
            try
            {
                var (logs, _) = Run(Phase.Edit, () => RealtimeGI.WriteLightingData(data, null, settings, new RealtimeGI.EnvironmentBake(true, null)));
                Assert.False(data.LightingDataWritten);
                Assert.Contains(logs.Messages, m => m.Type == LogType.Warning && m.Message.Contains("LightingData.asset"));
            }
            finally
            {
                RealtimeGI.s_Warned.Clear();
                Object.DestroyImmediate(settings);
            }
        }

        [Fact]
        public void ARealtimeContributorLitByProbesIsBakedAsLightmappedAndNamed()
        {
            // Receive Global Illumination is read from the scene; what the Editor's realtime GI
            // makes of such a contributor is not measured, so the bake keeps taking it as
            // lightmapped and says so.
            var lit = new GameObject("ProbeLit");
            var plain = new GameObject("Plain");
            RealtimeGI.s_Warned.Clear();
            try
            {
                lit.m_StaticEditorFlags = plain.m_StaticEditorFlags = 1;
                var renderer = lit.AddComponent<MeshRenderer>();
                renderer.ReadSerialized(YNode.MakeMap(("m_ReceiveGI", YNode.MakeScalar("2"))), null);
                var other = plain.AddComponent<MeshRenderer>();
                var none = new HashSet<Renderer>();
                var (logs, _) = Run(Phase.Edit, () =>
                {
                    Assert.True(RealtimeGI.TakesContributor("Assets/ProbeLit.unity", 1, renderer, none));
                    Assert.True(RealtimeGI.TakesContributor("Assets/ProbeLit.unity", 2, other, none));
                    Assert.False(RealtimeGI.TakesContributor("Assets/ProbeLit.unity", 1, renderer, new HashSet<Renderer> { renderer }));
                });
                var said = logs.Messages.FindAll(m => m.Type == LogType.Warning && m.Message.Contains("is lit by light probes"));
                Assert.Single(said);
                Assert.Contains("'ProbeLit'", said[0].Message);
            }
            finally
            {
                RealtimeGI.s_Warned.Clear();
                Object.DestroyImmediate(lit);
                Object.DestroyImmediate(plain);
            }
        }

        [Fact]
        public void EditCodeThatAsksForPlayModeOrRunsAsItIsLeftBakesInEditMode()
        {
            // Play mode requested in Edit mode, and ExitingEditMode (sent before the domain
            // reloads): the bake is an Edit-mode one.
            bool wasPlaying = EditorSession.IsPlaying, wasEditMode = EditorSession.StartedInEditMode;
            try
            {
                EditorSession.StartedInEditMode = true;
                EditorSession.IsPlaying = false;
                EditorSession.Request(true);
                Assert.True(EditorSession.WillChange);
                var (logs, events) = Run(Phase.Edit, () => Assert.True(Lightmapping.Bake()));
                Assert.Equal("SC", events);
                Assert.DoesNotContain(logs.Messages, m => m.Message == PlayMode);
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
                try { Assert.True(Lightmapping.Bake()); }
                finally { EditorSession.EndEnteringPlayMode(); }
            });
            Assert.Equal("SC", exiting.Events);
        }

        [Fact]
        public void TheDomainLoadForPlayModeBakesAndExitingEditModeBakesInEditMode()
        {
            // The domain loads for Play mode with this assembly's editor code: its load callbacks
            // bake; ExitingEditMode, sent between them, is an Edit-mode bake.
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
            Assert.Equal(new[] { "load:True:SC", "exiting:True:SC", "reload:True:SC" }, BakeFromDomainLoad.Results);
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
