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

        /// <summary>A directional light's cascades by their culling spheres (stable fit): a caster
        /// whose shadow stays inside the first cascade's sphere is left out of the second; one
        /// reaching past it is drawn into both; a caster beside every sphere into none. Close fit
        /// takes every cascade (the planes alone decide).</summary>
        [Fact]
        public void ACasterInsideAnEarlierCascadesSphereLeavesTheLaterOnes()
        {
            string version=ProjectInfo.UnityVersion;
            ProjectInfo.UnityVersion="6000.0.71f1";
            var projection=QualitySettings.shadowProjection;
            var camera=new GameObject("camera").AddComponent<Camera>();
            var sun=new GameObject("sun").AddComponent<Light>();
            CullingData data=null;
            try
            {
                QualitySettings.shadowProjection=ShadowProjection.StableFit;
                sun.type=LightType.Directional;
                sun.shadows=LightShadows.Hard;
                // Shining straight down.
                sun.transform.rotation=Quaternion.Euler(90f,0f,0f);
                Assert.True(camera.TryGetCullingParameters(out var parameters));
                parameters.shadowDistance=50f;
                var context=new ScriptableRenderContext();
                var results=context.Cull(ref parameters);
                data=results.Data;
                Assert.Equal(LightType.Directional,data.Lights[0].lightType);
                var splits=new NativeArray<ShadowSplitData>(2,Allocator.Temp);
                var lights=new NativeArray<LightShadowCasterCullingInfo>(1,Allocator.Temp);
                try
                {
                    splits[0]=new ShadowSplitData { cullingSphere=new Vector4(0f,0f,10f,10f) };
                    splits[1]=new ShadowSplitData { cullingSphere=new Vector4(0f,0f,30f,20f) };
                    lights[0]=new LightShadowCasterCullingInfo { splitRange=new RangeInt(0,2) };
                    context.CullShadowCasters(results,new ShadowCastersCullingInfos { splitBuffer=splits,perLightInfos=lights });
                }
                finally { splits.Dispose();lights.Dispose(); }
                // Its shadow falls inside the first sphere before leaving the view.
                var held=new Bounds(new Vector3(0f,2f,10f),Vector3.one);
                Assert.True(ShadowCasterCulling.SeesSplit(data,0,0,held,default));
                Assert.False(ShadowCasterCulling.SeesSplit(data,0,1,held,default));
                // Between the spheres' centers: reaches past the first sphere.
                var between=new Bounds(new Vector3(0f,0f,19f),Vector3.one);
                Assert.True(ShadowCasterCulling.SeesSplit(data,0,0,between,default));
                Assert.True(ShadowCasterCulling.SeesSplit(data,0,1,between,default));
                // Beside both spheres (across the light).
                var beside=new Bounds(new Vector3(40f,0f,10f),Vector3.one);
                Assert.False(ShadowCasterCulling.SeesSplit(data,0,0,beside,default));
                Assert.False(ShadowCasterCulling.SeesSplit(data,0,1,beside,default));
                QualitySettings.shadowProjection=ShadowProjection.CloseFit;
                Assert.True(ShadowCasterCulling.SeesSplit(data,0,1,held,default));
                Assert.True(ShadowCasterCulling.SeesSplit(data,0,1,beside,default));
            }
            finally
            {
                QualitySettings.shadowProjection=projection;
                data?.Reset();Object.DestroyImmediate(camera.gameObject);Object.DestroyImmediate(sun.gameObject);ProjectInfo.UnityVersion=version;
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
