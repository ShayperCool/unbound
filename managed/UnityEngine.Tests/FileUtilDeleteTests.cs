using System;
using System.IO;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// FileUtil.DeleteFileOrDirectory as the Unity 6000.0.71f1 Editor does it
    /// (tests/projects/fileutildelete): exactly the named file or tree, in a scratch directory of the
    /// test's own; controls next to each deleted path stay.
    /// </summary>
    public sealed class FileUtilDeleteTests : IDisposable
    {
        readonly string m_Root = Path.Combine(Path.GetTempPath(), "unbound-fileutil-" + Guid.NewGuid().ToString("N"));

        public FileUtilDeleteTests() => Directory.CreateDirectory(m_Root);

        public void Dispose()
        {
            if (Directory.Exists(m_Root)) Directory.Delete(m_Root, true);
        }

        string Make(string relative, string text = "x")
        {
            var path = Path.Combine(m_Root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text);
            return path;
        }

        [Fact]
        public void AFileGoesAndItsMetaFileStays()
        {
            var file = Make("a.txt");
            var meta = Make("a.txt.meta");
            Assert.True(FileUtil.DeleteFileOrDirectory(file));
            Assert.False(File.Exists(file));
            Assert.True(File.Exists(meta));
            Assert.False(FileUtil.DeleteFileOrDirectory(file));
        }

        [Fact]
        public void ATreeGoesWithItsReadOnlyFilesAndTheSiblingStays()
        {
            Make("tree/x.txt");
            var readOnly = Make("tree/sub/y.txt");
            File.SetAttributes(readOnly, FileAttributes.ReadOnly);
            var sibling = Make("treeSibling/z.txt");
            Assert.True(FileUtil.DeleteFileOrDirectory(Path.Combine(m_Root, "tree") + "/"));
            Assert.False(Directory.Exists(Path.Combine(m_Root, "tree")));
            Assert.True(File.Exists(sibling));
        }

        [Fact]
        public void BackslashesSeparateFoldersAndAWildcardIsAName()
        {
            var file = Make("back/x.txt");
            Assert.True(FileUtil.DeleteFileOrDirectory(m_Root + "\\back\\x.txt"));
            Assert.False(File.Exists(file));
            var kept = Make("wild/x.txt");
            Assert.False(FileUtil.DeleteFileOrDirectory(Path.Combine(m_Root, "wild", "*")));
            Assert.True(File.Exists(kept));
        }

        [Fact]
        public void ALinkGoesAndWhatItPointsToStays()
        {
            var target = Make("target/kept.txt");
            var link = Path.Combine(m_Root, "dirlink");
            Directory.CreateSymbolicLink(link, Path.GetDirectoryName(target));
            var fileLink = Path.Combine(m_Root, "filelink");
            File.CreateSymbolicLink(fileLink, target);
            // A tree holding a link to outside it: the link goes, not what it points to.
            Directory.CreateDirectory(Path.Combine(m_Root, "holder"));
            Directory.CreateSymbolicLink(Path.Combine(m_Root, "holder", "inner"), Path.GetDirectoryName(target));
            Assert.True(FileUtil.DeleteFileOrDirectory(link));
            Assert.True(FileUtil.DeleteFileOrDirectory(fileLink));
            Assert.True(FileUtil.DeleteFileOrDirectory(Path.Combine(m_Root, "holder")));
            Assert.False(File.Exists(link) || Directory.Exists(link));
            Assert.False(File.Exists(fileLink));
            Assert.False(Directory.Exists(Path.Combine(m_Root, "holder")));
            Assert.True(File.Exists(target));
        }

        [Fact]
        public void ARelativePathIsTheWorkingDirectorys()
        {
            var previous = Directory.GetCurrentDirectory();
            var file = Make("cwd/rel.txt");
            try
            {
                Directory.SetCurrentDirectory(Path.Combine(m_Root, "cwd"));
                Assert.True(FileUtil.DeleteFileOrDirectory("rel.txt"));
                Assert.False(File.Exists(file));
                Assert.False(FileUtil.DeleteFileOrDirectory("rel.txt"));
            }
            finally { Directory.SetCurrentDirectory(previous); }
        }

        [Fact]
        public void APathNamingAProjectViewLinkDeletesTheProjectsOwnEntry()
        {
            // The game runs in Library/Unbound/ProjectView, whose top-level entries and Packages/<name>
            // are links to the project's: naming one deletes the project's entry, not only the link.
            string project = Path.Combine(m_Root, "project");
            string view = Path.Combine(project, "Library", "Unbound", "ProjectView");
            Directory.CreateDirectory(Path.Combine(view, "Packages"));
            var build = Make("project/Builds/x.txt");
            var log = Make("project/Logs/a.log");
            var asset = Make("project/Assets/x.txt");
            var manifest = Make("project/Packages/manifest.json");
            var embedded = Make("project/Packages/com.example.tool/package.json");
            var outside = Make("outside/kept.txt");
            Directory.CreateSymbolicLink(Path.Combine(project, "UserLink"), Path.GetDirectoryName(outside));
            foreach (var name in new[] { "Builds", "Logs", "Assets", "UserLink" })
                Directory.CreateSymbolicLink(Path.Combine(view, name), Path.Combine(project, name));
            Directory.CreateSymbolicLink(Path.Combine(view, "Packages", "com.example.tool"), Path.GetDirectoryName(embedded));
            var previousDirectory = Directory.GetCurrentDirectory();
            var previousProject = ProjectInfo.ProjectPath;
            try
            {
                Directory.SetCurrentDirectory(view);
                ProjectInfo.ProjectPath = view;
                Assert.True(FileUtil.DeleteFileOrDirectory("Builds"));
                Assert.False(Directory.Exists(Path.GetDirectoryName(build)));
                Assert.False(File.Exists(Path.Combine(view, "Builds")) || Directory.Exists(Path.Combine(view, "Builds")));
                Assert.True(FileUtil.DeleteFileOrDirectory(Path.Combine(view, "Logs")));
                Assert.False(Directory.Exists(Path.GetDirectoryName(log)));
                Assert.True(FileUtil.DeleteFileOrDirectory("Packages/com.example.tool"));
                Assert.False(Directory.Exists(Path.GetDirectoryName(embedded)));
                Assert.True(File.Exists(manifest));
                // Below a link the project's files are reached through it; the link stays.
                Assert.True(FileUtil.DeleteFileOrDirectory("Assets/x.txt"));
                Assert.False(File.Exists(asset));
                Assert.True(Directory.Exists(Path.Combine(view, "Assets")));
                // A link of the project's own at its top: it goes, what it points to stays.
                Assert.True(FileUtil.DeleteFileOrDirectory("UserLink"));
                Assert.False(Directory.Exists(Path.Combine(project, "UserLink")));
                Assert.True(File.Exists(outside));
                // Control: the same names outside a project view are only the paths themselves.
                ProjectInfo.ProjectPath = Path.Combine(m_Root, "elsewhere");
                Assert.False(FileUtil.DeleteFileOrDirectory("Builds"));
            }
            finally
            {
                Directory.SetCurrentDirectory(previousDirectory);
                ProjectInfo.ProjectPath = previousProject;
            }
        }

        [Fact]
        public void NullAndEmptyPathsThrow()
        {
            Assert.Equal("path", Assert.Throws<ArgumentNullException>(() => FileUtil.DeleteFileOrDirectory(null)).ParamName);
            var empty = Assert.Throws<ArgumentException>(() => FileUtil.DeleteFileOrDirectory(""));
            Assert.Equal("The path cannot be empty.", empty.ParamName);
            Assert.StartsWith("path", empty.Message);
        }
    }
}
