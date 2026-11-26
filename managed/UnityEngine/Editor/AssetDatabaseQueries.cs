using System;
using System.Collections.Generic;
using System.IO;
using Unbound.Runtime;
using UnityEngine;

namespace UnityEditor
{
    /// <summary>
    /// AssetDatabase's answers about paths, folders, main types, scene paths, AssetBundle names,
    /// import workers and auto-refresh (matches the Unity 6000.0.71f1 Editor,
    /// tests/projects/assetqueries).
    /// </summary>
    public sealed partial class AssetDatabase
    {
        /// <summary>The path with <c>.meta</c> added, a trailing <c>/</c> left out; nothing else of
        /// it is checked or changed (assetqueries meta.*: a missing asset's, a backslash path's, null's
        /// ".meta").</summary>
        public static string GetTextMetaFilePathFromAssetPath(string path) => (path ?? "").TrimEnd('/') + ".meta";

        /// <summary>Whether the database has an asset at the path, as it names it: a backslash
        /// reads as <c>/</c> and case does not count; a trailing <c>/</c>, <c>./</c> or a space is
        /// no asset's path; an absolute path is none, said (assetqueries exists.*).</summary>
        public static bool AssetPathExists(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (Path.IsPathRooted(path))
            {
                Debug.LogWarning($"Invalid AssetDatabase path: {path}. Use path relative to the project folder.");
                return false;
            }
            path = path.Replace('\\', '/');
            if (path.EndsWith("/", StringComparison.Ordinal)) return false;
            if (SettingsAssets.AtPath(path) != null) return true;
            _ = Entries;
            return s_ByPath.ContainsKey(path);
        }

        /// <summary>Whether a path is in one of the database's root folders, whether it is one
        /// (<c>Assets</c>, a package's root), and whether that folder is immutable (a package the
        /// project does not hold); asked of the path's root alone, not of the asset (a missing
        /// file under Assets is found: assetqueries folder.*).</summary>
        public static bool TryGetAssetFolderInfo(string path, out bool rootFolder, out bool immutable)
        {
            rootFolder = immutable = false;
            if (string.IsNullOrEmpty(path) || Path.IsPathRooted(path)) return false;
            path = path.Replace('\\', '/');
            const string assets = "Assets";
            if (path.StartsWith(assets, StringComparison.OrdinalIgnoreCase) && (path.Length == assets.Length || path[assets.Length] == '/'))
            {
                rootFolder = path.Length == assets.Length;
                return true;
            }
            const string packages = "Packages/";
            if (!path.StartsWith(packages, StringComparison.Ordinal)) return false;
            var relative = path.AsSpan(packages.Length);
            foreach (var package in UnityEditor.PackageManager.PackageInfo.Registered)
            {
                var name = package.name;
                if (!relative.StartsWith(name.AsSpan(), StringComparison.Ordinal) || (relative.Length != name.Length && relative[name.Length] != '/')) continue;
                rootFolder = relative.Length == name.Length;
                immutable = IsImmutablePackagePath(path);
                return true;
            }
            return false;
        }

        /// <summary>The main asset's type of an asset by its GUID; null for a GUID of no asset
        /// (assetqueries main_type.*).</summary>
        public static Type GetMainAssetTypeFromGUID(GUID guid)
        {
            var path = GUIDToAssetPath(guid.ToString());
            return string.IsNullOrEmpty(path) ? null : GetMainAssetTypeAtPath(path);
        }

        /// <summary>An asset's path, else the path of the scene a GameObject or component is in
        /// (assetqueries scene_path.*).</summary>
        public static string GetAssetOrScenePath(UnityEngine.Object assetObject) => EditObjectPath(assetObject);

        internal static GUID AssetPathToGUID_Internal(string path) => new GUID(AssetPathToGUID(path));

        internal static bool IsV1Enabled() => false;
        internal static bool IsV2Enabled() => true;

        // AssetBundle names --------------------------------------------------------------------

        /// <summary>The database's AssetBundle names (with their variants, "name.variant"): those
        /// its assets have when it is listed, and each one an importer is given; a name stays until
        /// it is removed, whether an asset still has it or not (assetqueries cleared.*, remove_*).</summary>
        static SortedSet<string> s_BundleNames;

