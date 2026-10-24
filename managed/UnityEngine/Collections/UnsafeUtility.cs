using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unbound.Runtime;
using SysUnsafe = System.Runtime.CompilerServices.Unsafe;

namespace Unity.Collections.LowLevel.Unsafe
{
    /// <summary>Raw memory, as Unity's native allocators give it, and the type queries containers make.</summary>
    public static unsafe partial class UnsafeUtility
    {
        internal partial struct TypeFlagsCache<T>
        {
            internal static readonly int flags = GetScriptingTypeFlags(typeof(T));
        }

        struct AlignOfHelper<T> where T : struct
        {
            public byte dummy;
            public T data;
        }

        const int kIsManaged = 1;
        const int kIsNativeContainer = 2;

        // ------------------------------------------------------------------ allocation

        public static void* Malloc(long size, int alignment, Allocator allocator) => Allocation.Malloc(size, alignment, allocator, tracked: false, 0);

        public static void* MallocTracked(long size, int alignment, Allocator allocator, int callstacksToSkip) =>
            Allocation.Malloc(size, alignment, allocator, tracked: true, callstacksToSkip);

        public static void Free(void* memory, Allocator allocator) => Allocation.Free(memory, allocator);

        public static void FreeTracked(void* memory, Allocator allocator) => Allocation.Free(memory, allocator);

        public static bool IsValidAllocator(Allocator allocator) => allocator > Allocator.None;

        public static int CheckForLeaks() => Allocation.CheckForLeaks(forgive: false);

        public static int ForgiveLeaks() => Allocation.CheckForLeaks(forgive: true);

        public static NativeLeakDetectionMode GetLeakDetectionMode() => Allocation.LeakDetectionMode;

        public static void SetLeakDetectionMode(NativeLeakDetectionMode value) => Allocation.LeakDetectionMode = value;

        internal static int LeakRecord(IntPtr handle, LeakCategory category, int callstacksToSkip) => Allocation.LeakRecord(handle, category, callstacksToSkip + 1);

        internal static int LeakErase(IntPtr handle, LeakCategory category) => Allocation.LeakErase(handle);

        // ------------------------------------------------------------------ memory

        public static void MemCpy(void* destination, void* source, long size) => Buffer.MemoryCopy(source, destination, size, size);

        public static void MemCpyReplicate(void* destination, void* source, int size, int count)
        {
            for (int i = 0; i < count; i++) Buffer.MemoryCopy(source, (byte*)destination + (long)i * size, size, size);
        }

        public static void MemCpyStride(void* destination, int destinationStride, void* source, int sourceStride, int elementSize, int count)
        {
            for (int i = 0; i < count; i++)
                Buffer.MemoryCopy((byte*)source + (long)i * sourceStride, (byte*)destination + (long)i * destinationStride, elementSize, elementSize);
        }

        public static void MemMove(void* destination, void* source, long size) => Buffer.MemoryCopy(source, destination, size, size);

        public static void MemSwap(void* ptr1, void* ptr2, long size)
        {
            var a = (byte*)ptr1;
            var b = (byte*)ptr2;
            for (long i = 0; i < size; i++) (a[i], b[i]) = (b[i], a[i]);
        }

        public static void MemSet(void* destination, byte value, long size) => NativeMemory.Fill(destination, (nuint)size, value);

        public static void MemClear(void* destination, long size) => MemSet(destination, 0, size);

        public static int MemCmp(void* ptr1, void* ptr2, long size) =>
            new ReadOnlySpan<byte>(ptr1, (int)size).SequenceCompareTo(new ReadOnlySpan<byte>(ptr2, (int)size));

        // ------------------------------------------------------------------ types

        static readonly ConcurrentDictionary<Type, int> s_Sizes = new ConcurrentDictionary<Type, int>();

        public static int SizeOf(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            return s_Sizes.GetOrAdd(type, t =>
            {
                if (!t.IsValueType) return IntPtr.Size;
                return (int)typeof(SysUnsafe).GetMethod(nameof(SysUnsafe.SizeOf))!.MakeGenericMethod(t).Invoke(null, null)!;
            });
        }

        public static int SizeOf<T>() where T : struct => SysUnsafe.SizeOf<T>();

        public static int AlignOf<T>() where T : struct => SizeOf<AlignOfHelper<T>>() - SizeOf<T>();

        /// <summary>Blittable as Mono defines it: value types of primitives without bool and char.</summary>
        public static bool IsBlittable(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            return s_Blittable.GetOrAdd(type, t => ComputeBlittable(t, 0));
        }

        public static bool IsBlittable<T>() where T : struct => IsBlittable(typeof(T));

        static readonly ConcurrentDictionary<Type, bool> s_Blittable = new ConcurrentDictionary<Type, bool>();

