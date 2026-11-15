using System;
using System.IO;
using System.Xml;

namespace Unbound.Runtime
{
    /// <summary>The session's log file, Application.consoleLogPath: every message the game's log
    /// sees, as the console prints it. The command line's <c>-logFile</c> names it as for the
    /// Editor; otherwise it is <c>Library/Unbound/Editor.log</c> in the project, the previous
    /// session's moved to <c>Editor-prev.log</c> as the Editor does with its log.</summary>
    internal static class ConsoleLog
    {
        static readonly object s_Lock = new object();
        static string s_Path;
        static StreamWriter s_Writer;
        static bool s_Opened;

        internal static string Path
        {
            get
            {
                lock (s_Lock)
                {
                    Open();
                    return s_Path ?? "";
                }
            }
        }

        internal static string Choose(string[] args, string project)
        {
            for (int i = 0; i + 1 < args.Length; i++)
                if (string.Equals(args[i], "-logFile", StringComparison.OrdinalIgnoreCase))
                    return args[i + 1] == "-" ? null : System.IO.Path.GetFullPath(args[i + 1]);
            return string.IsNullOrEmpty(project) ? null : System.IO.Path.Combine(project, "Library", "Unbound", "Editor.log");
        }

        static void Open()
        {
            if (s_Opened) return;
            s_Opened = true;
            string path = null;
            try
            {
                path = Choose(EditorProcess.GetCommandLineArgs(), UnityEngine.ProjectInfo.ProjectPath);
                if (path == null) return;
                // Resolve and validate both destinations before even creating the parent directory.
                var cache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
                if (string.IsNullOrEmpty(cache)) cache = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
                var policy = new ConsoleLogPath.Policy(UnityEngine.ProjectInfo.ProjectPath, cache);
                string reportedPath = path;
                path = policy.Validate(path);
                string previous = policy.Validate(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path),
                    System.IO.Path.GetFileNameWithoutExtension(path) + "-prev" + System.IO.Path.GetExtension(path)));
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                if (File.Exists(path))
                    File.Move(path, previous, true);
                var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                s_Writer = new StreamWriter(stream) { AutoFlush = true };
                // Reporting preserves the requested spelling; all I/O uses checked physical paths.
                s_Path = reportedPath;
            }
            catch (Exception e)
            {
                Native.Log((int)UnityEngine.LogType.Warning, $"{Log.DiagnosticPrefix}cannot write the log file {path}: {e.Message}");
            }
        }

        /// <summary>Appends a message (opening the file with the session's first one).</summary>
        internal static void Append(string text)
        {
            lock (s_Lock)
            {
                Open();
                try { s_Writer?.WriteLine(text); }
                catch (Exception) { s_Writer = null; }
            }
        }

        /// <summary>A new session (tests): the next message or query chooses the file again.</summary>
        internal static void Reset()
        {
            lock (s_Lock)
            {
                s_Writer?.Dispose();
                s_Writer = null;
                s_Path = null;
                s_Opened = false;
            }
        }
    }

    /// <summary>The Editor licence of the machine, from the entitlement file the Unity licensing
    /// client keeps (<c>~/.config/unity3d/Unity/licenses</c> on Linux,
    /// <c>~/Library/Unity/licenses</c> on macOS).</summary>
    internal static class EditorLicense
    {
        static readonly Lazy<bool> s_Pro = new Lazy<bool>(Read);

        internal static bool HasPro => s_Pro.Value;

        static bool Read()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string file = OperatingSystem.IsMacOS()
                ? System.IO.Path.Combine(home, "Library", "Unity", "licenses", "UnityEntitlementLicense.xml")
                : System.IO.Path.Combine(home, ".config", "unity3d", "Unity", "licenses", "UnityEntitlementLicense.xml");
            try
            {
                var document = new XmlDocument { XmlResolver = null };
                document.Load(file);
                string paid = null;
                bool editor = false;
                foreach (XmlElement group in document.GetElementsByTagName("EntitlementGroup"))
                {
                    if (group.GetAttribute("Type") != "EDITOR") continue;
                    editor = true;
                    if (group.GetAttribute("Tag") != "UnityPersonal") paid ??= group.GetAttribute("Tag");
                }
                if (paid != null)
                {
                    Stubs.Ignored("Application.HasProLicense", $"answers true for the '{paid}' licence (only Unity Personal's false was measured)");
                    return true;
                }
                if (!editor) Stubs.Ignored("Application.HasProLicense", "answers false: the licence file names no Editor licence");
                return false;
            }
            catch (Exception e)
            {
                Stubs.Ignored("Application.HasProLicense", $"answers false: no Editor licence could be read ({e.Message})");
                return false;
            }
        }
    }
}
