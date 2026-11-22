using System;
using UnityEditor.VFX;

namespace UnityEngine.VFX
{
    /// <summary>
    /// The temporary buffers a task reads or writes (an output's last frames, for motion
    /// vectors): per mapping, the buffer of the effect's ring of the asset's descriptor
    /// <c>mapping.index</c> that is <c>pastFrameIndex</c> frames back, the ring kept per camera
    /// when the mapping asks for it (<see cref="VisualEffect.TemporaryBuffer"/>). A buffer is
    /// bound again only once it changed.
    /// </summary>
    sealed class VFXTemporaryBindings
    {
        readonly int[] m_Names;
        readonly int[] m_Descs;
        readonly uint[] m_Past;
        readonly bool[] m_PerCamera;
        readonly GraphicsBuffer[] m_Bound;

        internal VFXTemporaryBindings(in VFXEditorTaskDesc task)
        {
            var mappings = task.temporaryBuffers ?? Array.Empty<VFXMappingTemporary>();
            Count = mappings.Length;
            m_Names = new int[Count];
            m_Descs = new int[Count];
            m_Past = new uint[Count];
            m_PerCamera = new bool[Count];
            m_Bound = new GraphicsBuffer[Count];
            for (int i = 0; i < Count; i++)
            {
                m_Names[i] = Shader.PropertyToID(mappings[i].mapping.name);
                m_Descs[i] = mappings[i].mapping.index;
                m_Past[i] = mappings[i].pastFrameIndex;
                m_PerCamera[i] = mappings[i].perCameraBuffer;
            }
        }

        internal int Count { get; }

        /// <summary>Whether the two name a buffer of one descriptor (one task writes what the
        /// other reads).</summary>
        internal bool Shares(VFXTemporaryBindings other)
        {
            foreach (int desc in m_Descs)
                if (Array.IndexOf(other.m_Descs, desc) >= 0) return true;
            return false;
        }

        /// <summary>The key of a camera's own temporary buffers.</summary>
        internal static int CameraKey(Camera camera) => camera != null ? camera.GetInstanceID() : 0;

        /// <summary>Mapping <paramref name="i"/>'s buffer for the effect now (and
        /// <paramref name="cameraKey"/>'s camera): true when it is not the one bound last.</summary>
        internal bool Changed(VisualEffect effect, int cameraKey, int i, out int name, out GraphicsBuffer buffer)
        {
            name = m_Names[i];
            buffer = effect.TemporaryBuffer(m_Descs[i], m_PerCamera[i] ? cameraKey : 0, m_Past[i]);
            if (buffer == null || ReferenceEquals(buffer, m_Bound[i])) return false;
            m_Bound[i] = buffer;
            return true;
        }
    }
}
