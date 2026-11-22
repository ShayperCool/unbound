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

        [Fact]
        public void TheBakeTakesItsMixedAndBakedLightsWithTheirLinearColours()
        {
            var logger = Debug.unityLogger;
            var previous = logger.logHandler;
            var capture = new Capture(previous);
            var made = new List<GameObject>();
            Light Make(string name, LightType type, LightmapBakeType mode)
            {
                var go = new GameObject(name);
                made.Add(go);
                var light = go.AddComponent<Light>();
                light.type = type;
                light.lightmapBakeType = mode;
                return light;
            }
            try
            {
                logger.logHandler = capture;
                var file = new LoadedFile { Path = "Assets/Lit.unity", Guid = "10000000000000000000000000000000" };
                var sun = Make("Sun", LightType.Directional, LightmapBakeType.Baked);
                sun.color = new Color(1f, 0.5f, 0.25f);
                sun.intensity = 2f;
                sun.bounceIntensity = 3f;
                sun.shadows = LightShadows.Hard;
                sun.transform.eulerAngles = new Vector3(90f, 0f, 0f);
                var spot = Make("Spot", LightType.Spot, LightmapBakeType.Mixed);
                spot.transform.position = new Vector3(1f, 2f, 3f);
                spot.range = 7f;
                spot.spotAngle = 40f;
                spot.shadows = LightShadows.None;
                var soft = Make("Soft", LightType.Point, LightmapBakeType.Baked);
                soft.shadows = LightShadows.Soft;
                soft.shadowRadius = 0.5f;
                Make("Realtime", LightType.Point, LightmapBakeType.Realtime);
                Make("Disabled", LightType.Point, LightmapBakeType.Baked).enabled = false;
                Make("Inactive", LightType.Point, LightmapBakeType.Baked).gameObject.SetActive(false);
                Make("Area", LightType.Rectangle, LightmapBakeType.Baked);
                long id = 100;
                foreach (var go in made) file.Objects[id++] = go.GetComponent<Light>();

                var lights = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(BakedAtlas.Lights(file, file.Path))).RootElement;
                // Realtime, disabled and inactive lights are not of the bake; an area light is
                // said and left out.
                Assert.Equal(3, lights.GetArrayLength());
                var first = lights[0];
                Assert.Equal("Sun", first.GetProperty("name").GetString());
                Assert.Equal(100, first.GetProperty("fileId").GetInt64());
                Assert.Equal((0, 2, true), (first.GetProperty("kind").GetInt32(), first.GetProperty("mode").GetInt32(), first.GetProperty("castsShadows").GetBoolean()));
                // Colour times intensity, then to linear: 2 -> 2^2.2, 1 -> 1, 0.5 -> 0.214; the
                // indirect colour with the Indirect Multiplier before the conversion.
                float Channel(System.Text.Json.JsonElement light, string field, int k) => light.GetProperty(field)[k].GetSingle();
                Assert.Equal(4.5948f, Channel(first, "color", 0), 3);
                Assert.Equal(1f, Channel(first, "color", 1), 5);
                Assert.Equal(0.21404f, Channel(first, "color", 2), 4);
                Assert.Equal(51.515f, Channel(first, "indirectColor", 0), 2);
                Assert.Equal(11.2116f, Channel(first, "indirectColor", 1), 3);
                // Turned a quarter about x: (sin 45, 0, 0, cos 45).
                Assert.Equal(0.70711f, Channel(first, "rotation", 0), 4);
                Assert.Equal(0.70711f, Channel(first, "rotation", 3), 4);
                var second = lights[1];
                Assert.Equal((2, 1, false), (second.GetProperty("kind").GetInt32(), second.GetProperty("mode").GetInt32(), second.GetProperty("castsShadows").GetBoolean()));
                Assert.Equal((7f, 40f), (second.GetProperty("range").GetSingle(), second.GetProperty("spotAngle").GetSingle()));
                Assert.Equal(new[] { 1f, 2f, 3f }, new[] { Channel(second, "position", 0), Channel(second, "position", 1), Channel(second, "position", 2) });
                Assert.Equal("Soft", lights[2].GetProperty("name").GetString());
                Assert.Contains(capture.Logs, l => l.StartsWith("Warning: ") && l.Contains("'Area' is a ") && l.Contains(" light, which Unbound's bake"));
                Assert.Contains(capture.Logs, l => l.StartsWith("Warning: ") && l.Contains("'Soft' has soft baked shadows"));
                Assert.DoesNotContain(capture.Logs, l => l.Contains("'Sun'") || l.Contains("'Spot'") || l.Contains("'Realtime'"));
            }
            finally
            {
                logger.logHandler = previous;
                foreach (var go in made) Object.DestroyImmediate(go);
            }
        }
    }
}
