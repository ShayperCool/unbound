using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unbound.Runtime
{
    /// <summary>
    /// Makes the material sub-assets of an imported model as the Editor's model importer does,
    /// when the material loads: the native importer has described the file's material
    /// (<c>m_UnboundMaterialImport</c>, see the engine's model importer), with its textures found.
    /// "Import via MaterialDescription" runs every <c>OnPreprocessMaterialDescription</c> of the
    /// loaded Editor code by increasing <see cref="AssetPostprocessor.GetPostprocessOrder"/>: the
    /// built-in <see cref="FBXMaterialDescriptionPreprocessor"/>, render pipelines' and the
    /// project's. "Standard (Legacy)" applies the legacy importer's conversion.
    /// </summary>
    internal static class ModelMaterialImport
    {
        static readonly int s_Color = Shader.PropertyToID("_Color");
        static readonly int s_MainTex = Shader.PropertyToID("_MainTex");
        static List<(int Order, Type Type, MethodInfo Method)> s_Preprocessors;

        /// <summary>The model whose materials' description preprocessors wait for its
        /// postprocessors' pass (<see cref="UnityEditor.HierarchyPostprocessing"/>), while it is
        /// read.</summary>
        [ThreadStatic] static string s_Deferring;
        static readonly Dictionary<Material, MaterialDescription> s_Deferred =
            new Dictionary<Material, MaterialDescription>(ReferenceEqualityComparer.Instance);

        /// <summary>While the returned scope lasts, the description-imported materials of the
        /// model at <paramref name="assetPath"/> are read without their preprocessors.</summary>
        internal static IDisposable Defer(string assetPath) => new Deferral(assetPath);

        sealed class Deferral : IDisposable
        {
            readonly string m_Previous = s_Deferring;
            internal Deferral(string assetPath) => s_Deferring = assetPath;
            public void Dispose() => s_Deferring = m_Previous;
        }

        /// <summary>The description of a material read under <see cref="Defer"/>, once.</summary>
        internal static MaterialDescription TakeDeferred(Material material) =>
            s_Deferred.Remove(material, out var description) ? description : null;

        internal static void Apply(Material material, YNode import, IReferenceResolver resolver)
        {
            var description = Read(import, resolver, material.name);
            var assetPath = import.Str("assetPath");
            using (ObjectRegistry.AsAssets())
            {
                if (import.Str("importer") == "legacy")
                    Legacy(description, material);
                else if (s_Deferring != null && s_Deferring == assetPath)
                    s_Deferred[material] = description;
                else
                {
                    // In the model's import (its postprocessors' objects), else one of its own
                    // (a copy a scene's expansion reads).
                    Describe(AssetPostprocessing.ExistingSession(assetPath) ?? new ImportSession(assetPath, new AssetImportContext(assetPath), null),
                        description, material);
                    MaterialValidation.DeferImportedEditor(material);
                }
            }
        }

        /// <summary>A model's slots without a material of its own get the render pipeline's default
        /// material (URP's Lit.mat), Default-Material without one (measured:
        /// tests/projects/modelimport). A model loaded while the pipeline asset itself is being read
        /// (URP's global settings reference core's ProbeSamplingDebugMesh.fbx, and the asset's
        /// renderer loads them) gets it once the pipeline asset is read whole, as the Editor's
        /// models have it from their import (tests/projects/rpglobalsettings).</summary>
        internal static void UsePipelineDefault(LoadedFile file)
        {
            var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            if (pipeline != null && AssetDatabaseRuntime.WhenLoaded(pipeline, () => ReplaceBuiltinDefault(file, pipeline)))
                return;
            ReplaceBuiltinDefault(file, pipeline);
        }

        static void ReplaceBuiltinDefault(LoadedFile file, UnityEngine.Rendering.RenderPipelineAsset pipeline)
        {
            var replacement = pipeline != null ? pipeline.defaultMaterial : null;
            if (replacement == null) return;
            var builtin = BuiltinShaders.Resolve(BuiltinShaders.ExtraGuid, BuiltinShaders.DefaultMaterialFileId) as Material;
            if (builtin == null) return;
            foreach (var obj in file.Objects.Values)
            {
                if (obj is not Renderer renderer) continue;
                for (int i = 0; i < renderer.m_Materials.Length; i++)
                    if (ReferenceEquals(renderer.m_Materials[i], builtin)) renderer.m_Materials[i] = replacement;
            }
        }

        static MaterialDescription Read(YNode import, IReferenceResolver resolver, string name)
        {
            var d = new MaterialDescription { m_MaterialName = name };
            foreach (var (key, value) in Entries(import.Get("floats"))) d.m_Floats[key] = value.AsFloat();
            foreach (var (key, value) in Entries(import.Get("vectors")))
                d.m_Vectors[key] = new Vector4(value.Float("x"), value.Float("y"), value.Float("z"), value.Float("w"));
            foreach (var (key, value) in Entries(import.Get("strings"))) d.m_Strings[key] = value.AsString();
            foreach (var (key, value) in Entries(import.Get("textures")))
            {
                var reference = value.Ref("texture");
                d.m_Textures[key] = new TexturePropertyDescription
                {
                    path = value.Str("path"),
                    relativePath = value.Str("relativePath"),
                    texture = reference.IsNull ? null : resolver.Resolve(reference, typeof(Texture)) as Texture,
                    offset = value.Vector2("offset"),
                    scale = value.Vector2("scale"),
                };
            }
            return d;
        }

        static IEnumerable<(string, YNode)> Entries(YNode map)
        {
            if (map == null || map.Kind != YKind.Map) yield break;
            for (int i = 0; i < map.Keys.Length; i++) yield return (map.Keys[i], map.Values[i]);
        }

        /// <summary>The loaded postprocessors with <c>OnPreprocessMaterialDescription</c>, in order
        /// (ties by type name). Unbound's stand-ins for Editor classes it does not implement are
        /// left out.</summary>
        static List<(int, Type, MethodInfo)> Preprocessors() =>
            s_Preprocessors ??= AssetPostprocessorHooks.Find("OnPreprocessMaterialDescription",
                new[] { typeof(MaterialDescription), typeof(Material), typeof(AnimationClip[]) },
                typeof(FBXMaterialDescriptionPreprocessor));

        /// <summary>A material made from its description: runs the preprocessors, then validates
        /// the material with its shader's editor (render pipelines' and projects' editors, <see
        /// cref="MaterialValidation"/>). Each time a preprocessor sets the material's shader, the
        /// new shader's editor validates it at once too, as the Editor does when a material's
        /// shader changes (matches the Unity 6000.0.71f1 Editor, tests/projects/modelimport with
        /// URP, <c>stages.*</c>: Lit's validation as URP's FBX preprocessor sets Lit sees the
        /// emission and GI flags the Editor's own FBX preprocessor set before, a Shader Graph shader
        /// set next has its queue control before the next preprocessor runs, and a material whose
        /// shader changes again keeps the colour Lit's first validation gave it; the last
        /// validation gives the others Lit's <c>_Color</c> from the base colour the URP
        /// preprocessor set).</summary>
        internal static void Describe(ImportSession import, MaterialDescription description, Material material)
        {
            using (ObjectRegistry.AsAssets())
            {
                material.m_ValidateOnShaderChange = true;
                try
                {
                    AssetPostprocessorHooks.Run(Preprocessors(), import,
                        new object[] { description, material, Array.Empty<AnimationClip>() });
                }
                finally
                {
                    material.m_ValidateOnShaderChange = false;
                }
                MaterialValidation.Validate(material, editorsOfTheEditor: false);
            }
        }

        /// <summary>
        /// The legacy importer's material (measured: tests/projects/modelimport, the built-in
        /// pipeline and URP): the render pipeline's default shader (Standard without a pipeline)
        /// with the diffuse colour as it is (no factor, no colour-space conversion), the opacity of
        /// the transparency factor (or 1 minus the transparent colour when the factor is 1),
        /// transparent below 1 or with a transparent-colour texture, the diffuse, normal (Bump or
        /// NormalMap) and emissive textures, smoothness 0, and baked emission only from an emissive
        /// texture; an opaque material with one of those textures is tagged Opaque. The colour and texture go to the shader's main colour and texture and to
        /// <c>_Color</c>/<c>_MainTex</c>; Standard's blend mode properties are written only to a
        /// shader that has <c>_Mode</c>. Autodesk Interactive materials take the pipeline's Autodesk
        /// Interactive shaders, or keep their built-in shader's colour, opacity, roughness (as
        /// smoothness), metallic and emission without a pipeline. A model read while the pipeline
        /// asset itself loads gets its materials once the asset is read whole.
        /// </summary>
        static void Legacy(MaterialDescription d, Material m)
        {
            var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            if (pipeline != null && AssetDatabaseRuntime.WhenLoaded(pipeline, () =>
                {
                    using (ObjectRegistry.AsAssets()) Legacy(d, m, pipeline);
                }))
                return;
            Legacy(d, m, pipeline);
        }

        static void Legacy(MaterialDescription d, Material m, UnityEngine.Rendering.RenderPipelineAsset pipeline)
        {
            if (d.TryGetProperty("renderAPI", out string api) && api == "SFX_PBS_SHADER")
            {
                if (pipeline != null ? PipelineAutodeskInteractive(d, m, pipeline) : LegacyAutodeskInteractive(d, m))
                    return;
                Stubs.Ignored("Legacy model import of an Autodesk Interactive material without its shader",
                    "converted like any other legacy material (unmeasured)");
            }
            var shader = pipeline != null ? pipeline.defaultShader : null;
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) return;
            StartWith(m, shader);
            float opacity = 1f;
            if (d.TryGetProperty("TransparencyFactor", out float factor))
                opacity = factor == 1f ? 1f : 1f - factor;
            if (opacity == 1f && d.TryGetProperty("TransparentColor", out Vector4 transparent))
                opacity = transparent.x == 1f ? 1f : 1f - transparent.x;
            bool transparentMaterial = opacity < 1f || d.TryGetProperty("TransparentColor", out TexturePropertyDescription _);
            Transparency(m, transparentMaterial);
            bool textured = false;
            var color = d.TryGetProperty("DiffuseColor", out Vector4 diffuse)
                ? new Color(diffuse.x, diffuse.y, diffuse.z, opacity)
                : new Color(1f, 1f, 1f, opacity);
            // The main colour first; _Color then takes it as read back (URP: _Color is one
            // colour-space round trip further from the description than _BaseColor).
            m.color = color;
            if (shader.FlaggedProperty(UnityEngine.Rendering.ShaderPropertyFlags.MainColor, s_Color) != s_Color && m.HasProperty(s_Color))
                m.SetColor(s_Color, m.color);
            if (d.TryGetProperty("DiffuseColor", out TexturePropertyDescription texture))
            {
                int main = shader.FlaggedProperty(UnityEngine.Rendering.ShaderPropertyFlags.MainTexture, s_MainTex);
                if (main != s_MainTex) SetTexture(m, main, texture);
                SetTexture(m, s_MainTex, texture);
                textured = true;
            }
            if (d.TryGetProperty("Bump", out texture) || d.TryGetProperty("NormalMap", out texture))
            {
                SetTexture(m, Shader.PropertyToID("_BumpMap"), texture);
                m.EnableKeyword("_NORMALMAP");
                textured = true;
            }
            if (d.TryGetProperty("EmissiveColor", out texture))
            {
                SetTexture(m, Shader.PropertyToID("_EmissionMap"), texture);
                m.SetColor("_EmissionColor", Color.white);
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
                textured = true;
            }
            m.SetFloat("_Glossiness", 0f);
            // An opaque material with one of these textures is tagged Opaque (measured with URP's
            // Lit, whose untextured legacy materials read no RenderType; Standard reads Opaque
            // either way). Each measured case also had a normal map or emission: a diffuse
            // texture alone is unmeasured.
            if (textured && !transparentMaterial) m.SetOverrideTag("RenderType", "Opaque");
        }

        static bool LegacyAutodeskInteractive(MaterialDescription d, Material m)
        {
            var shader = Shader.Find("Autodesk Interactive");
            if (shader == null) return false;
            m.shader = shader;
            if (!d.TryGetProperty("opacity", out float opacity)) opacity = 1f;
            Transparency(m, opacity < 1f);
            if (d.TryGetProperty("base_color", out Vector4 color))
                m.SetColor("_Color", new Color(color.x, color.y, color.z, opacity));
            if (d.TryGetProperty("roughness", out float roughness)) m.SetFloat("_Glossiness", roughness);
            if (d.TryGetProperty("metallic", out float metallic)) m.SetFloat("_Metallic", metallic);
            if (d.TryGetProperty("emissive", out Vector4 emissive) && (emissive.x > 0f || emissive.y > 0f || emissive.z > 0f))
            {
                if (d.TryGetProperty("emissive_intensity", out float intensity)) emissive *= intensity;
                m.SetColor("_EmissionColor", emissive);
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            }
            return true;
        }

        /// <summary>
        /// An Autodesk Interactive material under a render pipeline (measured with URP:
        /// tests/projects/modelimport): the pipeline's masked shader with a mask threshold, its
        /// transparent one with an opacity, its plain one otherwise (the rule of the pipeline's own
        /// material description import). The base colour, emission (times its intensity),
        /// roughness and metallic are written as they are, zero when the file has none; the
        /// opacity, mask threshold and UV offset (V negated) and scale only when the file has them.
        /// No blend mode, keyword, queue or GI flag is set. False when the pipeline has no such
        /// shader: the material then takes the default shader like the others (unmeasured).
        /// </summary>
        static bool PipelineAutodeskInteractive(MaterialDescription d, Material m, UnityEngine.Rendering.RenderPipelineAsset pipeline)
        {
            bool masked = d.TryGetProperty("mask_threshold", out float threshold);
            bool transparent = d.TryGetProperty("opacity", out float opacity);
            var shader = masked ? pipeline.autodeskInteractiveMaskedShader
                : transparent ? pipeline.autodeskInteractiveTransparentShader
                : pipeline.autodeskInteractiveShader;
            if (shader == null) return false;
            StartWith(m, shader);
            d.TryGetProperty("base_color", out Vector4 color);
            m.SetColor("_Color", color);
            d.TryGetProperty("emissive", out Vector4 emissive);
            if (d.TryGetProperty("emissive_intensity", out float intensity)) emissive *= intensity;
            m.SetColor("_EmissionColor", emissive);
            d.TryGetProperty("roughness", out float roughness);
            m.SetFloat("_Glossiness", roughness);
            d.TryGetProperty("metallic", out float metallic);
            m.SetFloat("_Metallic", metallic);
            if (transparent) m.SetFloat("_Opacity", opacity);
            if (masked) m.SetFloat("_OpacityThreshold", threshold);
            if (d.TryGetProperty("uv_offset", out Vector4 offset)) m.SetVector("_UvOffset", new Vector4(offset.x, -offset.y, 0f, 0f));
            if (d.TryGetProperty("uv_scale", out Vector4 scale)) m.SetVector("_UvTiling", new Vector4(scale.x, scale.y, 0f, 0f));
            return true;
        }

        /// <summary>The legacy importer makes its material with the shader it picks (the material
        /// arrives with the engine's placeholder shader and no values of its own).</summary>
        static void StartWith(Material m, Shader shader)
        {
            if (shader == m.shader) return;
            m.StartWith(shader);
            m.MarkDirty();
            // Made with its shader: validated by that shader's editor before the importer sets its
            // values (measured: tests/projects/modelimport stages.ExoticLegacy.*, the queue control
            // of URP's Autodesk Interactive graphs).
            MaterialValidation.Validate(m, editorsOfTheEditor: false);
        }

        /// <summary>The legacy importer's blend set-up; Standard's blend mode properties only on a
        /// shader with <c>_Mode</c> (URP's Lit keeps its own, measured).</summary>
        static void Transparency(Material m, bool transparent)
        {
            if (m.HasProperty("_Mode"))
            {
                m.SetFloat("_Mode", transparent ? 3f : 0f);
                m.SetFloat("_SrcBlend", 1f);
                m.SetFloat("_DstBlend", transparent ? 10f : 0f);
                m.SetFloat("_ZWrite", transparent ? 0f : 1f);
            }
            m.SetOverrideTag("RenderType", transparent ? "Transparent" : "");
            if (transparent) m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = transparent ? 3000 : -1;
        }

        static void SetTexture(Material m, int name, TexturePropertyDescription texture)
        {
            m.SetTexture(name, texture.texture);
            m.SetTextureOffset(name, texture.offset);
            m.SetTextureScale(name, texture.scale);
        }
    }
}
