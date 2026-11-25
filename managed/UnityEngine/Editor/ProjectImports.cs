using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Unbound.Runtime;
using UnityEngine;

namespace UnityEditor
{
    /// <summary>
    /// What the project's import at a session's start imports, as the Editor's import at its
    /// start does (matches the Unity 6000.0.71f1 Editor, tests/projects/firstimport): every asset
    /// of a project opened for the first time (no import record), else the assets new or changed
    /// since the last session (their file or their <c>.meta</c>; a file only touched is not), with
    /// the assets deleted and those moved (same GUID, another path, not listed as imported unless
    /// changed). The assets are those of the asset database (<c>Assets</c> and its files and
    /// folders, the packages' files and folders) and the project's settings files
    /// (<c>ProjectSettings/&lt;manager&gt;.asset</c>); the Editor also lists two files of its own
    /// <c>Library/</c>, which Unbound does not have. Their order is the database's (the Editor's is
    /// a hash order Unity does not specify).
    /// Unbound keeps what it saw of each asset (GUID, size, time, content hash of the file and of its
    /// <c>.meta</c>) in <c>ImportRecord.json</c> of its project directory: written after the start's
    /// import and again as the session ends, so that what the session itself wrote is not imported
    /// again at the next start (the Editor imports such files during the session).
    /// </summary>
    internal static class ProjectImports
    {
        /// <summary>The record's format; a record of another format is not used (a first open).</summary>
        internal const int FormatVersion = 1;
        const string RecordFile = "ImportRecord.json";

        internal sealed class Changes
        {
            internal string[] Imported = Array.Empty<string>();
            internal string[] Deleted = Array.Empty<string>();
            internal string[] Moved = Array.Empty<string>();
            internal string[] MovedFrom = Array.Empty<string>();
            /// <summary>No record: the project's first open.</summary>
            internal bool First;
        }

        /// <summary>An asset as the session's import finds it: its project path, GUID and file.</summary>
        internal readonly struct Asset
        {
            internal readonly string Path;
            internal readonly string Guid;
            internal readonly string File;

            internal Asset(string path, string guid, string file)
            {
                Path = path;
                Guid = guid ?? "";
                File = file;
            }
        }

        internal sealed class Entry
        {
            public string Path { get; set; }
            public string Guid { get; set; }
            public long Size { get; set; }
            public long Time { get; set; }
            public string Hash { get; set; }
            public long MetaSize { get; set; }
            public long MetaTime { get; set; }
            public string MetaHash { get; set; }
        }

        sealed class Record
        {
            public int Format { get; set; }
            public List<Entry> Assets { get; set; } = new List<Entry>();
        }

        static bool s_Taken;
        static bool s_Recorded;

        /// <summary>The start's import, once per process: null after the first call, and for a
        /// session relaunched for a script edit (its start lists nothing; the record is brought up
        /// to date, as the Editor imports the edit during the session). Without the native side
        /// (unit tests) there is no project: null.</summary>
        internal static Changes TakeStart()
        {
            if (s_Taken) return null;
            s_Taken = true;
            if (!Native.IsReady || string.IsNullOrEmpty(ProjectInfo.ProjectPath)) return null;
            var file = RecordPath();
            var changes = Scan(ProjectAssets(), file, out var record);
            Save(file, record);
            s_Recorded = true;
            return EditorInitialization.ContinuesScriptReload() ? null : changes;
        }

        /// <summary>The session ends: the record takes the project's files as they are now.</summary>
        internal static void Settle()
        {
            if (!s_Recorded) return;
            var file = RecordPath();
            Scan(ProjectAssets(), file, out var record);
            Save(file, record);
        }

        static string RecordPath() => ProjectInfo.UnboundPath(RecordFile);

        /// <summary>The assets the import covers, in the database's order, then the settings files.</summary>
        static List<Asset> ProjectAssets()
        {
            var root = ProjectInfo.ProjectPath;
            var packages = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var package in PackageManager.PackageInfo.GetAllRegisteredPackages())
                if (!string.IsNullOrEmpty(package.resolvedPath)) packages[package.name] = package.resolvedPath;
            var assets = new List<Asset>();
            foreach (var path in AssetDatabase.GetAllAssetPaths())
                assets.Add(new Asset(path, AssetDatabase.AssetPathToGUID(path), Physical(root, packages, path)));
            foreach (var path in SettingsAssets.ProjectFiles())
                assets.Add(new Asset(path, "", System.IO.Path.Combine(root, path)));
            return assets;
        }

        /// <summary>The file of a project path; null for a package Unbound has no folder of (built-in
        /// module packages).</summary>
        static string Physical(string root, Dictionary<string, string> packages, string path)
        {
            if (!path.StartsWith("Packages/", StringComparison.Ordinal)) return System.IO.Path.Combine(root, path);
            var rest = path.Substring("Packages/".Length);
            int slash = rest.IndexOf('/');
            var name = slash < 0 ? rest : rest.Substring(0, slash);
            if (!packages.TryGetValue(name, out var folder)) return null;
            return slash < 0 ? folder : System.IO.Path.Combine(folder, rest.Substring(slash + 1));
        }

