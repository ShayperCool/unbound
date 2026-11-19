using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using UnityEditor;
using Object = UnityEngine.Object;

namespace Unbound.Runtime
{
    /// <summary>
    /// A texture importer's SerializedObject: the importer's native type tree (the Unity 6000.0.71f1
    /// Editor's, tests/projects/importerserialized <c>schema/</c> rows, as
    /// <c>Editor/Data/importer-types-6000.0.json</c>) over its .meta section. The section names
    /// each field without its <c>m_</c> and with a lower-case first letter (<c>m_SpriteSheet</c> is
    /// <c>spriteSheet</c>), keeps some of the tree's top-level fields in groups (<c>mipmaps</c>,
    /// <c>bumpmap</c>), and writes a pair whose first is a pair as a one-entry map
    /// (<c>internalIDToNameTable</c>'s <c>first: {213: 1001}</c>); the data the SerializedObject reads
    /// is the section under the tree's names, and an applied change goes back into the importer's
    /// settings under the section's (they reach the .meta as the importer's other settings do).
    /// Fields the section does not have (the import's output, the used file IDs) read their
    /// defaults.
    /// </summary>
    internal sealed class ImporterTypeTree : ITypeTreeTarget
    {
        readonly AssetImporter m_Importer;
        readonly TypeTreeNode[] m_Tree;
        YText m_Body;
        /// <summary>A top-level field's group in the section (null: the section itself).</summary>
        readonly Dictionary<string, string> m_Groups = new Dictionary<string, string>(StringComparer.Ordinal);

        ImporterTypeTree(AssetImporter importer, TypeTreeNode[] tree)
        {
            m_Importer = importer;
            m_Tree = tree;
        }

        /// <summary>The tree target of an importer whose class's tree the release has (Unity 6000.0's
        /// TextureImporter), else null (its SerializedObject reflects over the managed object).</summary>
        internal static ITypeTreeTarget Of(AssetImporter importer)
        {
            if (importer?.m_Settings == null || importer.m_Settings.Kind != YKind.Map || string.IsNullOrEmpty(importer.m_SettingsType)) return null;
            return Trees.TryGetValue(importer.m_SettingsType, out var tree) ? new ImporterTypeTree(importer, tree) : null;
        }

        static Dictionary<string, TypeTreeNode[]> s_Trees;

        static Dictionary<string, TypeTreeNode[]> Trees
        {
            get
            {
                if (s_Trees != null) return s_Trees;
                var trees = new Dictionary<string, TypeTreeNode[]>(StringComparer.Ordinal);
                if (Compat.Family == ReleaseFamily.Unity6000_0)
                {
                    using var stream = typeof(ImporterTypeTree).Assembly.GetManifestResourceStream("Unbound.importer-types-6000.0.json");
                    if (stream != null)
                    {
                        using var doc = JsonDocument.Parse(stream);
                        foreach (var p in doc.RootElement.EnumerateObject())
                            trees[p.Name] = p.Value.GetProperty("tree").EnumerateArray().Select(TypeTreeNode.Read).ToArray();
                    }
                }
                return s_Trees = trees;
            }
        }

        public TypeTreeNode[] Tree => m_Tree;

        public YText Body => m_Body ??= ToTree(m_Importer.m_Settings);

        public string RootTypeName => m_Importer.m_SettingsType;

        public Object Resolve(ObjectRef r)
        {
            if (r.IsNull || r.Guid == null) return null;
            try { return AssetDatabaseRuntime.Load(r.Guid, r.FileId, typeof(Object)); }
            catch (Exception) { return null; }
        }

        public ObjectRef Reference(Object o) =>
            AssetDatabaseRuntime.TryGetLocation(o, out var guid, out var fileId) ? new ObjectRef(fileId, guid, 2) : default;

        /// <summary>The changed data back into the importer's settings, under the section's names;
        /// the importer's settings changed (as a setter changes them).</summary>
        public void Applied()
        {
            var section = MetaSettings.Clone(m_Importer.m_Settings);
            var body = Body;
            foreach (var s in m_Tree)
            {
                var value = body.Get(s.Name);
                if (value == null || !m_FromSection.Contains(s.Name)) continue;
                var key = MetaKey(s.Name);
                var container = m_Groups.TryGetValue(s.Name, out var group) ? section.Get(group) : section;
                if (container == null || container.Kind != YKind.Map) continue;
                Set(container, key, FromTree(s, value, container.Get(key)));
            }
            m_Importer.m_Settings = section;
            m_Importer.Changed();
        }

        // ---------------------------------------------------------------- names

