using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.Rendering;
using Xunit;

namespace Unbound.Tests
{
    /// <summary>What Unbound's own realtime GI bake of a scene the Editor opens without lighting
    /// data takes and says (docs/REALTIME_GI.md, When the engine uses it).</summary>
    [Collection("RenderSettings state")]
    public class RealtimeGIBakeTests
    {
        sealed class Capture : ILogHandler
        {
            readonly List<string> m_Logs;
            internal Capture(List<string> logs) => m_Logs = logs;
            public void LogFormat(LogType type, UnityEngine.Object context, string format, params object[] args) => m_Logs.Add(string.Format(format, args));
            public void LogException(Exception exception, UnityEngine.Object context) => m_Logs.Add(exception.Message);
        }

        static List<string> Logged(Action action)
        {
            var logs = new List<string>();
            var previous = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = new Capture(logs);
            RealtimeGI.s_Warned.Clear();
            try { action(); }
            finally
            {
                Debug.unityLogger.logHandler = previous;
                RealtimeGI.s_Warned.Clear();
            }
            return logs;
        }

        [Fact]
        public void TheBakeTakesDefaultMediumsRealtimeFieldsAndRefusesOtherValues()
        {
            var d = RealtimeGI.BakeParameters.DefaultMedium;
            Assert.Null(d.Refusal());
            // What the bake takes changes the group (other systems), not the refusal.
            var tagged = d;
            tagged.SystemTag = 1;
            Assert.Null(tagged.Refusal());
            Assert.NotEqual(d.Group(), tagged.Group());
            var tolerance = d;
            tolerance.ModellingTolerance = 0.001f;
            Assert.Null(tolerance.Refusal());
            Assert.NotEqual(d.Group(), tolerance.Group());
            // The fields it does not measure refuse the renderer.
            var resolution = d;
            resolution.Resolution = 2f;
            Assert.Contains("Resolution", resolution.Refusal());
            var transparent = d;
            transparent.IsTransparent = true;
            Assert.Contains("Transparent", transparent.Refusal());
            var stitching = d;
            stitching.EdgeStitching = false;
            Assert.Contains("Edge Stitching", stitching.Refusal());
            var budget = d;
            budget.IrradianceBudget = 0;
            Assert.NotNull(budget.Refusal());
        }

        [Fact]
        public void WhatTheEditorsBakeAlsoMakesIsSaidOnce()
        {
            var file = new LoadedFile { Path = "Assets/Baked.unity" };
            var sky = RenderSettings.ambientMode;
            try
            {
                RenderSettings.ambientMode = AmbientMode.Flat;
                // Baked global illumination on (also a scene without Lighting Settings, whose
                // defaults have it): the baked lightmaps and probes are not made.
                var logs = Logged(() =>
                {
                    RealtimeGI.NoteBakeParts(file, new LightingSettings { bakedGI = true, realtimeGI = true }, RealtimeGI.BakeOnLoad);
                    RealtimeGI.NoteBakeParts(file, new LightingSettings { bakedGI = true, realtimeGI = true }, RealtimeGI.BakeOnLoad);
                });
                Assert.Single(logs, l => l.Contains("baked lightmaps"));
                Assert.Empty(Logged(() => RealtimeGI.NoteBakeParts(file, new LightingSettings { bakedGI = false, realtimeGI = true }, RealtimeGI.BakeOnLoad)));
                Assert.Single(Logged(() => RealtimeGI.NoteBakeParts(file, null, RealtimeGI.BakeOnLoad)), l => l.Contains("baked lightmaps"));
                // A sky as the ambient source: its environment lighting is not made.
                RenderSettings.ambientMode = AmbientMode.Skybox;
                Assert.Contains(Logged(() => RealtimeGI.NoteBakeParts(file, new LightingSettings { bakedGI = false, realtimeGI = true }, RealtimeGI.BakeOnLoad)),
                    l => l.Contains("environment lighting"));
                // A script's bake makes it (tests/projects/bakeenvironment): nothing is said.
                Assert.DoesNotContain(Logged(() => RealtimeGI.NoteBakeParts(file, new LightingSettings { bakedGI = false, realtimeGI = true }, RealtimeGI.ScriptBake)),
                    l => l.Contains("environment lighting"));
            }
            finally
            {
                RenderSettings.ambientMode = sky;
            }
        }

