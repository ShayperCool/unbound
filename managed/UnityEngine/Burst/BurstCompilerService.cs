using System;
using System.Collections.Generic;
using Conditional = System.Diagnostics.ConditionalAttribute;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Unity.Burst.LowLevel;
using UnityEngine;

namespace Unity.Burst.LowLevel
{
    /// <summary>
    /// The engine side of com.unity.burst. Unbound has no Burst compiler: it answers as the Editor
    /// does with Burst compilation disabled (Unbound starts games with
    /// UNITY_BURST_DISABLE_COMPILATION=1, so com.unity.burst never initializes the JIT and runs
    /// Burst code as managed code). Shared statics, logging and execution modes work as in Unity.
    /// </summary>
    internal static unsafe partial class BurstCompilerService
    {
        public unsafe delegate bool ExtractCompilerFlags(Type jobType, out string flags);

        public enum BurstLogType
        {
            Info = 0,
            Warning = 1,
            Error = 2,
        }

        /// <summary>Whether the JIT compiler was loaded: never, in Unbound.</summary>
        public static bool IsInitialized => false;

        public static void Initialize(string folderRuntime, ExtractCompilerFlags extractCompilerFlags)
        {
            if (folderRuntime == null) throw new ArgumentNullException(nameof(folderRuntime));
            if (extractCompilerFlags == null) throw new ArgumentNullException(nameof(extractCompilerFlags));
            if (!Directory.Exists(folderRuntime))
            {
                Debug.LogError("Unable to initialize the burst JIT compiler. The folder `" + folderRuntime + "` does not exist");
                return;
            }
            Debug.LogError("Unexpected error while trying to initialize the burst JIT compiler: Unbound has no Burst compiler and runs Burst code as managed code; " +
                "start it with UNITY_BURST_DISABLE_COMPILATION unset or 1 (UNITY_BURST_DISABLE_COMPILATION=0 asks for a compiler that is not there)");
        }

        /// <summary>Compiler commands and disassembly: nothing without a compiler.</summary>
        public static string GetDisassembly(MethodInfo m, string compilerOptions) => "";

        /// <summary>Compiling is refused: <see cref="GetAsyncCompiledAsyncDelegateMethod"/> returns null
        /// for the id, so callers fall back to the managed method.</summary>
        public static int CompileAsyncDelegateMethod(object delegateMethod, string compilerOptions) => -1;

        public static void* GetAsyncCompiledAsyncDelegateMethod(int userID) => null;

        public static bool LoadBurstLibrary(string fullPathToLibBurstGenerated) => false;

        public static string GetMethodSignature(MethodInfo method)
        {
            if (method == null) throw new ArgumentNullException(nameof(method));
            var text = new StringBuilder();
            text.Append(method.DeclaringType?.FullName).Append("::").Append(method.Name).Append('(');
            var parameters = method.GetParameters();
            for (int i = 0; i < parameters.Length; i++)
            {
                if (i > 0) text.Append(", ");
                text.Append(parameters[i].ParameterType.FullName);
            }
            return text.Append(')').ToString();
        }

        static readonly object s_SharedLock = new object();
        static readonly Dictionary<Hash128, (IntPtr Memory, uint Size)> s_Shared = new Dictionary<Hash128, (IntPtr, uint)>();

        /// <summary>Process-wide memory for a SharedStatic, zeroed when created; the same key with
        /// another size gets null (the caller reports it).</summary>
        public static void* GetOrCreateSharedMemory(ref Hash128 key, uint size_of, uint alignment)
        {
            lock (s_SharedLock)
            {
                if (s_Shared.TryGetValue(key, out var existing))
                    return existing.Size == size_of ? (void*)existing.Memory : null;
                nuint align = Math.Max(alignment, 4u);
                if ((align & (align - 1)) != 0) align = (nuint)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)align);
                var memory = NativeMemory.AlignedAlloc(Math.Max(size_of, 1u), align);
                NativeMemory.Clear(memory, Math.Max(size_of, 1u));
                s_Shared[key] = ((IntPtr)memory, size_of);
                return memory;
            }
        }

        static uint s_ExecutionMode;

        public static void SetCurrentExecutionMode(uint environment) => s_ExecutionMode = environment;

        public static uint GetCurrentExecutionMode() => s_ExecutionMode;

        /// <summary>Burst's log callback (BurstRuntime.Log, compiler messages).</summary>
        public static void Log(void* userData, BurstLogType logType, byte* message, byte* filename, int lineNumber) =>
            Write(logType, message);

        /// <summary>Logging from Burst code at runtime (Debug.Log in a Burst job).</summary>
        public static void RuntimeLog(void* userData, BurstLogType logType, byte* message, byte* filename, int lineNumber) =>
            Write(logType, message);

        static void Write(BurstLogType logType, byte* message)
        {
            var text = message == null ? "" : Marshal.PtrToStringUTF8((IntPtr)message);
            switch (logType)
            {
                case BurstLogType.Warning: Debug.LogWarning(text); break;
                case BurstLogType.Error: Debug.LogError(text); break;
                default: Debug.Log(text); break;
            }
        }
    }
}