        /// <summary>A tree field's key in the section: without <c>m_</c>, its first letter lower-case.</summary>
        internal static string MetaKey(string name) =>
            name.Length > 2 && name.StartsWith("m_", StringComparison.Ordinal) ? char.ToLowerInvariant(name[2]) + name.Substring(3) : name;

        static bool IsPPtr(TypeTreeNode s) => s.TypeName.StartsWith("PPtr<", StringComparison.Ordinal);

        static bool IsArray(TypeTreeNode s) => s.Type != UnityEditor.SerializedPropertyType.String && s.Child("Array") != null;

        // ---------------------------------------------------------------- section -> tree

        YText ToTree(YNode section)
        {
            var body = YText.Map();
            var rootKeys = new HashSet<string>(m_Tree.Select(s => MetaKey(s.Name)), StringComparer.Ordinal);
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var s in m_Tree)
            {
                var key = MetaKey(s.Name);
                var value = section.Get(key);
                if (value != null) used.Add(key);
                else
                {
                    // A field kept in a group of the section (mipmaps.mipMapMode).
                    for (int i = 0; i < section.Keys.Length && value == null; i++)
                    {
                        var group = section.Values[i];
                        if (rootKeys.Contains(section.Keys[i]) || group.Kind != YKind.Map) continue;
                        value = group.Get(key);
                        if (value != null) m_Groups[s.Name] = section.Keys[i];
                    }
                }
                if (value != null)
                {
                    body.Add(s.Name, Node(s, value));
                    m_FromSection.Add(s.Name);
                }
            }
            // The import's output: what the source image is, the size it imported at and the
            // compressed format it chose (the uncompressed format and usage mode are not given:
            // they read their defaults).
            if (m_Importer is TextureImporter texture && m_Tree.Any(s => s.Name == "m_Output") && texture.SourceInfo() is { } source)
            {
                var output = YText.Map();
                var instructions = YText.Map();
                instructions.Add("width", Scalar(source.ImportedWidth.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                instructions.Add("height", Scalar(source.ImportedHeight.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                if (source.CompressedFormat is int format)
                {
                    var text = format.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    instructions.Add("compressedFormat", Scalar(text));
                    instructions.Add("desiredFormat", Scalar(text));
                }
                output.Add("textureImportInstructions", instructions);
                var information = YText.Map();
                information.Add("width", Scalar(source.Width.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                information.Add("height", Scalar(source.Height.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                information.Add("doesTextureContainAlpha", Scalar(source.Alpha ? "1" : "0"));
                output.Add("sourceTextureInformation", information);
                body.Add("m_Output", output);
            }
            return body;
        }

        /// <summary>The tree's fields the section has (only they are written back).</summary>
        readonly HashSet<string> m_FromSection = new HashSet<string>(StringComparer.Ordinal);

        static YText Node(TypeTreeNode s, YNode y)
        {
            if (y == null || y.IsNull) return null;
            if (IsPPtr(s) || s.Type == UnityEditor.SerializedPropertyType.String || s.Children.Length == 0) return Verbatim(y);
            if (IsArray(s))
            {
                var element = s.Child("Array")?.Child("data");
                if (y.Kind == YKind.Seq)
                {
                    var seq = YText.Seq(flow: y.Items.Length == 0);
                    foreach (var item in y.Items) seq.Items.Add(element == null ? Verbatim(item) : Node(element, item) ?? Verbatim(item));
                    return seq;
                }
                if (y.Kind == YKind.Map)
                {
                    // A map with plain keys: its entries' values under the pair's second.
                    var second = element?.Child("second");
                    var map = YText.Map(flow: y.Keys.Length == 0);
                    for (int i = 0; i < y.Keys.Length; i++) map.Add(y.Keys[i], second == null ? Verbatim(y.Values[i]) : Node(second, y.Values[i]) ?? Verbatim(y.Values[i]));
                    return map;
                }
                return Verbatim(y);
            }
            if (y.Kind != YKind.Map) return Verbatim(y);
            if (s.TypeName == "pair" && y.Get("first") == null && y.Keys.Length == 1)
            {
                // A pair written as its one entry ({213: 1001}).
                var pair = YText.Map();
                pair.Add("first", Scalar(y.Keys[0]));
                var second = s.Child("second");
                pair.Add("second", second == null ? Verbatim(y.Values[0]) : Node(second, y.Values[0]) ?? Verbatim(y.Values[0]));
                return pair;
            }
            var result = YText.Map(flow: false);
            for (int i = 0; i < y.Keys.Length; i++)
            {
                var child = s.Children.FirstOrDefault(c => MetaKey(c.Name) == y.Keys[i]);
                result.Add(child?.Name ?? y.Keys[i], child == null ? Verbatim(y.Values[i]) : Node(child, y.Values[i]) ?? Verbatim(y.Values[i]));
            }
            return result;
        }

        /// <summary>A node as it is (keys and scalars unchanged).</summary>
        static YText Verbatim(YNode y)
        {
            switch (y?.Kind)
            {
                case YKind.Scalar: return Scalar(y.Scalar);
                case YKind.Seq:
                {
                    var seq = YText.Seq(flow: y.Items.Length == 0);
                    foreach (var item in y.Items) seq.Items.Add(Verbatim(item));
                    return seq;
                }
                case YKind.Map:
                {
                    var map = YText.Map(flow: y.Keys.Length == 0);
                    for (int i = 0; i < y.Keys.Length; i++) map.Add(y.Keys[i], Verbatim(y.Values[i]));
                    return map;
                }
                default: return Scalar("");
            }
        }

        /// <summary>A scalar whose text reads back as <paramref name="text"/>.</summary>
        static YText Scalar(string text)
        {
            text ??= "";
            bool plain = text.Length == 0 || (text[0] != '\'' && text[0] != '"' && text.IndexOfAny(new[] { '\n', '\r', '\\' }) < 0);
            if (plain) return YText.Scalar(text);
            var sb = new StringBuilder("\"");
            foreach (var c in text)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    default: sb.Append(c); break;
                }
            }
            return YText.Scalar(sb.Append('"').ToString());
        }

        // ---------------------------------------------------------------- tree -> section

        static YNode FromTree(TypeTreeNode s, YText t, YNode old)
        {
            if (t == null) return old;
            if (IsPPtr(s) || s.Type == UnityEditor.SerializedPropertyType.String || s.Children.Length == 0) return t.ToNode();
            if (IsArray(s))
            {
                var element = s.Child("Array")?.Child("data");
                if (t.Kind == YKind.Seq)
                {
                    var items = new YNode[t.Items.Count];
                    for (int i = 0; i < items.Length; i++)
                    {
                        var before = old != null && old.Kind == YKind.Seq && i < old.Items.Length ? old.Items[i] : null;
                        items[i] = element == null ? t.Items[i].ToNode() : FromTree(element, t.Items[i], before);
                    }
                    return new YNode { Kind = YKind.Seq, Items = items };
                }
                if (t.Kind == YKind.Map)
                {
                    var second = element?.Child("second");
                    var values = new YNode[t.Values.Count];
                    for (int i = 0; i < values.Length; i++)
                        values[i] = second == null ? t.Values[i].ToNode() : FromTree(second, t.Values[i], old?.Get(t.Keys[i]));
                    return new YNode { Kind = YKind.Map, Keys = t.Keys.ToArray(), Values = values };
                }
                return t.ToNode();
            }
            if (t.Kind != YKind.Map) return t.ToNode();
            if (s.TypeName == "pair" && WrittenAsEntry(s, old))
            {
                var first = t.Get("first")?.Text ?? "";
                var second = s.Child("second");
                var value = t.Get("second");
                return new YNode
                {
                    Kind = YKind.Map, Keys = new[] { first },
                    Values = new[] { second == null || value == null ? value?.ToNode() ?? YNode.MakeScalar("") : FromTree(second, value, old?.Kind == YKind.Map && old.Values.Length == 1 ? old.Values[0] : null) },
                };
            }
            var keys = new string[t.Keys.Count];
            var nodes = new YNode[t.Keys.Count];
            for (int i = 0; i < keys.Length; i++)
            {
                var child = s.Child(t.Keys[i]);
                keys[i] = child == null ? t.Keys[i] : MetaKey(child.Name);
                nodes[i] = child == null ? t.Values[i].ToNode() : FromTree(child, t.Values[i], old?.Get(keys[i]));
            }
            return new YNode { Kind = YKind.Map, Keys = keys, Values = nodes };
        }

        /// <summary>Whether a pair is written as its one entry: as it was, else when it is the first
        /// of a pair (internalIDToNameTable's <c>first: {213: 1001}</c>).</summary>
        static bool WrittenAsEntry(TypeTreeNode s, YNode old) =>
            old != null && old.Kind == YKind.Map ? old.Get("first") == null && old.Keys.Length == 1 : s.Name == "first";

        static void Set(YNode map, string key, YNode value)
        {
            for (int i = 0; i < map.Keys.Length; i++)
                if (map.Keys[i] == key) { map.Values[i] = value; return; }
            map.Keys = map.Keys.Append(key).ToArray();
            map.Values = map.Values.Append(value).ToArray();
        }
    }
}
