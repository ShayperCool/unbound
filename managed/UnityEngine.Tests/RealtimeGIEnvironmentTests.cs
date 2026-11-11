using System;
using UnityEngine;
using UnityEngine.Rendering;
using Xunit;

namespace Unbound.Tests
{
    /// <summary>The environment precomputed realtime GI reads, as the Unity 6000.0.71f1 Editor
    /// makes it (tests/projects/realtimegi: room-env*, room-gradient*, room-sky-none,
    /// room-ambient-custom, room-envdata*). The skybox's own faces need the renderer: the probe
    /// covers them.</summary>
    [Collection("RenderSettings state")]
    public class RealtimeGIEnvironmentTests
    {
        const int Face = 8 * 8 * 3;

        static void WithAmbient(Action test)
        {
            var mode = RenderSettings.ambientMode;
            var sky = RenderSettings.ambientSkyColor;
            var equator = RenderSettings.ambientEquatorColor;
            var ground = RenderSettings.ambientGroundColor;
            float intensity = RenderSettings.ambientIntensity;
            var skybox = RenderSettings.skybox;
            try
            {
                RenderSettings.skybox = null;
                test();
            }
            finally
            {
                RenderSettings.ambientMode = mode;
                RenderSettings.ambientSkyColor = sky;
                RenderSettings.ambientEquatorColor = equator;
                RenderSettings.ambientGroundColor = ground;
                RenderSettings.ambientIntensity = intensity;
                RenderSettings.skybox = skybox;
                RealtimeGI.EnvironmentChanged();
            }
        }

        static void AssertFace(int face, Color gamma)
        {
            var linear = gamma.linear;
            var faces = RealtimeGI.EnvironmentFaces;
            for (int i = face * Face; i < (face + 1) * Face; i += 3)
            {
                Assert.Equal(linear.r, faces[i]);
                Assert.Equal(linear.g, faces[i + 1]);
                Assert.Equal(linear.b, faces[i + 2]);
            }
        }

        static float[] Cube()
        {
            var data = new float[8 * 8 * 6 * 4];
            for (int i = 0; i < data.Length; i++) data[i] = i * 0.001f;
            return data;
        }

        static void AssertCube(float[] data)
        {
            var faces = RealtimeGI.EnvironmentFaces;
            for (int texel = 0; texel < 6 * 64; texel++)
                for (int c = 0; c < 3; c++)
                    Assert.Equal(data[4 * texel + c], faces[3 * texel + c]);
        }

        [Fact]
        public void AnAmbientColorFillsTheCubeWhateverTheIntensity()
        {
            WithAmbient(() =>
            {
                var color = new Color(0.6f, 0.5f, 0.2f);
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = color;
                RenderSettings.ambientIntensity = 1f;
                RealtimeGI.UpdateEnvironment();
                for (int face = 0; face < 6; face++) AssertFace(face, color);
                // The intensity scales a skybox's light only: nothing is made again.
                int version = RealtimeGI.EnvironmentVersion;
                RenderSettings.ambientIntensity = 2f;
                RealtimeGI.UpdateEnvironment();
                Assert.Equal(version, RealtimeGI.EnvironmentVersion);
                // The color of a custom ambient mode, and of a skybox mode without a skybox.
                var other = new Color(0.2f, 0.7f, 0.3f);
                RenderSettings.ambientLight = other;
                foreach (var mode in new[] { AmbientMode.Custom, AmbientMode.Skybox })
                {
                    RenderSettings.ambientMode = mode;
                    RealtimeGI.UpdateEnvironment();
                    for (int face = 0; face < 6; face++) AssertFace(face, other);
                }
            });
        }

        [Fact]
        public void AGradientGivesItsColorsToTheFaces()
        {
            WithAmbient(() =>
            {
                Color sky = new Color(0.5f, 0.6f, 0.9f), equator = new Color(0.4f, 0.35f, 0.3f), ground = new Color(0.15f, 0.12f, 0.1f);
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = sky;
                RenderSettings.ambientEquatorColor = equator;
                RenderSettings.ambientGroundColor = ground;
                RenderSettings.ambientIntensity = 2f;
                RealtimeGI.UpdateEnvironment();
                // +X, -X, +Y, -Y, +Z, -Z.
                var expected = new[] { equator, equator, sky, ground, equator, equator };
                for (int face = 0; face < 6; face++) AssertFace(face, expected[face]);
                // One color changed: made again.
                ground = new Color(0.3f, 0.1f, 0.1f);
                RenderSettings.ambientGroundColor = ground;
                RealtimeGI.UpdateEnvironment();
                AssertFace(3, ground);
                AssertFace(2, sky);
            });
        }

        [Fact]
        public void SetEnvironmentDataStandsUntilTheAmbientSourceChanges()
        {
            WithAmbient(() =>
            {
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.1f, 0.2f, 0.3f);
                RealtimeGI.UpdateEnvironment();
                var data = Cube();
                int version = RealtimeGI.EnvironmentVersion;
                DynamicGI.SetEnvironmentData(data);
                Assert.NotEqual(version, RealtimeGI.EnvironmentVersion);
                AssertCube(data);
                // Later updates keep it, without making anything again.
                version = RealtimeGI.EnvironmentVersion;
                RealtimeGI.UpdateEnvironment();
                RealtimeGI.UpdateEnvironment();
                Assert.Equal(version, RealtimeGI.EnvironmentVersion);
                AssertCube(data);
                // A change of the ambient source replaces it.
                var color = new Color(0.3f, 0.2f, 0.1f);
                RenderSettings.ambientLight = color;
                RealtimeGI.UpdateEnvironment();
                AssertFace(0, color);
                // So does DynamicGI.UpdateEnvironment, with nothing changed.
                DynamicGI.SetEnvironmentData(data);
                AssertCube(data);
                DynamicGI.UpdateEnvironment();
                RealtimeGI.UpdateEnvironment();
                AssertFace(5, color);
            });
        }

        [Fact]
        public void SetEnvironmentDataRefusesOtherArrays()
        {
            WithAmbient(() =>
            {
                RenderSettings.ambientMode = AmbientMode.Flat;
                var color = new Color(0.1f, 0.2f, 0.3f);
                RenderSettings.ambientLight = color;
                RealtimeGI.UpdateEnvironment();
                int version = RealtimeGI.EnvironmentVersion;
                var wrong = Assert.Throws<ArgumentException>(() => DynamicGI.SetEnvironmentData(new float[100]));
                Assert.Equal("Array size is wrong. It must have 1536 elements. The resolution of each face is 8x8 texels.", wrong.Message);
                Assert.Equal("input", Assert.Throws<ArgumentNullException>(() => DynamicGI.SetEnvironmentData(null)).ParamName);
                Assert.Throws<ArgumentException>(() => DynamicGI.SetEnvironmentData(new float[8 * 8 * 6 * 3]));
                // Nothing changed.
                Assert.Equal(version, RealtimeGI.EnvironmentVersion);
                AssertFace(0, color);
            });
        }
    }
}
