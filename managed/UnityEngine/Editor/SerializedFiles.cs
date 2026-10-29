using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unbound.Runtime;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEditor
{
    /// <summary>EditorJsonUtility: JsonUtility's format for any object, engine objects included,
    /// with references to assets by file id and GUID (they survive the session).</summary>
    public static partial class EditorJsonUtility
    {
        public static string ToJson(object obj) => ToJson(obj, false);

        public static string ToJson(object obj, bool prettyPrint) => JsonUtility.EditorToJson(obj, prettyPrint);

        public static void FromJsonOverwrite(string json, object objectToOverwrite) =>
            JsonUtility.EditorFromJsonOverwrite(json, objectToOverwrite);
    }
}

namespace UnityEditorInternal
{
    public partial class InternalEditorUtility
    {
        /// <summary>Unbound's Editor is interactive (it has the game window), not a batch-mode one.</summary>
        [Unbound.EditorFaithful("as the interactive Editor (tests/projects/editor lists it as an expected difference from the batch-mode reference)")]
        public static bool inBatchMode => false;

        [Unbound.EditorFaithful("as the interactive Editor (tests/projects/editor lists it as an expected difference from the batch-mode reference)")]
        public static bool isHumanControllingUs => true;

        public static bool CurrentThreadIsMainThread() => PlayerLoopDriver.IsMainThread;

        /// <summary>
        /// Reads the objects of a serialized file (Unity's text format), not as assets: editor
        /// singletons and settings load their state so. A file Unbound wrote earlier
        /// (<see cref="SaveToSerializedFileAndForget"/>) is read in place of the project's.
        /// </summary>
        public static Object[] LoadSerializedFileAndForget(string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            var file = SerializedFiles.ReadablePath(path);
            if (file == null) return Array.Empty<Object>();
            List<YDocument> documents;
            try
            {
                documents = Native.LoadDocuments(file);
            }
            catch (Exception e)
            {
                Debug.LogError($"Unbound: {path} cannot be read ({e.Message}); only Unity's text serialization is supported.");
                return Array.Empty<Object>();
            }
            var loaded = ObjectGraphBuilder.Build(documents, path, null, ObjectGraphBuilder.Kind.Asset, null);
            var objects = new List<Object>();
            foreach (var o in loaded.Objects.Values)
            {
                o.IsPersistentAsset = false;
                objects.Add(o);
            }
            return objects.ToArray();
        }

        /// <summary>
        /// Writes objects in Unity's text serialization. Authored project files are never written:
        /// a path inside the project goes to <c>Library/Unbound/EditorFiles/</c>, where
        /// <see cref="LoadSerializedFileAndForget"/> finds it again.
        /// </summary>
        public static void SaveToSerializedFileAndForget(Object[] obj, string path, bool allowTextSerialization)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            if (path == null) throw new ArgumentNullException(nameof(path));
            var target = SerializedFiles.WritablePath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.WriteAllText(target, UnityYamlWriter.Write(obj));
        }
    }
}

namespace Unbound.Runtime
{
    /// <summary>Where editor files live: project-relative paths are the project's; what Unbound
    /// writes in the Editor's stead goes under Library/Unbound/EditorFiles.</summary>
    internal static class SerializedFiles
    {
        static string Project => ProjectInfo.ProjectPath;

        static string Absolute(string path) =>
            Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(Project, path));

