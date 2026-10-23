using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Serialization;
using Object = UnityEngine.Object;

namespace Unbound.Runtime
{
    /// <summary>How a serialized field's type is stored.</summary>
    internal enum Shape
    {
        Unsupported,
        Bool, SByte, Byte, Short, UShort, Int, UInt, Long, ULong, Float, Double, Char, String, Enum,
        UnityObject,
        Vector2, Vector3, Vector4, Vector2Int, Vector3Int, Quaternion, Color, Color32, Rect, RectInt,
        Bounds, BoundsInt, Matrix4x4, LayerMask, Hash128,
        AnimationCurve, Gradient, RectOffset,
        /// <summary>A [Serializable] class or struct written field by field.</summary>
        Composite,
        Array,
        List,
        /// <summary>A [SerializeReference] field: polymorphic, by reference id.</summary>
        ManagedReference,
    }

    internal sealed class SerializedMember
    {
        internal FieldInfo Field;
        internal string Name;
        internal string[] FormerNames;
        internal Shape Shape;
        internal Type ElementType;
        internal Shape ElementShape;
    }

    /// <summary>
    /// Unity's serialization rules: which fields are serialized and how each type is written. Used
    /// to read scene and asset data, to clone objects (Instantiate), and by JsonUtility.
    /// </summary>
    internal static class SerializationRules
    {
        const int MaxDepth = 10;
        static readonly ConcurrentDictionary<Type, SerializedMember[]> s_Members = new ConcurrentDictionary<Type, SerializedMember[]>();

        internal static void ClearCache() => s_Members.Clear();

        /// <summary>The serialized fields of a type, base class fields first, in declaration order.</summary>
        internal static SerializedMember[] Members(Type type) => s_Members.GetOrAdd(type, Build);

        static SerializedMember[] Build(Type type)
        {
            var chain = new List<Type>();
            for (var t = type; t != null && t != typeof(object) && t != typeof(MonoBehaviour) && t != typeof(ScriptableObject)
                 && t != typeof(Behaviour) && t != typeof(Component) && t != typeof(Object); t = t.BaseType)
                chain.Add(t);
            chain.Reverse();
            var members = new List<SerializedMember>();
            foreach (var t in chain)
            {
                foreach (var field in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (field.IsInitOnly || field.IsLiteral || field.IsDefined(typeof(NonSerializedAttribute), false))
                        continue;
                    bool managedReference = field.IsDefined(typeof(SerializeReference), false);
                    bool marked = field.IsPublic || field.IsDefined(typeof(SerializeField), false) || managedReference;
                    if (!marked) continue;
                    var member = new SerializedMember { Field = field, Name = field.Name };
                    var former = field.GetCustomAttributes<FormerlySerializedAsAttribute>(false);
                    var names = new List<string>();
                    foreach (var f in former) names.Add(f.oldName);
                    member.FormerNames = names.ToArray();
                    if (managedReference)
                    {
                        member.Shape = ManagedReferenceShape(field.FieldType, out member.ElementType);
                        if (member.Shape == Shape.Unsupported) continue;
                        member.ElementShape = Shape.ManagedReference;
                    }
                    else
                    {
                        member.Shape = Classify(field.FieldType, 0);
                        if (member.Shape == Shape.Unsupported) continue;
                        if (member.Shape == Shape.Array || member.Shape == Shape.List)
                        {
                            member.ElementType = ElementOf(field.FieldType);
                            member.ElementShape = Classify(member.ElementType, 1);
                            if (member.ElementShape == Shape.Unsupported || member.ElementShape == Shape.Array || member.ElementShape == Shape.List)
                                continue;
                        }
                    }
                    members.Add(member);
                }
            }
            return members.ToArray();
        }