        [Fact]
        public void ABakeMakesTheEnvironmentPartsItsPipelineBakesAndKeepsCustomSources()
        {
            const RealtimeGI.EnvironmentPart made = RealtimeGI.EnvironmentPart.Made, cleared = RealtimeGI.EnvironmentPart.Cleared, kept = RealtimeGI.EnvironmentPart.Kept;
            var both = new SupportedRenderingFeatures { ambientProbeBaking = true, defaultReflectionProbeBaking = true };
            Assert.Equal((made, made), RealtimeGI.BakedParts(both, AmbientMode.Skybox, DefaultReflectionMode.Skybox));
            Assert.Equal((made, made), RealtimeGI.BakedParts(both, AmbientMode.Flat, DefaultReflectionMode.Skybox));
            // A pipeline that bakes neither, or one of them: the others are taken away
            // (tests/projects/bakeenvironment EnvNoBaking).
            var none = new SupportedRenderingFeatures { ambientProbeBaking = false, defaultReflectionProbeBaking = false };
            Assert.Equal((cleared, cleared), RealtimeGI.BakedParts(none, AmbientMode.Skybox, DefaultReflectionMode.Skybox));
            var reflectionOnly = new SupportedRenderingFeatures { ambientProbeBaking = false, defaultReflectionProbeBaking = true };
            Assert.Equal((cleared, made), RealtimeGI.BakedParts(reflectionOnly, AmbientMode.Skybox, DefaultReflectionMode.Skybox));
            // Custom sources keep what the scene had (not measured).
            Assert.Equal((kept, made), RealtimeGI.BakedParts(both, AmbientMode.Custom, DefaultReflectionMode.Skybox));
            Assert.Equal((made, kept), RealtimeGI.BakedParts(both, AmbientMode.Skybox, DefaultReflectionMode.Custom));
            Assert.Equal((cleared, kept), RealtimeGI.BakedParts(reflectionOnly, AmbientMode.Custom, DefaultReflectionMode.Custom));
        }

        [Fact]
        public void AScriptBakedSceneLoadedAgainHasTheEnvironmentTheBakeMade()
        {
            const string path = "Assets/EnvironmentBaked.unity";
            var probe = new SphericalHarmonicsL2();
            probe.AddAmbientLight(new Color(0.25f, 0.5f, 0.75f));
            var cube = new Cubemap(4, TextureFormat.RGBAHalf, false) { name = "ReflectionProbe-0" };
            var mode = RenderSettings.ambientMode;
            var (probeBefore, reflectionBefore) = RenderSettings.BakedEnvironment(null);
            try
            {
                RealtimeGI.RememberEnvironment(path, probe, cube);
                // The scene loads again without lighting data of its own.
                RenderSettings.SetBakedEnvironment(null, null, null);
                RealtimeGI.ReapplyEnvironment("Assets/Other.unity", null);
                Assert.NotSame(cube, ReflectionProbe.defaultTexture);
                RealtimeGI.ReapplyEnvironment(path, null);
                Assert.Same(cube, ReflectionProbe.defaultTexture);
                RenderSettings.ambientMode = AmbientMode.Custom;
                Assert.Equal(probe, RenderSettings.ambientProbe);
            }
            finally
            {
                RenderSettings.ambientMode = mode;
                RenderSettings.SetBakedEnvironment(null, probeBefore, reflectionBefore);
                RealtimeGI.ForgetEnvironment(path);
                UnityEngine.Object.DestroyImmediate(cube);
            }
        }

        [Fact]
        public void ABakeOfAdaptiveProbeVolumesIsSaidOnceWhereBakedLightingIsOn()
        {
            var made = new GameObject("Volumes");
            try
            {
                var file = new LoadedFile { Path = "Assets/Volumes.unity" };
                file.Objects[1] = made.AddComponent<UnityEngine.Rendering.ProbeVolume>();
                RealtimeGI.s_Warned.Clear();
                // Without baked lighting nothing of them is baked.
                Assert.DoesNotContain(Logged(() => RealtimeGI.NoteBakeParts(file, new LightingSettings { bakedGI = false }, RealtimeGI.ScriptBake)),
                    l => l.Contains("Adaptive Probe Volumes"));
                // With it: said, once for two bakes.
                var settings = new LightingSettings { bakedGI = true };
                Assert.Single(Logged(() =>
                {
                    RealtimeGI.NoteBakeParts(file, settings, RealtimeGI.ScriptBake);
                    RealtimeGI.NoteBakeParts(file, settings, RealtimeGI.ScriptBake);
                }), l => l.Contains("Adaptive Probe Volumes"));
            }
            finally
            {
                RealtimeGI.s_Warned.Clear();
                UnityEngine.Object.DestroyImmediate(made);
            }
        }
    }
}

namespace UnityEngine.Rendering
{
    /// <summary>Stands for the render pipeline Core package's Adaptive Probe Volume component.</summary>
    public class ProbeVolume : MonoBehaviour { }
}
