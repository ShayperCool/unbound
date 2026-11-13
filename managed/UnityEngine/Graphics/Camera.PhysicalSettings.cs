using Unbound.Runtime;

namespace UnityEngine
{
    public sealed partial class Camera
    {
        // Camera configuration recorded by defaultstate, Unity 6000.0.71f1.
        const float MinimumAperture = .7f, MaximumAperture = 32f;
        float m_Aperture = Compat.DefaultConstructorStateMeasured ? 16f : 0f;
        float m_BarrelClipping = Compat.DefaultConstructorStateMeasured ? .25f : 0f;
        int m_BladeCount = Compat.DefaultConstructorStateMeasured ? 5 : 0;
        Vector2 m_Curvature = Compat.DefaultConstructorStateMeasured ? new Vector2(2f, 11f) : default;
        float m_FocusDistance = Compat.DefaultConstructorStateMeasured ? 10f : 0f;
        int m_Iso = Compat.DefaultConstructorStateMeasured ? 200 : 0;
        float m_ShutterSpeed = Compat.DefaultConstructorStateMeasured ? .005f : 0f;
        StereoTargetEyeMask m_StereoTargetEye = Compat.DefaultConstructorStateMeasured ? StereoTargetEyeMask.Both : 0;

        public float aperture { get => m_Aperture; set => m_Aperture = Mathf.Clamp(value, MinimumAperture, MaximumAperture); }
        public float barrelClipping { get => m_BarrelClipping; set => m_BarrelClipping = Mathf.Clamp01(value); }
        public int bladeCount { get => m_BladeCount; set => m_BladeCount = Mathf.Clamp(value, 3, 11); }
        public Vector2 curvature
        {
            get => m_Curvature;
            set => m_Curvature = new Vector2(Mathf.Clamp(value.x, MinimumAperture, MaximumAperture), Mathf.Clamp(value.y, MinimumAperture, MaximumAperture));
        }
        public float focusDistance { get => m_FocusDistance; set => m_FocusDistance = value < .1f ? .1f : value; }
        public int iso { get => m_Iso; set => m_Iso = System.Math.Max(1, value); }
        public float shutterSpeed { get => m_ShutterSpeed; set => m_ShutterSpeed = value < 0f ? 0f : value; }
        public MonoOrStereoscopicEye stereoActiveEye => Compat.DefaultConstructorStateMeasured ? MonoOrStereoscopicEye.Mono : default;
        public StereoTargetEyeMask stereoTargetEye { get => m_StereoTargetEye; set => m_StereoTargetEye = value; }
        public bool renderCloudsInSceneView { get; set; } = Compat.DefaultConstructorStateMeasured;

        void CopyPhysicalSettings(Camera other)
        {
            m_Aperture = other.m_Aperture; m_BarrelClipping = other.m_BarrelClipping;
            m_BladeCount = other.m_BladeCount; m_Curvature = other.m_Curvature;
            m_FocusDistance = other.m_FocusDistance; m_Iso = other.m_Iso;
            m_ShutterSpeed = other.m_ShutterSpeed; m_StereoTargetEye = other.m_StereoTargetEye;
            renderCloudsInSceneView = other.renderCloudsInSceneView;
        }
    }
}
