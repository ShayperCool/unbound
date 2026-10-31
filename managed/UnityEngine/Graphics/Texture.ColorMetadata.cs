using UnityEngine.Experimental.Rendering;

namespace UnityEngine
{
    public partial class Texture
    {
        public bool isDataSRGB => this is Texture2D texture ? !texture.m_Linear : GraphicsFormatUtility.IsSRGBFormat(GraphicsFormatOf());
    }
}
