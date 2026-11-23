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
            AtomicSafetyHandle m_Safety;

            internal BindposeViews(Matrix4x4[] storage)
            {
                if (storage != null) m_Pin = GCHandle.Alloc(storage, GCHandleType.Pinned);
                m_Safety = AtomicSafetyHandle.Create();
                AtomicSafetyHandle.SetAllowSecondaryVersionWriting(m_Safety, false);
            }

            ~BindposeViews() => MainThreadContext.Instance.Post(static owner => ((BindposeViews)owner).Release(), this);

            internal unsafe NativeArray<Matrix4x4> Borrow(int count)
            {
                void* pointer = m_Pin.IsAllocated ? (void*)m_Pin.AddrOfPinnedObject() : null;
                var view = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<Matrix4x4>(pointer, count, Allocator.None);
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

        internal bool HasBindposes { get; private set; }
        internal int BindposeVersion { get; private set; }
        internal ArraySegment<Matrix4x4> BindposeData => m_BindposeStorage == null ? default :
            new ArraySegment<Matrix4x4>(m_BindposeStorage, 0, m_BindposeCount);

        public Matrix4x4[] bindposes
        {
            get
            {
                MainThreadCheck.Check("get_bindposes");
                EnsureModelLoaded();
                return BindposeData.AsSpan().ToArray();
            }
            set
            {
                MainThreadCheck.Check("set_bindposes");
                EnsureModelLoaded();
                StoreBindposes(value, value != null);
            }
        }

        public int bindposeCount
        {
            get
            {
                MainThreadCheck.Check("get_bindposeCount");
                EnsureModelLoaded();
                return m_BindposeCount;
            }
        }

        public void GetBindposes(List<Matrix4x4> bindposes)
        {
            if (bindposes == null) throw new ArgumentNullException(nameof(bindposes), "The result bindposes list cannot be null.");
            MainThreadCheck.Check("get_bindposeCount");
            EnsureModelLoaded();
            CollectionsMarshal.SetCount(bindposes, m_BindposeCount);
            BindposeData.AsSpan().CopyTo(CollectionsMarshal.AsSpan(bindposes));
        }

        public NativeArray<Matrix4x4> GetBindposes()
        {
            MainThreadCheck.Check("GetBindposesArray");
            ThrowIfDestroyed();
            EnsureModelLoaded();
            ThrowIfDestroyed();
            return (m_BindposeViews ??= new BindposeViews(m_BindposeStorage)).Borrow(m_BindposeCount);
        }

        public void SetBindposes(NativeArray<Matrix4x4> poses)
        {
            if (!poses.IsCreated || poses.Length == 0)
                throw new ArgumentException("Cannot set bindposes as the native poses array is empty.", nameof(poses));
            var data = poses.AsReadOnlySpan();
            MainThreadCheck.Check("SetBindposesFromScript_NativeArray");
            ThrowIfDestroyed();
            if (m_DeferredModel)
            {
                EnsureModelLoaded();
                // Loading can report an error to a callback that changes the source.
                data = poses.AsReadOnlySpan();
                ThrowIfDestroyed();
            }
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
