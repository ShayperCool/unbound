using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEditor;

namespace UnityEngine.VFX
{
    /// <summary>The effect's renderer: the component its outputs draw through.</summary>
    [ClassId(73398921)]
    [RejectDragAndDropMaterial]
    public sealed partial class VFXRenderer : Renderer
    {
        /// <summary>An effect's renderer starts without material slots, casting and receiving
        /// no shadows and using no probes (Unity 6000.0.71f1, tests/projects/defaultstate).</summary>
        [UnityEngine.Scripting.RequiredMember]
        public VFXRenderer()
        {
            m_Materials = System.Array.Empty<Material>();
            shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            receiveShadows = false;
            lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver) => ReadRendererData(body, resolver);

        /// <summary>The draws of the effect's outputs, system by system.</summary>
        internal override void CollectDraws(FrameBuilder.CameraContext camera, System.Collections.Generic.List<FrameBuilder.DrawItem> into)
        {
            if (TryGetComponent<VisualEffect>(out var effect)) effect.CollectDraws(this, camera, into);
        }

        /// <summary>The renderer's bounds are its effect's: the union of the systems' bounds.</summary>
        internal override Bounds GetLocalBounds() =>
            TryGetComponent<VisualEffect>(out var effect) && effect.RendererBounds(out var bounds) ? bounds : new Bounds(Vector3.zero, Vector3.zero);
    }

    /// <summary>
    /// The Visual Effect module's project settings (<c>ProjectSettings/VFXManager.asset</c>): the
    /// fixed time step effects in Fixed Delta Time mode advance by, the longest step any effect
    /// takes, the longest time a scrubbed effect simulates and the Visual Effect Graph package's
    /// runtime resources. A project without the file has the Editor's defaults.
    /// </summary>
    public static partial class VFXManager
    {
        /// <summary>A new project's settings, as the Unity 6000.0.71f1 Editor writes them.</summary>
        const float DefaultFixedTimeStep = 1f / 60f, DefaultMaxDeltaTime = 0.05f, DefaultMaxScrubTime = 30f;

        static bool s_Loaded;
        static float s_FixedTimeStep = DefaultFixedTimeStep, s_MaxDeltaTime = DefaultMaxDeltaTime, s_MaxScrubTime = DefaultMaxScrubTime;
        static ObjectRef s_RuntimeResources;

        static void Load()
        {
            if (s_Loaded) return;
            s_Loaded = true;
            var body = SettingsAssets.AtPath("ProjectSettings/VFXManager.asset")?.Body?.ToNode();
            if (body == null) return;
            s_FixedTimeStep = body.Float("m_FixedTimeStep", DefaultFixedTimeStep);
            s_MaxDeltaTime = body.Float("m_MaxDeltaTime", DefaultMaxDeltaTime);
            s_MaxScrubTime = body.Float("m_MaxScrubTime", DefaultMaxScrubTime);
            s_RuntimeResources = body.Ref("m_RuntimeResources");
        }

        /// <summary>The longest time an effect scrubbed in the Editor simulates
        /// (<c>m_MaxScrubTime</c>).</summary>
        internal static float maxScrubTime
        {
            get { Load(); return s_MaxScrubTime; }
            set { Load(); s_MaxScrubTime = value; }
        }

        /// <summary>The runtime resources the Visual Effect Graph package keeps in the settings
        /// (<c>m_RuntimeResources</c>; null when the settings name none).</summary>
        internal static ScriptableObject runtimeResources
        {
            get
            {
                Load();
                var r = s_RuntimeResources;
                if (string.IsNullOrEmpty(r.Guid) || r.FileId == 0) return null;
                return AssetDatabaseRuntime.Load(r.Guid, r.FileId, typeof(ScriptableObject)) as ScriptableObject;
            }
        }

        public static float fixedTimeStep
        {
            get { Load(); return s_FixedTimeStep; }
            set { Load(); s_FixedTimeStep = value; }
        }

        public static float maxDeltaTime
        {
            get { Load(); return s_MaxDeltaTime; }
            set { Load(); s_MaxDeltaTime = value; }
        }
    }
}
