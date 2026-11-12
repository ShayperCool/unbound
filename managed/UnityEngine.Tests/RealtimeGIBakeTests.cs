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
                    RealtimeGI.NoteBakeParts(file, new LightingSettings { bakedGI = true, realtimeGI = true });
                    RealtimeGI.NoteBakeParts(file, new LightingSettings { bakedGI = true, realtimeGI = true });
                });
                Assert.Single(logs, l => l.Contains("baked lightmaps"));
                Assert.Empty(Logged(() => RealtimeGI.NoteBakeParts(file, new LightingSettings { bakedGI = false, realtimeGI = true })));
                Assert.Single(Logged(() => RealtimeGI.NoteBakeParts(file, null)), l => l.Contains("baked lightmaps"));
                // A sky as the ambient source: its environment lighting is not made.
                RenderSettings.ambientMode = AmbientMode.Skybox;
                Assert.Contains(Logged(() => RealtimeGI.NoteBakeParts(file, new LightingSettings { bakedGI = false, realtimeGI = true })),
                    l => l.Contains("environment lighting"));
            }
            finally
            {
                RenderSettings.ambientMode = sky;
            }
        }
    }
}
