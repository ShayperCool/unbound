using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace FileUtilDeleteProbe
{
    /// <summary>
    /// FileUtil.DeleteFileOrDirectory on paths the probe makes in its own project copy and next to
    /// its output: a file, a directory tree, missing paths, null and empty, absolute paths inside
    /// and outside the project, a trailing slash, backslashes, "..", spaces, read-only files, an
    /// empty directory, symbolic links to a directory and to a file, a wildcard. Each row: the
    /// return value or exception, what exists afterwards, and the logs the call wrote.
    /// </summary>
    public class FileUtilDeleteBehaviour : MonoBehaviour
    {
        const string Variable = "FILEUTIL_DELETE_OUT";
        const string Root = "Assets/FileUtilDeleteScratch";
        readonly StringBuilder m_Out = new StringBuilder();
        readonly HashSet<string> m_Keys = new HashSet<string>();
        readonly List<string> m_Logs = new List<string>();
        string m_Outside;

        /// <summary>A symbolic link made by ln (portable across the Editor's runtime and Unbound's).</summary>
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
            new GameObject("probe").AddComponent<FileUtilDeleteBehaviour>();
        }

        void Put(string key, string value)
        {
            if (!m_Keys.Add(key)) throw new InvalidOperationException("duplicate key " + key);
            m_Out.Append("{\"k\":").Append(Json(key)).Append(",\"v\":").Append(Json(value)).Append("}\n");
        }

        static string Json(string text)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in text)
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

        static string Kind(string path) => File.Exists(path) ? "file" : Directory.Exists(path) ? "dir" : "none";

        /// <summary>Paths outside the project are written relative to the outside directory.</summary>
        string Show(string path) => m_Outside != null && path.StartsWith(m_Outside, StringComparison.Ordinal) ? "<outside>" + path.Substring(m_Outside.Length) : path;

        void Case(string name, string argument, params string[] check)
        {
            m_Logs.Clear();
            string result;
            try { result = FileUtil.DeleteFileOrDirectory(argument) ? "True" : "False"; }
            catch (Exception e) { result = e.GetType().FullName + ": " + e.Message; }
            Put(name + ".returns", result);
            var state = new List<string>();
            foreach (var path in check) state.Add(Show(path) + "=" + Kind(path));
            Put(name + ".after", string.Join(" ", state));
            Put(name + ".logs", string.Join(" | ", m_Logs));
        }

        void Start()
        {
            Application.logMessageReceived += (condition, stack, type) => m_Logs.Add(type + ": " + condition);
            string output = Environment.GetEnvironmentVariable(Variable);
            m_Outside = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output)), "fileutildelete-outside");
            if (Directory.Exists(m_Outside)) Directory.Delete(m_Outside, true);
            Directory.CreateDirectory(m_Outside);

            Make(Root + "/a.txt"); Make(Root + "/a.txt.meta");
            Case("file", Root + "/a.txt", Root + "/a.txt", Root + "/a.txt.meta");
            Make(Root + "/tree/x.txt"); Make(Root + "/tree/sub/y.txt"); Make(Root + "/tree.meta");
            Case("tree", Root + "/tree", Root + "/tree", Root + "/tree.meta");
            Make(Root + "/slash/x.txt");
            Case("trailingSlash", Root + "/slash/", Root + "/slash");
            Make(Root + "/back/x.txt");
            Case("backslashes", Root.Replace('/', '\\') + "\\back\\x.txt", Root + "/back/x.txt", Root + "/back");
            Case("missingFile", Root + "/missing.txt");
            Case("missingDirectory", Root + "/nowhere/missing.txt");
            Case("null", null);
            Case("empty", "", "Assets", "ProjectSettings");
            Make(m_Outside + "/o.txt"); Make(m_Outside + "/keep.txt");
            Case("absoluteOutside", m_Outside + "/o.txt", m_Outside + "/o.txt", m_Outside + "/keep.txt");
            Make(Root + "/abs.txt");
            Case("absoluteInside", Path.GetFullPath(Root + "/abs.txt"), Root + "/abs.txt");
            Make(Root + "/readonly.txt"); File.SetAttributes(Root + "/readonly.txt", FileAttributes.ReadOnly);
            Case("readOnlyFile", Root + "/readonly.txt", Root + "/readonly.txt");
            Make(Root + "/rodir/x.txt"); File.SetAttributes(Root + "/rodir/x.txt", FileAttributes.ReadOnly);
            Case("treeWithReadOnlyFile", Root + "/rodir", Root + "/rodir");
            Make(Root + "/emptydir/x.txt"); File.Delete(Root + "/emptydir/x.txt");
            Case("emptyDirectory", Root + "/emptydir", Root + "/emptydir");
            Make(Root + "/dots/x.txt");
            Case("dotDot", Root + "/dots/../dots/x.txt", Root + "/dots/x.txt", Root + "/dots");
            Make(Root + "/with space/x.txt");
            Case("space", Root + "/with space", Root + "/with space");
            Make(Root + "/wild/x.txt");
            Case("wildcard", Root + "/wild/*", Root + "/wild", Root + "/wild/x.txt");
            Make(m_Outside + "/target/kept.txt");
            Put("symlinkToDirectory.made", Symlink(m_Outside + "/target", Path.GetFullPath(Root) + "/dirlink"));
            Case("symlinkToDirectory", Root + "/dirlink", Root + "/dirlink", m_Outside + "/target", m_Outside + "/target/kept.txt");
            Make(m_Outside + "/target.txt");
            Put("symlinkToFile.made", Symlink(m_Outside + "/target.txt", Path.GetFullPath(Root) + "/filelink"));
            Case("symlinkToFile", Root + "/filelink", Root + "/filelink", m_Outside + "/target.txt");
            Case("again", Root + "/a.txt", Root + "/a.txt");
            Make(Root + "/last.txt");
            Case("directoryOfScratch", Root, Root, Root + "/last.txt");

            Directory.Delete(m_Outside, true);
            Put("complete", "true");
            File.WriteAllText(Path.Combine(output, "values.jsonl"), m_Out.ToString().Replace("{\"k\":\"complete\",\"v\":\"true\"}", "{\"k\":\"complete\",\"v\":true}"));
            EditorApplication.Exit(0);
        }
    }
}
