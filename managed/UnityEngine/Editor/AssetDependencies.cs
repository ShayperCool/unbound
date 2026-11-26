using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Unbound.Runtime;

namespace UnityEditor
{
    public sealed partial class AssetDatabase
    {
        // ------------------------------------------------------------------ GetDependencies

        // Matches the Unity 6000.0.71f1 Editor, tests/projects/assetdependencies: an asset's direct
        // dependencies are the other listed assets its saved data references (a native asset's
        // object references, a shader's default and non-modifiable textures), not itself, not the
        // built-in resources and not a GUID no asset has; a script's default references, a shader's
        // includes, a texture's, a text's and a folder's are none. The recursive answer holds the
        // given assets and every asset reached through them. An unsaved change is not seen (it
        // reads the files). Paths are read as AssetPathToGUID reads them (any case, either slash),
        // but for a trailing slash; null, empty and unknown paths answer nothing. The Editor's
        // order is not documented (it is not sorted); Unbound answers in ordinal path order.

        public static string[] GetDependencies(string pathName) => GetDependencies(pathName, true);

        public static string[] GetDependencies(string pathName, bool recursive) => GetDependencies(new[] { pathName }, recursive);

        public static string[] GetDependencies(string[] pathNames) => GetDependencies(pathNames, true);

        public static string[] GetDependencies(string[] pathNames, bool recursive)
        {
            if (pathNames == null) return Array.Empty<string>();
            _ = Entries;
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pending = new Queue<Entry>();
            foreach (var path in pathNames)
            {
                if (DependencyAsset(path) is not { } asset) continue;
                if (recursive)
                {
                    if (found.Add(asset.Guid)) pending.Enqueue(asset);
                }
                else
                    foreach (var dependency in DirectDependencies(asset)) found.Add(dependency.Guid);
            }
            while (pending.Count > 0)
                foreach (var dependency in DirectDependencies(pending.Dequeue()))
                    if (found.Add(dependency.Guid)) pending.Enqueue(dependency);
            var paths = new List<string>(found.Count);
            foreach (var guid in found)
                if (s_ByGuid.TryGetValue(guid, out var e)) paths.Add(e.Path);
            paths.Sort(StringComparer.Ordinal);
            return paths.ToArray();
        }

        /// <summary>The listed asset a path names for GetDependencies: either slash, any case, not a
        /// trailing slash (tests/projects/assetdependencies odd.*).</summary>
        static Entry DependencyAsset(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            return s_ByPath.TryGetValue(path.Replace('\\', '/'), out var e) ? e : null;
        }

        /// <summary>An asset's direct dependencies as GUIDs, by its file, with the write times of the
        /// file and its .meta they were read from.</summary>
        static readonly Dictionary<string, (DateTime File, DateTime Meta, string[] Guids)> s_Dependencies =
            new Dictionary<string, (DateTime, DateTime, string[])>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The importers whose assets reference no other asset (tests/projects/assetdependencies
        /// deps.Tex.png, DepBehaviour.cs, Text.txt, DepInclude.cginc).</summary>
        static readonly HashSet<string> s_ImportersWithoutReferences = new HashSet<string>(StringComparer.Ordinal)
        {
            "TextureImporter", "MonoImporter", "TextScriptImporter", "ShaderIncludeImporter",
        };

        static IEnumerable<Entry> DirectDependencies(Entry asset)
        {
            foreach (var guid in DirectGuids(asset))
                if (!string.Equals(guid, asset.Guid, StringComparison.OrdinalIgnoreCase) && s_ByGuid.TryGetValue(guid, out var e))
                    yield return e;
        }

        static string[] DirectGuids(Entry asset)
        {
            if (asset.Kind == "folder") return Array.Empty<string>();
            var file = FileOf(asset.Path);
            var meta = file + ".meta";
            DateTime fileTime, metaTime;
            try
            {
                fileTime = File.GetLastWriteTimeUtc(file);
                metaTime = File.GetLastWriteTimeUtc(meta);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return Array.Empty<string>();
            }
            if (s_Dependencies.TryGetValue(file, out var cached) && cached.File == fileTime && cached.Meta == metaTime) return cached.Guids;
            var guids = ReadDirectGuids(asset, file, meta);
            s_Dependencies[file] = (fileTime, metaTime, guids);
            return guids;
        }

        static string[] ReadDirectGuids(Entry asset, string file, string meta)
        {
            try
            {
                // A text-serialized native asset (a material, a prefab, a scene, a ScriptableObject):
                // the objects it references.
                if (IsTextSerialized(file)) return ReferencedGuids(File.ReadAllText(file));
                // A shader: the default and non-modifiable textures its importer gives it.
                if (asset.Importer == "ShaderImporter") return File.Exists(meta) ? ReferencedGuids(File.ReadAllText(meta)) : Array.Empty<string>();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return Array.Empty<string>();
            }
            if (asset.Importer != null && !s_ImportersWithoutReferences.Contains(asset.Importer))
                Stubs.Ignored("AssetDatabase.GetDependencies(" + asset.Importer + ")",
                    "does not read the references of " + asset.Importer + " assets yet (" + asset.Path + "): it answers none of them");
            return Array.Empty<string>();
        }

        /// <summary>Whether a file holds Unity's text serialization (it starts with its YAML header).</summary>
        static bool IsTextSerialized(string file)
        {
            if (!File.Exists(file)) return false;
            using var stream = File.OpenRead(file);
            Span<byte> head = stackalloc byte[5];
            return stream.Read(head) == 5 && head.SequenceEqual("%YAML"u8);
        }

        /// <summary>The GUIDs of the other files a text-serialized asset or a .meta references
        /// (<see cref="s_Reference"/>).</summary>
        static string[] ReferencedGuids(string text)
        {
            var guids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in s_Reference.Matches(text))
                if (m.Groups[2].Success) guids.Add(m.Groups[2].Value);
            return guids.ToArray();
        }
    }
}
