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
        // object references, a shader's default and non-modifiable textures, a texture's sprites'
        // secondary textures), not itself, not the built-in resources and not a GUID no asset has; a
        // script's default references, a shader's includes, fallback and UsePass and Dependency
        // shaders, a text's and a folder's are none. The recursive answer holds the
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

        /// <summary>An asset's direct dependencies as GUIDs, by its file, with the write times and
        /// lengths of the file and its .meta they were read from.</summary>
        static readonly Dictionary<string, (Stamp File, Stamp Meta, string[] Guids)> s_Dependencies =
            new Dictionary<string, (Stamp, Stamp, string[])>(StringComparer.OrdinalIgnoreCase);

        readonly record struct Stamp(DateTime Time, long Length)
        {
            internal static Stamp Of(string file)
            {
                var info = new FileInfo(file);
                return info.Exists ? new Stamp(info.LastWriteTimeUtc, info.Length) : default;
            }
        }

        /// <summary>The importers whose assets reference no other asset (tests/projects/assetdependencies
        /// deps.DepBehaviour.cs: not its default references, Text.txt, DepInclude.cginc).</summary>
        static readonly HashSet<string> s_ImportersWithoutReferences = new HashSet<string>(StringComparer.Ordinal)
        {
            "MonoImporter", "TextScriptImporter", "ShaderIncludeImporter",
        };

        /// <summary>The importers whose assets reference what their .meta does: a shader its default and
        /// non-modifiable textures (and its Dependency shaders, <see cref="DependencyShaders"/>), a
        /// texture its sprites' secondary textures (tests/projects/assetdependencies deps.Custom.shader,
        /// Tex.png, Sec.png).</summary>
        static readonly HashSet<string> s_ImportersOfMetaReferences = new HashSet<string>(StringComparer.Ordinal)
        {
            "ShaderImporter", "TextureImporter",
        };

        /// <summary>The importers of Unity's own serialized assets, whose text-serialized files
        /// reference what their objects do (deps.Mat.mat, Data.asset, Prefab.prefab, Deps.unity).</summary>
        static bool IsNativeAsset(Entry asset) =>
            asset.Importer is "NativeFormatImporter" or "PrefabImporter" || (asset.Importer == "DefaultImporter" && asset.Kind == "scene");

        static IEnumerable<Entry> DirectDependencies(Entry asset)
        {
            foreach (var guid in DirectGuids(asset))
                if (!string.Equals(guid, asset.Guid, StringComparison.OrdinalIgnoreCase) && s_ByGuid.TryGetValue(guid, out var e))
                    yield return e;
            if (asset.Importer == "ShaderImporter")
                foreach (var e in DependencyShaders(asset))
                    if (!string.Equals(e.Guid, asset.Guid, StringComparison.OrdinalIgnoreCase)) yield return e;
        }

        // ------------------------------------------------------------------ shaders by name

        /// <summary>A shader's <c>Dependency "..." = "name"</c> declarations.</summary>
        static readonly Regex s_ShaderDependency = new Regex(@"\bDependency\s+""[^""]*""\s*=\s*""([^""]+)""", RegexOptions.CultureInvariant);

        /// <summary>The name a shader file declares first (<c>Shader "name"</c>).</summary>
        static readonly Regex s_ShaderName = new Regex(@"\bShader\s+""([^""]+)""", RegexOptions.CultureInvariant);

        /// <summary>A shader file's name and Dependency shaders' names, by its file and stamp.</summary>
        static readonly Dictionary<string, (Stamp Stamp, string Name, string[] Dependencies)> s_ShaderSources =
            new Dictionary<string, (Stamp, string, string[])>(StringComparer.OrdinalIgnoreCase);

        static (string Name, string[] Dependencies) ShaderSource(Entry shader)
        {
            var file = FileOf(shader.Path);
            try
            {
                var stamp = Stamp.Of(file);
                if (s_ShaderSources.TryGetValue(file, out var cached) && cached.Stamp == stamp) return (cached.Name, cached.Dependencies);
                var text = stamp.Length > 0 ? File.ReadAllText(file) : "";
                var name = s_ShaderName.Match(text) is { Success: true } m ? m.Groups[1].Value : null;
                var dependencies = s_ShaderDependency.Matches(text).Select(d => d.Groups[1].Value).Distinct(StringComparer.Ordinal).ToArray();
                s_ShaderSources[file] = (stamp, name, dependencies);
                return (name, dependencies);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return (null, Array.Empty<string>());
            }
        }

        /// <summary>The project's shaders a shader names in its <c>Dependency</c> declarations: once
        /// the named shader is imported, a dependency (matches the Unity 6000.0.71f1 Editor,
        /// tests/projects/assetdependencies resolved.Dependency; a shader its Fallback or UsePass
        /// names is none, resolved.Fall, resolved.UsePass). Built-in shaders are not listed.</summary>
        static IEnumerable<Entry> DependencyShaders(Entry asset)
        {
            var names = ShaderSource(asset).Dependencies;
            if (names.Length == 0) yield break;
            foreach (var e in s_Entries)
                if (e.Importer == "ShaderImporter" && ShaderSource(e).Name is { } name && Array.IndexOf(names, name) >= 0)
                    yield return e;
        }

        static string[] DirectGuids(Entry asset)
        {
            if (asset.Kind == "folder") return Array.Empty<string>();
            var file = FileOf(asset.Path);
            var meta = file + ".meta";
            Stamp fileStamp, metaStamp;
            try
            {
                fileStamp = Stamp.Of(file);
                metaStamp = Stamp.Of(meta);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return Array.Empty<string>();
            }
            if (s_Dependencies.TryGetValue(file, out var cached) && cached.File == fileStamp && cached.Meta == metaStamp) return cached.Guids;
            var guids = ReadDirectGuids(asset, file, meta);
            s_Dependencies[file] = (fileStamp, metaStamp, guids);
            return guids;
        }

        /// <summary>An importer section a .meta names, and whether it overrides the path's importer.</summary>
        static readonly Regex s_MetaImporter = new Regex(@"(?m)^(\w+Importer):", RegexOptions.CultureInvariant);

        static string[] ReadDirectGuids(Entry asset, string file, string meta)
        {
            if (!File.Exists(file)) return Array.Empty<string>();
            // An asset another importer imports than its path's (a scripted importer's override).
            if (File.Exists(meta))
            {
                var text = File.ReadAllText(meta);
                var section = s_MetaImporter.Match(text) is { Success: true } m ? m.Groups[1].Value : null;
                if (text.Contains("\nimporterOverride:", StringComparison.Ordinal) || (section != null && section != asset.Importer))
                {
                    Stubs.Ignored("AssetDatabase.GetDependencies(" + (section ?? "override") + ")",
                        "does not read the references of " + (section ?? "overridden") + " assets yet (" + asset.Path + "): it answers none of them");
                    return Array.Empty<string>();
                }
            }
            if (asset.Importer != null && s_ImportersWithoutReferences.Contains(asset.Importer)) return Array.Empty<string>();
            try
            {
                if (asset.Importer != null && s_ImportersOfMetaReferences.Contains(asset.Importer))
                    return File.Exists(meta) ? ReferencedGuids(File.ReadAllText(meta)) : Array.Empty<string>();
                if (IsNativeAsset(asset))
                {
                    if (IsTextSerialized(file)) return ReferencedGuids(File.ReadAllText(file));
                    Stubs.Ignored("AssetDatabase.GetDependencies(binary)",
                        "does not read the references of binary-serialized assets yet (" + asset.Path + "): it answers none of them");
                    return Array.Empty<string>();
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return Array.Empty<string>();
            }
            // Another file of the DefaultImporter (an unknown format) holds no object references.
            if (asset.Importer == "DefaultImporter") return Array.Empty<string>();
            if (asset.Importer != null)
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
