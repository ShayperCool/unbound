using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
                var entries = new HashSet<string>(StringComparer.Ordinal);
                foreach (var entry in Directory.EnumerateFileSystemEntries(physicalFolder))
                    entries.Add(FoldAsciiCase(Path.GetFileName(entry)));

                bool Taken(string candidate)
                {
                    string filename = Path.GetFileName(candidate);
                    if (entries.Contains(FoldAsciiCase(filename))) return true;
                    // The filesystem can recognize aliases beyond ASCII case, such as on macOS.
                    string physicalPath = Path.Combine(physicalFolder, filename);
                    return File.Exists(physicalPath) || Directory.Exists(physicalPath);
                }

                return AssetPaths.Unique(folder, name, Taken, continueNumberedName: true);
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

        static string FoldAsciiCase(string name)
        {
            if (name.AsSpan().IndexOfAnyInRange('A', 'Z') < 0) return name;
            return string.Create(name.Length, name, static (buffer, value) =>
            {
                for (int i = 0; i < value.Length; i++)
                {
                    char c = value[i];
                    buffer[i] = c is >= 'A' and <= 'Z' ? (char)(c + ('a' - 'A')) : c;
                }
            });
        }

        static string SelectedAssetName(string name)
        {
            if (name == null) return "";
            var trimmed = name.AsSpan().TrimStart(' ');
            if (trimmed.IsEmpty) return "";
            Span<char> chars = trimmed.ToArray();
            int length = 0;
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (char.IsHighSurrogate(c))
                {
                    // An invalid pair contributes one replacement character.
                    if (i + 1 < chars.Length && char.IsLowSurrogate(chars[++i]))
                    {
                        chars[length++] = c;
                        chars[length++] = chars[i];
                        continue;
                    }
                    c = '\uFFFD';
                }
                else if (char.IsLowSurrogate(c)) c = '\uFFFD';
                chars[length++] = c is '\0' or '\\' or '/' or ':' or '*' or '?' or '|' or '<' or '>' or '"' ? '_' : c;
            }
            chars = chars[..length];
            if (chars[0] == '.') chars[0] = '_';
            if (chars[^1] is '.' or ' ') chars[^1] = '_';

            ReadOnlySpan<char> stem = chars;
            int dot = stem.IndexOf('.');
            if (dot >= 0) stem = stem[..dot];
            const StringComparison compare = StringComparison.OrdinalIgnoreCase;
            if (stem.Equals("CON", compare) || stem.Equals("NUL", compare) || stem.Equals("PRN", compare)
                || stem.Equals("AUX", compare) || (stem.Length == 4 && char.IsAsciiDigit(stem[3])
                    && (stem[..3].Equals("COM", compare) || stem[..3].Equals("LPT", compare)))) return "";
            string result = new string(chars);
            return OperatingSystem.IsMacOS() ? result.Normalize(NormalizationForm.FormD) : result;
        }
    }
}
