using Unbound.Runtime;

namespace UnityEngine
{
    public partial class RenderTexture
    {
        internal virtual Object CloneObject()
        {
            // Native copies the description and sampler, not the GPU allocation. The clone
            // starts uncreated even when its source is created and active.
            var copy = new RenderTexture(m_Descriptor) { m_CreatedFromScript = m_CreatedFromScript };
            copy.SetSamplerQuietly(filterMode, wrapModeU, wrapModeV, wrapModeW, anisoLevel, mipMapBias);
            return copy;
        }
    }
}
