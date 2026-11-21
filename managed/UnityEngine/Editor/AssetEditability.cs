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

        public static bool IsOpenForEdit(string assetOrMetaFilePath, out string message, StatusQueryOptions statusOptions)
        {
            message = "";
            if (string.IsNullOrEmpty(assetOrMetaFilePath)) return true;
            MainThreadCheck.Check("TryGetAssetFolderInfo");
            if (IsImmutablePackagePath(assetOrMetaFilePath)) return false;
            EnsureEditSettings();
            if (s_ExternalVersionControl)
                throw new NotSupportedException("AssetDatabase.IsOpenForEdit with an active version control provider is not supported.");
            return true;
        }

        public static void IsOpenForEdit(string[] assetOrMetaFilePaths, List<string> outNotEditablePaths,
            StatusQueryOptions statusQueryOptions = StatusQueryOptions.UseCachedIfPossible)
        {
            if (assetOrMetaFilePaths == null) throw new ArgumentNullException(nameof(assetOrMetaFilePaths));
            if (outNotEditablePaths == null) throw new ArgumentNullException(nameof(outNotEditablePaths));
            outNotEditablePaths.Clear();
            foreach (string path in assetOrMetaFilePaths)
                if (!IsOpenForEdit(path, out _, statusQueryOptions)) outNotEditablePaths.Add(path);
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
