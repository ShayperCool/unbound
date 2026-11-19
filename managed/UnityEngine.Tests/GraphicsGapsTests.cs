using System;
using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Xunit;

namespace Unbound.Tests
{
    /// <summary>Graphics members pipelines, packages and editor tools reach at run time, as the
    /// Unity 6000.0.71f1 Editor answers them (tests/projects/graphicsgaps).</summary>
    public class GraphicsGapsTests
    {
        /// <summary>A SubShader's RenderPipeline tag lists pipeline names: no tag or no name runs
        /// everywhere; a name must equal one of the pipeline tag's comma-separated names as
        /// written, the pipelines' own tags in any case reading as their spelling (select.*).</summary>
        [Theory]
        [InlineData(null, "", true)]
        [InlineData(null, "UniversalPipeline", true)]
        [InlineData("", "", true)]
        [InlineData("", "Foo", true)]
        [InlineData("UniversalPipeline", "", false)]
        [InlineData("UniversalPipeline", "UniversalPipeline", true)]
        [InlineData("UniversalPipeline", "UniversalPipeline,Foo", true)]
        [InlineData("UniversalPipeline", "Foo, UniversalPipeline", false)]
        [InlineData(" UniversalPipeline", "Foo, UniversalPipeline", true)]
        [InlineData("UniversalPipeline", "universalpipeline", false)]
        [InlineData("universalpipeline", "UniversalPipeline", true)]
        [InlineData("universalpipeline", "universalpipeline", false)]
        [InlineData("UniversalPipeline,HDRenderPipeline", "HDRenderPipeline", true)]
        [InlineData("HDRenderPipeline", "HDRenderPipeline,UniversalPipeline", true)]
        [InlineData("Foo", "Foo", true)]
        [InlineData("foo", "Foo", false)]
        [InlineData(" ", "", false)]
        public void PipelineTagsMatchByName(string tag, string pipeline, bool runs) =>
            Assert.Equal(runs, ShaderDeclarations.RunsUnder(tag, pipeline));

        /// <summary>A tile covers 64 KiB in the standard sparse block shape; an unloaded tile reads
        /// zero, a loaded one its pixels, UnloadTile zeroes it again (sparse.*).</summary>
        [Fact]
        public void SparseTextureTilesAreLoadedAndUnloaded()
        {
            Assert.True(SystemInfo.supportsSparseTextures);
            var texture = new SparseTexture(256, 256, TextureFormat.RGBA32, 1);
            Assert.True(texture.isCreated);
            Assert.Equal((128, 128), (texture.tileWidth, texture.tileHeight));
            Assert.Equal(GraphicsFormat.R8G8B8A8_UNorm, texture.graphicsFormat);
            Assert.Equal(256, texture.width);
            texture.UpdateTile(1, 0, 0, Enumerable.Repeat(new Color32(255, 0, 0, 255), 128 * 128).ToArray());
            Assert.Equal(new Color32(255, 0, 0, 255), (Color32)texture.Tiles.GetPixel(200, 10));
            Assert.Equal(new Color32(0, 0, 0, 0), (Color32)texture.Tiles.GetPixel(10, 10));
            texture.UnloadTile(1, 0, 0);
            Assert.Equal(new Color32(0, 0, 0, 0), (Color32)texture.Tiles.GetPixel(200, 10));
            // A material given the texture once keeps its handle: the tiles changed afterwards
            // are sent as the next frame renders, without another bind.
            var material = new Material(Shader.Find("Unlit/Texture") ?? ShaderUtil.CreateShaderAsset("Shader \"Tests/SparseUser\" { SubShader { Pass { } } }"));
            material.mainTexture = texture;
            material.Sync();
            uint sent = texture.Tiles.updateCount;
            texture.UpdateTile(0, 1, 0, Enumerable.Repeat(new Color32(0, 255, 0, 255), 128 * 128).ToArray());
            SparseTexture.SendChanged();
            Assert.True(texture.Tiles.updateCount > sent);
            uint again = texture.Tiles.updateCount;
            SparseTexture.SendChanged();
            Assert.Equal(again, texture.Tiles.updateCount);
            // A tile past the texture is no tile: nothing changes.
            texture.UpdateTile(5, 5, 0, new Color32[128 * 128]);
            Assert.Throws<ArgumentException>(() => texture.UpdateTile(0, 0, 0, new Color32[3]));
            UnityEngine.Object.DestroyImmediate(texture);

            var r8 = new SparseTexture(512, 512, GraphicsFormat.R8_UNorm, 1);
            Assert.Equal((256, 256), (r8.tileWidth, r8.tileHeight));
            UnityEngine.Object.DestroyImmediate(r8);
            Assert.Throws<ArgumentException>(() => new SparseTexture(256, 256, TextureFormat.DXT1Crunched, 1));
        }

