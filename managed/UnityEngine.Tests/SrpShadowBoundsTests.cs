using System.Collections.Generic;
using System.Text.Json;
using Unbound.Runtime;
using Unity.Collections;
using UnityEditor;
using UnityEngine.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    [Collection("Frames")]
    public class SrpShadowBoundsTests
    {
        [Theory]
        [InlineData(true, true, false, 1)]
        [InlineData(false, true, false, 0)]
        [InlineData(true, true, true, 0)]
        [InlineData(false, false, false, 1)]
        public void ListsRetainUntrustedPosesButStillApplySplitExclusions(bool skinned, bool outside, bool excluded, int draws)
        {
            string version = ProjectInfo.UnityVersion;
            var previousShaderCall = ShaderPassInfo.s_CallForTests;
            var sources = FrameBuilder.Sources.ToArray();
            var camera = new GameObject("split bounds camera").AddComponent<Camera>();
            var light = new GameObject("split bounds light").AddComponent<Light>();
            var body = new GameObject("split bounds caster") { layer = 30 };
            var rig = new GameObject("split bounds rig");
            var second = new GameObject("split bounds second");
            var mesh = new Mesh();
            var baked = new Mesh();
            Material material = null;
            Shader shader = null;
            var data = new CullingData { Camera = camera };
            try
            {
                ProjectInfo.UnityVersion = "6000.0.71f1";
                camera.transform.position = new Vector3(0, 0, -4);
                camera.cullingMask = light.cullingMask = 1 << 30;
                light.type = LightType.Directional;
                body.transform.position = outside ? new Vector3(100, 0, 0) : Vector3.zero;
                second.transform.position = Vector3.right;
                mesh.vertices = new[] { Vector3.zero, Vector3.up, Vector3.right };
                mesh.triangles = new[] { 0, 1, 2 };
                mesh.bindposes = new[] { Matrix4x4.identity, Matrix4x4.identity };
                mesh.boneWeights = new[] { new BoneWeight { weight0 = 1 }, new BoneWeight { weight0 = 1 },
                    new BoneWeight { weight0 = 1, boneIndex0 = 1 } };
                mesh.RecalculateBounds();
                Renderer renderer;
                if (skinned)
                {
                    var skin = body.AddComponent<SkinnedMeshRenderer>();
                    skin.ReadSerialized(YNode.MakeMap(), null);
                    skin.bones = new[] { rig.transform, second.transform };
                    skin.rootBone = rig.transform;
                    skin.sharedMesh = mesh;
                    skin.updateWhenOffscreen = true;
                    skin.PrepareCullingBounds();
                    Assert.False(skin.HasMeasuredCullingBounds);
                    skin.BakeMesh(baked);
                    foreach (var vertex in baked.vertices) Assert.InRange(body.transform.TransformPoint(vertex).x, -1f, 10f);
                    renderer = skin;
                }
                else
                {
                    body.AddComponent<MeshFilter>().sharedMesh = mesh;
                    renderer = body.AddComponent<MeshRenderer>();
                }
                ShaderPassInfo.ResetForTests();
                ShaderPassInfo.s_CallForTests = name => JsonSerializer.Serialize(new
                {
                    shader = name, found = true, tags = new Dictionary<string, string>(),
                    passes = new[] { new { name = "ShadowCaster", tags = new Dictionary<string, string> { ["LightMode"] = "ShadowCaster" } } },
                });
                shader = ShaderUtil.CreateShaderAsset("Shader \"Tests/SplitBounds\" { SubShader { Pass { Tags { \"LightMode\"=\"ShadowCaster\" } } } }");
                material = new Material(shader) { renderQueue = 2000 };
                renderer.sharedMaterial = material;
                data.Renderers.Add(renderer);
                data.Lights = new NativeArray<VisibleLight>(1, Allocator.Temp);
                data.Lights[0] = new VisibleLight(light, default, false, false, false);
                var split = default(ShadowSplitData);
                split.cullingPlaneCount = 1;
                split.SetCullingPlane(0, new Plane(Vector3.left, 10));
                Assert.Equal(!outside, ShadowCasterCulling.InsidePlanes(renderer.CullingBounds, split));
                data.ShadowSplits.Add(split);
                data.ShadowLights.Add(new LightShadowCasterCullingInfo { splitRange = new RangeInt(0, 1), splitExclusionMask = (byte)(excluded ? 1 : 0) });
                var tags = new List<ShaderTagId> { new ShaderTagId("ShadowCaster") };
                var drawing = new DrawingSettings(tags[0], new SortingSettings(camera));
                FrameBuilder.Sources.Clear();
                int before = FrameBuilder.s_DrawCount;
                FrameBuilder.DrawRendererList(data, drawing, new FilteringSettings(RenderQueueRange.all),
                    tags, shadows: ShadowObjectsFilter.AllObjects, shadowLight: 0, shadowSplit: 0);
                Assert.Equal(before + draws, FrameBuilder.s_DrawCount);
            }
            finally
            {
                data.Dispose();
                FrameBuilder.Sources.Clear(); FrameBuilder.Sources.AddRange(sources);
                ShaderPassInfo.ResetForTests(); ShaderPassInfo.s_CallForTests = previousShaderCall;
                Object.DestroyImmediate(body); Object.DestroyImmediate(rig); Object.DestroyImmediate(second);
                Object.DestroyImmediate(mesh); Object.DestroyImmediate(baked);
                Object.DestroyImmediate(camera.gameObject); Object.DestroyImmediate(light.gameObject);
                if (material != null) Object.DestroyImmediate(material);
                if (shader != null) Object.DestroyImmediate(shader);
                ProjectInfo.UnityVersion = version;
            }
        }
    }
}
