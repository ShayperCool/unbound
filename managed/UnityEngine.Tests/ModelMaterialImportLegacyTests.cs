using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Unbound.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Xunit;
using Object = UnityEngine.Object;

namespace Unbound.Tests
{
    /// <summary>
    /// The "Standard (Legacy)" conversion of a model's material sub-asset, read as the engine reads
    /// the model's documents: a Material document naming its placeholder shader (Standard in the
    /// documents the native importer writes) and the material's description
    /// (<c>m_UnboundMaterialImport</c>), loaded by <see cref="ObjectGraphBuilder"/>.
    ///
    /// Two kinds of expectations, kept apart:
    /// - Observed import behaviour (Unity 6000.0.71f1, URP 17.0.4, tests/projects/modelimport): under
    ///   a render pipeline the legacy material is one made with the pipeline's default shader, with
    ///   that shader's own defaults, before the legacy rule; the rule writes _Color and _MainTex by
    ///   name. Other pipelines and releases are not measured: the tests check the mechanism (the
    ///   material equals a new material of that shader), not any shader's or capture's values.
    /// - Documented main-property behaviour (Material.color, mainTexture, mainTextureOffset,
    ///   mainTextureScale, 6000.0 Scripting API): the [MainColor]/[MainTexture] property, else
    ///   _Color/_MainTex.
    /// A pipeline without a default shader is unsupported content: an error, and the material stays
    /// as the document made it.
    /// </summary>
    public class ModelMaterialImportLegacyTests
    {
        static int s_Made;

        const long MaterialFileId = 2100000;
        const long ShaderFileId = 4800000;
        const long TextureFileId = 2800000;

        // ------------------------------------------------------------------ fixtures

        /// <summary>The model document's placeholder: values for names the pipeline shader declares
        /// too (with other defaults), and a value and a texture slot of its own.</summary>
        static string PlaceholderSource(string name) => $@"Shader ""{name}""
{{
    Properties
    {{
        _Color (""Color"", Color) = (1,1,1,1)
        _MainTex (""Albedo"", 2D) = ""white"" {{}}
        _Cutoff (""Alpha Cutoff"", Range(0, 1)) = 0.5
        _Parallax (""Height Scale"", Range(0.005, 0.08)) = 0.02
        _EmissionColor (""Emission"", Color) = (0,0,0,1)
        _PlaceholderOnly (""Placeholder only"", Float) = 3
        _PlaceholderOnlyTex (""Placeholder only"", 2D) = ""white"" {{}}
    }}
    SubShader {{ Pass {{ }} }}
}}";

