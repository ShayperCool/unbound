using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine.Rendering;

namespace UnityEngine
{
    /// <summary>
    /// Light probe interpolation usable from jobs: the current probes as they were when the query
    /// was created (a later change of LightProbes.bakedProbes or a new tetrahedralization does not
    /// reach it: measured, tests/projects/lightprobes). A tetrahedron index is the hint the walk
    /// starts from and the cell found: any index out of range starts at the first cell, and a
    /// position outside the probes' hull gets the ambient probe (as at creation), occlusion 1 and
    /// index -1.
    /// </summary>
    public partial struct LightProbesQuery : IDisposable
    {
        internal IntPtr m_LightProbeContextWrapper;
        internal Allocator m_AllocatorLabel;
        internal AtomicSafetyHandle m_Safety;

        sealed class Context
        {
            internal ProbeSet Probes;
            internal SphericalHarmonicsL2 Ambient;
        }

        public LightProbesQuery(Allocator allocator)
        {
            var context = new Context
            {
                Probes = (LightmapSettings.lightProbes?.m_Set ?? ProbeSet.Empty).Snapshot(),
                Ambient = RenderSettings.ambientProbe,
            };
            m_LightProbeContextWrapper = GCHandle.ToIntPtr(GCHandle.Alloc(context));
            m_AllocatorLabel = allocator;
            m_Safety = AtomicSafetyHandle.Create();
        }

        public bool IsCreated => m_LightProbeContextWrapper != IntPtr.Zero;

        Context Get()
        {
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
            return (Context)GCHandle.FromIntPtr(m_LightProbeContextWrapper).Target;
        }

        public void Dispose()
        {
            if (m_LightProbeContextWrapper == IntPtr.Zero) throw new ObjectDisposedException("The LightProbesQuery is already disposed.");
            AtomicSafetyHandle.CheckDeallocateAndThrow(m_Safety);
            AtomicSafetyHandle.Release(m_Safety);
            GCHandle.FromIntPtr(m_LightProbeContextWrapper).Free();
            m_LightProbeContextWrapper = IntPtr.Zero;
        }

        /// <summary>Disposes once <paramref name="inputDeps"/> completed.</summary>
        public JobHandle Dispose(JobHandle inputDeps)
        {
            if (m_LightProbeContextWrapper == IntPtr.Zero) throw new ObjectDisposedException("The LightProbesQuery is already disposed.");
            inputDeps.Complete();
            Dispose();
            return inputDeps;
        }

        public void CalculateInterpolatedLightAndOcclusionProbe(Vector3 position, ref int tetrahedronIndex, out SphericalHarmonicsL2 lightProbe, out Vector4 occlusionProbe)
        {
            var context = Get();
            if (!context.Probes.Evaluate(position, ref tetrahedronIndex, out lightProbe, out occlusionProbe)) lightProbe = context.Ambient;
        }

        public void CalculateInterpolatedLightAndOcclusionProbes(NativeArray<Vector3> positions, NativeArray<int> tetrahedronIndices,
            NativeArray<SphericalHarmonicsL2> lightProbes, NativeArray<Vector4> occlusionProbes)
        {
            var context = Get();
            int count = positions.Length;
            if (!tetrahedronIndices.IsCreated || tetrahedronIndices.Length < count)
                throw new ArgumentException(nameof(tetrahedronIndices), "Argument tetrahedronIndices is null or has fewer elements than positions.");
            if (!lightProbes.IsCreated || lightProbes.Length < count)
                throw new ArgumentException(nameof(lightProbes), "Argument lightProbes is null or has fewer elements than positions.");
            if (!occlusionProbes.IsCreated || occlusionProbes.Length < count)
                throw new ArgumentException(nameof(occlusionProbes), "Argument occlusionProbes is null or has fewer elements than positions.");
            for (int i = 0; i < count; i++)
            {
                int index = tetrahedronIndices[i];
                if (!context.Probes.Evaluate(positions[i], ref index, out var sh, out var occlusion)) sh = context.Ambient;
                tetrahedronIndices[i] = index;
                lightProbes[i] = sh;
                occlusionProbes[i] = occlusion;
            }
        }
    }
}
