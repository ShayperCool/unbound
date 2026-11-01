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
        readonly string root;
        static string TestCache => Environment.GetEnvironmentVariable("XDG_CACHE_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
        readonly string oldProject = ProjectInfo.ProjectPath;
        readonly string[] oldArgs = EditorProcess.s_CommandLine;
        readonly string oldCache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        string Project => Path.Combine(root, "project");
        string Cache => Path.Combine(root, "cache");
        string Logs => Path.Combine(Project, "Library/Unbound");

        public ConsoleLogTests()
        {
            root = ConsoleLogPath.Validate(Path.Combine(TestCache, "unbound/validation/consolelog-tests", Guid.NewGuid().ToString("N")), null, TestCache);
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
        [InlineData("chained target ancestor")]
        [InlineData("loop")]
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
            if (kind == "chained target ancestor")
            {
                Directory.CreateSymbolicLink(Path.Combine(Logs, "alias"), Path.Combine(Project, "Assets"));
                File.CreateSymbolicLink(path, Path.Combine(Logs, "alias/sentinel.cs"));
            }
            if (kind == "loop") File.CreateSymbolicLink(path, path);
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

        [Fact]
        public void Migrated_validation_cache_is_permitted_but_cannot_point_into_authored_content()
        {
            string global = Path.Combine(Cache, "unbound");
            Directory.CreateDirectory(global);
            string validation = Path.Combine(global, "validation");
            string migrated = Path.Combine(root, "migrated-validation");
            Directory.CreateDirectory(migrated);
            Directory.CreateSymbolicLink(validation, migrated);
            Open(Path.Combine(validation, "session.log"));
            Assert.Equal(Path.Combine(migrated, "session.log"), ConsoleLog.Path);
            ConsoleLog.Reset();
            Directory.Delete(validation);
            Directory.CreateSymbolicLink(validation, Path.Combine(Project, "Assets"));
            var before = Authored();
            Open(Path.Combine(validation, "sentinel.cs"));
            Assert.Equal("", ConsoleLog.Path);
            Assert.Equal(before, Authored());
        }

        [Fact]
        public void Authored_directory_alias_into_allowed_cache_cannot_receive_logs()
        {
            foreach (string part in new[] { "Assets", "Packages", "ProjectSettings" })
            {
                string authored = Path.Combine(Project, part);
                string target = Path.Combine(Cache, "unbound", part);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                Directory.Move(authored, target);
                Directory.CreateSymbolicLink(authored, target);
                var before = Authored();
                Open(Path.Combine(target, "sentinel.cs"));
                Assert.Equal("", ConsoleLog.Path);
                Assert.Equal(before, Authored());
                Assert.False(File.Exists(Path.Combine(target, "sentinel-prev.cs")));
                Open(Path.Combine(authored, "new/sub/log.txt"));
                Assert.Equal("", ConsoleLog.Path);
                Assert.Equal(before, Authored());
                Assert.False(Directory.Exists(Path.Combine(target, "new")));
            }
        }

        // These fixtures are wholly temporary, including the external authored targets.
        // Do not follow links while recording names/parents/bytes, so cycle controls are safe.
        static string[] FixtureState(string fixture)
        {
            var state = new System.Collections.Generic.List<string>();
            var pending = new System.Collections.Generic.Stack<string>();
            pending.Push(fixture);
            while (pending.Count != 0)
                foreach (string path in Directory.EnumerateFileSystemEntries(pending.Pop()))
                {
                    string link = new FileInfo(path).LinkTarget;
                    if (link != null) state.Add(path + " -> " + link);
                    else if (Directory.Exists(path)) { state.Add(path + "/"); pending.Push(path); }
                    else state.Add(path + ":" + Convert.ToBase64String(File.ReadAllBytes(path)));
                }
            return state.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        }

        [Theory]
        [InlineData("directory")]
        [InlineData("file")]
        [InlineData("rotation")]
        [InlineData("direct-directory")]
        [InlineData("direct-file")]
        [InlineData("nested-external")]
        [InlineData("descendant-new")]
        [InlineData("dangling-directory")]
        [InlineData("dangling-file")]
        [InlineData("cycle-directory")]
        [InlineData("cycle-file")]
        [InlineData("case-veto")]
        public void Reachable_authored_aliases_leave_all_fixture_bytes_names_and_parents_unchanged(string kind)
        {
            string allowed = Path.Combine(Cache, "unbound");
            string shared = Path.Combine(allowed, "shared");
            Directory.CreateDirectory(shared);
            string sentinel = Path.Combine(shared, "sentinel.cs");
            File.WriteAllText(sentinel, "authored target sentinel");
            string alias = Path.Combine(Project, "Assets/shared");
            string path = Path.Combine(alias, "sentinel.cs");
            if (kind == "file" || kind == "direct-file")
            {
                alias = Path.Combine(Project, "Assets/file.cs");
                File.CreateSymbolicLink(alias, sentinel);
                path = kind == "file" ? alias : sentinel;
            }
            else Directory.CreateSymbolicLink(alias, shared);
            if (kind == "direct-directory") path = sentinel;
            if (kind == "descendant-new") path = Path.Combine(alias, "new/sub/session.log");
            if (kind == "rotation")
            {
                path = Path.Combine(allowed, "session.log");
                File.WriteAllText(path, "previous session sentinel");
                File.CreateSymbolicLink(Path.Combine(allowed, "session-prev.log"), Path.Combine(alias, "sentinel.cs"));
            }
            if (kind == "nested-external")
            {
                string external = Path.Combine(root, "external");
                Directory.CreateDirectory(external);
                Directory.Delete(alias);
                Directory.CreateSymbolicLink(alias, external);
                Directory.CreateSymbolicLink(Path.Combine(external, "second"), shared);
                path = sentinel;
            }
            if (kind.StartsWith("dangling-", StringComparison.Ordinal))
            {
                string missing = Path.Combine(allowed, "missing");
                if (kind == "dangling-file") File.CreateSymbolicLink(Path.Combine(Project, "Packages/missing.cs"), missing);
                else Directory.CreateSymbolicLink(Path.Combine(Project, "Packages/missing"), missing);
                path = Path.Combine(allowed, "innocent/new/session.log");
            }
            if (kind == "cycle-directory") Directory.CreateSymbolicLink(Path.Combine(shared, "cycle"), shared);
            if (kind == "cycle-file")
            {
                string cycle = Path.Combine(Project, "ProjectSettings/cycle");
                File.CreateSymbolicLink(cycle, cycle);
            }
            if (kind.StartsWith("cycle-", StringComparison.Ordinal)) path = Path.Combine(allowed, "innocent/new/session.log");
            if (kind == "case-veto") path = Path.Combine(allowed, "SHARED/new/session.log");
            var before = FixtureState(root);
            Open(path);
            Assert.Equal("", ConsoleLog.Path);
            Assert.Equal(before, FixtureState(root));
        }

        [Fact]
        public void Duplicate_authored_aliases_share_traversal_and_allow_an_independent_cache_log()
        {
            string external = Path.Combine(root, "external");
            Directory.CreateDirectory(external);
            File.WriteAllText(Path.Combine(external, "sentinel.cs"), "authored sentinel");
            Directory.CreateSymbolicLink(Path.Combine(Project, "Assets/first"), external);
            Directory.CreateSymbolicLink(Path.Combine(Project, "Assets/second"), external);
            Open(Path.Combine(Cache, "unbound/independent/session.log"));
            Assert.Contains("log sentinel", File.ReadAllText(ConsoleLog.Path));
            Assert.Equal("authored sentinel", File.ReadAllText(Path.Combine(external, "sentinel.cs")));
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
