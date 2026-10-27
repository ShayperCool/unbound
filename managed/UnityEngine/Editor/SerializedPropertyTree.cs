using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unbound.Runtime
{
    internal enum SerializedNodeKind
    {
        /// <summary>The iterator's start: the object itself ("Base", depth -1).</summary>
        Root,
        /// <summary>A value held whole: numbers, strings, references, math structs, curves.</summary>
        Leaf,
        /// <summary>A part of a leaf's value (`position.x`, `bounds.m_Center`), read and written
        /// through the leaf.</summary>
        Component,
        /// <summary>A [Serializable] class or struct, field by field.</summary>
        Composite,
        /// <summary>A [SerializeReference] field: the referenced object's fields.</summary>
        ManagedReference,
        /// <summary>An array or List field; its single child is the `Array` node.</summary>
        Array,
        /// <summary>`list.Array`: `size` then the elements `data[i]`.</summary>
        ArrayContainer,
        /// <summary>`list.Array.size`.</summary>
        ArraySize,
    }

    /// <summary>Reads and writes one serialized member on its container (an object, a boxed struct).</summary>
    internal interface ISerializedAccessor
    {
        object Get(object container);
        void Set(object container, object value);
    }

    internal sealed class FieldAccessor : ISerializedAccessor
    {
        readonly FieldInfo m_Field;
        internal FieldAccessor(FieldInfo field) { m_Field = field; }
        internal FieldInfo Field => m_Field;
        public object Get(object container) => m_Field.GetValue(container);
        public void Set(object container, object value) => m_Field.SetValue(container, value);
    }

    internal sealed class DelegateAccessor : ISerializedAccessor
    {
        readonly Func<Object, object> m_Get;
        readonly Action<Object, object> m_Set;
        internal DelegateAccessor(Func<Object, object> get, Action<Object, object> set) { m_Get = get; m_Set = set; }
        public object Get(object container) => m_Get((Object)container);
        public void Set(object container, object value) => m_Set?.Invoke((Object)container, value);
    }

    /// <summary>A part of a math struct (`x` of a Vector3): reads it from the whole value and
    /// returns the whole value with the part replaced.</summary>
    internal sealed class ComponentDef
    {
        internal string Name;
        internal Type Type;
        internal Shape Shape;
        internal Func<object, object> Get;
        internal Func<object, object, object> With;

        static ComponentDef C<TWhole, TPart>(string name, Shape shape, Func<TWhole, TPart> get, Func<TWhole, TPart, TWhole> with) =>
            new ComponentDef
            {
                Name = name, Type = typeof(TPart), Shape = shape,
                Get = w => get((TWhole)w),
                With = (w, p) => with((TWhole)w, (TPart)p),
            };

        static readonly Dictionary<Shape, ComponentDef[]> s_Defs = Build();

        internal static ComponentDef[] For(Shape shape) => s_Defs.TryGetValue(shape, out var defs) ? defs : System.Array.Empty<ComponentDef>();

        static Dictionary<Shape, ComponentDef[]> Build()
        {
            var matrix = new List<ComponentDef>();
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 4; c++)
                {
                    int row = r, column = c;
                    matrix.Add(C<Matrix4x4, float>($"e{r}{c}", Shape.Float, m => m[row, column], (m, v) => { m[row, column] = v; return m; }));
                }
            return new Dictionary<Shape, ComponentDef[]>
            {
                [Shape.Vector2] = new[]
                {
                    C<Vector2, float>("x", Shape.Float, v => v.x, (v, p) => { v.x = p; return v; }),
                    C<Vector2, float>("y", Shape.Float, v => v.y, (v, p) => { v.y = p; return v; }),
                },
                [Shape.Vector3] = new[]
                {
                    C<Vector3, float>("x", Shape.Float, v => v.x, (v, p) => { v.x = p; return v; }),
                    C<Vector3, float>("y", Shape.Float, v => v.y, (v, p) => { v.y = p; return v; }),
                    C<Vector3, float>("z", Shape.Float, v => v.z, (v, p) => { v.z = p; return v; }),
                },
                [Shape.Vector4] = new[]
                {
                    C<Vector4, float>("x", Shape.Float, v => v.x, (v, p) => { v.x = p; return v; }),
                    C<Vector4, float>("y", Shape.Float, v => v.y, (v, p) => { v.y = p; return v; }),
                    C<Vector4, float>("z", Shape.Float, v => v.z, (v, p) => { v.z = p; return v; }),
                    C<Vector4, float>("w", Shape.Float, v => v.w, (v, p) => { v.w = p; return v; }),
                },
                [Shape.Quaternion] = new[]
                {
                    C<Quaternion, float>("x", Shape.Float, v => v.x, (v, p) => { v.x = p; return v; }),
                    C<Quaternion, float>("y", Shape.Float, v => v.y, (v, p) => { v.y = p; return v; }),
                    C<Quaternion, float>("z", Shape.Float, v => v.z, (v, p) => { v.z = p; return v; }),
                    C<Quaternion, float>("w", Shape.Float, v => v.w, (v, p) => { v.w = p; return v; }),
                },
                [Shape.Color] = new[]
                {
                    C<Color, float>("r", Shape.Float, v => v.r, (v, p) => { v.r = p; return v; }),
                    C<Color, float>("g", Shape.Float, v => v.g, (v, p) => { v.g = p; return v; }),
                    C<Color, float>("b", Shape.Float, v => v.b, (v, p) => { v.b = p; return v; }),
                    C<Color, float>("a", Shape.Float, v => v.a, (v, p) => { v.a = p; return v; }),
                },
                [Shape.Color32] = new[]
                {
                    C<Color32, uint>("rgba", Shape.UInt, v => (uint)(v.r | (v.g << 8) | (v.b << 16) | (v.a << 24)),
                        (v, p) => new Color32((byte)(p & 0xff), (byte)((p >> 8) & 0xff), (byte)((p >> 16) & 0xff), (byte)(p >> 24))),
                },
                [Shape.Rect] = new[]
                {
                    C<Rect, float>("x", Shape.Float, v => v.x, (v, p) => { v.x = p; return v; }),
                    C<Rect, float>("y", Shape.Float, v => v.y, (v, p) => { v.y = p; return v; }),
                    C<Rect, float>("width", Shape.Float, v => v.width, (v, p) => { v.width = p; return v; }),
                    C<Rect, float>("height", Shape.Float, v => v.height, (v, p) => { v.height = p; return v; }),
                },
                [Shape.Vector2Int] = new[]
                {
                    C<Vector2Int, int>("x", Shape.Int, v => v.x, (v, p) => { v.x = p; return v; }),
                    C<Vector2Int, int>("y", Shape.Int, v => v.y, (v, p) => { v.y = p; return v; }),
                },
                [Shape.Vector3Int] = new[]
                {
                    C<Vector3Int, int>("x", Shape.Int, v => v.x, (v, p) => { v.x = p; return v; }),
                    C<Vector3Int, int>("y", Shape.Int, v => v.y, (v, p) => { v.y = p; return v; }),
                    C<Vector3Int, int>("z", Shape.Int, v => v.z, (v, p) => { v.z = p; return v; }),
                },
                [Shape.RectInt] = new[]
                {
                    C<RectInt, int>("x", Shape.Int, v => v.x, (v, p) => { v.x = p; return v; }),
                    C<RectInt, int>("y", Shape.Int, v => v.y, (v, p) => { v.y = p; return v; }),
                    C<RectInt, int>("width", Shape.Int, v => v.width, (v, p) => { v.width = p; return v; }),
                    C<RectInt, int>("height", Shape.Int, v => v.height, (v, p) => { v.height = p; return v; }),
                },
                [Shape.Bounds] = new[]
                {
                    C<Bounds, Vector3>("m_Center", Shape.Vector3, v => v.center, (v, p) => { v.center = p; return v; }),
                    C<Bounds, Vector3>("m_Extent", Shape.Vector3, v => v.extents, (v, p) => { v.extents = p; return v; }),
                },
                [Shape.BoundsInt] = new[]
                {
                    C<BoundsInt, Vector3Int>("m_Position", Shape.Vector3Int, v => v.position, (v, p) => { v.position = p; return v; }),
                    C<BoundsInt, Vector3Int>("m_Size", Shape.Vector3Int, v => v.size, (v, p) => { v.size = p; return v; }),
                },
                [Shape.LayerMask] = new[]
                {
                    C<LayerMask, uint>("m_Bits", Shape.UInt, v => (uint)v.value, (v, p) => (LayerMask)(int)p),
                },
                [Shape.RectOffset] = new[]
                {
                    C<RectOffset, int>("m_Left", Shape.Int, v => v.left, (v, p) => new RectOffset(p, v.right, v.top, v.bottom)),
                    C<RectOffset, int>("m_Right", Shape.Int, v => v.right, (v, p) => new RectOffset(v.left, p, v.top, v.bottom)),
                    C<RectOffset, int>("m_Top", Shape.Int, v => v.top, (v, p) => new RectOffset(v.left, v.right, p, v.bottom)),
                    C<RectOffset, int>("m_Bottom", Shape.Int, v => v.bottom, (v, p) => new RectOffset(v.left, v.right, v.top, p)),
                },
                [Shape.Matrix4x4] = matrix.ToArray(),
            };
        }
    }

    /// <summary>One property of a SerializedObject's buffered copy of an object's serialized data.</summary>
    internal sealed class SerializedNode
    {
        internal SerializedNodeKind Kind;
        internal SerializedNode Parent;
        internal List<SerializedNode> Children = new List<SerializedNode>();
        internal int SiblingIndex;
        internal string Name;
        internal string Path;
        internal string TypeName;
        internal string Tooltip;
        internal int Depth;
        /// <summary>The index of an array element; -1 otherwise.</summary>
        internal int Index = -1;
        internal Type ClrType;
        internal Shape Shape;
        internal Type ElementType;
        internal Shape ElementShape;
        internal SerializedPropertyType PropertyType;
        internal bool Hidden;
        internal bool ReadOnly;
        /// <summary>Changed since the last Update/Apply (this node or a descendant).</summary>
        internal bool Dirty;
        /// <summary>Replaced as a whole (array resized, value assigned): written back entirely.</summary>
        internal bool Replaced;
        /// <summary>Leaf: the buffered value. Composite class: the instance it was read from.
        /// ManagedReference: the referenced object.</summary>
        internal object Value;
        internal ISerializedAccessor Accessor;
        internal ComponentDef Component;
        /// <summary>A settings object's node: built from its class's type tree and its YAML
        /// (<see cref="TypeTreeSerialized"/>).</summary>
        internal bool TypeTree;
        internal TypeTreeNode Schema;
        /// <summary>A type tree node's YAML as read.</summary>
        internal object Extra;

        internal bool IsElement => Index >= 0;

        internal SerializedNode ArrayContainer => Kind == SerializedNodeKind.Array ? Children[0] : null;

        internal int ArrayCount => Kind == SerializedNodeKind.Array ? Children[0].Children.Count - 1 : 0;

        internal SerializedNode Element(int index) => Children[0].Children[index + 1];

        internal SerializedNode ShallowCopy() => (SerializedNode)MemberwiseClone();
    }

    /// <summary>
    /// Builds a SerializedObject's property tree from an object with Unity's serialization rules,
    /// navigates it as Unity's property iterator does, and writes buffered changes back.
    /// </summary>
    internal static class SerializedTree
    {
        const int MaxNesting = 10;

        sealed class BuildContext
        {
            internal bool Callbacks;
            /// <summary>New array elements: every value is its type's default (no constructors).</summary>
            internal bool Zero;
            internal readonly List<object> ReferencePath = new List<object>();
        }

        internal sealed class NativeField
        {
            internal string Name;
            internal Type Type;
            internal Shape Shape;
            internal string TypeName;
            internal bool Hidden;
            internal bool ReadOnly;
            internal Func<Object, object> Get;
            internal Action<Object, object> Set;
        }

        // ---------------------------------------------------------------- building

        /// <summary>The script field a node was read from, if any.</summary>
        internal static FieldInfo FieldOf(SerializedNode node) => (node?.Accessor as FieldAccessor)?.Field;

        internal static bool IsScript(Object target) => target is MonoBehaviour || target is ScriptableObject;

        internal static SerializedNode BuildRoot(Object target, bool callbacks)
        {
            var root = new SerializedNode
            {
                Kind = SerializedNodeKind.Root,
                Name = "Base",
                Path = "",
                Depth = -1,
                ClrType = target.GetType(),
                TypeName = IsScript(target) ? "MonoBehaviour" : target.GetType().Name,
                PropertyType = SerializedPropertyType.Generic,
            };
            var ctx = new BuildContext { Callbacks = callbacks };
            if (callbacks && target is ISerializationCallbackReceiver receiver)
            {
                try { receiver.OnBeforeSerialize(); }
                catch (Exception e) { Debug.LogException(e); }
            }
            foreach (var native in NativeFields(target))
            {
                object value;
                try { value = native.Get(target); }
                catch (Exception) { value = null; }
                var node = Make(root, native.Name, -1, native.Type, native.Shape, null, Shape.Unsupported, value, ctx, 0);
                node.TypeName = native.TypeName;
                node.Hidden = native.Hidden;
                node.ReadOnly = native.ReadOnly;
                node.Accessor = new DelegateAccessor(native.Get, native.Set);
                AddChild(root, node);
            }
            if (IsScript(target))
            {
                foreach (var member in SerializationRules.Members(target.GetType()))
                {
                    object value;
                    try { value = member.Field.GetValue(target); }
                    catch (Exception) { value = null; }
                    AddChild(root, MakeMember(root, member, value, ctx, 0));
                }
            }
            return root;
        }

        static SerializedNode MakeMember(SerializedNode parent, SerializedMember member, object value, BuildContext ctx, int nesting)
        {
            var node = Make(parent, member.Name, -1, member.Field.FieldType, member.Shape, member.ElementType, member.ElementShape, value, ctx, nesting);
            node.Accessor = new FieldAccessor(member.Field);
            node.Hidden = member.Field.IsDefined(typeof(HideInInspector), false);
            node.Tooltip = member.Field.GetCustomAttribute<TooltipAttribute>(false)?.tooltip;
            return node;
        }

        static SerializedNode Make(SerializedNode parent, string name, int index, Type type, Shape shape, Type elementType, Shape elementShape,
                                   object value, BuildContext ctx, int nesting)
        {
            var node = new SerializedNode
            {
                Parent = parent,
                Name = name,
                Index = index,
                Depth = parent.Depth + 1,
                ClrType = type,
                Shape = shape,
                ElementType = elementType,
                ElementShape = elementShape,
            };
            node.Path = ChildPath(parent, node);
            switch (shape)
            {
                case Shape.Composite:
                {
                    node.Kind = SerializedNodeKind.Composite;
                    node.PropertyType = SerializedPropertyType.Generic;
                    node.TypeName = type.Name;
                    object source = value;
                    if (source == null)
                        source = type.IsValueType ? Activator.CreateInstance(type)
                            : ctx.Zero ? RuntimeHelpers.GetUninitializedObject(type) : SerializationRules.Create(type);
                    else if (ctx.Callbacks && source is ISerializationCallbackReceiver receiver)
                    {
                        try { receiver.OnBeforeSerialize(); }
                        catch (Exception e) { Debug.LogException(e); }
                    }
                    node.Value = type.IsValueType ? null : value;
                    if (nesting < MaxNesting)
                        foreach (var member in SerializationRules.Members(type))
                            AddChild(node, MakeMember(node, member, member.Field.GetValue(source), ctx, nesting + 1));
                    break;
                }
                case Shape.Array:
                case Shape.List:
                {
                    node.Kind = SerializedNodeKind.Array;
                    node.PropertyType = SerializedPropertyType.Generic;
                    node.TypeName = "vector";
                    var container = new SerializedNode
                    {
                        Kind = SerializedNodeKind.ArrayContainer, Parent = node, Name = "Array", Depth = node.Depth + 1,
                        ClrType = type, Shape = shape, TypeName = "Array", PropertyType = SerializedPropertyType.Generic, Hidden = true,
                    };
                    container.Path = ChildPath(node, container);
                    AddChild(node, container);
                    var size = new SerializedNode
                    {
                        Kind = SerializedNodeKind.ArraySize, Parent = container, Name = "size", Depth = container.Depth + 1,
                        ClrType = typeof(int), Shape = Shape.Int, TypeName = "int", PropertyType = SerializedPropertyType.ArraySize,
                    };
                    size.Path = ChildPath(container, size);
                    AddChild(container, size);
                    if (value is IList items && nesting < MaxNesting)
                        for (int i = 0; i < items.Count; i++)
                            AddChild(container, Make(container, "data", i, elementType, elementShape, null, Shape.Unsupported, items[i], ctx, nesting + 1));
                    break;
                }
                case Shape.ManagedReference:
                {
                    node.Kind = SerializedNodeKind.ManagedReference;
                    node.PropertyType = SerializedPropertyType.ManagedReference;
                    node.Value = value;
                    node.TypeName = "managedReference<" + (value?.GetType() ?? type).Name + ">";
                    if (value != null && !ContainsReference(ctx.ReferencePath, value) && nesting < MaxNesting)
                    {
                        ctx.ReferencePath.Add(value);
                        if (ctx.Callbacks && value is ISerializationCallbackReceiver receiver)
                        {
                            try { receiver.OnBeforeSerialize(); }
                            catch (Exception e) { Debug.LogException(e); }
                        }
                        foreach (var member in SerializationRules.Members(value.GetType()))
                            AddChild(node, MakeMember(node, member, member.Field.GetValue(value), ctx, nesting + 1));
                        ctx.ReferencePath.RemoveAt(ctx.ReferencePath.Count - 1);
                    }
                    break;
                }
                default:
                    node.Kind = SerializedNodeKind.Leaf;
                    node.Value = NormalizeLeaf(value, type, shape);
                    node.PropertyType = PropertyTypeOf(shape);
                    node.TypeName = TypeNameOf(type, shape);
                    AddComponents(node);
                    break;
            }
            return node;
        }

        static bool ContainsReference(List<object> path, object value)
        {
            foreach (var o in path)
                if (ReferenceEquals(o, value)) return true;
            return false;
        }

        internal static void AddComponents(SerializedNode node)
        {
            foreach (var def in ComponentDef.For(node.Shape))
            {
                var child = new SerializedNode
                {
                    Kind = SerializedNodeKind.Component,
                    Parent = node,
                    Name = def.Name,
                    Depth = node.Depth + 1,
                    ClrType = def.Type,
                    Shape = def.Shape,
                    Component = def,
                    PropertyType = PropertyTypeOf(def.Shape),
                    TypeName = TypeNameOf(def.Type, def.Shape),
                    ReadOnly = node.ReadOnly,
                };
                child.Path = ChildPath(node, child);
                AddChild(node, child);
                AddComponents(child);
            }
        }

        static void AddChild(SerializedNode parent, SerializedNode child)
        {
            child.Parent = parent;
            child.SiblingIndex = parent.Children.Count;
            parent.Children.Add(child);
        }

        static string ChildPath(SerializedNode parent, SerializedNode child)
        {
            if (child.IsElement) return parent.Path + ".data[" + child.Index.ToString(CultureInfo.InvariantCulture) + "]";
            return parent.Kind == SerializedNodeKind.Root ? child.Name : parent.Path + "." + child.Name;
        }

        static object NormalizeLeaf(object value, Type type, Shape shape)
        {
            switch (shape)
            {
                case Shape.String: return value as string ?? "";
                case Shape.UnityObject: return value;
                case Shape.AnimationCurve: return value == null ? new AnimationCurve() : CopyLeaf(value, shape);
                case Shape.Gradient: return value == null ? new Gradient() : CopyLeaf(value, shape);
                case Shape.RectOffset: return value == null ? new RectOffset() : CopyLeaf(value, shape);
                case Shape.Enum: return value ?? Enum.ToObject(type, 0);
            }
            if (value == null && type.IsValueType) return Activator.CreateInstance(type);
            return value;
        }

        /// <summary>A copy of a leaf value, so the buffer and the object never share a mutable one.</summary>
        internal static object CopyLeaf(object value, Shape shape)
        {
            switch (value)
            {
                case AnimationCurve curve:
                    return new AnimationCurve(curve.keys) { preWrapMode = curve.preWrapMode, postWrapMode = curve.postWrapMode };
                case Gradient gradient:
                {
                    var copy = new Gradient { mode = gradient.mode, colorSpace = gradient.colorSpace };
                    copy.SetKeys(gradient.colorKeys, gradient.alphaKeys);
                    return copy;
                }
                case RectOffset offset:
                    return new RectOffset(offset.left, offset.right, offset.top, offset.bottom);
                default:
                    return value;
            }
        }

        internal static IEnumerable<NativeField> NativeFields(Object target)
        {
            yield return new NativeField
            {
                Name = "m_ObjectHideFlags", Type = typeof(uint), Shape = Shape.UInt, TypeName = "unsigned int", Hidden = true,
                Get = o => (uint)o.hideFlags, Set = (o, v) => o.hideFlags = (HideFlags)(uint)v,
            };
            yield return NullPointer("m_CorrespondingSourceObject", "PPtr<EditorExtension>");
            yield return NullPointer("m_PrefabInstance", "PPtr<PrefabInstance>");
            yield return NullPointer("m_PrefabAsset", "PPtr<Prefab>");
            bool script = IsScript(target);
            if (target is Component)
                yield return new NativeField
                {
                    Name = "m_GameObject", Type = typeof(GameObject), Shape = Shape.UnityObject, TypeName = "PPtr<GameObject>",
                    Hidden = true, ReadOnly = true, Get = o => ((Component)o).gameObject,
                };
            else if (script)
                yield return NullPointer("m_GameObject", "PPtr<GameObject>");
            if (target is Behaviour)
                yield return new NativeField
                {
                    Name = "m_Enabled", Type = typeof(bool), Shape = Shape.Bool, TypeName = "UInt8", Hidden = script,
                    Get = o => ((Behaviour)o).enabled, Set = (o, v) => ((Behaviour)o).enabled = (bool)v,
                };
            else if (script)
                yield return new NativeField
                {
                    Name = "m_Enabled", Type = typeof(bool), Shape = Shape.Bool, TypeName = "UInt8", Hidden = true, ReadOnly = true,
                    Get = o => true,
                };
            if (script)
            {
                yield return new NativeField
                {
                    Name = "m_EditorHideFlags", Type = typeof(uint), Shape = Shape.UInt, TypeName = "unsigned int", Hidden = true, ReadOnly = true,
                    Get = o => 0u,
                };
                yield return new NativeField
                {
                    Name = "m_Script", Type = typeof(MonoScript), Shape = Shape.UnityObject, TypeName = "PPtr<MonoScript>", ReadOnly = true,
                    Get = o => MonoScript.FromType(o.GetType()),
                };
                yield return NameField(hidden: true);
                yield return new NativeField
                {
                    Name = "m_EditorClassIdentifier", Type = typeof(string), Shape = Shape.String, TypeName = "string", Hidden = true, ReadOnly = true,
                    Get = o => "",
                };
            }
            else if (target is GameObject)
            {
                yield return new NativeField
                {
                    Name = "m_Layer", Type = typeof(uint), Shape = Shape.UInt, TypeName = "unsigned int",
                    Get = o => (uint)((GameObject)o).layer, Set = (o, v) => ((GameObject)o).layer = (int)(uint)v,
                };
                yield return NameField(hidden: false);
                yield return new NativeField
                {
                    Name = "m_TagString", Type = typeof(string), Shape = Shape.String, TypeName = "string",
                    Get = o => ((GameObject)o).tag, Set = (o, v) => ((GameObject)o).tag = (string)v,
                };
                yield return new NativeField
                {
                    Name = "m_IsActive", Type = typeof(bool), Shape = Shape.Bool, TypeName = "bool",
                    Get = o => ((GameObject)o).activeSelf, Set = (o, v) => ((GameObject)o).SetActive((bool)v),
                };
            }
            else if (target is Transform)
            {
                yield return new NativeField
                {
                    Name = "m_LocalRotation", Type = typeof(Quaternion), Shape = Shape.Quaternion, TypeName = "Quaternionf",
                    Get = o => ((Transform)o).localRotation, Set = (o, v) => ((Transform)o).localRotation = (Quaternion)v,
                };
                yield return new NativeField
                {
                    Name = "m_LocalPosition", Type = typeof(Vector3), Shape = Shape.Vector3, TypeName = "Vector3f",
                    Get = o => ((Transform)o).localPosition, Set = (o, v) => ((Transform)o).localPosition = (Vector3)v,
                };
                yield return new NativeField
                {
                    Name = "m_LocalScale", Type = typeof(Vector3), Shape = Shape.Vector3, TypeName = "Vector3f",
                    Get = o => ((Transform)o).localScale, Set = (o, v) => ((Transform)o).localScale = (Vector3)v,
                };
                yield return new NativeField
                {
                    Name = "m_Father", Type = typeof(Transform), Shape = Shape.UnityObject, TypeName = "PPtr<Transform>", Hidden = true, ReadOnly = true,
                    Get = o => ((Transform)o).parent,
                };
            }
            else if (target is PlayerSettings)
            {
                yield return NameField(hidden: false);
                // What editor code reads of ProjectSettings.asset (the Input System, the active
                // input handling).
                yield return new NativeField
                {
                    Name = "activeInputHandler", Type = typeof(int), Shape = Shape.Int, TypeName = "int",
                    Get = o => PlayerSettings.s_ActiveInputHandler, Set = (o, v) => PlayerSettings.s_ActiveInputHandler = (int)v,
                };
            }
            else if (!(target is Component))
            {
                yield return NameField(hidden: false);
            }
        }

        static NativeField NullPointer(string name, string typeName) => new NativeField
        {
            Name = name, Type = typeof(Object), Shape = Shape.UnityObject, TypeName = typeName, Hidden = true, ReadOnly = true, Get = o => null,
        };

        static NativeField NameField(bool hidden) => new NativeField
        {
            Name = "m_Name", Type = typeof(string), Shape = Shape.String, TypeName = "string", Hidden = hidden,
            Get = o => o.name, Set = (o, v) => o.name = (string)v,
        };

        // ---------------------------------------------------------------- type information

        internal static SerializedPropertyType PropertyTypeOf(Shape shape)
        {
            switch (shape)
            {
                case Shape.Bool: return SerializedPropertyType.Boolean;
                case Shape.SByte: case Shape.Byte: case Shape.Short: case Shape.UShort:
                case Shape.Int: case Shape.UInt: case Shape.Long: case Shape.ULong:
                    return SerializedPropertyType.Integer;
                case Shape.Float: case Shape.Double: return SerializedPropertyType.Float;
                case Shape.Char: return SerializedPropertyType.Character;
                case Shape.String: return SerializedPropertyType.String;
                case Shape.Enum: return SerializedPropertyType.Enum;
                case Shape.UnityObject: return SerializedPropertyType.ObjectReference;
                case Shape.Vector2: return SerializedPropertyType.Vector2;
                case Shape.Vector3: return SerializedPropertyType.Vector3;
                case Shape.Vector4: return SerializedPropertyType.Vector4;
                case Shape.Vector2Int: return SerializedPropertyType.Vector2Int;
                case Shape.Vector3Int: return SerializedPropertyType.Vector3Int;
                case Shape.Quaternion: return SerializedPropertyType.Quaternion;
                case Shape.Color: case Shape.Color32: return SerializedPropertyType.Color;
                case Shape.Rect: return SerializedPropertyType.Rect;
                case Shape.RectInt: return SerializedPropertyType.RectInt;
                case Shape.Bounds: return SerializedPropertyType.Bounds;
                case Shape.BoundsInt: return SerializedPropertyType.BoundsInt;
                case Shape.LayerMask: return SerializedPropertyType.LayerMask;
                case Shape.Hash128: return SerializedPropertyType.Hash128;
                case Shape.AnimationCurve: return SerializedPropertyType.AnimationCurve;
                case Shape.Gradient: return SerializedPropertyType.Gradient;
                case Shape.ManagedReference: return SerializedPropertyType.ManagedReference;
                default: return SerializedPropertyType.Generic;
            }
        }

        /// <summary>The type name Unity's serialization (the type tree) gives a value.</summary>
        internal static string TypeNameOf(Type type, Shape shape)
        {
            switch (shape)
            {
                case Shape.Bool: return "bool";
                case Shape.SByte: return "SInt8";
                case Shape.Byte: return "UInt8";
                case Shape.Short: return "SInt16";
                case Shape.UShort: return "UInt16";
                case Shape.Int: return "int";
                case Shape.UInt: return "unsigned int";
                case Shape.Long: return "SInt64";
                case Shape.ULong: return "UInt64";
                case Shape.Float: return "float";
                case Shape.Double: return "double";
                case Shape.Char: return "char";
                case Shape.String: return "string";
                case Shape.Enum: return "Enum";
                case Shape.UnityObject: return "PPtr<$" + type.Name + ">";
                case Shape.Vector2: return "Vector2f";
                case Shape.Vector3: return "Vector3f";
                case Shape.Vector4: return "Vector4f";
                case Shape.Vector2Int: return "Vector2Int";
                case Shape.Vector3Int: return "Vector3Int";
                case Shape.Quaternion: return "Quaternionf";
                case Shape.Color: case Shape.Color32: return "ColorRGBA";
                case Shape.Rect: return "Rectf";
                case Shape.RectInt: return "RectInt";
                case Shape.Bounds: return "AABB";
                case Shape.BoundsInt: return "BoundsInt";
                case Shape.Matrix4x4: return "Matrix4x4f";
                case Shape.LayerMask: return "BitField";
                case Shape.Hash128: return "Hash128";
                case Shape.AnimationCurve: return "AnimationCurve";
                case Shape.Gradient: return "Gradient";
                case Shape.RectOffset: return "RectOffset";
                case Shape.Array: case Shape.List: return "vector";
                case Shape.ManagedReference: return "managedReference<" + type.Name + ">";
                default: return type?.Name ?? "";
            }
        }

        internal static SerializedPropertyNumericType NumericTypeOf(SerializedNode node)
        {
            if (node.Kind == SerializedNodeKind.ArraySize) return SerializedPropertyNumericType.Int32;
            var shape = node.Shape;
            if (shape == Shape.Enum)
            {
                var underlying = Enum.GetUnderlyingType(node.ClrType);
                shape = SerializationRules.Classify(underlying, 0);
            }
            switch (shape)
            {
                case Shape.SByte: return SerializedPropertyNumericType.Int8;
                case Shape.Byte: return SerializedPropertyNumericType.UInt8;
                case Shape.Short: return SerializedPropertyNumericType.Int16;
                case Shape.UShort: case Shape.Char: return SerializedPropertyNumericType.UInt16;
                case Shape.Int: return SerializedPropertyNumericType.Int32;
                case Shape.UInt: return SerializedPropertyNumericType.UInt32;
                case Shape.Long: return SerializedPropertyNumericType.Int64;
                case Shape.ULong: return SerializedPropertyNumericType.UInt64;
                case Shape.Float: return SerializedPropertyNumericType.Float;
                case Shape.Double: return SerializedPropertyNumericType.Double;
                default: return SerializedPropertyNumericType.Unknown;
            }
        }

        // ---------------------------------------------------------------- navigation

        internal static SerializedNode Find(SerializedNode from, string path)
        {
            if (from == null || path == null) return null;
            if (path.Length == 0) return from;
            var node = from;
            foreach (var segment in path.Split('.'))
            {
                node = Child(node, segment);
                if (node == null) return null;
            }
            return node;
        }

        static SerializedNode Child(SerializedNode node, string segment)
        {
            if (node.Kind == SerializedNodeKind.ArrayContainer && segment.StartsWith("data[", StringComparison.Ordinal) && segment.EndsWith("]", StringComparison.Ordinal))
            {
                if (!int.TryParse(segment.AsSpan(5, segment.Length - 6), NumberStyles.None, CultureInfo.InvariantCulture, out int index)) return null;
                return index >= 0 && index + 1 < node.Children.Count ? node.Children[index + 1] : null;
            }
            foreach (var child in node.Children)
                if (!child.IsElement && child.Name == segment) return child;
            return null;
        }

        /// <summary>The next property in Unity's iteration order (depth first), or null at the end.</summary>
        internal static SerializedNode Next(SerializedNode node, bool enterChildren)
        {
            if ((enterChildren || node.Kind == SerializedNodeKind.Root) && node.Children.Count > 0) return node.Children[0];
            for (var n = node; n.Parent != null; n = n.Parent)
            {
                var siblings = n.Parent.Children;
                if (n.SiblingIndex + 1 < siblings.Count) return siblings[n.SiblingIndex + 1];
            }
            return null;
        }

        /// <summary>The next visible property: hidden properties are skipped with their children,
        /// except the `Array` node of an array, whose size and elements are visible.</summary>
        internal static SerializedNode NextVisible(SerializedNode node, bool enterChildren)
        {
            // A native string's characters and a reference's IDs are never visible.
            if (node.TypeTree && (node.Shape == Shape.String || node.Shape == Shape.UnityObject)) enterChildren = false;
            var next = Next(node, enterChildren);
            while (next != null && next.Hidden)
                next = Next(next, next.Kind == SerializedNodeKind.ArrayContainer);
            return next;
        }

        internal static bool HasVisibleChildren(SerializedNode node)
        {
            foreach (var child in node.Children)
                if (!child.Hidden || child.Kind == SerializedNodeKind.ArrayContainer) return true;
            return false;
        }

        // ---------------------------------------------------------------- values

        internal static object GetValue(SerializedNode node)
        {
            switch (node.Kind)
            {
                case SerializedNodeKind.Component: return node.Component.Get(GetValue(node.Parent));
                case SerializedNodeKind.ArraySize: return node.Parent.Children.Count - 1;
                default: return node.Value;
            }
        }

        /// <summary>Writes a leaf (or a part of one) into the buffer; false when nothing changed.</summary>
        internal static bool SetValue(SerializedNode node, object value)
        {
            if (node.ReadOnly) return false;
            switch (node.Kind)
            {
                case SerializedNodeKind.Component:
                {
                    var whole = GetValue(node.Parent);
                    if (ValueEquals(node.Component.Get(whole), value)) return false;
                    return SetValue(node.Parent, node.Component.With(whole, value));
                }
                case SerializedNodeKind.ArraySize:
                    return Resize(node.Parent.Parent, Convert.ToInt32(value, CultureInfo.InvariantCulture));
                case SerializedNodeKind.Leaf:
                    if (ValueEquals(node.Value, value)) return false;
                    node.Value = CopyLeaf(value, node.Shape);
                    if (node.TypeTree && node.Shape == Shape.String) TypeTreeSerialized.Chars(node);
                    MarkDirty(node);
                    return true;
                default:
                    return false;
            }
        }

        internal static bool ValueEquals(object a, object b)
        {
            if (a is Object || b is Object) return ReferenceEquals(a, b);
            if (a is AnimationCurve || a is Gradient || a is RectOffset)
            {
                if (a is RectOffset ra && b is RectOffset rb)
                    return ra.left == rb.left && ra.right == rb.right && ra.top == rb.top && ra.bottom == rb.bottom;
                return a.Equals(b);
            }
            return Equals(a, b);
        }

        /// <summary>Replaces the subtree of a composite, array or managed reference with the data of
        /// <paramref name="value"/> (boxedValue, managedReferenceValue).</summary>
        internal static void Assign(SerializedNode node, object value, bool callbacks)
        {
            var ctx = new BuildContext { Callbacks = callbacks };
            var fresh = Make(node.Parent, node.Name, node.Index, node.ClrType, node.Shape, node.ElementType, node.ElementShape, value, ctx, Nesting(node));
            node.Children = fresh.Children;
            foreach (var child in node.Children) child.Parent = node;
            if (node.Kind == SerializedNodeKind.ManagedReference)
            {
                node.Value = fresh.Value;
                node.TypeName = fresh.TypeName;
            }
            MarkReplaced(node);
        }

        static int Nesting(SerializedNode node)
        {
            int nesting = 0;
            for (var n = node.Parent; n != null && n.Kind != SerializedNodeKind.Root; n = n.Parent)
                if (n.Kind == SerializedNodeKind.Composite || n.Kind == SerializedNodeKind.ManagedReference || n.Kind == SerializedNodeKind.Array) nesting++;
            return nesting;
        }

        /// <summary>Copies the data of <paramref name="source"/> (another SerializedObject's
        /// property at the same path) into <paramref name="target"/>.</summary>
        internal static bool CopyData(SerializedNode target, SerializedNode source)
        {
            if (target.Kind != source.Kind || target.ReadOnly) return false;
            switch (target.Kind)
            {
                case SerializedNodeKind.Leaf:
                case SerializedNodeKind.Component:
                case SerializedNodeKind.ArraySize:
                    if (target.ClrType != source.ClrType) return false;
                    return SetValue(target, GetValue(source));
                case SerializedNodeKind.Root:
                    return false;
            }
            if (target.ClrType != source.ClrType) return false;
            var children = new List<SerializedNode>();
            foreach (var child in source.Children) children.Add(Clone(child, target));
            target.Children = children;
            if (target.Kind == SerializedNodeKind.ManagedReference)
            {
                target.Value = source.Value;
                target.TypeName = source.TypeName;
            }
            RePath(target, target.Path);
            MarkReplaced(target);
            return true;
        }

        static void MarkDirty(SerializedNode node)
        {
            for (var n = node; n != null; n = n.Parent) n.Dirty = true;
        }

        static void MarkReplaced(SerializedNode node)
        {
            node.Replaced = true;
            MarkDirty(node);
        }

        internal static void ClearDirty(SerializedNode node)
        {
            if (!node.Dirty && !node.Replaced) return;
            node.Dirty = false;
            node.Replaced = false;
            foreach (var child in node.Children) ClearDirty(child);
        }

        internal static bool DataEquals(SerializedNode a, SerializedNode b)
        {
            if (a == null || b == null) return a == b;
            if (a.Kind == SerializedNodeKind.Leaf || a.Kind == SerializedNodeKind.Component || a.Kind == SerializedNodeKind.ArraySize)
                return (b.Kind == SerializedNodeKind.Leaf || b.Kind == SerializedNodeKind.Component || b.Kind == SerializedNodeKind.ArraySize)
                       && a.ClrType == b.ClrType && ValueEquals(GetValue(a), GetValue(b));
            if (a.Kind != b.Kind || a.ClrType != b.ClrType || a.Children.Count != b.Children.Count) return false;
            if (a.Kind == SerializedNodeKind.ManagedReference && a.Value?.GetType() != b.Value?.GetType()) return false;
            for (int i = 0; i < a.Children.Count; i++)
                if (!DataEquals(a.Children[i], b.Children[i])) return false;
            return true;
        }

        internal static uint ContentHash(SerializedNode node)
        {
            var hash = new HashCode();
            AddHash(ref hash, node);
            return unchecked((uint)hash.ToHashCode());
        }

        static void AddHash(ref HashCode hash, SerializedNode node)
        {
            hash.Add((int)node.Kind);
            if (node.Kind == SerializedNodeKind.Leaf || node.Kind == SerializedNodeKind.ArraySize)
            {
                var value = GetValue(node);
                if (value is Object o) hash.Add(o is null ? 0 : o.GetInstanceID());
                else if (value is AnimationCurve curve) foreach (var k in curve.keys) hash.Add(k.GetHashCode());
                else if (value != null && !(value is Gradient)) hash.Add(value);
                return;
            }
            foreach (var child in node.Children)
                if (child.Kind != SerializedNodeKind.Component) AddHash(ref hash, child);
        }

        // ---------------------------------------------------------------- arrays

        internal static bool InsertElement(SerializedNode array, int index)
        {
            int count = array.ArrayCount;
            if (index < 0 || index > count)
            {
                Debug.LogError($"Index {index} is out of range of array '{array.Path}' (size {count}).");
                return false;
            }
            var container = array.ArrayContainer;
            SerializedNode element;
            var source = index < count ? array.Element(index) : count > 0 ? array.Element(count - 1) : null;
            if (source != null) element = Clone(source, container);
            else if (array.TypeTree) element = TypeTreeSerialized.NewElement(array, index);
            else element = Make(container, "data", index, array.ElementType, array.ElementShape, null, Shape.Unsupported, null,
                                new BuildContext { Zero = true }, Nesting(array) + 1);
            element.Index = index;
            container.Children.Insert(index + 1, element);
            Reindex(container);
            MarkReplaced(array);
            return true;
        }

        internal static bool DeleteElement(SerializedNode array, int index)
        {
            int count = array.ArrayCount;
            if (index < 0 || index >= count)
            {
                Debug.LogError($"Index {index} is out of range of array '{array.Path}' (size {count}).");
                return false;
            }
            var container = array.ArrayContainer;
            container.Children.RemoveAt(index + 1);
            Reindex(container);
            MarkReplaced(array);
            return true;
        }

        internal static bool MoveElement(SerializedNode array, int src, int dst)
        {
            int count = array.ArrayCount;
            if (src < 0 || src >= count || dst < 0 || dst >= count) return false;
            if (src == dst) return true;
            var container = array.ArrayContainer;
            var element = container.Children[src + 1];
            container.Children.RemoveAt(src + 1);
            container.Children.Insert(dst + 1, element);
            Reindex(container);
            MarkReplaced(array);
            return true;
        }

        internal static bool Resize(SerializedNode array, int size)
        {
            if (size < 0)
            {
                Debug.LogError($"Array size {size} of '{array.Path}' is negative.");
                return false;
            }
            int count = array.ArrayCount;
            if (size == count) return false;
            var container = array.ArrayContainer;
            if (size < count)
            {
                container.Children.RemoveRange(size + 1, count - size);
                Reindex(container);
                MarkReplaced(array);
                return true;
            }
            while (array.ArrayCount < size) InsertElement(array, array.ArrayCount);
            return true;
        }

        static void Reindex(SerializedNode container)
        {
            for (int i = 0; i < container.Children.Count; i++)
            {
                var child = container.Children[i];
                child.SiblingIndex = i;
                if (i == 0) continue;
                child.Index = i - 1;
                var path = ChildPath(container, child);
                if (child.Path != path) RePath(child, path);
            }
        }

        static void RePath(SerializedNode node, string path)
        {
            node.Path = path;
            for (int i = 0; i < node.Children.Count; i++)
            {
                var child = node.Children[i];
                child.SiblingIndex = i;
                RePath(child, ChildPath(node, child));
            }
        }

        /// <summary>A copy of a subtree (duplicated array elements, copied properties): class
        /// instances are created afresh when written back; managed references stay shared, as when
        /// Unity duplicates them.</summary>
        static SerializedNode Clone(SerializedNode node, SerializedNode parent)
        {
            var copy = node.ShallowCopy();
            copy.Parent = parent;
            copy.Dirty = false;
            copy.Replaced = false;
            copy.Children = new List<SerializedNode>(node.Children.Count);
            if (copy.Kind == SerializedNodeKind.Composite) copy.Value = null;
            else if (copy.Kind == SerializedNodeKind.Leaf) copy.Value = CopyLeaf(node.Value, node.Shape);
            foreach (var child in node.Children) copy.Children.Add(Clone(child, copy));
            return copy;
        }

        // ---------------------------------------------------------------- writing back

        /// <summary>Writes the buffered changes into an object. <paramref name="primary"/> is the
        /// object the buffer was read from (its class instances are reused).</summary>
        internal static void Apply(SerializedNode root, Object target, bool primary)
        {
            foreach (var child in root.Children)
            {
                if (!child.Dirty || child.ReadOnly || child.Accessor == null) continue;
                try
                {
                    child.Accessor.Set(target, ApplyNode(child, child.Accessor.Get(target), primary));
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        static void ApplyChildren(SerializedNode node, object container, bool primary)
        {
            foreach (var child in node.Children)
            {
                if (!child.Dirty || child.ReadOnly || child.Accessor == null) continue;
                child.Accessor.Set(container, ApplyNode(child, child.Accessor.Get(container), primary));
            }
        }

        static object ApplyNode(SerializedNode node, object live, bool primary)
        {
            if (node.Replaced) return Build(node, live, primary);
            switch (node.Kind)
            {
                case SerializedNodeKind.Leaf:
                    return CopyLeaf(node.Value, node.Shape);
                case SerializedNodeKind.Composite:
                    if (live == null) return Build(node, null, primary);
                    ApplyChildren(node, live, primary);
                    AfterDeserialize(live);
                    return live;
                case SerializedNodeKind.ManagedReference:
                    if (live == null || live.GetType() != node.Value?.GetType()) return Build(node, null, primary);
                    ApplyChildren(node, live, primary);
                    AfterDeserialize(live);
                    return live;
                case SerializedNodeKind.Array:
                {
                    int count = node.ArrayCount;
                    if (!(live is IList list) || list.Count != count) return Build(node, live, primary);
                    for (int i = 0; i < count; i++)
                    {
                        var element = node.Element(i);
                        if (element.Dirty) list[i] = ApplyNode(element, list[i], primary);
                    }
                    return list;
                }
                default:
                    return live;
            }
        }

        /// <summary>The value a subtree describes, written whole (into <paramref name="existing"/> when given).</summary>
        internal static object Build(SerializedNode node, object existing, bool primary)
        {
            switch (node.Kind)
            {
                case SerializedNodeKind.Leaf:
                    return CopyLeaf(node.Value, node.Shape);
                case SerializedNodeKind.Component:
                case SerializedNodeKind.ArraySize:
                    return GetValue(node);
                case SerializedNodeKind.Composite:
                {
                    var type = node.ClrType;
                    object instance = type.IsValueType
                        ? existing ?? Activator.CreateInstance(type)
                        : existing ?? (primary ? node.Value : null) ?? SerializationRules.Create(type);
                    WriteAll(node, instance, primary);
                    if (primary && !type.IsValueType) node.Value = instance;
                    AfterDeserialize(instance);
                    return instance;
                }
                case SerializedNodeKind.ManagedReference:
                {
                    if (node.Value == null) return null;
                    var instance = primary ? node.Value : SerializationRules.Create(node.Value.GetType());
                    WriteAll(node, instance, primary);
                    AfterDeserialize(instance);
                    return instance;
                }
                case SerializedNodeKind.Array:
                {
                    int count = node.ArrayCount;
                    IList result = node.ClrType.IsArray
                        ? System.Array.CreateInstance(node.ElementType, count)
                        : (IList)Activator.CreateInstance(node.ClrType, count);
                    for (int i = 0; i < count; i++)
                    {
                        var value = Build(node.Element(i), null, primary);
                        if (node.ClrType.IsArray) result[i] = value;
                        else result.Add(value);
                    }
                    return result;
                }
                default:
                    return existing;
            }
        }

        static void WriteAll(SerializedNode node, object instance, bool primary)
        {
            foreach (var child in node.Children)
            {
                if (child.ReadOnly || child.Accessor == null) continue;
                child.Accessor.Set(instance, Build(child, child.Accessor.Get(instance), primary));
            }
        }

        static void AfterDeserialize(object instance)
        {
            if (!(instance is ISerializationCallbackReceiver receiver)) return;
            try { receiver.OnAfterDeserialize(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        // ---------------------------------------------------------------- managed references

        static readonly ConditionalWeakTable<object, object> s_ReferenceIds = new ConditionalWeakTable<object, object>();
        static long s_NextReferenceId = 1000;

        /// <summary>Unity's "no reference" id.</summary>
        internal const long NullReferenceId = -2;

        internal static long ReferenceId(object value)
        {
            if (value == null) return NullReferenceId;
            lock (s_ReferenceIds)
                return (long)s_ReferenceIds.GetValue(value, _ => s_NextReferenceId++);
        }

        internal static object FindReference(SerializedNode root, long id)
        {
            if (root.Kind == SerializedNodeKind.ManagedReference && root.Value != null && ReferenceId(root.Value) == id) return root.Value;
            foreach (var child in root.Children)
            {
                var found = FindReference(child, id);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>`Assembly Namespace.Class` (nested classes `Outer/Inner`), as Unity names a managed reference type.</summary>
        internal static string ManagedTypeName(Type type)
        {
            if (type == null) return "";
            return type.Assembly.GetName().Name + " " + (type.FullName ?? type.Name).Replace('+', '/');
        }
    }
}
