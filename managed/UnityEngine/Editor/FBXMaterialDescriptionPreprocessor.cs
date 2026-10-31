using System;
using System.IO;
using UnityEngine;

namespace UnityEditor.AssetImporters
{
    /// <summary>
    /// The Editor's own <c>OnPreprocessMaterialDescription</c> for the built-in pipeline: turns the
    /// materials of FBX (and other model) files into Standard materials, or Autodesk Interactive
    /// ones for Autodesk Interactive, Arnold Standard Surface and 3ds Max Physical materials.
    /// Render pipelines' postprocessors run after it (order -990) and replace its results.
    /// Material animation curves are not imported (the descriptions have none).
    /// </summary>
    public partial class FBXMaterialDescriptionPreprocessor : AssetPostprocessor
    {
        const uint k_Version = 3;
        const int k_Order = -990;

        public FBXMaterialDescriptionPreprocessor() { }

        public override uint GetVersion() => k_Version;

        public override int GetPostprocessOrder() => k_Order;

        public void OnPreprocessMaterialDescription(MaterialDescription description, Material material, AnimationClip[] clips)
        {
            switch (Path.GetExtension(assetPath).ToLowerInvariant())
            {
                case ".fbx":
                case ".dae":
                case ".obj":
                case ".blend":
                case ".mb":
                case ".ma":
                case ".max":
                    break;
                default:
                    return;
            }
            if (IsAutodeskInteractiveMaterial(description))
                CreateFromAutodeskInteractiveMaterial(description, material);
            else if (IsMayaArnoldStandardSurfaceMaterial(description))
                CreateFromMayaArnoldStandardSurfaceMaterial(description, material);
            else if (Is3DsMaxArnoldStandardSurfaceMaterial(description))
                CreateFrom3DsMaxArnoldStandardSurfaceMaterial(description, material);
            else if (Is3DsMaxPhysicalMaterial(description))
                CreateFrom3DsMaxPhysicalMaterial(description, material);
            else if (Is3DsMaxSimplifiedPhysicalMaterial(description))
                CreateFrom3DsMaxSimplifiedPhysicalMaterial(description, material);
            else
                CreateFromStandardMaterial(description, material);
        }

        static bool Is3DsMaxPhysicalMaterial(MaterialDescription description)
        {
            description.TryGetProperty("ClassIDa", out float a);
            description.TryGetProperty("ClassIDb", out float b);
            description.TryGetProperty("ORIGINAL_MTL", out string original);
            return (a == 1.03042995E+09f && b == -559038460f) || original == "PHYSICAL_MTL";
        }

        static bool Is3DsMaxSimplifiedPhysicalMaterial(MaterialDescription description)
        {
            description.TryGetProperty("ClassIDa", out float a);
            description.TryGetProperty("ClassIDb", out float b);
            return a == -804315650f && b == -1.0994388E+09f;
        }

        static bool IsMayaArnoldStandardSurfaceMaterial(MaterialDescription description)
        {
            description.TryGetProperty("TypeId", out float typeId);
            return typeId == 1138001f;
        }

        static bool Is3DsMaxArnoldStandardSurfaceMaterial(MaterialDescription description)
        {
            description.TryGetProperty("ClassIDa", out float a);
            description.TryGetProperty("ClassIDb", out float b);
            description.TryGetProperty("ORIGINAL_MTL", out string original);
            return a == 2.1214715E+09f && b == 1.6603739E+09f && original != "PHYSICAL_MTL";
        }

        static bool IsAutodeskInteractiveMaterial(MaterialDescription description) =>
            description.TryGetProperty("renderAPI", out string api) && api == "SFX_PBS_SHADER";

        static void SetupMaterialTransparency(Material material, bool transparent)
        {
            if (transparent)
            {
                material.SetFloat("_Mode", 3f);
                material.SetOverrideTag("RenderType", "Transparent");
                material.SetFloat("_SrcBlend", 1f);
                material.SetFloat("_DstBlend", 10f);
                material.SetFloat("_ZWrite", 0f);
                material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                material.renderQueue = 3000;
            }
            else
            {
                material.SetFloat("_Mode", 0f);
                material.SetOverrideTag("RenderType", "");
                material.SetFloat("_SrcBlend", 1f);
                material.SetFloat("_DstBlend", 0f);
                material.SetFloat("_ZWrite", 1f);
                material.DisableKeyword("_ALPHATEST_ON");
                material.DisableKeyword("_ALPHABLEND_ON");
                material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                material.renderQueue = -1;
            }
        }

