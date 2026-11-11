using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using Xunit;
using GI = UnityEngine.Experimental.GlobalIllumination;

namespace UnityEngine.Tests
{
    /// <summary>The lights global illumination is given
    /// (UnityEngine.Experimental.GlobalIllumination, Unity 6000.0 Scripting API): what
    /// LightmapperUtils extracts from a Light, LightDataGI's fields per light type, and the
    /// delegate a render pipeline registers.</summary>
    [Collection("RenderSettings state")]
    public class GlobalIlluminationLightsTests
    {
        static void WithLinearIntensity(bool linear, Action test)
        {
            bool before = GraphicsSettings.lightsUseLinearIntensity;
            try
            {
                GraphicsSettings.lightsUseLinearIntensity = linear;
                test();
            }
            finally
            {
                GraphicsSettings.lightsUseLinearIntensity = before;
            }
        }

        [Fact]
        public void ALinearColorIsNormalizedByItsLargestComponent()
        {
            var color = new Color(1f, 0.5f, 0.25f);
            // The intensity scales the gamma color before its conversion...
            WithLinearIntensity(false, () =>
            {
                var c = GI.LinearColor.Convert(color, 2f);
                Assert.Equal(Mathf.GammaToLinearSpace(2f), c.intensity, 5);
                Assert.Equal(1f, c.red, 6);
                Assert.Equal(1f / Mathf.GammaToLinearSpace(2f), c.green, 6);
                Assert.Equal(Mathf.GammaToLinearSpace(0.5f) / Mathf.GammaToLinearSpace(2f), c.blue, 6);
            });
            // ...or, with linear light intensity, the linear color.
            WithLinearIntensity(true, () =>
            {
                var c = GI.LinearColor.Convert(color, 2f);
                Assert.Equal(2f, c.intensity, 6);
                Assert.Equal(1f, c.red, 6);
                Assert.Equal(Mathf.GammaToLinearSpace(0.5f), c.green, 6);
                Assert.Equal(Mathf.GammaToLinearSpace(0.25f), c.blue, 6);
                Assert.Equal(0f, GI.LinearColor.Convert(color, 0f).intensity);
                Assert.Equal(0f, GI.LinearColor.Convert(Color.black, 3f).red);
            });
            var black = GI.LinearColor.Black();
            Assert.Equal((0f, 0f, 0f, 0f), (black.red, black.green, black.blue, black.intensity));
            Assert.Throws<ArgumentOutOfRangeException>(() => black.red = 1.5f);
            Assert.Throws<ArgumentOutOfRangeException>(() => black.blue = -0.1f);
            black.intensity = 7f;
            black.green = 1f;
            Assert.Equal((1f, 7f), (black.green, black.intensity));
        }

