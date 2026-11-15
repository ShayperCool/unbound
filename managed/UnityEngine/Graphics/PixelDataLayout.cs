using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace UnityEngine
{
    /// <summary>The memory of an array of structs that hold object references, as the reference
    /// runtime lays it out (matches the Unity 6000.0.71f1 Editor, tests/projects/pixeltransfer):
    /// fields in declaration order, each at the next multiple of its alignment (a reference counts
    /// eight bytes), the struct's size a multiple of its largest alignment. This runtime places such a
    /// struct's references first, so SetPixelData copies its elements field by field into that
    /// layout. Reference slots receive this runtime's object addresses, which are arbitrary in both.
    /// A struct without references keeps its declared layout here too and is copied as one block.
    /// Explicit layouts, explicit packing or size, pointers and fields no sample value can be stored
    /// in are not supported.</summary>
    internal sealed class DeclaredLayout
    {
        /// <summary>One element's size in the declared layout.</summary>
        internal readonly int Size;
        /// <summary>Why the element type is not supported, or null.</summary>
        internal readonly string Unsupported;
        readonly int[] m_Source, m_Target, m_Length;

        DeclaredLayout(int size, string unsupported, List<(int source, int target, int length)> blocks)
        {
            Size = size;
            Unsupported = unsupported;
            m_Source = new int[blocks.Count];
            m_Target = new int[blocks.Count];
            m_Length = new int[blocks.Count];
            for (int i = 0; i < blocks.Count; i++) (m_Source[i], m_Target[i], m_Length[i]) = blocks[i];
        }

        /// <summary>The layout of an element type that holds references; null for any other.</summary>
        internal static DeclaredLayout Of<T>()
        {
            if (!typeof(T).IsValueType || !RuntimeHelpers.IsReferenceOrContainsReferences<T>()) return null;
            var blocks = new List<(int, int, int)>();
            try
            {
                if (Nullable.GetUnderlyingType(typeof(T)) != null) throw new NotSupportedException("it is a Nullable holding references");
                var (size, _) = Measure(typeof(T));
                var sample = new Sample<T>();
                Place(sample, typeof(T), new List<FieldInfo>(), 0, blocks);
                return new DeclaredLayout(size, null, blocks);
            }
            catch (NotSupportedException e)
            {
                return new DeclaredLayout(1, e.Message, blocks);
            }
        }

        /// <summary>The level's bytes from <paramref name="offset"/> of the array's declared-layout
        /// memory. This copy allocates; ordinary element types read the array in place.</summary>
        internal ReadOnlySpan<byte> Copy<T>(T[] data, long offset, int count)
        {
            var bytes = new byte[count];
            long end = offset + count;
            for (long e = offset / Size; e * Size < end; e++)
            {
                var element = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<T, byte>(ref data[e]), Unsafe.SizeOf<T>());
                long origin = e * Size - offset;
                for (int i = 0; i < m_Source.Length; i++)
                {
                    long from = Math.Max(0, origin + m_Target[i]), to = Math.Min(count, origin + m_Target[i] + m_Length[i]);
                    if (from < to)
                        element.Slice(m_Source[i] + (int)(from - origin - m_Target[i]), (int)(to - from)).CopyTo(bytes.AsSpan((int)from));
                }
            }
            return bytes;
        }

        static FieldInfo[] Fields(Type type)
        {
            var layout = type.StructLayoutAttribute;
            if (layout != null && layout.Value == LayoutKind.Explicit)
                throw new NotSupportedException($"{type.Name} has an explicit layout");
            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Array.Sort(fields, (a, b) => a.MetadataToken.CompareTo(b.MetadataToken));
            return fields;
        }

        static bool HoldsReferences(Type type) => !type.IsValueType ||
            (!type.IsPrimitive && !type.IsEnum && Array.Exists(Fields(type), f => HoldsReferences(f.FieldType)));

        /// <summary>A field type's size and alignment in the declared layout.</summary>
        static (int size, int align) Measure(Type type)
        {
            if (type.IsPointer || type == typeof(IntPtr) || type == typeof(UIntPtr) || type.IsByRef)
                throw new NotSupportedException($"its {type.Name} field is a pointer");
            if (!type.IsValueType) return (IntPtr.Size, IntPtr.Size);
            if (type.IsEnum) type = Enum.GetUnderlyingType(type);
            if (type.IsPrimitive)
            {
                int primitive = RuntimeHelpers.SizeOf(type.TypeHandle);
                return (primitive, primitive);
            }
            if (Nullable.GetUnderlyingType(type) != null && HoldsReferences(type))
                throw new NotSupportedException($"its {type.Name} field is a Nullable holding references");
            var layout = type.StructLayoutAttribute;
            bool references = HoldsReferences(type);
            if (references && layout != null && ((layout.Pack != 0 && layout.Pack != 8) || layout.Size != 0))
                throw new NotSupportedException($"{type.Name} sets its packing or size");
            int offset = 0, align = 1;
            foreach (var field in Fields(type))
            {
                var (size, fieldAlign) = Measure(field.FieldType);
                offset = (offset + fieldAlign - 1) / fieldAlign * fieldAlign + size;
                align = Math.Max(align, fieldAlign);
            }
            // A struct without references keeps this runtime's size (a fixed buffer's included).
            if (!references) offset = Math.Max(offset, RuntimeHelpers.SizeOf(type.TypeHandle));
            return (Math.Max(1, (offset + align - 1) / align * align), align);
        }

        /// <summary>Adds the blocks of a reference-holding struct placed at <paramref name="target"/>.</summary>
        static void Place<T>(Sample<T> sample, Type type, List<FieldInfo> path, int target, List<(int, int, int)> blocks)
        {
            int offset = 0;
            foreach (var field in Fields(type))
            {
                var (size, align) = Measure(field.FieldType);
                offset = (offset + align - 1) / align * align;
                path.Add(field);
                if (field.FieldType.IsValueType && !field.FieldType.IsPrimitive && !field.FieldType.IsEnum && HoldsReferences(field.FieldType))
                    Place(sample, field.FieldType, path, target + offset, blocks);
                else
                    blocks.Add((sample.OffsetOf(path, size), target + offset, size));
                path.RemoveAt(path.Count - 1);
                offset += size;
            }
        }

        /// <summary>Finds where this runtime keeps a field: stores a value with no zero byte in a
        /// zeroed boxed sample and looks for the bytes that changed.</summary>
        sealed class BoxedBytes
        {
            internal byte First;
        }

        sealed class Sample<T>
        {
            internal int OffsetOf(List<FieldInfo> path, int size)
            {
                object boxed = default(T);
                Store(boxed, path, 0, Marker(path[^1].FieldType));
                // A boxed value's bytes follow the object header, where a one-byte class's field is.
                var bytes = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<BoxedBytes>(boxed).First, Unsafe.SizeOf<T>());
                for (int i = 0; i < bytes.Length; i++)
                    if (bytes[i] != 0) return i;
                throw new NotSupportedException($"the place of its {path[^1].Name} field was not found");
            }

            static void Store(object owner, List<FieldInfo> path, int depth, object value)
            {
                var field = path[depth];
                if (depth == path.Count - 1)
                {
                    field.SetValue(owner, value);
                    return;
                }
                object inner = field.GetValue(owner);
                Store(inner, path, depth + 1, value);
                field.SetValue(owner, inner);
            }

            static object Marker(Type type)
            {
                if (!type.IsValueType)
                {
                    if (type == typeof(string) || type.IsAssignableFrom(typeof(string))) return "marker";
                    if (type.IsArray) return Array.CreateInstance(type.GetElementType(), 0);
                    if (type.IsAbstract || type.IsInterface)
                        throw new NotSupportedException($"no sample value fits its {type.Name} field");
                    return RuntimeHelpers.GetUninitializedObject(type);
                }
                // Every byte set: a struct without references starts with a primitive (or is a block
                // whose first byte is found the same way).
                if (type.IsEnum) return Enum.ToObject(type, Marker(Enum.GetUnderlyingType(type)));
                if (type == typeof(bool)) return true;
                if (type == typeof(char)) return (char)0xFFFF;
                if (type == typeof(float)) return BitConverter.Int32BitsToSingle(-1);
                if (type == typeof(double)) return BitConverter.Int64BitsToDouble(-1);
                if (type == typeof(byte)) return byte.MaxValue;
                if (type == typeof(sbyte)) return (sbyte)-1;
                if (type == typeof(short)) return (short)-1;
                if (type == typeof(ushort)) return ushort.MaxValue;
                if (type == typeof(int)) return -1;
                if (type == typeof(uint)) return uint.MaxValue;
                if (type == typeof(long)) return -1L;
                if (type == typeof(ulong)) return ulong.MaxValue;
                if (type.IsPrimitive) throw new NotSupportedException($"its {type.Name} field is not supported");
                object block = RuntimeHelpers.GetUninitializedObject(type);
                var fields = Fields(type);
                if (fields.Length == 0) throw new NotSupportedException($"its {type.Name} field has no fields");
                fields[0].SetValue(block, Marker(fields[0].FieldType));
                return block;
            }
        }
    }
}
