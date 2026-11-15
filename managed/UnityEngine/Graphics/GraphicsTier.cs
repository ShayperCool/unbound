using UnityEngine.Rendering;

namespace UnityEngine
{
    public partial class Graphics
    {
        /// <summary>Tier3 until set; any value set reads back (the Unity 6000.0.71f1 Editor on OpenGL
        /// Core, tests/projects/srpmembers tier.*). Other devices are not measured.</summary>
        public static GraphicsTier activeTier { get; set; } = GraphicsTier.Tier3;

        /// <summary>None for the Standalone target (srpmembers gles.min). The Android target's
        /// answer (which depends on its player settings) is not measured: None, warned.</summary>
        public static OpenGLESVersion minOpenGLESVersion
        {
            get
            {
                if (UnityEditor.EditorUserBuildSettingsInfo.ActiveGroup == UnityEditor.BuildTargetGroup.Android)
                    Unbound.Runtime.Stubs.Ignored("UnityEngine.Graphics.minOpenGLESVersion", "is not measured for the Android target: None is answered");
                return OpenGLESVersion.None;
            }
        }
    }
}