        /// <summary>A pipeline's default shader with every property kind: main colour and texture
        /// under other names, shared names with other defaults, an Int, a Vector, a keyword toggle
        /// on by default and a texture with a default texture (its importer's).</summary>
        static Shader PipelineDefault() => ShaderUtil.CreateShaderAsset($@"Shader ""Tests/ModelMaterialImport/PipelineDefault{++s_Made}""
{{
    Properties
    {{
        [MainColor] _BaseColor (""Base"", Color) = (0.5, 0.5, 0.5, 1)
        [MainTexture] _BaseMap (""Base"", 2D) = ""white"" {{}}
        _Color (""Color"", Color) = (1,1,1,1)
        _MainTex (""Albedo"", 2D) = ""white"" {{}}
        _Cutoff (""Alpha Cutoff"", Range(0, 1)) = 0.25
        _Parallax (""Height"", Range(0.005, 0.08)) = 0.005
        _EmissionColor (""Emission"", Color) = (0.125, 0.25, 0.5, 1)
        _Count (""Count"", Integer) = 7
        _Offsets (""Offsets"", Vector) = (1, 2, 3, 4)
        [Toggle(TESTS_MODELMATERIALIMPORT_FLAG)] _Flag (""Flag"", Float) = 1
        _DetailTex (""Detail"", 2D) = ""grey"" {{}}
    }}
    SubShader {{ Pass {{ }} }}
}}");

        sealed class Pipeline : RenderPipelineAsset
        {
            internal Shader Default;
            public override Shader defaultShader => Default;
            protected override RenderPipeline CreatePipeline() => null;
            // Destroying this test asset must not clean up the engine's current pipeline.
            protected override void OnDisable() { }
        }

        /// <summary>What a test makes and changes, undone in its finally: the render pipeline
        /// settings it replaces, the files it registers, the objects it owns.</summary>
        sealed class Scope
        {
            readonly RenderPipelineAsset m_Default = GraphicsSettings.defaultRenderPipeline;
            readonly RenderPipelineAsset m_Quality = QualitySettings.renderPipeline;
            readonly List<Object> m_Owned = new List<Object>();
            readonly List<string> m_Files = new List<string>();

            internal T Own<T>(T obj) where T : Object
            {
                m_Owned.Add(obj);
                return obj;
            }

            /// <summary>The current pipeline is <paramref name="pipeline"/> (null: none). A quality
            /// level's pipeline is cleared only when it has one.</summary>
            internal void UsePipeline(RenderPipelineAsset pipeline)
            {
                if (m_Quality != null) QualitySettings.renderPipeline = null;
                GraphicsSettings.defaultRenderPipeline = pipeline;
                Assert.Same(pipeline, GraphicsSettings.currentRenderPipeline);
            }

            internal string Register(Object obj, long fileId)
            {
                var guid = Guid.NewGuid().ToString("N");
                var file = new LoadedFile { Guid = guid, Path = "Assets/Tests/" + obj.name, MainObject = obj };
                file.Objects[fileId] = obj;
                AssetDatabaseRuntime.AddLoaded(file);
                m_Files.Add(guid);
                return guid;
            }

            internal void Restore()
            {
                GraphicsSettings.defaultRenderPipeline = m_Default;
                if (m_Quality != null) QualitySettings.renderPipeline = m_Quality;
                foreach (var guid in m_Files) AssetDatabaseRuntime.Invalidate(guid);
                for (int i = m_Owned.Count - 1; i >= 0; i--) Object.DestroyImmediate(m_Owned[i], true);
            }
        }

        static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);
        static string V(Vector4 v) => $"({F(v.x)}, {F(v.y)}, {F(v.z)}, {F(v.w)})";
        static string V(Vector2 v) => $"({F(v.x)}, {F(v.y)})";
        static string Tex(Texture t) => t == null ? "null" : t.name + "#" + t.GetInstanceID();

        /// <summary>A model document with one material: the placeholder shader's file and, unless
        /// null, the legacy description (diffuse colour, and diffuse texture when given).</summary>
        static string ModelDocument(string shaderGuid, string textureGuid, Vector2 offset, Vector2 scale, bool import = true)
        {
            var text = $@"%YAML 1.1
--- !u!21 &{MaterialFileId}
Material:
  m_Name: Legacy
  m_Shader: {{fileID: {ShaderFileId}, guid: {shaderGuid}, type: 3}}
";
            if (!import) return text;
            text += @"  m_UnboundMaterialImport:
    importer: legacy
    assetPath: Assets/Models/Legacy.fbx
    floats: {}
    vectors:
      DiffuseColor: {x: 0.25, y: 0.5, z: 0.75, w: 1}
    strings: {}
";
            if (textureGuid == null) return text + "    textures: {}\n";
            return text + $@"    textures:
      DiffuseColor:
        path: Legacy.png
        relativePath: Legacy.png
        texture: {{fileID: {TextureFileId}, guid: {textureGuid}, type: 3}}
        offset: {{x: {F(offset.x)}, y: {F(offset.y)}}}
        scale: {{x: {F(scale.x)}, y: {F(scale.y)}}}
";
        }

        static readonly Color Diffuse = new Color(0.25f, 0.5f, 0.75f, 1f);

        /// <summary>Loads the model document as the engine loads a model; the warnings and errors
        /// logged meanwhile; the materials made (owned by the scope).</summary>
        static Material Load(Scope scope, string yaml, out List<string> logs, out List<Material> made)
        {
            var before = new HashSet<Material>(Resources.FindObjectsOfTypeAll<Material>());
            var captured = new List<string>();
            Application.LogCallback capture = (message, stack, type) =>
            {
                if (type != LogType.Log) captured.Add(type + ": " + message);
            };
            Application.logMessageReceived += capture;
            LoadedFile file;
            try
            {
                file = ObjectGraphBuilder.Build(UnityYaml.Parse(yaml), "Assets/Models/Legacy.fbx",
                    Guid.NewGuid().ToString("N"), ObjectGraphBuilder.Kind.PrefabAsset, null);
            }
            finally
            {
                Application.logMessageReceived -= capture;
            }
            made = Resources.FindObjectsOfTypeAll<Material>().Where(m => !before.Contains(m)).ToList();
            foreach (var m in made) scope.Own(m);
            logs = captured;
            Assert.True(file.Objects.TryGetValue(MaterialFileId, out var obj), "the model's material was not made");
            return (Material)obj;
        }