        /// <summary>A texture under 64 KiB is refused with the Editor's error, and is no texture.</summary>
        [Fact]
        public void ASmallSparseTextureIsRefused()
        {
            var logged = new List<string>();
            Application.LogCallback listen = (message, _, type) => { if (type == LogType.Error) logged.Add(message); };
            Application.logMessageReceived += listen;
            try
            {
                var texture = new SparseTexture(64, 64, TextureFormat.RGBA32, 1);
                Assert.False(texture.isCreated);
            }
            finally { Application.logMessageReceived -= listen; }
            Assert.Equal(new[] { "SparseTexture creation failed. The minimum size in bytes of a SparseTexture is 64KB." }, logged);
        }

        /// <summary>Anamorphism is kept between -1 and 1 (NaN stays) and copied (anamorphism.*).</summary>
        [Fact]
        public void AnamorphismIsClampedAndCopied()
        {
            var go = new GameObject("anamorphic");
            var camera = go.AddComponent<Camera>();
            Assert.Equal(0f, camera.anamorphism);
            camera.anamorphism = 2f;
            Assert.Equal(1f, camera.anamorphism);
            camera.anamorphism = -2f;
            Assert.Equal(-1f, camera.anamorphism);
            camera.anamorphism = float.NaN;
            Assert.True(float.IsNaN(camera.anamorphism));
            camera.anamorphism = 0.5f;
            var other = new GameObject("copy").AddComponent<Camera>();
            other.CopyFrom(camera);
            Assert.Equal(0.5f, other.anamorphism);
            UnityEngine.Object.DestroyImmediate(other.gameObject);
            UnityEngine.Object.DestroyImmediate(go);
        }

        [Fact]
        public void GiWorkflowModeIsOnDemand()
        {
#pragma warning disable 618
            Assert.Equal(Lightmapping.GIWorkflowMode.OnDemand, Lightmapping.giWorkflowMode);
            Lightmapping.giWorkflowMode = Lightmapping.GIWorkflowMode.Iterative;
            Assert.Equal(Lightmapping.GIWorkflowMode.OnDemand, Lightmapping.giWorkflowMode);
#pragma warning restore 618
        }

        /// <summary>Rendering layer names: any name is added up to 32, Default is neither renamed
        /// nor removed, an index out of range is refused, each with the Editor's error (layers.*).</summary>
        [Fact]
        public void RenderingLayerNamesAreEditedAsInTheEditor()
        {
            var saved = RenderingLayerMask.s_Names;
            var logged = new List<string>();
            Application.LogCallback listen = (message, _, type) => { if (type == LogType.Error) logged.Add(message); };
            Application.logMessageReceived += listen;
            try
            {
                RenderingLayerMask.s_Names = new[] { "Default", "Layer 1" };
                Assert.True(RenderPipelineEditorUtility.TryAddRenderingLayerName("Layer 1"));
                Assert.True(RenderPipelineEditorUtility.TryAddRenderingLayerName(""));
                Assert.Equal(new[] { "Default", "Layer 1", "Layer 1", "" }, RenderingLayerMask.s_Names);
                Assert.False(RenderPipelineEditorUtility.TrySetRenderingLayerName(0, "X"));
                Assert.True(RenderPipelineEditorUtility.TrySetRenderingLayerName(3, null));
                Assert.Equal("", RenderingLayerMask.RenderingLayerToName(3));
                Assert.False(RenderPipelineEditorUtility.TrySetRenderingLayerName(4, "Past"));
                Assert.False(RenderPipelineEditorUtility.TrySetRenderingLayerName(-1, "Negative"));
                while (RenderingLayerMask.GetRenderingLayerCount() < 32) RenderPipelineEditorUtility.TryAddRenderingLayerName("Fill");
                Assert.False(RenderPipelineEditorUtility.TryAddRenderingLayerName("Past"));
                while (RenderPipelineEditorUtility.TryRemoveLastRenderingLayerName()) { }
                Assert.Equal(new[] { "Default" }, RenderingLayerMask.s_Names);
            }
            finally
            {
                Application.logMessageReceived -= listen;
                RenderingLayerMask.s_Names = saved;
            }
            Assert.Equal(new[]
            {
                "Index 0 is reserved for Default Layer and it can't be edited.",
                "Out of range index for Rendering Layers: 4. Use index between 0 and 4",
                "Out of range index for Rendering Layers: -1. Use index between 0 and 4",
                "TryAddRenderingLayerName can't add more Rendering Layers because the maximum amount of supported Rendering Layers is 32.",
                "Rendering layers always need at least one default layer. It's not possible to remove the default layer.",
            }, logged);
        }

