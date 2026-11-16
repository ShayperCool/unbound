using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEditor;
using UnityEngine.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// The legacy model importer's materials under a render pipeline, as measured with URP in
    /// tests/projects/modelimport (Unity 6000.0.71f1): the pipeline's default shader from the
    /// start (no defaults of the engine's placeholder shader stay), the main colour and texture
    /// beside <c>_Color</c>/<c>_MainTex</c>, Standard's blend floats only where the shader has
    /// <c>_Mode</c>, and the pipeline's Autodesk Interactive shaders with the description's
    /// values. The materials load through the same YAML body the engine's model importer writes.
    /// </summary>
    [Collection("RenderSettings state")]
    public sealed class ModelMaterialImportTests : IDisposable
    {
        const string PlaceholderGuid = "0000000000000000f000000000000001";
        const string TextureGuid = "0000000000000000f000000000000002";
        static int s_Made;

        readonly List<Object> m_Owned = new List<Object>();
        readonly RenderPipelineAsset m_Previous = GraphicsSettings.defaultRenderPipeline;
        readonly Shader m_Placeholder;
        readonly Texture2D m_Texture;

        public ModelMaterialImportTests()
        {
            // Stands in for the placeholder Standard shader: other defaults than the pipeline's.
            m_Placeholder = Make("Placeholder", "_Color (\"Color\", Color) = (1, 1, 1, 1) _MainTex (\"Albedo\", 2D) = \"white\" {}"
                + " _Parallax (\"Height\", Float) = 0.02 _Mode (\"Mode\", Float) = 0 _SrcBlend (\"Src\", Float) = 1"
                + " _DstBlend (\"Dst\", Float) = 0 _ZWrite (\"ZWrite\", Float) = 1 _Glossiness (\"Smoothness\", Float) = 0.5",
                "Tags { \"RenderType\"=\"Opaque\" }");
            m_Texture = Own(new Texture2D(2, 2));
        }

        public void Dispose()
        {
            GraphicsSettings.defaultRenderPipeline = m_Previous;
            foreach (var o in m_Owned) Object.DestroyImmediate(o);
        }

        T Own<T>(T o) where T : Object
        {
            m_Owned.Add(o);
            return o;
        }

        Shader Make(string name, string properties, string tags = "") =>
            Own(ShaderUtil.CreateShaderAsset("Shader \"Tests/ModelMaterialImport/" + name + (++s_Made) + "\" {\n"
                + "Properties { " + properties + " }\nSubShader { " + tags + " Pass { } } }"));

        /// <summary>A Lit-like shader: main colour and texture of their own, no _Mode, no RenderType tag.</summary>
        Shader Lit() => Make("Lit", "[MainColor] _BaseColor (\"Base\", Color) = (1, 1, 1, 1) [MainTexture] _BaseMap (\"Base\", 2D) = \"white\" {}"
            + " _Color (\"Color\", Color) = (1, 1, 1, 1) _MainTex (\"Albedo\", 2D) = \"white\" {} _Parallax (\"Height\", Float) = 0.005"
            + " _SrcBlend (\"Src\", Float) = 1 _DstBlend (\"Dst\", Float) = 0 _ZWrite (\"ZWrite\", Float) = 1"
            + " _BumpMap (\"Normal\", 2D) = \"bump\" {} _EmissionMap (\"Emission\", 2D) = \"white\" {}"
            + " _EmissionColor (\"Emission\", Color) = (0, 0, 0, 1) _Glossiness (\"Smoothness\", Float) = 0.5");

        Shader Interactive(string name) => Make(name, "_Color (\"Color\", Color) = (1, 1, 1, 1) _EmissionColor (\"Emission\", Color) = (0, 0, 0, 1)"
            + " _Glossiness (\"Roughness\", Float) = 0.5 _Metallic (\"Metallic\", Float) = 0 _Opacity (\"Opacity\", Float) = 1"
            + " _OpacityThreshold (\"Threshold\", Float) = 0 _UvOffset (\"Offset\", Vector) = (0, 0, 0, 0) _UvTiling (\"Tiling\", Vector) = (1, 1, 0, 0)");

        sealed class Pipeline : RenderPipelineAsset
        {
            internal Shader Default, Plain, Transparent, Masked;
            protected override RenderPipeline CreatePipeline() => null;
            public override Shader defaultShader => Default;
            public override Shader autodeskInteractiveShader => Plain;
            public override Shader autodeskInteractiveTransparentShader => Transparent;
            public override Shader autodeskInteractiveMaskedShader => Masked;
        }

        Pipeline Use(Shader lit)
        {
            var pipeline = Own(ScriptableObject.CreateInstance<Pipeline>());
            pipeline.Default = lit;
            pipeline.Plain = Interactive("Plain");
            pipeline.Transparent = Interactive("Transparent");
            pipeline.Masked = Interactive("Masked");
            GraphicsSettings.defaultRenderPipeline = pipeline;
            return pipeline;
        }

        sealed class Resolver : IReferenceResolver
        {
            readonly Dictionary<string, Object> m_Objects;
            internal Resolver(Dictionary<string, Object> objects) => m_Objects = objects;
            public Object Resolve(ObjectRef reference, Type expected) =>
                reference.Guid != null && m_Objects.TryGetValue(reference.Guid, out var o) ? o : null;
        }

        /// <summary>A model's material sub-asset as the engine writes it, made by the legacy
        /// importer. Each section is "name: value" entries; vectors and textures as block maps.</summary>
        Material Import(string[] floats = null, (string Name, Vector4 Value)[] vectors = null, string[] strings = null, bool texture = false)
        {
            var yaml = new System.Text.StringBuilder(@"Material:
  m_Name: M_Test
  m_Shader: {fileID: 4800000, guid: " + PlaceholderGuid + @", type: 3}
  m_UnboundMaterialImport:
    importer: legacy
    assetPath: Assets/Models/Test.fbx
");
            void Section(string name, IEnumerable<string> lines)
            {
                var list = new List<string>(lines ?? Array.Empty<string>());
                if (list.Count == 0) { yaml.Append("    " + name + ": {}\n"); return; }
                yaml.Append("    " + name + ":\n");
                foreach (var line in list) yaml.Append(line);
            }
            Section("floats", Array.ConvertAll(floats ?? Array.Empty<string>(), f => "      " + f + "\n"));
            Section("vectors", Array.ConvertAll(vectors ?? Array.Empty<(string, Vector4)>(), v =>
                "      " + v.Name + ":\n        x: " + v.Value.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                + "\n        y: " + v.Value.y.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                + "\n        z: " + v.Value.z.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                + "\n        w: " + v.Value.w.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "\n"));
            Section("strings", Array.ConvertAll(strings ?? Array.Empty<string>(), t => "      " + t + "\n"));
            Section("textures", texture ? new[] { "      DiffuseColor:\n        path: a.png\n        relativePath: a.png\n"
                + "        texture: {fileID: 2800000, guid: " + TextureGuid + ", type: 3}\n"
                + "        offset:\n          x: 0\n          y: 0\n        scale:\n          x: 1\n          y: 1\n" } : null);
            var m = Own(new Material());
            m.ReadSerialized(TestYaml.DocumentBody(yaml.ToString()), new Resolver(new Dictionary<string, Object>
            {
                [PlaceholderGuid] = m_Placeholder,
                [TextureGuid] = m_Texture,
            }));
            return m;
        }

        static readonly (string, Vector4)[] Diffuse = { ("DiffuseColor", new Vector4(0.2f, 0.4f, 0.9f, 1f)) };
        static readonly string[] Interactive_ = { "renderAPI: SFX_PBS_SHADER" };

        static void Near(Color expected, Color actual)
        {
            for (int i = 0; i < 4; i++) Assert.Equal(expected[i], actual[i], 5);
        }

        [Fact]
        public void ALegacyMaterialUnderAPipelineStartsWithItsDefaultShader()
        {
            var lit = Lit();
            Use(lit);
            var m = Import(floats: new[] { "TransparencyFactor: 0.6" }, vectors: Diffuse);
            Assert.Same(lit, m.shader);
            // The placeholder's 0.02 does not stay: the material was made with the pipeline's shader.
            Assert.Equal(0.005f, m.GetFloat("_Parallax"));
            Near(new Color(0.2f, 0.4f, 0.9f, 0.4f), m.GetColor("_BaseColor"));
            Near(m.GetColor("_BaseColor"), m.GetColor("_Color"));
            // Transparent, but the shader has no _Mode: its own blend values stay.
            Assert.Equal(1f, m.GetFloat("_SrcBlend"));
            Assert.Equal(0f, m.GetFloat("_DstBlend"));
            Assert.Equal(1f, m.GetFloat("_ZWrite"));
            Assert.Equal("Transparent", m.GetTag("RenderType", false));
            Assert.True(m.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON"));
            Assert.Equal(3000, m.renderQueue);
            Assert.Equal(0f, m.GetFloat("_Glossiness"));
        }

        [Fact]
        public void ATexturedOpaqueLegacyMaterialIsTaggedOpaqueAndAnUntexturedOneIsNot()
        {
            Use(Lit());
            var textured = Import(texture: true);
            Assert.Same(m_Texture, textured.GetTexture("_BaseMap"));
            Assert.Same(m_Texture, textured.GetTexture("_MainTex"));
            Assert.Equal("Opaque", textured.GetTag("RenderType", false));
            Assert.Equal(-1, textured.rawRenderQueue);
            var plain = Import(vectors: Diffuse);
            // No override: the shader's own tag answers (URP's Lit reads none there).
            Assert.False(plain.m_Tags.ContainsKey("RenderType"));
            Assert.False(plain.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON"));
        }

        [Fact]
        public void WithoutAPipelineTheLegacyMaterialKeepsTheStandardPath()
        {
            // Control outside the pipeline: Standard (when the build has it) with its blend mode.
            GraphicsSettings.defaultRenderPipeline = null;
            var m = Import(floats: new[] { "TransparencyFactor: 0.6" }, vectors: Diffuse);
            var standard = Shader.Find("Standard");
            if (standard == null)
            {
                Assert.Same(m_Placeholder, m.shader);
                return;
            }
            Assert.Same(standard, m.shader);
            Assert.Equal(3f, m.GetFloat("_Mode"));
            Assert.Equal(10f, m.GetFloat("_DstBlend"));
            Assert.Equal(0f, m.GetFloat("_ZWrite"));
        }

        [Fact]
        public void AnAutodeskInteractiveMaterialTakesThePipelinesShaderAndTheDescriptionsValues()
        {
            var pipeline = Use(Lit());
            var transparent = Import(floats: new[] { "opacity: 0.8", "roughness: 0.3", "metallic: 0.6", "emissive_intensity: 2" },
                vectors: new[] { ("base_color", new Vector4(0.4f, 0.3f, 0.2f, 1f)), ("emissive", new Vector4(0.2f, 0.1f, 0f, 1f)),
                    ("uv_offset", new Vector4(0.25f, 0.5f, 0f, 1f)), ("uv_scale", new Vector4(2f, 3f, 0f, 1f)) },
                strings: Interactive_);
            Assert.Same(pipeline.Transparent, transparent.shader);
            Near(new Color(0.4f, 0.3f, 0.2f, 1f), transparent.GetColor("_Color"));
            Near(new Color(0.4f, 0.2f, 0f, 2f), transparent.GetColor("_EmissionColor"));
            Assert.Equal(0.3f, transparent.GetFloat("_Glossiness"));
            Assert.Equal(0.6f, transparent.GetFloat("_Metallic"));
            Assert.Equal(0.8f, transparent.GetFloat("_Opacity"));
            Assert.Equal(new Vector4(0.25f, -0.5f, 0f, 0f), transparent.GetVector("_UvOffset"));
            Assert.Equal(new Vector4(2f, 3f, 0f, 0f), transparent.GetVector("_UvTiling"));
            // No blend mode, keyword, queue or GI flag of the built-in conversion.
            Assert.Empty(transparent.shaderKeywords);
            Assert.Equal(-1, transparent.rawRenderQueue);
            Assert.Equal(MaterialGlobalIlluminationFlags.EmissiveIsBlack, transparent.globalIlluminationFlags);

            // A mask threshold picks the masked shader; what the file lacks is written as zero.
            var masked = Import(floats: new[] { "mask_threshold: 0.4" }, strings: Interactive_);
            Assert.Same(pipeline.Masked, masked.shader);
            Assert.Equal(new Color(0f, 0f, 0f, 0f), masked.GetColor("_Color"));
            Assert.Equal(new Color(0f, 0f, 0f, 0f), masked.GetColor("_EmissionColor"));
            Assert.Equal(0f, masked.GetFloat("_Glossiness"));
            Assert.Equal(0.4f, masked.GetFloat("_OpacityThreshold"));
            Assert.Equal(new Vector4(1f, 1f, 0f, 0f), masked.GetVector("_UvTiling"));

            var plain = Import(floats: new[] { "roughness: 0.3" }, strings: Interactive_);
            Assert.Same(pipeline.Plain, plain.shader);
            Assert.Equal(1f, plain.GetFloat("_Opacity"));
        }

        [Fact]
        public void APipelineWithoutAutodeskInteractiveShadersGivesItsDefaultShader()
        {
            var lit = Lit();
            var pipeline = Use(lit);
            pipeline.Plain = pipeline.Transparent = pipeline.Masked = null;
            var m = Import(floats: new[] { "roughness: 0.3" }, strings: Interactive_);
            Assert.Same(lit, m.shader);
        }
    }
}
