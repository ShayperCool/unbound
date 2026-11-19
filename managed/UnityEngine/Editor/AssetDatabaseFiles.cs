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

        sealed class ImportResult
        {
            public string[] Guids { get; set; }
        }

        /// <summary>The engine's asset database brought up to date for these paths (a missing
        /// <c>.meta</c> written, a gone asset dropped); each path's GUID, "" for a gone one.</summary>
        internal static string[] Import(IEnumerable<ImportRequest> requests)
        {
            var list = requests.ToList();
            if (list.Count == 0) return Array.Empty<string>();
            var json = JsonSerializer.Serialize(new { paths = list }, s_Json);
            var result = JsonSerializer.Deserialize<ImportResult>(Native.Call("asset-import", json), s_Json);
            Invalidate();
            return result?.Guids ?? Array.Empty<string>();
        }

        static string ImportOne(string path, long? main = null) =>
            Import(new[] { new ImportRequest { Path = path, Main = main } }).FirstOrDefault() ?? "";

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
            if (s_Editing > 0)
            {
                s_PendingImports.Add(path);
                return;
            }
            var folders = new List<string>();
            for (var folder = Path.GetDirectoryName(path)?.Replace('\\', '/'); !string.IsNullOrEmpty(folder) && folder != "Assets" && folder != "Packages" && folder.Contains('/'); folder = Path.GetDirectoryName(folder)?.Replace('\\', '/'))
            {
                if (File.Exists(FileOf(folder) + ".meta")) break;
                folders.Add(folder);
            }
            folders.Reverse();
            Import(folders.Append(path).Select(p => new ImportRequest { Path = p }));
            // The import runs the asset's preprocessors (whose settings changes its .meta keeps).
            AssetPostprocessing.EnsurePreprocessed(path, Kind(path));
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
            Native.Call("asset-refresh", "{}");
            Invalidate();
        }

        /// <summary>Holds back imports (Refresh, ImportAsset of new files) until the matching
        /// StopAssetEditing; calls nest (tests/projects/projectwrites 07.batch).</summary>
        public static void StartAssetEditing() => s_Editing++;

        public static void StopAssetEditing()
        {
            if (s_Editing == 0) return;
            if (--s_Editing > 0) return;
            var pending = s_PendingImports.ToList();
            s_PendingImports.Clear();
            foreach (var path in pending) ImportNew(path);
            if (s_PendingRefresh)
            {
                s_PendingRefresh = false;
                Refresh();
            }
        }

        // ------------------------------------------------------------------ folders and paths

        /// <summary>A new folder in <paramref name="parentFolder"/>, named uniquely as
        /// GenerateUniqueAssetPath names it; its GUID, "" when the parent is not a folder.</summary>
        public static string CreateFolder(string parentFolder, string newFolderName)
        {
            parentFolder = Normalize(parentFolder);
            if (!IsValidFolder(parentFolder) || string.IsNullOrEmpty(newFolderName)) return "";
            var path = GenerateUniqueAssetPath(parentFolder + "/" + newFolderName);
            if (string.IsNullOrEmpty(path)) return "";
            Directory.CreateDirectory(FileOf(path));
            return ImportOne(path);
        }

        static readonly Regex s_Numbered = new Regex("^(.*) ([0-9]+)$");

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
            var name = path.Substring(slash + 1);
            var candidate = folder + "/" + name;
            if (!Taken(candidate)) return candidate;
            var extension = Path.GetExtension(name);
            var stem = name.Substring(0, name.Length - extension.Length);
            var numbered = s_Numbered.Match(stem);
            if (numbered.Success) stem = numbered.Groups[1].Value;
            for (int i = 1; ; i++)
            {
                candidate = folder + "/" + stem + " " + i + extension;
                if (!Taken(candidate)) return candidate;
            }
        }

        static bool Taken(string path) => Exists(path) || File.Exists(FileOf(path) + ".meta");

        /// <summary>A folder path as the project spells it, or null when there is none.</summary>
        static string ProjectFolder(string folder)
        {
            if (string.Equals(folder, "Assets", StringComparison.OrdinalIgnoreCase)) return "Assets";
            if (!folder.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) && !folder.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase)) return null;
            _ = Entries;
            if (s_ByPath.TryGetValue(folder, out var entry) && entry.Kind == "folder") return entry.Path;
            return Directory.Exists(FileOf(folder)) ? folder : null;
        }

        // ------------------------------------------------------------------ copy, move, rename

        static readonly Regex s_GuidLine = new Regex("^guid: [0-9a-fA-F]{32}$", RegexOptions.Multiline);

        /// <summary>A copy of the asset at <paramref name="newPath"/> with a new GUID; its
        /// <c>.meta</c> is the original's with that GUID (tests/projects/projectwrites 05.copy).</summary>
        public static bool CopyAsset(string path, string newPath)
        {
            path = Normalize(path);
            newPath = Normalize(newPath);
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(newPath) || !Exists(path) || Taken(newPath)) return false;
            var parent = Path.GetDirectoryName(newPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(parent) || ProjectFolder(parent) == null) return false;
            var copied = new List<string>();
            CopyTree(FileOf(path), FileOf(newPath), newPath, copied);
            RenameMainObject(newPath);
            Import(copied.Select(p => new ImportRequest { Path = p }));
            return true;
        }

        static void CopyTree(string source, string target, string assetPath, List<string> copied)
        {
            if (Directory.Exists(source))
            {
                Directory.CreateDirectory(target);
                foreach (var entry in Directory.EnumerateFileSystemEntries(source).OrderBy(e => e, StringComparer.Ordinal))
                {
                    var name = Path.GetFileName(entry);
                    if (name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                    CopyTree(entry, Path.Combine(target, name), assetPath + "/" + name, copied);
                }
            }
            else File.Copy(source, target);
            if (File.Exists(source + ".meta"))
            {
                var meta = File.ReadAllText(source + ".meta");
                File.WriteAllText(target + ".meta", s_GuidLine.Replace(meta, "guid: " + GUID.Generate(), 1));
            }
            copied.Add(assetPath);
        }

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

        static readonly Regex s_MainObjectFileId = new Regex("^  mainObjectFileID: (-?[0-9]+)$", RegexOptions.Multiline);
        static readonly Regex s_DocumentHeader = new Regex("^--- !u![0-9]+ &(-?[0-9]+).*$", RegexOptions.Multiline);
        static readonly Regex s_NameLine = new Regex("^  m_Name: .*$", RegexOptions.Multiline);

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
            var text = File.ReadAllText(file);
            var name = Path.GetFileNameWithoutExtension(path);
            var headers = s_DocumentHeader.Matches(text);
            for (int i = 0; i < headers.Count; i++)
            {
                if (headers[i].Groups[1].Value != main.Groups[1].Value) continue;
                int start = headers[i].Index, end = i + 1 < headers.Count ? headers[i + 1].Index : text.Length;
                var document = text.Substring(start, end - start);
                var line = s_NameLine.Match(document);
                if (!line.Success) return;
                File.WriteAllText(file, text.Substring(0, start) + document.Substring(0, line.Index) + "  m_Name: " + name + document.Substring(line.Index + line.Length) + text.Substring(end));
                var guid = s_GuidLine.Match(meta);
                if (guid.Success && AssetDatabaseRuntime.IsMainLoaded(guid.Value.Substring("guid: ".Length)))
                    AssetDatabaseRuntime.LoadMain(guid.Value.Substring("guid: ".Length), null).name = name;
                return;
            }
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
            if (string.IsNullOrEmpty(path) || !Exists(path) || path == "Assets") return false;
            var removed = TreeOf(path);
            foreach (var p in removed)
            {
                var guid = ExistingGuid(p);
                if (string.IsNullOrEmpty(guid)) continue;
                s_DeletedGuidByPath[p] = guid;
                s_DeletedPathByGuid[guid] = p;
                AssetDatabaseRuntime.Invalidate(guid);
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

        static readonly Regex s_LabelsBlock = new Regex("^labels:\n(?:- .*\n)*", RegexOptions.Multiline);

        /// <summary>The labels written into the asset's <c>.meta</c> at once, after its GUID, in
        /// ordinal order with repeats kept; none: no labels block (matches the Unity 6000.0.71f1
        /// Editor, tests/projects/projectwrites 09.labels).</summary>
        public static void SetLabels(Object obj, string[] labels)
        {
            var path = obj != null ? GetAssetPath(obj) : null;
            if (string.IsNullOrEmpty(path)) return;
            var meta = FileOf(path) + ".meta";
            if (!File.Exists(meta)) return;
            var text = s_LabelsBlock.Replace(File.ReadAllText(meta), "", 1);
            var sorted = (labels ?? Array.Empty<string>()).Where(l => !string.IsNullOrEmpty(l)).OrderBy(l => l, StringComparer.Ordinal).ToArray();
            if (sorted.Length > 0)
            {
                var block = new StringBuilder("labels:\n");
                foreach (var label in sorted) block.Append("- ").Append(label).Append('\n');
                var guid = s_GuidLine.Match(text);
                int at = guid.Success ? guid.Index + guid.Length + 1 : text.Length;
                text = text.Insert(Math.Min(at, text.Length), block.ToString());
            }
            File.WriteAllText(meta, text);
            ImportOne(path);
        }

        public static void ClearLabels(Object obj) => SetLabels(obj, Array.Empty<string>());
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
            if (Directory.Exists(file)) Directory.Move(file, target);
            else File.Move(file, target);
        }
    }
}
