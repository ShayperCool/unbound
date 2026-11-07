using Unbound.Runtime;
using UnityEngine;
using Xunit;

namespace Unbound.Tests
{
    [Collection("RenderSettings state")]
    public class EnvironmentSunTests
    {
        [Fact]
        public void AmbientCaptureOmitsOnlyTheProceduralDiskRadiance()
        {
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
                Assert.Null(FrameBuilder.SkyboxLightProperties(camera, other));
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
                RenderSettings.sun = oldSun;
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(sunOwner);
                Object.DestroyImmediate(sky);
                Object.DestroyImmediate(other);
            }
        }
    }
}
