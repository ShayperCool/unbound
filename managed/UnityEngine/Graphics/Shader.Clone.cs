using Unbound.Runtime;

namespace UnityEngine
{
    public sealed partial class Shader : Instantiation.ICloneable
    {
        Object Instantiation.ICloneable.CloneObject()
        {
            var copy = new Shader
            {
                m_RenderQueue = m_RenderQueue,
                m_CustomEditor = m_CustomEditor,
                m_MaximumLOD = m_MaximumLOD,
                m_QueueFromSource = m_QueueFromSource,
                m_Variant = m_Variant,
            };
            copy.m_Properties.AddRange(m_Properties);
            copy.m_PipelineEditors.AddRange(m_PipelineEditors);
            foreach (var pair in m_DefaultTextures) copy.m_DefaultTextures.Add(pair.Key, pair.Value);
            foreach (var pair in m_NonModifiableTextures) copy.m_NonModifiableTextures.Add(pair.Key, pair.Value);
            return copy;
        }
    }
}
