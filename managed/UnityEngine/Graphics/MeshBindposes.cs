using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unbound.Runtime.Jobs;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace UnityEngine
{
    public sealed partial class Mesh
    {
        Matrix4x4[] m_BindposeStorage;
        int m_BindposeCount;
        GCHandle m_BindposePin;
        AtomicSafetyHandle m_BindposeSafety;

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
            if (m_BindposeCount != 0) bindposes.AddRange(BindposeData);
        }

        public unsafe NativeArray<Matrix4x4> GetBindposes()
        {
            EnsureModelLoaded();
            if (m_BindposeStorage == null) return default;
            if (!m_BindposePin.IsAllocated)
            {
                m_BindposeSafety = AtomicSafetyHandle.Create();
                AtomicSafetyHandle.SetAllowSecondaryVersionWriting(m_BindposeSafety, false);
                m_BindposePin = GCHandle.Alloc(m_BindposeStorage, GCHandleType.Pinned);
            }
            var view = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<Matrix4x4>(
                (void*)m_BindposePin.AddrOfPinnedObject(), m_BindposeCount, Allocator.None);
            var safety = m_BindposeSafety;
            AtomicSafetyHandle.UseSecondaryVersion(ref safety);
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref view, safety);
            return view;
        }

        public void SetBindposes(NativeArray<Matrix4x4> poses)
        {
            if (poses.Length == 0)
                throw new ArgumentException("Cannot set bindposes as the native poses array is empty.", nameof(poses));
            EnsureModelLoaded();
            var data = poses.AsReadOnlySpan();
            if (data.Overlaps(m_BindposeStorage.AsSpan()))
            {
                // A log listener can change the mesh while reporting the alias.
                data = data.ToArray();
                Debug.LogAssertion("vector: assign data may not be data from the vector itself.");
            }
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
                CompleteBindposeReaders();
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

        void CompleteBindposeReaders()
        {
            if (!m_BindposePin.IsAllocated) return;
            if (JobSystem.IsExecutingJob)
                throw new InvalidOperationException("Mesh bind pose storage cannot be modified from a job.");
            AtomicSafetyHandle.EnforceAllBufferJobsHaveCompleted(m_BindposeSafety);
        }

        void EndBindposeViews()
        {
            if (!m_BindposePin.IsAllocated) return;
            CompleteBindposeReaders();
            AtomicSafetyHandle.Release(m_BindposeSafety);
            m_BindposeSafety = default;
            m_BindposePin.Free();
        }
    }
}
