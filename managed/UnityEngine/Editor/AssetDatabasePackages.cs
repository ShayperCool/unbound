using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Unbound.Runtime;
using UnityEngine;

namespace UnityEditor
{
    public sealed partial class AssetDatabase
    {
        public static event ImportPackageCallback importPackageStarted;
        public static event ImportPackageCallback importPackageCompleted;
        public static event ImportPackageFailedCallback importPackageFailed;

        /// <summary>Called after <see cref="importPackageCompleted"/> with the project paths of
        /// the package's items.</summary>
        public static Action<string[]> onImportPackageItemsCompleted;

        public static void ImportPackage(string packagePath, bool interactive) => PackageImports.Import(packagePath, interactive);

        internal static void RaisePackageStarted(string name) => importPackageStarted?.Invoke(name);

        internal static void RaisePackageCompleted(string name, string[] items)
        {
            importPackageCompleted?.Invoke(name);
            onImportPackageItemsCompleted?.Invoke(items);
        }

        internal static void RaisePackageFailed(string name, string error) => importPackageFailed?.Invoke(name, error);

        /// <summary>The database brought up to date with the files an import wrote: new and
        /// changed assets, and a path whose .meta names another GUID now.</summary>
        internal static void RefreshWritten() => ApplyRefresh(Native.Call("asset-refresh", "{}"));
    }

    /// <summary>
    /// AssetDatabase.ImportPackage of a <c>.unitypackage</c> (a gzipped tar with a folder per
    /// GUID: <c>pathname</c>, and for a file <c>asset</c>, and <c>asset.meta</c>), without the
    /// import window, as the Unity 6000.0.71f1 Editor does it in batch mode
    /// (tests/projects/projectwrites 14.package).
    /// <para>In Play mode nothing is imported: two errors and importPackageFailed. In Edit mode
    /// a file that is missing or no such archive fails the same way at once. Otherwise
    /// importPackageStarted is raised in the call, with nothing written yet; the Editor unpacks
    /// in the background and finishes some updates later, Unbound with the next editor update:
    /// every item's file and <c>.meta</c> are written over what is there (a folder's <c>.meta</c>
    /// only; folders the package does not list are made and get a new <c>.meta</c> like any new
    /// folder; an item without a <c>.meta</c> gets one of its GUID alone), the assets are
    /// imported, then importPackageCompleted and onImportPackageItemsCompleted are raised.</para>
    /// <para>An item whose GUID an asset of the project has goes to that asset's path, whatever
    /// path the package names. An item at the path of an asset with another GUID replaces it,
    /// GUID too, with a warning.</para>
    /// </summary>
    static class PackageImports
    {
        /// <summary>What importPackageFailed is given, whatever failed.</summary>
        const string FailedEvent = "Failed to unpack package contents";

        internal sealed class Item
        {
            internal string Guid, Path;
            /// <summary>Null for a folder.</summary>
            internal byte[] Asset;
            /// <summary>Null when the package has none for the item.</summary>
            internal byte[] Meta;
        }

        sealed class Pending
        {
            internal string Name;
            internal List<Item> Items;
        }

        static readonly List<Pending> s_Pending = new List<Pending>();

        /// <summary>The managed tests' stand-in for the engine's refresh.</summary>
        internal static Action RefreshForTests;

        internal static void Import(string packagePath, bool interactive)
        {
            var name = string.IsNullOrEmpty(packagePath) ? "" : Path.GetFileNameWithoutExtension(packagePath);
            if (EditorApplication.isPlaying)
            {
                Fail(name, "Cannot import package in play mode.");
                return;
            }
            var items = string.IsNullOrEmpty(packagePath) ? null : Unpack(Path.Combine(ProjectInfo.ProjectPath, packagePath));
            if (items == null)
            {
                Fail(name, "Couldn't decompress package");
                return;
            }
            // The Editor's window lets the user leave items out or cancel (in batch mode it never
            // comes and the import never ends); Unbound has none and imports the whole package.
            if (interactive) Stubs.Ignored("AssetDatabase.ImportPackage's import window", "imports the whole package");
            AssetDatabase.RaisePackageStarted(name);
            if (s_Pending.Count == 0) EditorApplication.tick += Finish;
            s_Pending.Add(new Pending { Name = name, Items = items });
        }