        bool UseShader(Material material, string name)
        {
            var shader = Shader.Find(name);
            if (shader == null)
            {
                context?.LogImportError($"FBXMaterialDescriptionPreprocessor cannot find a shader named '{name}'.");
                return false;
            }
            material.shader = shader;
            return true;
        }

        static void Emissive(Material material)
        {
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags |= MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        static Vector4 Gamma(Vector4 color) =>
            QualitySettings.activeColorSpace == ColorSpace.Linear
                ? new Vector4(Mathf.LinearToGammaSpace(color.x), Mathf.LinearToGammaSpace(color.y),
                    Mathf.LinearToGammaSpace(color.z), color.w)
                : color;

        void CreateFrom3DsMaxArnoldStandardSurfaceMaterial(MaterialDescription description, Material material)
        {
            if (!UseShader(material, "Autodesk Interactive")) return;
            bool hasTransmission = description.TryGetProperty("transmission", out float transmission);
            SetupMaterialTransparency(material, hasTransmission && transmission > 0f);
            if (description.TryGetProperty("base_color.shader", out TexturePropertyDescription texture))
            {
                SetMaterialTextureProperty("_MainTex", material, texture);
                material.SetColor("_Color", new Color(1f, 1f, 1f, 1f - transmission));
            }
            else if (description.TryGetProperty("base_color", out Vector4 color))
            {
                color = Gamma(color);
                color.w = 1f - transmission;
                material.SetColor("_Color", color);
            }
            if (description.TryGetProperty("normal.shader", out texture))
            {
                SetMaterialTextureProperty("_BumpMap", material, texture);
                material.EnableKeyword("_NORMALMAP");
            }
            if (description.TryGetProperty("specular_roughness", out texture))
            {
                SetMaterialTextureProperty("_SpecGlossMap", material, texture);
                material.EnableKeyword("_SPECGLOSSMAP");
            }
            else if (description.TryGetProperty("specular_roughness", out float roughness))
            {
                material.SetFloat("_Glossiness", roughness);
            }
            if (description.TryGetProperty("metalness", out texture))
            {
                SetMaterialTextureProperty("_MetallicGlossMap", material, texture);
                material.EnableKeyword("_METALLICGLOSSMAP");
            }
            if (description.TryGetProperty("emission", out float emission) && emission > 0f)
            {
                if (description.TryGetProperty("emission_color.shader", out texture))
                {
                    SetMaterialTextureProperty("_EmissionMap", material, texture);
                    material.SetColor("_EmissionColor", Color.white * emission);
                    Emissive(material);
                }
                else if (description.TryGetProperty("emission_color", out Vector4 emissionColor))
                {
                    material.SetColor("_EmissionColor", emissionColor * emission);
                    Emissive(material);
                }
            }
        }

        void CreateFromMayaArnoldStandardSurfaceMaterial(MaterialDescription description, Material material)
        {
            if (!UseShader(material, "Autodesk Interactive")) return;
            bool hasTransmission = description.TryGetProperty("transmission", out float transmission);
            SetupMaterialTransparency(material, hasTransmission && transmission > 0f);
            if (description.TryGetProperty("baseColor", out TexturePropertyDescription texture))
            {
                SetMaterialTextureProperty("_MainTex", material, texture);
                material.SetColor("_Color", new Color(1f, 1f, 1f, 1f - transmission));
            }
            else if (description.TryGetProperty("baseColor", out Vector4 color))
            {
                color = Gamma(color);
                color.w = 1f - transmission;
                material.SetColor("_Color", color);
            }
            if (description.TryGetProperty("normalCamera", out texture))
            {
                SetMaterialTextureProperty("_BumpMap", material, texture);
                material.EnableKeyword("_NORMALMAP");
            }
            if (description.TryGetProperty("specularRoughness", out texture))
            {
                SetMaterialTextureProperty("_SpecGlossMap", material, texture);
                material.EnableKeyword("_SPECGLOSSMAP");
            }
            else if (description.TryGetProperty("specularRoughness", out float roughness))
            {
                material.SetFloat("_Glossiness", roughness);
            }
            if (description.TryGetProperty("metalness", out texture))
            {
                SetMaterialTextureProperty("_MetallicGlossMap", material, texture);
                material.EnableKeyword("_METALLICGLOSSMAP");
            }
            if (description.TryGetProperty("emission", out float emission) && emission > 0f)
            {
                Emissive(material);
                if (description.TryGetProperty("emissionColor", out texture))
                {
                    SetMaterialTextureProperty("_EmissionMap", material, texture);
                    material.SetColor("_EmissionColor", Color.white * emission);
                }
                else if (description.TryGetProperty("emissionColor", out Vector4 emissionColor))
                {
                    material.SetColor("_EmissionColor", emissionColor * emission);
                }
            }
        }

        void CreateFrom3DsMaxPhysicalMaterial(MaterialDescription description, Material material)
        {
            if (!UseShader(material, "Autodesk Interactive")) return;
            bool hasTransparency = description.TryGetProperty("transparency", out float transparency);
            SetupMaterialTransparency(material, hasTransparency && transparency > 0f);
            if (description.TryGetProperty("base_color_map", out TexturePropertyDescription texture))
            {
                SetMaterialTextureProperty("_MainTex", material, texture);
                material.SetColor("_Color", new Color(1f, 1f, 1f, 1f - transparency));
            }
            else if (description.TryGetProperty("base_color", out Vector4 color))
            {
                color.w = 1f - transparency;
                material.SetColor("_Color", color);
            }
            if (description.TryGetProperty("bump_map", out texture))
            {
                if (description.TryGetProperty("bump_map_amt", out float amount))
                    material.SetFloat("_BumpScale", amount);
                SetMaterialTextureProperty("_BumpMap", material, texture);
                material.EnableKeyword("_NORMALMAP");
            }
            if (description.TryGetProperty("roughness_map", out texture))
            {
                SetMaterialTextureProperty("_SpecGlossMap", material, texture);
                material.EnableKeyword("_SPECGLOSSMAP");
            }
            if (description.TryGetProperty("metalness_map", out texture))
            {
                SetMaterialTextureProperty("_MetallicGlossMap", material, texture);
                material.EnableKeyword("_METALLICGLOSSMAP");
            }
            if (description.TryGetProperty("emission", out float emission) && emission > 0f)
            {
                Emissive(material);
                if (description.TryGetProperty("emit_color_map", out texture))
                {
                    SetMaterialTextureProperty("_EmissionMap", material, texture);
                    material.SetColor("_EmissionColor", Color.white * emission);
                }
                else if (description.TryGetProperty("emit_color", out Vector4 emitColor))
                {
                    material.SetColor("_EmissionColor", emitColor * emission);
                }
            }
        }

        void CreateFrom3DsMaxSimplifiedPhysicalMaterial(MaterialDescription description, Material material)
        {
            if (!UseShader(material, "Autodesk Interactive")) return;
            bool hasBase = description.TryGetProperty("base_color", out Vector4 color);
            SetupMaterialTransparency(material, hasBase && color.w > 0f);
            if (description.TryGetProperty("base_color_map", out TexturePropertyDescription texture))
                SetMaterialTextureProperty("_MainTex", material, texture);
            if (description.TryGetProperty("basecolor", out color))
                material.SetColor("_Color", color);
            if (description.TryGetProperty("norm_map", out texture))
            {
                if (description.TryGetProperty("bump_map_amt", out float amount))
                    material.SetFloat("_BumpScale", amount);
                SetMaterialTextureProperty("_BumpMap", material, texture);
                material.EnableKeyword("_NORMALMAP");
            }
            if (description.TryGetProperty("roughness_map", out texture))
            {
                SetMaterialTextureProperty("_SpecGlossMap", material, texture);
                material.EnableKeyword("_SPECGLOSSMAP");
            }
            else if (description.TryGetProperty("roughness", out float roughness))
            {
                material.SetFloat("_Glossiness", roughness);
            }
            if (description.TryGetProperty("metalness_map", out texture))
            {
                SetMaterialTextureProperty("_MetallicGlossMap", material, texture);
                material.EnableKeyword("_METALLICGLOSSMAP");
            }
            else if (description.TryGetProperty("metalness", out float metalness))
            {
                material.SetFloat("_Metallic", metalness);
            }
            if (description.TryGetProperty("displacement_map", out texture))
            {
                SetMaterialTextureProperty("_ParallaxMap", material, texture);
                material.EnableKeyword("_PARALLAXMAP");
            }
            if (description.TryGetProperty("ao_map", out texture))
            {
                SetMaterialTextureProperty("_OcclusionMap", material, texture);
                material.EnableKeyword("_PARALLAXMAP"); // sic: Unity's preprocessor enables this keyword
            }
            if (description.TryGetProperty("emit_color_map", out texture))
            {
                SetMaterialTextureProperty("_EmissionMap", material, texture);
                material.SetColor("_EmissionColor", Color.white);
                Emissive(material);
            }
            else if (description.TryGetProperty("emit_color", out color))
            {
                material.SetColor("_EmissionColor", color);
                Emissive(material);
            }
        }

        void CreateFromAutodeskInteractiveMaterial(MaterialDescription description, Material material)
        {
            if (!UseShader(material, "Autodesk Interactive")) return;
            var offset = Vector2.zero;
            var scale = Vector2.one;
            if (description.TryGetProperty("uv_offset", out Vector4 uvOffset))
                offset = new Vector2(uvOffset.x, -uvOffset.y);
            if (description.TryGetProperty("uv_scale", out Vector4 uvScale))
                scale = new Vector2(uvScale.x, uvScale.y);
            float opacity = 1f;
            description.TryGetProperty("opacity", out opacity);
            description.TryGetProperty("mask_threshold", out float threshold);
            float flag;
            if (threshold > 0f)
            {
                material.SetFloat("_Mode", 1f);
                material.SetOverrideTag("RenderType", "TransparentCutout");
                material.SetFloat("_SrcBlend", 1f);
                material.SetFloat("_DstBlend", 10f);
                material.SetFloat("_ZWrite", 1f);
                material.EnableKeyword("_ALPHATEST_ON");
                material.renderQueue = 3000;
            }
            else if (opacity < 1f || (description.TryGetProperty("use_opacity_map", out flag) && flag == 1f))
            {
                material.SetFloat("_Mode", 3f);
                material.SetOverrideTag("RenderType", "Transparent");
                material.SetFloat("_SrcBlend", 1f);
                material.SetFloat("_DstBlend", 10f);
                material.SetFloat("_ZWrite", 0f);
                material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                material.renderQueue = 3000;
            }
            else
            {
                SetupMaterialTransparency(material, false);
            }
            TexturePropertyDescription texture;
            if ((description.TryGetProperty("use_color_map", out flag) && flag == 1f) ||
                (description.TryGetProperty("use_opacity_map", out flag) && flag == 1f))
            {
                if (description.TryGetProperty("TEX_color_map", out texture))
                {
                    SetTexture("_MainTex", material, texture.texture, offset, scale);
                    material.SetColor("_Color", new Vector4(1f, 1f, 1f, opacity));
                    if (threshold > 0f) material.SetFloat("_Cutoff", threshold);
                }
            }
            else
            {
                description.TryGetProperty("base_color", out Vector4 color);
                color.w = opacity;
                material.SetColor("_Color", color);
            }
            if (description.TryGetProperty("use_normal_map", out flag) && flag == 1f &&
                description.TryGetProperty("TEX_normal_map", out texture))
            {
                SetTexture("_BumpMap", material, texture.texture, offset, scale);
                material.EnableKeyword("_NORMALMAP");
            }
            if (description.TryGetProperty("use_metallic_map", out flag) && flag == 1f)
            {
                if (description.TryGetProperty("TEX_metallic_map", out texture))
                {
                    SetTexture("_MetallicGlossMap", material, texture.texture, offset, scale);
                    material.EnableKeyword("_METALLICGLOSSMAP");
                }
            }
            else if (description.TryGetProperty("metallic", out float metallic))
            {
                material.SetFloat("_Metallic", metallic);
            }
            if (description.TryGetProperty("use_roughness_map", out flag) && flag == 1f)
            {
                if (description.TryGetProperty("TEX_roughness_map", out texture))
                {
                    SetTexture("_SpecGlossMap", material, texture.texture, offset, scale);
                    material.EnableKeyword("_SPECGLOSSMAP");
                }
            }
            else if (description.TryGetProperty("roughness", out float roughness))
            {
                material.SetFloat("_Glossiness", roughness);
            }
            if (description.TryGetProperty("use_emissive_map", out flag) && flag == 1f)
            {
                if (description.TryGetProperty("TEX_emissive_map", out texture))
                {
                    var emission = Vector4.one;
                    if (description.TryGetProperty("emissive_intensity", out float intensity))
                        emission *= intensity;
                    material.SetColor("_EmissionColor", emission);
                    SetTexture("_EmissionMap", material, texture.texture, offset, scale);
                    Emissive(material);
                }
            }
            else if (description.TryGetProperty("emissive", out Vector4 emissive))
            {
                if (emissive.x > 0f || emissive.y > 0f || emissive.z > 0f) Emissive(material);
                if (description.TryGetProperty("emissive_intensity", out float intensity))
                    emissive *= intensity;
                material.SetColor("_EmissionColor", emissive);
            }
            if (description.TryGetProperty("use_ao_map", out flag) && flag == 1f &&
                description.TryGetProperty("TEX_ao_map", out texture))
            {
                SetTexture("_OcclusionMap", material, texture.texture, offset, scale);
            }
        }

        void CreateFromStandardMaterial(MaterialDescription description, Material material)
        {
            if (!UseShader(material, "Standard")) return;
            // An absent property reads as 0, as in Unity's implementation.
            if (!description.TryGetProperty("Opacity", out float opacity))
            {
                if (description.TryGetProperty("TransparencyFactor", out float transparencyFactor))
                    opacity = transparencyFactor == 1f ? 1f : 1f - transparencyFactor;
                if (opacity == 1f && description.TryGetProperty("TransparentColor", out Vector4 transparentColor))
                    opacity = transparentColor.x == 1f ? 1f : 1f - transparentColor.x;
            }
            bool transparent = opacity < 1f || (opacity == 1f && description.TryGetProperty("TransparentColor", out TexturePropertyDescription _));
            SetupMaterialTransparency(material, transparent);
            float factor;
            if (description.TryGetProperty("DiffuseColor", out TexturePropertyDescription texture))
            {
                var color = Color.white;
                if (description.TryGetProperty("DiffuseFactor", out factor)) color *= factor;
                color.a = opacity;
                SetMaterialTextureProperty("_MainTex", material, texture);
                material.SetColor("_Color", color);
            }
            else if (description.TryGetProperty("DiffuseColor", out Vector4 diffuse))
            {
                Color color = diffuse;
                if (description.TryGetProperty("DiffuseFactor", out factor)) color *= factor;
                color.a = opacity;
                material.SetColor("_Color", PlayerSettings.colorSpace == ColorSpace.Linear ? color.gamma : color);
            }
            if (description.TryGetProperty("Bump", out texture) || description.TryGetProperty("NormalMap", out texture))
            {
                SetMaterialTextureProperty("_BumpMap", material, texture);
                material.EnableKeyword("_NORMALMAP");
                if (description.TryGetProperty("BumpFactor", out factor))
                    material.SetFloat("_BumpScale", factor);
            }
            if (description.TryGetProperty("EmissiveColor", out texture))
            {
                material.SetColor("_EmissionColor", Color.white);
                SetMaterialTextureProperty("_EmissionMap", material, texture);
                if (description.TryGetProperty("EmissiveFactor", out factor) && factor > 0f)
                    Emissive(material);
            }
            else if (description.TryGetProperty("EmissiveColor", out Vector4 emissive) && emissive.magnitude > emissive.w)
            {
                // An absent factor reads as 0: no emission keyword.
                if (description.TryGetProperty("EmissiveFactor", out factor)) emissive *= factor;
                material.SetColor("_EmissionColor", emissive);
                if (factor > 0f) Emissive(material);
            }
            material.SetFloat("_Glossiness",
                description.TryGetProperty("Shininess", out float shininess) ? Mathf.Sqrt(shininess * 0.01f) : 0f);
        }

        static void SetMaterialTextureProperty(string propertyName, Material material, TexturePropertyDescription texture) =>
            SetTexture(propertyName, material, texture.texture, texture.offset, texture.scale);

        static void SetTexture(string propertyName, Material material, Texture texture, Vector2 offset, Vector2 scale)
        {
            material.SetTexture(propertyName, texture);
            material.SetTextureOffset(propertyName, offset);
            material.SetTextureScale(propertyName, scale);
        }
    }
}
