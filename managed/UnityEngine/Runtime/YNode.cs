using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Unbound.Runtime
{
    internal enum YKind : byte
    {
        Null = 0,
        Scalar = 1,
        Seq = 2,
        Map = 3,
    }

    /// <summary>
    /// A node of Unity's YAML serialization, as the native side parsed it (and resolved prefabs in).
    /// Scalars are text; readers convert them by the type of the field they fill.
    /// </summary>
    internal sealed class YNode
    {
        internal static readonly YNode Null = new YNode { Kind = YKind.Null };

        internal YKind Kind;
        internal string Scalar;
        internal YNode[] Items;
        internal string[] Keys;
        internal YNode[] Values;

        internal bool IsNull => Kind == YKind.Null;

        internal YNode this[string key] => Get(key);

        internal YNode Get(string key)
        {
            if (Kind != YKind.Map) return null;
            for (int i = 0; i < Keys.Length; i++)
                if (Keys[i] == key) return Values[i];
            return null;
        }

        internal bool TryGet(string key, out YNode value)
        {
            value = Get(key);
            return value != null;
        }

        internal int Count => Kind == YKind.Seq ? Items.Length : Kind == YKind.Map ? Keys.Length : 0;

        internal string AsString() => Kind == YKind.Scalar ? Scalar : "";

        internal long AsLong(long fallback = 0)
        {
            if (Kind != YKind.Scalar) return fallback;
            return long.TryParse(Scalar, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v :
                ulong.TryParse(Scalar, NumberStyles.Integer, CultureInfo.InvariantCulture, out var u) ? unchecked((long)u) :
                double.TryParse(Scalar, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? (long)d : fallback;
        }

        internal int AsInt(int fallback = 0) => unchecked((int)AsLong(fallback));

        internal ulong AsULong() =>
            Kind == YKind.Scalar && ulong.TryParse(Scalar, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : unchecked((ulong)AsLong());

        internal double AsDouble(double fallback = 0)
        {
            if (Kind != YKind.Scalar) return fallback;
            switch (Scalar)
            {
                case "Infinity": case "inf": return double.PositiveInfinity;
                case "-Infinity": case "-inf": return double.NegativeInfinity;
                case "NaN": case "nan": return double.NaN;
            }
            return double.TryParse(Scalar, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
        }

        internal float AsFloat(float fallback = 0f) => (float)AsDouble(fallback);

        internal bool AsBool(bool fallback = false)
        {
            if (Kind != YKind.Scalar) return fallback;
            switch (Scalar)
            {
                case "0": case "false": case "False": return false;
                case "1": case "true": case "True": return true;
            }
            return AsLong(fallback ? 1 : 0) != 0;
        }

        /// <summary>A child scalar's value, or a default when missing.</summary>
        internal float Float(string key, float fallback = 0f) => Get(key)?.AsFloat(fallback) ?? fallback;
        internal int Int(string key, int fallback = 0) => Get(key)?.AsInt(fallback) ?? fallback;
        internal long Long(string key, long fallback = 0) => Get(key)?.AsLong(fallback) ?? fallback;
        internal bool Bool(string key, bool fallback = false) => Get(key)?.AsBool(fallback) ?? fallback;
        internal string Str(string key, string fallback = "") => Get(key) is YNode n && n.Kind == YKind.Scalar ? n.Scalar : fallback;

        internal UnityEngine.Vector2 Vector2(string key) =>
            Get(key) is YNode n ? new UnityEngine.Vector2(n.Float("x"), n.Float("y")) : default;

        internal UnityEngine.Vector3 Vector3(string key, UnityEngine.Vector3 fallback = default) =>
            Get(key) is YNode n && n.Kind == YKind.Map ? new UnityEngine.Vector3(n.Float("x"), n.Float("y"), n.Float("z")) : fallback;

        internal UnityEngine.Vector4 Vector4(string key) =>
            Get(key) is YNode n ? new UnityEngine.Vector4(n.Float("x"), n.Float("y"), n.Float("z"), n.Float("w")) : default;

        internal UnityEngine.Quaternion Quaternion(string key) =>
            Get(key) is YNode n && n.Kind == YKind.Map ? new UnityEngine.Quaternion(n.Float("x"), n.Float("y"), n.Float("z"), n.Float("w", 1f)) : UnityEngine.Quaternion.identity;

        internal UnityEngine.Color Color(string key, UnityEngine.Color fallback) =>
            Get(key) is YNode n && n.Kind == YKind.Map ? new UnityEngine.Color(n.Float("r"), n.Float("g"), n.Float("b"), n.Float("a", 1f)) : fallback;

        internal UnityEngine.Rect Rect(string key) =>
            Get(key) is YNode n ? new UnityEngine.Rect(n.Float("x"), n.Float("y"), n.Float("width"), n.Float("height")) : default;

        /// <summary>`{fileID, guid, type}`.</summary>
        internal ObjectRef Ref(string key) => Get(key) is YNode n ? n.AsRef() : default;

        internal ObjectRef AsRef()
        {
            if (Kind != YKind.Map) return default;
            return new ObjectRef(Long("fileID"), Str("guid"), Int("type"));
        }

        internal IEnumerable<YNode> Seq() => Kind == YKind.Seq ? Items : Array.Empty<YNode>();

        public override string ToString()
        {
            var sb = new StringBuilder();
            Write(sb, 0);
            return sb.ToString();
        }

        void Write(StringBuilder sb, int depth)
        {
            switch (Kind)
            {
                case YKind.Null: sb.Append("~"); break;
                case YKind.Scalar: sb.Append(Scalar); break;
                case YKind.Seq:
                    sb.Append('[');
                    for (int i = 0; i < Items.Length; i++) { if (i > 0) sb.Append(", "); Items[i].Write(sb, depth + 1); }
                    sb.Append(']');
                    break;
                case YKind.Map:
                    sb.Append('{');
                    for (int i = 0; i < Keys.Length; i++) { if (i > 0) sb.Append(", "); sb.Append(Keys[i]).Append(": "); Values[i].Write(sb, depth + 1); }
                    sb.Append('}');
                    break;
            }
        }

        // ------------------------------------------------------------------ construction

        internal static YNode MakeScalar(string s) => new YNode { Kind = YKind.Scalar, Scalar = s };

        internal static YNode MakeMap(params (string, YNode)[] entries)
        {
            var node = new YNode { Kind = YKind.Map, Keys = new string[entries.Length], Values = new YNode[entries.Length] };
            for (int i = 0; i < entries.Length; i++)
            {
                node.Keys[i] = entries[i].Item1;
                node.Values[i] = entries[i].Item2;
            }
            return node;
        }
    }

    /// <summary>A reference as the serialized data writes it.</summary>
    internal readonly struct ObjectRef
    {
        internal readonly long FileId;
        internal readonly string Guid;
        internal readonly int Type;

        internal ObjectRef(long fileId, string guid, int type)
        {
            FileId = fileId;
            Guid = string.IsNullOrEmpty(guid) || guid == "00000000000000000000000000000000" ? null : guid;
            Type = type;
        }

        internal bool IsNull => FileId == 0;
        internal bool IsLocal => Guid == null;

        public override string ToString() => $"{{fileID: {FileId}, guid: {Guid}, type: {Type}}}";
    }

    /// <summary>One object document of a resolved file (prefab instances already expanded).</summary>
    internal sealed class YDocument
    {
        internal int ClassId;
        internal long FileId;
        internal bool Stripped;
        internal string TypeName;
        internal YNode Body;
    }

    /// <summary>Decodes the native side's binary encoding of parsed Unity YAML files.</summary>
    internal static unsafe class YBinary
    {
        internal static List<YDocument> DecodeDocuments(byte* data, long length)
        {
            var reader = new Reader(data, length);
            if (reader.ReadU32() != 0x31425955) // "UYB1"
                throw new FormatException("not a Unbound YAML buffer");
            uint count = reader.ReadU32();
            var docs = new List<YDocument>((int)count);
            for (uint i = 0; i < count; i++)
            {
                var doc = new YDocument
                {
                    ClassId = reader.ReadI32(),
                    FileId = reader.ReadI64(),
                    Stripped = reader.ReadU8() != 0,
                    TypeName = reader.ReadString(),
                };
                doc.Body = reader.ReadNode();
                docs.Add(doc);
            }
            return docs;
        }

        internal static YNode DecodeValue(byte* data, long length)
        {
            var reader = new Reader(data, length);
            return reader.ReadNode();
        }

        ref struct Reader
        {
            readonly byte* m_Data;
            readonly long m_Length;
            long m_Position;

            internal Reader(byte* data, long length)
            {
                m_Data = data;
                m_Length = length;
                m_Position = 0;
            }

            void Need(long n)
            {
                if (m_Position + n > m_Length) throw new FormatException("truncated YAML buffer");
            }

            internal byte ReadU8()
            {
                Need(1);
                return m_Data[m_Position++];
            }

            internal uint ReadU32()
            {
                Need(4);
                uint v = *(uint*)(m_Data + m_Position);
                m_Position += 4;
                return v;
            }

            internal int ReadI32() => unchecked((int)ReadU32());

            internal long ReadI64()
            {
                Need(8);
                long v = *(long*)(m_Data + m_Position);
                m_Position += 8;
                return v;
            }

            internal string ReadString()
            {
                uint length = ReadU32();
                Need(length);
                var s = length == 0 ? "" : Encoding.UTF8.GetString(m_Data + m_Position, (int)length);
                m_Position += length;
                return s;
            }

            internal YNode ReadNode()
            {
                var kind = (YKind)ReadU8();
                switch (kind)
                {
                    case YKind.Null:
                        return YNode.Null;
                    case YKind.Scalar:
                        return new YNode { Kind = YKind.Scalar, Scalar = ReadString() };
                    case YKind.Seq:
                    {
                        uint n = ReadU32();
                        var items = new YNode[n];
                        for (uint i = 0; i < n; i++) items[i] = ReadNode();
                        return new YNode { Kind = YKind.Seq, Items = items };
                    }
                    case YKind.Map:
                    {
                        uint n = ReadU32();
                        var keys = new string[n];
                        var values = new YNode[n];
                        for (uint i = 0; i < n; i++)
                        {
                            keys[i] = ReadString();
                            values[i] = ReadNode();
                        }
                        return new YNode { Kind = YKind.Map, Keys = keys, Values = values };
                    }
                    default:
                        throw new FormatException($"bad node tag {(byte)kind}");
                }
            }
        }
    }
}
