using System.Collections.Generic;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>What a bake's lightmap layout takes from the scene's files: a renderer's Receive
    /// Global Illumination and the Lighting Settings' Fixed Lightmap Size; and a scene it cannot lay
    /// out.</summary>
    public class BakedAtlasInputTests
    {
        sealed class Capture : ILogHandler
        {
            readonly ILogHandler m_Inner;
            internal readonly List<string> Logs = new List<string>();

            internal Capture(ILogHandler inner) => m_Inner = inner;

            public void LogFormat(LogType type, Object context, string format, params object[] args)
            {
                Logs.Add(type + ": " + (args == null || args.Length == 0 ? format
                    : string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args)));
                m_Inner.LogFormat(type, context, format, args);
            }

            public void LogException(System.Exception exception, Object context) => m_Inner.LogException(exception, context);
        }

        [Fact]
        public void ALightmappedTerrainLeavesTheSceneWithoutALayout()
        {
            var logger = Debug.unityLogger;
            var previous = logger.logHandler;
            var capture = new Capture(previous);
            var go = new GameObject("ground");
            try
            {
                logger.logHandler = capture;
                go.m_StaticEditorFlags = 1;
                var file = new LoadedFile { Path = "Assets/TerrainOnly.unity", Guid = "10000000000000000000000000000000" };
                file.Objects[1] = go.AddComponent<Terrain>();
                BakedAtlas.Lay(file, new LightingSettings());
                Assert.Contains(capture.Logs, l => l.StartsWith("Warning: ") && l.Contains("'ground' is a lightmapped Terrain"));
                // It stops there: no layout is asked for.
                Assert.DoesNotContain(capture.Logs, l => l.Contains("layout of its baked lightmaps"));
            }
            finally
            {
                logger.logHandler = previous;
                Object.DestroyImmediate(go);
            }
        }

        [Fact]
        public void AMeshRendererReadsAndCopiesReceiveGlobalIllumination()
        {
            var go = new GameObject("lit by probes");
            var other = new GameObject("copy");
            try
            {
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.ReadSerialized(YNode.MakeMap(("m_ReceiveGI", YNode.MakeScalar("2"))), null);
                Assert.Equal(ReceiveGI.LightProbes, renderer.receiveGI);
                var copy = other.AddComponent<MeshRenderer>();
                copy.CopyFrom(renderer, o => o);
                Assert.Equal(ReceiveGI.LightProbes, copy.receiveGI);
                renderer.ReadSerialized(YNode.MakeMap(), null);
                Assert.Equal(ReceiveGI.Lightmaps, renderer.receiveGI);
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(other);
            }
        }

        [Fact]
        public void LightingSettingsReadFixedLightmapSize()
        {
            var settings = new LightingSettings();
            Assert.False(settings.LightmapSizeFixed);
            settings.ReadSerialized(YNode.MakeMap(("m_LightmapSizeFixed", YNode.MakeScalar("1"))), null);
            Assert.True(settings.LightmapSizeFixed);
        }
    }
}