        static string Overlay(string absolute)
        {
            var project = Path.GetFullPath(Project).TrimEnd('/') + "/";
            if (absolute.StartsWith(project, StringComparison.Ordinal))
                return Path.Combine(project, "Library", "Unbound", "EditorFiles", absolute.Substring(project.Length));
            // The installed Editor's preferences are never written either.
            var preferences = Path.GetFullPath(UnityEditorInternal.InternalEditorUtility.unityPreferencesFolder).TrimEnd('/') + "/";
            if (absolute.StartsWith(preferences, StringComparison.Ordinal))
            {
                var cache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
                if (string.IsNullOrEmpty(cache)) cache = Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? "", ".cache");
                return Path.Combine(cache, "unbound", "EditorPreferences", absolute.Substring(preferences.Length));
            }
            return null;
        }

        internal static string WritablePath(string path)
        {
            var absolute = Absolute(path);
            return Overlay(absolute) ?? absolute;
        }

        /// <summary>The file to read for a path (Unbound's copy first); null when there is none.</summary>
        internal static string ReadablePath(string path)
        {
            var absolute = Absolute(path);
            var overlay = Overlay(absolute);
            if (overlay != null && File.Exists(overlay)) return overlay;
            return File.Exists(absolute) ? absolute : null;
        }
    }

    /// <summary>
    /// Unity's text serialization of script objects (MonoBehaviour, ScriptableObject): the
    /// document per object, the native header fields, then the serialized fields in Unity's YAML
    /// forms (flow maps for math structs and references, block maps and sequences otherwise).
    /// </summary>
    internal static class UnityYamlWriter
    {
        internal static string Write(Object[] objects)
        {
            var sb = new StringBuilder("%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n");
            var ids = new Dictionary<Object, long>();
            long next = 1;
            foreach (var o in objects)
                if (o != null && !ids.ContainsKey(o)) ids[o] = next++;
            foreach (var o in objects)
            {
                if (o == null) continue;
                if (!SerializedTree.IsScript(o))
                    throw new NotSupportedException($"Unbound writes script objects only; {o.GetType().Name} '{o.name}' cannot be saved.");
                var writer = new Writer(sb, ids);
                sb.Append("--- !u!114 &").Append(ids[o]).Append('\n');
                sb.Append("MonoBehaviour:\n");
                writer.Header(o);
                if (o is ISerializationCallbackReceiver receiver)
                {
                    try { receiver.OnBeforeSerialize(); }
                    catch (Exception e) { Debug.LogException(e); }
                }
                writer.Fields(o, o.GetType(), 1);
            }
            return sb.ToString();
        }

        sealed class Writer
        {
            readonly StringBuilder m_Out;
            readonly Dictionary<Object, long> m_Ids;

            internal Writer(StringBuilder output, Dictionary<Object, long> ids)
            {
                m_Out = output;
                m_Ids = ids;
            }

            void Indent(int level) => m_Out.Append(' ', level * 2);

            internal void Header(Object o)
            {
                Line(1, "m_ObjectHideFlags", ((uint)o.hideFlags).ToString(CultureInfo.InvariantCulture));
                Line(1, "m_CorrespondingSourceObject", "{fileID: 0}");
                Line(1, "m_PrefabInstance", "{fileID: 0}");
                Line(1, "m_PrefabAsset", "{fileID: 0}");
                Line(1, "m_GameObject", o is Component c ? Reference(c.gameObject) : "{fileID: 0}");
                Line(1, "m_Enabled", o is Behaviour b && !b.enabled ? "0" : "1");
                Line(1, "m_EditorHideFlags", "0");
                Line(1, "m_Script", Reference(MonoScript.FromType(o.GetType())));
                Line(1, "m_Name", Scalar(o.name));
                Line(1, "m_EditorClassIdentifier", "");
            }

            void Line(int level, string key, string value)
            {
                Indent(level);
                m_Out.Append(key).Append(':');
                if (value.Length > 0) m_Out.Append(' ').Append(value);
                else m_Out.Append(' ');
                m_Out.Append('\n');
            }

            internal void Fields(object obj, Type type, int level)
            {
                foreach (var member in SerializationRules.Members(type))
                {
                    if (member.Shape == Shape.ManagedReference)
                        throw new NotSupportedException($"Unbound does not write [SerializeReference] fields yet ({type.Name}.{member.Name}).");
                    Member(member.Name, member.Field.GetValue(obj), member.Field.FieldType, member.Shape, member.ElementType, member.ElementShape, level);
                }
            }

            void Member(string name, object value, Type type, Shape shape, Type elementType, Shape elementShape, int level)
            {
                if (shape == Shape.Array || shape == Shape.List)
                {
                    var list = value as IList;
                    if (elementShape == Shape.Byte && type == typeof(byte[]))
                    {
                        var hex = new StringBuilder();
                        if (list != null) foreach (byte b in list) hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                        Line(level, name, hex.ToString());
                        return;
                    }
                    if (list == null || list.Count == 0)
                    {
                        Line(level, name, "[]");
                        return;
                    }
                    Indent(level);
                    m_Out.Append(name).Append(":\n");
                    foreach (var item in list) Element(item, elementType, elementShape, level);
                    return;
                }
                if (shape == Shape.Composite)
                {
                    var composite = value ?? SerializationRules.Create(type);
                    if (SerializationRules.Members(composite.GetType()).Length == 0)
                    {
                        Line(level, name, "{}");
                        return;
                    }
                    Indent(level);
                    m_Out.Append(name).Append(":\n");
                    Fields(composite, composite.GetType(), level + 1);
                    return;
                }
                if (TryBlock(value, shape, out var block))
                {
                    Indent(level);
                    m_Out.Append(name).Append(":\n");
                    foreach (var (key, text) in block) Line(level + 1, key, text);
                    return;
                }
                Line(level, name, Flow(value, type, shape));
            }

            /// <summary>A sequence item, at the parent key's indentation ("- value").</summary>
            void Element(object item, Type type, Shape shape, int level)
            {
                if (shape == Shape.Composite)
                {
                    var composite = item ?? SerializationRules.Create(type);
                    var start = m_Out.Length;
                    Fields(composite, composite.GetType(), level + 1);
                    if (m_Out.Length == start)
                    {
                        Indent(level);
                        m_Out.Append("- {}\n");
                        return;
                    }
                    // The first field's indentation becomes the dash.
                    m_Out.Remove(start, level * 2 + 2);
                    m_Out.Insert(start, new string(' ', level * 2) + "- ");
                    return;
                }
                Indent(level);
                m_Out.Append("- ").Append(Flow(item, type, shape)).Append('\n');
            }

            static bool TryBlock(object value, Shape shape, out List<(string, string)> block)
            {
                block = null;
                switch (shape)
                {
                    case Shape.Rect:
                    {
                        var r = (Rect)value;
                        block = new List<(string, string)> { ("serializedVersion", "2"), ("x", F(r.x)), ("y", F(r.y)), ("width", F(r.width)), ("height", F(r.height)) };
                        return true;
                    }
                    case Shape.LayerMask:
                        block = new List<(string, string)> { ("serializedVersion", "2"), ("m_Bits", ((uint)((LayerMask)value).value).ToString(CultureInfo.InvariantCulture)) };
                        return true;
                }
                return false;
            }

            string Flow(object value, Type type, Shape shape)
            {
                switch (shape)
                {
                    case Shape.Bool: return (bool)value ? "1" : "0";
                    case Shape.Float: return F((float)value);
                    case Shape.Double: return D((double)value);
                    case Shape.String: return Scalar((string)value);
                    case Shape.Char: return ((int)(char)value).ToString(CultureInfo.InvariantCulture);
                    case Shape.Enum: return Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
                    case Shape.SByte: case Shape.Byte: case Shape.Short: case Shape.UShort:
                    case Shape.Int: case Shape.UInt: case Shape.Long: case Shape.ULong:
                        return Convert.ToString(value, CultureInfo.InvariantCulture);
                    case Shape.UnityObject: return Reference(value as Object);
                    case Shape.Vector2: { var v = (Vector2)value; return $"{{x: {F(v.x)}, y: {F(v.y)}}}"; }
                    case Shape.Vector3: { var v = (Vector3)value; return $"{{x: {F(v.x)}, y: {F(v.y)}, z: {F(v.z)}}}"; }
                    case Shape.Vector4: { var v = (Vector4)value; return $"{{x: {F(v.x)}, y: {F(v.y)}, z: {F(v.z)}, w: {F(v.w)}}}"; }
                    case Shape.Quaternion: { var v = (Quaternion)value; return $"{{x: {F(v.x)}, y: {F(v.y)}, z: {F(v.z)}, w: {F(v.w)}}}"; }
                    case Shape.Vector2Int: { var v = (Vector2Int)value; return $"{{x: {v.x}, y: {v.y}}}"; }
                    case Shape.Vector3Int: { var v = (Vector3Int)value; return $"{{x: {v.x}, y: {v.y}, z: {v.z}}}"; }
                    case Shape.Color: { var c = (Color)value; return $"{{r: {F(c.r)}, g: {F(c.g)}, b: {F(c.b)}, a: {F(c.a)}}}"; }
                    case Shape.Color32:
                    {
                        var c = (Color32)value;
                        uint rgba = (uint)(c.r | c.g << 8 | c.b << 16 | c.a << 24);
                        return $"{{serializedVersion: 2, rgba: {rgba}}}";
                    }
                    case Shape.RectInt: { var r = (RectInt)value; return $"{{x: {r.x}, y: {r.y}, width: {r.width}, height: {r.height}}}"; }
                    case Shape.Bounds:
                    {
                        var b = (Bounds)value;
                        return $"{{m_Center: {Flow(b.center, typeof(Vector3), Shape.Vector3)}, m_Extent: {Flow(b.extents, typeof(Vector3), Shape.Vector3)}}}";
                    }
                    case Shape.BoundsInt:
                    {
                        var b = (BoundsInt)value;
                        return $"{{m_Position: {Flow(b.position, typeof(Vector3Int), Shape.Vector3Int)}, m_Size: {Flow(b.size, typeof(Vector3Int), Shape.Vector3Int)}}}";
                    }
                    case Shape.Hash128: return $"{{serializedVersion: 2, Hash: {((Hash128)value).ToString()}}}";
                    case Shape.PropertyName: return Scalar(value?.ToString() ?? "");
                    default:
                        throw new NotSupportedException($"Unbound does not write {type.Name} values in Unity's text serialization yet.");
                }
            }

            string Reference(Object o)
            {
                if (o == null) return "{fileID: 0}";
                if (m_Ids.TryGetValue(o, out var local)) return $"{{fileID: {local}}}";
                if (o is MonoScript script && script.m_Guid is string scriptGuid && scriptGuid.Length > 0)
                    return $"{{fileID: 11500000, guid: {scriptGuid}, type: 3}}";
                if (AssetDatabaseRuntime.TryGetLocation(o, out var guid, out var fileId))
                {
                    int kind;
                    try { kind = AssetDatabaseRuntime.Info(guid)?.Kind is "yaml" or "prefab" ? 2 : 3; }
                    catch (Exception) { kind = 2; }
                    return $"{{fileID: {fileId}, guid: {guid}, type: {kind}}}";
                }
                return "{fileID: 0}";
            }

            static string F(float v) => Number(v.ToString("R", CultureInfo.InvariantCulture));

            static string D(double v) => Number(v.ToString("R", CultureInfo.InvariantCulture));

            static string Number(string text)
            {
                if (text == "∞" || text == "Infinity") return "Infinity";
                if (text == "-∞" || text == "-Infinity") return "-Infinity";
                if (text == "NaN") return "NaN";
                int e = text.IndexOf('E');
                if (e < 0) return text;
                // Unity writes the shortest digits without an exponent: 1E-07 -> 0.0000001.
                bool negative = text[0] == '-';
                var mantissa = text.Substring(negative ? 1 : 0, e - (negative ? 1 : 0));
                var exponent = int.Parse(text.Substring(e + 1), CultureInfo.InvariantCulture);
                int dot = mantissa.IndexOf('.');
                var digits = dot < 0 ? mantissa : mantissa.Remove(dot, 1);
                int point = (dot < 0 ? mantissa.Length : dot) + exponent;
                string plain;
                if (point <= 0) plain = "0." + new string('0', -point) + digits;
                else if (point >= digits.Length) plain = digits + new string('0', point - digits.Length);
                else plain = digits.Substring(0, point) + "." + digits.Substring(point);
                return (negative ? "-" : "") + plain;
            }
        }

        /// <summary>A string as Unity's YAML writes it: plain when it reads back as itself, else
        /// quoted.</summary>
        internal static string Scalar(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            bool control = false;
            foreach (var ch in s) if (ch < 0x20 || ch == 0x7f) { control = true; break; }
            if (control)
            {
                var sb = new StringBuilder("\"");
                foreach (var ch in s)
                {
                    switch (ch)
                    {
                        case '"': sb.Append("\\\""); break;
                        case '\\': sb.Append("\\\\"); break;
                        case '\n': sb.Append("\\n"); break;
                        case '\r': sb.Append("\\r"); break;
                        case '\t': sb.Append("\\t"); break;
                        default:
                            if (ch < 0x20) sb.Append("\\x").Append(((int)ch).ToString("X2", CultureInfo.InvariantCulture));
                            else sb.Append(ch);
                            break;
                    }
                }
                return sb.Append('"').ToString();
            }
            bool plain = !(s[0] == ' ' || s[^1] == ' ' || "-?:,[]{}#&*!|>'\"%@`".IndexOf(s[0]) >= 0
                           || s.Contains(": ") || s.Contains(" #") || s.EndsWith(":", StringComparison.Ordinal));
            return plain ? s : "'" + s.Replace("'", "''") + "'";
        }
    }
}
