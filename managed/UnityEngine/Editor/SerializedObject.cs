using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using Unbound.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEditor
{
    /// <summary>
    /// An editable view of the serialized data of one or more objects, as the Editor gives it to
    /// editor and importer code. The data is read into a buffer (on construction and Update); writes
    /// through SerializedProperty change the buffer only, and ApplyModifiedProperties writes the
    /// changed properties back into every target object.
    /// </summary>
    public partial class SerializedObject : IDisposable
    {
        Object[] m_Targets;
        Object m_Context;
        internal SerializedNode m_UnboundRoot;
        /// <summary>Bumped whenever nodes may have moved (Update, array changes); properties then
        /// find their node again by path.</summary>
        internal int m_UnboundTreeVersion;
        bool m_Modified;
        SerializedNode[] m_OtherRoots;

        public SerializedObject(Object obj) : this(new[] { obj }, null) { }

        public SerializedObject(Object obj, Object context) : this(new[] { obj }, context) { }

        public SerializedObject(Object[] objs) : this(objs, null) { }

        public SerializedObject(Object[] objs, Object context)
        {
            if (objs == null) throw new ArgumentNullException(nameof(objs));
            if (objs.Length == 0) throw new ArgumentException("No objects to serialize.", nameof(objs));
            for (int i = 0; i < objs.Length; i++)
                if (objs[i] == null) throw new ArgumentException($"Object at index {i} is null");
            m_Targets = (Object[])objs.Clone();
            m_Context = context;
            Read();
        }

        void Read()
        {
            m_UnboundRoot = SerializedTree.BuildRoot(m_Targets[0], callbacks: true);
            m_UnboundTreeVersion++;
            m_Modified = false;
            m_OtherRoots = null;
        }

        internal void MarkModified() => m_Modified = true;

        internal void InvalidateNodes() => m_UnboundTreeVersion++;

        public Object targetObject => m_Targets[0];

        public Object[] targetObjects => (Object[])m_Targets.Clone();

        public Object context => m_Context;

        public bool hasModifiedProperties => m_Modified;

        public bool isEditingMultipleObjects => m_Targets.Length > 1;

        public int maxArraySizeForMultiEditing { get; set; } = 1000;

        public bool forceChildVisibility { get; set; }

        public SerializedProperty GetIterator() => new SerializedProperty(this, m_UnboundRoot);

        public SerializedProperty FindProperty(string propertyPath)
        {
            var node = SerializedTree.Find(m_UnboundRoot, propertyPath);
            return node == null || node.Kind == SerializedNodeKind.Root ? null : new SerializedProperty(this, node);
        }

        /// <summary>Re-reads the targets' data, dropping changes not applied.</summary>
        public void Update() => Read();

        /// <summary>Re-reads the data if the target changed since the last read or is a script
        /// (which Unbound cannot track, so always).</summary>
        public bool UpdateIfRequiredOrScript()
        {
            Read();
            return true;
        }

        [Obsolete("UpdateIfDirtyOrScript has been deprecated. Use UpdateIfRequiredOrScript instead.")]
        public void UpdateIfDirtyOrScript() => UpdateIfRequiredOrScript();

        public bool ApplyModifiedProperties() => Apply();

        public bool ApplyModifiedPropertiesWithoutUndo() => Apply();

        bool Apply()
        {
            if (!m_Modified) return false;
            for (int i = 0; i < m_Targets.Length; i++)
            {
                var target = m_Targets[i];
                if (target == null) continue;
                SerializedTree.Apply(m_UnboundRoot, target, primary: i == 0);
            }
            SerializedTree.ClearDirty(m_UnboundRoot);
            m_Modified = false;
            m_OtherRoots = null;
            foreach (var target in m_Targets)
            {
                if (target == null || !SerializedTree.IsScript(target)) continue;
                if (target is ISerializationCallbackReceiver receiver)
                {
                    try { receiver.OnAfterDeserialize(); }
                    catch (Exception e) { Debug.LogException(e); }
                }
                CallOnValidate(target);
            }
            return true;
        }

        static void CallOnValidate(Object target)
        {
            var method = ScriptMessages.Find(target.GetType(), "OnValidate", Type.EmptyTypes);
            if (method == null) return;
            try { method.Invoke(target, null); }
            catch (TargetInvocationException e) { Debug.LogException(e.InnerException ?? e); }
        }

        public void SetIsDifferentCacheDirty() => m_OtherRoots = null;

        public void CopyFromSerializedProperty(SerializedProperty prop)
        {
            if (prop == null) throw new ArgumentNullException(nameof(prop));
            CopyFrom(prop);
        }

        public bool CopyFromSerializedPropertyIfDifferent(SerializedProperty prop)
        {
            if (prop == null) throw new ArgumentNullException(nameof(prop));
            var target = SerializedTree.Find(m_UnboundRoot, prop.propertyPath);
            if (target != null && SerializedTree.DataEquals(target, prop.UnboundNode)) return false;
            return CopyFrom(prop);
        }

        bool CopyFrom(SerializedProperty prop)
        {
            var source = prop.UnboundNode;
            var target = SerializedTree.Find(m_UnboundRoot, source.Path);
            if (target == null || !SerializedTree.CopyData(target, source)) return false;
            m_Modified = true;
            InvalidateNodes();
            return true;
        }

        public void Dispose() { }

        /// <summary>The trees of the other targets, read on demand to compare values.</summary>
        internal SerializedNode[] OtherRoots()
        {
            if (m_OtherRoots != null) return m_OtherRoots;
            var roots = new SerializedNode[m_Targets.Length - 1];
            for (int i = 1; i < m_Targets.Length; i++)
                roots[i - 1] = m_Targets[i] != null ? SerializedTree.BuildRoot(m_Targets[i], callbacks: true) : null;
            return m_OtherRoots = roots;
        }
    }

    /// <summary>
    /// A position in a SerializedObject's data: one property, by path. It reads and writes that
    /// property's value and moves through the properties in Unity's order (Next, NextVisible).
    /// </summary>
    public partial class SerializedProperty : IDisposable
    {
        internal SerializedObject m_SerializedObject;
        SerializedNode m_UnboundNode;
        string m_UnboundPath;
        int m_UnboundVersion;
        bool m_UnboundAtEnd;

        static readonly ConditionalWeakTable<Object, Dictionary<string, bool>> s_Expanded = new ConditionalWeakTable<Object, Dictionary<string, bool>>();

        internal SerializedProperty() { }

        internal SerializedProperty(SerializedObject serializedObject, SerializedNode node)
        {
            m_SerializedObject = serializedObject;
            MoveTo(node);
        }

        void MoveTo(SerializedNode node)
        {
            m_UnboundNode = node;
            m_UnboundAtEnd = node == null;
            m_UnboundPath = node?.Path;
            m_UnboundVersion = m_SerializedObject.m_UnboundTreeVersion;
        }

        /// <summary>The node this property is on; throws past the end, as Unity does.</summary>
        internal SerializedNode UnboundNode
        {
            get
            {
                if (m_UnboundAtEnd)
                    throw new InvalidOperationException("The operation is not possible when moved past all properties (Next returned false)");
                if (m_UnboundVersion != m_SerializedObject.m_UnboundTreeVersion)
                {
                    m_UnboundNode = SerializedTree.Find(m_SerializedObject.m_UnboundRoot, m_UnboundPath);
                    m_UnboundVersion = m_SerializedObject.m_UnboundTreeVersion;
                }
                if (m_UnboundNode == null)
                    throw new InvalidOperationException($"The property '{m_UnboundPath}' no longer exists in its SerializedObject.");
                return m_UnboundNode;
            }
        }

        SerializedNode N => UnboundNode;

        public SerializedObject serializedObject => m_SerializedObject;

        public void Dispose() { }

        // ------------------------------------------------------------ description

        public string name => N.Name;

        public string displayName
        {
            get
            {
                var n = N;
                if (n.IsElement) return "Element " + n.Index.ToString(CultureInfo.InvariantCulture);
                if (n.Kind == SerializedNodeKind.Root) return n.Name;
                return ObjectNames.NicifyVariableName(n.Name);
            }
        }

        public string propertyPath => N.Path;

        public string type => N.TypeName;

        public string tooltip => N.Tooltip ?? "";

        public int depth => N.Depth;

        public SerializedPropertyType propertyType => N.PropertyType;

        public SerializedPropertyNumericType numericType => SerializedTree.NumericTypeOf(N);

        public bool editable
        {
            get
            {
                _ = N;
                var target = m_SerializedObject.targetObject;
                return target != null && (target.hideFlags & HideFlags.NotEditable) == 0;
            }
        }

        public bool isAnimated => false;

        public bool isInstantiatedPrefab => false;

        public bool prefabOverride
        {
            get => false;
            set { }
        }

        public bool isDefaultOverride => false;

        public bool isExpanded
        {
            get
            {
                var path = N.Path;
                var target = m_SerializedObject.targetObject;
                return target is not null && s_Expanded.TryGetValue(target, out var map) && map.TryGetValue(path, out var expanded) && expanded;
            }
            set
            {
                var path = N.Path;
                var target = m_SerializedObject.targetObject;
                if (target is null) return;
                s_Expanded.GetOrCreateValue(target)[path] = value;
            }
        }

        public bool hasChildren => N.Children.Count > 0;

        public bool hasVisibleChildren => SerializedTree.HasVisibleChildren(N);

        public bool hasMultipleDifferentValues
        {
            get
            {
                var n = N;
                if (!m_SerializedObject.isEditingMultipleObjects) return false;
                foreach (var root in m_SerializedObject.OtherRoots())
                {
                    if (root == null) continue;
                    if (!SerializedTree.DataEquals(n, SerializedTree.Find(root, n.Path))) return true;
                }
                return false;
            }
        }

        public uint contentHash => SerializedTree.ContentHash(N);

        // ------------------------------------------------------------ iteration

        public SerializedProperty Copy()
        {
            var copy = new SerializedProperty
            {
                m_SerializedObject = m_SerializedObject,
                m_UnboundNode = m_UnboundNode,
                m_UnboundPath = m_UnboundPath,
                m_UnboundVersion = m_UnboundVersion,
                m_UnboundAtEnd = m_UnboundAtEnd,
            };
            return copy;
        }

        public bool Next(bool enterChildren)
        {
            if (m_UnboundAtEnd) return false;
            var next = SerializedTree.Next(N, enterChildren);
            MoveTo(next);
            return next != null;
        }

        public bool NextVisible(bool enterChildren)
        {
            if (m_UnboundAtEnd) return false;
            var next = SerializedTree.NextVisible(N, enterChildren);
            MoveTo(next);
            return next != null;
        }

        /// <summary>Moves back to the start, before the first property.</summary>
        public void Reset() => MoveTo(m_SerializedObject.m_UnboundRoot);

        public int CountRemaining()
        {
            var p = Copy();
            int count = 0;
            while (p.NextVisible(true)) count++;
            return count;
        }

        public int CountInProperty()
        {
            var p = Copy();
            var end = GetEndProperty();
            int count = 1;
            while (p.NextVisible(true) && !EqualContents(p, end)) count++;
            return count;
        }

        public SerializedProperty GetEndProperty() => GetEndProperty(false);

        public SerializedProperty GetEndProperty(bool includeInvisible)
        {
            var end = Copy();
            if (includeInvisible) end.Next(false);
            else end.NextVisible(false);
            return end;
        }

        public IEnumerator GetEnumerator()
        {
            if (N.Kind == SerializedNodeKind.Array)
            {
                for (int i = 0; i < arraySize; i++)
                    yield return GetArrayElementAtIndex(i);
            }
            else
            {
                var end = GetEndProperty();
                while (NextVisible(true) && !EqualContents(this, end))
                    yield return this;
            }
        }

        public SerializedProperty FindPropertyRelative(string relativePropertyPath)
        {
            var node = SerializedTree.Find(N, relativePropertyPath);
            return node == null || node == N ? null : new SerializedProperty(m_SerializedObject, node);
        }

        /// <summary>Whether both are at the same property of the same SerializedObject.</summary>
        public static bool EqualContents(SerializedProperty x, SerializedProperty y)
        {
            if (x is null || y is null) return false;
            if (x.m_SerializedObject != y.m_SerializedObject) return false;
            if (x.m_UnboundAtEnd || y.m_UnboundAtEnd) return x.m_UnboundAtEnd && y.m_UnboundAtEnd;
            return x.m_UnboundPath == y.m_UnboundPath;
        }

        /// <summary>Whether both properties hold the same data (type and values).</summary>
        public static bool DataEquals(SerializedProperty x, SerializedProperty y)
        {
            if (x is null || y is null) return false;
            return SerializedTree.DataEquals(x.N, y.N);
        }

        // ------------------------------------------------------------ arrays

        public bool isArray => N.Kind == SerializedNodeKind.Array || N.PropertyType == SerializedPropertyType.String;

        public bool isFixedBuffer => false;

        public int fixedBufferSize => 0;

        public int minArraySize => 0;

        public string arrayElementType
        {
            get
            {
                var n = N;
                if (n.Kind == SerializedNodeKind.Array)
                    return n.ElementShape == Shape.ManagedReference
                        ? SerializedTree.TypeNameOf(n.ElementType, Shape.ManagedReference)
                        : n.ElementShape == Shape.Composite ? n.ElementType.Name : SerializedTree.TypeNameOf(n.ElementType, n.ElementShape);
                if (n.PropertyType == SerializedPropertyType.String) return "char";
                return "";
            }
        }

        public int arraySize
        {
            get
            {
                var n = N;
                if (n.Kind == SerializedNodeKind.Array) return n.ArrayCount;
                if (n.PropertyType == SerializedPropertyType.String && n.Kind == SerializedNodeKind.Leaf) return ((string)n.Value).Length;
                return 0;
            }
            set
            {
                var n = N;
                if (n.Kind != SerializedNodeKind.Array)
                {
                    Debug.LogError("Property is not an array.");
                    return;
                }
                if (SerializedTree.Resize(n, value)) Changed(structural: true);
            }
        }

        SerializedNode ArrayNode()
        {
            var n = N;
            if (n.Kind == SerializedNodeKind.Array) return n;
            Debug.LogError($"Property '{n.Path}' is not an array.");
            return null;
        }

        /// <summary>Unbound serializes no fixed buffers (C# `fixed` arrays), so there are no elements.</summary>
        public SerializedProperty GetFixedBufferElementAtIndex(int index) => null;

        public SerializedProperty GetArrayElementAtIndex(int index)
        {
            var n = N;
            if (n.Kind != SerializedNodeKind.Array || index < 0 || index >= n.ArrayCount) return null;
            return new SerializedProperty(m_SerializedObject, n.Element(index));
        }

        public void InsertArrayElementAtIndex(int index)
        {
            var array = ArrayNode();
            if (array != null && SerializedTree.InsertElement(array, index)) Changed(structural: true);
        }

        public void DeleteArrayElementAtIndex(int index)
        {
            var array = ArrayNode();
            if (array != null && SerializedTree.DeleteElement(array, index)) Changed(structural: true);
        }

        public bool MoveArrayElement(int srcIndex, int dstIndex)
        {
            var array = ArrayNode();
            if (array == null || !SerializedTree.MoveElement(array, srcIndex, dstIndex)) return false;
            Changed(structural: true);
            return true;
        }

        public void ClearArray()
        {
            var array = ArrayNode();
            if (array != null && SerializedTree.Resize(array, 0)) Changed(structural: true);
        }

        /// <summary>Duplicates the array element this property is on.</summary>
        public bool DuplicateCommand()
        {
            var n = N;
            if (!n.IsElement || !SerializedTree.InsertElement(n.Parent.Parent, n.Index)) return false;
            Changed(structural: true);
            return true;
        }

        /// <summary>Deletes the array element this property is on.</summary>
        public bool DeleteCommand()
        {
            var n = N;
            if (!n.IsElement || !SerializedTree.DeleteElement(n.Parent.Parent, n.Index)) return false;
            Changed(structural: true);
            return true;
        }

        void Changed(bool structural)
        {
            m_SerializedObject.MarkModified();
            if (structural) m_SerializedObject.InvalidateNodes();
        }

        // ------------------------------------------------------------ values

        static void Unsupported(string kind) => Debug.LogError($"type is not a supported {kind} value");

        void Write(SerializedNode node, object value)
        {
            if (SerializedTree.SetValue(node, value)) Changed(structural: node.Kind == SerializedNodeKind.ArraySize);
        }

        static bool IsIntegral(SerializedNode n) =>
            n.PropertyType == SerializedPropertyType.Integer || n.PropertyType == SerializedPropertyType.Enum
            || n.PropertyType == SerializedPropertyType.ArraySize || n.PropertyType == SerializedPropertyType.Character
            || n.PropertyType == SerializedPropertyType.LayerMask;

        static long ToLong(object value)
        {
            switch (value)
            {
                case null: return 0;
                case bool b: return b ? 1 : 0;
                case char c: return c;
                case ulong u: return unchecked((long)u);
                case LayerMask mask: return mask.value;
                case Enum e:
                    return Enum.GetUnderlyingType(e.GetType()) == typeof(ulong)
                        ? unchecked((long)Convert.ToUInt64(e, CultureInfo.InvariantCulture))
                        : Convert.ToInt64(e, CultureInfo.InvariantCulture);
                default: return Convert.ToInt64(value, CultureInfo.InvariantCulture);
            }
        }

        static object FromLong(long value, Type type)
        {
            unchecked
            {
                if (type.IsEnum) return Enum.ToObject(type, value);
                if (type == typeof(LayerMask)) return (LayerMask)(int)value;
                switch (Type.GetTypeCode(type))
                {
                    case TypeCode.SByte: return (sbyte)value;
                    case TypeCode.Byte: return (byte)value;
                    case TypeCode.Int16: return (short)value;
                    case TypeCode.UInt16: return (ushort)value;
                    case TypeCode.Int32: return (int)value;
                    case TypeCode.UInt32: return (uint)value;
                    case TypeCode.Int64: return value;
                    case TypeCode.UInt64: return (ulong)value;
                    case TypeCode.Char: return (char)value;
                    case TypeCode.Boolean: return value != 0;
                    case TypeCode.Single: return (float)value;
                    case TypeCode.Double: return (double)value;
                    default: return value;
                }
            }
        }

        public int intValue
        {
            get
            {
                var n = N;
                if (IsIntegral(n)) return unchecked((int)ToLong(SerializedTree.GetValue(n)));
                Unsupported("int");
                return 0;
            }
            set
            {
                var n = N;
                if (IsIntegral(n)) Write(n, FromLong(value, n.ClrType));
                else Unsupported("int");
            }
        }

        public long longValue
        {
            get
            {
                var n = N;
                if (IsIntegral(n)) return ToLong(SerializedTree.GetValue(n));
                Unsupported("long");
                return 0;
            }
            set
            {
                var n = N;
                if (IsIntegral(n)) Write(n, FromLong(value, n.ClrType));
                else Unsupported("long");
            }
        }

        public uint uintValue
        {
            get
            {
                var n = N;
                if (IsIntegral(n)) return unchecked((uint)ToLong(SerializedTree.GetValue(n)));
                Unsupported("uint");
                return 0;
            }
            set
            {
                var n = N;
                if (IsIntegral(n)) Write(n, FromLong(value, n.ClrType));
                else Unsupported("uint");
            }
        }

        public ulong ulongValue
        {
            get
            {
                var n = N;
                if (IsIntegral(n))
                {
                    var v = SerializedTree.GetValue(n);
                    return v is ulong u ? u : unchecked((ulong)ToLong(v));
                }
                Unsupported("ulong");
                return 0;
            }
            set
            {
                var n = N;
                if (IsIntegral(n)) Write(n, n.ClrType == typeof(ulong) ? value : FromLong(unchecked((long)value), n.ClrType));
                else Unsupported("ulong");
            }
        }

        public bool boolValue
        {
            get
            {
                var n = N;
                if (n.PropertyType == SerializedPropertyType.Boolean) return (bool)SerializedTree.GetValue(n);
                Unsupported("bool");
                return false;
            }
            set
            {
                var n = N;
                if (n.PropertyType == SerializedPropertyType.Boolean) Write(n, value);
                else Unsupported("bool");
            }
        }

        public float floatValue
        {
            get
            {
                var n = N;
                if (n.PropertyType == SerializedPropertyType.Float) return Convert.ToSingle(SerializedTree.GetValue(n), CultureInfo.InvariantCulture);
                Unsupported("float");
                return 0f;
            }
            set
            {
                var n = N;
                if (n.PropertyType == SerializedPropertyType.Float) Write(n, n.ClrType == typeof(double) ? (object)(double)value : value);
                else Unsupported("float");
            }
        }

        public double doubleValue
        {
            get
            {
                var n = N;
                if (n.PropertyType == SerializedPropertyType.Float) return Convert.ToDouble(SerializedTree.GetValue(n), CultureInfo.InvariantCulture);
                Unsupported("double");
                return 0d;
            }
            set
            {
                var n = N;
                if (n.PropertyType == SerializedPropertyType.Float) Write(n, n.ClrType == typeof(float) ? (object)(float)value : value);
                else Unsupported("double");
            }
        }

        public string stringValue
        {
            get
            {
                var n = N;
                if (n.PropertyType == SerializedPropertyType.String) return (string)SerializedTree.GetValue(n) ?? "";
                if (n.PropertyType == SerializedPropertyType.Character) return ((char)SerializedTree.GetValue(n)).ToString();
                Unsupported("string");
                return null;
            }
            set
            {
                var n = N;
                if (n.PropertyType == SerializedPropertyType.String) Write(n, value ?? "");
                else if (n.PropertyType == SerializedPropertyType.Character) Write(n, string.IsNullOrEmpty(value) ? '\0' : value[0]);
                else Unsupported("string");
            }
        }

        T GetTyped<T>(SerializedPropertyType expected, string kind)
        {
            var n = N;
            if (n.PropertyType == expected) return (T)SerializedTree.GetValue(n);
            Unsupported(kind);
            return default;
        }

        void SetTyped<T>(SerializedPropertyType expected, string kind, T value)
        {
            var n = N;
            if (n.PropertyType == expected) Write(n, value);
            else Unsupported(kind);
        }

        public Color colorValue
        {
            get
            {
                var n = N;
                if (n.PropertyType == SerializedPropertyType.Color)
                {
                    var v = SerializedTree.GetValue(n);
                    return v is Color32 c32 ? (Color)c32 : (Color)v;
                }
                Unsupported("color");
                return default;
            }
            set
            {
                var n = N;
                if (n.PropertyType == SerializedPropertyType.Color) Write(n, n.ClrType == typeof(Color32) ? (object)(Color32)value : value);
                else Unsupported("color");
            }
        }

        public Vector2 vector2Value
        {
            get => GetTyped<Vector2>(SerializedPropertyType.Vector2, "vector2");
            set => SetTyped(SerializedPropertyType.Vector2, "vector2", value);
        }

        public Vector3 vector3Value
        {
            get => GetTyped<Vector3>(SerializedPropertyType.Vector3, "vector3");
            set => SetTyped(SerializedPropertyType.Vector3, "vector3", value);
        }

        public Vector4 vector4Value
        {
            get => GetTyped<Vector4>(SerializedPropertyType.Vector4, "vector4");
            set => SetTyped(SerializedPropertyType.Vector4, "vector4", value);
        }

        public Vector2Int vector2IntValue
        {
            get => GetTyped<Vector2Int>(SerializedPropertyType.Vector2Int, "vector2int");
            set => SetTyped(SerializedPropertyType.Vector2Int, "vector2int", value);
        }

        public Vector3Int vector3IntValue
        {
            get => GetTyped<Vector3Int>(SerializedPropertyType.Vector3Int, "vector3int");
            set => SetTyped(SerializedPropertyType.Vector3Int, "vector3int", value);
        }

        public Quaternion quaternionValue
        {
            get => GetTyped<Quaternion>(SerializedPropertyType.Quaternion, "quaternion");
            set => SetTyped(SerializedPropertyType.Quaternion, "quaternion", value);
        }

        public Rect rectValue
        {
            get => GetTyped<Rect>(SerializedPropertyType.Rect, "rect");
            set => SetTyped(SerializedPropertyType.Rect, "rect", value);
        }

        public RectInt rectIntValue
        {
            get => GetTyped<RectInt>(SerializedPropertyType.RectInt, "rectint");
            set => SetTyped(SerializedPropertyType.RectInt, "rectint", value);
        }

        public Bounds boundsValue
        {
            get => GetTyped<Bounds>(SerializedPropertyType.Bounds, "bounds");
            set => SetTyped(SerializedPropertyType.Bounds, "bounds", value);
        }

        public BoundsInt boundsIntValue
        {
            get => GetTyped<BoundsInt>(SerializedPropertyType.BoundsInt, "boundsint");
            set => SetTyped(SerializedPropertyType.BoundsInt, "boundsint", value);
        }

        public Hash128 hash128Value
        {
            get => GetTyped<Hash128>(SerializedPropertyType.Hash128, "hash128");
            set => SetTyped(SerializedPropertyType.Hash128, "hash128", value);
        }

        public AnimationCurve animationCurveValue
        {
            get => (AnimationCurve)SerializedTree.CopyLeaf(GetTyped<AnimationCurve>(SerializedPropertyType.AnimationCurve, "animation curve"), Shape.AnimationCurve);
            set => SetTyped(SerializedPropertyType.AnimationCurve, "animation curve", value ?? new AnimationCurve());
        }

        public Gradient gradientValue
        {
            get => (Gradient)SerializedTree.CopyLeaf(GetTyped<Gradient>(SerializedPropertyType.Gradient, "gradient"), Shape.Gradient);
            set => SetTyped(SerializedPropertyType.Gradient, "gradient", value ?? new Gradient());
        }

        public Object objectReferenceValue
        {
            get
            {
                var n = N;
                if (n.PropertyType == SerializedPropertyType.ObjectReference) return (Object)SerializedTree.GetValue(n);
                Unsupported("pptr");
                return null;
            }
            set
            {
                var n = N;
                if (n.PropertyType != SerializedPropertyType.ObjectReference)
                {
                    Unsupported("pptr");
                    return;
                }
                // A reference of the wrong type is not assigned, as in the Editor.
                if (!(value is null) && !n.ClrType.IsInstanceOfType(value)) return;
                Write(n, value);
            }
        }

        public int objectReferenceInstanceIDValue
        {
            get
            {
                var obj = objectReferenceValue;
                return obj is null ? 0 : obj.GetInstanceID();
            }
            set => objectReferenceValue = value == 0 ? null : ObjectRegistry.FindById(value);
        }

        // ------------------------------------------------------------ enums

        static string[] EnumNames(Type type)
        {
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Static);
            var names = new string[fields.Length];
            for (int i = 0; i < fields.Length; i++) names[i] = fields[i].Name;
            return names;
        }

        static long[] EnumValues(Type type)
        {
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Static);
            var values = new long[fields.Length];
            for (int i = 0; i < fields.Length; i++) values[i] = ToLong(fields[i].GetValue(null));
            return values;
        }

        public int enumValueIndex
        {
            get
            {
                var n = N;
                if (n.PropertyType != SerializedPropertyType.Enum)
                {
                    Unsupported("enum");
                    return 0;
                }
                long current = ToLong(SerializedTree.GetValue(n));
                var values = EnumValues(n.ClrType);
                return Array.IndexOf(values, current);
            }
            set
            {
                var n = N;
                if (n.PropertyType != SerializedPropertyType.Enum)
                {
                    Unsupported("enum");
                    return;
                }
                var values = EnumValues(n.ClrType);
                if (value < 0 || value >= values.Length) return;
                Write(n, FromLong(values[value], n.ClrType));
            }
        }

        public int enumValueFlag
        {
            get
            {
                var n = N;
                if (n.PropertyType == SerializedPropertyType.Enum) return unchecked((int)ToLong(SerializedTree.GetValue(n)));
                Unsupported("enum");
                return 0;
            }
            set
            {
                var n = N;
                if (n.PropertyType == SerializedPropertyType.Enum) Write(n, FromLong(value, n.ClrType));
                else Unsupported("enum");
            }
        }

        public string[] enumNames
        {
            get
            {
                var n = N;
                return n.PropertyType == SerializedPropertyType.Enum ? EnumNames(n.ClrType) : Array.Empty<string>();
            }
        }

        public string[] enumDisplayNames
        {
            get
            {
                var n = N;
                if (n.PropertyType != SerializedPropertyType.Enum) return Array.Empty<string>();
                var fields = n.ClrType.GetFields(BindingFlags.Public | BindingFlags.Static);
                var names = new string[fields.Length];
                for (int i = 0; i < fields.Length; i++)
                    names[i] = fields[i].GetCustomAttribute<InspectorNameAttribute>(false)?.displayName ?? ObjectNames.NicifyVariableName(fields[i].Name);
                return names;
            }
        }

        // ------------------------------------------------------------ managed references and boxed values

        public object managedReferenceValue
        {
            get
            {
                var n = N;
                if (n.Kind == SerializedNodeKind.ManagedReference) return n.Value;
                Debug.LogError("managedReferenceValue is only available on fields with the [SerializeReference] attribute");
                return null;
            }
            set
            {
                var n = N;
                if (n.Kind != SerializedNodeKind.ManagedReference)
                {
                    Debug.LogError("managedReferenceValue is only available on fields with the [SerializeReference] attribute");
                    return;
                }
                if (value != null && (value is Object || !n.ClrType.IsInstanceOfType(value)))
                {
                    Debug.LogError($"Cannot assign an object of type '{value.GetType()}' to the managed reference field '{n.Path}' of type '{n.ClrType}'.");
                    return;
                }
                if (ReferenceEquals(n.Value, value)) return;
                SerializedTree.Assign(n, value, callbacks: true);
                Changed(structural: true);
            }
        }

        public long managedReferenceId
        {
            get
            {
                var n = N;
                return n.Kind == SerializedNodeKind.ManagedReference ? SerializedTree.ReferenceId(n.Value) : SerializedTree.NullReferenceId;
            }
            set
            {
                if (value == SerializedTree.NullReferenceId)
                {
                    managedReferenceValue = null;
                    return;
                }
                var target = SerializedTree.FindReference(m_SerializedObject.m_UnboundRoot, value);
                if (target == null)
                {
                    Debug.LogError($"No managed reference with id {value} in the serialized object.");
                    return;
                }
                managedReferenceValue = target;
            }
        }

        public string managedReferenceFullTypename
        {
            get
            {
                var n = N;
                return n.Kind == SerializedNodeKind.ManagedReference ? SerializedTree.ManagedTypeName(n.Value?.GetType()) : "";
            }
        }

        public string managedReferenceFieldTypename
        {
            get
            {
                var n = N;
                return n.Kind == SerializedNodeKind.ManagedReference ? SerializedTree.ManagedTypeName(n.ClrType) : "";
            }
        }

        /// <summary>The property's value as an object: primitives, structs, references and copies of
        /// serializable classes and collections.</summary>
        public object boxedValue
        {
            get
            {
                var n = N;
                switch (n.Kind)
                {
                    case SerializedNodeKind.Root:
                    case SerializedNodeKind.ArrayContainer:
                        throw new InvalidOperationException($"boxedValue is not supported on property '{n.Path}' of type '{n.TypeName}'.");
                    case SerializedNodeKind.ManagedReference:
                        return n.Value;
                    case SerializedNodeKind.Composite:
                    case SerializedNodeKind.Array:
                        return SerializedTree.Build(n, null, primary: false);
                    default:
                        return SerializedTree.CopyLeaf(SerializedTree.GetValue(n), n.Shape);
                }
            }
            set
            {
                var n = N;
                switch (n.Kind)
                {
                    case SerializedNodeKind.ManagedReference:
                        managedReferenceValue = value;
                        return;
                    case SerializedNodeKind.Composite:
                    case SerializedNodeKind.Array:
                        if (value == null || !n.ClrType.IsInstanceOfType(value))
                            throw new ArgumentException($"Cannot assign a value of type '{value?.GetType()}' to property '{n.Path}' of type '{n.ClrType}'.");
                        SerializedTree.Assign(n, value, callbacks: false);
                        Changed(structural: true);
                        return;
                    case SerializedNodeKind.Leaf:
                    case SerializedNodeKind.Component:
                    case SerializedNodeKind.ArraySize:
                        Write(n, ConvertBoxed(value, n));
                        return;
                    default:
                        throw new InvalidOperationException($"boxedValue is not supported on property '{n.Path}' of type '{n.TypeName}'.");
                }
            }
        }

        static object ConvertBoxed(object value, SerializedNode n)
        {
            var type = n.ClrType;
            if (value == null)
            {
                if (type.IsValueType) throw new ArgumentException($"Cannot assign null to property '{n.Path}' of type '{type}'.");
                return n.Shape == Shape.String ? "" : null;
            }
            if (type.IsInstanceOfType(value)) return value;
            if (type == typeof(float) || type == typeof(double)) return Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
            if (type.IsEnum || type.IsPrimitive) return FromLong(ToLong(value), type);
            if (value is Color c && type == typeof(Color32)) return (Color32)c;
            throw new ArgumentException($"Cannot assign a value of type '{value.GetType()}' to property '{n.Path}' of type '{type}'.");
        }
    }
}
