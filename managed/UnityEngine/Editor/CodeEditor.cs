using System.Collections.Generic;

namespace Unity.CodeEditor
{
    public partial interface IExternalCodeEditor
    {
        bool TryGetInstallationForPath(string editorPath, out CodeEditor.Installation installation);
        void OnGUI();
        void SyncIfNeeded(string[] addedFiles, string[] deletedFiles, string[] movedFiles, string[] movedFromFiles, string[] importedFiles);
        void SyncAll();
        void Initialize(string editorInstallationPath);
        bool OpenProject(string filePath = "", int line = -1, int column = -1);
        CodeEditor.Installation[] Installations { get; }
    }

    /// <summary>
    /// The external script editor: IDE packages (Rider, Visual Studio) register theirs. No editor is
    /// chosen outside the Editor's preferences, so the current one is the system default (none).
    /// </summary>
    public partial class CodeEditor
    {
        internal const string SystemDefaultPath = "";

        static readonly CodeEditor s_Editor = new CodeEditor();
        static readonly List<IExternalCodeEditor> s_Registered = new List<IExternalCodeEditor>();
        string m_Path = SystemDefaultPath;

        public CodeEditor() { }

        public partial struct Installation
        {
            public string Name;
            public string Path;
        }

        public static CodeEditor Editor => s_Editor;

        public static IExternalCodeEditor CurrentEditor => s_Editor.CurrentCodeEditor;

        public static string CurrentEditorInstallation => s_Editor.m_Path;

        public static string CurrentEditorPath => s_Editor.m_Path;

        public IExternalCodeEditor CurrentCodeEditor => GetCodeEditorForPath(m_Path);

        public Installation CurrentInstallation => GetInstallationForPath(m_Path);

        public Installation GetInstallationForPath(string editorPath)
        {
            foreach (var e in s_Registered)
                if (e.TryGetInstallationForPath(editorPath, out var installation)) return installation;
            return new Installation { Name = editorPath, Path = editorPath };
        }

        public IExternalCodeEditor GetCodeEditorForPath(string editorPath)
        {
            if (string.IsNullOrEmpty(editorPath)) return null;
            foreach (var e in s_Registered)
                if (e.TryGetInstallationForPath(editorPath, out _)) return e;
            return null;
        }

        public Dictionary<string, string> GetFoundScriptEditorPaths()
        {
            var paths = new Dictionary<string, string>();
            foreach (var e in s_Registered)
                foreach (var i in e.Installations ?? new Installation[0])
                    if (i.Path != null) paths[i.Path] = i.Name;
            return paths;
        }

        public static void SetExternalScriptEditor(string path) => s_Editor.SetCodeEditor(path);

        public void SetCodeEditor(string editorPath) => m_Path = editorPath ?? SystemDefaultPath;

        public static void Register(IExternalCodeEditor externalCodeEditor)
        {
            if (externalCodeEditor != null && !s_Registered.Contains(externalCodeEditor)) s_Registered.Add(externalCodeEditor);
        }

        public static void Unregister(IExternalCodeEditor externalCodeEditor) => s_Registered.Remove(externalCodeEditor);

        // Opening files is the Editor's business, not the game's.
        public static bool OSOpenFile(string appPath, string arguments) => false;

        public static string ParseArgument(string arguments, string path, int line, int column) =>
            (arguments ?? "").Replace("$(File)", path).Replace("$(Line)", line.ToString()).Replace("$(Column)", column.ToString());

        public static string QuoteForProcessStart(string argument) =>
            string.IsNullOrEmpty(argument) ? argument : "\"" + argument.Replace("\"", "\\\"") + "\"";
    }
}
