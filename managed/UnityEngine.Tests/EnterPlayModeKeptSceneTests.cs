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
                m_Scenes.Loaded("Main", "Assets/Main.unity");
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
