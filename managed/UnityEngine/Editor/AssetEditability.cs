using System;
using System.Collections.Generic;
using System.IO;
using Unbound.Runtime;
using UnityEditor.PackageManager;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEditor
{
    public enum StatusQueryOptions { ForceUpdate = 0, UseCachedIfPossible = 1, UseCachedAsync = 2 }

    public sealed partial class AssetDatabase
    {
        static bool s_EditSettingsLoaded;
        static bool s_ExternalVersionControl;

        static string EditObjectPath(Object assetObject)
        {
            MainThreadCheck.Check("GetAssetOrScenePath");
            if (assetObject == null) return "";
            string path = GetAssetPath(assetObject);
            if (path.Length != 0) return path;
            if (assetObject is Component component) assetObject = component.gameObject;
            return assetObject is GameObject gameObject ? gameObject.scene.path : "";
        }

        public static bool IsOpenForEdit(Object assetObject) => IsOpenForEdit(assetObject, out _, StatusQueryOptions.UseCachedIfPossible);
        public static bool IsOpenForEdit(string assetOrMetaFilePath) => IsOpenForEdit(assetOrMetaFilePath, out _, StatusQueryOptions.UseCachedIfPossible);
        public static bool IsOpenForEdit(Object assetObject, StatusQueryOptions statusOptions) => IsOpenForEdit(assetObject, out _, statusOptions);
        public static bool IsOpenForEdit(string assetOrMetaFilePath, StatusQueryOptions statusOptions) => IsOpenForEdit(assetOrMetaFilePath, out _, statusOptions);
        public static bool IsOpenForEdit(Object assetObject, out string message) => IsOpenForEdit(assetObject, out message, StatusQueryOptions.UseCachedIfPossible);
        public static bool IsOpenForEdit(string assetOrMetaFilePath, out string message) => IsOpenForEdit(assetOrMetaFilePath, out message, StatusQueryOptions.UseCachedIfPossible);
        public static bool IsOpenForEdit(Object assetObject, out string message, StatusQueryOptions statusOptions)
            => IsOpenForEdit(EditObjectPath(assetObject), out message, statusOptions);

        /// <summary>Open: not blank paths, paths outside the packages the project does not hold
        /// that the processors' IsOpenForEdit callbacks accept (tests/projects/openforedit,
        /// openforedithooks).</summary>
        public static bool IsOpenForEdit(string assetOrMetaFilePath, out string message, StatusQueryOptions statusOptions)
        {
            message = "";
            if (string.IsNullOrEmpty(assetOrMetaFilePath)) return true;
            MainThreadCheck.Check("TryGetAssetFolderInfo");
            if (IsImmutablePackagePath(assetOrMetaFilePath)) return false;
            if (!AssetModificationProcessorInternal.IsOpenForEdit(assetOrMetaFilePath, out message, statusOptions)) return false;
            return WithoutVersionControl(nameof(IsOpenForEdit));
        }

        public static void IsOpenForEdit(string[] assetOrMetaFilePaths, List<string> outNotEditablePaths,
            StatusQueryOptions statusQueryOptions = StatusQueryOptions.UseCachedIfPossible)
        {
            var paths = QueriedPaths(assetOrMetaFilePaths, outNotEditablePaths);
            if (paths.Length != 0 && AssetModificationProcessorInternal.IsOpenForEdit(paths, outNotEditablePaths, statusQueryOptions))
                WithoutVersionControl(nameof(IsOpenForEdit));
        }

        public static bool CanOpenForEdit(Object assetObject) => CanOpenForEdit(assetObject, out _, StatusQueryOptions.UseCachedIfPossible);
        public static bool CanOpenForEdit(string assetOrMetaFilePath) => CanOpenForEdit(assetOrMetaFilePath, out _, StatusQueryOptions.UseCachedIfPossible);
        public static bool CanOpenForEdit(Object assetObject, StatusQueryOptions statusOptions) => CanOpenForEdit(assetObject, out _, statusOptions);
        public static bool CanOpenForEdit(string assetOrMetaFilePath, StatusQueryOptions statusOptions) => CanOpenForEdit(assetOrMetaFilePath, out _, statusOptions);
        public static bool CanOpenForEdit(Object assetObject, out string message) => CanOpenForEdit(assetObject, out message, StatusQueryOptions.UseCachedIfPossible);
        public static bool CanOpenForEdit(string assetOrMetaFilePath, out string message) => CanOpenForEdit(assetOrMetaFilePath, out message, StatusQueryOptions.UseCachedIfPossible);
        public static bool CanOpenForEdit(Object assetObject, out string message, StatusQueryOptions statusOptions)
            => CanOpenForEdit(EditObjectPath(assetObject), out message, statusOptions);

        /// <summary>Open, or else accepted by the processors' CanOpenForEdit callbacks; the
        /// message stays empty (tests/projects/openforedithooks).</summary>
        public static bool CanOpenForEdit(string assetOrMetaFilePath, out string message, StatusQueryOptions statusOptions)
        {
            message = "";
            if (string.IsNullOrEmpty(assetOrMetaFilePath)) return true;
            MainThreadCheck.Check("TryGetAssetFolderInfo");
            if (IsImmutablePackagePath(assetOrMetaFilePath)) return false;
            if (!AssetModificationProcessorInternal.CanOpenForEdit(assetOrMetaFilePath, out message, statusOptions)) return false;
            return WithoutVersionControl(nameof(CanOpenForEdit));
        }

        /// <summary>Lists the paths that cannot be opened: the immutable ones, then those the
        /// IsOpenForEdit callbacks refuse and the CanOpenForEdit callbacks do not accept.</summary>
        public static void CanOpenForEdit(string[] assetOrMetaFilePaths, List<string> outNotEditablePaths,
            StatusQueryOptions statusQueryOptions = StatusQueryOptions.UseCachedIfPossible)
        {
            var paths = QueriedPaths(assetOrMetaFilePaths, outNotEditablePaths);
            if (paths.Length == 0) return;
            var notOpen = new List<string>();
            AssetModificationProcessorInternal.IsOpenForEdit(paths, notOpen, statusQueryOptions);
            if (notOpen.Count != 0 && !AssetModificationProcessorInternal.CanOpenForEdit(notOpen.ToArray(), outNotEditablePaths, statusQueryOptions))
                return;
            WithoutVersionControl(nameof(CanOpenForEdit));
        }

        public static bool IsMetaFileOpenForEdit(Object assetObject) => IsMetaFileOpenForEdit(assetObject, out _, StatusQueryOptions.UseCachedIfPossible);
        public static bool IsMetaFileOpenForEdit(Object assetObject, StatusQueryOptions statusOptions) => IsMetaFileOpenForEdit(assetObject, out _, statusOptions);
        public static bool IsMetaFileOpenForEdit(Object assetObject, out string message) => IsMetaFileOpenForEdit(assetObject, out message, StatusQueryOptions.UseCachedIfPossible);

        /// <summary>Whether the .meta file of the object's asset (or scene) is open; an object
        /// without one is.</summary>
        public static bool IsMetaFileOpenForEdit(Object assetObject, out string message, StatusQueryOptions statusOptions)
        {
            string path = EditObjectPath(assetObject);
            if (path.Length == 0)
            {
                message = "";
                return true;
            }
            return IsOpenForEdit(path + ".meta", out message, statusOptions);
        }

        /// <summary>One path; an empty one is passed to the callbacks as none.</summary>
        public static bool MakeEditable(string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            return MakeEditable(path.Length == 0 ? Array.Empty<string>() : new[] { path }, null, null);
        }

        /// <summary>The processors' MakeEditable callbacks get the paths as given (also an
        /// immutable package's: they are not filtered), the prompt ("" for none) and the list (a
        /// new one for none) until one refuses: false, with the list as they left it. Otherwise,
        /// without version control, every file is editable: the list is cleared and the answer is
        /// true (tests/projects/openforedit, openforedithooks). A read-only file stays read-only.</summary>
        public static bool MakeEditable(string[] paths, string prompt = null, List<string> outNotEditablePaths = null)
        {
            if (paths == null) throw new ArgumentNullException(nameof(paths));
            outNotEditablePaths ??= new List<string>();
            if (!AssetModificationProcessorInternal.MakeEditable(paths, prompt ?? "", outNotEditablePaths)) return false;
            WithoutVersionControl(nameof(MakeEditable));
            outNotEditablePaths.Clear();
            return true;
        }

        /// <summary>A list query's paths for the callbacks: blank ones skipped, those of an
        /// immutable package listed as not editable (the list cleared first).</summary>
        static string[] QueriedPaths(string[] assetOrMetaFilePaths, List<string> outNotEditablePaths)
        {
            if (assetOrMetaFilePaths == null) throw new ArgumentNullException(nameof(assetOrMetaFilePaths));
            if (outNotEditablePaths == null) throw new ArgumentNullException(nameof(outNotEditablePaths));
            outNotEditablePaths.Clear();
            var paths = new List<string>(assetOrMetaFilePaths.Length);
            foreach (string path in assetOrMetaFilePaths)
            {
                if (string.IsNullOrEmpty(path)) continue;
                MainThreadCheck.Check("TryGetAssetFolderInfo");
                if (IsImmutablePackagePath(path)) outNotEditablePaths.Add(path);
                else paths.Add(path);
            }
            return paths.ToArray();
        }

        /// <summary>Without a version control provider every file the callbacks accept is open;
        /// with one, the query is unsupported.</summary>
        static bool WithoutVersionControl(string member)
        {
            EnsureEditSettings();
            if (s_ExternalVersionControl)
                throw new NotSupportedException($"AssetDatabase.{member} with an active version control provider is not supported.");
            return true;
        }

        /// <summary>Whether the path is in a package the project does not hold (registry, built-in, Git).</summary>
        internal static bool IsImmutablePackagePath(string path)
        {
            const string prefix = "Packages/";
            if (!path.StartsWith(prefix, StringComparison.Ordinal)) return false;
            var relative = path.AsSpan(prefix.Length);
            var packages = UnityEditor.PackageManager.PackageInfo.Registered;
            for (int i = 0; i < packages.Count; i++)
            {
                var package = packages[i];
                string name = package.name;
                if (!relative.StartsWith(name.AsSpan(), StringComparison.Ordinal)
                    || (relative.Length != name.Length && relative[name.Length] != '/')) continue;
                return package.source != PackageSource.Embedded && package.source != PackageSource.Local;
            }
            return false;
        }

        static void EnsureEditSettings()
        {
            if (s_EditSettingsLoaded) return;
            s_EditSettingsLoaded = true;
            string path = Path.Combine(ProjectInfo.ProjectPath, "ProjectSettings", "VersionControlSettings.asset");
            if (!Native.IsReady || !File.Exists(path)) return;
            foreach (var document in Native.LoadDocuments(path))
            {
                string mode = document.Body.Str("m_Mode");
                s_ExternalVersionControl = !string.IsNullOrEmpty(mode) && mode != "Visible Meta Files"
                    && mode != "Hidden Meta Files" && mode != "Generic";
            }
        }
    }
}
