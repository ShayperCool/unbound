using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Unbound.Tests;
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

        sealed class Logs : ILogHandler
        {
            public readonly List<string> messages = new List<string>();
            public void LogFormat(LogType type, Object context, string format, params object[] args) => messages.Add(string.Format(format, args));
            public void LogException(Exception exception, Object context) { }
        }

        [Fact]
        public void UnmeasuredAutomaticBoundsReportAndDoNotEnableDrawCulling()
        {
            var skin = body.AddComponent<SkinnedMeshRenderer>();
            var mesh = new Mesh { vertices = new[] { Vector3.zero }, bindposes = new[] { Matrix4x4.identity, Matrix4x4.identity },
                boneWeights = new[] { new BoneWeight {weight0=.5f,weight1=.5f,boneIndex1=1} } };
            skin.sharedMesh = mesh;
            skin.bones = new[] {rig.transform,body.transform};
            var logs = new Logs();
            var saved = Debug.unityLogger.logHandler;
            try
            {
                Debug.unityLogger.logHandler = logs;
                skin.PrepareCullingBounds();
                Assert.False(skin.HasMeasuredCullingBounds);
                Assert.Contains(logs.messages, x => x.Contains("SkinnedMeshRenderer bounds outside measured support"));
                skin.localBounds = new Bounds(Vector3.zero,Vector3.one);
                Assert.True(skin.HasMeasuredCullingBounds);
                skin.updateWhenOffscreen = true;
                Assert.False(skin.HasMeasuredCullingBounds);
            }
            finally { Debug.unityLogger.logHandler = saved; Object.DestroyImmediate(mesh); }
        }
    }
}
