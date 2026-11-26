using System;
using System.Collections.Generic;
using System.Reflection;
using Unbound.Runtime;
using UnityEditor;
using UnityEngine.SceneManagement;
using Xunit;

namespace UnityEngine.Tests
{
    public abstract class KeptSceneCounter : MonoBehaviour
    {
        public static readonly List<string> Calls = new List<string>();
        void Line(string callback) => Calls.Add(name + " " + callback + " " + (Application.isPlaying ? "play" : "edit"));
        void Awake() => Line("Awake");
        void OnEnable() => Line("OnEnable");
        void OnDisable() => Line("OnDisable");
        void OnDestroy() => Line("OnDestroy");
    }
    [ExecuteAlways] public class KeptSceneAlways : KeptSceneCounter { }
    public class KeptScenePlain : KeptSceneCounter { }

    /// <summary>[InitializeOnEnterPlayMode] of this assembly's editor code (only while a test asks).</summary>
    static class KeptSceneEnterPlayMode
    {
        internal static bool Active;

        [InitializeOnEnterPlayMode]
        static void Entering(EnterPlayModeOptions options)
        {
            if (Active) KeptSceneCounter.Calls.Add("enterPlayMode " + (int)options + " " + (Application.isPlaying ? "play" : "edit")
                + (EditorApplication.isPlayingOrWillChangePlaymode ? " willPlay" : ""));
        }
    }

    /// <summary>Entering Play mode with Enter Play Mode Options that keep the domain or the open
    /// scene (tests/projects/scenereloadoff): the Edit-mode start's end and the kept scene's play.</summary>
    [Collection("Frames")]
    public class EnterPlayModeKeptSceneTests
    {
        static readonly FieldInfo s_StartRoots = typeof(EditorSession).GetField("s_EditModeStartRoots", BindingFlags.Static | BindingFlags.NonPublic);

        sealed class Setup : IDisposable
        {
            readonly bool m_Playing = EditorSession.IsPlaying;
            readonly bool m_Enabled = EditorSettings.enterPlayModeOptionsEnabled;
            readonly EnterPlayModeOptions m_Options = EditorSettings.enterPlayModeOptions;
            readonly Unbound.Tests.LightProbesTests.Scenes m_Scenes = new Unbound.Tests.LightProbesTests.Scenes();
            readonly UnityEngine.Events.UnityAction<Scene, LoadSceneMode> m_Loaded;
            internal readonly List<GameObject> Roots = new List<GameObject>();
            internal readonly List<string> Loaded = new List<string>();

            internal Setup(EnterPlayModeOptions options)
            {
                EditorSettings.enterPlayModeOptionsEnabled = true;
                EditorSettings.enterPlayModeOptions = options;
                KeptSceneCounter.Calls.Clear();
                m_Scenes.Loaded("Main");
                EditorSession.IsPlaying = false;
                Roots.Add(Make<KeptSceneAlways>("Always", enabled: true));
                Roots.Add(Make<KeptScenePlain>("Plain", enabled: true));
                Roots.Add(Make<KeptSceneAlways>("AlwaysDisabled", enabled: false));
                m_Loaded = (scene, mode) => Loaded.Add(scene.name + ":" + (int)mode);
                SceneManager.sceneLoaded += m_Loaded;
                // As the Edit-mode open of the start scene leaves it (EditorSession.OpenStartSceneInEditMode).
                s_StartRoots.SetValue(null, new List<GameObject>(Roots));
            }

            static GameObject Make<T>(string name, bool enabled) where T : KeptSceneCounter
            {
                var go = new GameObject(name);
                go.SetActive(false);
                go.AddComponent<T>().enabled = enabled;
                go.SetActive(true);
                return go;
            }

            public void Dispose()
            {
                SceneManager.sceneLoaded -= m_Loaded;
                s_StartRoots.SetValue(null, null);
                EditorSession.IsPlaying = true;
                foreach (var root in Roots)
                    if (root != null) Object.DestroyImmediate(root);
                EditorSession.IsPlaying = m_Playing;
                EditorSettings.enterPlayModeOptionsEnabled = m_Enabled;
                EditorSettings.enterPlayModeOptions = m_Options;
                m_Scenes.Dispose();
                KeptSceneCounter.Calls.Clear();
            }
        }

        [Theory]
        [InlineData(EnterPlayModeOptions.DisableSceneReload)]
        [InlineData(EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload)]
        public void AKeptSceneStopsInEditModeThenPlaysWithoutASecondAwake(EnterPlayModeOptions options)
        {
            using var setup = new Setup(options);
            Assert.Equal(new[] { "Always Awake edit", "Always OnEnable edit", "AlwaysDisabled Awake edit" }, KeptSceneCounter.Calls);
            KeptSceneCounter.Calls.Clear();
            Assert.True(EditorSession.PlaysStartSceneAsOpened);
            EditorSession.EndEditModeStart();
            Assert.Equal(new[] { "Always OnDisable edit" }, KeptSceneCounter.Calls);
            Assert.All(setup.Roots, root => Assert.True(root != null));
            KeptSceneCounter.Calls.Clear();
            EditorSession.IsPlaying = true;
            EditorSession.PlayOpenScenes(reloaded: false);
            Assert.Equal(new[] { "Always OnEnable play", "Plain Awake play", "Plain OnEnable play" }, KeptSceneCounter.Calls);
            // A kept scene raises no sceneLoaded, and is not played again.
            Assert.Empty(setup.Loaded);
            Assert.False(EditorSession.PlaysStartSceneAsOpened);
        }

