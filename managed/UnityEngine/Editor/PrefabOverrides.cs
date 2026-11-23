using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unbound.Runtime
{
    /// <summary>The objects a body written for a comparison or copy of values names: each one by a
    /// token file id, so values of different objects compare by the objects they reference, and a
    /// copy reads them back as the same objects.</summary>
    internal sealed class ValueReferences : IReferenceResolver
    {
        readonly Dictionary<Object, long> m_Ids = new Dictionary<Object, long>(ReferenceEqualityComparer.Instance);
        readonly List<Object> m_Objects = new List<Object>();

        /// <summary>The reference text of an object (null for a script: written as its file has it).</summary>
        internal string Text(Object o)
        {
            if (o is MonoScript) return null;
            if (o is null || o == null) return "{fileID: 0}";
            if (!m_Ids.TryGetValue(o, out var id))
            {
                m_Objects.Add(o);
                m_Ids[o] = id = m_Objects.Count;
            }
            return "{fileID: " + id.ToString(CultureInfo.InvariantCulture) + "}";
        }

        /// <summary>The object a written reference names (null: none).</summary>
        internal Object ObjectOf(YText reference)
        {
            if (reference == null || reference.Kind != YKind.Map || !reference.Flow) return null;
            long id = SceneFiles.ParseFileId(reference);
            if (id == 0) return null;
            var guid = reference.Get("guid")?.Text;
            return Resolve(new ObjectRef(id, guid, 0), typeof(Object));
        }

        public Object Resolve(ObjectRef reference, Type expected)
        {
            if (reference.IsNull) return null;
            if (!reference.IsLocal) return AssetDatabaseRuntime.Load(reference.Guid, reference.FileId, expected);
            long index = reference.FileId - 1;
            return index >= 0 && index < m_Objects.Count ? m_Objects[(int)index] : null;
        }
    }

    /// <summary>
    /// The serialized values of a GameObject or component as property paths
    /// (<c>m_LocalPosition.x</c>, <c>list.Array.size</c>, <c>list.Array.data[0]</c>), as Unbound
    /// writes them to a file: compared between a prefab instance's object and its source, and set
    /// on an object by writing its body, changing the paths and reading it back as a file load
    /// reads it (prefab overrides applied, reverted and merged).
    /// </summary>
    internal static class PrefabValues
    {
        /// <summary>Fields no modification holds: the hierarchy and the document's links.</summary>
        static readonly HashSet<string> s_Structural = new HashSet<string>(StringComparer.Ordinal)
        {
            "m_ObjectHideFlags", "m_CorrespondingSourceObject", "m_PrefabInstance", "m_PrefabAsset", "m_GameObject",
            "m_Component", "m_Children", "m_Father", "serializedVersion", "m_Script", "m_EditorHideFlags", "m_EditorClassIdentifier",
        };

        /// <summary>The fields Unbound writes of an object (a GameObject's own, a script's fields, an
        /// engine component's writer or its enabled state), references named by
        /// <paramref name="refs"/> after <paramref name="remap"/>.</summary>
        internal static YText Fields(Object o, Func<Object, Object> remap, ValueReferences refs)
        {
            string Name(Object x) => x is MonoScript ? null : refs.Text(remap == null ? x : remap(x));
            return Fields(o, Name);
        }

        /// <summary>The fields Unbound writes of an object, references written by
        /// <paramref name="references"/> (null: as a file of its own names them).</summary>
        internal static YText Fields(Object o, Func<Object, string> references, Dictionary<Object, long> ids = null,
            AssetDatabaseRuntime.Locations locations = null)
        {
            ids ??= new Dictionary<Object, long>(ReferenceEqualityComparer.Instance);
            switch (o)
            {
                case GameObject go:
                    return SceneFiles.Body(w => SceneFiles.WriteGameObject(w, go, Array.Empty<long>()), ids, locations, references);
                case MonoBehaviour mb:
                    return SceneFiles.Body(w => { w.Header(mb); w.Fields(mb, mb.GetType(), 1); }, ids, locations, references);
                case Component c:
                    return SceneFiles.Body(w =>
                    {
                        // A class's writer writes its enabled state itself.
                        if (SceneFiles.Writable(c.GetType())) c.WriteSerialized(w);
                        else if (SceneFiles.Enabled(c) is bool e) w.Bool("m_Enabled", e);
                    }, ids, locations, references);
            }
            return YText.Map();
        }

        /// <summary>The values of a body by property path; references stay whole maps.</summary>
        internal static List<(string Path, YText Value)> Flatten(YText body)
        {
            var list = new List<(string, YText)>();
            void Walk(string prefix, YText v)
            {
                switch (v.Kind)
                {
                    case YKind.Map when v.Flow && v.Get("fileID") != null:
                        list.Add((prefix, v));
                        return;
                    case YKind.Map:
                        for (int i = 0; i < v.Keys.Count; i++)
                        {
                            if (prefix.Length == 0 && s_Structural.Contains(v.Keys[i])) continue;
                            Walk(prefix.Length == 0 ? v.Keys[i] : prefix + "." + v.Keys[i], v.Values[i]);
                        }
                        return;
                    case YKind.Seq:
                        list.Add((prefix + ".Array.size", YText.Scalar(v.Items.Count.ToString(CultureInfo.InvariantCulture))));
                        for (int i = 0; i < v.Items.Count; i++) Walk(prefix + ".Array.data[" + i.ToString(CultureInfo.InvariantCulture) + "]", v.Items[i]);
                        return;
                    default:
                        list.Add((prefix, v));
                        return;
                }
            }
            Walk("", body);
            return list;
        }

        internal static Dictionary<string, YText> ByPath(List<(string Path, YText Value)> values)
        {
            var map = new Dictionary<string, YText>(values.Count, StringComparer.Ordinal);
            foreach (var (path, value) in values) map[path] = value;
            return map;
        }

        internal static bool Same(YText a, YText b) =>
            a.Kind == b.Kind && (a.Kind == YKind.Scalar ? a.Raw == b.Raw : UnityYamlText.FlowText(a) == UnityYamlText.FlowText(b));

        /// <summary>Whether a path names the property or a part of it.</summary>
        internal static bool Within(string path, string property) =>
            path.Length == property.Length ? path == property
            : path.Length > property.Length && path.StartsWith(property, StringComparison.Ordinal) && path[property.Length] == '.';

        /// <summary>Sets values on the object at their paths (references in <paramref name="refs"/>'
        /// names); a path the object does not have is ignored.</summary>
        internal static void Write(Object target, IEnumerable<(string Path, YText Value)> values, ValueReferences refs)
        {
            using var quiet = PrefabRecordTiming.QuietScope.Begin();
            PrefabRecords.Drop(target);
            var body = Fields(target, null, refs);
            foreach (var (path, value) in values) SetPath(body, path, value.Clone());
            Read(target, body, refs);
        }

        static void SetPath(YText body, string path, YText value)
        {
            var parts = path.Split('.');
            var node = body;
            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                bool last = i == parts.Length - 1;
                if (part == "Array" && node.Kind == YKind.Seq && i + 1 < parts.Length)
                {
                    var element = parts[i + 1];
                    if (element == "size")
                    {
                        if (int.TryParse(value.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size) && size >= 0) Resize(node, size);
                        return;
                    }
                    if (!element.StartsWith("data[", StringComparison.Ordinal) || !element.EndsWith("]", StringComparison.Ordinal)
                        || !int.TryParse(element.AsSpan(5, element.Length - 6), NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) || index < 0)
                        return;
                    if (index >= node.Items.Count) Resize(node, index + 1);
                    if (i + 1 == parts.Length - 1)
                    {
                        node.Items[index] = value;
                        return;
                    }
                    if (node.Items[index].Kind != YKind.Map) node.Items[index] = YText.Map();
                    node = node.Items[index];
                    i++;
                    continue;
                }
                if (node.Kind != YKind.Map) return;
                if (last)
                {
                    node.Set(part, value);
                    return;
                }
                var child = node.Get(part);
                if (child == null) return;
                node = child;
            }
        }

        static void Resize(YText seq, int size)
        {
            if (seq.Items.Count > size) seq.Items.RemoveRange(size, seq.Items.Count - size);
            while (seq.Items.Count < size)
                seq.Items.Add(seq.Items.Count > 0 ? seq.Items[^1].Clone() : YText.Scalar("0"));
            seq.Flow = seq.Items.Count == 0;
        }

        /// <summary>Reads a body into the object as a file load reads its document; an enabled state
        /// or an active state that changes goes through the object's own setters (its callbacks).</summary>
        static void Read(Object target, YText body, ValueReferences refs)
        {
            var node = body.ToNode();
            switch (target)
            {
                case GameObject go:
                    go.m_Name = node.Str("m_Name");
                    go.m_Layer = node.Int("m_Layer");
                    go.m_Tag = node.Str("m_TagString", "Untagged");
                    go.m_NavMeshLayer = (uint)node.Long("m_NavMeshLayer");
                    go.m_StaticEditorFlags = (uint)node.Long("m_StaticEditorFlags");
                    bool active = node.Bool("m_IsActive", true);
                    if (go.m_IsActive != active) go.SetActive(active);
                    return;
                case Component c:
                {
                    bool? before = SceneFiles.Enabled(c);
                    if (c is MonoBehaviour || SceneFiles.Writable(c.GetType())) ObjectGraphBuilder.ReadComponent(c, node, refs);
                    bool? read = c is MonoBehaviour || SceneFiles.Writable(c.GetType()) ? SceneFiles.Enabled(c) : node.Get("m_Enabled") is YNode e ? e.Scalar != "0" : before;
                    if (before is bool was && read is bool now && was != now)
                    {
                        SetEnabled(c, was, field: true);
                        SetEnabled(c, now, field: false);
                    }
                    if (c is Transform t) t.MarkChanged();
                    return;
                }
            }
        }

        static void SetEnabled(Component c, bool value, bool field)
        {
            switch (c)
            {
                case Behaviour b:
                    if (field) b.m_Enabled = value;
                    else b.enabled = value;
                    break;
                case Renderer r:
                    if (field) r.m_Enabled = value;
                    else r.enabled = value;
                    break;
                case Collider col:
                    if (field) col.m_Enabled = value;
                    else col.enabled = value;
                    break;
            }
        }
    }
}
