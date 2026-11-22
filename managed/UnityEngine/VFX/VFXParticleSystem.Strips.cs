using System;
using UnityEngine;

namespace Unbound.Runtime
{
    /// <summary>
    /// Particle strips: a system whose particles are kept in strips of a fixed room, each a
    /// ring its particles enter at one end and leave at the other, drawn joined from one to
    /// the next. The kernels the graph compiled to keep the strips' state in a buffer of the
    /// system (the count of all the strips' particles, then five words for each strip: its
    /// first particle, how many it has, that count again as the initialize kernel reads it, and
    /// the least and greatest place of an alive particle the update kernel found); after a
    /// step's update kernels the package's own kernel (Shaders/UpdateStrips.compute) turns what
    /// they found into each strip's new first particle and count, and adds the counts up.
    ///
    /// A strip system has no list of dead particles: a new particle takes the next place of
    /// the strip it is spawned into (given by the initialize context, or the place of the
    /// particle that spawned it by a GPU event) and is dropped when that strip is full. Its
    /// alive count is the buffer's first word. Strips whose particles never die need none of
    /// this after the initialize kernel: their update kernel looks for no alive particle, and
    /// the package's kernel is not run for them (it would find every strip empty). Matches the
    /// Unity 6000.0.71f1 Editor (tests/projects/vfxstrips).
    /// </summary>
    internal sealed partial class VFXParticleSystem
    {
        const string StripDataName = "stripDataBuffer", StripCountName = "stripCount", ParticlePerStripName = "particlePerStripCount";
        const string StripKernelName = "UpdateParticleStrip";
        const string WithoutStripShader = "particle strips are not simulated";

        static readonly int s_StripData = Shader.PropertyToID(StripDataName);
        static readonly int s_StripCount = Shader.PropertyToID(StripCountName);
        static readonly int s_ParticlePerStrip = Shader.PropertyToID(ParticlePerStripName);
        static readonly int s_StripIndirection = Shader.PropertyToID("instancingIndirectAndActiveIndirect");
        static readonly int s_StripDispatchWidth = Shader.PropertyToID("dispatchWidth");
        static readonly int s_StripInstancing = Shader.PropertyToID("instancingConstants");

        /// <summary>The strips' state, and its size: how many strips, of how many particles.</summary>
        int m_StripData = -1;
        uint m_StripCount, m_ParticlePerStrip;
        ComputeShader m_StripShader;
        int m_StripKernel;
        GraphicsBuffer m_StripBound, m_StripIndirectionBound;
        uint[] m_NoStrips;

        /// <summary>Why a system's strips are not run, null when they are (or it has none): a
        /// strip system as the graph compiler writes it has the strips' buffer and sizes. A
        /// system whose particles die and spawn strips by GPU events keeps a dead particle (not
        /// drawn) until the package's strip kernel finds the strip it spawned empty: its update
        /// kernel reads that strip system's buffer (`attachedStripDataBuffer`, bound as its
        /// task names it), and its alive count includes such particles (tests/projects/vfxstrips
        /// `trails alive`; a strip whose particles never die is never found empty, `trails alive
        /// keep`).</summary>
        internal static string StripRefusal(in UnityEditor.VFX.VFXEditorSystemDesc system)
        {
            if ((system.flags & UnityEngine.VFX.VFXSystemFlag.SystemHasStrips) == 0) return null;
            if (Value(system.buffers, StripDataName) < 0 || Value(system.values, StripCountName) <= 0 || Value(system.values, ParticlePerStripName) <= 0)
                return $"the strip system '{system.name}' without its strips' buffer or sizes";
            return null;
        }

        void SetUpStrips()
        {
            if (!HasStrips) return;
            m_StripData = Value(Desc.buffers, StripDataName);
            // The system's values of these names hold the numbers themselves.
            m_StripCount = (uint)Math.Max(0, Value(Desc.values, StripCountName));
            m_ParticlePerStrip = (uint)Math.Max(0, Value(Desc.values, ParticlePerStripName));
        }

        /// <summary>The segments a strip output draws: one less than its room for each strip,
        /// from each particle to the next (the vertex program leaves out those past a strip's
        /// last particle).</summary>
        internal uint StripSegments => m_ParticlePerStrip == 0 ? 0 : (m_ParticlePerStrip - 1) * m_StripCount;

        /// <summary>The strips of an effect that starts: every word zero.</summary>
        void ResetStrips()
        {
            if (m_StripData < 0) return;
            var buffer = Effect.GpuBuffer(m_StripData, 1);
            buffer.SetData(m_NoStrips ??= new uint[buffer.count]);
        }

        /// <summary>Whether the package's strip kernel is there and compiles (asked once, with
        /// the system's own kernels): of strips whose particles die.</summary>
        bool StripKernelUsable()
        {
            if (!HasStrips || !HasKill || m_StripData < 0) return true;
            m_StripShader = VFXPackageShaders.Load(VFXPackageShaders.UpdateStrips, WithoutStripShader);
            if (m_StripShader == null) return false;
            try
            {
                m_StripKernel = m_StripShader.FindKernel(StripKernelName);
                m_StripShader.GetKernelThreadGroupSizes(m_StripKernel, out _, out _, out _);
            }
            catch (Exception e)
            {
                Debug.LogError($"Unbound: VisualEffect '{Effect.name}': the strip kernel of '{Desc.name}' does not compile ({e.Message}); the system does not simulate", Effect);
                return false;
            }
            // One instance, active, in a batch of one; the strips' sizes.
            float one = BitConverter.Int32BitsToSingle(1);
            m_StripShader.SetVector(s_StripInstancing, new Vector4(one, one, one, 0f));
            m_StripShader.SetInt(s_StripCount, (int)m_StripCount);
            m_StripShader.SetInt(s_ParticlePerStrip, (int)m_ParticlePerStrip);
            ThreadGroups(m_StripCount, out int x, out _);
            m_StripShader.SetInt(s_StripDispatchWidth, x);
            return true;
        }

        /// <summary>After a step's update kernels: each strip's first particle and count from
        /// what they found alive, and the count of all.</summary>
        void UpdateStrips()
        {
            if (m_StripShader == null || !m_HasRenderer || m_StripCount == 0) return;
            var data = Effect.GpuBuffer(m_StripData, 0);
            if (!ReferenceEquals(data, m_StripBound)) m_StripShader.SetBuffer(m_StripKernel, s_StripData, m_StripBound = data);
            if (m_Indirection >= 0)
            {
                var indirection = Effect.GpuBuffer(m_Indirection, 2);
                if (!ReferenceEquals(indirection, m_StripIndirectionBound)) m_StripShader.SetBuffer(m_StripKernel, s_StripIndirection, m_StripIndirectionBound = indirection);
            }
            ThreadGroups(m_StripCount, out int x, out int y);
            m_StripShader.Dispatch(m_StripKernel, x, y, 1);
        }

        void ReleaseStrips()
        {
            if (m_StripShader != null) UnityEngine.Object.Destroy(m_StripShader);
            m_StripShader = null;
        }
    }
}
