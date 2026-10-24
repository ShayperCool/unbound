using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Unity.Collections;

namespace Unbound.Runtime
{
    /// <summary>
    /// Unity's native allocators for <see cref="Unity.Collections.LowLevel.Unsafe.UnsafeUtility"/>:
    /// aligned native memory; Allocator.Temp memory belongs to the current temp scope (the frame on
    /// the main thread, the execution of a job) and is freed when it ends; tracked allocations are
    /// what leak detection reports.
    /// </summary>
    internal static unsafe class Allocation
    {
        internal static NativeLeakDetectionMode LeakDetectionMode = NativeLeakDetectionMode.Enabled;

        /// <summary>The scope each live temp allocation belongs to.</summary>
        static readonly ConcurrentDictionary<IntPtr, Safety.TempScope> s_TempOwners = new ConcurrentDictionary<IntPtr, Safety.TempScope>();

        static readonly object s_LeakLock = new object();
        static readonly Dictionary<IntPtr, (Allocator Allocator, StackTrace Stack)> s_Tracked = new Dictionary<IntPtr, (Allocator, StackTrace)>();

        internal static void* Malloc(long size, int alignment, Allocator allocator, bool tracked, int callstacksToSkip)
        {
            if (allocator <= Allocator.None) throw new ArgumentException("Allocator must be Temp, TempJob or Persistent", nameof(allocator));
            if (size < 0) throw new ArgumentOutOfRangeException(nameof(size));
            nuint align = (nuint)Math.Max(alignment, 1);
            if ((align & (align - 1)) != 0) align = (nuint)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)align);
            var memory = NativeMemory.AlignedAlloc((nuint)Math.Max(size, 1), Math.Max(align, (nuint)sizeof(void*)));
            if (allocator == Allocator.Temp)
            {
                var scope = Safety.CurrentScope;
                scope.AddAllocation((IntPtr)memory);
                s_TempOwners[(IntPtr)memory] = scope;
            }
            else if (tracked && LeakDetectionMode != NativeLeakDetectionMode.Disabled)
            {
                var stack = LeakDetectionMode == NativeLeakDetectionMode.EnabledWithStackTrace ? new StackTrace(callstacksToSkip + 2, true) : null;
                lock (s_LeakLock) s_Tracked[(IntPtr)memory] = (allocator, stack);
            }
            return memory;
        }

        internal static void Free(void* memory, Allocator allocator)
        {
            if (memory == null) return;
            if (allocator <= Allocator.None) throw new ArgumentException("Allocator must be Temp, TempJob or Persistent", nameof(allocator));
            if (allocator == Allocator.Temp)
            {
                // Temp memory of an ended scope is gone already.
                if (!s_TempOwners.TryRemove((IntPtr)memory, out var scope)) return;
                scope.RemoveAllocation((IntPtr)memory);
            }
            else
            {
                lock (s_LeakLock) s_Tracked.Remove((IntPtr)memory);
            }
            NativeMemory.AlignedFree(memory);
        }

        /// <summary>Frees the temp memory a scope still holds.</summary>
        internal static void FreeScope(List<IntPtr> allocations)
        {
            foreach (var memory in allocations)
            {
                if (s_TempOwners.TryRemove(memory, out _)) NativeMemory.AlignedFree((void*)memory);
            }
        }

        internal static int LeakRecord(IntPtr handle, LeakCategory category, int callstacksToSkip)
        {
            if (LeakDetectionMode == NativeLeakDetectionMode.Disabled) return 0;
            var stack = LeakDetectionMode == NativeLeakDetectionMode.EnabledWithStackTrace ? new StackTrace(callstacksToSkip + 1, true) : null;
            lock (s_LeakLock) s_Tracked[handle] = (Allocator.Persistent, stack);
            return 1;
        }

        internal static int LeakErase(IntPtr handle)
        {
            lock (s_LeakLock) return s_Tracked.Remove(handle) ? 1 : 0;
        }

        /// <summary>The tracked allocations not freed; <paramref name="forgive"/> stops tracking them.</summary>
        internal static int CheckForLeaks(bool forgive)
        {
            lock (s_LeakLock)
            {
                int count = s_Tracked.Count;
                if (forgive) s_Tracked.Clear();
                return count;
            }
        }
    }
}