        sealed class LimitedLayers : RenderingLayersLimitSettings
        {
            internal int Max;
            protected override int maxRenderingLayersForPipeline => Max;
        }

        [Theory]
        [InlineData(16, 16)]
        [InlineData(32, 32)]
        [InlineData(2, 2)]
        [InlineData(1, 32)]
        [InlineData(33, 32)]
        [InlineData(-4, 32)]
        public void ALayerLimitOutsideTwoToThirtyTwoIsThirtyTwo(int limit, int supported) =>
            Assert.Equal(supported, new LimitedLayers { Max = limit }.maxSupportedRenderingLayers);

        /// <summary>A pipeline asset type's pipeline type: its generic argument; a type that is no
        /// pipeline asset has none.</summary>
        [Fact]
        public void PipelineTypesComeFromTheAssetType()
        {
            Assert.Null(RenderPipelineEditorUtility.GetPipelineTypeFromPipelineAssetType(typeof(Material)));
            Assert.Equal(typeof(ProbePipeline), RenderPipelineEditorUtility.GetPipelineTypeFromPipelineAssetType(typeof(ProbePipelineAsset)));
        }

        sealed class ProbePipeline : RenderPipeline
        {
            protected override void Render(ScriptableRenderContext context, Camera[] cameras) { }
        }

        sealed class ProbePipelineAsset : RenderPipelineAsset<ProbePipeline>
        {
            protected override RenderPipeline CreatePipeline() => new ProbePipeline();
        }

        /// <summary>The filters answer true for renderers; a hidden renderer's bit is set, and the
        /// span lengths are checked as Unity's C# checks them (cameraUtils.*).</summary>
        [Fact]
        public void SceneViewFiltersFollowHiddenObjects()
        {
            var a = new GameObject("a").AddComponent<MeshRenderer>();
            var b = new GameObject("b").AddComponent<MeshRenderer>();
            var ids = new[] { a.GetInstanceID(), b.GetInstanceID() };
            var results = new bool[2];
            EditorCameraUtils.GetRenderersFilteringResults(ids, results);
            Assert.Equal(new[] { true, true }, results);
            var bits = new ulong[1];
            EditorCameraUtils.GetRenderersHiddenResultBits(ids, bits);
            Assert.Equal(0ul, bits[0]);
            SceneVisibilityManager.instance.Hide(b.gameObject, false);
            try
            {
                EditorCameraUtils.GetRenderersHiddenResultBits(ids, bits);
                Assert.Equal(2ul, bits[0]);
                EditorCameraUtils.GetRenderersFilteringResults(ids, results);
                Assert.Equal(new[] { true, true }, results);
            }
            finally { SceneVisibilityManager.instance.Show(b.gameObject, false); }
            EditorCameraUtils.GetRenderersHiddenResultBits(ids, bits);
            Assert.Equal(0ul, bits[0]);
            Assert.Throws<ArgumentException>(() => EditorCameraUtils.GetRenderersFilteringResults(ids, new bool[1]));
            Assert.Throws<ArgumentException>(() => EditorCameraUtils.GetRenderersHiddenResultBits(ids, new ulong[2]));
            UnityEngine.Object.DestroyImmediate(a.gameObject);
            UnityEngine.Object.DestroyImmediate(b.gameObject);
        }

