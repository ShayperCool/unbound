using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>
    /// Unity's JSON serialization: the fields the serializer writes (see SerializationRules), in
    /// Unity's format: floats written through double precision ("0.10000000149011612", "1.0"),
    /// object references as {"instanceID":N}, built-in structs by their serialized field names.
    /// </summary>
    public static partial class JsonUtility
    {
        public static string ToJson(object obj) => ToJson(obj, false);

        public static string ToJson(object obj, bool prettyPrint)
        {
            if (obj == null) return "";
            var type = obj.GetType();
            if (obj is Object unityObject && !(obj is MonoBehaviour) && !(obj is ScriptableObject))
            {
                // Engine objects write their native fields in Unity; Unbound keeps none of those.
                return "{}";
            }
            if (!IsObjectType(type)) return "{}";
            var writer = new JsonWriter(prettyPrint);
            writer.WriteObject(obj, type, 0);
            return writer.ToString();
        }

        public static T FromJson<T>(string json) => (T)FromJson(json, typeof(T));

        public static object FromJson(string json, Type type)
        {
            if (string.IsNullOrEmpty(json)) return null;
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (type.IsAbstract || type.IsSubclassOf(typeof(Object)))
                throw new ArgumentException($"Cannot deserialize JSON to new instances of type '{type.Name}.'");
            var root = JsonParser.Parse(json);
            if (!IsObjectType(type) || root.Kind != JsonKind.Object)
                throw new ArgumentException("JSON must represent an object type.");
            var obj = SerializationRules.Create(type);
            var reader = new JsonReader();
            obj = reader.ReadFields(obj, type, root, 0);
            reader.Complete();
            return obj;
        }

        public static void FromJsonOverwrite(string json, object objectToOverwrite)
        {
            if (string.IsNullOrEmpty(json)) return;
            if (objectToOverwrite == null) throw new ArgumentNullException(nameof(objectToOverwrite));
            var type = objectToOverwrite.GetType();
            if (objectToOverwrite is Object && !(objectToOverwrite is MonoBehaviour) && !(objectToOverwrite is ScriptableObject))
                throw new ArgumentException("Engine types cannot be overwritten from JSON.");
            var root = JsonParser.Parse(json);
            if (root.Kind != JsonKind.Object) throw new ArgumentException("JSON must represent an object type.");
            var reader = new JsonReader();
            reader.ReadFields(objectToOverwrite, type, root, 0);
            reader.Complete();
        }

        static bool IsObjectType(Type type) =>
            !type.IsPrimitive && type != typeof(string) && !type.IsEnum && !type.IsArray
            && !(type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>));

        // ------------------------------------------------------------------ writing

        sealed class JsonWriter
        {
            readonly StringBuilder m_Out = new StringBuilder();
            readonly bool m_Pretty;
            int m_Indent;

            internal JsonWriter(bool pretty) { m_Pretty = pretty; }

            public override string ToString() => m_Out.ToString();

            void NewLine()
            {
                if (!m_Pretty) return;
                m_Out.Append('\n');
                m_Out.Append(' ', m_Indent * 4);
            }

            void Key(string name, ref bool first)
            {
                if (!first) m_Out.Append(',');
                first = false;
                NewLine();
                WriteString(name);
                m_Out.Append(m_Pretty ? ": " : ":");
            }

            internal void WriteObject(object obj, Type type, int depth)
            {
                if (obj is ISerializationCallbackReceiver receiver)
                {
                    try { receiver.OnBeforeSerialize(); }
                    catch (Exception e) { Debug.LogException(e); }
                }
                m_Out.Append('{');
                m_Indent++;
                bool first = true;
                foreach (var member in SerializationRules.Members(type))
                {
                    if (member.Shape == Shape.ManagedReference) continue;
                    Key(member.Name, ref first);
                    var value = obj == null ? null : member.Field.GetValue(obj);
                    if (member.Shape == Shape.Array || member.Shape == Shape.List)
                        WriteList(value as IList, member.ElementType, member.ElementShape, depth + 1);
                    else
                        WriteValue(value, member.Field.FieldType, member.Shape, depth + 1);
                }
                m_Indent--;
                if (!first) NewLine();
                m_Out.Append('}');
            }

            void WriteList(IList list, Type elementType, Shape shape, int depth)
            {
                m_Out.Append('[');
                m_Indent++;
                bool first = true;
                if (list != null)
                {
                    foreach (var item in list)
                    {
                        if (!first) m_Out.Append(',');
                        first = false;
                        NewLine();
                        WriteValue(item, elementType, shape, depth);
                    }
                }
                m_Indent--;
                if (!first) NewLine();
                m_Out.Append(']');
            }

            void WriteValue(object value, Type type, Shape shape, int depth)
            {
                switch (shape)
                {
                    case Shape.Bool: m_Out.Append((bool)value ? "true" : "false"); break;
                    case Shape.SByte: m_Out.Append(((sbyte)value).ToString(CultureInfo.InvariantCulture)); break;
                    case Shape.Byte: m_Out.Append(((byte)value).ToString(CultureInfo.InvariantCulture)); break;
                    case Shape.Short: m_Out.Append(((short)value).ToString(CultureInfo.InvariantCulture)); break;
                    case Shape.UShort: m_Out.Append(((ushort)value).ToString(CultureInfo.InvariantCulture)); break;
                    case Shape.Int: m_Out.Append(((int)value).ToString(CultureInfo.InvariantCulture)); break;
                    case Shape.UInt: m_Out.Append(((uint)value).ToString(CultureInfo.InvariantCulture)); break;
                    case Shape.Long: m_Out.Append(((long)value).ToString(CultureInfo.InvariantCulture)); break;
                    case Shape.ULong: m_Out.Append(((ulong)value).ToString(CultureInfo.InvariantCulture)); break;
                    case Shape.Char: m_Out.Append(((int)(char)value).ToString(CultureInfo.InvariantCulture)); break;
                    case Shape.Float: WriteNumber((float)value); break;
                    case Shape.Double: WriteNumber((double)value); break;
                    case Shape.String: WriteString((string)value ?? ""); break;
                    case Shape.Enum: m_Out.Append(Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)); break;
                    case Shape.UnityObject:
                        m_Out.Append(m_Pretty ? "{\n" : "{");
                        if (m_Pretty) m_Out.Append(' ', (m_Indent + 1) * 4);
                        m_Out.Append(m_Pretty ? "\"instanceID\": " : "\"instanceID\":");
                        m_Out.Append((value as Object) is Object o && o != null ? o.GetInstanceID() : 0);
                        if (m_Pretty) { m_Out.Append('\n'); m_Out.Append(' ', m_Indent * 4); }
                        m_Out.Append('}');
                        break;
                    case Shape.Vector2: { var v = (Vector2)value; Fields(("x", v.x), ("y", v.y)); break; }
                    case Shape.Vector3: { var v = (Vector3)value; Fields(("x", v.x), ("y", v.y), ("z", v.z)); break; }
                    case Shape.Vector4: { var v = (Vector4)value; Fields(("x", v.x), ("y", v.y), ("z", v.z), ("w", v.w)); break; }
                    case Shape.Quaternion: { var v = (Quaternion)value; Fields(("x", v.x), ("y", v.y), ("z", v.z), ("w", v.w)); break; }
                    case Shape.Color: { var v = (Color)value; Fields(("r", v.r), ("g", v.g), ("b", v.b), ("a", v.a)); break; }
                    case Shape.Color32: { var v = (Color32)value; Fields(("r", v.r), ("g", v.g), ("b", v.b), ("a", v.a)); break; }
                    case Shape.Vector2Int: { var v = (Vector2Int)value; Fields(("x", v.x), ("y", v.y)); break; }
                    case Shape.Vector3Int: { var v = (Vector3Int)value; Fields(("x", v.x), ("y", v.y), ("z", v.z)); break; }
                    case Shape.Rect: { var v = (Rect)value; Fields(("serializedVersion", "2"), ("x", v.x), ("y", v.y), ("width", v.width), ("height", v.height)); break; }
                    case Shape.RectInt: { var v = (RectInt)value; Fields(("x", v.x), ("y", v.y), ("width", v.width), ("height", v.height)); break; }
                    case Shape.LayerMask: { var v = (LayerMask)value; Fields(("serializedVersion", "2"), ("m_Bits", (uint)v.value)); break; }
                    case Shape.RectOffset: { var v = (RectOffset)value ?? new RectOffset(); Fields(("m_Left", v.left), ("m_Right", v.right), ("m_Top", v.top), ("m_Bottom", v.bottom)); break; }
                    case Shape.Bounds:
                    {
                        var v = (Bounds)value;
                        Fields(("m_Center", v.center), ("m_Extent", v.extents));
                        break;
                    }
                    case Shape.BoundsInt:
                    {
                        var v = (BoundsInt)value;
                        Fields(("m_Position", v.position), ("m_Size", v.size));
                        break;
                    }
                    case Shape.Matrix4x4:
                    {
                        var m = (Matrix4x4)value;
                        var fields = new (string, object)[16];
                        for (int r = 0; r < 4; r++)
                            for (int c = 0; c < 4; c++)
                                fields[r * 4 + c] = ($"e{r}{c}", m[r, c]);
                        Fields(fields);
                        break;
                    }
                    case Shape.Hash128: { var v = (Hash128)value; Fields(("serializedVersion", "2"), ("Hash", v.ToString())); break; }
                    case Shape.Composite:
                        if (depth > 10) { m_Out.Append("{}"); break; }
                        WriteObject(value ?? SerializationRules.Create(type), value?.GetType() ?? type, depth);
                        break;
                    default:
                        // AnimationCurve, Gradient: not written by Unbound yet.
                        m_Out.Append("{}");
                        break;
                }
            }

            void Fields(params (string Name, object Value)[] fields)
            {
                m_Out.Append('{');
                m_Indent++;
                bool first = true;
                foreach (var (name, value) in fields)
                {
                    Key(name, ref first);
                    switch (value)
                    {
                        case float f: WriteNumber(f); break;
                        case string s: WriteString(s); break;
                        case Vector3 v: Fields(("x", v.x), ("y", v.y), ("z", v.z)); break;
                        case Vector3Int v: Fields(("x", v.x), ("y", v.y), ("z", v.z)); break;
                        case IFormattable n: m_Out.Append(n.ToString(null, CultureInfo.InvariantCulture)); break;
                    }
                }
                m_Indent--;
                NewLine();
                m_Out.Append('}');
            }

            void WriteNumber(double value)
            {
                if (double.IsNaN(value)) { m_Out.Append("NaN"); return; }
                if (double.IsPositiveInfinity(value)) { m_Out.Append("Infinity"); return; }
                if (double.IsNegativeInfinity(value)) { m_Out.Append("-Infinity"); return; }
                var text = value.ToString("R", CultureInfo.InvariantCulture);
                if (text.IndexOfAny(new[] { '.', 'E', 'e' }) < 0) text += ".0";
                m_Out.Append(text);
            }

            void WriteString(string s)
            {
                m_Out.Append('"');
                foreach (var ch in s)
                {
                    switch (ch)
                    {
                        case '"': m_Out.Append("\\\""); break;
                        case '\\': m_Out.Append("\\\\"); break;
                        case '\n': m_Out.Append("\\n"); break;
                        case '\r': m_Out.Append("\\r"); break;
                        case '\t': m_Out.Append("\\t"); break;
                        case '\b': m_Out.Append("\\b"); break;
                        case '\f': m_Out.Append("\\f"); break;
                        default:
                            if (ch < 0x20) m_Out.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                            else m_Out.Append(ch);
                            break;
                    }
                }
                m_Out.Append('"');
            }
        }

        // ------------------------------------------------------------------ reading

        sealed class JsonReader
        {
            readonly List<ISerializationCallbackReceiver> m_Callbacks = new List<ISerializationCallbackReceiver>();

            internal void Complete()
            {
                foreach (var receiver in m_Callbacks)
                {
                    try { receiver.OnAfterDeserialize(); }
                    catch (Exception e) { Debug.LogException(e); }
                }
            }

            /// <summary>Fills the fields the JSON names; returns the (boxed, for structs) object.</summary>
            internal object ReadFields(object target, Type type, JsonNode node, int depth)
            {
                foreach (var member in SerializationRules.Members(type))
                {
                    if (member.Shape == Shape.ManagedReference) continue;
                    var value = node.Get(member.Name);
                    if (value == null)
                        foreach (var former in member.FormerNames)
                            if ((value = node.Get(former)) != null) break;
                    if (value == null) continue;
                    try
                    {
                        object result;
                        if (member.Shape == Shape.Array || member.Shape == Shape.List)
                            result = ReadList(value, member.Field.FieldType, member.ElementType, member.ElementShape, member.Field.GetValue(target), depth + 1);
                        else
                            result = ReadValue(value, member.Field.FieldType, member.Shape, member.Field.GetValue(target), depth + 1);
                        member.Field.SetValue(target, result);
                    }
                    catch (InvalidCastException) { }
                    catch (FormatException) { }
                    catch (OverflowException) { }
                }
                if (target is ISerializationCallbackReceiver receiver) m_Callbacks.Add(receiver);
                return target;
            }

            object ReadList(JsonNode node, Type fieldType, Type elementType, Shape shape, object existing, int depth)
            {
                if (node.Kind != JsonKind.Array) return existing;
                var items = node.Items;
                if (fieldType.IsArray)
                {
                    var array = Array.CreateInstance(elementType, items.Count);
                    for (int i = 0; i < items.Count; i++) array.SetValue(ReadValue(items[i], elementType, shape, null, depth), i);
                    return array;
                }
                var list = (IList)Activator.CreateInstance(fieldType);
                foreach (var item in items) list.Add(ReadValue(item, elementType, shape, null, depth));
                return list;
            }

            object ReadValue(JsonNode node, Type type, Shape shape, object existing, int depth)
            {
                switch (shape)
                {
                    case Shape.Bool: return node.Kind == JsonKind.Bool ? node.Bool : node.Number != 0;
                    case Shape.SByte: return (sbyte)node.Number;
                    case Shape.Byte: return (byte)node.Number;
                    case Shape.Short: return (short)node.Number;
                    case Shape.UShort: return (ushort)node.Number;
                    case Shape.Int: return (int)node.Number;
                    case Shape.UInt: return (uint)node.Number;
                    case Shape.Long: return node.Integer;
                    case Shape.ULong: return node.UnsignedInteger;
                    case Shape.Char: return (char)(int)node.Number;
                    case Shape.Float: return (float)node.Number;
                    case Shape.Double: return node.Number;
                    case Shape.String: return node.Kind == JsonKind.String ? node.Text : node.Kind == JsonKind.Null ? "" : node.Text;
                    case Shape.Enum: return Enum.ToObject(type, node.Integer);
                    case Shape.UnityObject:
                    {
                        var id = (int)(node.Get("instanceID")?.Number ?? 0);
                        var obj = id == 0 ? null : ObjectRegistry.FindById(id);
                        return obj != null && type.IsInstanceOfType(obj) ? obj : null;
                    }
                    case Shape.Vector2: return new Vector2(F(node, "x"), F(node, "y"));
                    case Shape.Vector3: return new Vector3(F(node, "x"), F(node, "y"), F(node, "z"));
                    case Shape.Vector4: return new Vector4(F(node, "x"), F(node, "y"), F(node, "z"), F(node, "w"));
                    case Shape.Quaternion: return new Quaternion(F(node, "x"), F(node, "y"), F(node, "z"), F(node, "w"));
                    case Shape.Color: return new Color(F(node, "r"), F(node, "g"), F(node, "b"), F(node, "a"));
                    case Shape.Color32: return new Color32((byte)F(node, "r"), (byte)F(node, "g"), (byte)F(node, "b"), (byte)F(node, "a"));
                    case Shape.Vector2Int: return new Vector2Int((int)F(node, "x"), (int)F(node, "y"));
                    case Shape.Vector3Int: return new Vector3Int((int)F(node, "x"), (int)F(node, "y"), (int)F(node, "z"));
                    case Shape.Rect: return new Rect(F(node, "x"), F(node, "y"), F(node, "width"), F(node, "height"));
                    case Shape.RectInt: return new RectInt((int)F(node, "x"), (int)F(node, "y"), (int)F(node, "width"), (int)F(node, "height"));
                    case Shape.LayerMask: return (LayerMask)(int)(uint)(node.Get("m_Bits")?.UnsignedInteger ?? 0);
                    case Shape.RectOffset: return new RectOffset((int)F(node, "m_Left"), (int)F(node, "m_Right"), (int)F(node, "m_Top"), (int)F(node, "m_Bottom"));
                    case Shape.Bounds:
                    {
                        var c = node.Get("m_Center") ?? JsonNode.Empty;
                        var e = node.Get("m_Extent") ?? JsonNode.Empty;
                        return new Bounds(new Vector3(F(c, "x"), F(c, "y"), F(c, "z")), 2f * new Vector3(F(e, "x"), F(e, "y"), F(e, "z")));
                    }
                    case Shape.BoundsInt:
                    {
                        var p = node.Get("m_Position") ?? JsonNode.Empty;
                        var s = node.Get("m_Size") ?? JsonNode.Empty;
                        return new BoundsInt(new Vector3Int((int)F(p, "x"), (int)F(p, "y"), (int)F(p, "z")), new Vector3Int((int)F(s, "x"), (int)F(s, "y"), (int)F(s, "z")));
                    }
                    case Shape.Matrix4x4:
                    {
                        var m = new Matrix4x4();
                        for (int r = 0; r < 4; r++)
                            for (int c = 0; c < 4; c++)
                                m[r, c] = F(node, $"e{r}{c}");
                        return m;
                    }
                    case Shape.Hash128: return Hash128.Parse(node.Get("Hash")?.Text ?? "");
                    case Shape.Composite:
                    {
                        if (node.Kind != JsonKind.Object || depth > 10) return existing;
                        var target = existing ?? SerializationRules.Create(type);
                        return ReadFields(target, target.GetType(), node, depth);
                    }
                    default:
                        return existing;
                }
            }

            static float F(JsonNode node, string key) => (float)(node.Get(key)?.Number ?? 0);
        }

        // ------------------------------------------------------------------ parsing

        enum JsonKind { Null, Bool, Number, String, Array, Object }

        sealed class JsonNode
        {
            internal static readonly JsonNode Empty = new JsonNode { Kind = JsonKind.Object, Members = new List<(string, JsonNode)>() };

            internal JsonKind Kind;
            internal bool Bool;
            /// <summary>A number's or string's text.</summary>
            internal string Text = "";
            internal List<JsonNode> Items;
            internal List<(string Key, JsonNode Value)> Members;

            internal JsonNode Get(string key)
            {
                if (Members == null) return null;
                // The last duplicate wins, as in Unity's reader.
                for (int i = Members.Count - 1; i >= 0; i--)
                    if (Members[i].Key == key) return Members[i].Value;
                return null;
            }

            internal double Number
            {
                get
                {
                    if (Kind == JsonKind.Bool) return Bool ? 1 : 0;
                    if (Kind != JsonKind.Number && Kind != JsonKind.String) return 0;
                    switch (Text)
                    {
                        case "NaN": return double.NaN;
                        case "Infinity": return double.PositiveInfinity;
                        case "-Infinity": return double.NegativeInfinity;
                    }
                    return double.TryParse(Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
                }
            }

            internal long Integer =>
                long.TryParse(Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : (long)Number;

            internal ulong UnsignedInteger =>
                ulong.TryParse(Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : (ulong)Math.Max(0, Number);
        }

        static class JsonParser
        {
            internal static JsonNode Parse(string text)
            {
                int i = 0;
                var node = ParseValue(text, ref i);
                SkipSpace(text, ref i);
                if (i < text.Length) throw Error("The document root must not be followed by other values.");
                return node;
            }

            static ArgumentException Error(string message) => new ArgumentException("JSON parse error: " + message);

            static void SkipSpace(string s, ref int i)
            {
                while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            }

            static JsonNode ParseValue(string s, ref int i)
            {
                SkipSpace(s, ref i);
                if (i >= s.Length) throw Error("The document is empty.");
                char c = s[i];
                switch (c)
                {
                    case '{': return ParseObject(s, ref i);
                    case '[': return ParseArray(s, ref i);
                    case '"': return new JsonNode { Kind = JsonKind.String, Text = ParseString(s, ref i) };
                    case 't': Expect(s, ref i, "true"); return new JsonNode { Kind = JsonKind.Bool, Bool = true };
                    case 'f': Expect(s, ref i, "false"); return new JsonNode { Kind = JsonKind.Bool, Bool = false };
                    case 'n': Expect(s, ref i, "null"); return new JsonNode { Kind = JsonKind.Null };
                    case 'N': Expect(s, ref i, "NaN"); return new JsonNode { Kind = JsonKind.Number, Text = "NaN" };
                    case 'I': Expect(s, ref i, "Infinity"); return new JsonNode { Kind = JsonKind.Number, Text = "Infinity" };
                }
                if (c == '-' || (c >= '0' && c <= '9'))
                {
                    int start = i;
                    if (c == '-' && i + 1 < s.Length && s[i + 1] == 'I')
                    {
                        i++;
                        Expect(s, ref i, "Infinity");
                        return new JsonNode { Kind = JsonKind.Number, Text = "-Infinity" };
                    }
                    i++;
                    while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || s[i] == 'e' || s[i] == 'E' || s[i] == '+' || s[i] == '-')) i++;
                    return new JsonNode { Kind = JsonKind.Number, Text = s.Substring(start, i - start) };
                }
                throw Error("Invalid value.");
            }

            static void Expect(string s, ref int i, string word)
            {
                if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) throw Error("Invalid value.");
                i += word.Length;
            }

            static JsonNode ParseObject(string s, ref int i)
            {
                var node = new JsonNode { Kind = JsonKind.Object, Members = new List<(string, JsonNode)>() };
                i++;
                SkipSpace(s, ref i);
                if (i < s.Length && s[i] == '}') { i++; return node; }
                while (true)
                {
                    SkipSpace(s, ref i);
                    if (i >= s.Length || s[i] != '"') throw Error("Missing a name for object member.");
                    var key = ParseString(s, ref i);
                    SkipSpace(s, ref i);
                    if (i >= s.Length || s[i] != ':') throw Error("Missing a colon after a name of object member.");
                    i++;
                    node.Members.Add((key, ParseValue(s, ref i)));
                    SkipSpace(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == '}') { i++; return node; }
                    throw Error("Missing a comma or '}' after an object member.");
                }
            }

            static JsonNode ParseArray(string s, ref int i)
            {
                var node = new JsonNode { Kind = JsonKind.Array, Items = new List<JsonNode>() };
                i++;
                SkipSpace(s, ref i);
                if (i < s.Length && s[i] == ']') { i++; return node; }
                while (true)
                {
                    node.Items.Add(ParseValue(s, ref i));
                    SkipSpace(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == ']') { i++; return node; }
                    throw Error("Missing a comma or ']' after an array element.");
                }
            }

            static string ParseString(string s, ref int i)
            {
                var sb = new StringBuilder();
                i++;
                while (i < s.Length)
                {
                    char c = s[i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (i >= s.Length) break;
                    char e = s[i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 > s.Length) throw Error("Incorrect hex digit after \\u escape in string.");
                            sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            i += 4;
                            break;
                        default: throw Error("Invalid escape character in string.");
                    }
                }
                throw Error("Missing a closing quotation mark in string.");
            }
        }
    }
}
