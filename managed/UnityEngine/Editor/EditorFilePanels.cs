using System;
using UnityEngine;

namespace UnityEditor
{
    /// <summary>
    /// Dialogs and file panels where nobody can answer them: Unbound shows no editor windows, so
    /// each is cancelled as the batch-mode Unity 6000.0.71f1 Editor cancels it, with an assertion
    /// that names it and an answer of "no" or no path (tests/projects/filepanels; the first line
    /// of the dialog's also in tests/projects/editorui).
    /// </summary>
    public partial class EditorUtility
    {
        /// <summary>The assertion of a cancelled dialog or panel.</summary>
        internal static void Cancelled(string kind, string text) =>
            Debug.LogAssertion($"Cancelling {kind}: {text}\nThis should not be called in batch mode.");

        static string CancelledPanel(string text)
        {
            Cancelled("FileDialog", text);
            return "";
        }

        public static bool DisplayDialog(string title, string message, string ok, [UnityEngine.Internal.DefaultValue("\"\"")] string cancel)
        {
            if (string.IsNullOrEmpty(ok) && string.IsNullOrEmpty(cancel)) throw new ArgumentException("Both 'ok' and 'cancel' strings are null or empty");
            if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(message)) throw new ArgumentException("Both 'title' and 'message' strings are null or empty");
            Cancelled("DisplayDialog", title + " " + message);
            return false;
        }

        public static bool DisplayDialog(string title, string message, string ok) => DisplayDialog(title, message, ok, "");

        /// <summary>A panel without a title is named "Open".</summary>
        public static string OpenFilePanel(string title, string directory, string extension) => CancelledPanel(title ?? "Open");

        public static string OpenFilePanelWithFilters(string title, string directory, string[] filters)
        {
            if (filters == null) throw Unbound.Runtime.MonoExceptions.NullReference();
            if (filters.Length % 2 != 0)
                throw new Exception("Filters must be declared in pairs { \"FileType\", \"FileExtension\" }; for instance { \"CSharp\", \"cs\" }. ");
            return CancelledPanel(title ?? "Open");
        }

        public static string OpenFolderPanel(string title, string folder, string defaultName) => CancelledPanel(title);

        public static string SaveFolderPanel(string title, string folder, string defaultName) => CancelledPanel(title);

        public static string SaveFilePanel(string title, string directory, string defaultName, string extension) => CancelledPanel(title);

        public static string SaveFilePanelInProject(string title, string defaultName, string extension, string message) =>
            SaveFilePanelInProject(title, defaultName, extension, message, "Assets");

        /// <summary>The panel that asks where in the project to save: its assertion has the
        /// message after the title.</summary>
        public static string SaveFilePanelInProject(string title, string defaultName, string extension, string message, string path) =>
            CancelledPanel(title + " " + message);
    }
}
