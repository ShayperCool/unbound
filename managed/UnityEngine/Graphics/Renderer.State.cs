using System;
using System.ComponentModel;
using Unbound.Runtime;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace UnityEngine
{
    public partial class Renderer
    {
        // Configuration masks and initial flags recorded by defaultstate, 6000.0.71f1.
        const int RayModeMask = 3, AccelerationFlagsMask = 7;
        RayTracingMode m_RayTracingMode;
        RayTracingAccelerationStructureBuildFlags m_AccelerationFlags = Compat.DefaultConstructorStateMeasured
            ? RayTracingAccelerationStructureBuildFlags.PreferFastTrace : 0;
        bool m_AccelerationFlagsOverride;
        internal bool m_FirstLOD = true;

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("Use shadowCastingMode instead.", false)]
        public bool castShadows
        {
            get => shadowCastingMode != ShadowCastingMode.Off;
            set => shadowCastingMode = value ? ShadowCastingMode.On : ShadowCastingMode.Off;
        }
        [Obsolete("Use motionVectorGenerationMode instead.", false)]
        public bool motionVectors
        {
            get => motionVectorGenerationMode == MotionVectorGenerationMode.Object;
            set => motionVectorGenerationMode = value ? MotionVectorGenerationMode.Object : MotionVectorGenerationMode.Camera;
        }
        [Obsolete("Use lightProbeUsage instead.", false)]
        public bool useLightProbes
        {
            get => lightProbeUsage != LightProbeUsage.Off;
            set => lightProbeUsage = value ? LightProbeUsage.BlendProbes : LightProbeUsage.Off;
        }
        public RayTracingMode rayTracingMode
        {
            get => m_RayTracingMode;
            set => m_RayTracingMode = Compat.DefaultConstructorStateMeasured ? (RayTracingMode)((int)value & RayModeMask) : value;
        }
        public RayTracingAccelerationStructureBuildFlags rayTracingAccelerationStructureBuildFlags
        {
            get => m_AccelerationFlags;
            set => m_AccelerationFlags = Compat.DefaultConstructorStateMeasured ? (RayTracingAccelerationStructureBuildFlags)((int)value & AccelerationFlagsMask) : value;
        }
        public bool rayTracingAccelerationStructureBuildFlagsOverride
        {
            get => m_AccelerationFlagsOverride;
            set => m_AccelerationFlagsOverride = value;
        }
        public bool isLOD0 => m_LODGroup == null || !m_LODGroup.enabled
            || !m_LODGroup.gameObject.activeInHierarchy || !gameObject.activeInHierarchy || m_FirstLOD;

        internal void InitializeStripDefaults()
        {
            if (!Compat.DefaultConstructorStateMeasured) return;
            lightProbeUsage = LightProbeUsage.Off;
            reflectionProbeUsage = ReflectionProbeUsage.Off;
            motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
        }

        internal void InitializeBillboardDefaults()
        {
            if (!Compat.DefaultConstructorStateMeasured) return;
            shadowCastingMode = ShadowCastingMode.Off;
            receiveShadows = false;
            lightProbeUsage = LightProbeUsage.Off;
            reflectionProbeUsage = ReflectionProbeUsage.Off;
        }
    }
}