        /// <summary>Every declared property's value, in declaration order, except
        /// <paramref name="skip"/> (texture: texture, offset, scale).</summary>
        static List<string> Values(Material m, Shader shader, ICollection<string> skip)
        {
            var values = new List<string>();
            for (int i = 0; i < shader.GetPropertyCount(); i++)
            {
                var name = shader.GetPropertyName(i);
                if (skip.Contains(name)) continue;
                switch (shader.GetPropertyType(i))
                {
                    case ShaderPropertyType.Color:
                    case ShaderPropertyType.Vector:
                        values.Add(name + "=" + V(m.GetVector(name)));
                        break;
                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range:
                        values.Add(name + "=" + F(m.GetFloat(name)));
                        break;
                    case ShaderPropertyType.Int:
                        values.Add(name + "=" + m.GetInteger(name).ToString(CultureInfo.InvariantCulture));
                        break;
                    case ShaderPropertyType.Texture:
                        values.Add($"{name}={Tex(m.GetTexture(name))} {V(m.GetTextureOffset(name))} {V(m.GetTextureScale(name))}");
                        break;
                }
            }
            return values;
        }

        /// <summary>Everything a material holds: public state and the values, slots, tags and
        /// script-set names behind it.</summary>
        static List<string> Snapshot(Material m)
        {
            static string Id(int id) => Shader.IdToName(id) ?? id.ToString(CultureInfo.InvariantCulture);
            var s = new List<string>
            {
                "name=" + m.name,
                "shader=" + (m.shader != null ? m.shader.name + "#" + m.shader.GetInstanceID() : "null"),
                "renderQueue=" + m.renderQueue,
                "rawRenderQueue=" + m.rawRenderQueue,
                "keywords=" + string.Join(" ", m.shaderKeywords),
                "gi=" + m.globalIlluminationFlags,
                "doubleSidedGI=" + m.doubleSidedGI,
                "instancing=" + m.enableInstancing,
                "crc=" + m.ComputeCRC(),
                "textureNames=" + string.Join(" ", m.GetTexturePropertyNames()),
            };
            if (m.shader != null) s.AddRange(Values(m, m.shader, Array.Empty<string>()));
            foreach (var name in new[] { "_Mode", "_SrcBlend", "_DstBlend", "_ZWrite", "_Glossiness", "_BumpMap", "_EmissionMap" })
                s.Add("has " + name + "=" + m.HasProperty(name));
            s.AddRange(m.m_Floats.OrderBy(p => Id(p.Key), StringComparer.Ordinal).Select(p => "float " + Id(p.Key) + "=" + F(p.Value)));
            s.AddRange(m.m_Ints.OrderBy(p => Id(p.Key), StringComparer.Ordinal).Select(p => "int " + Id(p.Key) + "=" + p.Value));
            s.AddRange(m.m_Vectors.OrderBy(p => Id(p.Key), StringComparer.Ordinal).Select(p => "vector " + Id(p.Key) + "=" + V(p.Value)));
            s.AddRange(m.m_Textures.OrderBy(p => Id(p.Key), StringComparer.Ordinal)
                .Select(p => $"slot {Id(p.Key)}={Tex(p.Value.Texture)} {V(p.Value.Offset)} {V(p.Value.Scale)}"));
            s.AddRange(m.m_Tags.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => "tag " + p.Key + "=" + p.Value));
            s.AddRange(m.m_ScriptSet.Select(Id).OrderBy(n => n, StringComparer.Ordinal).Select(n => "scriptSet " + n));
            return s;
        }

        // ------------------------------------------------------------------ tests

