using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Unbound.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEditor
{
    /// <summary>
    /// The AssetDatabase calls that change the project's files, as the Editor makes them (matches
    /// the Unity 6000.0.71f1 Editor in Edit and Play mode, tests/projects/projectwrites): a new
    /// file or folder gets its <c>.meta</c> when it is imported (Refresh, ImportAsset, a new
    /// folder; the importer's defaults, written by the engine's asset database), labels are
    /// written into the <c>.meta</c> at once, and copies, moves, renames and deletions take the
    /// <c>.meta</c> along. Between StartAssetEditing and StopAssetEditing imports wait for the
    /// last StopAssetEditing; files an API writes itself are written at once.
    /// </summary>
    public sealed partial class AssetDatabase
    {
        static int s_Editing;
        static readonly List<string> s_PendingImports = new List<string>();
        static bool s_PendingRefresh;

        /// <summary>Assets deleted in this session: the default AssetPathToGUID options and
        /// GUIDToAssetPath still answer for them (matches the Unity 6000.0.71f1 Editor,
        /// tests/projects/projectwrites 06.delete).</summary>
        static readonly Dictionary<string, string> s_DeletedGuidByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, string> s_DeletedPathByGuid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        internal sealed class ImportRequest
        {
            public string Path { get; set; }
            public long? Main { get; set; }
        }

        sealed class Imported
        {
            public string Path { get; set; }
            public string Guid { get; set; }
            public string Kind { get; set; }
            /// <summary>The importer the Editor gives the asset.</summary>
            public string Importer { get; set; }
            public string[] Labels { get; set; }
            /// <summary>Why the path was not imported (no .meta is written for it).</summary>
            public string Error { get; set; }
            /// <summary>The GUID of an asset a refresh found gone.</summary>
            public string Removed { get; set; }
            /// <summary>The GUID the asset's .meta had, which another asset holds (it got a new one).</summary>
            public Conflict Conflict { get; set; }
        }

        sealed class Conflict
        {
            public string Guid { get; set; }
            public string Owner { get; set; }
        }

        /// <summary>What the Editor says when a .meta repeats another asset's GUID
        /// (tests/projects/projectwrites 07.batch.logs, which keeps the first line; the whole
        /// message as the reference runs' Editor log has it).</summary>
        static void SayConflict(string path, Imported asset)
        {
            if (asset?.Conflict == null) return;
            Debug.LogWarning($"GUID [{asset.Conflict.Guid}] for asset '{path}' conflicts with:\n  '{asset.Conflict.Owner}' (current owner)\nAssigning a new guid.");
        }

        sealed class ImportResult
        {
            public Imported[] Assets { get; set; }
        }

        /// <summary>The engine's asset database brought up to date for these paths (a missing
        /// <c>.meta</c> written, a gone asset dropped), and the listing with it; each path's GUID,
        /// "" for a gone one or one that was not imported (said in a warning: its importer is not
        /// measured, it is hidden, ...).</summary>
        internal static string[] Import(IEnumerable<ImportRequest> requests)
        {
            var list = requests.ToList();
            if (list.Count == 0) return Array.Empty<string>();
            // The project's scripts hear of each .meta file about to be written for a new asset
            // (tests/projects/projectwindowutil create.*). The .meta files a refresh writes are not
            // reported yet: the engine writes them without listing the new paths first.
            foreach (var request in list)
                if (!AssetPaths.Hidden(request.Path) && Exists(request.Path) && !File.Exists(FileOf(request.Path) + ".meta"))
                    AssetModificationProcessorInternal.OnWillCreateAsset(request.Path + ".meta");
            var json = JsonSerializer.Serialize(new { paths = list }, s_Json);
            var assets = JsonSerializer.Deserialize<ImportResult>(Native.Call("asset-import", json), s_Json)?.Assets ?? Array.Empty<Imported>();
            var guids = new string[list.Count];
            // A large batch is listed again from the engine when next asked (cheaper than each edit).
            bool relist = list.Count <= RelistLimit;
            for (int i = 0; i < list.Count; i++)
            {
                var asset = i < assets.Length ? assets[i] : null;
                guids[i] = asset?.Guid ?? "";
                AssetDatabaseRuntime.ImportedInSession(guids[i]);
                if (!string.IsNullOrEmpty(asset?.Error))
                {
                    Debug.LogWarning("Unbound: " + asset.Error);
                    continue;
                }
                SayConflict(list[i].Path, asset);
                if (relist) Relist(list[i].Path, asset);
            }
            if (!relist) Invalidate();
            return guids;
        }

        /// <summary>The most paths whose listing entries are edited in place; more are listed again
        /// from the engine when next asked.</summary>
        const int RelistLimit = 64;

        /// <summary>The listing's entry for a path as an import left it.</summary>
        static void Relist(string path, Imported asset)
        {
            if (s_Entries == null) return;  // listed from the engine when first asked
            if (s_ByPath.TryGetValue(path, out var old))
            {
                s_Entries.Remove(old);
                s_ByPath.Remove(path);
                if (s_ByGuid.TryGetValue(old.Guid, out var same) && same == old) s_ByGuid.Remove(old.Guid);
            }
            if (asset == null || string.IsNullOrEmpty(asset.Guid)) return;
            if (s_ByGuid.TryGetValue(asset.Guid, out var moved))
            {
                s_Entries.Remove(moved);
                s_ByPath.Remove(moved.Path);
            }
            var entry = new Entry { Guid = asset.Guid, Path = path, Kind = asset.Kind, Importer = asset.Importer, Labels = asset.Labels ?? Array.Empty<string>() };
            s_Entries.Add(entry);
            s_ByPath[path] = entry;
            s_ByGuid[asset.Guid] = entry;
        }

        /// <summary>Writes a file a bake makes at <paramref name="path"/> (one there is replaced,
        /// its .meta and GUID kept) and imports it; its GUID ("" where the path is not the
        /// project's own).</summary>
        internal static string WriteBakedFile(string path, byte[] bytes, long? main = null)
        {
            if (!Editable(path))
            {
                Refused("Lightmapping.Bake", path);
                return "";
            }
            File.WriteAllBytes(FileOf(path), bytes);
            return ImportOne(path, main);
        }

        internal static string ImportOne(string path, long? main = null) =>
            Import(new[] { new ImportRequest { Path = path, Main = main } }).FirstOrDefault() ?? "";

        sealed class Root
        {
            public string Path { get; set; }
            public bool Editable { get; set; }
        }

        static Root[] s_Roots;

        /// <summary>The asset database's roots: <c>Assets</c> and each package, and whether the
        /// project holds it (Assets, embedded and local packages) or it is read-only.</summary>
        static Root[] Roots => s_Roots ??= JsonSerializer.Deserialize<Root[]>(Native.Call("asset-roots", "{}"), s_Json) ?? Array.Empty<Root>();

        /// <summary>The roots for the managed tests, which have no engine to ask (null: ask it again).</summary>
        internal static void SetRootsForTests(params (string Path, bool Editable)[] roots) =>
            s_Roots = roots?.Select(r => new Root { Path = r.Path, Editable = r.Editable }).ToArray();

        static bool IsRoot(string path) =>
            string.Equals(path, "Packages", StringComparison.OrdinalIgnoreCase) || Roots.Any(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));

        /// <summary>Whether the Editor writes at <paramref name="path"/>: inside <c>Assets</c> or a
        /// package the project holds; never a root itself, nor a registry or built-in package.</summary>
        internal static bool Editable(string path) =>
            AssetPaths.Editable(Roots.Select(r => (r.Path, r.Editable)), path);

        /// <summary>Whether a path is in a package the project does not hold (built-in, registry, Git).</summary>
        static bool InReadOnlyPackage(string path) =>
            Roots.Any(r => !r.Editable && path.StartsWith(r.Path + "/", StringComparison.OrdinalIgnoreCase));

        internal static void Refused(string operation, string path) =>
            Debug.LogError($"{operation}: '{path}' is not in the project's own assets (Assets/, or a package embedded or kept locally); nothing is written there.");

        /// <summary>The instance ID of the main asset at <paramref name="assetPath"/> (a folder's
        /// too); 0 for no asset.</summary>
        internal static int GetMainAssetOrInProgressProxyInstanceID(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return 0;
            var main = LoadMainAssetAtPath(assetPath);
            return main == null ? 0 : main.GetInstanceID();
        }

        /// <summary>The file or folder a project path names (absolute).</summary>
        internal static string FileOf(string path)
        {
            var physical = FileUtil.GetPhysicalPath(path);
            return Path.IsPathRooted(physical) ? physical : Path.Combine(ProjectInfo.ProjectPath, physical);
        }

        static bool Exists(string path) => File.Exists(FileOf(path)) || Directory.Exists(FileOf(path));

        static string Normalize(string path) => path?.Replace('\\', '/').TrimEnd('/');

        /// <summary>A new file or folder at <paramref name="path"/>, and each parent folder
        /// without a <c>.meta</c>, imported now or (between Start/StopAssetEditing) later.</summary>
        static void ImportNew(string path)
        {
            if (!Editable(path)) return;
            if (s_Editing > 0)
            {
                s_PendingImports.Add(path);
                return;
            }
            ImportNew(new[] { path });
        }

        /// <summary>Imports a file Unbound wrote (a saved scene): a new one gets its <c>.meta</c>,
        /// a new folder's parents too, as an import of a new file does (tests/projects/scenesave
        /// Fresh.unity.meta, tests/projects/projectwrites 02.import).</summary>
        internal static void ImportWritten(string path)
        {
            if (Unbound.Runtime.Native.IsReady) ImportNew(path);
        }

        static void ImportNew(IReadOnlyList<string> paths)
        {
            var requests = new List<string>();
            var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in paths)
            {
                if (AssetPaths.Hidden(path)) continue;
                var folders = new List<string>();
                for (var folder = Path.GetDirectoryName(path)?.Replace('\\', '/'); Editable(folder); folder = Path.GetDirectoryName(folder)?.Replace('\\', '/'))
                {
                    if (File.Exists(FileOf(folder) + ".meta") || listed.Contains(folder)) break;
                    folders.Add(folder);
                }
                folders.Reverse();
                foreach (var p in folders.Append(path))
                    if (listed.Add(p)) requests.Add(p);
            }
            Import(requests.Select(p => new ImportRequest { Path = p }));
            // The import runs the assets' preprocessors (whose settings changes their .meta keeps).
            foreach (var path in paths) AssetPostprocessing.EnsurePreprocessed(path, Kind(path));
        }

        [Unbound.EditorFaithful("tests/projects/projectwrites 01.refresh: new files and folders get their .meta, the database is scanned again")]
        public static void Refresh() => Refresh(ImportAssetOptions.Default);

        [Unbound.EditorFaithful("tests/projects/projectwrites 01.refresh: new files and folders get their .meta, the database is scanned again")]
        public static void Refresh(ImportAssetOptions options)
        {
            if (s_Editing > 0)
            {
                s_PendingRefresh = true;
                return;
            }
            ApplyRefresh(Native.Call("asset-refresh", "{}"));
        }

        /// <summary>The listing brought up to date with what a refresh found changed.</summary>
        internal static void ApplyRefresh(string json)
        {
            var changes = JsonSerializer.Deserialize<ImportResult>(json, s_Json)?.Assets ?? Array.Empty<Imported>();
            foreach (var change in changes)
            {
                // A path the engine could not import (a .meta without a GUID line, ...) keeps its
                // listing entry, as the engine keeps its asset.
                if (!string.IsNullOrEmpty(change.Error))
                {
                    Debug.LogWarning("Unbound: " + change.Error);
                    continue;
                }
                SayConflict(change.Path, change);
                // An asset gone from disk is remembered as deleted (tests/projects/projectwrites 07.outside).
                if (!string.IsNullOrEmpty(change.Removed))
                {
                    s_DeletedGuidByPath[change.Path] = change.Removed;
                    s_DeletedPathByGuid[change.Removed] = change.Path;
                    AssetDatabaseRuntime.Invalidate(change.Removed);
                    AssetDatabaseRuntime.ForgetPersistentIds(change.Removed);
                }
                // A new or changed asset is imported in this session (its messages are logged at its load).
                else AssetDatabaseRuntime.ImportedInSession(change.Guid);
                if (changes.Length <= RelistLimit) Relist(change.Path, change);
            }
            if (changes.Length > RelistLimit) Invalidate();
        }

        /// <summary>Holds back imports (Refresh, ImportAsset of new files) until the matching
        /// StopAssetEditing; calls nest (tests/projects/projectwrites 07.batch).</summary>
        public static void StartAssetEditing() => s_Editing++;

        public static void StopAssetEditing()
        {
            if (s_Editing == 0) return;
            if (--s_Editing > 0) return;
            var pending = s_PendingImports.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            s_PendingImports.Clear();
            if (pending.Count > 0) ImportNew(pending);
            if (s_PendingRefresh)
            {
                s_PendingRefresh = false;
                Refresh();
            }
        }

        // ------------------------------------------------------------------ folders and paths

        /// <summary>A new folder in <paramref name="parentFolder"/>, named uniquely as
        /// GenerateUniqueAssetPath names it; its GUID, "" when the parent is not a folder of the
        /// project's own assets.</summary>
        public static string CreateFolder(string parentFolder, string newFolderName)
        {
            parentFolder = Normalize(parentFolder);
            if (!IsValidFolder(parentFolder) || string.IsNullOrEmpty(newFolderName)) return "";
            var path = GenerateUniqueAssetPath(parentFolder + "/" + newFolderName);
            if (string.IsNullOrEmpty(path)) return "";
            if (!Editable(path) || AssetPaths.Hidden(path))
            {
                Refused("AssetDatabase.CreateFolder", path);
                return "";
            }
            Directory.CreateDirectory(FileOf(path));
            return ImportOne(path);
        }


        /// <summary>The path itself when nothing is there, else the first free "name N.ext"
        /// (a name's own " N" replaced); "" when its folder does not exist. The folder is
        /// spelled as the project spells it, the name as given (matches the Unity 6000.0.71f1
        /// Editor, tests/projects/projectwrites 05.unique).</summary>
        public static string GenerateUniqueAssetPath(string path)
        {
            path = Normalize(path);
            if (string.IsNullOrEmpty(path)) return "";
            int slash = path.LastIndexOf('/');
            if (slash < 0) return "";
            var folder = ProjectFolder(path.Substring(0, slash));
            if (folder == null) return "";
            return AssetPaths.Unique(folder, path.Substring(slash + 1), Taken);
        }

        /// <summary>Whether an asset, a file or a <c>.meta</c> has this path, its case ignored as
        /// Unity's paths compare.</summary>
        static bool Taken(string path)
        {
            if (ExistingGuid(path).Length > 0) return true;
            var file = FileOf(path);
            var directory = Path.GetDirectoryName(file);
            if (directory == null || !Directory.Exists(directory)) return false;
            var name = Path.GetFileName(file);
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var other = Path.GetFileName(entry);
                if (string.Equals(other, name, StringComparison.OrdinalIgnoreCase) || string.Equals(other, name + ".meta", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>A folder path as the project spells it, or null when there is none.</summary>
        static string ProjectFolder(string folder)
        {
            if (!string.Equals(folder, "Assets", StringComparison.OrdinalIgnoreCase)
                && !folder.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) && !folder.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase)) return null;
            _ = Entries;
            if (s_ByPath.TryGetValue(folder, out var entry) && entry.Kind == "folder") return entry.Path;
            return Directory.Exists(FileOf(folder)) ? folder : null;
        }

        // ------------------------------------------------------------------ copy, move, rename

        static readonly Regex s_GuidLine = new Regex("^guid: [0-9a-fA-F]{32}(?=\\r?$)", RegexOptions.Multiline);

        /// <summary>A copy of the asset at <paramref name="newPath"/> with a new GUID; its
        /// <c>.meta</c> is the original's with that GUID (tests/projects/projectwrites 05.copy).</summary>
        public static bool CopyAsset(string path, string newPath)
        {
            path = Normalize(path);
            newPath = Normalize(newPath);
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(newPath) || !Exists(path) || Taken(newPath)) return false;
            var parent = Path.GetDirectoryName(newPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(parent) || ProjectFolder(parent) == null) return false;
            if (!Editable(newPath))
            {
                Refused("AssetDatabase.CopyAsset", newPath);
                return false;
            }
            if (File.Exists(FileOf(path) + ".meta") && AssetPaths.WithGuid(File.ReadAllText(FileOf(path) + ".meta"), "0") == null)
            {
                Debug.LogError($"AssetDatabase.CopyAsset: the .meta of '{path}' has no GUID line; nothing is copied.");
                return false;
            }
            var copied = new List<string>();
            CopyTree(FileOf(path), FileOf(newPath), newPath, copied);
            RenameMainObject(newPath);
            Import(copied.Select(p => new ImportRequest { Path = p }));
            return true;
        }

        /// <summary>Copies a file or folder with its <c>.meta</c> (a new GUID in it); hidden files
        /// go along as no assets, a folder plug-in goes whole as one asset.</summary>
        static void CopyTree(string source, string target, string assetPath, List<string> copied)
        {
            if (Directory.Exists(source) && AssetPaths.FolderPlugin(assetPath)) CopyAll(source, target);
            else if (Directory.Exists(source))
            {
                Directory.CreateDirectory(target);
                foreach (var entry in Directory.EnumerateFileSystemEntries(source).OrderBy(e => e, StringComparer.Ordinal))
                {
                    var name = Path.GetFileName(entry);
                    if (name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                    // A hidden file goes along, as no asset (tests/projects/projectwrites 06.folder).
                    if (AssetPaths.HiddenName(name))
                    {
                        if (Directory.Exists(entry)) CopyAll(entry, Path.Combine(target, name));
                        else File.Copy(entry, Path.Combine(target, name));
                        continue;
                    }
                    CopyTree(entry, Path.Combine(target, name), assetPath + "/" + name, copied);
                }
            }
            else File.Copy(source, target);
            if (File.Exists(source + ".meta"))
            {
                var meta = AssetPaths.WithGuid(File.ReadAllText(source + ".meta"), GUID.Generate().ToString());
                if (meta != null) File.WriteAllText(target + ".meta", meta);
            }
            copied.Add(assetPath);
        }

        static void CopyAll(string source, string target) => Trash.CopyAll(source, target);

        public static bool CopyAssets(string[] paths, string[] newPaths)
        {
            if (paths == null || newPaths == null || paths.Length != newPaths.Length) return false;
            bool all = true;
            for (int i = 0; i < paths.Length; i++) all &= CopyAsset(paths[i], newPaths[i]);
            return all;
        }

        /// <summary>Why the asset cannot move there, or "" (the Editor's messages:
        /// tests/projects/projectwrites 05.move).</summary>
        public static string ValidateMoveAsset(string oldPath, string newPath)
        {
            oldPath = Normalize(oldPath);
            newPath = Normalize(newPath);
            if (string.IsNullOrEmpty(oldPath) || !Exists(oldPath)) return $"Trying to move asset that does not exist {oldPath}";
            var parent = Path.GetDirectoryName(newPath ?? "")?.Replace('\\', '/');
            if (string.IsNullOrEmpty(parent) || ProjectFolder(parent) == null)
                return $"Trying to move asset as a sub directory of a directory that does not exist {newPath}";
            if (Taken(newPath) && !string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
                return $"Cannot move asset from {oldPath} to {newPath}. Destination path name does already exist";
            // An asset of a read-only package passes the validation; MoveAsset refuses it
            // (tests/projects/projectwrites 06.readonly).
            return "";
        }

        /// <summary>Moves the asset and its <c>.meta</c>: it keeps its GUID. "" on success, else
        /// why not.</summary>
        public static string MoveAsset(string oldPath, string newPath)
        {
            var error = ValidateMoveAsset(oldPath, newPath);
            if (error.Length > 0) return error;
            oldPath = Normalize(oldPath);
            newPath = Normalize(newPath);
            foreach (var end in new[] { oldPath, newPath })
            {
                if (Editable(end)) continue;
                if (InReadOnlyPackage(end) || IsRoot(end)) return $"Cannot move asset. {end} is an immutable path.";
                Refused("AssetDatabase.MoveAsset", end);
                return $"Cannot move asset. {end} is not an asset of the project's own.";
            }
            var moved = TreeOf(oldPath);
            var source = FileOf(oldPath);
            var target = FileOf(newPath);
            if (Directory.Exists(source)) Directory.Move(source, target);
            else File.Move(source, target);
            if (File.Exists(source + ".meta")) File.Move(source + ".meta", target + ".meta");
            foreach (var created in s_Created.Values)
                if (created.Path.Equals(oldPath, StringComparison.OrdinalIgnoreCase) || created.Path.StartsWith(oldPath + "/", StringComparison.OrdinalIgnoreCase))
                    created.Path = newPath + created.Path.Substring(oldPath.Length);
            RenameMainObject(newPath);
            Import(moved.Select(p => new ImportRequest { Path = p })
                .Concat(moved.Select(p => new ImportRequest { Path = newPath + p.Substring(oldPath.Length) })));
            return "";
        }

        /// <summary>The asset at <paramref name="pathName"/> named <paramref name="newName"/>
        /// (its extension kept): a move within its folder, with the move's messages.</summary>
        public static string RenameAsset(string pathName, string newName)
        {
            pathName = Normalize(pathName);
            if (string.IsNullOrEmpty(pathName) || string.IsNullOrEmpty(newName)) return "Invalid asset or name";
            var folder = Path.GetDirectoryName(pathName)?.Replace('\\', '/');
            var extension = Directory.Exists(FileOf(pathName)) ? "" : Path.GetExtension(pathName);
            return MoveAsset(pathName, folder + "/" + newName + extension);
        }

        static readonly Regex s_MainObjectFileId = new Regex("^  mainObjectFileID: (-?[0-9]+)(?=\\r?$)", RegexOptions.Multiline);

        /// <summary>A native asset's main object named after its file, in the file and, when it is
        /// loaded, in memory: the Editor names it so when it copies, moves or renames the asset
        /// (tests/projects/projectwrites 05.copy, 05.rename).</summary>
        static void RenameMainObject(string path)
        {
            var file = FileOf(path);
            if (!File.Exists(file) || !File.Exists(file + ".meta")) return;
            var meta = File.ReadAllText(file + ".meta");
            var main = s_MainObjectFileId.Match(meta);
            if (!main.Success || main.Groups[1].Value == "0") return;
            var name = Path.GetFileNameWithoutExtension(path);
            var renamed = AssetPaths.WithMainObjectName(File.ReadAllText(file), main.Groups[1].Value, name);
            if (renamed == null) return;
            File.WriteAllText(file, renamed);
            var guid = s_GuidLine.Match(meta);
            if (guid.Success && AssetDatabaseRuntime.IsMainLoaded(guid.Value.Substring("guid: ".Length)))
                AssetDatabaseRuntime.LoadMain(guid.Value.Substring("guid: ".Length), null).name = name;
        }

        /// <summary>The asset paths of a file or folder and everything the database lists in it.</summary>
        static List<string> TreeOf(string path)
        {
            _ = Entries;
            var list = new List<string> { path };
            var prefix = path + "/";
            list.AddRange(s_Entries.Where(e => e.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Select(e => e.Path));
            return list;
        }

        // ------------------------------------------------------------------ delete, trash

        /// <summary>Deletes the asset (a folder with everything in it) and its <c>.meta</c>;
        /// false when nothing is there (tests/projects/projectwrites 06.delete).</summary>
        public static bool DeleteAsset(string path) => Remove(Normalize(path), trash: false);

        public static bool DeleteAssets(string[] paths, List<string> outFailedPaths) => RemoveAll(paths, outFailedPaths, trash: false);

        /// <summary>Moves the asset and its <c>.meta</c> to the user's trash; false when nothing
        /// is there.</summary>
        public static bool MoveAssetToTrash(string path) => Remove(Normalize(path), trash: true);

        public static bool MoveAssetsToTrash(string[] paths, List<string> outFailedPaths) => RemoveAll(paths, outFailedPaths, trash: true);

        static bool RemoveAll(string[] paths, List<string> outFailedPaths, bool trash)
        {
            bool all = true;
            foreach (var path in paths ?? Array.Empty<string>())
            {
                if (Remove(Normalize(path), trash)) continue;
                all = false;
                outFailedPaths?.Add(path);
            }
            return all;
        }

        static bool Remove(string path, bool trash)
        {
            if (string.IsNullOrEmpty(path) || !Exists(path)) return false;
            if (!Editable(path))
            {
                // An asset of a read-only package is not deleted: false, nothing logged
                // (tests/projects/projectwrites 06.readonly); other paths are said so.
                if (!InReadOnlyPackage(path) && !IsRoot(path)) Refused(trash ? "AssetDatabase.MoveAssetToTrash" : "AssetDatabase.DeleteAsset", path);
                return false;
            }
            var removed = TreeOf(path);
            foreach (var p in removed)
            {
                var guid = ExistingGuid(p);
                if (string.IsNullOrEmpty(guid)) continue;
                s_DeletedGuidByPath[p] = guid;
                s_DeletedPathByGuid[guid] = p;
                AssetDatabaseRuntime.Invalidate(guid);
                AssetDatabaseRuntime.ForgetPersistentIds(guid);
            }
            var file = FileOf(path);
            if (trash) Trash.Move(file);
            else if (Directory.Exists(file)) Directory.Delete(file, true);
            else File.Delete(file);
            if (File.Exists(file + ".meta"))
            {
                if (trash) Trash.Move(file + ".meta");
                else File.Delete(file + ".meta");
            }
            Import(removed.Select(p => new ImportRequest { Path = p }));
            return true;
        }

        static string DeletedGuid(string path) => path != null && s_DeletedGuidByPath.TryGetValue(path, out var guid) ? guid : "";

        static string DeletedPath(string guid) => guid != null && s_DeletedPathByGuid.TryGetValue(guid, out var path) ? path : "";

        // ------------------------------------------------------------------ labels


        /// <summary>The labels written into the asset's <c>.meta</c> at once, after its GUID, in
        /// ordinal order with repeats kept; none: no labels block (matches the Unity 6000.0.71f1
        /// Editor, tests/projects/projectwrites 09.labels).</summary>
        public static void SetLabels(Object obj, string[] labels)
        {
            var path = obj != null ? GetAssetPath(obj) : null;
            if (string.IsNullOrEmpty(path)) return;
            if (!Editable(path))
            {
                Refused("AssetDatabase.SetLabels", path);
                return;
            }
            var meta = FileOf(path) + ".meta";
            if (!File.Exists(meta)) return;
            // A script's .meta written again gets its importer's section (09.labels.script).
            var section = path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ? ScriptMetaSection : null;
            try
            {
                var text = File.ReadAllText(meta);
                // A plug-in's .meta written again gets its PluginImporter section, with settings
                // that depend on the plug-in's kind, folder and the installed platform modules: not
                // measured, so the labels are written without it.
                if (string.IsNullOrEmpty(section) && !AssetPaths.HasImporterSection(text))
                    Debug.LogWarning($"Unbound: {path}.meta is written without its importer's settings: Unbound does not know the settings the Editor writes there yet.");
                File.WriteAllText(meta, AssetPaths.WithLabels(text, labels, section));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogError($"AssetDatabase.SetLabels: writing {path}.meta: {e.Message}");
                return;
            }
            ImportOne(path);
        }

        public static void ClearLabels(Object obj) => SetLabels(obj, Array.Empty<string>());

        static string s_ScriptMetaSection;

        /// <summary>The MonoImporter section a script's .meta gets when it is written again
        /// (empty for a release family whose files are not measured).</summary>
        static string ScriptMetaSection => s_ScriptMetaSection ??=
            JsonDocument.Parse(Native.Call("script-meta-section", "{}")).RootElement.GetProperty("section").GetString() ?? "";
    }
}

namespace Unbound.Runtime
{
    /// <summary>The user's trash, where AssetDatabase.MoveAssetToTrash puts files: on macOS
    /// <c>~/.Trash</c>, elsewhere the freedesktop.org trash (<c>$XDG_DATA_HOME/Trash</c>, with
    /// the <c>.trashinfo</c> file that says where it came from).</summary>
    internal static class Trash
    {
        internal static void Move(string file)
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string files, info = null;
            if (OperatingSystem.IsMacOS()) files = Path.Combine(home, ".Trash");
            else
            {
                var data = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
                var trash = Path.Combine(string.IsNullOrEmpty(data) ? Path.Combine(home, ".local", "share") : data, "Trash");
                files = Path.Combine(trash, "files");
                info = Path.Combine(trash, "info");
                Directory.CreateDirectory(info);
            }
            Directory.CreateDirectory(files);
            var name = Path.GetFileName(file);
            var target = Path.Combine(files, name);
            for (int i = 2; File.Exists(target) || Directory.Exists(target); i++)
                target = Path.Combine(files, Path.GetFileNameWithoutExtension(name) + " " + i + Path.GetExtension(name));
            if (info != null)
                File.WriteAllText(Path.Combine(info, Path.GetFileName(target) + ".trashinfo"),
                    "[Trash Info]\nPath=" + Uri.EscapeDataString(file).Replace("%2F", "/") + "\nDeletionDate=" + DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss") + "\n");
            try
            {
                if (Directory.Exists(file)) Directory.Move(file, target);
                else File.Move(file, target);
            }
            catch (IOException) when (Directory.Exists(file))
            {
                // Another volume: copied into the trash, then removed here.
                CopyAll(file, target);
                Directory.Delete(file, true);
            }
        }

        /// <summary>A folder copied file by file (a move across volumes, a copy of a folder plug-in).</summary>
        internal static void CopyAll(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (var f in Directory.EnumerateFiles(source)) File.Copy(f, Path.Combine(target, Path.GetFileName(f)));
            foreach (var d in Directory.EnumerateDirectories(source)) CopyAll(d, Path.Combine(target, Path.GetFileName(d)));
        }
    }
}
