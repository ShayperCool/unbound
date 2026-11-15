using UnityEngine.Rendering;

namespace UnityEngine
{
    public partial class Graphics
    {
        static GraphicsTier s_ActiveTier = GraphicsTier.Tier3;

        /// <summary>Tier3 until set; any value set reads back (the Unity 6000.0.71f1 Editor on OpenGL
        /// Core, tests/projects/srpmembers tier.*). On the other devices the same is answered,
        /// warned: they are not measured.</summary>
        public static GraphicsTier activeTier
        {
            get
            {
                if (Unbound.Runtime.GraphicsDeviceProfile.Current.Type != GraphicsDeviceType.OpenGLCore)
                    Unbound.Runtime.Stubs.Ignored("UnityEngine.Graphics.activeTier", "is measured on OpenGL Core only: this device answers as it does");
                return s_ActiveTier;
            }
            set => s_ActiveTier = value;
        }

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