        static Shape ManagedReferenceShape(Type type, out Type element)
        {
            element = null;
            if (type.IsArray)
            {
                element = type.GetElementType();
                return element.IsValueType ? Shape.Unsupported : Shape.Array;
            }
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                element = type.GetGenericArguments()[0];
                return element.IsValueType ? Shape.Unsupported : Shape.List;
            }
            return type.IsValueType || typeof(Object).IsAssignableFrom(type) ? Shape.Unsupported : Shape.ManagedReference;
        }

        internal static Type ElementOf(Type collection) =>
            collection.IsArray ? collection.GetElementType() : collection.GetGenericArguments()[0];

        internal static Shape Classify(Type t, int depth)
        {
            if (t == typeof(bool)) return Shape.Bool;
            if (t == typeof(sbyte)) return Shape.SByte;
            if (t == typeof(byte)) return Shape.Byte;
            if (t == typeof(short)) return Shape.Short;
            if (t == typeof(ushort)) return Shape.UShort;
            if (t == typeof(int)) return Shape.Int;
            if (t == typeof(uint)) return Shape.UInt;
            if (t == typeof(long)) return Shape.Long;
            if (t == typeof(ulong)) return Shape.ULong;
            if (t == typeof(float)) return Shape.Float;
            if (t == typeof(double)) return Shape.Double;
            if (t == typeof(char)) return Shape.Char;
            if (t == typeof(string)) return Shape.String;
            if (t.IsEnum) return Shape.Enum;
            if (typeof(Object).IsAssignableFrom(t)) return Shape.UnityObject;
            if (t == typeof(Vector2)) return Shape.Vector2;
            if (t == typeof(Vector3)) return Shape.Vector3;
            if (t == typeof(Vector4)) return Shape.Vector4;
            if (t == typeof(Vector2Int)) return Shape.Vector2Int;
            if (t == typeof(Vector3Int)) return Shape.Vector3Int;
            if (t == typeof(Quaternion)) return Shape.Quaternion;
            if (t == typeof(Color)) return Shape.Color;
            if (t == typeof(Color32)) return Shape.Color32;
            if (t == typeof(Rect)) return Shape.Rect;
            if (t == typeof(RectInt)) return Shape.RectInt;
            if (t == typeof(Bounds)) return Shape.Bounds;
            if (t == typeof(BoundsInt)) return Shape.BoundsInt;
            if (t == typeof(Matrix4x4)) return Shape.Matrix4x4;
            if (t == typeof(LayerMask)) return Shape.LayerMask;
            if (t == typeof(Hash128)) return Shape.Hash128;
            if (t == typeof(AnimationCurve)) return Shape.AnimationCurve;
            if (t == typeof(Gradient)) return Shape.Gradient;
            if (t == typeof(RectOffset)) return Shape.RectOffset;
            if (t.IsArray) return t.GetArrayRank() == 1 ? Shape.Array : Shape.Unsupported;
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>)) return Shape.List;
            if (depth > MaxDepth) return Shape.Unsupported;
            if (IsSerializableComposite(t)) return Shape.Composite;
            return Shape.Unsupported;
        }

        static bool IsSerializableComposite(Type t)
        {
            if (t.IsAbstract || t.IsInterface || t.IsPointer || t.ContainsGenericParameters) return false;
            if (t.IsPrimitive || t == typeof(decimal) || t == typeof(IntPtr)) return false;
            if (!t.IsSerializable && t.GetCustomAttribute<SerializableAttribute>() == null) return false;
            var ns = t.Namespace ?? "";
            // Framework collections and the like are not serialized by Unity.
            if (ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal))
                return false;
            if (typeof(Delegate).IsAssignableFrom(t)) return false;
            return true;
        }

        /// <summary>A new instance for a serialized class or struct: its constructor runs, as in Unity.</summary>
        internal static object Create(Type type)
        {
            if (type.IsValueType) return Activator.CreateInstance(type);
            var ctor = type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (ctor != null)
            {
                try { return ctor.Invoke(null); }
                catch (TargetInvocationException e) { Debug.LogException(e.InnerException ?? e); }
            }
            return RuntimeHelpers.GetUninitializedObject(type);
        }
    }

    /// <summary>Resolves serialized object references of the data being read.</summary>
    internal interface IReferenceResolver
    {
        Object Resolve(ObjectRef reference, Type expected);
    }

    /// <summary>Reads Unity's YAML serialization into objects.</summary>
    internal sealed class YamlReader
    {
        readonly IReferenceResolver m_Resolver;
        Dictionary<long, object> m_ManagedReferences;
        YNode m_ReferenceTable;
        readonly List<ISerializationCallbackReceiver> m_Callbacks = new List<ISerializationCallbackReceiver>();

        internal YamlReader(IReferenceResolver resolver)
        {
            m_Resolver = resolver;
        }

        /// <summary>Fills the serialized fields of a script object (MonoBehaviour, ScriptableObject)
        /// from its document body, then runs OnAfterDeserialize callbacks.</summary>
        internal void ReadScriptObject(object target, YNode body)
        {
            m_ReferenceTable = body.Get("references");
            m_ManagedReferences = null;
            m_Callbacks.Clear();
            ReadFields(target, target.GetType(), body, 0);
            if (target is ISerializationCallbackReceiver receiver) m_Callbacks.Add(receiver);
            foreach (var callback in m_Callbacks)
            {
                try { callback.OnAfterDeserialize(); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        internal void ReadFields(object target, Type type, YNode map, int depth)
        {
            if (map == null || map.Kind != YKind.Map) return;
            foreach (var member in SerializationRules.Members(type))
            {
                var node = map.Get(member.Name);
                if (node == null)
                {
                    foreach (var former in member.FormerNames)
                    {
                        node = map.Get(former);
                        if (node != null) break;
                    }
                }
                if (node == null) continue;
                try
                {
                    var existing = member.Field.GetValue(target);
                    var value = member.Shape == Shape.ManagedReference
                        ? ReadManagedReference(node)
                        : (member.Shape == Shape.Array || member.Shape == Shape.List) && member.ElementShape == Shape.ManagedReference
                            ? ReadCollection(node, member.Field.FieldType, member.ElementType, Shape.ManagedReference, depth)
                            : ReadValue(node, member.Field.FieldType, member.Shape, member.ElementType, member.ElementShape, existing, depth);
                    member.Field.SetValue(target, value);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Unbound: could not read field {type.Name}.{member.Name}: {e.Message}");
                }
            }
        }

        internal object ReadValue(YNode node, Type type, Shape shape, Type elementType, Shape elementShape, object existing, int depth)
        {
            switch (shape)
            {
                case Shape.Bool: return node.AsBool();
                case Shape.SByte: return (sbyte)node.AsLong();
                case Shape.Byte: return (byte)node.AsLong();
                case Shape.Short: return (short)node.AsLong();
                case Shape.UShort: return (ushort)node.AsLong();
                case Shape.Int: return (int)node.AsLong();
                case Shape.UInt: return (uint)node.AsLong();
                case Shape.Long: return node.AsLong();
                case Shape.ULong: return node.AsULong();
                case Shape.Float: return node.AsFloat();
                case Shape.Double: return node.AsDouble();
                case Shape.Char: return (char)node.AsLong();
                case Shape.String: return node.Kind == YKind.Scalar ? node.Scalar : "";
                case Shape.Enum: return Enum.ToObject(type, node.AsLong());
                case Shape.UnityObject:
                {
                    var reference = node.AsRef();
                    if (reference.IsNull) return null;
                    var obj = m_Resolver.Resolve(reference, type);
                    return obj != null && type.IsInstanceOfType(obj) ? obj : null;
                }
                case Shape.Vector2: return new Vector2(node.Float("x"), node.Float("y"));
                case Shape.Vector3: return new Vector3(node.Float("x"), node.Float("y"), node.Float("z"));
                case Shape.Vector4: return new Vector4(node.Float("x"), node.Float("y"), node.Float("z"), node.Float("w"));
                case Shape.Vector2Int: return new Vector2Int(node.Int("x"), node.Int("y"));
                case Shape.Vector3Int: return new Vector3Int(node.Int("x"), node.Int("y"), node.Int("z"));
                case Shape.Quaternion: return new Quaternion(node.Float("x"), node.Float("y"), node.Float("z"), node.Float("w", 1f));
                case Shape.Color: return new Color(node.Float("r"), node.Float("g"), node.Float("b"), node.Float("a"));
                case Shape.Color32:
                {
                    uint rgba = (uint)node.Long("rgba");
                    return new Color32((byte)(rgba & 0xff), (byte)((rgba >> 8) & 0xff), (byte)((rgba >> 16) & 0xff), (byte)(rgba >> 24));
                }
                case Shape.Rect: return new Rect(node.Float("x"), node.Float("y"), node.Float("width"), node.Float("height"));
                case Shape.RectInt: return new RectInt(node.Int("x"), node.Int("y"), node.Int("width"), node.Int("height"));
                case Shape.Bounds: return new Bounds(node.Vector3("m_Center"), node.Vector3("m_Extent") * 2f);
                case Shape.BoundsInt:
                {
                    var p = node.Get("m_Position");
                    var s = node.Get("m_Size");
                    return new BoundsInt(p?.Int("x") ?? 0, p?.Int("y") ?? 0, p?.Int("z") ?? 0, s?.Int("x") ?? 0, s?.Int("y") ?? 0, s?.Int("z") ?? 0);
                }
                case Shape.Matrix4x4:
                {
                    var m = new Matrix4x4();
                    for (int r = 0; r < 4; r++)
                        for (int c = 0; c < 4; c++)
                            m[r, c] = node.Float($"e{r}{c}");
                    return m;
                }
                case Shape.LayerMask: return (LayerMask)(int)node.Long("m_Bits");
                case Shape.Hash128: return Hash128.Parse(node.Str("Hash", node.Kind == YKind.Scalar ? node.Scalar : ""));
                case Shape.AnimationCurve: return BuiltinReaders.AnimationCurve(node);
                case Shape.Gradient: return BuiltinReaders.Gradient(node);
                case Shape.RectOffset: return new RectOffset(node.Int("m_Left"), node.Int("m_Right"), node.Int("m_Top"), node.Int("m_Bottom"));
                case Shape.Composite:
                {
                    var instance = existing ?? SerializationRules.Create(type);
                    ReadFields(instance, type, node, depth + 1);
                    if (instance is ISerializationCallbackReceiver receiver)
                    {
                        if (type.IsValueType)
                        {
                            // A struct is copied into its field when returned: its callback must
                            // run on this box first, or what it sets is lost.
                            try { receiver.OnAfterDeserialize(); }
                            catch (Exception e) { Debug.LogException(e); }
                        }
                        else
                        {
                            m_Callbacks.Add(receiver);
                        }
                    }
                    return instance;
                }
                case Shape.Array:
                case Shape.List:
                    return ReadCollection(node, type, elementType, elementShape, depth);
                default:
                    return existing;
            }
        }

        object ReadCollection(YNode node, Type collectionType, Type elementType, Shape elementShape, int depth)
        {
            IList items;
            int count;
            if (node.Kind == YKind.Scalar && ((elementShape <= Shape.Double && elementShape >= Shape.Bool) || elementShape == Shape.Char || elementShape == Shape.Enum))
            {
                // Arrays of primitives and enums may be written as one hex string of their
                // little-endian bytes.
                var values = BuiltinReaders.HexArray(node.Scalar, elementType);
                count = values.Count;
                items = values;
            }
            else
            {
                count = node.Kind == YKind.Seq ? node.Items.Length : 0;
                items = null;
            }
            IList result;
            if (collectionType.IsArray) result = Array.CreateInstance(elementType, count);
            else result = (IList)Activator.CreateInstance(collectionType, count);
            for (int i = 0; i < count; i++)
            {
                object value;
                if (items != null) value = items[i];
                else if (elementShape == Shape.ManagedReference) value = ReadManagedReference(node.Items[i]);
                else value = ReadValue(node.Items[i], elementType, elementShape, null, Shape.Unsupported, null, depth + 1);
                if (collectionType.IsArray) result[i] = value;
                else result.Add(value);
            }
            return result;
        }

        /// <summary>A managed reference against the object's `references` table: `{rid: N}` and a
        /// `RefIds` list (version 2, Unity 2021.2+), or `{id: N}` and entries keyed by the id as eight
        /// uppercase hex digits (version 1, Unity 2019.3 to 2021.1).</summary>
        object ReadManagedReference(YNode node)
        {
            long rid = node.Kind == YKind.Map ? node.Long("rid", node.Long("id", -2)) : node.AsLong(-2);
            if (rid < 0 || m_ReferenceTable == null) return null;
            m_ManagedReferences ??= new Dictionary<long, object>();
            if (m_ManagedReferences.TryGetValue(rid, out var existing)) return existing;
            YNode found = null;
            var list = m_ReferenceTable.Get("RefIds");
            if (list != null)
            {
                foreach (var entry in list.Seq())
                {
                    if (entry.Long("rid", long.MinValue) != rid) continue;
                    found = entry;
                    break;
                }
            }
            else
            {
                found = m_ReferenceTable.Get(rid.ToString("X8", CultureInfo.InvariantCulture));
            }
            if (found == null) return null;
            var typeNode = found.Get("type");
            var type = TypeResolver.Find(typeNode?.Str("class"), typeNode?.Str("ns"), typeNode?.Str("asm"));
            if (type == null) return null;
            var instance = SerializationRules.Create(type);
            m_ManagedReferences[rid] = instance;
            ReadFields(instance, type, found.Get("data"), 1);
            if (instance is ISerializationCallbackReceiver receiver) m_Callbacks.Add(receiver);
            return instance;
        }
    }

    /// <summary>Copies serialized state from one object to another, as Instantiate does: serialized
    /// fields are copied (deeply), references into the cloned set are remapped, non-serialized
    /// fields keep the values the constructor gave them.</summary>
    internal sealed class SerializedCopier
    {
        readonly Func<Object, Object> m_Remap;
        readonly Dictionary<object, object> m_ManagedCopies = new Dictionary<object, object>(ReferenceEqualityComparer.Instance);

        internal SerializedCopier(Func<Object, Object> remap)
        {
            m_Remap = remap;
        }

        internal void CopyScriptObject(object source, object target)
        {
            if (source is ISerializationCallbackReceiver before)
            {
                try { before.OnBeforeSerialize(); }
                catch (Exception e) { Debug.LogException(e); }
            }
            CopyFields(source, target, source.GetType(), 0);
            if (target is ISerializationCallbackReceiver after)
            {
                try { after.OnAfterDeserialize(); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        void CopyFields(object source, object target, Type type, int depth)
        {
            foreach (var member in SerializationRules.Members(type))
            {
                var value = member.Field.GetValue(source);
                member.Field.SetValue(target, Copy(value, member.Field.FieldType, member.Shape, member.ElementType, member.ElementShape, depth));
            }
        }

        object Copy(object value, Type type, Shape shape, Type elementType, Shape elementShape, int depth)
        {
            if (value == null)
            {
                // Unity never leaves a serialized class field or collection null.
                if (shape == Shape.Composite && !type.IsValueType) return SerializationRules.Create(type);
                if (shape == Shape.Array) return Array.CreateInstance(elementType, 0);
                if (shape == Shape.List) return Activator.CreateInstance(type);
                if (shape == Shape.String) return "";
                return null;
            }
            switch (shape)
            {
                case Shape.UnityObject:
                    return m_Remap((Object)value);
                case Shape.AnimationCurve:
                    return new AnimationCurve(((AnimationCurve)value).keys) { preWrapMode = ((AnimationCurve)value).preWrapMode, postWrapMode = ((AnimationCurve)value).postWrapMode };
                case Shape.Gradient:
                {
                    var g = (Gradient)value;
                    var copy = new Gradient { mode = g.mode };
                    copy.SetKeys(g.colorKeys, g.alphaKeys);
                    return copy;
                }
                case Shape.RectOffset:
                {
                    var r = (RectOffset)value;
                    return new RectOffset(r.left, r.right, r.top, r.bottom);
                }
                case Shape.Composite:
                {
                    if (value is ISerializationCallbackReceiver before)
                    {
                        try { before.OnBeforeSerialize(); } catch (Exception e) { Debug.LogException(e); }
                    }
                    var copy = type.IsValueType ? value : SerializationRules.Create(type);
                    if (type.IsValueType) copy = RuntimeHelpers.GetObjectValue(value);
                    CopyFields(value, copy, type, depth + 1);
                    if (copy is ISerializationCallbackReceiver after)
                    {
                        try { after.OnAfterDeserialize(); } catch (Exception e) { Debug.LogException(e); }
                    }
                    return copy;
                }
                case Shape.ManagedReference:
                {
                    if (m_ManagedCopies.TryGetValue(value, out var existing)) return existing;
                    var runtimeType = value.GetType();
                    var copy = SerializationRules.Create(runtimeType);
                    m_ManagedCopies[value] = copy;
                    CopyFields(value, copy, runtimeType, depth + 1);
                    return copy;
                }
                case Shape.Array:
                case Shape.List:
                {
                    var source = (IList)value;
                    IList result = type.IsArray ? Array.CreateInstance(elementType, source.Count) : (IList)Activator.CreateInstance(type, source.Count);
                    for (int i = 0; i < source.Count; i++)
                    {
                        var item = source[i];
                        var element = elementShape == Shape.ManagedReference
                            ? (item == null ? null : Copy(item, item.GetType(), Shape.ManagedReference, null, Shape.Unsupported, depth + 1))
                            : Copy(item, elementType, elementShape, null, Shape.Unsupported, depth + 1);
                        if (type.IsArray) result[i] = element;
                        else result.Add(element);
                    }
                    return result;
                }
                default:
                    return value; // value types and strings are copied by assignment
            }
        }
    }

    internal static class BuiltinReaders
    {
        internal static AnimationCurve AnimationCurve(YNode node)
        {
            var keys = new List<Keyframe>();
            foreach (var k in node.Get("m_Curve")?.Seq() ?? Array.Empty<YNode>())
            {
                var key = new Keyframe(k.Float("time"), k.Float("value"), k.Float("inSlope"), k.Float("outSlope"), k.Float("inWeight", 1f / 3f), k.Float("outWeight", 1f / 3f));
                key.weightedMode = (WeightedMode)k.Int("weightedMode");
#pragma warning disable CS0618
                key.tangentMode = k.Int("tangentMode");
#pragma warning restore CS0618
                keys.Add(key);
            }
            var curve = new AnimationCurve(keys.ToArray())
            {
                preWrapMode = WrapModeOf(node.Int("m_PreInfinity", 2)),
                postWrapMode = WrapModeOf(node.Int("m_PostInfinity", 2)),
            };
            return curve;
        }

        /// <summary>Curves store their infinity modes as the engine's internal enum: 0 ping-pong,
        /// 1 loop, 2 clamp (the default).</summary>
        static WrapMode WrapModeOf(int infinity)
        {
            switch (infinity)
            {
                case 0: return WrapMode.PingPong;
                case 1: return WrapMode.Loop;
                default: return WrapMode.ClampForever;
            }
        }

        internal static Gradient Gradient(YNode node)
        {
            int colorCount = node.Int("m_NumColorKeys", 2);
            int alphaCount = node.Int("m_NumAlphaKeys", 2);
            var colors = new GradientColorKey[colorCount];
            var alphas = new GradientAlphaKey[alphaCount];
            for (int i = 0; i < Math.Max(colorCount, alphaCount); i++)
            {
                var key = node.Color($"key{i}", UnityEngine.Color.white);
                if (i < colorCount) colors[i] = new GradientColorKey(new Color(key.r, key.g, key.b, 1f), node.Int($"ctime{i}") / 65535f);
                if (i < alphaCount) alphas[i] = new GradientAlphaKey(key.a, node.Int($"atime{i}") / 65535f);
            }
            var g = new Gradient { mode = (GradientMode)node.Int("m_Mode") };
            g.SetKeys(colors, alphas);
            return g;
        }

        internal static IList HexArray(string hex, Type elementType)
        {
            var stored = elementType.IsEnum ? Enum.GetUnderlyingType(elementType) : elementType;
            int size = stored == typeof(bool) ? 1 : stored == typeof(char) ? 2 : System.Runtime.InteropServices.Marshal.SizeOf(stored);
            int count = hex.Length / 2 / size;
            var bytes = new byte[count * size];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = byte.Parse(hex.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var array = Array.CreateInstance(elementType, count);
            if (stored == typeof(bool))
            {
                for (int i = 0; i < count; i++) array.SetValue(bytes[i] != 0, i);
            }
            else if (elementType.IsEnum)
            {
                var raw = Array.CreateInstance(stored, count);
                Buffer.BlockCopy(bytes, 0, raw, 0, bytes.Length);
                for (int i = 0; i < count; i++) array.SetValue(Enum.ToObject(elementType, raw.GetValue(i)), i);
            }
            else
            {
                Buffer.BlockCopy(bytes, 0, array, 0, bytes.Length);
            }
            return array;
        }
    }

    /// <summary>Finds game and engine types by the names serialized data uses.</summary>
    internal static class TypeResolver
    {
        static readonly ConcurrentDictionary<string, Type> s_Cache = new ConcurrentDictionary<string, Type>();

        /// <summary>`Namespace.Type, Assembly` as UnityEvents store it. Unity's own types resolve to
        /// Unbound's whatever module the name gives.</summary>
        internal static Type FindByAssemblyQualifiedName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            int depth = 0, comma = -1;
            for (int i = 0; i < name.Length; i++)
            {
                if (name[i] == '[') depth++;
                else if (name[i] == ']') depth--;
                else if (name[i] == ',' && depth == 0) { comma = i; break; }
            }
            var typeName = (comma < 0 ? name : name.Substring(0, comma)).Trim();
            var assembly = comma < 0 ? null : name.Substring(comma + 1).Split(',')[0].Trim();
            if (assembly != null && (assembly == "UnityEngine" || assembly.StartsWith("UnityEngine.", StringComparison.Ordinal)))
                assembly = null;
            int dot = typeName.LastIndexOf('.');
            return Find(dot < 0 ? typeName : typeName.Substring(dot + 1), dot < 0 ? "" : typeName.Substring(0, dot), assembly)
                   ?? Find(dot < 0 ? typeName : typeName.Substring(dot + 1), dot < 0 ? "" : typeName.Substring(0, dot), null);
        }

        internal static void ClearCache() => s_Cache.Clear();

        internal static Type Find(string className, string ns, string assembly)
        {
            if (string.IsNullOrEmpty(className)) return null;
            var key = $"{assembly}|{ns}|{className}";
            return s_Cache.GetOrAdd(key, _ =>
            {
                // Nested types are written `Outer/Inner`.
                var fullName = (string.IsNullOrEmpty(ns) ? "" : ns + ".") + className.Replace('/', '+');
                foreach (var asm in GameDomain.Assemblies())
                {
                    if (!string.IsNullOrEmpty(assembly) && asm.GetName().Name != assembly) continue;
                    var type = asm.GetType(fullName, false);
                    if (type != null) return type;
                }
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var type = asm.GetType(fullName, false);
                    if (type != null) return type;
                }
                return null;
            });
        }
    }
}
