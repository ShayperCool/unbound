using System;
using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>A camera's own sky: while enabled with a material, the camera draws it instead of
    /// RenderSettings.skybox (with none, the settings' sky; measured: tests/projects/builtinfx
    /// sky_*).</summary>
    [ClassId(45)]
    public sealed partial class Skybox : Behaviour
    {
        Material m_CustomSkybox;

        public Skybox() { }

        public Material material { get => m_CustomSkybox; set => m_CustomSkybox = value; }

        /// <summary>The sky a camera draws: its enabled Skybox's material, else the settings'.</summary>
        internal static Material Of(Camera camera)
        {
            if (camera != null && camera.m_GameObject != null)
            {
                var sky = camera.m_GameObject.GetComponent<Skybox>();
                if (sky != null && sky.enabled && sky.m_CustomSkybox != null) return sky.m_CustomSkybox;
            }
            return RenderSettings.skybox;
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap) =>
            m_CustomSkybox = remap(((Skybox)source).m_CustomSkybox) as Material;

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            var sky = body.Ref("m_CustomSkybox");
            m_CustomSkybox = sky.IsNull ? null : resolver.Resolve(sky, typeof(Material)) as Material;
        }
    }
}
