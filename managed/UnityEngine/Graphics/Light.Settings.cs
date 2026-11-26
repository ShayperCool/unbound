using System;
using Unbound.Runtime;
using UnityEngine.Rendering;

namespace UnityEngine
{
    public sealed partial class Light
    {
        public LightUnit lightUnit { get; set; } = Compat.DefaultConstructorStateMeasured ? LightUnit.Candela : 0;
        public float luxAtDistance { get; set; } = Compat.DefaultConstructorStateMeasured ? 1f : 0f;
        public bool enableSpotReflector { get; set; } = Compat.DefaultConstructorStateMeasured;

        /// <summary>The per-layer shadow culling distances: a new copy of the 32 layers' distances,
        /// all 0 (use the shadow distance) until set (Unity 6000.0.71f1, tests/projects/defaultcontrols
        /// light.layerShadowCullDistances*). Setting them is not implemented yet.</summary>
        public float[] layerShadowCullDistances
        {
            get => Compat.DefaultConstructorStateMeasured ? new float[LayerCount] : Array.Empty<float>();
            [Unbound.Stub] set => Stubs.NotImplemented("UnityEngine.Light.layerShadowCullDistances");
        }

        /// <summary>The number of layers.</summary>
        const int LayerCount = 32;
        [Obsolete("light.attenuate was removed; all lights always attenuate now", true)]
        [Unbound.EditorFaithful("Removed setting ignores writes in Unity 6000.0.71f1, defaultstate setter controls")]
        public bool attenuate { get => Compat.DefaultConstructorStateMeasured; set { } }
        [Obsolete("Light.lightmappingMode has been deprecated. Use Light.lightmapBakeType instead (UnityUpgradable) -> lightmapBakeType", true)]
        [Unbound.EditorFaithful("Removed setting ignores writes in Unity 6000.0.71f1, defaultstate setter controls")]
        public LightmappingMode lightmappingMode { get => Compat.DefaultConstructorStateMeasured ? (LightmappingMode)4 : default; set { } }
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("Shadow softness is removed in Unity 5.0+", true)]
        [Unbound.EditorFaithful("Removed setting ignores writes in Unity 6000.0.71f1, defaultstate setter controls")]
        public float shadowSoftness { get => Compat.DefaultConstructorStateMeasured ? 4f : 0f; set { } }
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("Shadow softness is removed in Unity 5.0+", true)]
        [Unbound.EditorFaithful("Removed setting ignores writes in Unity 6000.0.71f1, defaultstate setter controls")]
        public float shadowSoftnessFade { get => Compat.DefaultConstructorStateMeasured ? 1f : 0f; set { } }
    }
}
