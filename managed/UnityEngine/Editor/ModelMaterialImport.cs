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
        static List<(int Order, Type Type, MethodInfo Method)> s_Preprocessors;

        internal static void Apply(Material material, YNode import, IReferenceResolver resolver)
        {
            var description = Read(import, resolver, material.name);
            var assetPath = import.Str("assetPath");
            using (ObjectRegistry.AsAssets())
            {
                if (import.Str("importer") == "legacy")
                    Legacy(assetPath, description, material);
                else
                    Preprocess(assetPath, description, material);
            }
        }

        /// <summary>A model's slots without a material of its own get the render pipeline's default
        /// material (URP's Lit.mat), Default-Material without one (measured:
        /// tests/projects/modelimport).</summary>
        internal static void UsePipelineDefault(LoadedFile file)
        {
            var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            var replacement = pipeline != null ? pipeline.defaultMaterial : null;
            if (replacement == null) return;
            var builtin = BuiltinShaders.Resolve(BuiltinShaders.ExtraGuid, DefaultMaterialFileId) as Material;
            if (builtin == null) return;
            foreach (var obj in file.Objects.Values)
            {
                if (obj is not Renderer renderer) continue;
                for (int i = 0; i < renderer.m_Materials.Length; i++)
                    if (ReferenceEquals(renderer.m_Materials[i], builtin)) renderer.m_Materials[i] = replacement;
            }
        }

        const long DefaultMaterialFileId = 10303;

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
        static List<(int, Type, MethodInfo)> Preprocessors()
        {
            if (s_Preprocessors != null) return s_Preprocessors;
            var found = new List<(int, Type, MethodInfo)>();
            var parameters = new[] { typeof(MaterialDescription), typeof(Material), typeof(AnimationClip[]) };
            void Consider(Type type)
            {
                if (type.IsAbstract || !typeof(AssetPostprocessor).IsAssignableFrom(type)) return;
                if (type.IsDefined(typeof(StubAttribute), false)) return;
                var method = type.GetMethod("OnPreprocessMaterialDescription",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, parameters, null);
                if (method == null) return;
                try
                {
                    var instance = (AssetPostprocessor)Activator.CreateInstance(type, nonPublic: true);
                    found.Add((instance.GetPostprocessOrder(), type, method));
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
            Consider(typeof(FBXMaterialDescriptionPreprocessor));
            foreach (var assembly in GameDomain.Assemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = Array.FindAll(e.Types, t => t != null); }
                foreach (var type in types)
                    if (type != typeof(FBXMaterialDescriptionPreprocessor)) Consider(type);
            }
            found.Sort((a, b) => a.Item1 != b.Item1 ? a.Item1.CompareTo(b.Item1)
                : string.CompareOrdinal(a.Item2.FullName, b.Item2.FullName));
            return s_Preprocessors = found;
        }

        static void Preprocess(string assetPath, MaterialDescription description, Material material)
        {
            var context = new AssetImportContext(assetPath);
            foreach (var (_, type, method) in Preprocessors())
            {
                try
                {
                    var postprocessor = (AssetPostprocessor)Activator.CreateInstance(type, nonPublic: true);
                    postprocessor.assetPath = assetPath;
                    postprocessor.context = context;
                    method.Invoke(postprocessor, new object[] { description, material, Array.Empty<AnimationClip>() });
                }
                catch (TargetInvocationException e)
                {
                    Debug.LogException(e.InnerException ?? e);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
            Validate(material);
        }

        /// <summary>
        /// The Editor validates the material with its shader's <see cref="ShaderGUI"/> (the
        /// pipeline-specific editor first, else <c>CustomEditor</c>), when Unbound has that class:
        /// render pipelines' and projects' (measured: URP's model materials get their blend states,
        /// queue control and GI flags; the built-in pipeline's Standard materials keep the
        /// preprocessor's, tests/projects/modelimport).
        /// </summary>
        static void Validate(Material material)
        {
            var shader = material.shader;
            if (shader == null) return;
            var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            string name = null;
            if (pipeline != null)
                foreach (var (editor, pipelineType) in shader.m_PipelineEditors)
                    if (pipeline.GetType().FullName == pipelineType || pipeline.GetType().Name == pipelineType)
                    {
                        name = editor;
                        break;
                    }
            name ??= shader.m_CustomEditor;
            var type = string.IsNullOrEmpty(name) ? null : ShaderGuiType(name);
            if (type == null) return;
            try
            {
                var gui = (ShaderGUI)Activator.CreateInstance(type, nonPublic: true);
                gui.ValidateMaterial(material);
            }
            catch (Exception e)
            {
                Debug.LogException(e is TargetInvocationException t ? t.InnerException ?? e : e);
            }
        }

        static readonly Dictionary<string, Type> s_ShaderGuis = new Dictionary<string, Type>();

        static Type ShaderGuiType(string name)
        {
            if (s_ShaderGuis.TryGetValue(name, out var cached)) return cached;
            Type found = null;
            foreach (var assembly in GameDomain.Assemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = Array.FindAll(e.Types, t => t != null); }
                foreach (var type in types)
                {
                    if (type.IsAbstract || !typeof(ShaderGUI).IsAssignableFrom(type)) continue;
                    if (type.FullName != name && type.Name != name) continue;
                    if (type.IsDefined(typeof(StubAttribute), false)) continue;
                    found = type;
                    break;
                }
                if (found != null) break;
            }
            return s_ShaderGuis[name] = found;
        }

        /// <summary>
        /// The legacy importer's material (measured: tests/projects/modelimport): Standard with the
        /// diffuse colour as it is (no factor, no colour-space conversion), the opacity of the
        /// transparency factor (or 1 minus the transparent colour when the factor is 1),
        /// transparent below 1 or with a transparent-colour texture, the diffuse, normal (Bump or
        /// NormalMap) and emissive textures, smoothness 0, and baked emission only from an emissive
        /// texture. The colour and diffuse texture also go to the shader's main colour and texture
        /// (<see cref="Material.color"/>, <see cref="Material.mainTexture"/>: the [MainColor] and
        /// [MainTexture] properties, else the same _Color and _MainTex, as for Standard).
        /// Under a render pipeline the material is one made with the pipeline's
        /// <see cref="UnityEngine.Rendering.RenderPipelineAsset.defaultShader"/>, holding that
        /// shader's own defaults before the same rule (observed in the 6000.0.71f1 URP capture of
        /// tests/projects/modelimport: URP's Lit with Lit's defaults; other pipelines and releases
        /// are not measured); a pipeline without one is an error and the material stays as it is.
        /// Autodesk Interactive materials keep their shader, colour, opacity, roughness (as
        /// smoothness), metallic and emission (under a render pipeline too: what the Editor sets
        /// there on the pipeline's Autodesk Interactive shaders is not reproduced).
        /// </summary>
        static void Legacy(string assetPath, MaterialDescription d, Material m)
        {
            if (d.TryGetProperty("renderAPI", out string api) && api == "SFX_PBS_SHADER")
            {
                LegacyAutodeskInteractive(d, m);
                return;
            }
            var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            Shader shader;
            if (pipeline == null)
                shader = Shader.Find("Standard");
            else
            {
                try
                {
                    shader = pipeline.defaultShader;
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    return;
                }
                if (shader == null)
                {
                    Debug.LogError($"Unbound: the render pipeline asset '{pipeline.name}' ({pipeline.GetType().FullName}) has no default shader, " +
                        $"so the legacy model importer cannot make material '{m.name}' of {assetPath}; the material is left as it was.", m);
                    return;
                }
            }
            if (shader == null) return;
            m.shader = shader;
            if (pipeline != null) MadeWith(m, shader);
            float opacity = 1f;
            if (d.TryGetProperty("TransparencyFactor", out float factor))
                opacity = factor == 1f ? 1f : 1f - factor;
            if (opacity == 1f && d.TryGetProperty("TransparentColor", out Vector4 transparent))
                opacity = transparent.x == 1f ? 1f : 1f - transparent.x;
            Transparency(m, opacity < 1f || d.TryGetProperty("TransparentColor", out TexturePropertyDescription _));
            var color = d.TryGetProperty("DiffuseColor", out Vector4 diffuse)
                ? new Color(diffuse.x, diffuse.y, diffuse.z, opacity)
                : new Color(1f, 1f, 1f, opacity);
            m.SetColor("_Color", color);
            m.color = color;
            if (d.TryGetProperty("DiffuseColor", out TexturePropertyDescription texture))
            {
                SetTexture(m, "_MainTex", texture);
                m.mainTexture = texture.texture;
                m.mainTextureOffset = texture.offset;
                m.mainTextureScale = texture.scale;
            }
            if (d.TryGetProperty("Bump", out texture) || d.TryGetProperty("NormalMap", out texture))
            {
                SetTexture(m, "_BumpMap", texture);
                m.EnableKeyword("_NORMALMAP");
            }
            if (d.TryGetProperty("EmissiveColor", out texture))
            {
                SetTexture(m, "_EmissionMap", texture);
                m.SetColor("_EmissionColor", Color.white);
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            }
            m.SetFloat("_Glossiness", 0f);
        }

        /// <summary>
        /// Makes the model document's material (named, registered and referenced by the model, with
        /// Standard until now) hold what a material made with <paramref name="shader"/> holds: every
        /// property value, texture slot, keyword and render queue of a new material of that shader
        /// (<see cref="Material.CopyPropertiesFromMaterial"/>, which leaves the shader as it is),
        /// instead of the values <see cref="Material.shader"/> keeps from Standard for the names
        /// both declare and Standard's texture slots. Its name, identity and references stay.
        /// </summary>
        static void MadeWith(Material m, Shader shader)
        {
            var made = new Material(shader);
            try
            {
                m.CopyPropertiesFromMaterial(made);
            }
            finally
            {
                Object.DestroyImmediate(made);
            }
        }

        static void LegacyAutodeskInteractive(MaterialDescription d, Material m)
        {
            var shader = Shader.Find("Autodesk Interactive");
            if (shader == null) return;
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
        }

        static void Transparency(Material m, bool transparent)
        {
            m.SetFloat("_Mode", transparent ? 3f : 0f);
            m.SetOverrideTag("RenderType", transparent ? "Transparent" : "");
            m.SetFloat("_SrcBlend", 1f);
            m.SetFloat("_DstBlend", transparent ? 10f : 0f);
            m.SetFloat("_ZWrite", transparent ? 0f : 1f);
            if (transparent) m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = transparent ? 3000 : -1;
        }

        static void SetTexture(Material m, string name, TexturePropertyDescription texture)
        {
            m.SetTexture(name, texture.texture);
            m.SetTextureOffset(name, texture.offset);
            m.SetTextureScale(name, texture.scale);
        }
    }
}
