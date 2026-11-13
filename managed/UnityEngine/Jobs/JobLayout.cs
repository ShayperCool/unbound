using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unbound.Runtime.Jobs
{
    /// <summary>How a job uses a container: the access bits of its field's attributes.</summary>
    internal static class ContainerAccess
    {
        internal const int Read = 1;
        internal const int Write = 2;
        internal const int ReadWrite = Read | Write;
    }

    /// <summary>
    /// What the job system reads from a job struct's layout before scheduling it, as Unity's native
    /// scanner does: the native containers it holds (their safety handles, access, parallel-for
    /// index restriction, deallocation on completion), thread index fields, class fields to null,
    /// and the fields that make it invalid (references, unsafe pointers).
    /// </summary>
    internal sealed class JobLayout
    {
        internal sealed class Container
        {
            /// <summary>Path of the field for messages: <c>Job.Field.Inner</c>.</summary>
            public string Path;
            public int Access;
            /// <summary>Offsets of the container's safety handles in the job data.</summary>
            public readonly List<int> Handles = new List<int>();
            /// <summary>m_Length, m_MinIndex and m_MaxIndex offsets when the container restricts a
            /// parallel-for job to its range, else -1.</summary>
            public int LengthOffset = -1, MinOffset = -1, MaxOffset = -1;
            public bool SupportsMinMax;
            public bool AtomicWriteOnly;
            public bool DisableParallelForRestriction;
            public bool DeallocateOnCompletion;
            /// <summary>An array that may be a deferred view of a list (NativeList.AsDeferredJobArray):
            /// its buffer is patched to the list's when the job starts.</summary>
            public bool DeferredConvert;
            /// <summary>m_Buffer and m_AllocatorLabel offsets, for deallocation on completion.</summary>
            public int BufferOffset = -1, AllocatorOffset = -1;
        }

        public readonly List<Container> Containers = new List<Container>();
        /// <summary>Containers with [NativeDisableContainerSafetyRestriction]: the job's copies of
        /// their handles allow everything, and the job is not registered with them.</summary>
        public readonly List<Container> Unchecked = new List<Container>();
        public readonly List<int> ThreadIndexOffsets = new List<int>();
        public readonly List<int> NullOnSchedule = new List<int>();
        /// <summary>The first reason the job may not be scheduled, or null.</summary>
        public string Error;

        static readonly ConcurrentDictionary<Type, JobLayout> s_Layouts = new ConcurrentDictionary<Type, JobLayout>();

        internal static JobLayout For(Type jobType) => s_Layouts.GetOrAdd(jobType, t =>
        {
            var layout = new JobLayout();
            layout.Scan(t, 0, t.Name, 0, Inherited.None);
            return layout;
        });

        const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>The attributes of an enclosing plain struct field, which apply to the pointers
        /// and containers of the struct it holds as to its own fields (measured: the GPU Resident
        /// Drawer's jobs, with an UnsafeAtomicCounter32 field marked
        /// [NativeDisableUnsafePtrRestriction] and struct fields of containers marked
        /// [NativeDisableParallelForRestriction] or [NativeDisableContainerSafetyRestriction], run
        /// in the Unity 6000.0.71f1 Editor, tests/projects/residentdrawer).</summary>
        [Flags]
        enum Inherited
        {
            None = 0,
            UnsafePointers = 1,
            ParallelFor = 2,
            Safety = 4,
            ReadOnly = 8,
            WriteOnly = 16,
        }

        static Inherited Attributes(FieldInfo field, Inherited inherited)
        {
            if (field.IsDefined(typeof(NativeDisableUnsafePtrRestrictionAttribute), false)) inherited |= Inherited.UnsafePointers;
            if (field.IsDefined(typeof(NativeDisableParallelForRestrictionAttribute), false)) inherited |= Inherited.ParallelFor;
            if (field.IsDefined(typeof(NativeDisableContainerSafetyRestrictionAttribute), false)) inherited |= Inherited.Safety;
            if (field.IsDefined(typeof(ReadOnlyAttribute), false)) inherited |= Inherited.ReadOnly;
            if (field.IsDefined(typeof(WriteOnlyAttribute), false)) inherited |= Inherited.WriteOnly;
            return inherited;
        }

        void Scan(Type type, int baseOffset, string path, int depth, Inherited inherited)
        {
            if (depth > 16) return;
            foreach (var field in type.GetFields(Instance))
            {
                var fieldType = field.FieldType;
                var fieldPath = path + "." + field.Name;
                int offset = baseOffset + UnsafeUtility.GetFieldOffset(field);
                var attributes = Attributes(field, inherited);
                if (fieldType.IsPointer || fieldType.IsFunctionPointer)
                {
                    if ((attributes & Inherited.UnsafePointers) == 0)
                        Error ??= $"{fieldPath} uses unsafe Pointers which is not allowed. Unsafe Pointers can lead to crashes and no safety against race conditions can be provided.\nIf you really need to use unsafe pointers, you can disable this check using [NativeDisableUnsafePtrRestriction].";
                    continue;
                }
                if (!fieldType.IsValueType)
                {
                    if (field.IsDefined(typeof(NativeSetClassTypeToNullOnScheduleAttribute), false)) NullOnSchedule.Add(offset);
                    else Error ??= $"{fieldPath} is not a value type. Job structs may not contain any reference types.";
                    continue;
                }
                if (fieldType.IsPrimitive || fieldType.IsEnum)
                {
                    if (field.IsDefined(typeof(NativeSetThreadIndexAttribute), false) && fieldType == typeof(int)) ThreadIndexOffsets.Add(offset);
                    continue;
                }
                if (fieldType.IsDefined(typeof(NativeContainerAttribute), false))
                {
                    if ((attributes & Inherited.Safety) != 0)
                    {
                        var free = new Container { Path = fieldPath, Access = ContainerAccess.ReadWrite };
                        // A deferred view of a list still takes the list's buffer when the job starts.
                        Deferred(free, fieldType, offset);
                        ScanInsideContainer(fieldType, offset, fieldPath, free, depth + 1);
                        Unchecked.Add(free);
                        continue;
                    }
                    var container = new Container { Path = fieldPath };
                    bool readOnly = (attributes & Inherited.ReadOnly) != 0 || fieldType.IsDefined(typeof(NativeContainerIsReadOnlyAttribute), false);
                    bool writeOnly = (attributes & Inherited.WriteOnly) != 0;
                    container.AtomicWriteOnly = fieldType.IsDefined(typeof(NativeContainerIsAtomicWriteOnlyAttribute), false);
                    container.Access = readOnly ? ContainerAccess.Read : writeOnly || container.AtomicWriteOnly ? ContainerAccess.Write : ContainerAccess.ReadWrite;
                    container.DisableParallelForRestriction = (attributes & Inherited.ParallelFor) != 0;
                    container.SupportsMinMax = fieldType.IsDefined(typeof(NativeContainerSupportsMinMaxWriteRestrictionAttribute), false);
                    if (container.SupportsMinMax)
                    {
                        container.LengthOffset = FieldOffset(fieldType, "m_Length", offset);
                        container.MinOffset = FieldOffset(fieldType, "m_MinIndex", offset);
                        container.MaxOffset = FieldOffset(fieldType, "m_MaxIndex", offset);
                    }
                    Deferred(container, fieldType, offset);
                    if (field.IsDefined(typeof(DeallocateOnJobCompletionAttribute), false))
                    {
                        if (!fieldType.IsDefined(typeof(NativeContainerSupportsDeallocateOnJobCompletionAttribute), false))
                            Error ??= $"{fieldPath} uses [DeallocateOnJobCompletion] but the native container does not support deallocating the memory from a job.";
                        container.DeallocateOnCompletion = true;
                        container.BufferOffset = FieldOffset(fieldType, "m_Buffer", offset);
                        container.AllocatorOffset = FieldOffset(fieldType, "m_AllocatorLabel", offset);
                    }
                    ScanInsideContainer(fieldType, offset, fieldPath, container, depth + 1);
                    Containers.Add(container);
                    continue;
                }
                if (fieldType == typeof(AtomicSafetyHandle)) continue;
                Scan(fieldType, offset, fieldPath, depth + 1, attributes);
            }
        }

        /// <summary>The safety handles and thread index fields of a container (and of the plain
        /// structs in it), and the unsafe pointers it would need to declare.</summary>
        void ScanInsideContainer(Type type, int baseOffset, string path, Container container, int depth)
        {
            if (depth > 16) return;
            foreach (var field in type.GetFields(Instance))
            {
                var fieldType = field.FieldType;
                int offset = baseOffset + UnsafeUtility.GetFieldOffset(field);
                if (fieldType == typeof(AtomicSafetyHandle))
                {
                    container?.Handles.Add(offset);
                    continue;
                }
                if (fieldType.IsPointer || fieldType.IsFunctionPointer)
                {
                    if (!field.IsDefined(typeof(NativeDisableUnsafePtrRestrictionAttribute), false))
                        Error ??= $"{path}.{field.Name} uses unsafe Pointers which is not allowed. Unsafe Pointers can lead to crashes and no safety against race conditions can be provided.\nIf you really need to use unsafe pointers, you can disable this check using [NativeDisableUnsafePtrRestriction].";
                    continue;
                }
                if (!fieldType.IsValueType)
                {
                    if (field.IsDefined(typeof(NativeSetClassTypeToNullOnScheduleAttribute), false)) NullOnSchedule.Add(offset);
                    else Error ??= $"{path}.{field.Name} is not a value type. Job structs may not contain any reference types.";
                    continue;
                }
                if (fieldType.IsPrimitive || fieldType.IsEnum)
                {
                    if (field.IsDefined(typeof(NativeSetThreadIndexAttribute), false) && fieldType == typeof(int)) ThreadIndexOffsets.Add(offset);
                    continue;
                }
                ScanInsideContainer(fieldType, offset, path + "." + field.Name, container, depth + 1);
            }
        }

        /// <summary>A container that may be a deferred view of a list (NativeList.AsDeferredJobArray):
        /// where its buffer, length and range are, to patch when the job starts.</summary>
        static void Deferred(Container container, Type type, int offset)
        {
            if (!type.IsDefined(typeof(NativeContainerSupportsDeferredConvertListToArray), false)) return;
            container.DeferredConvert = true;
            container.BufferOffset = FieldOffset(type, "m_Buffer", offset);
            container.LengthOffset = FieldOffset(type, "m_Length", offset);
            container.MinOffset = FieldOffset(type, "m_MinIndex", offset);
            container.MaxOffset = FieldOffset(type, "m_MaxIndex", offset);
        }

        static int FieldOffset(Type type, string name, int baseOffset)
        {
            var field = type.GetField(name, Instance);
            return field == null ? -1 : baseOffset + UnsafeUtility.GetFieldOffset(field);
        }
    }
}
