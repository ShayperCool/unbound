using System;
using Unbound.Runtime;
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
        public void AnAreaLightsRangeIsDilatedByItsSizeAndLightmapperUtilsGivesThatRange()
        {
            var go = new GameObject("Area");
            try
            {
                var light = go.AddComponent<Light>();
                light.range = 5f;
                light.areaSize = new Vector2(2f, 1f);
                // A rectangle: its range plus half its diagonal.
                light.type = UnityEngine.LightType.Rectangle;
                float rectangle = 5f + 0.5f * Mathf.Sqrt(5f);
                Assert.Equal(rectangle, light.dilatedRange, 6);
                var rect = new GI.RectangleLight();
                GI.LightmapperUtils.Extract(light, ref rect);
                Assert.Equal(light.dilatedRange, rect.range);
                Assert.Equal((2f, 1f), (rect.width, rect.height));
                // A disc: its range plus half of areaSize.x (its radius), whatever areaSize.y.
                light.type = UnityEngine.LightType.Disc;
                Assert.Equal(6f, light.dilatedRange);
                light.areaSize = new Vector2(2f, 7f);
                Assert.Equal(6f, light.dilatedRange);
                var disc = new GI.DiscLight();
                GI.LightmapperUtils.Extract(light, ref disc);
                Assert.Equal(6f, disc.range);
                // A rectangle without size, and the other light types: the range itself.
                light.type = UnityEngine.LightType.Rectangle;
                light.areaSize = Vector2.zero;
                Assert.Equal(5f, light.dilatedRange);
                light.areaSize = new Vector2(2f, 1f);
                foreach (var type in new[] { UnityEngine.LightType.Point, UnityEngine.LightType.Spot, UnityEngine.LightType.Directional })
                {
                    light.type = type;
                    Assert.Equal(5f, light.dilatedRange);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Fact]
        public void LightmapperUtilsAppliesTheColorTemperatureToTheConvertedComponents()
        {
            var go = new GameObject("Warm");
            try
            {
                var light = go.AddComponent<Light>();
                light.type = UnityEngine.LightType.Point;
                light.color = new Color(1f, 0.5f, 0.25f);
                light.intensity = 2f;
                light.bounceIntensity = 3f;
                light.useColorTemperature = true;
                light.colorTemperature = 3000f;
                var tint = Mathf.CorrelatedColorTemperatureToRGB(3000f);
                // With linear light intensity: the converted components times the linear tint,
                // the intensities as they are; the public indirect extraction carries no tint.
                WithLinearIntensity(true, () =>
                {
                    var point = new GI.PointLight();
                    GI.LightmapperUtils.Extract(light, ref point);
                    Assert.Equal(2f, point.color.intensity, 5);
                    Assert.Equal(6f, point.indirectColor.intensity, 5);
                    Assert.Equal(tint.r, point.color.red, 6);
                    Assert.Equal(Mathf.GammaToLinearSpace(0.5f) * tint.g, point.color.green, 6);
                    Assert.Equal(Mathf.GammaToLinearSpace(0.25f) * tint.b, point.indirectColor.blue, 6);
                    Assert.True(tint.b < 0.9f);
                    var untinted = GI.LightmapperUtils.ExtractIndirect(light);
                    Assert.Equal(Mathf.GammaToLinearSpace(0.25f), untinted.blue, 6);
                    Assert.Equal(6f, untinted.intensity, 5);
                });
                // Without it the temperature is not applied.
                WithLinearIntensity(false, () =>
                {
                    var point = new GI.PointLight();
                    GI.LightmapperUtils.Extract(light, ref point);
                    Assert.Equal(1f, point.color.red, 6);
                    Assert.Equal(Mathf.GammaToLinearSpace(1f) / Mathf.GammaToLinearSpace(2f), point.color.green, 6);
                });
                Assert.Throws<ArgumentOutOfRangeException>(() => { var c = GI.LinearColor.Black(); c.intensity = -1f; });
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Fact]
        public void RealtimeGITakesItsLightsFromAPipelinesDelegate()
        {
            var pointObject = new GameObject("GI Point");
            var spotObject = new GameObject("GI Spot");
            var skippedObject = new GameObject("GI Skipped");
            var original = GI.Lightmapping.GetDelegate();
            try
            {
                var point = pointObject.AddComponent<Light>();
                point.type = UnityEngine.LightType.Point;
                point.lightmapBakeType = LightmapBakeType.Realtime;
                point.color = new Color(1f, 0.5f, 0.25f);
                point.intensity = 2f;
                point.bounceIntensity = 3f;
                point.range = 7f;
                pointObject.transform.position = new Vector3(901f, 2f, 3f);
                var spot = spotObject.AddComponent<Light>();
                spot.type = UnityEngine.LightType.Spot;
                spot.lightmapBakeType = LightmapBakeType.Realtime;
                spot.spotAngle = 60f;
                spot.innerSpotAngle = 12f;
                spot.range = 9f;
                spotObject.transform.SetPositionAndRotation(new Vector3(902f, 2f, 3f), Quaternion.Euler(90f, 0f, 0f));
                var skipped = skippedObject.AddComponent<Light>();
                skipped.type = UnityEngine.LightType.Point;
                skipped.lightmapBakeType = LightmapBakeType.Realtime;
                skippedObject.transform.position = new Vector3(903f, 2f, 3f);

                GiLight? Find(float x)
                {
                    int count = RealtimeGI.CollectLights();
                    for (int i = 0; i < count; i++)
                        if (RealtimeGI.CollectedLight(i).PositionX == x) return RealtimeGI.CollectedLight(i);
                    return null;
                }

                // A pipeline's delegate, as the Universal Render Pipeline's: the inverse-squared
                // falloff, a spot light's own inner angle, and one light it does not give.
                GI.Lightmapping.SetDelegate((lights, converted) =>
                {
                    for (int i = 0; i < lights.Length; i++)
                    {
                        var data = new GI.LightDataGI();
                        if (lights[i] == skipped) data.InitNoBake(lights[i].GetInstanceID());
                        else if (lights[i].type == UnityEngine.LightType.Spot)
                        {
                            var s = new GI.SpotLight();
                            GI.LightmapperUtils.Extract(lights[i], ref s);
                            s.innerConeAngle = lights[i].innerSpotAngle * Mathf.Deg2Rad;
                            data.Init(ref s);
                        }
                        else
                        {
                            var p = new GI.PointLight();
                            GI.LightmapperUtils.Extract(lights[i], ref p);
                            data.Init(ref p);
                        }
                        data.falloff = GI.FalloffType.InverseSquared;
                        converted[i] = data;
                    }
                });
                WithLinearIntensity(true, () =>
                {
                    var p = Find(901f).Value;
                    Assert.Equal((int)UnityEngine.LightType.Point, p.Kind);
                    Assert.Equal((int)GI.FalloffType.InverseSquared, p.Falloff);
                    Assert.Equal(7f, p.Range);
                    // The indirect color: the linear color times intensity and bounce intensity.
                    Assert.Equal(6f, p.R, 4);
                    Assert.Equal(6f * Mathf.GammaToLinearSpace(0.5f), p.G, 4);
                    Assert.Equal(6f * Mathf.GammaToLinearSpace(0.25f), p.B, 4);
                    var s = Find(902f).Value;
                    Assert.Equal((int)UnityEngine.LightType.Spot, s.Kind);
                    Assert.Equal(60f, s.SpotAngle, 3);
                    Assert.Equal(12f, s.InnerSpotAngle, 3);
                    Assert.Equal((0f, -1f, 0f), (Mathf.Round(s.DirectionX * 1e4f) / 1e4f, Mathf.Round(s.DirectionY * 1e4f) / 1e4f, Mathf.Round(s.DirectionZ * 1e4f) / 1e4f));
                    Assert.Null(Find(903f));
                    // A light without indirect light adds nothing either.
                    point.bounceIntensity = 0f;
                    Assert.Null(Find(901f));
                    point.bounceIntensity = 3f;
                    // A conversion that throws leaves the frame without lights, not the update.
                    var converting = GI.Lightmapping.GetDelegate();
                    GI.Lightmapping.SetDelegate((_, _) => throw new InvalidOperationException("no lights"));
                    Assert.Equal(0, RealtimeGI.CollectLights());
                    GI.Lightmapping.SetDelegate(converting);
                    Assert.NotNull(Find(901f));
                });

                // Without a delegate of a pipeline's: the legacy falloff, the default inner cone
                // and the color scaled before its conversion.
                GI.Lightmapping.ResetDelegate();
                WithLinearIntensity(false, () =>
                {
                    var p = Find(901f).Value;
                    Assert.Equal((int)GI.FalloffType.Legacy, p.Falloff);
                    Assert.Equal(Mathf.GammaToLinearSpace(6f), p.R, 3);
                    var s = Find(902f).Value;
                    Assert.Equal(2f * Mathf.Atan(Mathf.Tan(30f * Mathf.Deg2Rad) * 0.71875f) * Mathf.Rad2Deg, s.InnerSpotAngle, 3);
                    Assert.NotNull(Find(903f));
                });
            }
            finally
            {
                GI.Lightmapping.SetDelegate(original);
                UnityEngine.Object.DestroyImmediate(pointObject);
                UnityEngine.Object.DestroyImmediate(spotObject);
                UnityEngine.Object.DestroyImmediate(skippedObject);
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
