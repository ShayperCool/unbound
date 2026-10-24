using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;

namespace Unity.Collections.LowLevel.Unsafe
{
    /// <summary>
    /// Reports containers garbage collected without Dispose (the leak detection of containers that
    /// still create a sentinel), with the allocation's stack trace in EnabledWithStackTrace mode.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public sealed partial class DisposeSentinel
    {
        [StructLayout(LayoutKind.Sequential, Size = 1)]
        internal partial struct Dummy
        {
            public partial class Class
            {
            }

            public static implicit operator Dummy(Class value) => default;
        }

        int m_IsCreated;
        StackTrace m_StackTrace;

        DisposeSentinel()
        {
        }

        public static void Dispose(ref AtomicSafetyHandle safety, ref DisposeSentinel sentinel)
        {
            AtomicSafetyHandle.CheckDeallocateAndThrow(safety);
            if (AtomicSafetyHandle.IsTempMemoryHandle(safety))
            {
                int staticSafetyId = safety.staticSafetyId;
                safety = AtomicSafetyHandle.GetTempMemoryHandle();
                safety.staticSafetyId = staticSafetyId;
            }
            AtomicSafetyHandle.Release(safety);
            Clear(ref sentinel);
        }

        public static void Create(out AtomicSafetyHandle safety, out DisposeSentinel sentinel, int callSiteStackDepth, Allocator allocator)
        {
            safety = allocator == Allocator.Temp ? AtomicSafetyHandle.GetTempMemoryHandle() : AtomicSafetyHandle.Create();
            sentinel = null;
            if (allocator != Allocator.Temp && allocator != Allocator.AudioKernel)
            {
                if (JobsUtility.IsExecutingJob) throw new InvalidOperationException("Jobs can only create Temp memory");
                CreateInternal(ref sentinel, callSiteStackDepth);
            }
        }

        [BurstDiscard]
        static void CreateInternal(ref DisposeSentinel sentinel, int callSiteStackDepth)
        {
            var mode = NativeLeakDetection.Mode;
            if (mode == NativeLeakDetectionMode.Disabled) return;
            sentinel = new DisposeSentinel
            {
                m_StackTrace = mode == NativeLeakDetectionMode.EnabledWithStackTrace ? new StackTrace(callSiteStackDepth + 2, true) : null,
                m_IsCreated = 1,
            };
        }

        ~DisposeSentinel()
        {
            if (m_IsCreated == 0) return;
            string filename = "";
            int line = 0;
            if (m_StackTrace != null)
            {
                var text = m_StackTrace.ToString();
                if (m_StackTrace.FrameCount != 0)
                {
                    filename = m_StackTrace.GetFrame(0).GetFileName();
                    line = m_StackTrace.GetFrame(0).GetFileLineNumber();
                }
                UnsafeUtility.LogError("A Native Collection has not been disposed, resulting in a memory leak. Allocated from:\n" + text, filename, line);
            }
            else
            {
                UnsafeUtility.LogError("A Native Collection has not been disposed, resulting in a memory leak. Enable Full StackTraces to get more details. Leak tracking may be enabled via `Unity.Collections.NativeLeakDetection.Mode` or from the editor preferences menu Edit > Preferences > Jobs > Leak Detection Level.", filename, line);
            }
        }

        [BurstDiscard]
        public static void Clear(ref DisposeSentinel sentinel)
        {
            if (sentinel != null)
            {
                sentinel.m_IsCreated = 0;
                sentinel = null;
            }
        }

        internal static void Create(out AtomicSafetyHandle safety, out Dummy sentinel, int callSiteStackDepth, Allocator allocator)
        {
            safety = allocator == Allocator.Temp ? AtomicSafetyHandle.GetTempMemoryHandle() : AtomicSafetyHandle.Create();
            sentinel = default;
            if (allocator != Allocator.Temp && allocator != Allocator.AudioKernel && JobsUtility.IsExecutingJob)
                throw new InvalidOperationException("Jobs can only create Temp memory");
        }

        internal static void Clear(ref Dummy sentinel) => sentinel = default;
    }
}