        static SortedSet<string> BundleNames
        {
            get
            {
                if (s_BundleNames != null) return s_BundleNames;
                s_BundleNames = new SortedSet<string>(StringComparer.Ordinal);
                foreach (var e in Entries)
                    if (!string.IsNullOrEmpty(e.Bundle)) s_BundleNames.Add(FullBundleName(e.Bundle, e.Variant));
                return s_BundleNames;
            }
        }

        /// <summary>For tests: the names are taken from the next listing again.</summary>
        internal static void ForgetBundleNames() => s_BundleNames = null;

        static string FullBundleName(string name, string variant) => string.IsNullOrEmpty(variant) ? name : name + "." + variant;

        /// <summary>An importer was given an AssetBundle name (in lower case): its listing entry has
        /// it, and the database the name.</summary>
        internal static void BundleNameSet(string path, string name, string variant)
        {
            if (string.IsNullOrEmpty(path)) return;
            _ = BundleNames;
            if (s_ByPath.TryGetValue(path, out var entry))
            {
                entry.Bundle = name ?? "";
                entry.Variant = variant ?? "";
            }
            if (!string.IsNullOrEmpty(name)) s_BundleNames.Add(FullBundleName(name, variant));
        }

        /// <summary>The full AssetBundle name an asset has of its own ("" for none).</summary>
        static string OwnBundle(Entry e) => string.IsNullOrEmpty(e.Bundle) ? "" : FullBundleName(e.Bundle, e.Variant);

        /// <summary>The asset's own AssetBundle name and variant, else its nearest folder's.</summary>
        static (string Name, string Variant) ImplicitBundle(Entry e)
        {
            for (var path = e.Path; ; )
            {
                if (s_ByPath.TryGetValue(path, out var at) && !string.IsNullOrEmpty(at.Bundle)) return (at.Bundle, at.Variant ?? "");
                int slash = path.LastIndexOf('/');
                if (slash <= 0) return ("", "");
                path = path.Substring(0, slash);
            }
        }

        public static string[] GetAllAssetBundleNames()
        {
            var names = new string[BundleNames.Count];
            BundleNames.CopyTo(names);
            return names;
        }

        /// <summary>The database's names no asset has of its own.</summary>
        public static string[] GetUnusedAssetBundleNames()
        {
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var e in Entries) used.Add(OwnBundle(e));
            var unused = new List<string>();
            foreach (var name in BundleNames)
                if (!used.Contains(name)) unused.Add(name);
            return unused.ToArray();
        }

        public static void RemoveUnusedAssetBundleNames()
        {
            foreach (var name in GetUnusedAssetBundleNames()) BundleNames.Remove(name);
        }

        /// <summary>Removes a name from the database, not from the assets that have it (their .meta
        /// and importer keep it, assetqueries remove_forced.*): one an asset has only when forced,
        /// else it stays, said; a name the database lacks is said too.</summary>
        public static bool RemoveAssetBundleName(string assetBundleName, bool forceRemove)
        {
            if (assetBundleName == null || !BundleNames.Contains(assetBundleName))
            {
                Debug.LogWarning($"AssetBundle name \"{assetBundleName}\" is not in the asset database.");
                return true;
            }
            if (!forceRemove)
            {
                string user = null;
                foreach (var e in Entries)
                    if (OwnBundle(e) == assetBundleName && (user == null || string.CompareOrdinal(e.Path, user) < 0)) user = e.Path;
                if (user != null)
                {
                    Debug.LogWarning($"AssetBundle name \"{assetBundleName}\" is still in use by \"{user}\".");
                    return false;
                }
            }
            BundleNames.Remove(assetBundleName);
            return true;
        }

        /// <summary>The assets (not folders) whose AssetBundle name, their own or their nearest
        /// folder's, with its variant, is <paramref name="assetBundleName"/> as given (case counts),
        /// in path order.</summary>
        public static string[] GetAssetPathsFromAssetBundle(string assetBundleName)
        {
            if (string.IsNullOrEmpty(assetBundleName)) return Array.Empty<string>();
            var paths = new List<string>();
            foreach (var e in Entries)
            {
                if (e.Kind == "folder") continue;
                var (name, variant) = ImplicitBundle(e);
                if (name.Length != 0 && FullBundleName(name, variant) == assetBundleName) paths.Add(e.Path);
            }
            paths.Sort(StringComparer.Ordinal);
            return paths.ToArray();
        }

