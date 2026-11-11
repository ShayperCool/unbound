using System;
using System.Collections.Generic;
using Unbound.Runtime;
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
            var probe = RenderSettings.s_EnvironmentProbe;
            var baked = RenderSettings.s_BakedProbe;
            var given = RenderSettings.s_CustomProbe;
            var serialized = RenderSettings.s_SerializedProbe;
            try
            {
                RenderSettings.skybox = null;
                RenderSettings.s_SerializedProbe = null;
                test();
            }
            finally
            {
                RenderSettings.s_EnvironmentProbe = probe;
                RenderSettings.s_BakedProbe = baked;
                RenderSettings.s_CustomProbe = given;
                RenderSettings.s_SerializedProbe = serialized;
                RenderSettings.skybox = skybox;
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
                // And another ambient source (documented; tests/projects/realtimegi
                // room-envdata-source): the gradient's colors over the data.
                DynamicGI.SetEnvironmentData(data);
                var ground = new Color(0.15f, 0.12f, 0.1f);
                RenderSettings.ambientGroundColor = ground;
                RealtimeGI.UpdateEnvironment();
                AssertCube(data);
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RealtimeGI.UpdateEnvironment();
                AssertFace(2, color);
                AssertFace(3, ground);
                // The ambient intensity scales nothing of a gradient, and data set there stands
                // when it changes (room-envdata-gradient-intensity).
                DynamicGI.SetEnvironmentData(data);
                RenderSettings.ambientIntensity = 3f;
                RealtimeGI.UpdateEnvironment();
                AssertCube(data);
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

        sealed class RenderObjectCounter : MonoBehaviour
        {
            internal static int Calls;
            void OnRenderObject() => Calls++;
        }

        [Fact]
        public void TheSkysCaptureSendsScriptsNothing()
        {
            // The engine's own draws of the sky (realtime GI's faces and the ambient probe's) are
            // not cameras of the game: no camera events, no OnRenderObject.
            var sky = new Material(Shader.Create("Skybox/Procedural", 1000));
            var owner = new GameObject("environment callbacks test");
            int events = 0;
            Camera.CameraCallback count = _ => events++;
            Camera.onPreCull += count;
            Camera.onPreRender += count;
            Camera.onPostRender += count;
            var active = RenderTexture.ActiveTarget;
            var saved = GfxStream.Current;
            try
            {
                owner.AddComponent<RenderObjectCounter>();
                // A control: a camera of the game does raise them.
                var camera = owner.AddComponent<Camera>();
                camera.enabled = false;
                var target = new RenderTexture(8, 8, 24);
                camera.targetTexture = target;
                RenderObjectCounter.Calls = 0;
                camera.Render();
                Assert.Equal(3, events);
                Assert.Equal(1, RenderObjectCounter.Calls);
                camera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(target);
                events = 0;
                RenderObjectCounter.Calls = 0;
                WithAmbient(() =>
                {
                    RenderSettings.skybox = sky;
                    RenderSettings.ambientMode = AmbientMode.Skybox;
                    // Both captures run: the faces are made and the probe is derived.
                    RealtimeGI.EnvironmentChanged();
                    int version = RealtimeGI.EnvironmentVersion;
                    RealtimeGI.UpdateEnvironment();
                    Assert.NotEqual(version, RealtimeGI.EnvironmentVersion);
                    RenderSettings.s_EnvironmentProbe = null;
                    DynamicGI.UpdateEnvironment();
                    Assert.True(RenderSettings.s_EnvironmentProbe.HasValue);
                });
                Assert.Equal(0, events);
                Assert.Equal(0, RenderObjectCounter.Calls);
            }
            finally
            {
                Camera.onPreCull -= count;
                Camera.onPreRender -= count;
                Camera.onPostRender -= count;
                UnityEngine.Object.DestroyImmediate(owner);
                UnityEngine.Object.DestroyImmediate(sky);
                RenderTexture.SetActiveQuietly(active);
                GfxStream.Restore(saved);
            }
        }

        sealed class CountingPipelineAsset : RenderPipelineAsset
        {
            protected override RenderPipeline CreatePipeline() => new CountingPipeline();
        }

        sealed class CountingPipeline : RenderPipeline
        {
            internal static int Renders;
            protected override void Render(ScriptableRenderContext context, Camera[] cameras) => Renders++;
            protected override void Render(ScriptableRenderContext context, List<Camera> cameras) => Renders++;
        }

        sealed class Warnings : ILogHandler
        {
            internal readonly List<string> Lines = new List<string>();
            public void LogException(Exception exception, UnityEngine.Object context) { }
            public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args)
            {
                if (logType == LogType.Warning) Lines.Add(string.Format(format, args));
            }
        }

        [Fact]
        public void ASkyboxUnderARenderPipelineAddsNoLightAndWarnsOnce()
        {
            // Unmeasured, and the capture is the built-in pipeline's: nothing is drawn through
            // the pipeline (its events would reach scripts); colors and a script's cube work.
            var sky = new Material(Shader.Create("Skybox/Procedural", 1000));
            var asset = ScriptableObject.CreateInstance<CountingPipelineAsset>();
            var pipeline = GraphicsSettings.defaultRenderPipeline;
            var log = Debug.unityLogger.logHandler;
            var warnings = new Warnings();
            int begun = 0;
            Action<ScriptableRenderContext, Camera> begin = (_, _) => begun++;
            RenderPipelineManager.beginCameraRendering += begin;
            try
            {
                GraphicsSettings.defaultRenderPipeline = asset;
                Debug.unityLogger.logHandler = warnings;
                RealtimeGI.s_Warned.Remove("environment-pipeline");
                CountingPipeline.Renders = 0;
                WithAmbient(() =>
                {
                    RenderSettings.ambientMode = AmbientMode.Flat;
                    var color = new Color(0.3f, 0.2f, 0.1f);
                    RenderSettings.ambientLight = color;
                    RealtimeGI.UpdateEnvironment();
                    AssertFace(4, color);
                    Assert.Empty(warnings.Lines);
                    RenderSettings.skybox = sky;
                    RenderSettings.ambientMode = AmbientMode.Skybox;
                    RealtimeGI.UpdateEnvironment();
                    for (int face = 0; face < 6; face++) AssertFace(face, Color.black);
                    RenderSettings.ambientIntensity = 2f;
                    RealtimeGI.UpdateEnvironment();
                    var line = Assert.Single(warnings.Lines);
                    Assert.Contains("scriptable render pipeline", line);
                    var data = Cube();
                    DynamicGI.SetEnvironmentData(data);
                    RealtimeGI.UpdateEnvironment();
                    AssertCube(data);
                });
                Assert.Equal(0, CountingPipeline.Renders);
                Assert.Equal(0, begun);
            }
            finally
            {
                RenderPipelineManager.beginCameraRendering -= begin;
                Debug.unityLogger.logHandler = log;
                GraphicsSettings.defaultRenderPipeline = pipeline;
                UnityEngine.Object.DestroyImmediate(asset);
                UnityEngine.Object.DestroyImmediate(sky);
            }
        }

        [Fact]
        public void TheSunsColorCountsWhereTheCaptureDrawsWithIt()
        {
            // The measured release draws the capture without the sun's color, so only its
            // direction makes the faces again; other releases draw with it.
            string release = ProjectInfo.UnityVersion;
            var sky = new Material(Shader.Create("Skybox/Procedural", 1000));
            var owner = new GameObject("environment sun test");
            var oldSun = RenderSettings.sun;
            var active = RenderTexture.ActiveTarget;
            var saved = GfxStream.Current;
            try
            {
                var sun = owner.AddComponent<Light>();
                sun.type = LightType.Directional;
                RenderSettings.sun = sun;
                WithAmbient(() =>
                {
                    RenderSettings.skybox = sky;
                    RenderSettings.ambientMode = AmbientMode.Skybox;
                    foreach (var (version, counts) in new[] { ("6000.0.71f1", false), ("6000.0.70f1", true) })
                    {
                        ProjectInfo.UnityVersion = version;
                        sun.color = Color.white;
                        RealtimeGI.UpdateEnvironment();
                        int made = RealtimeGI.EnvironmentVersion;
                        sun.color = new Color(1f, 0.5f, 0.25f);
                        RealtimeGI.UpdateEnvironment();
                        Assert.Equal(counts, made != RealtimeGI.EnvironmentVersion);
                        // The direction counts on both.
                        made = RealtimeGI.EnvironmentVersion;
                        owner.transform.rotation = Quaternion.Euler(version == release ? 30f : 50f, 20f, 0f) * owner.transform.rotation;
                        RealtimeGI.UpdateEnvironment();
                        Assert.NotEqual(made, RealtimeGI.EnvironmentVersion);
                    }
                });
            }
            finally
            {
                ProjectInfo.UnityVersion = release;
                RenderSettings.sun = oldSun;
                UnityEngine.Object.DestroyImmediate(owner);
                UnityEngine.Object.DestroyImmediate(sky);
                RenderTexture.SetActiveQuietly(active);
                GfxStream.Restore(saved);
            }
        }

        [Fact]
        public void TheSkyboxProbeIsScaledByTheIntensityOfTheMoment()
        {
            // Measured (tests/projects/realtimegi, a linear project): after
            // DynamicGI.UpdateEnvironment at intensity 2, RenderSettings.ambientProbe at 0.5 and 1
            // without another call is the sky's probe times GammaToLinearSpace(intensity). A
            // gamma project scales the lighting data's probe the same way (measured), and so
            // the sky's.
            var mode = RenderSettings.ambientMode;
            float intensity = RenderSettings.ambientIntensity;
            var probe = RenderSettings.s_EnvironmentProbe;
            var given = RenderSettings.s_CustomProbe;
            var skybox = RenderSettings.skybox;
            var colorSpace = QualitySettings.s_ColorSpace;
            var sky = new Material(Shader.Create("Skybox/Procedural", 1000));
            try
            {
                var sh = new SphericalHarmonicsL2();
                sh[0, 0] = 0.0979f; sh[1, 1] = 0.0366f; sh[2, 4] = -0.02f;
                RenderSettings.s_EnvironmentProbe = sh;
                RenderSettings.s_CustomProbe = null;
                // The intensity scales a skybox's light: the scene has a skybox material.
                RenderSettings.skybox = sky;
                RenderSettings.ambientMode = AmbientMode.Skybox;
                foreach (var space in new[] { ColorSpace.Linear, ColorSpace.Gamma })
                {
                    QualitySettings.s_ColorSpace = space;
                    foreach (var (value, scale) in new[] { (0.5f, 0.21404114f), (1f, 1f), (2f, 4.5947934f) })
                    {
                        RenderSettings.ambientIntensity = value;
                        float expected = scale;
                        var read = RenderSettings.ambientProbe;
                        Assert.Equal(0.0979f * expected, read[0, 0], 6);
                        Assert.Equal(0.0366f * expected, read[1, 1], 6);
                        Assert.Equal(-0.02f * expected, read[2, 4], 6);
                    }
                }
            }
            finally
            {
                QualitySettings.s_ColorSpace = colorSpace;
                RenderSettings.s_EnvironmentProbe = probe;
                RenderSettings.s_CustomProbe = given;
                RenderSettings.skybox = skybox;
                RenderSettings.ambientIntensity = intensity;
                RenderSettings.ambientMode = mode;
                UnityEngine.Object.DestroyImmediate(sky);
            }
        }

        [Fact]
        public void AProbeGivenStandsWhenAnotherScenesLightingDataIsRead()
        {
            // A scene loaded beside the active one reads its lighting data without its
            // RenderSettings taking effect: the probe a script gave is still the stored one.
            var reflection = RenderSettings.s_BakedReflection;
            WithAmbient(() =>
            {
                try
                {
                    RenderSettings.ambientMode = AmbientMode.Custom;
                    var given = new SphericalHarmonicsL2();
                    given[0, 0] = 0.25f; given[1, 3] = -0.5f;
                    RenderSettings.ambientProbe = given;
                    RenderSettings.ReadLightingData(default, null, firstScene: false);
                    AssertProbe(given, 1f, "after another scene's lighting data");
                }
                finally
                {
                    RenderSettings.s_BakedReflection = reflection;
                }
            });
        }

        static void AssertProbe(SphericalHarmonicsL2 expected, float scale, string what)
        {
            var read = RenderSettings.ambientProbe;
            for (int c = 0; c < 3; c++)
                for (int k = 0; k < 9; k++)
                    Assert.True(Mathf.Abs(expected[c, k] * scale - read[c, k]) < 2e-6f * Mathf.Max(1f, scale), $"{what}: [{c},{k}] {read[c, k]} != {expected[c, k] * scale}");
        }

        static SphericalHarmonicsL2 Flat(Color gamma)
        {
            var sh = new SphericalHarmonicsL2();
            sh.AddAmbientLight(gamma.linear);
            return sh;
        }

        [Fact]
        public void TheAmbientProbeFollowsTheModeAndOneStoredProbe()
        {
            // The Unity 6000.0.71f1 Editor's answers, tests/projects/realtimegi end-* (a linear
            // project; a scene whose lighting data was made without a sky: its probe is zero).
            var colorSpace = QualitySettings.s_ColorSpace;
            var sky = new Material(Shader.Create("Skybox/Procedural", 1000));
            WithAmbient(() =>
            {
                try
                {
                    QualitySettings.s_ColorSpace = ColorSpace.Linear;
                    var color = new Color(0.1f, 0.12f, 0.2f);
                    RenderSettings.s_BakedProbe = new SphericalHarmonicsL2();
                    RenderSettings.s_EnvironmentProbe = null;
                    RenderSettings.s_CustomProbe = null;
                    RenderSettings.ambientLight = color;
                    RenderSettings.ambientIntensity = 1f;
                    // The custom mode: the stored probe, here the lighting data's.
                    RenderSettings.ambientMode = AmbientMode.Custom;
                    AssertProbe(new SphericalHarmonicsL2(), 1f, "custom, as loaded");
                    var given = new SphericalHarmonicsL2();
                    given.AddAmbientLight(new Color(0.3f, 0.2f, 0.1f));
                    given.AddDirectionalLight(Vector3.up, new Color(0.1f, 0.4f, 0.2f), 1f);
                    RenderSettings.ambientProbe = given;
                    AssertProbe(given, 1f, "custom, given");
                    Assert.Equal(0.347058833f, RenderSettings.ambientProbe[0, 0], 6);
                    // The intensity does not scale it, and it stands through another mode.
                    RenderSettings.ambientIntensity = 2f;
                    AssertProbe(given, 1f, "custom, intensity 2");
                    RenderSettings.ambientMode = AmbientMode.Flat;
                    AssertProbe(Flat(color), 1f, "flat");
                    Assert.Equal(0.0100228256f, RenderSettings.ambientProbe[0, 0], 7);
                    RenderSettings.ambientMode = AmbientMode.Custom;
                    AssertProbe(given, 1f, "custom again");
                    // The skybox mode without a skybox material: the ambient color, unscaled;
                    // a skybox assigned alone does not make the probe again.
                    RenderSettings.ambientMode = AmbientMode.Skybox;
                    AssertProbe(Flat(color), 1f, "skybox mode without a skybox");
                    RenderSettings.skybox = sky;
                    AssertProbe(Flat(color), 1f, "a skybox assigned alone");
                    // With the material, the stored probe times the intensity as a gamma value.
                    RenderSettings.ambientIntensity = 1.5f;
                    AssertProbe(given, Mathf.Pow(1.5f, 2.2f), "skybox, intensity 1.5");
                    Assert.Equal(0.8468f, RenderSettings.ambientProbe[0, 0], 3);
                    var other = new SphericalHarmonicsL2();
                    other.AddAmbientLight(new Color(0.05f, 0.1f, 0.4f));
                    other.AddDirectionalLight(Vector3.right, new Color(0.5f, 0.1f, 0.1f), 1f);
                    RenderSettings.ambientProbe = other;
                    AssertProbe(other, Mathf.Pow(1.5f, 2.2f), "skybox, another probe given");
                    RenderSettings.ambientIntensity = 0.5f;
                    AssertProbe(other, Mathf.GammaToLinearSpace(0.5f), "skybox, intensity 0.5");
                    Assert.Equal(0.0611f, RenderSettings.ambientProbe[0, 0], 3);
                    // The flat mode gives its color whatever probe is given, and keeps the probe.
                    RenderSettings.ambientMode = AmbientMode.Flat;
                    RenderSettings.ambientProbe = given;
                    AssertProbe(Flat(color), 1f, "flat, a probe given");
                    var next = new Color(0.4f, 0.3f, 0.6f);
                    RenderSettings.ambientLight = next;
                    AssertProbe(Flat(next), 1f, "flat, another color");
                    RenderSettings.skybox = null;
                    RenderSettings.ambientMode = AmbientMode.Skybox;
                    AssertProbe(Flat(next), 1f, "skybox mode without a skybox, intensity 0.5");
                    RenderSettings.ambientMode = AmbientMode.Custom;
                    AssertProbe(given, 1f, "custom, the probe given in the flat mode");

                    // The sky's probe DynamicGI.UpdateEnvironment derives is the stored one from
                    // then on (room-ambient-custom: the custom mode gives it at intensity 3), and
                    // a skybox removed after the mode was set leaves the probe as it was made
                    // (room-sky-none).
                    var derived = new SphericalHarmonicsL2();
                    derived[0, 0] = 0.0979f; derived[1, 1] = 0.0366f; derived[2, 4] = -0.02f;
                    RenderSettings.SetEnvironmentProbe(derived);
                    RenderSettings.ambientIntensity = 3f;
                    AssertProbe(derived, 1f, "custom, the sky's probe");
                    RenderSettings.skybox = sky;
                    RenderSettings.ambientMode = AmbientMode.Skybox;
                    RenderSettings.skybox = null;
                    AssertProbe(derived, Mathf.Pow(3f, 2.2f), "skybox removed after the mode was set");
                    // The next change of an ambient input finds no skybox.
                    RenderSettings.ambientIntensity = 2f;
                    AssertProbe(Flat(next), 1f, "then an ambient input changed");

                    // A gradient does not read the stored probe either, nor one given in its
                    // mode, which the custom mode then returns (end-gradient*).
                    RenderSettings.ambientMode = AmbientMode.Trilight;
                    var gradient = RenderSettings.ambientProbe;
                    Assert.NotEqual(0f, gradient[0, 1]);
                    RenderSettings.ambientProbe = other;
                    AssertProbe(gradient, 1f, "gradient, a probe given");
                    RenderSettings.ambientMode = AmbientMode.Custom;
                    AssertProbe(other, 1f, "custom, the probe given in the gradient mode");

                    // A scene without lighting data has no stored probe: zero in the custom mode
                    // and in the skybox mode with a skybox material (nodata*-ambient, -custom).
                    RenderSettings.s_BakedProbe = null;
                    RenderSettings.s_EnvironmentProbe = null;
                    RenderSettings.s_CustomProbe = null;
                    AssertProbe(new SphericalHarmonicsL2(), 1f, "custom, no lighting data");
                    RenderSettings.skybox = sky;
                    RenderSettings.ambientMode = AmbientMode.Skybox;
                    AssertProbe(new SphericalHarmonicsL2(), 1f, "skybox, no lighting data");
                }
                finally
                {
                    QualitySettings.s_ColorSpace = colorSpace;
                    UnityEngine.Object.DestroyImmediate(sky);
                }
            });
        }
    }
}