        /// <summary>The Editor logs the error twice (tests/projects/projectwrites 14.package.*.logs).</summary>
        static void Fail(string name, string reason)
        {
            for (int i = 0; i < 2; i++) Debug.LogError("Failed to import package with error: " + reason);
            AssetDatabase.RaisePackageFailed(name, FailedEvent);
        }

        /// <summary>The items of a package file in its order; null when the file is missing or no
        /// gzipped tar. Entries that are no part of an item (no <c>pathname</c>) are passed over.</summary>
        internal static List<Item> Unpack(string file)
        {
            var items = new List<Item>();
            var byGuid = new Dictionary<string, Item>(StringComparer.Ordinal);
            var paths = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                using var stream = File.OpenRead(file);
                using var unzipped = new GZipStream(stream, CompressionMode.Decompress);
                using var tar = new TarReader(unzipped);
                while (tar.GetNextEntry() is TarEntry entry)
                {
                    if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) || entry.DataStream == null) continue;
                    var parts = entry.Name.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).Where(p => p != ".").ToArray();
                    if (parts.Length != 2) continue;
                    using var data = new MemoryStream();
                    entry.DataStream.CopyTo(data);
                    if (!byGuid.TryGetValue(parts[0], out var item))
                    {
                        byGuid[parts[0]] = item = new Item { Guid = parts[0] };
                        items.Add(item);
                    }
                    switch (parts[1])
                    {
                        // The path is the first line (packages of some releases add a second).
                        case "pathname": item.Path = Encoding.UTF8.GetString(data.ToArray()).Split('\n')[0].Trim().Replace('\\', '/').TrimEnd('/'); break;
                        case "asset": item.Asset = data.ToArray(); break;
                        case "asset.meta": item.Meta = data.ToArray(); break;
                    }
                }
            }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or FormatException or ArgumentException or NotSupportedException)
            {
                return null;
            }
            items.RemoveAll(item => string.IsNullOrEmpty(item.Path));
            return items;
        }

        /// <summary>The next editor update after ImportPackage: the imports asked for until then,
        /// in order. One asked for by their events ends with a later update.</summary>
        static void Finish()
        {
            EditorApplication.tick -= Finish;
            var batch = s_Pending.ToArray();
            s_Pending.Clear();
            foreach (var pending in batch)
            {
                string[] written;
                try { written = Write(pending.Items); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // Files that cannot be written end the import as a failed one (what the
                    // Editor says then is not measured).
                    Fail(pending.Name, e.Message);
                    continue;
                }
                AssetDatabase.RaisePackageCompleted(pending.Name, written);
            }
        }

        /// <summary>Writes the items and imports them; the project paths they went to. When a
        /// file cannot be written the exception goes on, after what was written is imported.</summary>
        internal static string[] Write(List<Item> items)
        {
            var written = new List<string>();
            var replaced = new List<string>();
            try
            {
                foreach (var item in items)
                {
                    // An asset of the item's GUID keeps its place.
                    var owner = AssetDatabase.GUIDToAssetPath(item.Guid);
                    var path = string.IsNullOrEmpty(owner) ? item.Path : owner;
                    if (path.Split('/').Contains("..") || !AssetDatabase.Editable(path))
                    {
                        AssetDatabase.Refused("AssetDatabase.ImportPackage", path);
                        continue;
                    }
                    var before = AssetDatabase.AssetPathToGUID(path, AssetPathToGUIDOptions.OnlyExistingAssets);
                    if (before.Length > 0)
                    {
                        replaced.Add(before);
                        if (before != item.Guid)
                            Debug.LogWarning($"Overriding existing asset with new GUID from package, this may break links within your project to package assets ({path})");
                    }
                    // Whatever was loaded under the item's GUID (also of an asset deleted since) is
                    // read again when next asked for.
                    replaced.Add(item.Guid);
                    var file = AssetDatabase.FileOf(path);
                    if (item.Asset == null) Directory.CreateDirectory(file);
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(file));
                        File.WriteAllBytes(file, item.Asset);
                    }
                    File.WriteAllBytes(file + ".meta", item.Meta ?? Encoding.UTF8.GetBytes("fileFormatVersion: 2\nguid: " + item.Guid + "\n"));
                    written.Add(path);
                }
            }
            finally
            {
                if (RefreshForTests != null) RefreshForTests();
                else AssetDatabase.RefreshWritten();
                // What was loaded of a replaced asset is read again when next asked for.
                foreach (var guid in replaced) AssetDatabaseRuntime.Invalidate(guid);
            }
            return written.ToArray();
        }
    }
}
