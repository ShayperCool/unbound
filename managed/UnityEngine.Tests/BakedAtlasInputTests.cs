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

        const string InvalidNormals = "Warning: Some meshes have invalid normals and will be ignored by the Global Illumination system.";

        static string Entries(List<object> entries) => System.Text.Json.JsonSerializer.Serialize(entries);

        [Fact]
        public void TheBakesAnswerGivesItsRenderersInItsOrderAndSaysWhichMeshesItLeftOut()
        {
            var logger = Debug.unityLogger;
            var previous = logger.logHandler;
            var capture = new Capture(previous);
            var sent = new List<(string, long)> { ("A", 11), ("Parent/B", 22), ("C", 33) };
            try
            {
                logger.logHandler = capture;
                // The second renderer's mesh has no normals: it is not of the bake, which numbers
                // the third before the first.
                var lightmaps = new List<int[]>();
                var entries = BakedAtlas.Read(
                    "{\"lightmaps\":[[64,32]],\"placements\":[{\"lightmap\":0,\"scaleOffset\":[0.5,0.5,0,0.25]},null,{\"lightmap\":65534,\"scaleOffset\":[1,1,0,0]}],\"order\":[2,0],\"invalidNormals\":true}",
                    sent, lightmaps);
                Assert.Equal(
                    "[{\"path\":\"C\",\"fileId\":33,\"lightmap\":65534,\"scaleOffset\":[1,1,0,0]},{\"path\":\"A\",\"fileId\":11,\"lightmap\":0,\"scaleOffset\":[0.5,0.5,0,0.25]}]",
                    Entries(entries));
                Assert.Equal(new[] { 64, 32 }, Assert.Single(lightmaps));
                // Said as the Editor says it, once for the bake.
                Assert.Equal(new[] { InvalidNormals }, capture.Logs);

                // Every mesh with normals: nothing is said.
                capture.Logs.Clear();
                entries = BakedAtlas.Read(
                    "{\"lightmaps\":[],\"placements\":[{\"lightmap\":0,\"scaleOffset\":[1,1,0,0]},{\"lightmap\":0,\"scaleOffset\":[1,1,0,0]},{\"lightmap\":0,\"scaleOffset\":[1,1,0,0]}],\"order\":[1,2,0],\"invalidNormals\":false}",
                    sent, new List<int[]>());
                Assert.Equal(3, entries.Count);
                Assert.StartsWith("[{\"path\":\"Parent/B\",\"fileId\":22,", Entries(entries));
                Assert.Empty(capture.Logs);

                // An answer for another number of renderers is refused.
                Assert.Throws<System.InvalidOperationException>(() => BakedAtlas.Read(
                    "{\"lightmaps\":[],\"placements\":[null],\"order\":[],\"invalidNormals\":false}", sent, new List<int[]>()));
            }
            finally
            {
                logger.logHandler = previous;
            }
        }
    }
}
