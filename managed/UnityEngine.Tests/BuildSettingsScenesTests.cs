using System;
using System.IO;
using System.Linq;
using Unbound.Runtime;
using UnityEditor;
using UnityEngine.SceneManagement;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The build settings' scene list as the Editor keeps it (tests/projects/scenesave
    /// build-scenes): EditorBuildSettingsScene as Unity's own C# makes it, a set list copied and its
    /// scenes' paths taken from their GUIDs, sceneListChanged only for a change, the file written by
    /// SaveAssets as the Editor's snapshot.</summary>
    [Collection("EditorState")]
    public class BuildSettingsScenesTests
    {
        const string BaseGuid = "e0fd96c04ed5f4230bfb1fc1aa060f13";
        const string SecondGuid = "6f748856d2f6b48bba33779b860ce87a";

        static string Repo()
        {
            var dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "AGENTS.md"))) dir = Path.GetDirectoryName(dir);
            return dir;
        }

        static string Describe(EditorBuildSettingsScene s) => (s.enabled ? "1" : "0") + "|" + (s.path ?? "null") + "|" + s.guid;

        [Fact]
        public void AScenesPathAndComparisonAreUnitysOwn()
        {
            AssetDatabase.List("[{\"guid\":\"" + SecondGuid + "\",\"path\":\"Assets/Scenes/Second.unity\",\"kind\":\"scene\"}]");
            try { PathsAndComparison(); }
            finally { AssetDatabase.Invalidate(); }
        }

        static void PathsAndComparison()
        {
            Assert.Equal("Assets/Scenes/Second.unity", new EditorBuildSettingsScene("Assets\\Scenes\\Second.unity", false).path);
            Assert.Throws<NullReferenceException>(() => new EditorBuildSettingsScene((string)null, true));
            var scene = new EditorBuildSettingsScene { path = "Assets\\Other\\Path.unity" };
            Assert.Equal("Assets/Other/Path.unity", scene.path);
            Assert.Throws<NullReferenceException>(() => scene.path = null);
            var a = new EditorBuildSettingsScene { path = "Assets/Scenes/Base.unity" };
            var b = new EditorBuildSettingsScene { path = "Assets/Scenes/Second.unity" };
            // The other's path compared with this one's (measured: compare.ab 1, compare.ba -1).
            Assert.Equal(1, Math.Sign(a.CompareTo(b)));
            Assert.Equal(-1, Math.Sign(b.CompareTo(a)));
            Assert.Throws<ArgumentException>(() => a.CompareTo(null));
            // Enabled scenes with a path (measured: active).
            var on = new EditorBuildSettingsScene { path = "Assets/Scenes/Base.unity", enabled = true };
            Assert.Equal(new[] { "Assets/Scenes/Base.unity" }, EditorBuildSettingsScene.GetActiveSceneList(new[]
                { on, b, new EditorBuildSettingsScene { enabled = true }, new EditorBuildSettingsScene { path = "", enabled = true } }));
            Assert.Throws<ArgumentNullException>(() => EditorBuildSettingsScene.GetActiveSceneList(null));
        }

        [Fact]
        public void DirectPlaysStartupSceneTakesItsBuildIndexFromTheResolvedList()
        {
            var buildScenes = ProjectInfo.BuildScenes.ToList();
            AssetDatabase.List("[{\"guid\":\"" + BaseGuid + "\",\"path\":\"Assets/Scenes/Base.unity\",\"kind\":\"scene\"}]");
            SceneData scene = null;
            try
            {
                // The host's list as the file has it: Base.unity under a stale path.
                EditorBuildSettings.Configure(new[] { new EditorBuildSettingsScene { path = "Assets/Old/Moved.unity", guid = new GUID(BaseGuid), enabled = true } },
                    new System.Collections.Generic.Dictionary<string, (string, long)>());
                ProjectInfo.BuildScenes.Clear();
                ProjectInfo.BuildScenes.Add(("Assets/Old/Moved.unity", BaseGuid));
                scene = PlayerLoopDriver.PrepareStartupScene("Assets/Scenes/Base.unity");
                Assert.Equal(0, scene.BuildIndex);
                Assert.Equal(0, SceneUtility.GetBuildIndexByScenePath("Assets/Scenes/Base.unity"));
            }
            finally
            {
                if (scene != null)
                {
                    SceneRegistry.Loaded.Remove(scene);
                    SceneRegistry.Forget(scene);
                    SceneRegistry.ResetToUntitled();
                }
                ProjectInfo.BuildScenes.Clear();
                ProjectInfo.BuildScenes.AddRange(buildScenes);
                SettingsAssets.Reset();
                AssetDatabase.Invalidate();
            }
        }

        [Fact]
        public void ASetListIsCopiedResolvedSavedAndAnnouncedOnlyWhenItChanges()
        {
            var probe = Path.Combine(Repo(), "tests", "projects", "scenesave");
            var snapshot = File.ReadAllText(Path.Combine(probe, "Reference", "unity-6000.0.71f1", "build-scenes", "snapshots", "set.saved.file"));
            var project = Path.Combine(Path.GetTempPath(), "unbound-build-scenes-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(project, "ProjectSettings"));
            foreach (var f in Directory.GetFiles(Path.Combine(probe, "ProjectSettings")))
                File.Copy(f, Path.Combine(project, "ProjectSettings", Path.GetFileName(f)));
            var before = ProjectInfo.ProjectPath;
            bool playing = EditorSession.IsPlaying;
            var buildScenes = ProjectInfo.BuildScenes.ToList();
            int changed = 0;
            Action count = () => changed++;
            ProjectInfo.ProjectPath = project;
            EditorSession.IsPlaying = false;
            SettingsAssets.Reset();
            AssetDatabase.List("[{\"guid\":\"" + BaseGuid + "\",\"path\":\"Assets/Scenes/Base.unity\",\"kind\":\"scene\"},"
                + "{\"guid\":\"" + SecondGuid + "\",\"path\":\"Assets/Scenes/Second.unity\",\"kind\":\"scene\"}]");
            EditorBuildSettings.sceneListChanged += count;
            try
            {
                Assert.Empty(EditorBuildSettings.scenes);
                var first = new EditorBuildSettingsScene("Assets/Scenes/Base.unity", true);
                var list = new[]
                {
                    first,
                    new EditorBuildSettingsScene("Assets\\Scenes\\Second.unity", false),
                    new EditorBuildSettingsScene("Assets/Scenes/Missing.unity", true),
                    new EditorBuildSettingsScene { path = "Assets/Old/Moved.unity", guid = new GUID(BaseGuid), enabled = true },
                    new EditorBuildSettingsScene { path = "", guid = new GUID(SecondGuid), enabled = true },
                };
                EditorBuildSettings.scenes = list;
                var expected = new[]
                {
                    "1|Assets/Scenes/Base.unity|" + BaseGuid, "0|Assets/Scenes/Second.unity|" + SecondGuid,
                    "1|Assets/Scenes/Missing.unity|00000000000000000000000000000000", "1|Assets/Scenes/Base.unity|" + BaseGuid,
                    "1|Assets/Scenes/Second.unity|" + SecondGuid,
                };
                Assert.Equal(expected, EditorBuildSettings.scenes.Select(Describe));
                Assert.Equal(1, changed);
                // Copies in, copies out.
                first.enabled = false;
                EditorBuildSettings.scenes[0].path = "Assets/Changed.unity";
                Assert.Equal(expected, EditorBuildSettings.scenes.Select(Describe));
                Assert.NotSame(EditorBuildSettings.scenes[0], EditorBuildSettings.scenes[0]);
                // The build indices after a set: the enabled scenes (Play mode unmeasured).
                Assert.Equal(new[] { "Assets/Scenes/Base.unity", "Assets/Scenes/Missing.unity", "Assets/Scenes/Base.unity", "Assets/Scenes/Second.unity" },
                    ProjectInfo.BuildScenes.Select(s => s.Path));
                // Written by SaveAssets, not by the set, as the Editor's snapshot.
                Assert.DoesNotContain("Missing", File.ReadAllText(Path.Combine(project, "ProjectSettings/EditorBuildSettings.asset")));
                AssetDatabase.SaveAssets();
                Assert.Equal(snapshot, File.ReadAllText(Path.Combine(project, "ProjectSettings/EditorBuildSettings.asset")));
                // The same list: no event; a null entry: refused, nothing changed.
                EditorBuildSettings.scenes = EditorBuildSettings.scenes;
                Assert.Equal(1, changed);
                Assert.Throws<NullReferenceException>(() => EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Second.unity", true), null });
                Assert.Equal(expected, EditorBuildSettings.scenes.Select(Describe));
                Assert.Equal(1, changed);
                // Null clears the list (an event); an empty list then changes nothing.
                EditorBuildSettings.scenes = null;
                Assert.Empty(EditorBuildSettings.scenes);
                Assert.Equal(2, changed);
                EditorBuildSettings.scenes = new EditorBuildSettingsScene[0];
                Assert.Equal(2, changed);
                // A scene given by its GUID alone (no path): its path is the GUID's asset's.
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene { guid = new GUID(BaseGuid), enabled = true } };
                Assert.Equal(new[] { "1|Assets/Scenes/Base.unity|" + BaseGuid }, EditorBuildSettings.scenes.Select(Describe));
                Assert.Equal(3, changed);
                // globalScenes sets the list with no event.
                EditorBuildSettings.globalScenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Second.unity", true) };
                Assert.Equal(new[] { "1|Assets/Scenes/Second.unity|" + SecondGuid }, EditorBuildSettings.scenes.Select(Describe));
                Assert.Equal(3, changed);
                // A list read with a stale path answers the current one; set again as it is, it
                // changes nothing (no event, the file as read).
                EditorBuildSettings.Configure(new[] { new EditorBuildSettingsScene { path = "Assets/Old/Moved.unity", guid = new GUID(BaseGuid), enabled = true } },
                    new System.Collections.Generic.Dictionary<string, (string, long)>());
                Assert.Equal(new[] { "1|Assets/Scenes/Base.unity|" + BaseGuid }, EditorBuildSettings.scenes.Select(Describe));
                // The host's build indices, as read, name the current path once the editor domain loads.
                ProjectInfo.BuildScenes.Clear();
                ProjectInfo.BuildScenes.Add(("Assets/Old/Moved.unity", BaseGuid));
                EditorBuildSettings.ResolveBuildIndices();
                Assert.Equal(new[] { "Assets/Scenes/Base.unity" }, ProjectInfo.BuildScenes.Select(b => b.Path));
                AssetDatabase.SaveAssets();
                var file = File.ReadAllText(Path.Combine(project, "ProjectSettings/EditorBuildSettings.asset"));
                EditorBuildSettings.scenes = EditorBuildSettings.scenes;
                AssetDatabase.SaveAssets();
                Assert.Equal(file, File.ReadAllText(Path.Combine(project, "ProjectSettings/EditorBuildSettings.asset")));
                Assert.Equal(3, changed);
            }
            finally
            {
                EditorBuildSettings.sceneListChanged -= count;
                ProjectInfo.ProjectPath = before;
                EditorSession.IsPlaying = playing;
                ProjectInfo.BuildScenes.Clear();
                ProjectInfo.BuildScenes.AddRange(buildScenes);
                SettingsAssets.Reset();
                AssetDatabase.Invalidate();
                Directory.Delete(project, true);
            }
        }
    }
}
