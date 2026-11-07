using System;
using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using Unbound.Tests;
using UnityEngine.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    public sealed class SkinnedBoundsTests : IDisposable
    {
        readonly string version = ProjectInfo.UnityVersion;
        readonly GameObject rig = new GameObject("rig");
        readonly GameObject body = new GameObject("body");
        public SkinnedBoundsTests() => ProjectInfo.UnityVersion = "6000.0.71f1";
        public void Dispose()
        {
            Object.DestroyImmediate(body);
            Object.DestroyImmediate(rig);
            ProjectInfo.UnityVersion = version;
        }

        [Fact]
        public void ExplicitBoundsFollowRootAndSurviveLocalResetAndCloning()
        {
            var skin = body.AddComponent<SkinnedMeshRenderer>();
            skin.enabled = false;
            body.transform.position = new Vector3(20, 3, 2);
            skin.rootBone = rig.transform;
            skin.localBounds = new Bounds(new Vector3(1, 2, 3), new Vector3(2, 4, 6));
            rig.transform.position = new Vector3(4, 5, 6);
            rig.transform.rotation = Quaternion.Euler(0, 0, 90);
            rig.transform.localScale = new Vector3(2, 3, 4);
            Near.Equal(-2, skin.bounds.center.x);
            Near.Equal(7, skin.bounds.center.y);
            Near.Equal(18, skin.bounds.center.z);
            Near.Equal(12, skin.bounds.size.x);
            Near.Equal(4, skin.bounds.size.y);
            Near.Equal(24, skin.bounds.size.z);
            skin.ResetLocalBounds();
            Assert.Equal(new Vector3(2, 4, 6), skin.localBounds.size);
            var copy = Object.Instantiate(body);
            try { Assert.Equal(skin.bounds, copy.GetComponent<SkinnedMeshRenderer>().bounds); }
            finally { Object.DestroyImmediate(copy); }
            skin.rootBone = null;
            Assert.Equal(new Vector3(21, 5, 5), skin.bounds.center);
        }

        [Fact]
        public void CullingRefreshesWorldBoundsWhileLocalWritesInvalidateTheCache()
        {
            var skin = body.AddComponent<SkinnedMeshRenderer>();
            skin.rootBone = rig.transform;
            skin.localBounds = new Bounds(Vector3.zero, Vector3.one);
            Assert.Equal(Vector3.zero, skin.bounds.center);
            rig.transform.position = new Vector3(12, 0, 0);
            Assert.Equal(Vector3.zero, skin.bounds.center);
            skin.PrepareCullingBounds();
            Assert.Equal(rig.transform.position, skin.bounds.center);
            skin.localBounds = new Bounds(Vector3.one, Vector3.one);
            Assert.Equal(new Vector3(13, 1, 1), skin.bounds.center);
            skin.bounds = new Bounds(Vector3.zero, Vector3.one);
            skin.PrepareCullingBounds();
            Assert.Equal(Vector3.zero, skin.bounds.center);
            skin.ResetBounds();
            Assert.Equal(new Vector3(13, 1, 1), skin.bounds.center);
        }

        Mesh RigidMesh()
        {
            return new Mesh { vertices = new[] { new Vector3(-.5f, 0, 0), new Vector3(.5f, 0, 0) },
                bindposes = new[] { Matrix4x4.identity },
                boneWeights = new[] { new BoneWeight {weight0=1}, new BoneWeight {weight0=1} } };
        }

        [Fact]
        public void RawVertexEditsDisableAutomaticBoundsTrustUntilMeasured()
        {
            var skin = body.AddComponent<SkinnedMeshRenderer>();
            var mesh = RigidMesh();
            mesh.SetVertexBufferParams(2,
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0),
                new VertexAttributeDescriptor(VertexAttribute.BlendWeight, VertexAttributeFormat.Float32, 4, 1),
                new VertexAttributeDescriptor(VertexAttribute.BlendIndices, VertexAttributeFormat.UInt32, 4, 1));
            skin.sharedMesh = mesh;
            skin.bones = new[] {rig.transform};
            skin.rootBone = rig.transform;
            skin.updateWhenOffscreen = true;
            try
            {
                Assert.True(skin.HasMeasuredCullingBounds);
                var originalArray = mesh.m_Vertices;
                mesh.SetVertexBufferData(new[] { new Vector3(11.5f,0,0),new Vector3(12.5f,0,0) },0,0,2);
                Assert.Same(originalArray,mesh.m_Vertices);
                Assert.False(skin.HasMeasuredCullingBounds);
                ReportsLimitation(skin);
                var clone = Object.Instantiate(mesh);
                try { skin.sharedMesh=clone; Assert.False(skin.HasMeasuredCullingBounds); }
                finally { Object.DestroyImmediate(clone); }
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        // Unity 6000.0.71f1 (skinnedbounds static/swap/*): a runtime renderer computes its
        // default local bounds on their first query and keeps them through later sharedMesh
        // assignments, a null mesh and ResetLocalBounds; never queried, they come from the
        // mesh assigned at the first query. Rigid one-bone and two-bone meshes behave alike, and
        // the public world box is that local bound in root space in every case.
        [Theory]
        [InlineData(false, true)]
        [InlineData(false, false)]
        [InlineData(true, true)]
        [InlineData(true, false)]
        public void DefaultBoundsAreTakenAtFirstQueryAndSurviveMeshSwaps(bool twoBones, bool queried)
        {
            Mesh Quad(float scale, Vector3 offset)
            {
                var v = new[] { new Vector3(-.5f, -.5f, 0), new Vector3(.5f, -.5f, 0), new Vector3(-.5f, .5f, 0), new Vector3(.5f, .5f, .25f) };
                for (int i = 0; i < v.Length; i++) v[i] = v[i] * scale + offset;
                var weights = new BoneWeight[4];
                for (int i = 0; i < 4; i++) weights[i] = twoBones && i >= 2 ? new BoneWeight { boneIndex0 = 1, weight0 = 1 } : new BoneWeight { weight0 = 1 };
                var mesh = new Mesh { vertices = v, triangles = new[] { 0, 2, 1, 1, 2, 3 }, boneWeights = weights };
                mesh.bindposes = twoBones ? new[] { Matrix4x4.identity, Matrix4x4.Translate(new Vector3(0, -1, 0)) } : new[] { Matrix4x4.identity };
                mesh.RecalculateBounds();
                return mesh;
            }
            var second = new GameObject("second");
            second.transform.SetParent(rig.transform, false);
            second.transform.localPosition = new Vector3(0, 1, 0);
            rig.transform.position = new Vector3(5, 6, 7);
            var skin = body.AddComponent<SkinnedMeshRenderer>();
            skin.enabled = false;
            skin.bones = twoBones ? new[] { rig.transform, second.transform } : new[] { rig.transform };
            skin.rootBone = rig.transform;
            var first = Quad(1f, Vector3.zero);
            var other = Quad(3f, new Vector3(2, -1, 0.5f));
            try
            {
                skin.sharedMesh = first;
                if (queried)
                {
                    var stored = skin.localBounds;
                    Near.Equal(new Vector3(0, 0, 0.125f), stored.center, 1e-5f);
                    Near.Equal(new Vector3(1, 1, 0.25f), stored.size, 1e-5f);
                    skin.sharedMesh = null;
                    Assert.Equal(stored, skin.localBounds);
                    skin.sharedMesh = other;
                    Assert.Equal(stored, skin.localBounds);
                    skin.ResetLocalBounds();
                    Assert.Equal(stored, skin.localBounds);
                    skin.sharedMesh = null;
                    Near.Equal(rig.transform.TransformPoint(stored.center), skin.bounds.center, 1e-4f);
                    skin.sharedMesh = other;
                }
                else
                {
                    skin.sharedMesh = other;
                    Near.Equal(new Vector3(2, -1, 0.875f), skin.localBounds.center, 1e-5f);
                    Near.Equal(new Vector3(3, 3, 0.75f), skin.localBounds.size, 1e-5f);
                }
                Near.Equal(rig.transform.TransformPoint(skin.localBounds.center), skin.bounds.center, 1e-4f);
            }
            finally
            {
                Object.DestroyImmediate(second);
                Object.DestroyImmediate(other);
                Object.DestroyImmediate(first);
            }
        }

        [Fact]
        public void AutomaticBoundsDoNotRetainTrustAfterChangingMeshOrBones()
        {
            var skin = body.AddComponent<SkinnedMeshRenderer>();
            var mesh = RigidMesh();
            skin.sharedMesh = mesh;
            skin.bones = new[] {rig.transform};
            skin.rootBone = rig.transform;
            var other = Object.Instantiate(mesh);
            try
            {
                var stored = skin.localBounds;
                Assert.True(skin.HasMeasuredCullingBounds);
                other.bindposes = new[] {Matrix4x4.identity,Matrix4x4.identity};
                skin.sharedMesh = other;
                skin.bones = new[] {rig.transform,body.transform};
                Assert.Equal(stored,skin.localBounds);
                Assert.False(skin.HasMeasuredCullingBounds);
                ReportsLimitation(skin);
                skin.localBounds = stored;
                Assert.True(skin.HasMeasuredCullingBounds);
                skin.sharedMesh = mesh;
                skin.bones = new[] {rig.transform};
                skin.updateWhenOffscreen = true;
                Assert.True(skin.HasMeasuredCullingBounds);
                skin.bones = new[] {rig.transform,body.transform};
                Assert.False(skin.HasMeasuredCullingBounds);
            }
            finally { Object.DestroyImmediate(other); Object.DestroyImmediate(mesh); }
        }

        public sealed class WillRender : MonoBehaviour
        {
            public int calls;
            void OnWillRenderObject() => calls++;
        }

        [Fact]
        public void ScriptableCullingRefreshesTheCachedWorldBox()
        {
            var skin = body.AddComponent<SkinnedMeshRenderer>();
            var willRender = body.AddComponent<WillRender>();
            skin.rootBone = rig.transform;
            body.layer=30;
            skin.localBounds = new Bounds(Vector3.zero,Vector3.one);
            rig.transform.position = new Vector3(20,0,0);
            Assert.Equal(rig.transform.position,skin.bounds.center);
            rig.transform.position = Vector3.zero;
            var cameraObject = new GameObject("camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.transform.position = new Vector3(0,0,-10);
            camera.cullingMask=1<<30;
            try
            {
                Assert.True(camera.TryGetCullingParameters(out var parameters));
                var data = Culling.Get(Culling.Cull(ref parameters));
                Assert.Contains(skin,data.Visible);
                Assert.Equal(1,willRender.calls);
                Assert.Equal(Vector3.zero,skin.bounds.center);
            }
            finally { Culling.EndFrame(); Object.DestroyImmediate(cameraObject); }
        }

        sealed class Logs : ILogHandler
        {
            public readonly List<string> messages = new List<string>();
            public void LogFormat(LogType type, Object context, string format, params object[] args) => messages.Add(string.Format(format, args));
            public void LogException(Exception exception, Object context) { }
        }

        static void ReportsLimitation(SkinnedMeshRenderer skin)
        {
            var logs = new Logs();
            var saved = Debug.unityLogger.logHandler;
            const string diagnostic = "SkinnedMeshRenderer bounds outside measured support";
            int priorHits = Stubs.HitReport().FirstOrDefault(x => x.Key == diagnostic).Value;
            try
            {
                Debug.unityLogger.logHandler = logs;
                skin.PrepareCullingBounds();
                Assert.True(Stubs.HitReport().First(x => x.Key == diagnostic).Value > priorHits);
                if (priorHits == 0) Assert.Contains(logs.messages, x => x.Contains(diagnostic));
            }
            finally { Debug.unityLogger.logHandler = saved; }
        }

        [Fact]
        public void UnmeasuredAutomaticBoundsReportAndDoNotEnableDrawCulling()
        {
            var skin = body.AddComponent<SkinnedMeshRenderer>();
            var mesh = new Mesh { vertices = new[] { Vector3.zero }, bindposes = new[] { Matrix4x4.identity, Matrix4x4.identity },
                boneWeights = new[] { new BoneWeight {weight0=.5f,weight1=.5f,boneIndex1=1} } };
            skin.sharedMesh = mesh;
            skin.bones = new[] {rig.transform,body.transform};
            try
            {
                ReportsLimitation(skin);
                Assert.False(skin.HasMeasuredCullingBounds);
                skin.localBounds = new Bounds(Vector3.zero,Vector3.one);
                Assert.True(skin.HasMeasuredCullingBounds);
                skin.updateWhenOffscreen = true;
                Assert.False(skin.HasMeasuredCullingBounds);
            }
            finally { Object.DestroyImmediate(mesh); }
        }
    }
}
