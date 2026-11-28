using System;
using System.Collections.Generic;
using System.IO;
using Unbound.Runtime;

namespace UnityEditor
{
    public sealed partial class AssetDatabase
    {
        [Unbound.ImplementedIn(Compat.SelectedAssetPathFamily)]
        internal static string GetUniquePathNameAtSelectedPath(string fileName)
        {
            const string member = "UnityEditor.AssetDatabase.GetUniquePathNameAtSelectedPath";
            if (!Compat.SelectedAssetPathMeasured)
            {
                Stubs.Ignored(member, "selection-based paths are unmeasured for this release family");
                return null;
            }

            string name = SelectedAssetName(fileName);
            if (name.Length == 0) return "";

            string path = GetAssetPath(Selection.activeObject);
            string parent = IsValidFolder(path) ? path : ProjectWindowUtil.GetContainingFolder(path);
            string folder = parent == null ? null : ProjectFolder(parent);
            folder ??= "Assets";
            try
            {
                var physicalFolder = FileOf(folder);
                var entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in Directory.EnumerateFileSystemEntries(physicalFolder))
                    entries.Add(Path.GetFileName(entry));
                return AssetPaths.Unique(folder, name, candidate => entries.Contains(Path.GetFileName(candidate)),
                    continueNumberedName: true);
            }
            catch (DirectoryNotFoundException)
            {
                return "";
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Stubs.Ignored(member + " for inaccessible folders", "the selected folder cannot be listed");
                return "";
            }
        }

        static string SelectedAssetName(string name)
        {
            if (name == null) return "";
            var trimmed = name.AsSpan().TrimStart(' ');
            if (trimmed.IsEmpty) return "";
            var chars = trimmed.ToArray();
            for (int i = 0; i < chars.Length; i++)
                if (chars[i] is '\0' or '\\' or '/' or ':' or '*' or '?' or '|' or '<' or '>' or '"') chars[i] = '_';
            if (chars[0] == '.') chars[0] = '_';
            if (chars[^1] is '.' or ' ') chars[^1] = '_';

            ReadOnlySpan<char> stem = chars;
            int dot = stem.IndexOf('.');
            if (dot >= 0) stem = stem[..dot];
            const StringComparison compare = StringComparison.OrdinalIgnoreCase;
            if (stem.Equals("CON", compare) || stem.Equals("NUL", compare) || stem.Equals("PRN", compare)
                || stem.Equals("AUX", compare) || (stem.Length == 4 && char.IsAsciiDigit(stem[3])
                    && (stem[..3].Equals("COM", compare) || stem[..3].Equals("LPT", compare)))) return "";
            return new string(chars);
        }
    }
}
