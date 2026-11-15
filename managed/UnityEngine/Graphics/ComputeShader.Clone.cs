using Unbound.Runtime;

namespace UnityEngine
{
    public sealed partial class ComputeShader
    {
        // Shader source is immutable; runtime bindings and parameter values start empty.
        internal Object CloneObject() => new ComputeShader
        {
            m_File = m_File,
            m_Kernels = (string[])m_Kernels.Clone(),
        };
    }
}
