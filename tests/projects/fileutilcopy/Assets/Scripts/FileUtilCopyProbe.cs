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

        void Case(string name, Action call, params string[] check)
        {
            m_Logs.Clear();
            string result = "ok";
            try { call(); }
            catch (Exception e) { result = e.GetType().FullName + ": " + e.Message; }
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

            Directory.Delete(m_Outside, true);
            Put("complete", "true");
            File.WriteAllText(Path.Combine(output, "values.jsonl"), m_Out.ToString().Replace("{\"k\":\"complete\",\"v\":\"true\"}", "{\"k\":\"complete\",\"v\":true}"));
            EditorApplication.Exit(0);
        }
    }
}