        [Fact]
        public void WithTheSceneReloadedTheEditModeObjectsAreDestroyedInEditMode()
        {
            using var setup = new Setup(EnterPlayModeOptions.DisableDomainReload);
            KeptSceneCounter.Calls.Clear();
            Assert.False(EditorSession.PlaysStartSceneAsOpened);
            EditorSession.EndEditModeStart();
            Assert.Equal(new[] { "Always OnDisable edit", "Always OnDestroy edit", "AlwaysDisabled OnDestroy edit" }, KeptSceneCounter.Calls);
            Assert.All(setup.Roots, root => Assert.True(root == null));
            // Once ended, a second end does nothing.
            KeptSceneCounter.Calls.Clear();
            EditorSession.EndEditModeStart();
            Assert.Empty(KeptSceneCounter.Calls);
        }

        [Fact]
        public void WithTheSceneReloadedARootTheLoadKeepsIsNotDestroyed()
        {
            // A DontSave object an Edit-mode script made: a scene load keeps it (moves it out).
            using var setup = new Setup(EnterPlayModeOptions.DisableDomainReload);
            var helper = new GameObject("Helper") { hideFlags = HideFlags.DontSave };
            setup.Roots.Add(helper);
            s_StartRoots.SetValue(null, new List<GameObject>(setup.Roots));
            KeptSceneCounter.Calls.Clear();
            EditorSession.EndEditModeStart();
            Assert.True(helper != null);
            Assert.True(setup.Roots[0] == null && setup.Roots[1] == null && setup.Roots[2] == null);
        }

        [Fact]
        public void EnteringPlayModeFromEditModeRunsEnterPlayModeMethodsBeforeStoppingAKeptScene()
        {
            // As the probe's Editor capture enters Play mode (EditorApplication.isPlaying = true):
            // ExitingEditMode, [InitializeOnEnterPlayMode] in Edit mode, then OnDisable in Edit mode.
            var assemblies = (List<Assembly>)typeof(GameDomain).GetField("s_Assemblies", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            var own = typeof(KeptSceneEnterPlayMode).Assembly;
            bool added = !assemblies.Contains(own);
            bool startedInEditMode = EditorSession.StartedInEditMode;
            using var setup = new Setup(EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload);
            s_StartRoots.SetValue(null, null);
            Action<PlayModeStateChange> state = change => KeptSceneCounter.Calls.Add(change.ToString());
            EditorApplication.playModeStateChanged += state;
            if (added) assemblies.Add(own);
            KeptSceneEnterPlayMode.Active = true;
            try
            {
                EditorSession.StartedInEditMode = true;
                KeptSceneCounter.Calls.Clear();
                EditorSession.Request(true);
                EditorSession.ApplyRequest();
                Assert.Equal(new[]
                {
                    "ExitingEditMode", "enterPlayMode 3 edit willPlay", "Always OnDisable edit",
                    "Always OnEnable play", "Plain Awake play", "Plain OnEnable play",
                }, KeptSceneCounter.Calls);
                Assert.Empty(setup.Loaded);
            }
            finally
            {
                KeptSceneEnterPlayMode.Active = false;
                if (added) assemblies.Remove(own);
                EditorApplication.playModeStateChanged -= state;
                if (EditorSession.IsPlaying)
                {
                    EditorSession.Request(false);
                    EditorSession.ApplyRequest();
                }
                EditorSession.StartedInEditMode = startedInEditMode;
            }
        }

        [Fact]
        public void ReloadedOpenScenesReportSceneLoaded()
        {
            // Control: the scenes the Editor reloads for Play mode raise sceneLoaded (PlayModeEntry, 4).
            using var setup = new Setup(EnterPlayModeOptions.None);
            KeptSceneCounter.Calls.Clear();
            EditorSession.IsPlaying = true;
            EditorSession.PlayOpenScenes(reloaded: true);
            Assert.Equal(new[] { "Plain Awake play", "Plain OnEnable play" }, KeptSceneCounter.Calls);
            Assert.Equal(new[] { "Main:4" }, setup.Loaded);
        }

        [Theory]
        [InlineData(EnterPlayModeOptions.None, false, false)]
        [InlineData(EnterPlayModeOptions.DisableDomainReload, true, false)]
        [InlineData(EnterPlayModeOptions.DisableSceneReload, true, true)]
        [InlineData(EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload, true, true)]
        public void TheOptionsChooseTheEditModeStart(EnterPlayModeOptions options, bool opens, bool keeps)
        {
            bool enabled = EditorSettings.enterPlayModeOptionsEnabled;
            var previous = EditorSettings.enterPlayModeOptions;
            try
            {
                EditorSettings.enterPlayModeOptionsEnabled = true;
                EditorSettings.enterPlayModeOptions = options;
                Assert.Equal(opens, EditorSession.OpensStartSceneInEditMode);
                Assert.Equal(keeps, EditorSession.KeepsScenesForPlay);
                // Options that are off keep nothing.
                EditorSettings.enterPlayModeOptionsEnabled = false;
                Assert.False(EditorSession.OpensStartSceneInEditMode);
                Assert.False(EditorSession.KeepsScenesForPlay);
                // A family without the measurement loads the start scene in Play mode, as before.
                EditorSettings.enterPlayModeOptionsEnabled = true;
                using (TestRelease.Pin(TestRelease.Unmeasured))
                {
                    Assert.False(EditorSession.OpensStartSceneInEditMode);
                    Assert.False(EditorSession.KeepsScenesForPlay);
                }
            }
            finally
            {
                EditorSettings.enterPlayModeOptionsEnabled = enabled;
                EditorSettings.enterPlayModeOptions = previous;
            }
        }
    }
}