        /// <summary>The changes since the record in <paramref name="recordFile"/> (all assets imported
        /// when it is missing or of another format), and the record of the assets as they are.</summary>
        internal static Changes Scan(IReadOnlyList<Asset> assets, string recordFile, out List<Entry> now)
        {
            var old = Load(recordFile);
            var byPath = new Dictionary<string, Entry>(StringComparer.Ordinal);
            var byGuid = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            if (old != null)
                foreach (var e in old)
                {
                    byPath[e.Path] = e;
                    if (e.Guid.Length > 0) byGuid[e.Guid] = e;
                }
            var current = new HashSet<string>(StringComparer.Ordinal);
            foreach (var a in assets) current.Add(a.Path);

            var changes = new Changes { First = old == null };
            var imported = new List<string>();
            var moved = new List<string>();
            var movedFrom = new List<string>();
            var gone = new HashSet<string>(StringComparer.Ordinal);
            now = new List<Entry>(assets.Count);
            foreach (var a in assets)
            {
                byPath.TryGetValue(a.Path, out var before);
                Entry from = null;
                if (before == null && a.Guid.Length > 0 && byGuid.TryGetValue(a.Guid, out var other) && !current.Contains(other.Path))
                    from = other;
                var entry = Observe(a, before ?? from);
                now.Add(entry);
                if (old == null) imported.Add(a.Path);
                else if (from != null)
                {
                    moved.Add(a.Path);
                    movedFrom.Add(from.Path);
                    gone.Add(from.Path);
                    if (!SameContent(entry, from)) imported.Add(a.Path);
                }
                else if (before == null || !SameContent(entry, before)) imported.Add(a.Path);
            }
            var deleted = new List<string>();
            if (old != null)
                foreach (var e in old)
                    if (!current.Contains(e.Path) && !gone.Contains(e.Path)) deleted.Add(e.Path);
            changes.Imported = imported.ToArray();
            changes.Deleted = deleted.ToArray();
            changes.Moved = moved.ToArray();
            changes.MovedFrom = movedFrom.ToArray();
            return changes;
        }

        static bool SameContent(Entry a, Entry b) =>
            a.Hash == b.Hash && a.MetaHash == b.MetaHash && a.MetaSize == b.MetaSize;

        /// <summary>The asset's entry now; a file whose size and time are those recorded keeps its
        /// recorded hash, any other is hashed.</summary>
        static Entry Observe(Asset a, Entry known)
        {
            var entry = new Entry { Path = a.Path, Guid = a.Guid };
            Stat(a.File, known == null ? null : (known.Size, known.Time, known.Hash), out var size, out var time, out var hash);
            entry.Size = size;
            entry.Time = time;
            entry.Hash = hash;
            Stat(a.File == null ? null : a.File + ".meta", known == null ? null : (known.MetaSize, known.MetaTime, known.MetaHash), out size, out time, out hash);
            entry.MetaSize = size;
            entry.MetaTime = time;
            entry.MetaHash = hash;
            return entry;
        }

        /// <summary>A file's size, time and content hash; a folder is "folder" (its contents are
        /// assets of their own), what does not exist size -1.</summary>
        static void Stat(string file, (long Size, long Time, string Hash)? known, out long size, out long time, out string hash)
        {
            size = -1;
            time = 0;
            hash = "";
            if (file == null) return;
            if (Directory.Exists(file))
            {
                size = 0;
                hash = "folder";
                return;
            }
            var info = new FileInfo(file);
            if (!info.Exists) return;
            size = info.Length;
            time = info.LastWriteTimeUtc.Ticks;
            if (known is { } k && k.Size == size && k.Time == time && !string.IsNullOrEmpty(k.Hash))
            {
                hash = k.Hash;
                return;
            }
            try
            {
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
                hash = Convert.ToHexString(SHA256.HashData(stream));
            }
            catch (IOException) { hash = "unreadable"; }
            catch (UnauthorizedAccessException) { hash = "unreadable"; }
        }

        static readonly JsonSerializerOptions s_Json = new JsonSerializerOptions { WriteIndented = false };

        static List<Entry> Load(string file)
        {
            try
            {
                if (!File.Exists(file)) return null;
                var record = JsonSerializer.Deserialize<Record>(File.ReadAllBytes(file), s_Json);
                if (record == null || record.Format != FormatVersion || record.Assets == null) return null;
                foreach (var e in record.Assets)
                    if (e.Path == null) return null;
                    else e.Guid ??= "";
                return record.Assets;
            }
            catch (JsonException) { return null; }
            catch (IOException) { return null; }
        }

        static void Save(string file, List<Entry> entries)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file));
                var temp = file + ".tmp";
                File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(new Record { Format = FormatVersion, Assets = entries }, s_Json));
                File.Move(temp, file, overwrite: true);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning($"Unbound: the project's import record is not saved ({e.Message}); the next start imports the assets changed since the last one saved");
            }
        }

        /// <summary>Forgets that the start was taken (tests).</summary>
        internal static void ResetForTests()
        {
            s_Taken = false;
            s_Recorded = false;
        }
    }
}
