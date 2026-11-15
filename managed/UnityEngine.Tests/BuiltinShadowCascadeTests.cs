using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    [Collection("Frames")]
    public class BuiltinShadowCascadeTests
    {
        [Theory]
        [InlineData(0,1)]
        [InlineData(1,1)]
        [InlineData(2,2)]
        [InlineData(4,4)]
        public void OrthographicUsesOneMapAndPerspectiveUsesTheQualityCount(int quality,int perspective)
        {
            string version=ProjectInfo.UnityVersion;int previous=QualitySettings.shadowCascades;
            try
            {
                ProjectInfo.UnityVersion="6000.0.71f1";QualitySettings.shadowCascades=quality;
                Assert.Equal(1,FrameBuilder.BuiltinShadowCascades(true));
                Assert.Equal(perspective,FrameBuilder.BuiltinShadowCascades(false));
                ProjectInfo.UnityVersion="2022.3.1f1";
                Assert.Equal(perspective,FrameBuilder.BuiltinShadowCascades(true));
            }
            finally {QualitySettings.shadowCascades=previous;ProjectInfo.UnityVersion=version;}
        }

        [Theory]
        [InlineData(1, 128)]
        [InlineData(2, 64)]
        [InlineData(4, 128)]
        public void AtlasTilesCoverTheSelectedMap(int cascades, int height)
        {
            string version = ProjectInfo.UnityVersion;
            try
            {
                ProjectInfo.UnityVersion = "6000.0.71f1";
                Assert.Equal(height, FrameBuilder.BuiltinShadowAtlasHeight(128, cascades));
                int tile = cascades == 1 ? 128 : 64;
                for (int i = 0; i < cascades; i++)
                {
                    var transform = FrameBuilder.BuiltinShadowTileTransform(128, height, tile, i);
                    var low = transform.MultiplyPoint3x4(new Vector3(-1, -1, 0));
                    var high = transform.MultiplyPoint3x4(new Vector3(1, 1, 0));
                    Assert.Equal((i % 2) * (float)tile / 128, low.x);
                    Assert.Equal((i / 2) * (float)tile / height, low.y);
                    Assert.Equal((i % 2 + 1) * (float)tile / 128, high.x);
                    Assert.Equal((i / 2 + 1) * (float)tile / height, high.y);
                }
                ProjectInfo.UnityVersion = "2022.3.1f1";
                Assert.Equal(128, FrameBuilder.BuiltinShadowAtlasHeight(128, cascades));
            }
            finally { ProjectInfo.UnityVersion = version; }
        }

        [Fact]
        public void BuiltinPlanesUseExactCameraClipsAndIgnoreTheScriptCullingMatrix()
        {
            var go = new GameObject("builtin shadow camera");
            try
            {
                var camera = go.AddComponent<Camera>();
                camera.nearClipPlane = 0.3f;
                camera.farClipPlane = 30;
                camera.transform.position = new Vector3(0, 0, -6);
                var before = new Plane[6];
                camera.BuiltinCullingPlanes(before);
                Assert.True(camera.TryGetCullingParameters(out var parameters));
                for (int i = 0; i < 6; i++) Assert.Equal(parameters.GetCullingPlane(i), before[i]);
                camera.cullingMatrix = Matrix4x4.Translate(new Vector3(100, 0, 0));
                var after = new Plane[6];
                camera.BuiltinCullingPlanes(after);
                Assert.Equal(before, after);
                Assert.True(camera.TryGetCullingParameters(out parameters));
                Assert.NotEqual(parameters.GetCullingPlane(0), after[0]);
                Assert.Equal(0f, after[4].GetDistanceToPoint(new Vector3(0, 0, -5.7f)), 5);
                Assert.Equal(0f, after[5].GetDistanceToPoint(new Vector3(0, 0, 24)), 5);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Fact]
        public void SplitFilteringRetainsMultiBoneDrawsOutsideTheirFallbackBounds()
        {
            string version = ProjectInfo.UnityVersion;
            var body = new GameObject("shadow skin");
            var rig = new GameObject("shadow rig");
            var second = new GameObject("shadow second bone");
            var ordinary = new GameObject("shadow ordinary");
            var mesh = new Mesh();
            var baked = new Mesh();
            try
            {
                ProjectInfo.UnityVersion = "6000.0.71f1";
                body.transform.position = ordinary.transform.position = new Vector3(100, 0, 0);
                second.transform.position = Vector3.right;
                mesh.vertices = new[] { Vector3.zero, Vector3.up, Vector3.right };
                mesh.triangles = new[] { 0, 1, 2 };
                mesh.bindposes = new[] { Matrix4x4.identity, Matrix4x4.identity };
                mesh.boneWeights = new[] { new BoneWeight { weight0 = 1 }, new BoneWeight { weight0 = 1 },
                    new BoneWeight { weight0 = 1, boneIndex0 = 1 } };
                mesh.RecalculateBounds();
                var skin = body.AddComponent<SkinnedMeshRenderer>();
                skin.ReadSerialized(YNode.MakeMap(), null);
                skin.bones = new[] { rig.transform, second.transform };
                skin.rootBone = rig.transform;
                skin.sharedMesh = mesh;
                skin.updateWhenOffscreen = true;
                skin.PrepareCullingBounds();
                Assert.False(skin.HasMeasuredCullingBounds);
                var split = default(UnityEngine.Rendering.ShadowSplitData);
                split.cullingPlaneCount = 1;
                split.SetCullingPlane(0, new Plane(Vector3.left, 10));
                Assert.False(ShadowCasterCulling.InsidePlanes(skin.CullingBounds, split));
                skin.BakeMesh(baked);
                foreach (var vertex in baked.vertices)
                    Assert.True(split.GetCullingPlane(0).GetDistanceToPoint(body.transform.TransformPoint(vertex)) >= 0);
                Assert.True(FrameBuilder.BuiltinSeesShadowSplit(new FrameBuilder.DrawItem { Renderer = skin }, split));
                ordinary.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = ordinary.AddComponent<MeshRenderer>();
                Assert.False(FrameBuilder.BuiltinSeesShadowSplit(new FrameBuilder.DrawItem { Renderer = renderer }, split));
                Assert.False(FrameBuilder.BuiltinSeesShadowSplit(new FrameBuilder.DrawItem { CullBounds = renderer.bounds }, split));
            }
            finally
            {
                Object.DestroyImmediate(body); Object.DestroyImmediate(rig); Object.DestroyImmediate(second);
                Object.DestroyImmediate(ordinary); Object.DestroyImmediate(mesh); Object.DestroyImmediate(baked);
                ProjectInfo.UnityVersion = version;
            }
        }
    }
}