        static bool ComputeBlittable(Type type, int depth)
        {
            if (depth > 32 || !type.IsValueType) return false;
            if (type.IsPrimitive) return type != typeof(bool) && type != typeof(char);
            if (type.IsEnum || type.IsPointer || type == typeof(IntPtr) || type == typeof(UIntPtr)) return true;
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>)) return false;
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var fieldType = field.FieldType;
                if (fieldType.IsPointer || fieldType.IsFunctionPointer) continue;
                if (!ComputeBlittable(fieldType, depth + 1)) return false;
            }
            return true;
        }

        public static bool IsUnmanaged(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            return (GetScriptingTypeFlags(type) & kIsManaged) == 0;
        }

        public static bool IsUnmanaged<T>() => (TypeFlagsCache<T>.flags & kIsManaged) == 0;

        public static bool IsNativeContainerType<T>() => (TypeFlagsCache<T>.flags & kIsNativeContainer) != 0;

        public static bool IsValidNativeContainerElementType(Type type) => GetScriptingTypeFlags(type) == 0;

        public static bool IsValidNativeContainerElementType<T>() => TypeFlagsCache<T>.flags == 0;

        static readonly ConcurrentDictionary<Type, int> s_TypeFlags = new ConcurrentDictionary<Type, int>();

        /// <summary>Whether a type holds managed references (1) and whether it is or holds a native
        /// container (2).</summary>
        internal static int GetScriptingTypeFlags(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            return s_TypeFlags.GetOrAdd(type, t => ComputeFlags(t, 0));
        }

        static int ComputeFlags(Type type, int depth)
        {
            if (!type.IsValueType) return type.IsPointer || type.IsFunctionPointer ? 0 : kIsManaged;
            if (type.IsPrimitive || type.IsEnum || depth > 32) return 0;
            int flags = type.IsDefined(typeof(NativeContainerAttribute), false) ? kIsNativeContainer : 0;
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var fieldType = field.FieldType;
                if (fieldType.IsPointer || fieldType.IsFunctionPointer) continue;
                if (fieldType == type) continue;
                flags |= fieldType.IsValueType ? ComputeFlags(fieldType, depth + 1) : kIsManaged;
            }
            return flags;
        }

        static bool IsBlittableValueType(Type t) => t.IsValueType && IsBlittable(t);

        static string GetReasonForTypeNonBlittableImpl(Type t, string name)
        {
            if (!t.IsValueType) return $"{name} is not blittable because it is not of value type ({t})\n";
            if (t.IsPrimitive) return $"{name} is not blittable ({t})\n";
            var text = "";
            foreach (var field in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                if (!IsBlittableValueType(field.FieldType))
                    text += GetReasonForTypeNonBlittableImpl(field.FieldType, $"{name}.{field.Name}");
            return text;
        }

        internal static bool IsArrayBlittable(Array arr) => IsBlittableValueType(arr.GetType().GetElementType());

        internal static bool IsGenericListBlittable<T>() where T : struct => IsBlittable<T>();

        internal static string GetReasonForArrayNonBlittable(Array arr)
        {
            var element = arr.GetType().GetElementType();
            return GetReasonForTypeNonBlittableImpl(element, element.Name);
        }

        internal static string GetReasonForGenericListNonBlittable<T>() where T : struct => GetReasonForTypeNonBlittableImpl(typeof(T), typeof(T).Name);

        internal static string GetReasonForTypeNonBlittable(Type t) => GetReasonForTypeNonBlittableImpl(t, t.Name);

        internal static string GetReasonForValueTypeNonBlittable<T>() where T : struct => GetReasonForTypeNonBlittableImpl(typeof(T), typeof(T).Name);

        // ------------------------------------------------------------------ fields and objects

        static readonly ConcurrentDictionary<FieldInfo, int> s_FieldOffsets = new ConcurrentDictionary<FieldInfo, int>();

        /// <summary>The offset of a field in its struct, or from the object reference (its header
        /// included, as Mono counts) in a class.</summary>
        public static int GetFieldOffset(FieldInfo field)
        {
            if (field == null) throw new ArgumentNullException(nameof(field));
            if (field.DeclaringType.IsValueType) return s_FieldOffsets.GetOrAdd(field, ComputeFieldOffset);
            if (field.DeclaringType.IsClass) return IntPtr.Size + s_FieldOffsets.GetOrAdd(field, ComputeFieldOffset);
            throw new ArgumentException("field.DeclaringType must be a struct or class");
        }

        /// <summary>The managed layout's offset (Marshal.OffsetOf gives the marshaled one): the
        /// distance from a reference to the instance data to the field's address.</summary>
        static int ComputeFieldOffset(FieldInfo field)
        {
            if (field.IsStatic) throw new ArgumentException("field must not be static");
            var declaring = field.DeclaringType;
            var method = new DynamicMethod("FieldOffset", typeof(int), new[] { typeof(object) }, typeof(UnsafeUtility).Module, true);
            var il = method.GetILGenerator();
            if (declaring.IsValueType)
            {
                // Offset within the boxed value: unbox gives the address of its data.
                var data = il.DeclareLocal(typeof(IntPtr));
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Unbox, declaring);
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Conv_U);
                il.Emit(OpCodes.Stloc, data);
                il.Emit(OpCodes.Ldflda, field);
                il.Emit(OpCodes.Conv_U);
                il.Emit(OpCodes.Ldloc, data);
                il.Emit(OpCodes.Sub);
                il.Emit(OpCodes.Conv_I4);
                il.Emit(OpCodes.Ret);
                var instance = RuntimeHelpers.GetUninitializedObject(declaring);
                return ((Func<object, int>)method.CreateDelegate(typeof(Func<object, int>)))(instance);
            }
            else
            {
                // Offset of the field from the first byte after the method table pointer.
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Castclass, declaring);
                il.Emit(OpCodes.Ldflda, field);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Call, typeof(UnsafeUtility).GetMethod(nameof(ObjectDataReference), BindingFlags.NonPublic | BindingFlags.Static));
                il.Emit(OpCodes.Sub);
                il.Emit(OpCodes.Conv_I4);
                il.Emit(OpCodes.Ret);
                var instance = RuntimeHelpers.GetUninitializedObject(declaring);
                return ((Func<object, int>)method.CreateDelegate(typeof(Func<object, int>)))(instance);
            }
        }

        static ref byte ObjectDataReference(object o) => ref SysUnsafe.As<RawData>(o).Data;

        sealed class RawData
        {
            public byte Data;
        }

        public static void* PinGCObjectAndGetAddress(object target, out ulong gcHandle)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            var handle = new PinnedGCHandle<object>(target);
            gcHandle = (ulong)(long)PinnedGCHandle<object>.ToIntPtr(handle);
            // The object reference itself (its header included), which GetFieldOffset counts from.
            return (byte*)handle.GetAddressOfObjectData() - IntPtr.Size;
        }

        public static void* PinGCArrayAndGetDataAddress(Array target, out ulong gcHandle)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            var handle = new PinnedGCHandle<object>(target);
            gcHandle = (ulong)(long)PinnedGCHandle<object>.ToIntPtr(handle);
            return SysUnsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(target));
        }

        public static void ReleaseGCObject(ulong gcHandle) => GCHandle.FromIntPtr((IntPtr)(long)gcHandle).Free();

        public static void CopyObjectAddressToPtr(object target, void* dstPtr) => *(IntPtr*)dstPtr = SysUnsafe.As<object, IntPtr>(ref target);

        // ------------------------------------------------------------------ structures and elements

        public static void CopyPtrToStructure<T>(void* ptr, out T output) where T : struct
        {
            if (ptr == null) throw new ArgumentNullException();
            output = SysUnsafe.ReadUnaligned<T>(ptr);
        }

        public static void CopyStructureToPtr<T>(ref T input, void* ptr) where T : struct
        {
            if (ptr == null) throw new ArgumentNullException();
            SysUnsafe.WriteUnaligned(ptr, input);
        }

        public static T ReadArrayElement<T>(void* source, int index) => SysUnsafe.ReadUnaligned<T>((byte*)source + (long)index * SysUnsafe.SizeOf<T>());

        public static T ReadArrayElementWithStride<T>(void* source, int index, int stride) => SysUnsafe.ReadUnaligned<T>((byte*)source + (long)index * stride);

        public static void WriteArrayElement<T>(void* destination, int index, T value) =>
            SysUnsafe.WriteUnaligned((byte*)destination + (long)index * SysUnsafe.SizeOf<T>(), value);

        public static void WriteArrayElementWithStride<T>(void* destination, int index, int stride, T value) =>
            SysUnsafe.WriteUnaligned((byte*)destination + (long)index * stride, value);

        public static void* AddressOf<T>(ref T output) where T : struct => SysUnsafe.AsPointer(ref output);

        public static ref T As<U, T>(ref U from) => ref SysUnsafe.As<U, T>(ref from);

        internal static T As<T>(object from) where T : class => (T)from;

        public static ref T AsRef<T>(void* ptr) where T : struct => ref SysUnsafe.AsRef<T>(ptr);

        internal static ref T ClassAsRef<T>(void* ptr) where T : class => ref SysUnsafe.AsRef<T>(ptr);

        public static ref T ArrayElementAsRef<T>(void* ptr, int index) where T : struct =>
            ref SysUnsafe.AsRef<T>((byte*)ptr + (long)index * SysUnsafe.SizeOf<T>());

        internal static ref T Add<T>(ref T source, int elementOffset) where T : unmanaged => ref SysUnsafe.Add(ref source, elementOffset);

        public static int EnumToInt<T>(T enumValue) where T : struct, IConvertible
        {
            int intValue = 0;
            SysUnsafe.CopyBlockUnaligned(ref SysUnsafe.As<int, byte>(ref intValue), ref SysUnsafe.As<T, byte>(ref enumValue), (uint)Math.Min(4, SysUnsafe.SizeOf<T>()));
            return intValue;
        }

        public static bool EnumEquals<T>(T lhs, T rhs) where T : struct, IConvertible => lhs.ToInt64(null) == rhs.ToInt64(null);

        internal static Span<byte> GetByteSpanFromArray(Array array, int elementSize)
        {
            if (array == null || array.Length == 0) return default;
            return new Span<byte>(SysUnsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(array)), array.Length * elementSize);
        }

        internal static Span<byte> GetByteSpanFromList<T>(List<T> list) where T : struct =>
            MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(list));

        internal static void LogError(string msg, string filename, int linenumber) => UnityEngine.Debug.LogError(msg);
    }
}
