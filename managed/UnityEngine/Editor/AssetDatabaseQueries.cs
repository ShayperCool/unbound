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
        internal static void BundleNameSet(string path, string name, string variant, bool written)
        {
            if (string.IsNullOrEmpty(path)) return;
            _ = BundleNames;
            // The engine's listing reads the .meta written again, as a rescan reads it (a later
            // Refresh finds nothing changed: its importer's section, which the save may have
            // given it, too); the asset is not imported again.
            Entry header = null;
            if (written && Native.IsReady)
                header = System.Text.Json.JsonSerializer.Deserialize<Entry>(
                    Native.Call("asset-header", System.Text.Json.JsonSerializer.Serialize(new { path })), s_Json);
            if (s_ByPath.TryGetValue(path, out var entry))
            {
                if (header != null)
                {
                    entry.Importer = header.Importer;
                    entry.Labels = header.Labels ?? Array.Empty<string>();
                }
                entry.Bundle = name ?? "";
                entry.Variant = variant ?? "";
            }
            if (!string.IsNullOrEmpty(name)) s_BundleNames.Add(FullBundleName(name, variant));
        }

        /// <summary>The full AssetBundle name an asset has of its own ("" for none).</summary>
        static string OwnBundle(Entry e) => string.IsNullOrEmpty(e.Bundle) ? "" : FullBundleName(e.Bundle, e.Variant);

        /// <summary>An asset listed anew (an import, a refresh, the listing taken again) brings its
        /// name to the database when it is new or its name changed; an asset imported again with
        /// the name a forced removal took away does not bring it back (assetqueries
        /// reimport_removed.all).</summary>
        static void BundleListed(Entry e, Entry previous)
        {
            if (s_BundleNames == null || string.IsNullOrEmpty(e.Bundle)) return;
            if (previous != null && previous.Bundle == e.Bundle && previous.Variant == e.Variant) return;
            s_BundleNames.Add(FullBundleName(e.Bundle, e.Variant));
        }

        /// <summary>The asset's own AssetBundle name and variant, else its nearest folder's
        /// (<paramref name="folders"/>: the folders' answers so far, for a pass over many assets).</summary>
        static (string Name, string Variant) ImplicitBundle(Entry e, Dictionary<string, (string, string)> folders = null)
        {
            if (!string.IsNullOrEmpty(e.Bundle)) return (e.Bundle, e.Variant ?? "");
            int slash = e.Path.LastIndexOf('/');
            return slash <= 0 ? ("", "") : FolderBundle(e.Path.Substring(0, slash), folders);
        }

        static (string, string) FolderBundle(string folder, Dictionary<string, (string, string)> folders)
        {
            if (folders != null && folders.TryGetValue(folder, out var known)) return known;
            (string, string) found;
            if (s_ByPath.TryGetValue(folder, out var at) && !string.IsNullOrEmpty(at.Bundle)) found = (at.Bundle, at.Variant ?? "");
            else
            {
                int slash = folder.LastIndexOf('/');
                found = slash <= 0 ? ("", "") : FolderBundle(folder.Substring(0, slash), folders);
            }
            if (folders != null) folders[folder] = found;
            return found;
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
            var folders = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in Entries)
            {
                if (e.Kind == "folder") continue;
                var (name, variant) = ImplicitBundle(e, folders);
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
            get => s_DesiredWorkerCount ??= EditorUserSettings.InitialDesiredImportWorkerCount;
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

        /// <summary>The user's import worker count: the user settings file's
        /// (<c>m_DesiredImportWorkerCount</c>), else what the Editor starts a project with, a
        /// quarter of the logical processors (<see cref="DefaultDesiredImportWorkerCount"/>). Kept
        /// for the session (not saved: Unbound never writes the user's file).</summary>
        public static int desiredImportWorkerCount
        {
            get => s_DesiredImportWorkerCount ??= InitialDesiredImportWorkerCount;
            set => s_DesiredImportWorkerCount = value;
        }

        /// <summary>The count the session starts with (AssetDatabase.DesiredWorkerCount's first).</summary>
        internal static int InitialDesiredImportWorkerCount =>
            WorkerSetting("m_DesiredImportWorkerCount") ?? DefaultDesiredImportWorkerCount(SystemInfo.processorCount);

        /// <summary>A quarter of the logical processors: 12 gave 3 (assetqueries workers.default and
        /// the user settings file the Editor wrote). Its rounding of other counts, and a floor, are
        /// not measured (here: rounded down, at least one).</summary>
        internal static int DefaultDesiredImportWorkerCount(int processors) =>
            DefaultDesiredImportWorkerCount(processors, EditorPrefs.GetFloat(DesiredImportWorkerCountShareKey, DefaultDesiredImportWorkerCountShare));

        internal static int DefaultDesiredImportWorkerCount(int processors, float share) => Math.Max(1, (int)(share * processors));

        /// <summary>The Editor preference "Import Worker Count %" (a share of the logical processors,
        /// 0.25 by default: Unity's AssetPipelinePreferences).</summary>
        const string DesiredImportWorkerCountShareKey = "DesiredImportWorkerCountPctOfLogicalCPUs";
        const float DefaultDesiredImportWorkerCountShare = 0.25f;

        /// <summary>Workers kept waiting: the user settings file's, else 2 (assetqueries workers.default).</summary>
        public static int standbyImportWorkerCount
        {
            get => s_StandbyImportWorkerCount ??= WorkerSetting("m_StandbyImportWorkerCount") ?? 2;
            set => s_StandbyImportWorkerCount = value;
        }

        /// <summary>A worker's idle time before it stops: the user settings file's, else 60000 ms
        /// (the file the Editor wrote, assetqueries workers.user_settings_file).</summary>
        public static int idleImportWorkerShutdownDelayMilliseconds
        {
            get => s_IdleImportWorkerShutdownDelay ??= WorkerSetting("m_IdleImportWorkerShutdownDelay") ?? 60000;
            set => s_IdleImportWorkerShutdownDelay = value;
        }

        /// <summary>A top-level integer of the user settings file (UserSettings/EditorUserSettings.asset), if it has it.</summary>
        static int? WorkerSetting(string key)
        {
            try { return WorkerSetting(File.Exists(UnityFile) ? File.ReadAllText(UnityFile) : null, key); }
            catch (Exception e)
            {
                Debug.LogWarning($"Unbound: could not read the import worker settings of EditorUserSettings: {e.Message}");
                return null;
            }
        }

        internal static int? WorkerSetting(string text, string key)
        {
            if (text == null) return null;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith(key + ":", StringComparison.Ordinal)
                    && int.TryParse(line.Substring(key.Length + 1).Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int value))
                    return value;
            }
            return null;
        }
    }
}
