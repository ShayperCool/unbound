using System;
using Unity.Collections;
using UnityEngine.Rendering;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    [Collection("Frames")]
    public class ShadowSplitCullingTests
    {
        [Fact]
        public void CullingKeepsTheSelectedSplitsAfterTheirInputArraysAreDisposedAndClearsOnReuse()
        {
            string version=ProjectInfo.UnityVersion;
            ProjectInfo.UnityVersion="6000.0.71f1";
            var camera=new GameObject("camera").AddComponent<Camera>();
            CullingData data=null;
            try
            {
                Assert.True(camera.TryGetCullingParameters(out var parameters));
                var context=new ScriptableRenderContext();
                var results=context.Cull(ref parameters);
                data=results.Data;
                var planes=new NativeArray<ShadowSplitData>(2,Allocator.Temp);
                var lights=new NativeArray<LightShadowCasterCullingInfo>(1,Allocator.Temp);
                try
                {
                    var left=new ShadowSplitData { cullingPlaneCount=1 };
                    left.SetCullingPlane(0,new Plane(Vector3.left,0));
                    var right=new ShadowSplitData { cullingPlaneCount=1 };
                    right.SetCullingPlane(0,new Plane(Vector3.right,0));
                    planes[0]=left;planes[1]=right;
                    lights[0]=new LightShadowCasterCullingInfo { splitRange=new RangeInt(0,2) };
                    context.CullShadowCasters(results,new ShadowCastersCullingInfos { splitBuffer=planes,perLightInfos=lights });
                    // Later edits to the caller's arrays cannot change the completed culling.
                    planes[1]=left;
                }
                finally { planes.Dispose();lights.Dispose(); }
                var positive=new Bounds(Vector3.right,Vector3.one*0.2f);
                Assert.False(ShadowCasterCulling.SeesSplit(data,0,0,positive,default));
                Assert.True(ShadowCasterCulling.SeesSplit(data,0,1,positive,default));
                Assert.True(ShadowCasterCulling.SeesSplit(data,0,0,new Bounds(Vector3.zero,Vector3.one),default));
                data.Reset();
                Assert.True(ShadowCasterCulling.SeesSplit(data,0,0,positive,default));
                Assert.Empty(data.ShadowLights);Assert.Empty(data.ShadowSplits);
            }
            finally
            {
                data?.Reset();Object.DestroyImmediate(camera.gameObject);ProjectInfo.UnityVersion=version;
            }
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(float.NaN)]
        public void UnknownOrNonfiniteBoundsAreNotDiscarded(float size)
        {
            var split=new ShadowSplitData { cullingPlaneCount=1 };
            split.SetCullingPlane(0,new Plane(Vector3.right,-10));
            Assert.True(ShadowCasterCulling.InsidePlanes(new Bounds(Vector3.zero,Vector3.one*size),split));
        }

        [Fact]
        public void SplitExclusionsAndLegacySettingsUseTheirOwnPlanes()
        {
            string version=ProjectInfo.UnityVersion;
            ProjectInfo.UnityVersion="6000.0.71f1";
            var data=new CullingData();
            try
            {
                var split=new ShadowSplitData { cullingPlaneCount=1 };
                split.SetCullingPlane(0,new Plane(Vector3.left,0));
                var positive=new Bounds(Vector3.right,Vector3.one*0.2f);
                Assert.False(ShadowCasterCulling.SeesSplit(data,0,-1,positive,split));
                data.ShadowLights.Add(new LightShadowCasterCullingInfo { splitRange=new RangeInt(0,1),splitExclusionMask=1 });
                data.ShadowSplits.Add(default);
                Assert.False(ShadowCasterCulling.SeesSplit(data,0,0,positive,default));
                ProjectInfo.UnityVersion="2022.3.1f1";
                Assert.True(ShadowCasterCulling.SeesSplit(data,0,0,positive,split));
            }
            finally { data.Reset();ProjectInfo.UnityVersion=version; }
        }
    }
}
