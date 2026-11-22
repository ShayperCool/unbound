using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// FileUtil's internal directory, file and text helpers as the Unity 6000.0.71f1 Editor has them
    /// (tests/projects/fileutilcopy internal.*), in a scratch directory of the test's own.
    /// </summary>
    public sealed class FileUtilInternalTests : IDisposable
    {
        readonly string m_Root = Path.Combine(Path.GetTempPath(), "unbound-fileutilinternal-" + Guid.NewGuid().ToString("N"));

        public FileUtilInternalTests() => Directory.CreateDirectory(m_Root);

        public void Dispose()
        {
            if (!Directory.Exists(m_Root)) return;
            foreach (var file in Directory.GetFiles(m_Root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(m_Root, true);
        }

        string At(string relative) => Path.Combine(m_Root, relative);

        string Make(string relative, string text = "x")
        {
            var path = At(relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text);
            return path;
        }

        static string Tree(string path)
        {
            if (File.Exists(path)) return "file(" + File.ReadAllText(path) + ")";
            if (!Directory.Exists(path)) return "none";
            return "dir[" + string.Join(",", Directory.GetFileSystemEntries(path).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).Select(n => n + "=" + Tree(Path.Combine(path, n)))) + "]";
        }

        string MakeTree()
        {
            Make("tree/x.txt", "X");
            Make("tree/x.txt.meta", "m");
            Make("tree/sub/y.txt", "Y");
            Make("tree/sub/y.txt.meta", "n");
            Make("tree/.hidden", "H");
            Make("tree/.git/config", "G");
            Make("tree/noext", "N");
            Directory.CreateDirectory(At("tree/empty"));
            return At("tree");
        }

        const string Whole = "dir[.git=dir[config=file(G)],.hidden=file(H),noext=file(N),sub=dir[y.txt=file(Y),y.txt.meta=file(n)],x.txt=file(X),x.txt.meta=file(m)]";

        [Fact]
        public void ADirectoryIsCopiedFileByFileAndADirectoryWithoutFilesIsNotMade()
        {
            string tree = MakeTree();
            // The empty directory has no file to be made for.
            FileUtil.CopyDirectoryRecursive(tree, At("c1"));
            Assert.Equal(Whole, Tree(At("c1")));
            // Without overwriting the first existing file stops the copy.
            Assert.Throws<IOException>(() => FileUtil.CopyDirectoryRecursive(tree, At("c1")));
            // Into a directory that is there, existing files are overwritten and others kept.
            Make("c2/x.txt", "old");
            Make("c2/keep.txt", "K");
            FileUtil.CopyDirectoryRecursive(tree, At("c2"), true);
            Assert.Equal(Whole.Replace(",noext=", ",keep.txt=file(K),noext="), Tree(At("c2")));
            Directory.CreateDirectory(At("onlyEmpty/inner"));
            FileUtil.CopyDirectoryRecursive(At("onlyEmpty"), At("c3"));
            Assert.Equal("none", Tree(At("c3")));
            Assert.Throws<DirectoryNotFoundException>(() => FileUtil.CopyDirectoryRecursive(At("missing"), At("c4")));
            Assert.Equal("none", Tree(At("c4")));
        }

        [Fact]
        public void ACopyThatStopsHasCopiedWhatComesBeforeInNameOrder()
        {
            string tree = MakeTree();
            Make("c/sub/y.txt", "old");
            Assert.Throws<IOException>(() => FileUtil.CopyDirectoryRecursive(tree, At("c"), false));
            // The files of the directory, then .git, then sub up to the file that was there.
            Assert.Equal("dir[.git=dir[config=file(G)],.hidden=file(H),noext=file(N),sub=dir[y.txt=file(old)],x.txt=file(X),x.txt.meta=file(m)]", Tree(At("c")));
        }

        [Fact]
        public void FiltersLeaveOutWhatTheirPatternOrCallbackNames()
        {
            string tree = MakeTree();
            FileUtil.CopyDirectoryRecursive(tree, At("noMeta"), false, true);
            Assert.Equal("dir[.git=dir[config=file(G)],.hidden=file(H),noext=file(N),sub=dir[y.txt=file(Y)],x.txt=file(X)]", Tree(At("noMeta")));
            // For a build: neither .meta files nor anything whose name starts with a dot.
            FileUtil.CopyDirectoryRecursiveForPostprocess(tree, At("post"), false);
            Assert.Equal("dir[noext=file(N),sub=dir[y.txt=file(Y)],x.txt=file(X)]", Tree(At("post")));
            FileUtil.CopyDirectoryRecursiveFiltered(tree, At("filtered"), false, "y\\.txt|noext");
            Assert.Equal("dir[.git=dir[config=file(G)],.hidden=file(H),x.txt=file(X),x.txt.meta=file(m)]", Tree(At("filtered")));
            FileUtil.CopyDirectory(tree, At("flat"), false);
            Assert.Equal("dir[.hidden=file(H),noext=file(N),x.txt=file(X),x.txt.meta=file(m)]", Tree(At("flat")));
            FileUtil.CopyDirectoryFiltered(tree, At("called"), false, f => !f.EndsWith("sub") && !f.EndsWith(".txt"), true);
            Assert.Equal("dir[.git=dir[config=file(G)],.hidden=file(H),noext=file(N),x.txt.meta=file(m)]", Tree(At("called")));

            // A pattern that is none: a log, and nothing copied.
            var logs = new List<string>();
            var previous = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = new Capture(logs);
            try
            {
                FileUtil.CopyDirectoryRecursiveFiltered(tree, At("bad"), false, "(");
            }
            finally { Debug.unityLogger.logHandler = previous; }
            Assert.Equal(new[] { "Log:CopyDirectoryRecursive: Pattern '(' is not a correct Regular Expression. Not excluding any files." }, logs);
            Assert.Equal("none", Tree(At("bad")));
        }

        sealed class Capture : ILogHandler
        {
            readonly List<string> m_Logs;
            internal Capture(List<string> logs) => m_Logs = logs;
            public void LogFormat(LogType logType, Object context, string format, params object[] args) =>
                m_Logs.Add(logType + ":" + (args != null && args.Length > 0 ? string.Format(format, args) : format));
            public void LogException(Exception exception, Object context) => m_Logs.Add("Exception:" + exception.Message);
        }

        [Fact]
        public void DirectoriesAreDeletedCleanedAndMadeWritable()
        {
            MakeTree();
            Assert.Throws<IOException>(() => FileUtil.UnityDirectoryDelete(At("tree")));
            Assert.True(File.Exists(At("tree/x.txt")));
            FileUtil.UnityDirectoryDelete(At("tree"), true);
            Assert.Equal("none", Tree(At("tree")));
            Assert.Throws<DirectoryNotFoundException>(() => FileUtil.UnityDirectoryDelete(At("tree")));
            Make("clean/x.txt");
            FileUtil.CreateOrCleanDirectory(At("clean"));
            FileUtil.CreateOrCleanDirectory(At("fresh/deep"));
            Assert.Equal("dir[] dir[deep=dir[]]", Tree(At("clean")) + " " + Tree(At("fresh")));
            var a = Make("ro/a.txt");
            var b = Make("ro/sub/b.txt");
            File.SetAttributes(a, FileAttributes.ReadOnly);
            File.SetAttributes(b, FileAttributes.ReadOnly);
            FileUtil.UnityDirectoryRemoveReadonlyAttribute(At("ro"));
            Assert.Equal(0, (int)((File.GetAttributes(a) | File.GetAttributes(b)) & FileAttributes.ReadOnly));
        }

        [Fact]
        public void FilesAreCopiedAndMovedWhenTheyExist()
        {
            Make("f1.txt", "one");
            Make("f2.txt", "two");
            Assert.Throws<IOException>(() => FileUtil.UnityFileCopy(At("f1.txt"), At("f2.txt")));
            Assert.Equal("file(two)", Tree(At("f2.txt")));
            FileUtil.UnityFileCopy(At("f1.txt"), At("f2.txt"), true);
            FileUtil.UnityFileCopy(At("f1.txt"), At("f3.txt"));
            FileUtil.CopyFileIfExists(At("none.txt"), At("f4.txt"), false);
            Assert.Equal("file(one) file(one) none", Tree(At("f2.txt")) + " " + Tree(At("f3.txt")) + " " + Tree(At("f4.txt")));
            Assert.Throws<IOException>(() => FileUtil.CopyFileIfExists(At("f1.txt"), At("f3.txt"), false));

            // A move replaces the destination, a directory too; a missing source leaves it.
            Make("mv1.txt", "new");
            Make("mv2.txt", "old");
            FileUtil.MoveFileIfExists(At("mv1.txt"), At("mv2.txt"));
            FileUtil.MoveFileIfExists(At("mv1.txt"), At("mv2.txt"));
            Assert.Equal("none file(new)", Tree(At("mv1.txt")) + " " + Tree(At("mv2.txt")));
            Make("mvdir/in.txt");
            FileUtil.MoveFileIfExists(At("mv2.txt"), At("mvdir"));
            Assert.Equal("file(new)", Tree(At("mvdir")));

            Assert.True(FileUtil.ReadFileContentBinary(At("f1.txt"), out var bytes, out var error));
            Assert.Equal(3, bytes.Length);
            Assert.Null(error);
            Assert.False(FileUtil.ReadFileContentBinary(At("none.bin"), out bytes, out error));
            Assert.Null(bytes);
            Assert.False(string.IsNullOrEmpty(error));
        }

        [Fact]
        public void TextIsReplacedLineByLineAndWrittenWithNewlines()
        {
            var text = Make("text.txt", "alpha beta\ngamma alpha\n");
            FileUtil.ReplaceText(text, "alpha", "A", "gamma", "G");
            Assert.Equal("A beta\nG A\n", File.ReadAllText(text));
            // A text to find without one to put.
            Assert.Throws<IndexOutOfRangeException>(() => FileUtil.ReplaceText(text, "A", "B", "G"));
            Assert.Equal("A beta\nG A\n", File.ReadAllText(text));
            Assert.True(FileUtil.ReplaceTextRegex(text, "[BG]", "<$0>", "zzz", "y"));
            Assert.False(FileUtil.ReplaceTextRegex(text, "zzz", "y"));
            Assert.Equal("A beta\n<G> A\n", File.ReadAllText(text));
            Assert.True(FileUtil.AppendTextAfter(text, "beta", "inserted"));
            Assert.False(FileUtil.AppendTextAfter(text, "nowhere", "never"));
            Assert.Equal("A beta\ninserted\n<G> A\n", File.ReadAllText(text));
            // Other line endings and a missing last one become newlines.
            var mixed = Make("mixed.txt", "one\r\ntwo");
            FileUtil.AppendTextAfter(mixed, "two", "three");
            Assert.Equal("one\ntwo\nthree\n", File.ReadAllText(mixed));
        }

        [Theory]
        [InlineData("a/b/c.txt", "c.txt", "c", "a/b")]
        [InlineData("a//b/c.d.txt", "c.d.txt", "c.d", "a//b")]
        [InlineData("c", "c", "c", "")]
        [InlineData("a/b/", "", "", "a/b")]
        [InlineData("/x.y", "x.y", "x", "/")]
        [InlineData("a\\b.txt", "a\\b.txt", "a\\b", "")]
        [InlineData(".hidden", ".hidden", "", "")]
        [InlineData("//a", "//a", "//a", "")]
        public void PathCallsKeepDoubledSlashes(string path, string name, string withoutExtension, string directory)
        {
            Assert.Equal(name, FileUtil.UnityGetFileName(path));
            Assert.Equal(withoutExtension, FileUtil.UnityGetFileNameWithoutExtension(path));
            Assert.Equal(directory, FileUtil.UnityGetDirectoryName(path));
            Assert.Equal(path, FileUtil.NiceWinPath(path));
        }

        [Fact]
        public void PathsAreJoinedAndTheirSharedFoldersRemoved()
        {
            Assert.Equal("", FileUtil.UnityGetFileName(""));
            Assert.Equal("", FileUtil.UnityGetFileNameWithoutExtension(""));
            Assert.Throws<ArgumentException>(() => FileUtil.UnityGetDirectoryName(""));
            Assert.Throws<NullReferenceException>(() => FileUtil.UnityGetFileName(null));
            Assert.Throws<NullReferenceException>(() => FileUtil.UnityGetDirectoryName(null));
            Assert.Null(FileUtil.NiceWinPath(null));

            var prefixes = new[] { ("/a/b/c", "/a/b", "c"), ("a/b/c", "a/b", "c"), ("/a/b/c", "/a/x", "b/c"), ("/a/b", "/a/b", ""), ("/a/b", "/a/b/c/d", ""), ("a/b", "", "a/b"), ("", "a", ""), ("/a/bc", "/a/b", "bc"), ("a/b/c", "x/b", "a/b/c") };
            foreach (var (full, prefix, expected) in prefixes) Assert.Equal(expected, FileUtil.RemovePathPrefix(full, prefix));
            Assert.Equal("a/b/c", FileUtil.CombinePaths("a", "b", "c"));
            Assert.Equal("a///b", FileUtil.CombinePaths("a/", "/b"));
            Assert.Equal("a//c", FileUtil.CombinePaths("a", null, "c"));
            Assert.Equal("", FileUtil.CombinePaths());
            Assert.Equal("", FileUtil.CombinePaths(null));
        }

        [Fact]
        public void TreesAreWalkedAndSizedWithTheirHiddenAndExtensionlessFiles()
        {
            string tree = MakeTree();
            var all = FileUtil.GetAllFilesRecursive(tree).Select(f => f.Substring(tree.Length + 1)).ToList();
            // The files of a directory in name order, then each subdirectory's.
            Assert.Equal(new[] { ".hidden", "noext", "x.txt", "x.txt.meta", ".git/config", "sub/y.txt", "sub/y.txt.meta" }, all);
            var walked = new List<string>();
            FileUtil.WalkFilesystemRecursively(tree, f => walked.Add("f:" + f.Substring(tree.Length + 1)), d => { walked.Add("d:" + d.Substring(tree.Length + 1)); return !d.EndsWith("sub"); });
            Assert.Equal(new[] { "f:.hidden", "f:noext", "f:x.txt", "f:x.txt.meta", "d:.git", "f:.git/config", "d:empty", "d:sub" }, walked);
            Assert.Equal(7L, FileUtil.GetDirectorySize(tree));
            Assert.Throws<DirectoryNotFoundException>(() => FileUtil.GetDirectorySize(At("missing")));
        }
    }
}