        /// <summary>Color ids pack the channels' bytes, red lowest (Unity's C#).</summary>
        [Fact]
        public void ObjectIdsDecodeFromColours()
        {
            Assert.Equal(0, ObjectIdResult.DecodeIdFromColor(new Color(0, 0, 0, 0)));
            Assert.Equal(1 + (2 << 8) + (3 << 16), ObjectIdResult.DecodeIdFromColor(new Color32(1, 2, 3, 0)));
            var request = new ObjectIdRequest(null);
            Assert.Equal((0, CubemapFace.Unknown, 0), (request.mipLevel, request.face, request.slice));
            Assert.Null(request.result);
            var camera = new GameObject("camera").AddComponent<Camera>();
            Assert.Throws<ArgumentException>(() => camera.SubmitRenderRequest<ObjectIdRequest>(null));
            Assert.True(RenderPipeline.SupportsRenderRequest(camera, request));
            // A pipeline asset set, its pipeline not made yet (nothing rendered): the pipeline is
            // made, takes no ObjectIdRequest, and the Editor's own pass answers.
            var previous = GraphicsSettings.defaultRenderPipeline;
            var previousTag = Shader.globalRenderPipeline;
            GraphicsSettings.defaultRenderPipeline = ScriptableObject.CreateInstance<ProbePipelineAsset>();
            try
            {
                var target = new RenderTexture(8, 8, 24);
                var ids = new ObjectIdRequest(target);
                camera.SubmitRenderRequest(ids);
                Assert.NotNull(ids.result);
                Assert.Null(ids.result.idToObjectMapping[0]);
            }
            finally
            {
                var asset = GraphicsSettings.defaultRenderPipeline;
                GraphicsSettings.defaultRenderPipeline = previous;
                RenderPipelineManager.CleanupRenderPipeline();
                Shader.globalRenderPipeline = previousTag;
                UnityEngine.Object.DestroyImmediate(asset);
            }
            UnityEngine.Object.DestroyImmediate(camera.gameObject);
        }

