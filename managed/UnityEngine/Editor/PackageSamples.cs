using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using UnityEngine;

namespace UnityEditor.PackageManager.UI
{
    /// <summary>
    /// A package's sample (its package.json "samples"), as the Unity 6000.0.71f1 Editor finds and
    /// imports it (tests/projects/packagesamples): found by the installed package's name, and its
    /// version when one is given; imported by copying its folder (its .meta files as they are)
    /// into Assets/Samples/&lt;package display name&gt;/&lt;version&gt;/&lt;sample display name&gt;,
    /// names sanitized, then a Refresh.
    /// </summary>
    [Serializable]
    public partial struct Sample
    {
        public string displayName { get; private set; }

        public string description { get; private set; }

        public string resolvedPath { get; private set; }

        public string importPath { get; private set; }

        public bool interactiveImport { get; private set; }

        /// <summary>Whether its import folder exists.</summary>
        public bool isImported
        {
            get
            {
                try
                {
                    return !string.IsNullOrEmpty(importPath) && Directory.Exists(importPath);
                }
                catch (IOException e)
                {
                    Debug.Log($"[Package Manager Window] Cannot determine if sample {displayName} is imported: {e.Message}");
                    return false;
                }
            }
        }

        Sample(string displayName, string description, string resolvedPath, string importPath, bool interactiveImport)
        {
            this.displayName = displayName;
            this.description = description;
            this.resolvedPath = resolvedPath;
            this.importPath = importPath;
            this.interactiveImport = interactiveImport;
        }

        /// <summary>The samples of the installed package <paramref name="packageName"/>; none when
        /// <paramref name="packageVersion"/> is given and is not its version (packagesamples find.*).</summary>
        public static IEnumerable<Sample> FindByPackage(string packageName, string packageVersion)
        {
            var package = string.IsNullOrEmpty(packageName) ? null : PackageInfo.FindForPackageName(packageName);
            return package?.version == packageVersion || string.IsNullOrEmpty(packageVersion) ? Samples(package) : Enumerable.Empty<Sample>();
        }

        static IEnumerable<Sample> Samples(PackageInfo package)
        {
            if (package == null || string.IsNullOrEmpty(package.resolvedPath)) return Enumerable.Empty<Sample>();
            try
            {
                var json = Path.Combine(package.resolvedPath, "package.json");
                if (!File.Exists(json)) return Enumerable.Empty<Sample>();
                using var document = JsonDocument.Parse(File.ReadAllText(json));
                if (!document.RootElement.TryGetProperty("samples", out var samples) || samples.ValueKind != JsonValueKind.Array) return Enumerable.Empty<Sample>();
                var found = new List<Sample>();
                foreach (var sample in samples.EnumerateArray())
                {
                    var name = Text(sample, "displayName");
                    var importPath = Path.Combine(Application.dataPath, "Samples", SanitizeFileName(package.displayName), package.version,
                        string.IsNullOrEmpty(name) ? "" : SanitizeFileName(name));
                    found.Add(new Sample(name, Text(sample, "description"), Path.Combine(package.resolvedPath, Text(sample, "path") ?? ""),
                        importPath, sample.TryGetProperty("interactiveImport", out var interactive) && interactive.ValueKind == JsonValueKind.True));
                }
                return found;
            }
            catch (Exception e) when (e is IOException or JsonException or InvalidOperationException)
            {
                Debug.Log($"[Package Manager Window] Cannot find samples for package {package.displayName}: {e}");
                return Enumerable.Empty<Sample>();
            }
        }

        static string Text(JsonElement sample, string key) =>
            sample.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        /// <summary>A name with the characters a file name cannot hold, and those Unity avoids, as '_',
        /// trimmed (packagesamples: "Probe: Samples" -> "Probe_ Samples").</summary>
        static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            foreach (char c in "/:?<>*|\\~") name = name.Replace(c, '_');
            return name.Trim();
        }

        /// <summary>Imports the sample: a .unitypackage in its folder as a package, else its folder
        /// copied into <see cref="importPath"/> (none if an earlier import of it, of any version, is
        /// there and <see cref="ImportOptions.OverridePreviousImports"/> is not given; those removed
        /// with it), then a Refresh (packagesamples import.*, files.*).</summary>
        public bool Import(ImportOptions options = ImportOptions.None)
        {
            try
            {
                bool interactive = (options & ImportOptions.HideImportWindow) == ImportOptions.None && interactiveImport;
                var packages = Directory.GetFiles(resolvedPath, "*.unitypackage");
                if (packages.Length > 0)
                {
                    AssetDatabase.ImportPackage(packages[0], interactive);
                    return true;
                }
                var previous = PreviousImports();
                if (previous.Count > 0 && (options & ImportOptions.OverridePreviousImports) == ImportOptions.None) return false;
                foreach (var path in previous) RemovePathAndMeta(path);
                CopyFolder(resolvedPath, importPath);
                AssetDatabase.Refresh();
                return true;
            }
            catch (IOException e)
            {
                Debug.Log($"[Package Manager Window] Cannot import sample {displayName}: {e.Message}");
                return false;
            }
        }

        /// <summary>The sample's import folders of each version of its package.</summary>
        List<string> PreviousImports()
        {
            var found = new List<string>();
            if (string.IsNullOrEmpty(importPath)) return found;
            var root = Path.GetDirectoryName(Path.GetDirectoryName(importPath));
            if (root == null || !Directory.Exists(root)) return found;
            foreach (var version in Directory.GetDirectories(root, "*"))
            {
                var path = Path.Combine(version, Path.GetFileName(importPath));
                if (Directory.Exists(path)) found.Add(path);
            }
            return found;
        }

        /// <summary>Deletes a folder and its .meta, then each parent folder it leaves empty.</summary>
        static void RemovePathAndMeta(string path)
        {
            while (true)
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
                if (File.Exists(path + ".meta")) File.Delete(path + ".meta");
                var parent = Path.GetDirectoryName(path);
                if (parent == null || !Directory.Exists(parent) || Directory.EnumerateFileSystemEntries(parent).Any()) break;
                path = parent;
            }
        }

        /// <summary>Copies a folder's files and folders over another's, the copies writable.</summary>
        static void CopyFolder(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var folder in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(folder.Replace(source, destination));
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var copy = file.Replace(source, destination);
                File.Copy(file, copy, true);
                File.SetAttributes(copy, File.GetAttributes(copy) & ~FileAttributes.ReadOnly);
            }
        }

        [Flags]
        public enum ImportOptions
        {
            None = 0,
            OverridePreviousImports = 1,
            HideImportWindow = 2,
        }
    }
}