        /// <summary>Observed import behaviour (6000.0.71f1 URP): the pipeline's default shader with
        /// that shader's own defaults. Expected red on the previous Core (Standard placeholder kept).</summary>
        [Fact]
        public void UnderAPipelineTheLegacyMaterialIsOneMadeWithItsDefaultShader()
        {
            var scope = new Scope();
            try
            {
                var placeholder = scope.Own(ShaderUtil.CreateShaderAsset(PlaceholderSource($"Tests/ModelMaterialImport/Placeholder{++s_Made}")));
                var shader = scope.Own(PipelineDefault());
                var defaultTexture = scope.Own(new Texture2D(2, 2) { name = "PipelineDefaultDetail" });
                shader.m_DefaultTextures["_DetailTex"] = defaultTexture;
                var pipeline = scope.Own(ScriptableObject.CreateInstance<Pipeline>());
                pipeline.Default = shader;
                scope.UsePipeline(pipeline);

                var material = Load(scope, ModelDocument(scope.Register(placeholder, ShaderFileId), null, default, default), out var logs, out var made);

                Assert.Empty(logs);
                // The registered sub-asset itself, nothing else left behind.
                Assert.Equal(new[] { material }, made);
                Assert.Equal("Legacy", material.name);
                Assert.True(material.IsPersistentAsset);
                Assert.Same(shader, material.shader);

                // A material made with that shader, for comparison (made after the load).
                var fresh = scope.Own(new Material(shader));
                var placeholderFresh = scope.Own(new Material(placeholder));
                // The fixture tells a placeholder value from the shader's own.
                Assert.NotEqual(placeholderFresh.GetFloat("_Cutoff"), fresh.GetFloat("_Cutoff"));
                Assert.NotEqual(placeholderFresh.GetFloat("_Parallax"), fresh.GetFloat("_Parallax"));

                // What the legacy rule writes (opaque, untextured): _Color and the main colour; the
                // rest of the shader's properties keep its own defaults, every kind.
                var written = new[] { "_Color", "_BaseColor" };
                Assert.Equal(Values(fresh, shader, written), Values(material, shader, written));
                Assert.Same(defaultTexture, material.GetTexture("_DetailTex"));
                // Its keywords (the toggle's default), texture slots and no value of the placeholder's.
                Assert.Equal(fresh.shaderKeywords, material.shaderKeywords);
                Assert.Equal(fresh.GetTexturePropertyNames(), material.GetTexturePropertyNames());
                Assert.DoesNotContain("_PlaceholderOnlyTex", material.GetTexturePropertyNames());
                Assert.False(material.m_Floats.ContainsKey(Shader.PropertyToID("_PlaceholderOnly")));
                Assert.Equal(fresh.renderQueue, material.renderQueue);
                Assert.Equal(-1, material.rawRenderQueue);
            }
            finally
            {
                scope.Restore();
            }
        }

        /// <summary>The diffuse colour and texture with its offset and scale: by name (_Color,
        /// _MainTex: observed import behaviour) and as the main colour and texture (documented:
        /// the [MainColor]/[MainTexture] properties). Expected red on the previous Core (_BaseColor,
        /// _BaseMap are not the placeholder's).</summary>
        [Fact]
        public void UnderAPipelineTheDiffuseColourAndTextureAreTheMainColourAndTexture()
        {
            var scope = new Scope();
            try
            {
                var placeholder = scope.Own(ShaderUtil.CreateShaderAsset(PlaceholderSource($"Tests/ModelMaterialImport/Placeholder{++s_Made}")));
                var shader = scope.Own(PipelineDefault());
                var texture = scope.Own(new Texture2D(2, 2) { name = "LegacyDiffuse" });
                var pipeline = scope.Own(ScriptableObject.CreateInstance<Pipeline>());
                pipeline.Default = shader;
                scope.UsePipeline(pipeline);
                var offset = new Vector2(0.125f, 0.25f);
                var scale = new Vector2(2f, 4f);

                var material = Load(scope, ModelDocument(scope.Register(placeholder, ShaderFileId), scope.Register(texture, TextureFileId), offset, scale),
                    out var logs, out _);

                Assert.Empty(logs);
                Assert.Same(shader, material.shader);
                // Documented main properties.
                Assert.Equal(Diffuse, material.color);
                Assert.Equal(Diffuse, material.GetColor("_BaseColor"));
                Assert.Same(texture, material.mainTexture);
                Assert.Same(texture, material.GetTexture("_BaseMap"));
                Assert.Equal(offset, material.mainTextureOffset);
                Assert.Equal(scale, material.mainTextureScale);
                Assert.Equal(offset, material.GetTextureOffset("_BaseMap"));
                Assert.Equal(scale, material.GetTextureScale("_BaseMap"));
                // The legacy rule's names (observed).
                Assert.Equal(Diffuse, material.GetColor("_Color"));
                Assert.Same(texture, material.GetTexture("_MainTex"));
                Assert.Equal(offset, material.GetTextureOffset("_MainTex"));
                Assert.Equal(scale, material.GetTextureScale("_MainTex"));
            }
            finally
            {
                scope.Restore();
            }
        }