        const string PropsSource = @"Shader ""Tests/GapsProps"" { Properties {
            _Color (""Color"", Color) = (1, 0.5, 0.25, 1)
            [HDR] _Emis (""Emission"", Color) = (0, 0, 0, 0)
            _Vec (""Vector"", Vector) = (1, 2, 3, 4)
            _Rng (""Range"", Range(0, 2)) = 1
            _Integer (""Integer"", Integer) = 7
            [NoScaleOffset] _Tex (""Tex"", 2D) = ""white"" {}
        } SubShader { Pass { } } }";

        /// <summary>A property's value is the first material's, mixed where another differs; the
        /// range limits are a Range's, else the default vector's y and z; writing sets every
        /// material (materialProperty.*).</summary>
        [Fact]
        public void MaterialPropertiesReadAndWriteTheirMaterials()
        {
            var shader = ShaderUtil.CreateShaderAsset(PropsSource);
            var a = new Material(shader) { name = "a" };
            var b = new Material(shader) { name = "b" };
            b.SetColor("_Color", Color.green);
            var props = MaterialEditor.GetMaterialProperties(new UnityEngine.Object[] { a });
            Assert.Equal(new[] { "_Color", "_Emis", "_Vec", "_Rng", "_Integer", "_Tex" }, props.Select(p => p.name));
            var color = props[0];
            Assert.Equal(MaterialProperty.PropType.Color, color.type);
            Assert.Equal(new Color(1, 0.5f, 0.25f, 1), color.colorValue);
            Assert.Equal(Vector4.zero, color.vectorValue);
            Assert.Equal(new Vector2(0.5f, 0.25f), color.rangeLimits);
            Assert.Equal(TextureDimension.Any, color.textureDimension);
            Assert.Equal(MaterialProperty.PropFlags.HDR, props[1].flags);
            Assert.Equal(new Vector2(2, 3), props[2].rangeLimits);
            Assert.Equal(new Vector2(0, 2), props[3].rangeLimits);
            Assert.Equal(7, props[4].intValue);
            Assert.Equal(0f, props[4].floatValue);
            Assert.Equal(TextureDimension.Tex2D, props[5].textureDimension);
            Assert.Equal(new Vector4(1, 1, 0, 0), props[5].textureScaleAndOffset);
            Assert.Equal(Color.black, props[3].colorValue);

            var both = MaterialEditor.GetMaterialProperty(new UnityEngine.Object[] { a, b }, "_Color");
            Assert.True(both.hasMixedValue);
            Assert.False(MaterialEditor.GetMaterialProperty(new UnityEngine.Object[] { a, b }, "_Rng").hasMixedValue);
            both.colorValue = Color.blue;
            Assert.Equal(Color.blue, a.GetColor("_Color"));
            Assert.Equal(Color.blue, b.GetColor("_Color"));
            Assert.False(both.hasMixedValue);
            var range = MaterialEditor.GetMaterialProperty(new UnityEngine.Object[] { a }, "_Rng");
            range.floatValue = 1.5f;
            Assert.Equal(1.5f, a.GetFloat("_Rng"));
            Assert.Null(MaterialEditor.GetMaterialProperty(new UnityEngine.Object[] { a }, "_None").name);
            Assert.Empty(MaterialEditor.GetMaterialProperties(Array.Empty<UnityEngine.Object>()));
        }

        /// <summary>A new pipeline tag selects again and reports a selected undefined Queue value
        /// once more, naming no shader (queue.errors.unknown); a shader never asked for is not
        /// reported.</summary>
        [Fact]
        public void APipelineChangeReportsUndefinedQueuesAgain()
        {
            var shader = ShaderUtil.CreateShaderAsset("Shader \"Tests/GapsQueue\" { SubShader { Pass { } } }");
            var logged = new List<string>();
            Application.LogCallback listen = (message, _, type) => { if (type == LogType.Error) logged.Add(message); };
            ShaderDeclarations.s_CallForTests = (method, request) => request.Contains("Tests/GapsQueue")
                ? "{\"found\":true,\"builtin\":false,\"transient\":false,\"reason\":\"\",\"subshaders_known\":true,\"keywords_known\":true,\"keywords\":[],"
                  + "\"subshaders\":[{\"tags\":[[\"QUEUE\",\"2450\"]],\"passes\":[{\"name\":\"P\",\"tags\":[]}],\"lod\":0,\"restricted\":false}]}"
                : "{\"found\":false}";
            ShaderDeclarations.ResetForTests();
            var previous = Shader.globalRenderPipeline;
            Application.logMessageReceived += listen;
            try
            {
                Assert.Equal(2000, shader.renderQueue);
                Shader.globalRenderPipeline = previous + "Other";
            }
            finally
            {
                Application.logMessageReceived -= listen;
                Shader.globalRenderPipeline = previous;
                ShaderDeclarations.s_CallForTests = null;
                ShaderDeclarations.ResetForTests();
                UnityEngine.Object.DestroyImmediate(shader);
            }
            Assert.Equal(new[] { "Shader Tests/GapsQueue uses undefined Queue: '2450'", "Shader Unknown uses undefined Queue: '2450'" }, logged);
        }

        /// <summary>A SubShader without passes is selected like any other (the renderer's choice
        /// too): its pass count is 0.</summary>
        [Fact]
        public void APasslessSubShaderIsSelected()
        {
            var shader = ShaderUtil.CreateShaderAsset("Shader \"Tests/GapsPassless\" { SubShader { Pass { } } }");
            ShaderDeclarations.s_CallForTests = (method, request) => request.Contains("Tests/GapsPassless")
                ? "{\"found\":true,\"builtin\":false,\"transient\":false,\"reason\":\"\",\"subshaders_known\":true,\"keywords_known\":true,\"keywords\":[],"
                  + "\"subshaders\":[{\"tags\":[[\"QUEUE\",\"Transparent\"]],\"passes\":[],\"lod\":0,\"restricted\":false},"
                  + "{\"tags\":[],\"passes\":[{\"name\":\"P\",\"tags\":[]}],\"lod\":0,\"restricted\":false}]}"
                : "{\"found\":false}";
            ShaderDeclarations.ResetForTests();
            try
            {
                Assert.Equal(0, shader.passCount);
                Assert.Equal(3000, shader.renderQueue);
            }
            finally
            {
                ShaderDeclarations.s_CallForTests = null;
                ShaderDeclarations.ResetForTests();
                UnityEngine.Object.DestroyImmediate(shader);
            }
        }

        [Fact]
        public void MeshCompressionIsKeptWithTheMesh()
        {
            var mesh = new Mesh();
            Assert.Equal(ModelImporterMeshCompression.Off, MeshUtility.GetMeshCompression(mesh));
            MeshUtility.SetMeshCompression(mesh, ModelImporterMeshCompression.High);
            Assert.Equal(ModelImporterMeshCompression.High, MeshUtility.GetMeshCompression(mesh));
            Assert.Throws<ArgumentNullException>(() => MeshUtility.GetMeshCompression(null));
        }
    }
}
