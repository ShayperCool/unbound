using Unbound.Runtime;
using UnityEngine;
using UnityEngine.Rendering;
using Xunit;

namespace Unbound.Tests
{
    /// <summary>The material a SpriteRenderer starts with (tests/projects/toolapis spriteRenderer.*):
    /// the render pipeline's default 2D material when a script adds it, and its own materials
    /// when it is loaded or copied.</summary>
    [Collection("RenderSettings state")]
    public class SpriteRendererDefaultMaterialTests : System.IDisposable
    {
        readonly RenderPipelineAsset m_Previous = GraphicsSettings.defaultRenderPipeline;

        public void Dispose() => GraphicsSettings.defaultRenderPipeline = m_Previous;

        sealed class Pipeline : RenderPipelineAsset
        {
            internal Material Material;
            protected override RenderPipeline CreatePipeline() => null;
            public override Material default2DMaterial => Material;
        }

        static Material Named(string name) => new Material(Shader.Create("Sprites/Default", 3000)) { name = name };

        [Fact]
        public void AnAddedRendererTakesThePipelinesDefault2DMaterial()
        {
            var before = new GameObject("Before").AddComponent<SpriteRenderer>();
            var beforeMaterials = before.sharedMaterials;
            var pipeline = ScriptableObject.CreateInstance<Pipeline>();
            pipeline.Material = Named("PipelineDefault2D");
            GraphicsSettings.defaultRenderPipeline = pipeline;
            var added = new GameObject("Added").AddComponent<SpriteRenderer>();
            Assert.Same(pipeline.Material, added.sharedMaterial);
            // A copy of a renderer made before keeps that renderer's materials.
            var copy = Object.Instantiate(before.gameObject).GetComponent<SpriteRenderer>();
            Assert.Equal(beforeMaterials, copy.sharedMaterials);
            Assert.DoesNotContain(pipeline.Material, copy.sharedMaterials);
            Object.DestroyImmediate(before.gameObject);
            Object.DestroyImmediate(added.gameObject);
            Object.DestroyImmediate(copy.gameObject);
        }

        [Fact]
        public void APipelineWithoutA2DMaterialLeavesTheBuiltInDefault()
        {
            var plain = new GameObject("Plain").AddComponent<SpriteRenderer>().sharedMaterials;
            GraphicsSettings.defaultRenderPipeline = ScriptableObject.CreateInstance<Pipeline>();
            var added = new GameObject("Added").AddComponent<SpriteRenderer>();
            Assert.Equal(plain, added.sharedMaterials);
            Object.DestroyImmediate(added.gameObject);
        }

        [Fact]
        public void ALoadedRendererDoesNotAskThePipeline()
        {
            var pipeline = ScriptableObject.CreateInstance<Pipeline>();
            pipeline.Material = Named("PipelineDefault2D");
            GraphicsSettings.defaultRenderPipeline = pipeline;
            const string yaml = @"%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!1 &1
GameObject:
  m_Name: Loaded
  m_Component:
  - component: {fileID: 2}
  - component: {fileID: 3}
  m_IsActive: 1
--- !u!4 &2
Transform:
  m_GameObject: {fileID: 1}
  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}
  m_LocalPosition: {x: 0, y: 0, z: 0}
  m_LocalScale: {x: 1, y: 1, z: 1}
  m_Children: []
  m_Father: {fileID: 0}
--- !u!212 &3
SpriteRenderer:
  m_GameObject: {fileID: 1}
  m_Enabled: 1
  m_Materials: []
";
            var file = ObjectGraphBuilder.Build(UnityYaml.Parse(yaml), "loaded.prefab", null, ObjectGraphBuilder.Kind.Asset, null);
            var renderer = Assert.IsType<SpriteRenderer>(file.Objects[3]);
            Assert.DoesNotContain(pipeline.Material, renderer.sharedMaterials);
        }
    }
}
