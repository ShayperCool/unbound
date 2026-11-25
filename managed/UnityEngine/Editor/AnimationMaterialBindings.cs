using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityEditor
{
    public partial class AnimationUtility
    {
        /// <summary>
        /// The ids Unity 6000.0's engine gives property names before any shader loads (measured:
        /// Shader.PropertyToID in the Editor, tests/projects/animmaterialbindings; names Unbound's
        /// own code uses). The material bindings are ordered by Unity's name ids: these names keep
        /// their fixed ids; every other name is numbered after them in the order it is first seen,
        /// a shader's own names in its declaration order (a texture's name, then its _ST,
        /// _TexelSize and _HDR), as Unity numbers a shader's names when it loads it.
        /// </summary>
        static readonly Dictionary<string, int> s_EngineNameIds = new Dictionary<string, int>
        {
            ["_RendererColor"] = 5, ["_Flip"] = 6, ["_MainTex"] = 7, ["_Texel"] = 8, ["_Level"] = 9, ["_TexA"] = 12,
            ["_TexB"] = 13, ["_value"] = 14, ["_LightTextureB0"] = 15, ["_LightTexture0"] = 16,
            ["_ShadowMapTexture"] = 17, ["_HaloFalloff"] = 18, ["_LightPos"] = 19, ["_LightDir"] = 20,
            ["_LightColor"] = 21, ["_LightAsQuad"] = 22, ["unity_Lightmap_HDR"] = 23,
            ["unity_DynamicLightmap_HDR"] = 24, ["_FlareTexture"] = 26, ["_CameraDepthTexture"] = 27,
            ["_CameraDepthNormalsTexture"] = 28, ["_MainTex_ST"] = 34, ["_MainTex_TexelSize"] = 35,
            ["_MainTex_HDR"] = 36, ["_Rect"] = 41, ["_BorderWidths"] = 42, ["_CornerRadiuses"] = 44,
            ["_SmoothCorners"] = 45, ["_ManualTex2SRGB"] = 47, ["_LightBuffer"] = 48, ["_StencilNonBackground"] = 49,
            ["_CameraReflectionsTexture"] = 52, ["_CameraGBufferTexture0"] = 53, ["_CameraGBufferTexture1"] = 54,
            ["_CameraGBufferTexture2"] = 55, ["_SrcBlend"] = 58, ["_DstBlend"] = 59, ["_HasLastPositionData"] = 60,
            ["_ForceNoMotion"] = 61, ["_PreviousM"] = 62, ["_PreviousVP"] = 63, ["_NonJitteredVP"] = 64,
            ["_MotionVectorDepthBias"] = 67, ["_CameraMotionVectorsTexture"] = 68, ["_EmissionColor"] = 69,
            ["unity_BillboardInfo"] = 70, ["unity_BillboardSize"] = 71, ["unity_BillboardImageTexCoords"] = 72,
            ["_AlphaTex"] = 74, ["_EnableExternalAlpha"] = 75, ["unity_DOTSInstanceData"] = 176, ["_Color"] = 177,
            ["unity_BaseCommandID"] = 204, ["unity_IndirectDrawArgs"] = 205, ["unity_OneOverOutputBoost"] = 207,
            ["unity_MaxOutputValue"] = 208, ["unity_UseLinearSpace"] = 209, ["_Cutoff"] = 223,
            ["_TerrainHolesTexture"] = 230, ["_TerrainTreeLightDirections"] = 234, ["_TerrainTreeLightColors"] = 235,
            ["_TreeBillboardCameraRight"] = 236, ["_TreeBillboardCameraUp"] = 237, ["_TreeBillboardCameraFront"] = 238,
            ["_TreeBillboardCameraPos"] = 239, ["_TreeBillboardDistances"] = 240, ["_CameraPosition"] = 242,
            ["_WaveAndDistance"] = 243, ["_WavingTint"] = 244, ["_CameraRight"] = 245, ["_CameraUp"] = 246,
            ["_Wind"] = 250, ["_ClipRect"] = 253, ["_UIMaskSoftnessX"] = 254, ["_UIMaskSoftnessY"] = 255,
            ["_UIVertexColorAlwaysGammaSpace"] = 256, ["_BumpMap"] = 260, ["_NormalMap"] = 261,
            ["_DetailNormalMap"] = 263, ["_SelectionID"] = 267, ["_TintColor"] = 281, ["_ST_WindVector"] = 292,
            ["_ST_TreeExtents_SharedHeightStart"] = 322, ["_ST_BranchStretchLimits"] = 323,
            ["_ST_Shared_NoisePosTurbulence_Independence"] = 324, ["_ST_Ripple_NoisePosTurbulence_Independence"] = 330,
            ["_Control"] = 343, ["_NumLayersCount"] = 344, ["_CustomRenderTextureInfo"] = 349,
            ["_SelfTexture2D"] = 350, ["_SelfTexture3D"] = 351, ["_SelfTextureCube"] = 352,
        };

        const int k_AfterEngineNames = 1 << 20;
        static readonly Dictionary<string, int> s_SeenNames = new Dictionary<string, int>();

        static int NameOrder(string name)
        {
            if (s_EngineNameIds.TryGetValue(name, out var id)) return id;
            if (!s_SeenNames.TryGetValue(name, out var seen)) s_SeenNames[name] = seen = s_SeenNames.Count;
            return k_AfterEngineNames + seen;
        }

        static void SeeShaderNames(Shader shader)
        {
            foreach (var p in shader.m_Properties)
            {
                NameOrder(p.Name);
                if (p.Type != ShaderPropertyType.Texture) continue;
                NameOrder(p.Name + "_ST");
                NameOrder(p.Name + "_TexelSize");
                NameOrder(p.Name + "_HDR");
            }
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
