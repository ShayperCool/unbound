using System;
using System.Text.Json;
using Unbound.Runtime;
using UnityEngine;
using Xunit;

namespace Unbound.Tests
{
    /// <summary>The lighting data reply's floats and renderer entries (the values
    /// tests/projects/lightingdatanan records in the Editor).</summary>
    public class LightingDataReplyTests
    {
        static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement;

        [Fact]
        public void NonFiniteFloatsAreReadByName()
        {
            var values = LightingDataReply.Singles(Json("[0.25, \"Infinity\", \"-Infinity\", \"NaN\", -0.0]"));
            Assert.Equal(0.25f, values[0]);
            Assert.Equal(float.PositiveInfinity, values[1]);
            Assert.Equal(float.NegativeInfinity, values[2]);
            Assert.True(float.IsNaN(values[3]));
            Assert.True(float.IsNegative(values[4]));
            Assert.Throws<FormatException>(() => LightingDataReply.Single(Json("\"inf\"")));
            Assert.Equal(new Vector4(1f, 2f, 0f, 0f), LightingDataReply.Vector(Json("[1, 2]")));
        }

        [Fact]
        public void RendererEntriesKeepProbeLitIndexAndNonFiniteScaleOffset()
        {
            LightmapSettings.Reset();
            try
            {
                var file = new LoadedFile();
                MeshRenderer Renderer(long id)
                {
                    var renderer = new GameObject("r" + id).AddComponent<MeshRenderer>();
                    file.Objects[id] = renderer;
                    return renderer;
                }
                var flat = Renderer(10);
                var probeLit = Renderer(11);
                var unlisted = Renderer(12);
                var lightmapped = Renderer(13);
                var reply = Json(@"{ ""renderers"": [
                    { ""object"": 10, ""prefab"": 0, ""index"": 65534, ""st"": [""Infinity"", ""Infinity"", ""NaN"", ""NaN""], ""indexDynamic"": 65535, ""stDynamic"": [1, 1, 0, 0] },
                    { ""object"": 11, ""prefab"": 0, ""index"": 65534, ""st"": [0.25, 0.25, 0, 0], ""indexDynamic"": 65535, ""stDynamic"": [1, 1, 0, 0] },
                    { ""object"": 12, ""prefab"": 0, ""index"": 65535, ""st"": [1, 1, 0, 0], ""indexDynamic"": 65535, ""stDynamic"": [1, 1, 0, 0] },
                    { ""object"": 13, ""prefab"": 0, ""index"": 0, ""st"": [0.5, 0.5, 0.125, 0], ""indexDynamic"": 65535, ""stDynamic"": [1, 1, 0, 0] } ] }");
                BakedLighting.Read(reply, file);

                Assert.Equal(65534, flat.lightmapIndex);
                var st = flat.lightmapScaleOffset;
                Assert.Equal((float.PositiveInfinity, float.PositiveInfinity), (st.x, st.y));
                Assert.True(float.IsNaN(st.z) && float.IsNaN(st.w));
                Assert.Equal(-1, flat.realtimeLightmapIndex);
                Assert.Equal(65534, probeLit.lightmapIndex);
                Assert.Equal(new Vector4(0.25f, 0.25f, 0f, 0f), probeLit.lightmapScaleOffset);
                Assert.Equal(-1, unlisted.lightmapIndex);
                Assert.Equal(0, lightmapped.lightmapIndex);
                Assert.Equal(new Vector4(0.5f, 0.5f, 0.125f, 0f), lightmapped.lightmapScaleOffset);
            }
            finally
            {
                LightmapSettings.Reset();
            }
        }
    }
}
