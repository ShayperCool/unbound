using System;
using System.IO;
using System.Linq;
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

        static int Hits() => Unbound.Runtime.Stubs.HitReport().FirstOrDefault(h => h.Key == FileUtil.UnsupportedMember).Value;

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

        /// <summary>The host places Unbound's directory (--project-data): the rules follow the
        /// place it resolved, here one outside the project, and one in the project under
        /// another name.</summary>
        [Theory]
        [InlineData("data")]
        [InlineData("project/Build/UnboundData")]
        public void TheRulesFollowThePlaceTheHostResolvedForUnboundsDirectory(string place)
        {
            string project = Path.Combine(m_Root, "project");
            string data = Path.Combine(m_Root, place);
            string view = Path.Combine(data, "ProjectView");
            Directory.CreateDirectory(view);
            var asset = Make("project/Assets/x.txt");
            var build = Make("project/Build/b.txt");
            var cache = Make(place + "/cache.bin");
            // A folder of the default name is the project's own here.
            var named = Make("project/LibraryUnbound/theirs.txt");
            foreach (var name in new[] { "Assets", "Build", "LibraryUnbound" })
                Directory.CreateSymbolicLink(Path.Combine(view, name), Path.Combine(project, name));
            var previousDirectory = Directory.GetCurrentDirectory();
            var (previousProject, previousRoot, previousData) = (ProjectInfo.ProjectPath, ProjectInfo.ProjectRootPath, ProjectInfo.UnboundDirectoryPath);
            try
            {
                Directory.SetCurrentDirectory(view);
                ProjectInfo.ProjectPath = view;
                ProjectInfo.ProjectRootPath = project;
                ProjectInfo.UnboundDirectoryPath = data;
                Assert.Equal(Path.Combine(data, "Caching", "x"), ProjectInfo.UnboundPath("Caching", "x"));
                int hits = Hits();
                Assert.False(FileUtil.DeleteFileOrDirectory(cache));
                Assert.False(FileUtil.DeleteFileOrDirectory(data));
                Assert.False(FileUtil.DeleteFileOrDirectory(view));
                Assert.True(File.Exists(cache));
                Assert.Equal(hits + 3, Hits());
                if (place.StartsWith("project/", StringComparison.Ordinal))
                {
                    // In the project it is reached through the view's links too.
                    Assert.False(FileUtil.DeleteFileOrDirectory("Build/UnboundData/cache.bin"));
                    Assert.False(FileUtil.DeleteFileOrDirectory("Build"));
                    Assert.True(File.Exists(cache));
                    Assert.Equal(hits + 5, Hits());
                }
                // Controls: the project's files, and its folder of the default name, are its own.
                Assert.True(FileUtil.DeleteFileOrDirectory("Build/b.txt"));
                Assert.False(File.Exists(build));
                Assert.True(FileUtil.DeleteFileOrDirectory("LibraryUnbound/theirs.txt"));
                Assert.False(File.Exists(named));
                Assert.True(FileUtil.DeleteFileOrDirectory("Assets"));
                Assert.False(File.Exists(asset));
            }
            finally
            {
                Directory.SetCurrentDirectory(previousDirectory);
                (ProjectInfo.ProjectPath, ProjectInfo.ProjectRootPath, ProjectInfo.UnboundDirectoryPath) = (previousProject, previousRoot, previousData);
            }
        }

        [Fact]
        public void UnboundsDirectoryIsInTheProjectsFolderUnlessTheHostSaysOtherwise()
        {
            var (previousProject, previousData) = (ProjectInfo.ProjectPath, ProjectInfo.UnboundDirectoryPath);
            try
            {
                ProjectInfo.UnboundDirectoryPath = "";
                Assert.Equal(Path.Combine("/p", "LibraryUnbound"), ProjectInfo.UnboundDirectory("/p"));
                Assert.Equal(Path.Combine("/p", "LibraryUnbound"), ProjectInfo.UnboundDirectory("/p/"));
                // The project view is in the directory.
                Assert.Equal(Path.Combine("/p", "LibraryUnbound"), ProjectInfo.UnboundDirectory("/p/LibraryUnbound/ProjectView"));
                ProjectInfo.ProjectPath = "/elsewhere/data/ProjectView";
                ProjectInfo.UnboundDirectoryPath = "/elsewhere/data";
                Assert.Equal("/elsewhere/data", ProjectInfo.UnboundDirectory(ProjectInfo.ProjectPath));
                Assert.Equal(Path.Combine("/elsewhere/data", "Editor.log"), ProjectInfo.UnboundPath("Editor.log"));
                // Another project path is not the running project's.
                Assert.Equal(Path.Combine("/q", "LibraryUnbound"), ProjectInfo.UnboundDirectory("/q"));
            }
            finally
            {
                (ProjectInfo.ProjectPath, ProjectInfo.UnboundDirectoryPath) = (previousProject, previousData);
            }
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
            // The game runs in LibraryUnbound/ProjectView, whose top-level entries and Packages/<name>
            // are links to the project's: naming one deletes the project's entry, not only the link.
            string project = Path.Combine(m_Root, "project");
            string view = Path.Combine(project, "LibraryUnbound", "ProjectView");
            Directory.CreateDirectory(Path.Combine(view, "Packages"));
            var build = Make("project/Builds/x.txt");
            var log = Make("project/Logs/a.log");
            var asset = Make("project/Assets/x.txt");
            var manifest = Make("project/Packages/manifest.json");
            var embedded = Make("project/Packages/com.example.tool/package.json");
            var outside = Make("outside/kept.txt");
            var shared = Make("store/com.example.shared/package.json");
            var other = Make("project/Other/o.txt");
            Directory.CreateSymbolicLink(Path.Combine(project, "UserLink"), Path.GetDirectoryName(outside));
            var libraryFile = Make("project/Library/Other/l.txt");
            var unboundFile = Make("project/LibraryUnbound/cache.bin");
            foreach (var name in new[] { "Builds", "Logs", "Assets", "UserLink", "Other", "Library" })
                Directory.CreateSymbolicLink(Path.Combine(view, name), Path.Combine(project, name));
            Directory.CreateSymbolicLink(Path.Combine(view, "Packages", "com.example.tool"), Path.GetDirectoryName(embedded));
            Directory.CreateSymbolicLink(Path.Combine(view, "Packages", "com.example.shared"), Path.GetDirectoryName(shared));
            File.CreateSymbolicLink(Path.Combine(view, "Packages", "manifest.json"), manifest);
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
                // A package kept outside the project and the project folder itself are refused and
                // reported (unmeasured): nothing goes.
                int reported = Hits();
                Assert.False(FileUtil.DeleteFileOrDirectory("Packages/com.example.shared"));
                Assert.True(File.Exists(shared));
                Assert.True(Directory.Exists(Path.Combine(view, "Packages", "com.example.shared")));
                Assert.False(FileUtil.DeleteFileOrDirectory("Packages/com.example.shared/package.json"));
                Assert.True(File.Exists(shared));
                Assert.False(FileUtil.DeleteFileOrDirectory("."));
                Assert.True(File.Exists(other));
                // The Library folder and Unbound's own folder, also by the project's own path.
                Assert.False(FileUtil.DeleteFileOrDirectory("Library"));
                Assert.False(FileUtil.DeleteFileOrDirectory("LibraryUnbound/cache.bin"));
                Assert.False(FileUtil.DeleteFileOrDirectory(Path.Combine(project, "LibraryUnbound")));
                Assert.True(File.Exists(unboundFile));
                Assert.True(Directory.Exists(view));
                Assert.Equal(reported + 6, Hits());
                // Spelled in another case: on a volume that ignores case the same places are refused
                // (decided on where the path lands); elsewhere these name nothing. Nothing goes either way.
                bool ignoresCase = Directory.Exists(Path.Combine(project, "LIBRARY"));
                Assert.False(FileUtil.DeleteFileOrDirectory("packages/com.example.shared/package.json"));
                Assert.False(FileUtil.DeleteFileOrDirectory("library"));
                Assert.False(FileUtil.DeleteFileOrDirectory(Path.Combine(project, "libraryunbound")));
                Assert.True(File.Exists(shared));
                Assert.True(File.Exists(unboundFile));
                Assert.True(Directory.Exists(view));
                Assert.Equal(reported + 6 + (ignoresCase ? 3 : 0), Hits());
                // Into Unbound's folder by its name beside the view's links: refused, whatever it would reach.
                File.WriteAllText(asset, "again");
                int throughLibrary = Hits();
                Assert.False(FileUtil.DeleteFileOrDirectory("LibraryUnbound/ProjectView/Assets/x.txt"));
                Assert.False(FileUtil.DeleteFileOrDirectory("LibraryUnbound/ProjectView/Assets"));
                Assert.True(File.Exists(asset));
                Assert.True(Directory.Exists(Path.Combine(view, "Assets")));
                Assert.Equal(throughLibrary + 2, Hits());
                // Into Unbound's folder through a link of the project's own that leads there:
                // refused too, the view's own entries as well.
                Directory.CreateSymbolicLink(Path.Combine(project, "DataLink"), Path.Combine(project, "LibraryUnbound"));
                Directory.CreateSymbolicLink(Path.Combine(view, "DataLink"), Path.Combine(project, "DataLink"));
                Directory.CreateSymbolicLink(Path.Combine(project, "LibraryLink"), Path.Combine(project, "Library"));
                Directory.CreateSymbolicLink(Path.Combine(view, "LibraryLink"), Path.Combine(project, "LibraryLink"));
                File.WriteAllText(Path.Combine(view, "kept.txt"), "k");
                int throughProjectLink = Hits();
                Assert.False(FileUtil.DeleteFileOrDirectory("DataLink/ProjectView/Assets"));
                Assert.False(FileUtil.DeleteFileOrDirectory("DataLink/ProjectView/kept.txt"));
                Assert.False(FileUtil.DeleteFileOrDirectory("DataLink/cache.bin"));
                Assert.True(Directory.Exists(Path.Combine(view, "Assets")));
                Assert.True(File.Exists(Path.Combine(view, "kept.txt")));
                Assert.True(File.Exists(unboundFile));
                Assert.Equal(throughProjectLink + 3, Hits());
                // Control: other Library content through that link is the project's to delete.
                var linkedLibraryFile = Make("project/Library/Other/linked.txt");
                Assert.True(FileUtil.DeleteFileOrDirectory("LibraryLink/Other/linked.txt"));
                Assert.False(File.Exists(linkedLibraryFile));
                // Control: a file a script wrote in its working directory (the view) is its to delete.
                File.WriteAllText(Path.Combine(view, "written.txt"), "w");
                Assert.True(FileUtil.DeleteFileOrDirectory("written.txt"));
                Assert.False(File.Exists(Path.Combine(view, "written.txt")));
                // Control: other Library content is the project's to delete.
                Assert.True(FileUtil.DeleteFileOrDirectory("Library/Other/l.txt"));
                Assert.False(File.Exists(libraryFile));
                // The Packages folder: the project's goes, and the view's folder of links with it.
                Assert.True(FileUtil.DeleteFileOrDirectory("Packages"));
                Assert.False(Directory.Exists(Path.Combine(project, "Packages")));
                Assert.False(Directory.Exists(Path.Combine(view, "Packages")));
                Assert.True(File.Exists(shared));
                // Control: outside a project view the same name is only the path itself.
                ProjectInfo.ProjectPath = Path.Combine(m_Root, "elsewhere");
                Assert.True(FileUtil.DeleteFileOrDirectory("Other"));
                Assert.False(Directory.Exists(Path.Combine(view, "Other")));
                Assert.True(File.Exists(other));
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