        /// <summary>Preservation control: without a render pipeline the measured Standard conversion
        /// is unchanged (the placeholder, which the document names, is Standard; its other values
        /// stay). Expected green on the previous Core too.</summary>
        [Fact]
        public void WithoutAPipelineTheLegacyMaterialStaysStandard()
        {
            var scope = new Scope();
            try
            {
                scope.UsePipeline(null);
                // Shader.Find("Standard") is the placeholder, as in the engine's documents. A name
                // the test run already has (another "Standard") stops the test here.
                var standard = scope.Own(ShaderUtil.CreateShaderAsset(PlaceholderSource("Standard")));
                Assert.Same(standard, Shader.Find("Standard"));
                var texture = scope.Own(new Texture2D(2, 2) { name = "LegacyDiffuse" });
                var offset = new Vector2(0.125f, 0.25f);
                var scale = new Vector2(2f, 4f);

                var material = Load(scope, ModelDocument(scope.Register(standard, ShaderFileId), scope.Register(texture, TextureFileId), offset, scale),
                    out var logs, out var made);

                Assert.Empty(logs);
                Assert.Equal(new[] { material }, made);
                Assert.Same(standard, material.shader);
                Assert.Equal("Legacy", material.name);
                Assert.Equal(Diffuse, material.GetColor("_Color"));
                Assert.Equal(Diffuse, material.color);
                Assert.Same(texture, material.GetTexture("_MainTex"));
                Assert.Same(texture, material.mainTexture);
                Assert.Equal(offset, material.GetTextureOffset("_MainTex"));
                Assert.Equal(scale, material.GetTextureScale("_MainTex"));
                Assert.Equal(0f, material.GetFloat("_Glossiness"));
                Assert.Equal(-1, material.rawRenderQueue);
                // The rest is the placeholder's: its defaults and its texture slots.
                var fresh = scope.Own(new Material(standard));
                var written = new[] { "_Color", "_MainTex" };
                Assert.Equal(Values(fresh, standard, written), Values(material, standard, written));
                Assert.Contains("_PlaceholderOnlyTex", material.GetTexturePropertyNames());
            }
            finally
            {
                scope.Restore();
            }
        }

        /// <summary>Unsupported content fails loudly: a pipeline without a default shader logs an
        /// error naming the pipeline asset and the material, and the material is exactly the
        /// document's placeholder (compared with the same document without a description).
        /// Expected red on the previous Core (it converted to Standard and logged nothing).</summary>
        [Fact]
        public void APipelineWithoutADefaultShaderIsAnErrorAndLeavesTheMaterialUnchanged()
        {
            var scope = new Scope();
            try
            {
                var placeholder = scope.Own(ShaderUtil.CreateShaderAsset(PlaceholderSource($"Tests/ModelMaterialImport/Placeholder{++s_Made}")));
                var texture = scope.Own(new Texture2D(2, 2) { name = "LegacyDiffuse" });
                var pipeline = scope.Own(ScriptableObject.CreateInstance<Pipeline>());
                pipeline.name = "Tests pipeline without a default shader";
                scope.UsePipeline(pipeline);
                var shaderGuid = scope.Register(placeholder, ShaderFileId);
                var textureGuid = scope.Register(texture, TextureFileId);
                var offset = new Vector2(0.125f, 0.25f);
                var scale = new Vector2(2f, 4f);

                var material = Load(scope, ModelDocument(shaderGuid, textureGuid, offset, scale), out var logs, out var made);
                var unimported = Load(scope, ModelDocument(shaderGuid, textureGuid, offset, scale, import: false), out var plainLogs, out _);

                var error = Assert.Single(logs);
                Assert.StartsWith("Error: ", error);
                Assert.Contains(pipeline.name, error);
                Assert.Contains("'Legacy'", error);
                Assert.Contains("Assets/Models/Legacy.fbx", error);
                Assert.Empty(plainLogs);
                Assert.Equal(new[] { material }, made);
                Assert.Same(placeholder, material.shader);
                Assert.Equal(Snapshot(unimported), Snapshot(material));
            }
            finally
            {
                scope.Restore();
            }
        }
    }
}
