using Unbound.Runtime;
using UnityEngine;
using Xunit;

namespace Unbound.Tests
{
    [Collection("RenderSettings state")]
    public class EnvironmentSunTests
    {
        /// <summary>On the measured release the sky's ambient projection draws any sky material
        /// with <c>_LightColor0</c> zero (Compat.SkyAmbientWithoutLightColor); other releases
        /// project the sky as drawn.</summary>
        [Fact]
        public void AmbientCaptureOmitsTheMainLightColourOnTheMeasuredRelease()
        {
            string version = ProjectInfo.UnityVersion;
            ProjectInfo.UnityVersion = "6000.0.71f1";
            var owner = new GameObject("environment test camera");
            var sunOwner = new GameObject("environment test sun");
            var oldSun = RenderSettings.sun;
            var sky = new Material(Shader.Create("Skybox/Procedural", 1000));
            var other = new Material(Shader.Create("Skybox/Cubemap", 1000));
            try
            {
                var camera = owner.AddComponent<Camera>();
                var sun = sunOwner.AddComponent<Light>();
                sun.type = LightType.Directional;
                RenderSettings.sun = sun;
                sky.EnableKeyword("_SUNDISK_HIGH_QUALITY");
                sky.SetFloat("_SunSize", 0.04f);
                Assert.Null(FrameBuilder.SkyboxLightProperties(camera, sky));
                camera.m_EnvironmentCapture = true;
                var properties = FrameBuilder.SkyboxLightProperties(camera, sky);
                Assert.Single(properties);
                Assert.Equal(Vector4.zero, properties[Shader.PropertyToID("_LightColor0")]);
                // Keyed on the projection, not on the shader: a cubemap sky gets it too.
                properties = FrameBuilder.SkyboxLightProperties(camera, other);
                Assert.Equal(Vector4.zero, Assert.Single(properties).Value);
                // Unmeasured release: the sky is projected as drawn.
                ProjectInfo.UnityVersion = "6000.0.70f1";
                Assert.Null(FrameBuilder.SkyboxLightProperties(camera, sky));
                ProjectInfo.UnityVersion = "6000.0.71f1";
                Assert.True(sky.IsKeywordEnabled("_SUNDISK_HIGH_QUALITY"));
                Assert.Equal(0.04f, sky.GetFloat("_SunSize"));
                Assert.Same(sun, RenderSettings.sun);
                // The no-light direction fallback must survive the disk override.
                RenderSettings.sun = null;
                sun.enabled = false;
                properties = FrameBuilder.SkyboxLightProperties(camera, sky);
                Assert.Equal(new Vector4(0f, 0f, -1f, 0f), properties[Shader.PropertyToID("_WorldSpaceLightPos0")]);
            }
            finally
            {
                ProjectInfo.UnityVersion = version;
                RenderSettings.sun = oldSun;
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(sunOwner);
                Object.DestroyImmediate(sky);
                Object.DestroyImmediate(other);
            }
        }

        static readonly LightBakingOutput FullyBaked = new LightBakingOutput { isBaked = true, lightmapBakeType = LightmapBakeType.Baked };

        /// <summary>A fully baked sun lights nothing at run time yet the sky is drawn with it
        /// (tests/projects/lightingdatanan); a realtime directional light beside it stays the sky's
        /// sun, for the sky draw and the environment's key alike.</summary>
        [Fact]
        public void SkyTakesAFullyBakedSunOnlyWithoutARealtimeOne()
        {
            var owner = new GameObject("sky sun test camera");
            var bakedOwner = new GameObject("sky sun test baked");
            var realtimeOwner = new GameObject("sky sun test realtime");
            var oldSun = RenderSettings.sun;
            var sky = new Material(Shader.Create("Skybox/Procedural", 1000));
            try
            {
                var camera = owner.AddComponent<Camera>();
                var baked = bakedOwner.AddComponent<Light>();
                baked.type = LightType.Directional;
                baked.intensity = 2f;
                baked.bakingOutput = FullyBaked;
                bakedOwner.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                RenderSettings.sun = baked;

                Assert.Null(LightingGlobals.MainLightOf());
                Assert.Same(baked, LightingGlobals.SkySun());
                var properties = FrameBuilder.SkyboxLightProperties(camera, sky);
                var toSun = -bakedOwner.transform.forward;
                Assert.Equal(new Vector4(toSun.x, toSun.y, toSun.z, 0f), properties[Shader.PropertyToID("_WorldSpaceLightPos0")]);
                Assert.Equal((Vector4)baked.FinalColor(QualitySettings.activeColorSpace == ColorSpace.Linear), properties[Shader.PropertyToID("_LightColor0")]);

                // A dimmer realtime directional light is the main light and so the sky's sun.
                var realtime = realtimeOwner.AddComponent<Light>();
                realtime.type = LightType.Directional;
                realtime.intensity = 0.5f;
                Assert.Same(realtime, LightingGlobals.MainLightOf());
                Assert.Same(realtime, LightingGlobals.SkySun());
                Assert.Null(FrameBuilder.SkyboxLightProperties(camera, sky));
            }
            finally
            {
                RenderSettings.sun = oldSun;
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(bakedOwner);
                Object.DestroyImmediate(realtimeOwner);
                Object.DestroyImmediate(sky);
            }
        }
    }
}
