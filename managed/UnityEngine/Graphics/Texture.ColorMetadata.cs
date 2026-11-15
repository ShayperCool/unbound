using UnityEngine.Experimental.Rendering;

namespace UnityEngine
{
    public partial class Texture
    {
        /// <summary>The texture's colour space, which a gamma project's GPU format does not show:
        /// an sRGB cubemap's format is UNorm there (matches the Unity 6000.0.71f1 Editor,
        /// tests/projects/lightmapencoding Android gamma profiles).</summary>
        public bool isDataSRGB => this switch
        {
            Texture2D texture => !texture.m_Linear,
            Cubemap cube => !cube.m_Linear,
            _ => GraphicsFormatUtility.IsSRGBFormat(GraphicsFormatOf()),
        };
    }
}
