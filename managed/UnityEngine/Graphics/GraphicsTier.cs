using System;
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
                    Unbound.Runtime.Stubs.Ignored("UnityEngine.Graphics.activeTier", "is measured on OpenGL Core only: Tier3 is answered as measured there");
                return s_ActiveTier;
            }
            set => s_ActiveTier = value;
        }

        /// <summary>
        /// The lowest OpenGL ES version the active build target's player settings require: with
        /// OpenGL ES 3 among the target's graphics APIs (an Android target left automatic has it),
        /// 3.2, 3.1 AEP or 3.1 as required, checked in that order, else 3.0; None without it
        /// (matches the Unity 6000.0.71f1 Editor: tests/projects/glesversion for Android,
        /// tests/projects/srpmembers gles.min for Standalone).
        /// </summary>
        public static OpenGLESVersion minOpenGLESVersion
        {
            get
            {
                var group = UnityEditor.EditorUserBuildSettingsInfo.ActiveGroup;
                string target = group switch
                {
                    UnityEditor.BuildTargetGroup.Android => "AndroidPlayer",
                    UnityEditor.BuildTargetGroup.iOS => "iOSSupport",
                    UnityEditor.BuildTargetGroup.Standalone => OperatingSystem.IsMacOS() ? "OSXStandaloneSupport" : "LinuxStandaloneSupport",
                    _ => null,
                };
                if (target == null) return OpenGLESVersion.None;
                var apis = UnityEditor.ProjectSettingsText.GraphicsApis(target);
                bool gles = apis != null ? Array.IndexOf(apis, (int)GraphicsDeviceType.OpenGLES3) >= 0
                    : group == UnityEditor.BuildTargetGroup.Android;
                if (!gles) return OpenGLESVersion.None;
                if (UnityEditor.ProjectSettingsText.Int("openGLRequireES32", 0) != 0) return OpenGLESVersion.OpenGLES32;
                if (UnityEditor.ProjectSettingsText.Int("openGLRequireES31AEP", 0) != 0) return OpenGLESVersion.OpenGLES31AEP;
                if (UnityEditor.ProjectSettingsText.Int("openGLRequireES31", 0) != 0) return OpenGLESVersion.OpenGLES31;
                return OpenGLESVersion.OpenGLES30;
            }
        }
    }
}
