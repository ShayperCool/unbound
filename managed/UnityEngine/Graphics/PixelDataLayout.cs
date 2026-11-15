using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace UnityEngine
{
    /// <summary>The memory of an array of structs that hold object references, as the reference
    /// runtime lays it out (matches the Unity 6000.0.71f1 Editor, tests/projects/pixeltransfer):
    /// fields in declaration order, each at the next multiple of its alignment (a reference counts
    /// eight bytes), the struct's size a multiple of its largest alignment. This runtime places such a
    /// struct's references first, so SetPixelData writes these elements field by field: a reference
    /// as its object's address (arbitrary in both runtimes), a Nullable as its flag then its value,
    /// a struct without references as its own bytes, aligned to its largest field within its packing.
    /// Explicit layouts, packing or sizes on structs holding references, Nullables holding references,
    /// pointers and the class library's own structs are not supported.</summary>
    internal sealed class DeclaredLayout
    {
        enum Kind { Reference, Bytes, Nullable, Struct }

        sealed class Slot
        {
            internal FieldInfo Field;
            internal Kind Kind;
            internal int Target;
            /// <summary>Bytes of a block or of a Nullable's value.</summary>
            internal int Size;
            /// <summary>Where a Nullable's value starts after its flag.</summary>
            internal int ValueTarget;
            internal Slot[] Fields;
        }

        /// <summary>One element's size in the declared layout.</summary>
        internal readonly int Size;
        /// <summary>Why the element type is not supported, or null.</summary>
        internal readonly string Unsupported;
        readonly Slot[] m_Slots;

        DeclaredLayout(int size, string unsupported, Slot[] slots)
        {
            Size = size;
            Unsupported = unsupported;
            m_Slots = slots;
        }

        /// <summary>The layout of a struct type that holds references; null for any other type.</summary>
        internal static DeclaredLayout Of<T>()
        {
            if (!typeof(T).IsValueType || !RuntimeHelpers.IsReferenceOrContainsReferences<T>()) return null;
            try
            {
                if (Nullable.GetUnderlyingType(typeof(T)) != null) throw new NotSupportedException("it is a Nullable holding references");
                var slots = Plan(typeof(T), out int size, out _);
                return new DeclaredLayout(size, null, slots);
            }
            catch (Exception e)
            {
                return new DeclaredLayout(1, e.Message, null);
            }
        }

        /// <summary>The level's bytes from <paramref name="offset"/> of the array's declared-layout
        /// memory. Unlike ordinary element types, which are read in place, this builds a copy.</summary>
        internal ReadOnlySpan<byte> Copy<T>(T[] data, long offset, int count)
        {
            var bytes = new byte[count];
            var element = new byte[Size];
            long end = offset + count;
            for (long e = offset / Size; e * Size < end; e++)
            {
                Array.Clear(element);
                Write(m_Slots, data[e], element, 0);
                long origin = e * Size - offset;
                long from = Math.Max(0, origin), to = Math.Min(count, origin + Size);
                element.AsSpan((int)(from - origin), (int)(to - from)).CopyTo(bytes.AsSpan((int)from));
            }
            return bytes;
        }

        static void Write(Slot[] slots, object owner, byte[] element, int origin)
        {
            foreach (var slot in slots)
            {
                object value = slot.Field.GetValue(owner);
                int at = origin + slot.Target;
                switch (slot.Kind)
                {
                    case Kind.Reference:
                        if (value != null) MemoryMarshal.Write(element.AsSpan(at), Unsafe.As<object, nint>(ref value));
                        break;
                    case Kind.Bytes:
                        Raw(value, slot.Size).CopyTo(element.AsSpan(at));
                        break;
                    case Kind.Nullable:
                        if (value == null) break;
                        element[at] = 1;
                        Raw(value, slot.Size).CopyTo(element.AsSpan(at + slot.ValueTarget));
                        break;
                    case Kind.Struct:
                        Write(slot.Fields, value, element, at);
                        break;
                }
            }
        }

        sealed class BoxedBytes
        {
            internal byte First;
        }

        /// <summary>A boxed value's bytes, which follow the object header where a one-byte class's
        /// field is.</summary>
        static ReadOnlySpan<byte> Raw(object boxed, int size) =>
            MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<BoxedBytes>(boxed).First, size);

        static FieldInfo[] Fields(Type type)
        {
            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Array.Sort(fields, (a, b) => a.MetadataToken.CompareTo(b.MetadataToken));
            return fields;
        }

        static bool HoldsReferences(Type type) => !type.IsValueType ||
            (!type.IsPrimitive && !type.IsEnum && !type.IsPointer && Array.Exists(Fields(type), f => HoldsReferences(f.FieldType)));

        static int Align(int offset, int align) => (offset + align - 1) / align * align;

        /// <summary>The slots of a struct that holds references, with its declared size and alignment.</summary>
        static Slot[] Plan(Type type, out int size, out int align)
        {
            var layout = type.StructLayoutAttribute;
            if (layout != null && layout.Value != LayoutKind.Sequential)
                throw new NotSupportedException($"{type.Name} holds references with LayoutKind.{layout.Value}");
            if (layout != null && ((layout.Pack != 0 && layout.Pack != 8) || layout.Size != 0))
                throw new NotSupportedException($"{type.Name} holds references and sets its packing or size");
            var fields = Fields(type);
            var slots = new Slot[fields.Length];
            int offset = 0;
            align = 1;
            for (int i = 0; i < fields.Length; i++)
            {
                slots[i] = SlotOf(fields[i], out int fieldSize, out int fieldAlign);
                offset = Align(offset, fieldAlign);
                slots[i].Target = offset;
                offset += fieldSize;
                align = Math.Max(align, fieldAlign);
            }
            size = Align(Math.Max(1, offset), align);
            return slots;
        }

        static Slot SlotOf(FieldInfo field, out int size, out int align)
        {
            var type = field.FieldType;
            if (type.IsPointer || type.IsFunctionPointer || type.IsByRef)
                throw new NotSupportedException($"its {field.Name} field is a pointer");
            if (!type.IsValueType)
            {
                size = align = IntPtr.Size;
                return new Slot { Field = field, Kind = Kind.Reference };
            }
            var underlying = Nullable.GetUnderlyingType(type);
            if (underlying != null)
            {
                if (HoldsReferences(underlying)) throw new NotSupportedException($"its {field.Name} field is a Nullable holding references");
                var (valueSize, valueAlign) = Block(underlying);
                int valueTarget = Align(1, valueAlign);
                align = valueAlign;
                size = Align(valueTarget + valueSize, valueAlign);
                return new Slot { Field = field, Kind = Kind.Nullable, Size = valueSize, ValueTarget = valueTarget };
            }
            if (HoldsReferences(type))
                return new Slot { Field = field, Kind = Kind.Struct, Fields = Plan(type, out size, out align) };
            (size, align) = Block(type);
            return new Slot { Field = field, Kind = Kind.Bytes, Size = size };
        }

        /// <summary>A value type without references: this runtime's size, and the alignment of its
        /// largest field within its packing.</summary>
        static (int size, int align) Block(Type type)
        {
            if (type.IsPointer) return (IntPtr.Size, IntPtr.Size);
            int size = RuntimeHelpers.SizeOf(type.TypeHandle);
            if (type.IsPrimitive || type.IsEnum) return (size, size);
            // The two runtimes' class libraries declare their own structs' fields differently
            // (decimal's alignment, for one).
            if (type.Assembly == typeof(object).Assembly)
                throw new NotSupportedException($"the reference runtime's layout of its {type.Name} field is unknown");
            int align = 1;
            foreach (var field in Fields(type)) align = Math.Max(align, Block(field.FieldType).align);
            var layout = type.StructLayoutAttribute;
            if (layout != null && layout.Pack > 0) align = Math.Min(align, layout.Pack);
            return (size, align);
        }
    }
}
