using System;
using System.IO;
using System.Linq;
using Unbound;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    public class ConsoleLogTests : IDisposable
    {
        readonly string root = Path.Combine(Environment.GetEnvironmentVariable("XDG_CACHE_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache"), "unbound/validation/consolelog-tests", Guid.NewGuid().ToString("N"));
        readonly string oldProject = ProjectInfo.ProjectPath;
        readonly string[] oldArgs = EditorProcess.s_CommandLine;
        readonly string oldCache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        string Project => Path.Combine(root, "project");
        string Cache => Path.Combine(root, "cache");
        string Logs => Path.Combine(Project, "Library/Unbound");

        public ConsoleLogTests()
        {
            foreach (string part in new[] { "Assets", "Packages", "ProjectSettings" })
            {
                Directory.CreateDirectory(Path.Combine(Project, part));
                File.WriteAllText(Path.Combine(Project, part, "sentinel.cs"), "authored sentinel " + part);
            }
            ProjectInfo.ProjectPath = Project;
            Environment.SetEnvironmentVariable("XDG_CACHE_HOME", Cache);
            ConsoleLog.Reset();
        }

        string[] Authored() => new[] { "Assets", "Packages", "ProjectSettings" }.SelectMany(part =>
            Directory.GetFileSystemEntries(Path.Combine(Project, part), "*", SearchOption.AllDirectories)
                .OrderBy(x => x).Select(x => x + (File.Exists(x) ? ":" + File.ReadAllText(x) : "/"))).ToArray();

        void Open(string path)
        {
            ConsoleLog.Reset();
            EditorProcess.s_CommandLine = new[] { "Unity", "-logFile", path };
            ConsoleLog.Append("log sentinel");
        }

        [Theory]
        [InlineData("Assets/sentinel.cs")]
        [InlineData("Packages/sentinel.cs")]
        [InlineData("ProjectSettings/sentinel.cs")]
        [InlineData("Assets/new/sub/log.txt")]
        [InlineData("Library/Unbound/../../Assets/sentinel.cs")]
        [InlineData("Library/Unbound-sibling/log.txt")]
        public void Unsafe_paths_leave_authored_files_and_directories_unchanged(string relative)
        {
            var before = Authored();
            Open(Path.Combine(Project, relative));
            Assert.Equal("", ConsoleLog.Path);
            Assert.Equal(before, Authored());
            Assert.False(Directory.Exists(Path.Combine(Project, "Library")));
        }

        [Theory]
        [InlineData("ancestor")]
        [InlineData("final")]
        [InlineData("dangling")]
        [InlineData("backup")]
        [InlineData("root")]
        public void Symlink_escapes_cannot_create_rotate_or_overwrite(string kind)
        {
            Directory.CreateDirectory(Logs);
            string path = Path.Combine(Logs, "test.log");
            if (kind == "ancestor")
            {
                Directory.CreateSymbolicLink(Path.Combine(Logs, "escape"), Path.Combine(Project, "Assets"));
                path = Path.Combine(Logs, "escape/new/sub/log.txt");
            }
            if (kind == "final" || kind == "dangling")
                File.CreateSymbolicLink(path, Path.Combine(Project, kind == "final" ? "Assets/sentinel.cs" : "Assets/new.cs"));
            if (kind == "backup")
            {
                File.WriteAllText(path, "previous log sentinel");
                File.CreateSymbolicLink(Path.Combine(Logs, "test-prev.log"), Path.Combine(Project, "Assets/sentinel.cs"));
            }
            if (kind == "root")
            {
                Directory.Delete(Logs);
                Directory.CreateSymbolicLink(Logs, Path.Combine(Project, "Assets"));
            }
            var before = Authored();
            Open(path);
            Assert.Equal("", ConsoleLog.Path);
            Assert.Equal(before, Authored());
            if (kind == "backup") Assert.Equal("previous log sentinel", File.ReadAllText(path));
        }

        [Fact]
        public void Defaults_rotation_cache_and_stdout_work()
        {
            EditorProcess.s_CommandLine = new[] { "Unity" };
            ConsoleLog.Append("first session");
            Assert.Equal(Path.Combine(Logs, "Editor.log"), ConsoleLog.Path);
            ConsoleLog.Reset();
            ConsoleLog.Append("second session");
            Assert.Contains("first session", File.ReadAllText(Path.Combine(Logs, "Editor-prev.log")));
            Assert.Contains("second session", File.ReadAllText(Path.Combine(Logs, "Editor.log")));
            Open(Path.Combine(Cache, "unbound/test/log.txt"));
            Assert.Contains("log sentinel", File.ReadAllText(ConsoleLog.Path));
            Open("-");
            Assert.Equal("", ConsoleLog.Path);
            Assert.False(File.Exists(Path.Combine(Environment.CurrentDirectory, "-")));
        }

        [Fact]
        public void Project_alias_and_relocated_global_cache_are_supported()
        {
            string alias = Path.Combine(root, "project-alias");
            Directory.CreateSymbolicLink(alias, Project);
            ProjectInfo.ProjectPath = alias;
            Open(Path.Combine(alias, "Library/Unbound/alias.log"));
            Assert.Equal(Path.Combine(Logs, "alias.log"), ConsoleLog.Path);
            Directory.CreateDirectory(Cache);
            string relocated = Path.Combine(root, "relocated");
            Directory.CreateDirectory(relocated);
            Directory.CreateSymbolicLink(Path.Combine(Cache, "unbound"), relocated);
            Open(Path.Combine(Cache, "unbound/session.log"));
            Assert.Equal(Path.Combine(relocated, "session.log"), ConsoleLog.Path);
            Directory.CreateSymbolicLink(Path.Combine(relocated, "escape"), Path.Combine(Project, "Assets"));
            Open(Path.Combine(Cache, "unbound/escape/sentinel.cs"));
            Assert.Equal("", ConsoleLog.Path);
        }

        public void Dispose()
        {
            ConsoleLog.Reset();
            ProjectInfo.ProjectPath = oldProject;
            EditorProcess.s_CommandLine = oldArgs;
            Environment.SetEnvironmentVariable("XDG_CACHE_HOME", oldCache);
            Directory.Delete(root, true);
        }
    }
}
