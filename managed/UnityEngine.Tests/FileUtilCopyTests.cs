using System;
using System.IO;
using System.Linq;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// FileUtil's copy, move and replace calls and path helpers as the Unity 6000.0.71f1 Editor has
    /// them (tests/projects/fileutilcopy), in a scratch directory of the test's own.
    /// </summary>
    public sealed class FileUtilCopyTests : IDisposable
    {
        readonly string m_Root = Path.Combine(Path.GetTempPath(), "unbound-fileutilcopy-" + Guid.NewGuid().ToString("N"));

        public FileUtilCopyTests() => Directory.CreateDirectory(m_Root);

        public void Dispose()
        {
            if (!Directory.Exists(m_Root)) return;
            foreach (var file in Directory.GetFiles(m_Root, "*", SearchOption.AllDirectories))
                if (new FileInfo(file).LinkTarget == null) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(m_Root, true);
        }

        static int Hits() => Unbound.Runtime.Stubs.HitReport().FirstOrDefault(h => h.Key == FileUtil.UnsupportedCopyMember).Value;

        string At(string relative) => Path.Combine(m_Root, relative);

        string Make(string relative, string text = "x")
        {
            var path = At(relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text);
            return path;
        }

        // What a path is and holds: a link as a link, a file with its text.
        static string Tree(string path)
        {
            if (new FileInfo(path).LinkTarget != null) return "link";
            if (File.Exists(path)) return "file(" + File.ReadAllText(path) + ")";
            if (!Directory.Exists(path)) return "none";
            return "dir[" + string.Join(",", Directory.GetFileSystemEntries(path).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).Select(n => n + "=" + Tree(Path.Combine(path, n)))) + "]";
        }

        [Fact]
        public void AFileIsCopiedWithItsReadOnlyFlagAndWithoutItsMetaFile()
        {
            var source = Make("a.txt", "A");
            Make("a.txt.meta", "meta");
            FileUtil.CopyFileOrDirectory(source, At("b.txt"));
            Assert.Equal("file(A)", Tree(At("b.txt")));
            Assert.Equal("none", Tree(At("b.txt.meta")));
            Assert.Equal("file(A)", Tree(source));
            // Backslashes separate folders; ".." is walked.
            Directory.CreateDirectory(At("into"));
            FileUtil.CopyFileOrDirectory(m_Root + "\\a.txt", m_Root + "\\into\\..\\back.txt");
            Assert.Equal("file(A)", Tree(At("back.txt")));
            File.SetAttributes(source, FileAttributes.ReadOnly);
            FileUtil.CopyFileOrDirectoryFollowSymlinks(source, At("ro.txt"));
            Assert.True((File.GetAttributes(At("ro.txt")) & FileAttributes.ReadOnly) != 0);
        }

        [Fact]
        public void ACopyThatCannotBeMadeThrowsAndChangesNothing()
        {
            var source = Make("a.txt", "A");
            Make("b.txt", "B");
            Directory.CreateDirectory(At("into"));
            string Message(Action call) => Assert.Throws<IOException>(call).Message;
            Assert.Equal($"Failed to Copy File / Directory from '{source}' to '{At("b.txt")}': destination path already exists.", Message(() => FileUtil.CopyFileOrDirectory(source, At("b.txt"))));
            Assert.Equal("file(B)", Tree(At("b.txt")));
            Assert.EndsWith("destination path already exists.", Message(() => FileUtil.CopyFileOrDirectory(source, At("into"))));
            Assert.EndsWith("destination path already exists.", Message(() => FileUtil.CopyFileOrDirectory(source, source)));
            Assert.Equal("dir[]", Tree(At("into")));
            // A destination whose folder is missing, and a missing source.
            Assert.Equal($"Failed to Copy File / Directory from '{source}' to '{At("no/such/c.txt")}'.", Message(() => FileUtil.CopyFileOrDirectory(source, At("no/such/c.txt"))));
            Assert.Equal("none", Tree(At("no")));
            Assert.EndsWith($"to '{At("d.txt")}'.", Message(() => FileUtil.CopyFileOrDirectoryFollowSymlinks(At("missing.txt"), At("d.txt"))));
            Assert.Equal("none", Tree(At("d.txt")));

            Assert.Equal("source", Assert.Throws<ArgumentNullException>(() => FileUtil.CopyFileOrDirectory(null, "x")).ParamName);
            Assert.Equal("dest", Assert.Throws<ArgumentNullException>(() => FileUtil.MoveFileOrDirectory("x", null)).ParamName);
            // The Editor's argument order: the name is the message.
            var empty = Assert.Throws<ArgumentException>(() => FileUtil.CopyFileOrDirectory("", "x"));
            Assert.Equal("The source path cannot be empty.", empty.ParamName);
            Assert.StartsWith("source", empty.Message);
            Assert.Equal("The destination path cannot be empty.", Assert.Throws<ArgumentException>(() => FileUtil.MoveFileOrDirectory("x", "")).ParamName);
        }

        [Fact]
        public void ATreeIsCopiedWithEverythingInItAndLinksAreFollowed()
        {
            Make("tree/x.txt", "X");
            Make("tree/x.txt.meta", "m");
            Make("tree/sub/y.txt", "Y");
            Make("tree/.hidden", "H");
            Directory.CreateDirectory(At("tree/empty"));
            Make("tree.meta", "tm");
            Make("outside/target.txt", "T");
            Make("outside/dir/k.txt", "K");
            File.CreateSymbolicLink(At("tree/filelink"), At("outside/target.txt"));
            Directory.CreateSymbolicLink(At("tree/dirlink"), At("outside/dir"));
            FileUtil.CopyFileOrDirectory(At("tree") + "/", At("tree2") + "/");
            Assert.Equal("dir[.hidden=file(H),dirlink=dir[k.txt=file(K)],empty=dir[],filelink=file(T),sub=dir[y.txt=file(Y)],x.txt=file(X),x.txt.meta=file(m)]", Tree(At("tree2")));
            Assert.Equal("none", Tree(At("tree2.meta")));
            Assert.EndsWith("destination path already exists.", Assert.Throws<IOException>(() => FileUtil.CopyFileOrDirectory(At("tree"), At("tree2"))).Message);
            // A link itself as the source: what it points to is copied.
            FileUtil.CopyFileOrDirectory(At("tree/filelink"), At("copiedFile"));
            FileUtil.CopyFileOrDirectoryFollowSymlinks(At("tree/dirlink"), At("copiedDir"));
            Assert.Equal("file(T)", Tree(At("copiedFile")));
            Assert.Equal("dir[k.txt=file(K)]", Tree(At("copiedDir")));
        }

        // Unbound's choice (unmeasured): a link back into the tree ends the copy.
        [Fact]
        public void ALinkBackIntoTheCopiedTreeFailsTheCopy()
        {
            Make("loop/x.txt", "X");
            Directory.CreateSymbolicLink(At("loop/back"), At("loop"));
            Assert.Throws<IOException>(() => FileUtil.CopyFileOrDirectory(At("loop"), At("loop2")));
        }

        [Fact]
        public void AMoveRenamesTheEntryALinkAsALink()
        {
            var file = Make("m.txt", "M");
            Make("m.txt.meta", "mm");
            FileUtil.MoveFileOrDirectory(file, At("m2.txt"));
            Assert.Equal("none file(mm) file(M) none", string.Join(" ", new[] { "m.txt", "m.txt.meta", "m2.txt", "m2.txt.meta" }.Select(n => Tree(At(n)))));
            Make("mtree/x.txt", "X");
            Make("mtree/sub/y.txt", "Y");
            FileUtil.MoveFileOrDirectory(At("mtree"), At("mtree2"));
            Assert.Equal("none", Tree(At("mtree")));
            Assert.Equal("dir[sub=dir[y.txt=file(Y)],x.txt=file(X)]", Tree(At("mtree2")));
            Make("target.txt", "T");
            Make("targetdir/k.txt", "K");
            File.CreateSymbolicLink(At("filelink"), At("target.txt"));
            Directory.CreateSymbolicLink(At("dirlink"), At("targetdir"));
            FileUtil.MoveFileOrDirectory(At("filelink"), At("filelinkMoved"));
            FileUtil.MoveFileOrDirectory(At("dirlink"), At("dirlinkMoved"));
            Assert.Equal("none link file(T)", string.Join(" ", new[] { "filelink", "filelinkMoved", "target.txt" }.Select(n => Tree(At(n)))));
            Assert.Equal("none link dir[k.txt=file(K)]", string.Join(" ", new[] { "dirlink", "dirlinkMoved", "targetdir" }.Select(n => Tree(At(n)))));

            // The message of an existing destination says Copy; the others say Move.
            string Message(Action call) => Assert.Throws<IOException>(call).Message;
            Assert.Equal($"Failed to Copy File / Directory from '{At("target.txt")}' to '{At("m2.txt")}': destination path already exists.", Message(() => FileUtil.MoveFileOrDirectory(At("target.txt"), At("m2.txt"))));
            Assert.Equal($"Failed to Move File / Directory from '{At("missing.txt")}' to '{At("m3.txt")}'.", Message(() => FileUtil.MoveFileOrDirectory(At("missing.txt"), At("m3.txt"))));
            Assert.StartsWith("Failed to Move", Message(() => FileUtil.MoveFileOrDirectory(At("m2.txt"), At("nope/m4.txt"))));
            Assert.Equal("file(M) none", Tree(At("m2.txt")) + " " + Tree(At("nope")));
        }

        [Fact]
        public void AReplaceDeletesTheDestinationFirstAlsoWhenTheCopyFails()
        {
            Make("r1.txt", "one");
            Make("r2.txt", "two");
            FileUtil.ReplaceFile(At("r1.txt"), At("r2.txt"));
            FileUtil.ReplaceFile(At("r1.txt"), At("r3.txt"));
            Assert.Equal("file(one) file(one) file(one)", string.Join(" ", new[] { "r1.txt", "r2.txt", "r3.txt" }.Select(n => Tree(At(n)))));
            Assert.Throws<IOException>(() => FileUtil.ReplaceFile(At("missing.txt"), At("r2.txt")));
            Assert.Equal("none", Tree(At("r2.txt")));
            // A directory in a file's place is not deleted.
            Directory.CreateDirectory(At("into"));
            Assert.EndsWith("destination path already exists.", Assert.Throws<IOException>(() => FileUtil.ReplaceFile(At("r1.txt"), At("into"))).Message);

            Make("d1/x.txt", "1");
            Make("d2/old.txt", "2");
            FileUtil.ReplaceDirectory(At("d1"), At("d2"));
            FileUtil.ReplaceDirectory(At("d1"), At("d3"));
            Assert.Equal("dir[x.txt=file(1)] dir[x.txt=file(1)]", Tree(At("d2")) + " " + Tree(At("d3")));
            Assert.Throws<IOException>(() => FileUtil.ReplaceDirectory(At("missing"), At("d2")));
            Assert.Equal("none", Tree(At("d2")));
            Assert.EndsWith("destination path already exists.", Assert.Throws<IOException>(() => FileUtil.ReplaceDirectory(At("d1"), At("r1.txt"))).Message);
            Assert.Equal("file(one)", Tree(At("r1.txt")));
        }

        [Theory]
        [InlineData("a/b/c.txt", "c.txt", "a/b", "txt", "a/b/c")]
        [InlineData("a/b/", "", "a", "", "a/b/")]
        [InlineData("a/b", "b", "a", "", "a/b")]
        [InlineData("c", "c", "", "", "c")]
        [InlineData("", "", "", "", "")]
        [InlineData("/", "", "", "", "/")]
        [InlineData("/abs/x.y", "x.y", "/abs", "y", "/abs/x")]
        [InlineData("/x", "x", "/", "", "/x")]
        [InlineData("a\\b\\c.d.e", "a\\b\\c.d.e", "", "e", "a\\b\\c.d")]
        [InlineData(".hidden", ".hidden", "", "hidden", "")]
        [InlineData("a/.b", ".b", "a", "b", "a/")]
        [InlineData("a.b/c", "c", "a.b", "", "a.b/c")]
        [InlineData("a.b/", "", "", "", "a.b/")]
        [InlineData("a/b.c/", "", "a", "", "a/b.c/")]
        [InlineData("a/b//", "", "a", "", "a/b//")]
        [InlineData("//", "", "", "", "//")]
        [InlineData("name.", "name.", "", "", "name")]
        [InlineData("a//b.txt", "b.txt", "a", "txt", "a//b")]
        [InlineData("x.tar.gz", "x.tar.gz", "", "gz", "x.tar")]
        [InlineData("..", "..", "", "", ".")]
        [InlineData("a/..", "..", "a", "", "a/.")]
        [InlineData(null, "", "", "", "")]
        public void PathsAreTakenApartAtSlashesAndTheLastDot(string path, string last, string folder, string extension, string withoutExtension)
        {
            Assert.Equal(last, FileUtil.GetLastPathNameComponent(path));
            Assert.Equal(folder, FileUtil.DeleteLastPathNameComponent(path));
            Assert.Equal(extension, FileUtil.GetPathExtension(path));
            Assert.Equal(withoutExtension, FileUtil.GetPathWithoutExtension(path));
        }

        [Fact]
        public void ATemporaryPathIsInTempAndNamesNothing()
        {
            string first = FileUtil.GetUniqueTempPathInProject(), second = FileUtil.GetUniqueTempPathInProject();
            Assert.Matches("^Temp/UnityTempFile-[0-9a-f]{32}$", first);
            Assert.NotEqual(first, second);
            Assert.False(File.Exists(first) || Directory.Exists(first));
        }

        [Fact]
        public void LinksAreToldAndResolvedAndProjectPathsMadeRelative()
        {
            var target = Make("outside/target.txt", "T");
            var file = Make("project/Assets/a.txt", "A");
            File.CreateSymbolicLink(At("project/Assets/link"), target);
            Assert.True(FileUtil.IsSymlink(At("project/Assets/link")));
            Assert.False(FileUtil.IsSymlink(file));
            Assert.False(FileUtil.IsSymlink(At("missing")));
            Assert.False(FileUtil.IsSymlink(""));
            Assert.Equal(new FileInfo(target).FullName, FileUtil.ResolveSymlinks(At("project/Assets/link")));
            Assert.Equal(file, FileUtil.ResolveSymlinks(file));
            Assert.Equal("Assets/missing.txt", FileUtil.ResolveSymlinks("Assets/missing.txt"));
            Assert.Equal("", FileUtil.ResolveSymlinks(""));
            Assert.Equal(file, FileUtil.GetActualPathName(file));
            Assert.Equal(At("project/Assets/missing.txt"), FileUtil.GetActualPathName(At("project/Assets/missing.txt")));
            Assert.Equal("", FileUtil.GetActualPathName(""));

            var previous = ProjectInfo.ProjectPath;
            try
            {
                string project = At("project");
                ProjectInfo.ProjectPath = project;
                Assert.Equal("Assets/a.txt", FileUtil.GetProjectRelativePath(project + "/Assets/a.txt"));
                Assert.Equal("Assets", FileUtil.GetProjectRelativePath(project + "/Assets"));
                // Spelled as given: ".." is not walked.
                Assert.Equal("Assets/../ProjectSettings/y", FileUtil.GetProjectRelativePath(project + "/Assets/../ProjectSettings/y"));
                foreach (var other in new[] { project, project + "/", project + "\\Assets\\a.txt", At("outside/target.txt"), "Assets/a.txt", "a.txt", "", "/", project + "Other/x.txt", null })
                    Assert.Equal("", FileUtil.GetProjectRelativePath(other));
            }
            finally { ProjectInfo.ProjectPath = previous; }
        }

        // Unbound's own rule, as for DeleteFileOrDirectory: nothing is written onto the project
        // folder, Library or Unbound's folder, and none of them is moved away.
        [Fact]
        public void WhatDeletingRefusesIsRefusedAsADestinationAndAsAMovedSource()
        {
            string project = At("project");
            string view = Path.Combine(project, "Library", "Unbound", "ProjectView");
            Directory.CreateDirectory(view);
            var asset = Make("project/Assets/x.txt", "X");
            var cache = Make("project/Library/Unbound/cache.bin", "C");
            foreach (var name in new[] { "Assets", "Library" })
                Directory.CreateSymbolicLink(Path.Combine(view, name), Path.Combine(project, name));
            var previousDirectory = Directory.GetCurrentDirectory();
            var previousProject = ProjectInfo.ProjectPath;
            try
            {
                Directory.SetCurrentDirectory(view);
                ProjectInfo.ProjectPath = view;
                int hits = Hits();
                // Inside the project's folders through the view's links: as anywhere.
                FileUtil.CopyFileOrDirectory("Assets/x.txt", "Assets/y.txt");
                Assert.Equal("file(X)", Tree(Path.Combine(project, "Assets/y.txt")));
                Assert.Equal(hits, Hits());
                Assert.Throws<IOException>(() => FileUtil.CopyFileOrDirectory("Assets/x.txt", "Library/Unbound/stolen.txt"));
                Assert.Equal("none", Tree(Path.Combine(project, "Library/Unbound/stolen.txt")));
                Assert.Equal(hits + 1, Hits());
                Assert.Throws<IOException>(() => FileUtil.MoveFileOrDirectory("Library/Unbound/cache.bin", "Assets/cache.bin"));
                Assert.Equal("file(C)", Tree(cache));
                Assert.Equal("none", Tree(Path.Combine(project, "Assets/cache.bin")));
                Assert.Equal(hits + 2, Hits());
                Assert.Throws<IOException>(() => FileUtil.MoveFileOrDirectory("Library", "Assets/Library"));
                Assert.True(Directory.Exists(Path.Combine(project, "Library")));
                Assert.Equal("file(X)", Tree(asset));
            }
            finally
            {
                Directory.SetCurrentDirectory(previousDirectory);
                ProjectInfo.ProjectPath = previousProject;
            }
        }
    }
}