namespace Unity.Collections.LowLevel.Unsafe
{
    /// <summary>The SharedStatic Unity's own modules use (job reflection data), on Burst's memory.</summary>
    internal static partial class BurstLike
    {
        internal readonly unsafe partial struct SharedStatic<T> where T : unmanaged
        {
            readonly void* _buffer;

            SharedStatic(void* buffer)
            {
                _buffer = buffer;
            }

            public ref T Data => ref UnsafeUtility.AsRef<T>(_buffer);

            public void* UnsafeDataPointer => _buffer;

            public static SharedStatic<T> GetOrCreate<TContext>(uint alignment = 0U) =>
                new SharedStatic<T>(SharedStatic.GetOrCreateSharedStaticInternal(BurstRuntime.GetHashCode64<TContext>(), 0L, (uint)UnsafeUtility.SizeOf<T>(), alignment));

            public static SharedStatic<T> GetOrCreate<TContext, TSubContext>(uint alignment = 0U) =>
                new SharedStatic<T>(SharedStatic.GetOrCreateSharedStaticInternal(BurstRuntime.GetHashCode64<TContext>(), BurstRuntime.GetHashCode64<TSubContext>(), (uint)UnsafeUtility.SizeOf<T>(), alignment));
        }

        internal static unsafe partial class SharedStatic
        {
            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            static void CheckSizeOf(uint sizeOf)
            {
                if (sizeOf == 0) throw new ArgumentException("sizeOf must be > 0", nameof(sizeOf));
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            static void CheckResult(void* result)
            {
                if (result == null)
                    throw new InvalidOperationException("Unable to create a SharedStatic for this key. This is most likely due to the size of the struct inside of the SharedStatic having changed or the same key being reused for differently sized values. To fix this the editor needs to be restarted.");
            }

            public static void* GetOrCreateSharedStaticInternal(long getHashCode64, long getSubHashCode64, uint sizeOf, uint alignment)
            {
                CheckSizeOf(sizeOf);
                var key = new Hash128((ulong)getHashCode64, (ulong)getSubHashCode64);
                void* memory = BurstCompilerService.GetOrCreateSharedMemory(ref key, sizeOf, alignment == 0 ? 4u : alignment);
                CheckResult(memory);
                return memory;
            }
        }

        /// <summary>Calls a native function pointer taking two pointers (Baselib callbacks).</summary>
        internal static int NativeFunctionCall_Int_IntPtr_IntPtr(IntPtr function, IntPtr p0, IntPtr p1, out int error)
        {
            error = 0;
            unsafe
            {
                return ((delegate* unmanaged<IntPtr, IntPtr, int>)function)(p0, p1);
            }
        }
    }

    internal static partial class BurstRuntime
    {
        struct HashCode64<T>
        {
            public static readonly long Value = HashStringWithFNV1A64(typeof(T).AssemblyQualifiedName);
        }

        public static long GetHashCode64<T>() => HashCode64<T>.Value;

        internal static long HashStringWithFNV1A64(string text)
        {
            ulong hash = 14695981039346656037UL;
            foreach (char c in text)
            {
                hash = 1099511628211UL * (hash ^ (byte)(c & 0xFF));
                hash = 1099511628211UL * (hash ^ (byte)(c >> 8));
            }
            return (long)hash;
        }
    }
}
