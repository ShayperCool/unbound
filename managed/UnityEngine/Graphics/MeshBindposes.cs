using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unbound.Runtime;
using Unbound.Runtime.Jobs;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace UnityEngine
{
    public sealed partial class Mesh
    {
        Matrix4x4[] m_BindposeStorage;
        int m_BindposeCount;
        BindposeViews m_BindposeViews;

        // Mesh wrappers may be collected without an explicit Destroy. Only meshes that handed
        // out views need a finalizer, and pending readers finish on the engine thread.
        sealed class BindposeViews
        {
            GCHandle m_Pin;
            readonly AtomicSafetyHandle m_Safety;

            internal BindposeViews(Matrix4x4[] storage)
            {
                m_Pin = GCHandle.Alloc(storage, GCHandleType.Pinned);
                m_Safety = AtomicSafetyHandle.Create();
                AtomicSafetyHandle.SetAllowSecondaryVersionWriting(m_Safety, false);
            }

            ~BindposeViews() => MainThreadContext.Instance.Post(static owner => ((BindposeViews)owner).Release(), this);

            internal unsafe NativeArray<Matrix4x4> Borrow(int count)
            {
                var view = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<Matrix4x4>(
                    (void*)m_Pin.AddrOfPinnedObject(), count, Allocator.None);
                var safety = m_Safety;
                AtomicSafetyHandle.UseSecondaryVersion(ref safety);
                NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref view, safety);
                return view;
            }

            internal void CompleteReaders()
            {
                if (JobSystem.IsExecutingJob)
                    throw new InvalidOperationException("Mesh bind pose storage cannot be modified from a job.");
                AtomicSafetyHandle.EnforceAllBufferJobsHaveCompleted(m_Safety);
            }

            internal void Release()
            {
                if (!m_Pin.IsAllocated) return;
                CompleteReaders();
                if (!AtomicSafetyHandle.IsDefaultValue(m_Safety)) AtomicSafetyHandle.Release(m_Safety);
                m_Pin.Free();
                GC.SuppressFinalize(this);
            }
        }

        internal bool HasBindposes { get; private set; }
        internal int BindposeVersion { get; private set; }
        internal ArraySegment<Matrix4x4> BindposeData => m_BindposeStorage == null ? default :
            new ArraySegment<Matrix4x4>(m_BindposeStorage, 0, m_BindposeCount);

        public Matrix4x4[] bindposes
        {
            get { EnsureModelLoaded(); return BindposeData.AsSpan().ToArray(); }
            set { EnsureModelLoaded(); StoreBindposes(value, value != null); }
        }

        public int bindposeCount
        {
            get { EnsureModelLoaded(); return m_BindposeCount; }
        }

        public void GetBindposes(List<Matrix4x4> bindposes)
        {
            bindposes.Clear();
            EnsureModelLoaded();
            CollectionsMarshal.SetCount(bindposes, m_BindposeCount);
            BindposeData.AsSpan().CopyTo(CollectionsMarshal.AsSpan(bindposes));
        }

        public NativeArray<Matrix4x4> GetBindposes()
        {
            ThrowIfDestroyed();
            EnsureModelLoaded();
            ThrowIfDestroyed();
            if (m_BindposeStorage == null) return default;
            return (m_BindposeViews ??= new BindposeViews(m_BindposeStorage)).Borrow(m_BindposeCount);
        }

        public void SetBindposes(NativeArray<Matrix4x4> poses)
        {
            ThrowIfDestroyed();
            if (poses.Length == 0)
                throw new ArgumentException("Cannot set bindposes as the native poses array is empty.", nameof(poses));
            if (!poses.IsCreated) throw new ObjectDisposedException(nameof(poses));
            EnsureModelLoaded();
            var data = poses.AsReadOnlySpan();
            if (data.Overlaps(m_BindposeStorage.AsSpan()))
            {
                Debug.LogAssertion("vector: assign data may not be data from the vector itself.");
                // A listener can change or relocate the source while handling the assertion.
                data = poses.AsReadOnlySpan();
            }
            ThrowIfDestroyed();
            StoreBindposes(data, true);
        }

        void StoreBindposes(ReadOnlySpan<Matrix4x4> poses, bool hasBindposes)
        {
            if (poses.Length > (m_BindposeStorage?.Length ?? 0))
            {
                var storage = poses.ToArray();
                EndBindposeViews();
                m_BindposeStorage = storage;
            }
            else
            {
                m_BindposeViews?.CompleteReaders();
                poses.CopyTo(m_BindposeStorage);
            }
            m_BindposeCount = poses.Length;
            HasBindposes = hasBindposes;
            BindposeVersion++;
        }

        void CopyBindposesFrom(Mesh source) => StoreBindposes(source.BindposeData.AsSpan(), source.HasBindposes);

        internal void ResetBindposes()
        {
            EndBindposeViews();
            m_BindposeStorage = null;
            m_BindposeCount = 0;
            HasBindposes = false;
            BindposeVersion++;
        }

        void EndBindposeViews()
        {
            m_BindposeViews?.Release();
            m_BindposeViews = null;
        }
    }
}