        [Fact]
        public void LightmapperUtilsExtractsALightAndLightDataGIHoldsItByType()
        {
            var go = new GameObject("Spot");
            try
            {
                var light = go.AddComponent<Light>();
                light.type = UnityEngine.LightType.Spot;
                light.color = new Color(1f, 0.5f, 0.25f);
                light.intensity = 3f;
                light.bounceIntensity = 2f;
                light.range = 9f;
                light.spotAngle = 60f;
                light.innerSpotAngle = 20f;
                light.shadows = LightShadows.Hard;
                go.transform.SetPositionAndRotation(new Vector3(1f, 2f, 3f), Quaternion.Euler(30f, 40f, 0f));
                WithLinearIntensity(true, () =>
                {
                    var spot = new GI.SpotLight();
                    GI.LightmapperUtils.Extract(light, ref spot);
                    Assert.Equal(light.GetInstanceID(), spot.instanceID);
                    Assert.True(spot.shadow);
                    Assert.Equal(new Vector3(1f, 2f, 3f), spot.position);
                    Assert.Equal(go.transform.rotation, spot.orientation);
                    Assert.Equal(3f, spot.color.intensity, 5);
                    // The indirect color carries the bounce intensity.
                    Assert.Equal(6f, spot.indirectColor.intensity, 5);
                    Assert.Equal(Mathf.GammaToLinearSpace(0.5f), spot.indirectColor.green, 6);
                    Assert.Equal(9f, spot.range);
                    Assert.Equal(60f * Mathf.Deg2Rad, spot.coneAngle, 6);
                    // The default inner cone, not the light's own inner angle: 46/64 of the
                    // outer half angle's tangent.
                    Assert.Equal(2f * Mathf.Atan(Mathf.Tan(30f * Mathf.Deg2Rad) * 0.71875f), spot.innerConeAngle, 6);
                    Assert.Equal(spot.innerConeAngle, GI.LightmapperUtils.ExtractInnerCone(light));
                    Assert.Equal(GI.FalloffType.Legacy, spot.falloff);
                    Assert.Equal(GI.AngularFalloffType.LUT, spot.angularFalloff);
                    Assert.Equal(0f, spot.sphereRadius);

                    var data = new GI.LightDataGI();
                    spot.innerConeAngle = 0.25f;
                    spot.angularFalloff = GI.AngularFalloffType.AnalyticAndInnerAngle;
                    data.Init(ref spot);
                    Assert.Equal(GI.LightType.Spot, data.type);
                    Assert.Equal((9f, 60f * Mathf.Deg2Rad, 0.25f), (data.range, data.coneAngle, data.innerConeAngle));
                    Assert.Equal((0f, 1f), (data.shape0, data.shape1));
                    Assert.Equal((byte)1, data.shadow);
                    Assert.Equal(GI.FalloffType.Legacy, data.falloff);
                    Assert.Equal((0, 1f), (data.cookieID, data.cookieScale));
                    Assert.Equal(6f, data.indirectColor.intensity, 5);

                    // The same light as a point light and as a directional one.
                    light.type = UnityEngine.LightType.Point;
                    light.shadows = LightShadows.None;
                    var point = new GI.PointLight();
                    GI.LightmapperUtils.Extract(light, ref point);
                    data.Init(ref point);
                    Assert.Equal(GI.LightType.Point, data.type);
                    Assert.Equal((9f, 0f, 0f, (byte)0), (data.range, data.coneAngle, data.innerConeAngle, data.shadow));
                    Assert.Equal(GI.FalloffType.Legacy, data.falloff);
                    light.type = UnityEngine.LightType.Directional;
                    var directional = new GI.DirectionalLight();
                    GI.LightmapperUtils.Extract(light, ref directional);
                    data.Init(ref directional);
                    Assert.Equal(GI.LightType.Directional, data.type);
                    Assert.Equal(0f, data.range);
                    Assert.Equal(GI.FalloffType.Undefined, data.falloff);
                    // A directional light's cone angles hold its cookie's sizes (none: 1, 1).
                    Assert.Equal((1f, 1f), (data.coneAngle, data.innerConeAngle));

                    // A light global illumination does not take.
                    data.InitNoBake(42);
                    Assert.Equal((42, GI.LightMode.Unknown), (data.instanceID, data.mode));
                });
                Assert.Equal(GI.LightMode.Realtime, GI.LightmapperUtils.Extract(LightmapBakeType.Realtime));
                Assert.Equal(GI.LightMode.Mixed, GI.LightmapperUtils.Extract(LightmapBakeType.Mixed));
                Assert.Equal(GI.LightMode.Baked, GI.LightmapperUtils.Extract(LightmapBakeType.Baked));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Fact]
        public void APipelinesDelegateConvertsTheLightsUntilItIsReset()
        {
            var go = new GameObject("Point");
            var original = GI.Lightmapping.GetDelegate();
            try
            {
                GI.Lightmapping.ResetDelegate();
                Assert.False(GI.Lightmapping.HasCustomDelegate);
                var light = go.AddComponent<Light>();
                light.type = UnityEngine.LightType.Point;
                light.range = 5f;
                var requests = new[] { light };
                using var output = new NativeArray<GI.LightDataGI>(1, Allocator.Temp);
                // Without a pipeline's delegate: LightmapperUtils' values, the legacy falloff.
                GI.Lightmapping.GetDelegate()(requests, output);
                Assert.Equal((GI.LightType.Point, GI.FalloffType.Legacy, 5f), (output[0].type, output[0].falloff, output[0].range));
                // A pipeline's own: what it writes is what global illumination gets.
                GI.Lightmapping.RequestLightsDelegate custom = (lights, converted) =>
                {
                    for (int i = 0; i < lights.Length; i++)
                    {
                        var point = new GI.PointLight();
                        GI.LightmapperUtils.Extract(lights[i], ref point);
                        var data = new GI.LightDataGI();
                        data.Init(ref point);
                        data.falloff = GI.FalloffType.InverseSquared;
                        converted[i] = data;
                    }
                };
                GI.Lightmapping.SetDelegate(custom);
                Assert.True(GI.Lightmapping.HasCustomDelegate);
                Assert.Same(custom, GI.Lightmapping.GetDelegate());
                GI.Lightmapping.GetDelegate()(requests, output);
                Assert.Equal(GI.FalloffType.InverseSquared, output[0].falloff);
                // None, and a reset, give the default one back.
                GI.Lightmapping.SetDelegate(null);
                Assert.False(GI.Lightmapping.HasCustomDelegate);
                GI.Lightmapping.SetDelegate(custom);
                GI.Lightmapping.ResetDelegate();
                Assert.False(GI.Lightmapping.HasCustomDelegate);
                GI.Lightmapping.GetDelegate()(requests, output);
                Assert.Equal(GI.FalloffType.Legacy, output[0].falloff);
            }
            finally
            {
                GI.Lightmapping.SetDelegate(original);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
