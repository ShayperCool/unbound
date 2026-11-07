using Unbound.Runtime;

namespace UnityEngine
{
    public sealed partial class ComputeShader : Instantiation.ICloneable
    {
        // Shader source is immutable; runtime bindings and parameter values start empty.
        Object Instantiation.ICloneable.CloneObject() => new ComputeShader
        {
            m_File = m_File,
            m_Kernels = (string[])m_Kernels.Clone(),
        };
    }
}
