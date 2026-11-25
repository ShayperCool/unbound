using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Unbound.Runtime
{
    /// <summary>
    /// The rules of project paths and of the <c>.meta</c> and asset text edits the AssetDatabase
    /// makes, as the Editor makes them (tests/projects/projectwrites): free names, which paths are
    /// assets of the project's own, and GUID, label and name edits (a <c>.meta</c> written again
    /// has LF endings).
    /// </summary>
    internal static class AssetPaths
    {
        static readonly Regex s_Numbered = new Regex("^(.*) ([0-9]+)$");

        /// <summary>The name itself when it is free in the folder, else the first free
        /// "stem N.ext" from 1 (a stem's own " N" replaced): tests/projects/projectwrites
        /// 05.unique.</summary>
        internal static string Unique(string folder, string name, Func<string, bool> taken)
        {
            var candidate = folder + "/" + name;
            if (!taken(candidate)) return candidate;
            var extension = System.IO.Path.GetExtension(name);
            var stem = name.Substring(0, name.Length - extension.Length);
            var numbered = s_Numbered.Match(stem);
            if (numbered.Success) stem = numbered.Groups[1].Value;
            for (int i = 1; ; i++)
            {
                candidate = folder + "/" + stem + " " + i + extension;
                if (!taken(candidate)) return candidate;
            }
        }

        /// <summary>A name the asset database skips, as the engine's (a leading '.', a trailing
        /// '~', <c>cvs</c> in any case, <c>.tmp</c> files).</summary>
        internal static bool HiddenName(string name) =>
            name.StartsWith(".", StringComparison.Ordinal) || name.EndsWith("~", StringComparison.Ordinal)
            || string.Equals(name, "cvs", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".tmp", StringComparison.Ordinal);

        /// <summary>Whether a project path has a hidden name below its root.</summary>
        internal static bool Hidden(string path) => path.Split('/').Skip(1).Any(HiddenName);

        static readonly string[] s_FolderPlugins = { ".bundle", ".framework", ".xcframework", ".plugin", ".androidlib" };

        /// <summary>A folder the Editor imports as one plug-in asset.</summary>
        internal static bool FolderPlugin(string path) =>
            s_FolderPlugins.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase));

        /// <summary>Whether the Editor writes at <paramref name="path"/>: below a root the project
        /// holds (Assets, an embedded or local package), not a root itself, not hidden, not inside
        /// a folder plug-in.</summary>
        internal static bool Editable(IEnumerable<(string Path, bool Editable)> roots, string path)
        {
            if (string.IsNullOrEmpty(path) || Hidden(path)) return false;
            (string Path, bool Editable)? best = null;
            foreach (var root in roots)
            {
                if (string.Equals(root.Path, path, StringComparison.OrdinalIgnoreCase)) return false;
                if (path.StartsWith(root.Path + "/", StringComparison.OrdinalIgnoreCase) && (best == null || root.Path.Length > best.Value.Path.Length))
                    best = root;
            }
            if (best?.Editable != true) return false;
            var inner = path.Substring(best.Value.Path.Length + 1).Split('/');
            for (int i = 0; i < inner.Length - 1; i++)
                if (FolderPlugin(inner[i])) return false;
            return true;
        }

        static readonly Regex s_GuidLine = new Regex("^guid: [0-9a-fA-F]{32}(?=\\r?$)", RegexOptions.Multiline);

        /// <summary>A <c>.meta</c> text with another GUID, with LF endings; null when it has no
        /// GUID line.</summary>
        internal static string WithGuid(string meta, string guid)
        {
            // The Editor writes .meta files with LF (tests/projects/projectwrites 06.crlf).
            meta = meta.Replace("\r\n", "\n");
            var line = s_GuidLine.Match(meta);
            return line.Success ? meta.Substring(0, line.Index) + "guid: " + guid + meta.Substring(line.Index + line.Length) : null;
        }

        static readonly Regex s_LabelsBlock = new Regex("^labels:\\r?\\n(?:- [^\\r\\n]*\\r?\\n)*", RegexOptions.Multiline);

        static readonly Regex s_ImporterSection = new Regex("^[A-Za-z]+:\\s*$", RegexOptions.Multiline);

        /// <summary>Whether a <c>.meta</c> text has its importer's section (a script's or a
        /// plug-in's new <c>.meta</c> has its GUID alone; a labels block is no section).</summary>
        internal static bool HasImporterSection(string meta)
        {
            foreach (Match match in s_ImporterSection.Matches(meta))
                if (!match.Value.StartsWith("labels:", StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>A <c>.meta</c> text whose importer section (from its line to the end) is
        /// <paramref name="section"/>; one without a section gets it at the end.</summary>
        internal static string WithImporterSection(string meta, string section)
        {
            var text = meta.Replace("\r\n", "\n");
            foreach (Match match in s_ImporterSection.Matches(text))
                if (!match.Value.StartsWith("labels:", StringComparison.Ordinal)) return text.Substring(0, match.Index) + section;
            if (text.Length > 0 && !text.EndsWith("\n", StringComparison.Ordinal)) text += "\n";
            return text + section;
        }

        /// <summary>A <c>.meta</c> text with these labels: after its GUID line, as written (the
        /// Editor writes them unquoted), in ordinal order, repeats kept; none: no labels block. The
        /// file is written again as the Editor writes it: LF endings, and a <c>.meta</c> without an
        /// importer section (a script's) gets <paramref name="missingSection"/>
        /// (tests/projects/projectwrites 09.labels, 06.crlf).</summary>
        internal static string WithLabels(string meta, string[] labels, string missingSection = null)
        {
            var text = s_LabelsBlock.Replace(meta.Replace("\r\n", "\n"), "", 1);
            if (text.Length > 0 && !text.EndsWith("\n", StringComparison.Ordinal)) text += "\n";
            if (!string.IsNullOrEmpty(missingSection) && !HasImporterSection(text)) text += missingSection;
            var sorted = (labels ?? Array.Empty<string>()).Where(l => !string.IsNullOrEmpty(l)).OrderBy(l => l, StringComparer.Ordinal).ToArray();
            if (sorted.Length == 0) return text;
            var block = new StringBuilder("labels:\n");
            foreach (var label in sorted) block.Append("- ").Append(label).Append('\n');
            var guid = s_GuidLine.Match(text);
            int at = guid.Success ? guid.Index + guid.Length + 1 : text.Length;
            return text.Insert(at, block.ToString());
        }

        static readonly Regex s_DocumentHeader = new Regex("^--- !u![0-9]+ &(-?[0-9]+)[^\\r\\n]*", RegexOptions.Multiline);
        static readonly Regex s_NameLine = new Regex("^  m_Name: [^\\r\\n]*", RegexOptions.Multiline);

        /// <summary>A native asset's text with its main object (file id
        /// <paramref name="mainFileId"/>) named <paramref name="name"/> (quoted as YAML needs);
        /// null when it has no such object or name.</summary>
        internal static string WithMainObjectName(string text, string mainFileId, string name)
        {
            var headers = s_DocumentHeader.Matches(text);
            for (int i = 0; i < headers.Count; i++)
            {
                if (headers[i].Groups[1].Value != mainFileId) continue;
                int start = headers[i].Index, end = i + 1 < headers.Count ? headers[i + 1].Index : text.Length;
                var document = text.Substring(start, end - start);
                var line = s_NameLine.Match(document);
                if (!line.Success) return null;
                return text.Substring(0, start) + document.Substring(0, line.Index) + "  m_Name: " + UnityYamlWriter.Scalar(name)
                    + document.Substring(line.Index + line.Length) + text.Substring(end);
            }
            return null;
        }
    }
}
