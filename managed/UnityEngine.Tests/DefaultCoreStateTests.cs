using System;
using Unbound.Runtime;
using UnityEngine.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    [Collection("Frames")]
    public sealed class DefaultCoreStateTests : IDisposable
    {
        readonly string release = ProjectInfo.UnityVersion;
        public DefaultCoreStateTests() { ProjectInfo.UnityVersion = "6000.0.71f1"; }
        public void Dispose() { ProjectInfo.UnityVersion = release; }

        [Fact]
        public void OperationValidityAndFactoryCapabilitiesAreIndependent()
        {
            var bare = new AsyncOperation();
            Assert.Throws<NullReferenceException>(() => bare.isDone);
            Assert.Throws<NullReferenceException>(() => bare.priority = 4);
            Assert.Throws<NullReferenceException>(() => bare.completed += _ => { });
            bare.completed -= null;
            Assert.Throws<ArgumentNullException>(() => new ResourceRequest().asset);
            Assert.Null(new ManagedRequest().asset);
            var preload = AsyncOperation.Bind(new AsyncOperation(), completionProgress: 0f);
            preload.priority = -4; preload.allowSceneActivation = false; preload.Complete();
            Assert.True(preload.isDone); Assert.Equal(0f, preload.progress);
            Assert.Equal(-4, preload.priority); Assert.True(preload.allowSceneActivation);
            int calls = 0;
            preload.completed += value => { Assert.Same(preload, value); calls++; };
            Assert.Equal(1, calls);
            var activation = AsyncOperation.Bind(new AsyncOperation(), AsyncOperation.OperationCapabilities.SceneActivation);
            activation.priority = 9; activation.allowSceneActivation = false;
            Assert.Equal(0, activation.priority); Assert.False(activation.allowSceneActivation);
        }
        sealed class ManagedRequest : ResourceRequest { protected override Object GetResult() => null; }

        [Fact]
        public void MeshLayoutsSurviveEmptyDataAndRejectMissingStreamsBeforeMutation()
        {
            var mesh = new Mesh();
            try
            {
                Assert.Equal(0, mesh.vertexBufferCount);
                Assert.False(mesh.HasVertexAttribute(VertexAttribute.Position));
                mesh.vertices = Array.Empty<Vector3>();
                Assert.True(mesh.HasVertexAttribute(VertexAttribute.Position));
                Assert.Equal(0, mesh.vertexBufferCount);
                mesh.vertices = new[] { Vector3.zero, Vector3.one };
                Assert.Equal(1, mesh.vertexBufferCount);
                Assert.Throws<ArgumentException>(() => mesh.SetVertexBufferParams(3,
                    new VertexAttributeDescriptor(VertexAttribute.Position, stream: 2)));
                Assert.Equal(2, mesh.vertexCount);
                mesh.Clear(true);
                Assert.True(mesh.HasVertexAttribute(VertexAttribute.Position));
                Assert.Equal(0, mesh.vertexBufferCount);
                mesh.Clear(false);
                Assert.False(mesh.HasVertexAttribute(VertexAttribute.Position));
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Fact]
        public void ExplicitLayoutsAndClearedMeshesAcceptNewChannels()
        {
            var mesh = new Mesh();
            try
            {
                mesh.SetVertexBufferParams(3, new VertexAttributeDescriptor(VertexAttribute.Position));
                mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
                mesh.triangles = new[] { 0, 1, 2 };
                mesh.RecalculateNormals();
                Assert.True(mesh.HasVertexAttribute(VertexAttribute.Normal));
                Assert.Equal(Vector3.forward, mesh.normals[0]);
                mesh.Clear(true);
                Assert.True(mesh.HasVertexAttribute(VertexAttribute.Normal));
                mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
                Assert.Equal(3, mesh.normals.Length);
                mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up };
                Assert.True(mesh.HasVertexAttribute(VertexAttribute.TexCoord0));
                mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up };
                Assert.True(mesh.HasVertexAttribute(VertexAttribute.Normal));
                mesh.normals = null;
                Assert.False(mesh.HasVertexAttribute(VertexAttribute.Normal));
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Fact]
        public void MeshDataChecksDefaultDisposedAndReadOnlyViews()
        {
            Mesh.MeshData empty = default;
            Assert.Throws<NullReferenceException>(() => empty.vertexCount);
            var mesh = new Mesh { vertices = new[] { Vector3.zero } };
            try
            {
                var array = Mesh.AcquireReadOnlyMeshData(mesh);
                var view = array[0];
                Assert.Equal(1, view.vertexCount);
                Assert.Throws<InvalidOperationException>(() => view.SetVertexBufferParams(2, new VertexAttributeDescriptor(VertexAttribute.Position)));
                array.Dispose();
                Assert.Throws<ObjectDisposedException>(() => view.vertexCount);
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Fact]
        public void StripCurvesAreCopiesAndHaveWeightedSingleKeyDefaults()
        {
            var go = new GameObject("curve ownership");
            try
            {
                var line = go.AddComponent<LineRenderer>();
                var trail = go.AddComponent<TrailRenderer>();
                foreach (var get in new Func<AnimationCurve>[] { () => line.widthCurve, () => trail.widthCurve })
                {
                    var curve = get(); Assert.Single(curve.keys);
                    Assert.Equal(1f / 3f, curve.keys[0].inWeight);
                    var key = curve.keys[0]; key.value = 4; curve.MoveKey(0, key);
                    Assert.Equal(1f, get().keys[0].value);
                }
                var assigned = new AnimationCurve(new Keyframe(0, 2));
                line.widthCurve = assigned; trail.widthCurve = assigned;
                assigned.MoveKey(0, new Keyframe(0, 7));
                Assert.Equal(2f, line.widthCurve.Evaluate(0)); Assert.Equal(2f, trail.widthCurve.Evaluate(0));
                Assert.Throws<ArgumentNullException>(() => line.widthCurve = null);
                Assert.Throws<ArgumentNullException>(() => trail.widthCurve = null);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Fact]
        public void LodMembershipAndRendererAliasesFollowSharedState()
        {
            var go = new GameObject("LOD membership");
            try
            {
                var billboard = go.AddComponent<BillboardRenderer>();
                Assert.Null(Assert.Single(billboard.sharedMaterials));
                var tilemap = new GameObject("tile renderer").AddComponent<UnityEngine.Tilemaps.TilemapRenderer>(); tilemap.transform.SetParent(go.transform);
                Assert.False(tilemap.castShadows); Assert.False(tilemap.useLightProbes);
                Assert.False(tilemap.receiveShadows); Assert.Equal(ReflectionProbeUsage.Off, tilemap.reflectionProbeUsage);
                var group = go.AddComponent<LODGroup>();
                var a = go.AddComponent<MeshRenderer>();
                var b = new GameObject("second").AddComponent<MeshRenderer>(); b.transform.SetParent(go.transform);
                Assert.Equal(3, group.lodCount);
                group.SetLODs(new[] { new LOD(.5f, new[] { a }), new LOD(.1f, new[] { b }) });
                Assert.True(a.isLOD0); Assert.False(b.isLOD0);
                group.ForceLOD(1); Assert.False(b.isLOD0);
                group.enabled = false; Assert.True(b.isLOD0);
                group.enabled = true;
                group.SetLODs(new[] { new LOD(.5f, new[] { b }), new LOD(.1f, new[] { b }) });
                Assert.True(b.isLOD0);
                a.shadowCastingMode = ShadowCastingMode.ShadowsOnly; Assert.True(a.castShadows);
                a.castShadows = false; Assert.Equal(ShadowCastingMode.Off, a.shadowCastingMode);
                a.rayTracingMode = (UnityEngine.Experimental.Rendering.RayTracingMode)(-1);
                Assert.Equal(3, (int)a.rayTracingMode);
                a.rayTracingAccelerationStructureBuildFlags = (RayTracingAccelerationStructureBuildFlags)129;
                Assert.Equal(1, (int)a.rayTracingAccelerationStructureBuildFlags);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Fact]
        public void PhysicalSettingsClampIndependentlyAndCloneWithTheCamera()
        {
            var go = new GameObject("camera configuration");
            GameObject clone = null;
            try
            {
                var camera = go.AddComponent<Camera>();
                camera.curvature = new Vector2(7, 4); camera.iso = -1; camera.aperture = float.NaN;
                camera.focusDistance = -1; camera.barrelClipping = 2;
                Assert.Equal(new Vector2(7, 4), camera.curvature);
                Assert.Equal(1, camera.iso); Assert.True(float.IsNaN(camera.aperture));
                Assert.Equal(.1f, camera.focusDistance); Assert.Equal(1f, camera.barrelClipping);
                clone = Object.Instantiate(go); var copy = clone.GetComponent<Camera>();
                Assert.Equal(camera.curvature, copy.curvature); Assert.True(float.IsNaN(copy.aperture));
                Assert.Equal(camera.focusDistance, copy.focusDistance);
            }
            finally { if (clone) Object.DestroyImmediate(clone); Object.DestroyImmediate(go); }
        }
    }
}
