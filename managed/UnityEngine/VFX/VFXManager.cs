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
        [UnityEngine.Scripting.RequiredMember]
        public VFXRenderer() { }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver) => ReadRendererData(body, resolver);
    }

    /// <summary>
    /// The Visual Effect module's project settings (<c>ProjectSettings/VFXManager.asset</c>): the
    /// fixed time step effects in Fixed Delta Time mode advance by and the longest step any
    /// effect takes. A project without the file has the Editor's defaults.
    /// </summary>
    public static partial class VFXManager
    {
        /// <summary>A new project's settings, as the Unity 6000.0.71f1 Editor writes them.</summary>
        const float DefaultFixedTimeStep = 1f / 60f, DefaultMaxDeltaTime = 0.05f;

        static bool s_Loaded;
        static float s_FixedTimeStep = DefaultFixedTimeStep, s_MaxDeltaTime = DefaultMaxDeltaTime;

        static void Load()
        {
            if (s_Loaded) return;
            s_Loaded = true;
            var body = SettingsAssets.AtPath("ProjectSettings/VFXManager.asset")?.Body?.ToNode();
            if (body == null) return;
            s_FixedTimeStep = body.Float("m_FixedTimeStep", DefaultFixedTimeStep);
            s_MaxDeltaTime = body.Float("m_MaxDeltaTime", DefaultMaxDeltaTime);
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
