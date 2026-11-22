using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace FileUtilCopyProbe
{
    /// <summary>
    /// FileUtil's copy, move and replace calls on paths the probe makes in its own project copy and
    /// next to its output, and FileUtil's path helpers. Each call's row: its exception if any, the
    /// tree of what it was given and what it made afterwards, and the logs it wrote.
    /// </summary>
    public class FileUtilCopyBehaviour : MonoBehaviour
    {
        const string Variable = "FILEUTIL_COPY_OUT";
        const string Root = "Assets/FileUtilCopyScratch";
        readonly StringBuilder m_Out = new StringBuilder();
        readonly HashSet<string> m_Keys = new HashSet<string>();
        readonly List<string> m_Logs = new List<string>();
        string m_Outside;
        string m_Project;

        static string Symlink(string target, string link)
        {
            var start = new System.Diagnostics.ProcessStartInfo("ln", "-s \"" + target + "\" \"" + link + "\"") { UseShellExecute = false };
            using (var process = System.Diagnostics.Process.Start(start))
            {
                process.WaitForExit();
                return process.ExitCode.ToString();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
            new GameObject("probe").AddComponent<FileUtilCopyBehaviour>();
        }

        void Put(string key, string value)
        {
            if (!m_Keys.Add(key)) throw new InvalidOperationException("duplicate key " + key);
            m_Out.Append("{\"k\":").Append(Json(key)).Append(",\"v\":").Append(Json(value)).Append("}\n");
        }

        static string Json(string text)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in text ?? "<null>")
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        static void Make(string path, string text = "x")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllText(path, text);
        }

        static bool IsLink(string path)
        {
            try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
            catch (Exception) { return false; }
        }

        /// <summary>What a path is and, for a directory, everything in it: names in ordinal order,
        /// a file with its text, a link as a link.</summary>
        static string Tree(string path)
        {
            if (IsLink(path)) return "link";
            if (File.Exists(path)) return "file(" + File.ReadAllText(path) + ((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0 ? ",readonly" : "") + ")";
            if (!Directory.Exists(path)) return "none";
            var names = Directory.GetFileSystemEntries(path).Select(Path.GetFileName).ToList();
            names.Sort(StringComparer.Ordinal);
            return "dir[" + string.Join(",", names.Select(n => n + "=" + Tree(path + "/" + n))) + "]";
        }

        string Show(string text)
        {
            if (text == null) return "<null>";
            if (m_Outside != null) text = text.Replace(m_Outside, "<outside>");
            return text.Replace(m_Project, "<project>");
        }

        // The internal helpers throw what the runtime's file calls throw: their type is recorded,
        // not the class library's wording.
        bool m_TypesOnly;

        void Case(string name, Action call, params string[] check)
        {
            m_Logs.Clear();
            string result = "ok";
            try { call(); }
            catch (Exception e) { result = m_TypesOnly ? e.GetType().FullName : e.GetType().FullName + ": " + e.Message; }
            Put(name + ".result", Show(result));
            Put(name + ".after", string.Join(" ", check.Select(p => Show(p) + "=" + Tree(p))));
            Put(name + ".logs", Show(string.Join(" | ", m_Logs)));
        }

        static string Call(string method, params object[] arguments)
        {
            try
            {
                var m = typeof(FileUtil).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .First(x => x.Name == method && x.GetParameters().Length == arguments.Length);
                var value = m.Invoke(null, arguments);
                return value == null ? "<null>" : value is string s ? "'" + s + "'" : value.ToString();
            }
            catch (TargetInvocationException e) { return e.InnerException.GetType().Name; }
        }

        static object Invoke(string method, Type[] types, params object[] arguments)
        {
            var m = typeof(FileUtil).GetMethod(method, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, types, null);
            if (m == null) throw new MissingMethodException(method);
            try { return m.Invoke(null, arguments); }
            catch (TargetInvocationException e) { throw e.InnerException; }
        }

        static readonly Type S = typeof(string), B = typeof(bool);

        void MakeTree(string root)
        {
            Make(root + "/x.txt", "X"); Make(root + "/x.txt.meta", "m"); Make(root + "/sub/y.txt", "Y"); Make(root + "/sub/y.txt.meta", "n");
            Make(root + "/.hidden", "H"); Make(root + "/.git/config", "G"); Make(root + "/noext", "N");
            Directory.CreateDirectory(root + "/empty");
        }

        /// <summary>FileUtil's internal directory, file and text helpers, called by reflection.</summary>
        void Internal(string I)
        {
            m_TypesOnly = true;
            MakeTree(I + "/tree");
            Case("internal.copyRecursive", () => Invoke("CopyDirectoryRecursive", new[] { S, S }, I + "/tree", I + "/c1"), I + "/c1");
            Case("internal.copyRecursive.again", () => Invoke("CopyDirectoryRecursive", new[] { S, S }, I + "/tree", I + "/c1"), I + "/c1");
            Make(I + "/c2/x.txt", "old"); Make(I + "/c2/keep.txt", "K");
            Case("internal.copyRecursive.overwrite", () => Invoke("CopyDirectoryRecursive", new[] { S, S, B }, I + "/tree", I + "/c2", true), I + "/c2");
            Make(I + "/c2b/sub/y.txt", "old");
            Case("internal.copyRecursive.noOverwriteInSubfolder", () => Invoke("CopyDirectoryRecursive", new[] { S, S, B }, I + "/tree", I + "/c2b", false), I + "/c2b");
            Case("internal.copyRecursive.ignoreMeta", () => Invoke("CopyDirectoryRecursive", new[] { S, S, B, B }, I + "/tree", I + "/c3", false, true), I + "/c3");
            Case("internal.copyForPostprocess", () => Invoke("CopyDirectoryRecursiveForPostprocess", new[] { S, S, B }, I + "/tree", I + "/c4", false), I + "/c4");
            Case("internal.copyFiltered", () => Invoke("CopyDirectoryRecursiveFiltered", new[] { S, S, B, S }, I + "/tree", I + "/c5", false, "y\\.txt|noext"), I + "/c5");
            Case("internal.copyFiltered.badPattern", () => Invoke("CopyDirectoryRecursiveFiltered", new[] { S, S, B, S }, I + "/tree", I + "/c6", false, "("), I + "/c6");
            Case("internal.copyDirectory", () => Invoke("CopyDirectory", new[] { S, S, B }, I + "/tree", I + "/c7", false), I + "/c7");
            Case("internal.copyFilteredCallback", () => Invoke("CopyDirectoryFiltered", new[] { S, S, B, typeof(Func<string, bool>), B }, I + "/tree", I + "/c8", false, (Func<string, bool>)(f => !f.EndsWith("sub") && !f.EndsWith(".txt")), true), I + "/c8");
            Directory.CreateDirectory(I + "/onlyEmpty/inner");
            Case("internal.copyRecursive.emptySource", () => Invoke("CopyDirectoryRecursive", new[] { S, S }, I + "/onlyEmpty", I + "/c9"), I + "/c9");
            Case("internal.copyRecursive.missingSource", () => Invoke("CopyDirectoryRecursive", new[] { S, S }, I + "/missing", I + "/c10"), I + "/c10");

            Case("internal.directoryDelete.nonEmpty", () => Invoke("UnityDirectoryDelete", new[] { S }, I + "/c1"), I + "/c1/x.txt");
            Case("internal.directoryDelete.recursive", () => Invoke("UnityDirectoryDelete", new[] { S, B }, I + "/c1", true), I + "/c1");
            Case("internal.directoryDelete.missing", () => Invoke("UnityDirectoryDelete", new[] { S }, I + "/c1"), I + "/c1");
            Make(I + "/clean/x.txt", "X");
            Case("internal.createOrClean.existing", () => Invoke("CreateOrCleanDirectory", new[] { S }, I + "/clean"), I + "/clean");
            Case("internal.createOrClean.new", () => Invoke("CreateOrCleanDirectory", new[] { S }, I + "/fresh/deep"), I + "/fresh");
            Make(I + "/ro/a.txt", "A"); Make(I + "/ro/sub/b.txt", "B");
            File.SetAttributes(I + "/ro/a.txt", FileAttributes.ReadOnly); File.SetAttributes(I + "/ro/sub/b.txt", FileAttributes.ReadOnly);
            Case("internal.removeReadonly", () => Invoke("UnityDirectoryRemoveReadonlyAttribute", new[] { S }, I + "/ro"), I + "/ro");

            Make(I + "/f1.txt", "one"); Make(I + "/f2.txt", "two");
            Case("internal.fileCopy.existing", () => Invoke("UnityFileCopy", new[] { S, S }, I + "/f1.txt", I + "/f2.txt"), I + "/f2.txt");
            Case("internal.fileCopy.overwrite", () => Invoke("UnityFileCopy", new[] { S, S, B }, I + "/f1.txt", I + "/f2.txt", true), I + "/f2.txt");
            Case("internal.fileCopy.new", () => Invoke("UnityFileCopy", new[] { S, S }, I + "/f1.txt", I + "/f3.txt"), I + "/f3.txt");
            Case("internal.copyIfExists.missing", () => Invoke("CopyFileIfExists", new[] { S, S, B }, I + "/none.txt", I + "/f4.txt", false), I + "/f4.txt");
            Case("internal.copyIfExists.existingDestination", () => Invoke("CopyFileIfExists", new[] { S, S, B }, I + "/f1.txt", I + "/f3.txt", false), I + "/f3.txt");
            Case("internal.copyIfExists", () => Invoke("CopyFileIfExists", new[] { S, S, B }, I + "/f1.txt", I + "/f4.txt", false), I + "/f4.txt");
            Make(I + "/mv1.txt", "new"); Make(I + "/mv2.txt", "old");
            Case("internal.moveIfExists.replaces", () => Invoke("MoveFileIfExists", new[] { S, S }, I + "/mv1.txt", I + "/mv2.txt"), I + "/mv1.txt", I + "/mv2.txt");
            Case("internal.moveIfExists.missing", () => Invoke("MoveFileIfExists", new[] { S, S }, I + "/mv1.txt", I + "/mv2.txt"), I + "/mv2.txt");
            Make(I + "/mvsrc.txt", "S"); Directory.CreateDirectory(I + "/mvdir"); Make(I + "/mvdir/in.txt", "I");
            Case("internal.moveIfExists.ontoDirectory", () => Invoke("MoveFileIfExists", new[] { S, S }, I + "/mvsrc.txt", I + "/mvdir"), I + "/mvsrc.txt", I + "/mvdir");

            var read = new object[] { I + "/f1.txt", null, null };
            object readOk = Invoke("ReadFileContentBinary", new[] { S, typeof(byte[]).MakeByRefType(), S.MakeByRefType() }, read);
            var missing = new object[] { I + "/none.bin", null, null };
            object readMissing = Invoke("ReadFileContentBinary", new[] { S, typeof(byte[]).MakeByRefType(), S.MakeByRefType() }, missing);
            Put("internal.readBinary", $"{readOk}|bytes={((byte[])read[1])?.Length}|error={(read[2] == null ? "null" : "set")} ; {readMissing}|bytes={(missing[1] == null ? "null" : "set")}|error={(string.IsNullOrEmpty((string)missing[2]) ? "empty" : "set")}");

            Make(I + "/text.txt", "alpha beta\ngamma alpha\n");
            Case("internal.replaceText", () => Invoke("ReplaceText", new[] { S, typeof(string[]) }, I + "/text.txt", new[] { "alpha", "A", "gamma", "G" }), I + "/text.txt");
            Case("internal.replaceText.oddCount", () => Invoke("ReplaceText", new[] { S, typeof(string[]) }, I + "/text.txt", new[] { "A", "B", "G" }), I + "/text.txt");
            string regex = "";
            Case("internal.replaceTextRegex", () => regex = Invoke("ReplaceTextRegex", new[] { S, typeof(string[]) }, I + "/text.txt", new[] { "[BG]", "<$0>", "zzz", "y" }) + "," + Invoke("ReplaceTextRegex", new[] { S, typeof(string[]) }, I + "/text.txt", new[] { "zzz", "y" }), I + "/text.txt");
            Put("internal.replaceTextRegex.returns", regex);
            string append = "";
            Case("internal.appendTextAfter", () => append = Invoke("AppendTextAfter", new[] { S, S, S }, I + "/text.txt", "beta", "inserted") + "," + Invoke("AppendTextAfter", new[] { S, S, S }, I + "/text.txt", "nowhere", "never"), I + "/text.txt");
            Put("internal.appendTextAfter.returns", append);
            Make(I + "/noNewline.txt", "one\r\ntwo");
            Case("internal.appendTextAfter.lineEndings", () => Invoke("AppendTextAfter", new[] { S, S, S }, I + "/noNewline.txt", "two", "three"), I + "/noNewline.txt");

            string[] names = { "a/b/c.txt", "a//b/c.d.txt", "c", "a/b/", "", "/x.y", "a\\b.txt", ".hidden", "//a" };
            foreach (string method in new[] { "UnityGetFileName", "UnityGetFileNameWithoutExtension", "UnityGetDirectoryName", "NiceWinPath" })
                Put("internal." + method, string.Join(" | ", names.Select(n => "'" + n + "'>" + Call(method, n))) + " | null>" + Call(method, new object[] { null }));
            var prefixes = new[] { new[] { "/a/b/c", "/a/b" }, new[] { "a/b/c", "a/b" }, new[] { "/a/b/c", "/a/x" }, new[] { "/a/b", "/a/b" }, new[] { "/a/b", "/a/b/c/d" }, new[] { "a/b", "" }, new[] { "", "a" }, new[] { "/a/bc", "/a/b" }, new[] { "a/b/c", "x/b" } };
            Put("internal.RemovePathPrefix", string.Join(" | ", prefixes.Select(x => "'" + x[0] + "'-'" + x[1] + "'>" + Call("RemovePathPrefix", x[0], x[1]))));
            Put("internal.CombinePaths", string.Join(" | ", new[] { new[] { "a", "b", "c" }, new[] { "a/", "/b" }, new[] { "a" }, new string[0], new[] { "", "b" }, new[] { "a", null, "c" } }.Select(x => "'" + Call("CombinePaths", new object[] { x }) + "'")) + " | null>" + Call("CombinePaths", new object[] { null }));

            var all = ((List<string>)Invoke("GetAllFilesRecursive", new[] { S }, I + "/tree")).Select(x => x.Substring(I.Length + 1)).ToList();
            all.Sort(StringComparer.Ordinal);
            Put("internal.GetAllFilesRecursive", string.Join(",", all));
            var walked = new List<string>();
            Invoke("WalkFilesystemRecursively", new[] { S, typeof(Action<string>), typeof(Func<string, bool>) }, I + "/tree", (Action<string>)(f => walked.Add("f:" + f.Substring(I.Length + 1))), (Func<string, bool>)(d => { walked.Add("d:" + d.Substring(I.Length + 1)); return !d.EndsWith("sub"); }));
            walked.Sort(StringComparer.Ordinal);
            Put("internal.WalkFilesystemRecursively", string.Join(",", walked));
            Put("internal.GetDirectorySize", Call("GetDirectorySize", I + "/tree") + "|missing=" + Call("GetDirectorySize", I + "/missingDirectory"));
        }

        void Start()
        {
            Application.logMessageReceived += (condition, stack, type) => m_Logs.Add(type + ": " + condition);
            string output = Environment.GetEnvironmentVariable(Variable);
            m_Project = Path.GetDirectoryName(Path.GetFullPath("Assets"));
            m_Outside = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output)), "fileutilcopy-outside");
            if (Directory.Exists(m_Outside)) Directory.Delete(m_Outside, true);
            Directory.CreateDirectory(m_Outside);
            string R = Root;

            // Copy: files.
            Make(R + "/a.txt", "A"); Make(R + "/a.txt.meta", "meta");
            Case("copy.file", () => FileUtil.CopyFileOrDirectory(R + "/a.txt", R + "/b.txt"), R + "/a.txt", R + "/b.txt", R + "/b.txt.meta");
            Case("copy.file.destinationExists", () => FileUtil.CopyFileOrDirectory(R + "/a.txt", R + "/b.txt"), R + "/b.txt");
            Directory.CreateDirectory(R + "/into");
            Case("copy.file.destinationIsDirectory", () => FileUtil.CopyFileOrDirectory(R + "/a.txt", R + "/into"), R + "/into");
            Case("copy.file.missingParent", () => FileUtil.CopyFileOrDirectory(R + "/a.txt", R + "/no/such/c.txt"), R + "/no");
            Case("copy.file.missingSource", () => FileUtil.CopyFileOrDirectory(R + "/missing.txt", R + "/d.txt"), R + "/d.txt");
            Case("copy.file.same", () => FileUtil.CopyFileOrDirectory(R + "/a.txt", R + "/a.txt"), R + "/a.txt");
            Case("copy.nullSource", () => FileUtil.CopyFileOrDirectory(null, R + "/n.txt"));
            Case("copy.nullDestination", () => FileUtil.CopyFileOrDirectory(R + "/a.txt", null));
            Case("copy.emptySource", () => FileUtil.CopyFileOrDirectory("", R + "/n.txt"));
            Case("copy.emptyDestination", () => FileUtil.CopyFileOrDirectory(R + "/a.txt", ""));
            Case("copy.file.backslashes", () => FileUtil.CopyFileOrDirectory(R.Replace('/', '\\') + "\\a.txt", R.Replace('/', '\\') + "\\back.txt"), R + "/back.txt");
            Case("copy.file.absolute", () => FileUtil.CopyFileOrDirectory(Path.GetFullPath(R + "/a.txt"), m_Outside + "/abs.txt"), m_Outside + "/abs.txt");
            Case("copy.file.dotDot", () => FileUtil.CopyFileOrDirectory(R + "/into/../a.txt", R + "/into/../dots.txt"), R + "/dots.txt");
            Make(R + "/ro.txt", "R"); File.SetAttributes(R + "/ro.txt", FileAttributes.ReadOnly);
            Case("copy.file.readOnly", () => FileUtil.CopyFileOrDirectory(R + "/ro.txt", R + "/ro2.txt"), R + "/ro2.txt");
            File.SetAttributes(R + "/ro.txt", FileAttributes.Normal);
            if (File.Exists(R + "/ro2.txt")) File.SetAttributes(R + "/ro2.txt", FileAttributes.Normal);

            // Copy: directories.
            Make(R + "/tree/x.txt", "X"); Make(R + "/tree/x.txt.meta", "m"); Make(R + "/tree/sub/y.txt", "Y"); Make(R + "/tree/.hidden", "H");
            Directory.CreateDirectory(R + "/tree/empty");
            Make(R + "/tree.meta", "tm");
            Case("copy.tree", () => FileUtil.CopyFileOrDirectory(R + "/tree", R + "/tree2"), R + "/tree2", R + "/tree2.meta");
            Case("copy.tree.destinationExists", () => FileUtil.CopyFileOrDirectory(R + "/tree", R + "/tree2"), R + "/tree2");
            Case("copy.tree.trailingSlashes", () => FileUtil.CopyFileOrDirectory(R + "/tree/", R + "/tree3/"), R + "/tree3");
            Case("copy.tree.missingParent", () => FileUtil.CopyFileOrDirectory(R + "/tree", R + "/deep/er/tree"), R + "/deep");
            Case("copy.tree.outside", () => FileUtil.CopyFileOrDirectory(R + "/tree", m_Outside + "/tree"), m_Outside + "/tree");

            // Links: copied as they are, or what they point to.
            Make(m_Outside + "/target.txt", "T"); Make(m_Outside + "/targetdir/k.txt", "K");
            Put("links.made", Symlink(m_Outside + "/target.txt", Path.GetFullPath(R) + "/filelink") + Symlink(m_Outside + "/targetdir", Path.GetFullPath(R) + "/dirlink"));
            Make(R + "/linked/p.txt", "P");
            Put("links.madeInside", Symlink(m_Outside + "/target.txt", Path.GetFullPath(R) + "/linked/inner"));
            Case("copy.fileLink", () => FileUtil.CopyFileOrDirectory(R + "/filelink", R + "/filelink2"), R + "/filelink2");
            Case("copy.dirLink", () => FileUtil.CopyFileOrDirectory(R + "/dirlink", R + "/dirlink2"), R + "/dirlink2");
            Case("copy.treeWithLink", () => FileUtil.CopyFileOrDirectory(R + "/linked", R + "/linked2"), R + "/linked2");
            Case("follow.fileLink", () => FileUtil.CopyFileOrDirectoryFollowSymlinks(R + "/filelink", R + "/filelink3"), R + "/filelink3");
            Case("follow.dirLink", () => FileUtil.CopyFileOrDirectoryFollowSymlinks(R + "/dirlink", R + "/dirlink3"), R + "/dirlink3");
            Case("follow.treeWithLink", () => FileUtil.CopyFileOrDirectoryFollowSymlinks(R + "/linked", R + "/linked3"), R + "/linked3");
            Case("follow.file", () => FileUtil.CopyFileOrDirectoryFollowSymlinks(R + "/a.txt", R + "/followed.txt"), R + "/followed.txt");
            Case("follow.destinationExists", () => FileUtil.CopyFileOrDirectoryFollowSymlinks(R + "/a.txt", R + "/followed.txt"), R + "/followed.txt");
            Case("follow.missingSource", () => FileUtil.CopyFileOrDirectoryFollowSymlinks(R + "/missing.txt", R + "/f2.txt"), R + "/f2.txt");
            Case("follow.nullSource", () => FileUtil.CopyFileOrDirectoryFollowSymlinks(null, R + "/f2.txt"));

            // Move.
            Make(R + "/m.txt", "M"); Make(R + "/m.txt.meta", "mm");
            Case("move.file", () => FileUtil.MoveFileOrDirectory(R + "/m.txt", R + "/m2.txt"), R + "/m.txt", R + "/m.txt.meta", R + "/m2.txt", R + "/m2.txt.meta");
            Case("move.file.destinationExists", () => FileUtil.MoveFileOrDirectory(R + "/a.txt", R + "/m2.txt"), R + "/a.txt", R + "/m2.txt");
            Case("move.file.missingSource", () => FileUtil.MoveFileOrDirectory(R + "/missing.txt", R + "/m3.txt"), R + "/m3.txt");
            Case("move.file.missingParent", () => FileUtil.MoveFileOrDirectory(R + "/m2.txt", R + "/nope/m4.txt"), R + "/m2.txt", R + "/nope");
            Case("move.file.outside", () => FileUtil.MoveFileOrDirectory(R + "/m2.txt", m_Outside + "/moved.txt"), R + "/m2.txt", m_Outside + "/moved.txt");
            Make(R + "/mtree/x.txt", "X"); Make(R + "/mtree/sub/y.txt", "Y");
            Case("move.tree", () => FileUtil.MoveFileOrDirectory(R + "/mtree", R + "/mtree2"), R + "/mtree", R + "/mtree2");
            Case("move.tree.destinationExists", () => FileUtil.MoveFileOrDirectory(R + "/tree", R + "/mtree2"), R + "/tree", R + "/mtree2");
            Case("move.fileLink", () => FileUtil.MoveFileOrDirectory(R + "/filelink", R + "/filelinkMoved"), R + "/filelink", R + "/filelinkMoved", m_Outside + "/target.txt");
            Case("move.nullSource", () => FileUtil.MoveFileOrDirectory(null, R + "/n.txt"));
            Case("move.emptyDestination", () => FileUtil.MoveFileOrDirectory(R + "/a.txt", ""));

            // Replace.
            Make(R + "/r1.txt", "one"); Make(R + "/r2.txt", "two");
            Case("replaceFile.existing", () => FileUtil.ReplaceFile(R + "/r1.txt", R + "/r2.txt"), R + "/r1.txt", R + "/r2.txt");
            Case("replaceFile.new", () => FileUtil.ReplaceFile(R + "/r1.txt", R + "/r3.txt"), R + "/r3.txt");
            Case("replaceFile.missingSource", () => FileUtil.ReplaceFile(R + "/missing.txt", R + "/r2.txt"), R + "/r2.txt");
            Case("replaceFile.destinationIsDirectory", () => FileUtil.ReplaceFile(R + "/r1.txt", R + "/into"), R + "/into");
            Make(R + "/d1/x.txt", "1"); Make(R + "/d2/old.txt", "2");
            Case("replaceDirectory.existing", () => FileUtil.ReplaceDirectory(R + "/d1", R + "/d2"), R + "/d1", R + "/d2");
            Case("replaceDirectory.new", () => FileUtil.ReplaceDirectory(R + "/d1", R + "/d3"), R + "/d3");
            Case("replaceDirectory.missingSource", () => FileUtil.ReplaceDirectory(R + "/missing", R + "/d2"), R + "/d2");
            Case("replaceDirectory.destinationIsFile", () => FileUtil.ReplaceDirectory(R + "/d1", R + "/r1.txt"), R + "/r1.txt");

            // A temporary path in the project: its form, and nothing is made.
            string temp1 = FileUtil.GetUniqueTempPathInProject(), temp2 = FileUtil.GetUniqueTempPathInProject();
            string name = temp1.Substring(temp1.LastIndexOf('/') + 1);
            int dash = name.IndexOf('-');
            string tail = dash < 0 ? "" : name.Substring(dash + 1);
            Put("temp", $"folder={temp1.Substring(0, temp1.LastIndexOf('/') + 1)}|prefix={(dash < 0 ? name : name.Substring(0, dash + 1))}|tail={tail.Length}|hex={tail.All(Uri.IsHexDigit)}|lower={tail == tail.ToLowerInvariant()}|unique={temp1 != temp2}|made={Tree(temp1)}");

            // Path helpers.
            string[] paths =
            {
                "a/b/c.txt", "a/b/", "a/b", "c", "", "/", "/abs/x.y", "a\\b\\c.d.e", ".hidden", "a/.b", "a.b/c", "a.b/", "name.", "a//b.txt", "x.tar.gz", "..", "a/..", "/x", "a/b//", "//", "a/b.c/",
            };
            foreach (string method in new[] { "GetLastPathNameComponent", "DeleteLastPathNameComponent", "GetPathExtension", "GetPathWithoutExtension" })
                Put("path." + method, string.Join(" | ", paths.Select(p => "'" + p + "'>" + Call(method, p))) + " | null>" + Call(method, new object[] { null }));
            string project = m_Project;
            string[] relative =
            {
                project + "/Assets/x.txt", project + "/Assets", project, project + "/", project + "/Assets/../ProjectSettings/y",
                project + "\\Assets\\x.txt", m_Outside + "/o.txt", "Assets/x.txt", "x.txt", "", "/", project + "Other/x.txt",
            };
            Put("path.GetProjectRelativePath", string.Join(" | ", relative.Select(p => "'" + Show(p) + "'>" + Show(Call("GetProjectRelativePath", p)))) + " | null>" + Call("GetProjectRelativePath", new object[] { null }));
            string[] links = { R + "/dirlink", R + "/a.txt", R + "/tree", R + "/missing.txt", R + "/linked/inner", "" };
            Put("path.IsSymlink", string.Join(" | ", links.Select(p => "'" + p + "'>" + Call("IsSymlink", p))));
            Put("path.ResolveSymlinks", string.Join(" | ", links.Select(p => "'" + p + "'>" + Show(Call("ResolveSymlinks", p)))));
            Put("path.GetActualPathName", string.Join(" | ", new[] { R + "/a.txt", R + "/tree/sub/y.txt", R + "/missing.txt", R + "/dirlink/k.txt", "" }.Select(p => "'" + p + "'>" + Show(Call("GetActualPathName", p)))));

            Internal(R + "/internal");

            Directory.Delete(m_Outside, true);
            Put("complete", "true");
            File.WriteAllText(Path.Combine(output, "values.jsonl"), m_Out.ToString().Replace("{\"k\":\"complete\",\"v\":\"true\"}", "{\"k\":\"complete\",\"v\":true}"));
            EditorApplication.Exit(0);
        }
    }
}
