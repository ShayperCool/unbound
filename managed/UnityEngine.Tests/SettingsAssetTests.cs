using System.IO;
using System.Linq;
using Unbound.Runtime;
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
