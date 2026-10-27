using System.IO;
using System.Linq;
using Unbound.Runtime;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Settings assets as the Editor has them (tests/projects/settings: its README tells how
    /// the reference is recorded).</summary>
    public class SettingsAssetTests
    {
        static string Repo([System.Runtime.CompilerServices.CallerFilePath] string source = "") =>
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source), "..", ".."));

        /// <summary>Every Unity YAML settings file of the probe projects writes back byte for byte.</summary>
        [Fact]
        public void UnityYamlTextWritesFilesBackAsTheyWere()
        {
            int files = 0;
            // The probe projects, and the test game where the machine has it.
            var projects = Directory.GetDirectories(Path.Combine(Repo(), "tests", "projects")).ToList();
            var game = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), "unity-projects", "the test game");
            if (Directory.Exists(game)) projects.Add(game);
            foreach (var path in projects
                         .SelectMany(p => Directory.Exists(Path.Combine(p, "ProjectSettings")) ? Directory.GetFiles(Path.Combine(p, "ProjectSettings"), "*.asset") : new string[0]))
            {
                var text = File.ReadAllText(path);
                if (!text.StartsWith("%YAML")) continue;
                var written = UnityYamlText.Write(UnityYamlText.Parse(text));
                Assert.True(text == written, $"{path} differs at {FirstDifference(text, written)}");
                files++;
            }
            Assert.True(files > 100, $"{files} files");
        }

        /// <summary>The probe's edits made in Edit mode and saved: the files the Editor wrote
        /// (Reference/saved, ProbeRunner.Save), byte for byte.</summary>
        [Fact]
        public void SavedSettingsAreTheEditors()
        {
            var probe = Path.Combine(Repo(), "tests", "projects", "settings");
            var project = Path.Combine(Path.GetTempPath(), "unbound-settings-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(project, "ProjectSettings"));
            foreach (var f in Directory.GetFiles(Path.Combine(probe, "ProjectSettings")))
                File.Copy(f, Path.Combine(project, "ProjectSettings", Path.GetFileName(f)));
            var before = ProjectInfo.ProjectPath;
            bool playing = EditorSession.IsPlaying;
            ProjectInfo.ProjectPath = project;
            // Edit mode: the Editor saves settings there only.
            EditorSession.IsPlaying = false;
            SettingsAssets.Reset();
            try
            {
                var input = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/InputManager.asset")[0]);
                var axes = input.FindProperty("m_Axes");
                int count = axes.arraySize;
                axes.arraySize = count + 1;
                var axis = axes.GetArrayElementAtIndex(count);
                axis.FindPropertyRelative("m_Name").stringValue = "Unbound Probe Axis";
                axis.FindPropertyRelative("descriptiveName").stringValue = "A probe";
                axis.FindPropertyRelative("descriptiveNegativeName").stringValue = "";
                axis.FindPropertyRelative("negativeButton").stringValue = "page down";
                axis.FindPropertyRelative("positiveButton").stringValue = "page up";
                axis.FindPropertyRelative("altNegativeButton").stringValue = "";
                axis.FindPropertyRelative("altPositiveButton").stringValue = "";
                axis.FindPropertyRelative("gravity").floatValue = 1000f;
                axis.FindPropertyRelative("dead").floatValue = 0.001f;
                axis.FindPropertyRelative("sensitivity").floatValue = 1000f;
                axis.FindPropertyRelative("snap").boolValue = false;
                axis.FindPropertyRelative("invert").boolValue = false;
                axis.FindPropertyRelative("type").intValue = 0;
                axis.FindPropertyRelative("axis").intValue = 0;
                axis.FindPropertyRelative("joyNum").intValue = 0;
                input.ApplyModifiedProperties();

                var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
                var tags = tagManager.FindProperty("tags");
                tags.InsertArrayElementAtIndex(tags.arraySize);
                tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = "ProbeTag";
                tagManager.FindProperty("layers").GetArrayElementAtIndex(8).stringValue = "ProbeLayer";
                tagManager.ApplyModifiedProperties();

                var player = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
                player.FindProperty("productName").stringValue = "Probe Product";
                player.ApplyModifiedProperties();

                var physics = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/DynamicsManager.asset")[0]);
                physics.FindProperty("m_Gravity").vector3Value = new Vector3(0, -5, 0);
                physics.ApplyModifiedProperties();

                AssetDatabase.SaveAssets();
                var saved = Directory.GetFiles(Path.Combine(probe, "Reference", "saved")).ToDictionary(Path.GetFileName);
                foreach (var f in Directory.GetFiles(Path.Combine(project, "ProjectSettings"), "*.asset"))
                {
                    var name = Path.GetFileName(f);
                    var want = saved.TryGetValue(name, out var s) ? File.ReadAllText(s) : File.ReadAllText(Path.Combine(probe, "ProjectSettings", name));
                    var got = File.ReadAllText(f);
                    Assert.True(want == got, $"{name} differs at {FirstDifference(want, got)}");
                }
            }
            finally
            {
                ProjectInfo.ProjectPath = before;
                EditorSession.IsPlaying = playing;
                SettingsAssets.Reset();
                Directory.Delete(project, true);
            }
        }

        internal static string FirstDifference(string a, string b)
        {
            int i = 0;
            while (i < a.Length && i < b.Length && a[i] == b[i]) i++;
            int line = a.Take(i).Count(c => c == '\n') + 1;
            string Around(string s) => s.Substring(System.Math.Max(0, i - 40), System.Math.Min(80, s.Length - System.Math.Max(0, i - 40)));
            return $"line {line}: «{Around(a)}» vs «{Around(b)}»";
        }
    }
}
