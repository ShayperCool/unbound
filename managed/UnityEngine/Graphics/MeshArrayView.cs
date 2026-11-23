using System;
using System.Runtime.InteropServices;
using Unbound.Runtime;
using Unbound.Runtime.Jobs;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace UnityEngine
{
    /// <summary>Safety and pin for a mesh's read-only native arrays, created only on first borrow.</summary>
    internal sealed class MeshArrayView<T> where T : unmanaged
    {
        GCHandle m_Pin;
        AtomicSafetyHandle m_Safety;

        internal MeshArrayView(T[] data)
        {
            if (data != null) m_Pin = GCHandle.Alloc(data, GCHandleType.Pinned);
            m_Safety = AtomicSafetyHandle.Create();
            AtomicSafetyHandle.SetAllowSecondaryVersionWriting(m_Safety, false);
        }

        ~MeshArrayView() => MainThreadContext.Instance.Post(static owner => ((MeshArrayView<T>)owner).Release(), this);

        internal unsafe NativeArray<T> Borrow(int length)
        {
            void* pointer = m_Pin.IsAllocated ? (void*)m_Pin.AddrOfPinnedObject() : null;
            var view = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(pointer, length, Allocator.None);
            var safety = m_Safety;
            AtomicSafetyHandle.UseSecondaryVersion(ref safety);
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref view, safety);
            return view;
        }

        internal void CompleteReaders()
        {
            if (JobSystem.IsExecutingJob) throw new InvalidOperationException("Mesh storage cannot be modified from a job.");
            AtomicSafetyHandle.EnforceAllBufferJobsHaveCompleted(m_Safety);
        }

        internal void Release()
        {
            if (!AtomicSafetyHandle.IsDefaultValue(m_Safety))
            {
                CompleteReaders();
                AtomicSafetyHandle.Release(m_Safety);
                m_Safety = default;
            }
            if (m_Pin.IsAllocated) m_Pin.Free();
            GC.SuppressFinalize(this);
        }
    }
}
