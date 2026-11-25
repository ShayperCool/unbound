using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unbound.Runtime;
using UnityEditor;
using UnityEngine.SceneManagement;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The build indices in Edit mode and in Play mode as the Unity 6000.0.71f1 Editor
    /// answers them (tests/projects/scenesave build-index-edit and build-index-play, whose rows
    /// the expected values are): Edit mode counts the whole list and finds a scene among its
    /// enabled entries by its path; Play mode's list, fixed as it starts, holds the enabled scenes
    /// the project has and the open scenes, and finds a scene also by the names LoadScene takes.</summary>
    [Collection("EditorState")]
    public class BuildIndexModesTests
    {
        const string BaseGuid = "e0fd96c04ed5f4230bfb1fc1aa060f13";
        const string SecondGuid = "6f748856d2f6b48bba33779b860ce87a";
        const string ThirdGuid = "9a3e5c1d7b2f4e6a8c0d1e2f3a4b5c6d";
        const string Base = "Assets/Scenes/Base.unity";
        const string Second = "Assets/Scenes/Second.unity";
        const string Third = "Assets/Scenes/Deep/Third.unity";
        const string Runner = "Assets/InitTestScene0.unity";
        const string RunnerGuid = "1f2e3d4c5b6a49788796a5b4c3d2e1f0";

        static EditorBuildSettingsScene Entry(string path, string guid, bool enabled) =>
            new EditorBuildSettingsScene { path = path, guid = new GUID(guid), enabled = enabled };

        /// <summary>The list run.py seeds: Second disabled, Base, Missing, Second under a stale path,
        /// Base disabled, Deep/Third (and, for Play mode, Base again).</summary>
        static List<EditorBuildSettingsScene> Seeded(bool play)
        {
            var list = new List<EditorBuildSettingsScene>
            {
                Entry(Second, SecondGuid, false), Entry(Base, BaseGuid, true),
                Entry("Assets/Scenes/Missing.unity", "0123456789abcdef0123456789abcdef", true),
                Entry("Assets/Old/Moved.unity", SecondGuid, true), Entry(Base, BaseGuid, false), Entry(Third, ThirdGuid, true),
            };
            if (play) list.Add(Entry(Base, BaseGuid, true));
            return list;
        }

        static string Paths() =>
            string.Join(",", Enumerable.Range(-1, SceneManager.sceneCountInBuildSettings + 2).Select(SceneUtility.GetScenePathByBuildIndex));

        /// <summary>Runs a step on a scratch project with the probe's scenes listed, Edit mode or
        /// Play mode as asked, the seeded list configured; everything restored after.</summary>
        static void WithList(bool play, Action step)
        {
            var project = Path.Combine(Path.GetTempPath(), "unbound-build-index-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(project, "ProjectSettings"));
            File.WriteAllText(Path.Combine(project, "ProjectSettings", "EditorBuildSettings.asset"),
                "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!1045 &1\nEditorBuildSettings:\n  m_ObjectHideFlags: 0\n"
                + "  serializedVersion: 2\n  m_Scenes: []\n  m_configObjects: {}\n  m_UseUCBPForAssetBundles: 0\n");
            var before = ProjectInfo.ProjectPath;
            bool playing = EditorSession.IsPlaying;
            var buildScenes = ProjectInfo.BuildScenes.ToList();
            ProjectInfo.ProjectPath = project;
            SettingsAssets.Reset();
            AssetDatabase.List("[{\"guid\":\"" + BaseGuid + "\",\"path\":\"" + Base + "\",\"kind\":\"scene\"},"
                + "{\"guid\":\"" + SecondGuid + "\",\"path\":\"" + Second + "\",\"kind\":\"scene\"},"
                + "{\"guid\":\"" + ThirdGuid + "\",\"path\":\"" + Third + "\",\"kind\":\"scene\"},"
                + "{\"guid\":\"" + RunnerGuid + "\",\"path\":\"" + Runner + "\",\"kind\":\"scene\"}]");
            EditorBuildSettings.Configure(Seeded(play), new Dictionary<string, (string, long)>());
            EditorSession.IsPlaying = play;
            try { step(); }
            finally
            {
                EditorSession.IsPlaying = playing;
                ProjectInfo.ProjectPath = before;
                ProjectInfo.BuildScenes.Clear();
                ProjectInfo.BuildScenes.AddRange(buildScenes);
                EditorBuildSettings.Reset();
                SettingsAssets.Reset();
                AssetDatabase.Invalidate();
                Directory.Delete(project, true);
            }
        }

        static SceneData Open(string path)
        {
            var data = SceneRegistry.Create(Path.GetFileNameWithoutExtension(path), path, SceneUtility.GetBuildIndexByScenePath(path));
            data.IsLoaded = true;
            data.LoadingState = Scene.LoadingState.Loaded;
            SceneRegistry.Loaded.Add(data);
            return data;
        }

        static void Close(params SceneData[] scenes)
        {
            foreach (var scene in scenes)
            {
                SceneRegistry.Loaded.Remove(scene);
                SceneRegistry.Forget(scene);
            }
            SceneRegistry.ResetToUntitled();
        }

        [Fact]
        public void EditModeCountsTheWholeListAndFindsAScenesIndexAmongTheEnabledEntriesByItsPath() => WithList(false, () =>
        {
            // start.count, start.path[*] (the stale entry answers its GUID's scene).
            Assert.Equal(6, SceneManager.sceneCountInBuildSettings);
            Assert.Equal("," + Second + "," + Base + ",Assets/Scenes/Missing.unity," + Second + "," + Base + "," + Third + ",", Paths());
            // start.index[*]: among Base, Missing, Second, Third; by the path alone, in any case.
            Assert.Equal(0, SceneUtility.GetBuildIndexByScenePath(Base));
            Assert.Equal(0, SceneUtility.GetBuildIndexByScenePath("assets/scenes/base.unity"));
            Assert.Equal(1, SceneUtility.GetBuildIndexByScenePath("Assets/Scenes/Missing.unity"));
            Assert.Equal(2, SceneUtility.GetBuildIndexByScenePath(Second));
            Assert.Equal(3, SceneUtility.GetBuildIndexByScenePath(Third));
            foreach (var other in new[] { "Assets/Old/Moved.unity", "Base", "Scenes/Base", "Assets/Scenes/Base", "Assets\\Scenes\\Base.unity", "Third", "Scenes/Deep/Third", "", null })
                Assert.Equal(-1, SceneUtility.GetBuildIndexByScenePath(other));
            // start.canLoad[*], start.canLoadName[*], start.sceneByIndex[*].
            Assert.True(Application.CanStreamedLevelBeLoaded(5));
            Assert.False(Application.CanStreamedLevelBeLoaded(6));
            Assert.False(Application.CanStreamedLevelBeLoaded("Base"));
            Assert.False(Application.CanStreamedLevelBeLoaded(Second));
            Assert.False(SceneManager.GetSceneByBuildIndex(5).IsValid());
            Assert.Throws<ArgumentException>(() => SceneManager.GetSceneByBuildIndex(6));
        });

        [Fact]
        public void InEditModeTheOpenScenesTakeTheirIndicesFromANewListAtOnce() => WithList(false, () =>
        {
            var base_ = Open(Base);
            var second = Open(Second);
            try
            {
                // opened.open[*], opened.sceneByIndex[*].
                Assert.Equal(0, base_.BuildIndex);
                Assert.Equal(2, second.BuildIndex);
                Assert.Equal(Second, SceneManager.GetSceneByBuildIndex(2).path);
                Assert.False(SceneManager.GetSceneByBuildIndex(1).IsValid());
                // reordered.*: a disabled entry first, then Second and Base enabled.
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(Base, false), new EditorBuildSettingsScene(Second, true), new EditorBuildSettingsScene(Base, true) };
                Assert.Equal(1, base_.BuildIndex);
                Assert.Equal(0, second.BuildIndex);
                Assert.Equal(3, SceneManager.sceneCountInBuildSettings);
                Assert.Equal(Base, SceneManager.GetSceneByBuildIndex(1).path);
                // disabled.*: every entry disabled.
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(Second, false), new EditorBuildSettingsScene(Base, false) };
                Assert.Equal(-1, base_.BuildIndex);
                Assert.Equal(-1, second.BuildIndex);
                Assert.Equal(2, SceneManager.sceneCountInBuildSettings);
                Assert.Equal(Second, SceneUtility.GetScenePathByBuildIndex(0));
            }
            finally { Close(base_, second); }
        });

        [Fact]
        public void PlayModesListHoldsTheEnabledScenesTheProjectHasThenTheOpenScenesAndStaysAsItStarted() => WithList(true, () =>
        {
            // start.count, start.path[*]: Missing left out, Base twice, the runner's scene last (an
            // open scene already listed, an untitled one and a path without a scene add nothing).
            EditorBuildSettings.BeginPlayModeList(new[] { Runner, Base, "", "Assets/Old/Moved.unity" });
            Assert.Equal(5, SceneManager.sceneCountInBuildSettings);
            Assert.Equal("," + Base + "," + Second + "," + Third + "," + Base + "," + Runner + ",", Paths());
            Assert.Equal(4, SceneUtility.GetBuildIndexByScenePath(Runner));
            Assert.Equal(-1, SceneUtility.GetBuildIndexByScenePath("Assets/Scenes/Missing.unity"));
            Assert.True(Application.CanStreamedLevelBeLoaded(4));
            Assert.False(Application.CanStreamedLevelBeLoaded(5));
            // set.*: a list set in Play mode changes nothing of Play mode's.
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(Second, true), new EditorBuildSettingsScene(Base, false) };
            Assert.Equal(5, SceneManager.sceneCountInBuildSettings);
            Assert.Equal(1, SceneUtility.GetBuildIndexByScenePath(Second));
            // Edit mode answers the new list.
            EditorSession.IsPlaying = false;
            Assert.Equal(2, SceneManager.sceneCountInBuildSettings);
            Assert.Equal(-1, SceneUtility.GetBuildIndexByScenePath(Base));
        });

        [Fact]
        public void InPlayModeAScenesNamesAreItsPathItsFileNameAndThePathTheBuildSettingsShow() => WithList(true, () =>
        {
            EditorBuildSettings.BeginPlayModeList(new[] { Runner });
            // start.index[*], start.canLoadName[*].
            var answers = new Dictionary<string, int>
            {
                [Base] = 0, ["assets/scenes/base.unity"] = 0, ["Base"] = 0, ["Scenes/Base"] = 0, ["second"] = 1,
                [Third] = 2, ["Third"] = 2, ["Scenes/Deep/Third"] = 2,
                ["Assets/Scenes/Base"] = -1, ["Assets\\Scenes\\Base.unity"] = -1, ["Deep/Third"] = -1, ["Deep/Third.unity"] = -1,
                ["Scenes/Deep/Third.unity"] = -1, ["Assets/Scenes/Missing.unity"] = -1, ["Missing"] = -1, ["Assets/Old/Moved.unity"] = -1, [""] = -1,
            };
            foreach (var pair in answers)
            {
                Assert.True(pair.Value == SceneUtility.GetBuildIndexByScenePath(pair.Key), pair.Key);
                Assert.True((pair.Value >= 0) == Application.CanStreamedLevelBeLoaded(pair.Key), pair.Key);
            }
            Assert.Equal(-1, SceneUtility.GetBuildIndexByScenePath(null));
        });

        [Fact]
        public void EnteringPlayModeGivesTheOpenScenesTheirPlayModeIndices() => WithList(false, () =>
        {
            var runner = Open(Runner);
            var third = Open(Third);
            try
            {
                Assert.Equal(-1, runner.BuildIndex);
                Assert.Equal(3, third.BuildIndex);
                // As EditorSession.EnterPlayMode: the list, then the open scenes' indices in it.
                EditorBuildSettings.BeginPlayModeList(new[] { Runner, Third });
                EditorSession.IsPlaying = true;
                EditorBuildSettings.RefreshOpenSceneIndices();
                // Base, Second (its stale entry), Third, then the runner's scene.
                Assert.Equal(3, runner.BuildIndex);
                Assert.Equal(2, third.BuildIndex);
            }
            finally { Close(runner, third); }
        });
    }
}