        /// <summary>Those of <see cref="GetAssetPathsFromAssetBundle"/> whose file name without its
        /// extension is <paramref name="assetName"/>, case aside.</summary>
        public static string[] GetAssetPathsFromAssetBundleAndAssetName(string assetBundleName, string assetName)
        {
            var paths = new List<string>();
            foreach (var path in GetAssetPathsFromAssetBundle(assetBundleName))
                if (string.Equals(Path.GetFileNameWithoutExtension(path), assetName, StringComparison.OrdinalIgnoreCase)) paths.Add(path);
            return paths.ToArray();
        }

        public static string GetImplicitAssetBundleName(string assetPath) => ImplicitBundle(BundleAsset(assetPath)).Name;

        public static string GetImplicitAssetBundleVariantName(string assetPath) => ImplicitBundle(BundleAsset(assetPath)).Variant;

        static Entry BundleAsset(string assetPath)
        {
            _ = Entries;
            if (assetPath != null && s_ByPath.TryGetValue(assetPath.Replace('\\', '/'), out var e)) return e;
            throw new ArgumentException($"The specified asset {assetPath} cannot be found.");
        }

        // Import workers and auto-refresh -------------------------------------------------------

        static int? s_DesiredWorkerCount;

        /// <summary>The session's import worker count: the user setting's when first asked, then
        /// its own (setting one leaves the other: assetqueries workers.*). Unbound imports in its
        /// own process; the count starts no workers.</summary>
        public static int DesiredWorkerCount
        {
            get => s_DesiredWorkerCount ??= EditorUserSettings.desiredImportWorkerCount;
            set => s_DesiredWorkerCount = value;
        }

        /// <summary>Unbound imports in its own process: there are no import workers to start or stop.</summary>
        public static void ForceToDesiredWorkerCount() { }

        static int s_DisallowAutoRefresh;

        /// <summary>Whether a script disallowed the automatic refresh (DisallowAutoRefresh calls
        /// outnumber AllowAutoRefresh's).</summary>
        internal static bool AutoRefreshDisallowed => s_DisallowAutoRefresh > 0;

        public static void DisallowAutoRefresh() => s_DisallowAutoRefresh++;

        /// <summary>An AllowAutoRefresh without its DisallowAutoRefresh leaves the count below zero,
        /// asserted at each call while it is (assetqueries autorefresh.extra_allow, after_extra).</summary>
        public static void AllowAutoRefresh()
        {
            s_DisallowAutoRefresh--;
            if (s_DisallowAutoRefresh < 0) Debug.LogAssertion("Assertion failed on expression: 'm_DisallowAutoRefresh >= 0'");
        }
    }

    public sealed partial class EditorUserSettings
    {
        static int? s_DesiredImportWorkerCount;
        static int? s_StandbyImportWorkerCount;
        static int? s_IdleImportWorkerShutdownDelay;

        /// <summary>The user's import worker count: a quarter of the logical processors (12 gave 3,
        /// assetqueries workers.default; the rounding of other counts and a floor are not
        /// measured: at least one). Kept for the session.</summary>
        public static int desiredImportWorkerCount
        {
            get => s_DesiredImportWorkerCount ??= Math.Max(1, SystemInfo.processorCount / 4);
            set => s_DesiredImportWorkerCount = value;
        }

        /// <summary>Workers kept waiting: 2 (assetqueries workers.default). Kept for the session.</summary>
        public static int standbyImportWorkerCount
        {
            get => s_StandbyImportWorkerCount ??= 2;
            set => s_StandbyImportWorkerCount = value;
        }

        /// <summary>A worker's idle time before it stops: 60000 ms (the Editor's user settings file,
        /// assetqueries workers.user_settings_file). Kept for the session.</summary>
        public static int idleImportWorkerShutdownDelayMilliseconds
        {
            get => s_IdleImportWorkerShutdownDelay ??= 60000;
            set => s_IdleImportWorkerShutdownDelay = value;
        }
    }
}
