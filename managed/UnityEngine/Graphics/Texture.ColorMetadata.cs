using UnityEngine.Experimental.Rendering;

namespace UnityEngine
{
    public partial class Texture
    {
        public bool isDataSRGB => GraphicsFormatUtility.IsSRGBFormat(GraphicsFormatOf());
    }
}
