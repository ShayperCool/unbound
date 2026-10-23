using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Unity.Profiling.LowLevel.Unsafe
{
    /// <summary>
    /// The profiler's low-level API. Unbound has no profiler: markers do nothing, but counter values
    /// get real memory, since ProfilerCounterValue writes its value through the returned pointer.
    /// </summary>
    public static unsafe partial class ProfilerUnsafeUtility
    {
        static readonly Dictionary<string, IntPtr> s_Counters = new Dictionary<string, IntPtr>();

        public static void* CreateCounterValue(out IntPtr counterPtr, string name, ushort categoryId, MarkerFlags flags, byte dataType, byte dataUnit, int dataSize, ProfilerCounterOptions counterOptions)
        {
            // The same counter name shares its value, as in Unity.
            lock (s_Counters)
            {
                var key = categoryId + "/" + name;
                if (!s_Counters.TryGetValue(key, out var memory))
                {
                    memory = (IntPtr)NativeMemory.AllocZeroed((nuint)Math.Max(dataSize, 8));
                    s_Counters[key] = memory;
                }
                counterPtr = memory;
                return (void*)memory;
            }
        }

        public static void* CreateCounterValue(out IntPtr counterPtr, char* name, int nameLen, ushort categoryId, MarkerFlags flags, byte dataType, byte dataUnit, int dataSize, ProfilerCounterOptions counterOptions) =>
            CreateCounterValue(out counterPtr, new string(name, 0, nameLen), categoryId, flags, dataType, dataUnit, dataSize, counterOptions);

        public static void FlushCounterValue(void* counterValuePtr) { }
    }
}
