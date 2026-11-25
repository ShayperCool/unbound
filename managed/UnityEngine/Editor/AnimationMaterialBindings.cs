using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityEditor
{
    public partial class AnimationUtility
    {
        /// <summary>
        /// The ids the Unity 6000.0 Editor gives property names before a project's shaders load
        /// (measured: Shader.PropertyToID, tests/projects/animmaterialbindings ids.txt): the engine's
        /// fixed names (up to 382) and the names its built-in shaders register at startup (Sprites,
        /// Skybox, Standard: 383 to 464 in the measured project), for the names Unbound's code and
        /// those shaders use. The material bindings are ordered by Unity's name ids: these names
        /// keep their ids, a texture's _ST, _TexelSize and _HDR follow the texture (or its _ST) when
        /// the shaders registered them, and every other name is numbered after them in the order it
        /// is first seen, a shader's own names in its declaration order, as Unity numbers a shader's
        /// names when it loads it.
        /// </summary>
        static readonly Dictionary<string, int> s_EngineNameIds = new Dictionary<string, int>
        {
            ["_RendererColor"] = 5, ["_Flip"] = 6, ["_MainTex"] = 7, ["_Texel"] = 8, ["_Level"] = 9, ["_TexA"] = 12,
            ["_TexB"] = 13, ["_value"] = 14, ["_LightTextureB0"] = 15, ["_LightTexture0"] = 16,
            ["_ShadowMapTexture"] = 17, ["_HaloFalloff"] = 18, ["_LightPos"] = 19, ["_LightDir"] = 20,
            ["_LightColor"] = 21, ["_LightAsQuad"] = 22, ["unity_Lightmap_HDR"] = 23,
            ["unity_DynamicLightmap_HDR"] = 24, ["_ODSWorldTexture"] = 25, ["_FlareTexture"] = 26,
            ["_CameraDepthTexture"] = 27, ["_CameraDepthNormalsTexture"] = 28, ["_MainTex_ST"] = 34,
            ["_MainTex_TexelSize"] = 35, ["_MainTex_HDR"] = 36, ["_Rect"] = 41, ["_BorderWidths"] = 42,
            ["_CornerRadiuses"] = 44, ["_SmoothCorners"] = 45, ["_ManualTex2SRGB"] = 47, ["_LightBuffer"] = 48,
            ["_StencilNonBackground"] = 49, ["_CameraReflectionsTexture"] = 52, ["_CameraGBufferTexture0"] = 53,
            ["_CameraGBufferTexture1"] = 54, ["_CameraGBufferTexture2"] = 55, ["_SrcBlend"] = 58, ["_DstBlend"] = 59,
            ["_HasLastPositionData"] = 60, ["_ForceNoMotion"] = 61, ["_PreviousM"] = 62, ["_PreviousVP"] = 63,
            ["_NonJitteredVP"] = 64, ["_MotionVectorDepthBias"] = 67, ["_CameraMotionVectorsTexture"] = 68,
            ["_EmissionColor"] = 69, ["unity_BillboardInfo"] = 70, ["unity_BillboardSize"] = 71,
            ["unity_BillboardImageTexCoords"] = 72, ["_AlphaTex"] = 74, ["_EnableExternalAlpha"] = 75,
            ["unity_DOTSInstanceData"] = 176, ["_Color"] = 177, ["unity_BaseCommandID"] = 204,
            ["unity_IndirectDrawArgs"] = 205, ["unity_OneOverOutputBoost"] = 207, ["unity_MaxOutputValue"] = 208,
            ["unity_UseLinearSpace"] = 209, ["_Cutoff"] = 223, ["_TerrainHolesTexture"] = 230,
            ["_TerrainTreeLightDirections"] = 234, ["_TerrainTreeLightColors"] = 235,
            ["_TreeBillboardCameraRight"] = 236, ["_TreeBillboardCameraUp"] = 237, ["_TreeBillboardCameraFront"] = 238,
            ["_TreeBillboardCameraPos"] = 239, ["_TreeBillboardDistances"] = 240, ["_CameraPosition"] = 242,
            ["_WaveAndDistance"] = 243, ["_WavingTint"] = 244, ["_CameraRight"] = 245, ["_CameraUp"] = 246,
            ["_Wind"] = 250, ["_ClipRect"] = 253, ["_UIMaskSoftnessX"] = 254, ["_UIMaskSoftnessY"] = 255,
            ["_UIVertexColorAlwaysGammaSpace"] = 256, ["_BumpMap"] = 260, ["_NormalMap"] = 261,
            ["_DetailNormalMap"] = 263, ["_SelectionID"] = 267, ["_TintColor"] = 281, ["_ST_WindVector"] = 292,
            ["_ST_TreeExtents_SharedHeightStart"] = 322, ["_ST_BranchStretchLimits"] = 323,
            ["_ST_Shared_NoisePosTurbulence_Independence"] = 324, ["_ST_Ripple_NoisePosTurbulence_Independence"] = 330,
            ["_Control"] = 343, ["_NumLayersCount"] = 344, ["_CustomRenderTextureInfo"] = 349,
            ["_SelfTexture2D"] = 350, ["_SelfTexture3D"] = 351, ["_SelfTextureCube"] = 352, ["PixelSnap"] = 383,
            ["_Stencil"] = 385, ["_StencilOp"] = 386, ["_AlphaTex_ST"] = 387, ["_AlphaTex_TexelSize"] = 388,
            ["_AlphaTex_HDR"] = 389, ["_LightTextureB0_ST"] = 390, ["_LightTextureB0_TexelSize"] = 391,
            ["_LightTextureB0_HDR"] = 392, ["_LightTexture0_ST"] = 393, ["_LightTexture0_TexelSize"] = 394,
            ["_LightTexture0_HDR"] = 395, ["_ShadowMapTexture_ST"] = 396, ["_ShadowMapTexture_TexelSize"] = 397,
            ["_ShadowMapTexture_HDR"] = 398, ["_AtmosphereThickness"] = 399, ["_Exposure"] = 400, ["_SunDisk"] = 401,
            ["_SunSize"] = 402, ["_GroundColor"] = 403, ["_SkyTint"] = 404, ["_SunSizeConvergence"] = 405,
            ["_HaloFalloff_ST"] = 406, ["_ODSWorldTexture_ST"] = 409, ["_ODSWorldTexture_TexelSize"] = 410,
            ["_ODSWorldTexture_HDR"] = 411, ["_ZWrite"] = 413, ["_Glossiness"] = 414, ["_GlossMapScale"] = 415,
            ["_SmoothnessTextureChannel"] = 416, ["_Metallic"] = 417, ["_MetallicGlossMap"] = 418,
            ["_MetallicGlossMap_ST"] = 419, ["_MetallicGlossMap_TexelSize"] = 420, ["_MetallicGlossMap_HDR"] = 421,
            ["_SpecularHighlights"] = 422, ["_GlossyReflections"] = 423, ["_BumpScale"] = 424, ["_BumpMap_ST"] = 425,
            ["_BumpMap_TexelSize"] = 426, ["_BumpMap_HDR"] = 427, ["_Parallax"] = 428, ["_ParallaxMap"] = 429,
            ["_ParallaxMap_ST"] = 430, ["_ParallaxMap_TexelSize"] = 431, ["_ParallaxMap_HDR"] = 432,
            ["_OcclusionStrength"] = 433, ["_OcclusionMap"] = 434, ["_OcclusionMap_ST"] = 435,
            ["_OcclusionMap_TexelSize"] = 436, ["_OcclusionMap_HDR"] = 437, ["_EmissionMap"] = 438,
            ["_EmissionMap_ST"] = 439, ["_EmissionMap_TexelSize"] = 440, ["_EmissionMap_HDR"] = 441,
            ["_DetailMask"] = 442, ["_DetailMask_ST"] = 443, ["_DetailMask_TexelSize"] = 444,
            ["_DetailMask_HDR"] = 445, ["_DetailAlbedoMap"] = 446, ["_DetailAlbedoMap_ST"] = 447,
            ["_DetailAlbedoMap_TexelSize"] = 448, ["_DetailAlbedoMap_HDR"] = 449, ["_DetailNormalMapScale"] = 450,
            ["_DetailNormalMap_ST"] = 451, ["_DetailNormalMap_TexelSize"] = 452, ["_DetailNormalMap_HDR"] = 453,
            ["_UVSec"] = 454, ["_Mode"] = 455, ["_SpecColor"] = 456, ["_Emission"] = 457, ["_Shininess"] = 458,
        };

        const int k_AfterEngineNames = 1 << 20;
        static readonly Dictionary<string, int> s_SeenNames = new Dictionary<string, int>();

        const int k_FirstStartupShaderId = 383;
        static readonly string[] s_TextureSuffixes = { "_ST", "_TexelSize", "_HDR" };

        static int NameOrder(string name)
        {
            if (s_EngineNameIds.TryGetValue(name, out var id)) return id;
            for (int i = 0; i < s_TextureSuffixes.Length; i++)
            {
                if (!name.EndsWith(s_TextureSuffixes[i], StringComparison.Ordinal)) continue;
                var texture = name.Substring(0, name.Length - s_TextureSuffixes[i].Length);
                if (s_EngineNameIds.TryGetValue(texture + "_ST", out var st) && st >= k_FirstStartupShaderId) return st + i;
                if (s_EngineNameIds.TryGetValue(texture, out var tex) && tex >= k_FirstStartupShaderId) return tex + 1 + i;
                break;
            }
            if (!s_SeenNames.TryGetValue(name, out var seen)) s_SeenNames[name] = seen = s_SeenNames.Count;
            return k_AfterEngineNames + seen;
        }

        static readonly Dictionary<string, Shader> s_FirstShaderOf = new Dictionary<string, Shader>();

        static void SeeShaderNames(Shader shader)
        {
            foreach (var p in shader.m_Properties)
            {
                SeeShaderName(shader, p.Name);
                if (p.Type != ShaderPropertyType.Texture) continue;
                SeeShaderName(shader, p.Name + "_ST");
                SeeShaderName(shader, p.Name + "_TexelSize");
                SeeShaderName(shader, p.Name + "_HDR");
            }
        }

        static void SeeShaderName(Shader shader, string name)
        {
            if (NameOrder(name) < k_AfterEngineNames) return;
            if (!s_FirstShaderOf.TryGetValue(name, out var first)) s_FirstShaderOf[name] = shader;
            else if (first != shader && first != null)
                Unbound.Runtime.Stubs.Ignored("AnimationUtility.GetAnimatableBindings names shaders share",
                    "orders a name two shaders declare as the first shader seen declares it (the Editor numbers it in its own shader load order; not measured)");
        }

        static readonly string[] s_Rgba = { "r", "g", "b", "a" };
        static readonly string[] s_Xyzw = { "x", "y", "z", "w" };

        /// <summary>A renderer's material property bindings, ahead of its own, as the Editor lists
        /// them (tests/projects/animmaterialbindings, animbindings): for each material in slot order,
        /// its float properties, then its vector properties (colours, vectors, and each texture's
        /// _ST, _TexelSize and _HDR), each kind by Unity's name ids. Values a script set for names
        /// its shader does not declare are listed too; Integer properties are not. A colour has
        /// r, g, b, a components, an HDR colour and every vector x, y, z, w.</summary>
        internal static void AddMaterialBindings(Renderer renderer, string path, Type type, List<EditorCurveBinding> list)
        {
            foreach (var material in renderer.sharedMaterials)
            {
                if (material == null) continue;
                var floats = new List<string>();
                var vectors = new List<(string Name, string[] Axes)>();
                var shader = material.shader;
                if (shader != null)
                {
                    SeeShaderNames(shader);
                    foreach (var p in shader.m_Properties)
                    {
                        switch (p.Type)
                        {
                            case ShaderPropertyType.Float:
                            case ShaderPropertyType.Range:
                                floats.Add(p.Name);
                                break;
                            case ShaderPropertyType.Color:
                                vectors.Add((p.Name, (p.Flags & ShaderPropertyFlags.HDR) != 0 ? s_Xyzw : s_Rgba));
                                break;
                            case ShaderPropertyType.Vector:
                                vectors.Add((p.Name, s_Xyzw));
                                break;
                            case ShaderPropertyType.Texture:
                                vectors.Add((p.Name + "_ST", s_Xyzw));
                                vectors.Add((p.Name + "_TexelSize", s_Xyzw));
                                vectors.Add((p.Name + "_HDR", s_Xyzw));
                                break;
                        }
                    }
                }
                // Values a script set for names the shader does not declare; not the values a
                // loaded material's data or an earlier shader's defaults hold (the built-in
                // Sprites-Mask material's stale entries; a replaced shader's properties).
                foreach (var id in material.m_ScriptSet)
                {
                    var name = Shader.IdToName(id);
                    if (name == null) continue;
                    if (material.m_Floats.ContainsKey(id) && !floats.Contains(name)) floats.Add(name);
                    else if (material.m_Vectors.ContainsKey(id) && !vectors.Exists(v => v.Name == name)) vectors.Add((name, s_Xyzw));
                }
                floats.Sort((a, b) => NameOrder(a).CompareTo(NameOrder(b)));
                vectors.Sort((a, b) => NameOrder(a.Name).CompareTo(NameOrder(b.Name)));
                // A binding names a property, not a slot: a name two materials share is listed once
                // (two Sprites-Default materials: tests/projects/animbindings SpriteShapeRenderer).
                foreach (var name in floats) AddOnce(list, EditorCurveBinding.FloatCurve(path, type, "material." + name));
                foreach (var (name, axes) in vectors)
                    foreach (var axis in axes)
                        AddOnce(list, EditorCurveBinding.FloatCurve(path, type, "material." + name + "." + axis));
            }
        }

        static void AddOnce(List<EditorCurveBinding> list, EditorCurveBinding binding)
        {
            if (!list.Contains(binding)) list.Add(binding);
        }
    }
}
