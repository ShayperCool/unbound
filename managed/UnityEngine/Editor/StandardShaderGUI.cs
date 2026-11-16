using UnityEngine;

namespace UnityEditor
{
    /// <summary>The built-in pipeline's Standard shaders' editor (their <c>CustomEditor</c>). Unbound
    /// draws no inspector; its validation is the Editor's, which runs it on every material of
    /// these shaders it loads (tests/projects/materialvalidation).</summary>
    internal partial class StandardShaderGUI : ShaderGUI
    {
        public enum BlendMode
        {
            Opaque = 0,
            Cutout = 1,
            Fade = 2,
            Transparent = 3,
        }

        public enum SmoothnessMapChannel
        {
            SpecularMetallicAlpha = 0,
            AlbedoAlpha = 1,
        }

        enum WorkflowMode
        {
            Specular,
            Metallic,
            Dielectric,
        }

        public StandardShaderGUI() { }

        /// <summary>The blend mode's render states, keywords and render queue range, and the keywords
        /// that follow the material's textures and emission.</summary>
        public override void ValidateMaterial(Material material)
        {
            var workflow = Workflow(material.shader);
            SetupMaterialWithBlendMode(material, (BlendMode)material.GetFloat("_Mode"), overrideRenderQueue: false);
            SetMaterialKeywords(material, workflow);
        }

        /// <summary>Specular with a specular map and colour, metallic with a metallic map and value.</summary>
        static WorkflowMode Workflow(Shader shader)
        {
            if (shader == null) return WorkflowMode.Dielectric;
            if (shader.FindPropertyIndex("_SpecGlossMap") >= 0 && shader.FindPropertyIndex("_SpecColor") >= 0)
                return WorkflowMode.Specular;
            if (shader.FindPropertyIndex("_MetallicGlossMap") >= 0 && shader.FindPropertyIndex("_Metallic") >= 0)
                return WorkflowMode.Metallic;
            return WorkflowMode.Dielectric;
        }

        /// <summary>Sets the blend mode's RenderType tag, blend factors, depth writes and alpha
        /// keyword; a render queue outside the mode's range (or any, with
        /// <paramref name="overrideRenderQueue"/>) becomes the mode's default, which is logged
        /// unless it was asked for. A mode out of the enum's range changes nothing.</summary>
        public static void SetupMaterialWithBlendMode(Material material, BlendMode blendMode, bool overrideRenderQueue)
        {
            int min = -1, max = 5000, queue = -1;
            switch (blendMode)
            {
                case BlendMode.Opaque:
                    States(material, "", 1, 0, 1, null);
                    max = 2449;
                    break;
                case BlendMode.Cutout:
                    States(material, "TransparentCutout", 1, 0, 1, "_ALPHATEST_ON");
                    (min, max, queue) = (2450, 2500, 2450);
                    break;
                case BlendMode.Fade:
                    States(material, "Transparent", 5, 10, 0, "_ALPHABLEND_ON");
                    (min, max, queue) = (2501, 3999, 3000);
                    break;
                case BlendMode.Transparent:
                    States(material, "Transparent", 1, 10, 0, "_ALPHAPREMULTIPLY_ON");
                    (min, max, queue) = (2501, 3999, 3000);
                    break;
            }
            if (overrideRenderQueue || material.renderQueue < min || material.renderQueue > max)
            {
                if (!overrideRenderQueue)
                    Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, material,
                        "Render queue value outside of the allowed range ({0} - {1}) for selected Blend mode, resetting render queue to default",
                        min, max);
                material.renderQueue = queue;
            }
        }

        static readonly string[] s_AlphaKeywords = { "_ALPHATEST_ON", "_ALPHABLEND_ON", "_ALPHAPREMULTIPLY_ON" };

        static void States(Material material, string renderType, float source, float destination, float zWrite, string alphaKeyword)
        {
            material.SetOverrideTag("RenderType", renderType);
            material.SetFloat("_SrcBlend", source);
            material.SetFloat("_DstBlend", destination);
            material.SetFloat("_ZWrite", zWrite);
            foreach (var keyword in s_AlphaKeywords) SetKeyword(material, keyword, keyword == alphaKeyword);
        }

        static void SetMaterialKeywords(Material material, WorkflowMode workflow)
        {
            SetKeyword(material, "_NORMALMAP", material.GetTexture("_BumpMap") || material.GetTexture("_DetailNormalMap"));
            if (workflow == WorkflowMode.Specular)
                SetKeyword(material, "_SPECGLOSSMAP", material.GetTexture("_SpecGlossMap"));
            else if (workflow == WorkflowMode.Metallic)
                SetKeyword(material, "_METALLICGLOSSMAP", material.GetTexture("_MetallicGlossMap"));
            SetKeyword(material, "_PARALLAXMAP", material.GetTexture("_ParallaxMap"));
            SetKeyword(material, "_DETAIL_MULX2", material.GetTexture("_DetailAlbedoMap") || material.GetTexture("_DetailNormalMap"));
            MaterialEditor.FixupEmissiveFlag(material);
            SetKeyword(material, "_EMISSION", (material.globalIlluminationFlags & MaterialGlobalIlluminationFlags.EmissiveIsBlack) == 0);
            if (material.HasProperty("_SmoothnessTextureChannel"))
                SetKeyword(material, "_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A",
                    (SmoothnessMapChannel)(int)material.GetFloat("_SmoothnessTextureChannel") == SmoothnessMapChannel.AlbedoAlpha);
        }

        static void SetKeyword(Material material, string keyword, bool on)
        {
            if (on) material.EnableKeyword(keyword);
            else material.DisableKeyword(keyword);
        }
    }
}
