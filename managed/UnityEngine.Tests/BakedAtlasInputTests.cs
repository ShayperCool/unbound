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
        sealed class StubPipelineAsset : UnityEngine.Rendering.RenderPipelineAsset
        {
            protected override UnityEngine.Rendering.RenderPipeline CreatePipeline() => null;
        }
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

        // Which materials' emission the bake takes (tests/projects/bakedbuffers Glow measures the
        // first five; a material flagged both emissive and black is Unbound's reading).
        [Theory]
        [InlineData(MaterialGlobalIlluminationFlags.BakedEmissive, true)]
        [InlineData(MaterialGlobalIlluminationFlags.RealtimeEmissive, true)]
        [InlineData(MaterialGlobalIlluminationFlags.AnyEmissive, true)]
        [InlineData(MaterialGlobalIlluminationFlags.None, false)]
        [InlineData(MaterialGlobalIlluminationFlags.EmissiveIsBlack, false)]
        [InlineData(MaterialGlobalIlluminationFlags.BakedEmissive | MaterialGlobalIlluminationFlags.EmissiveIsBlack, false)]
        public void EmissionIsBakedForMaterialsFlaggedEmissiveAndNotBlack(MaterialGlobalIlluminationFlags flags, bool emits)
        {
            var material = new Material(Shader.Create("Test/BakedEmission", 2000)) { globalIlluminationFlags = flags };
            Assert.Equal(emits, BakedAtlas.Emits(material));
            Object.DestroyImmediate(material);
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
        public void AMeshRendererReadsAndWritesStitchSeams()
        {
            // The bake stitches the seams of a renderer with Stitch Seams on (its file's
            // m_StitchLightmapSeams, which a scene save writes back).
            var go = new GameObject("stitched");
            try
            {
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.ReadSerialized(YNode.MakeMap(("m_StitchLightmapSeams", YNode.MakeScalar("0"))), null);
                Assert.False(renderer.stitchLightmapSeams);
                var written = SceneFiles.Body(renderer.WriteSerialized, new System.Collections.Generic.Dictionary<Object, long>(ReferenceEqualityComparer.Instance), new AssetDatabaseRuntime.Locations());
                Assert.Equal("0", written.Get("m_StitchLightmapSeams").Text);
                renderer.ReadSerialized(YNode.MakeMap(("m_StitchLightmapSeams", YNode.MakeScalar("1"))), null);
                Assert.True(renderer.stitchLightmapSeams);
                // A file without the key keeps the setting.
                renderer.ReadSerialized(YNode.MakeMap(), null);
                Assert.True(renderer.stitchLightmapSeams);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Fact]
        public void ABakesLightmapsAreWrittenOnlyWhenItMadeAllOfTheirLight()
        {
            Assert.Null(BakedAtlas.ImagesRefused(prefabs: false, approximated: false, indirectMade: true, lightsMeasured: true));
            Assert.Contains("prefab", BakedAtlas.ImagesRefused(true, false, true, true));
            Assert.Contains("did not make", BakedAtlas.ImagesRefused(false, true, true, true));
            Assert.Contains("did not make", BakedAtlas.ImagesRefused(false, false, false, true));
            Assert.Contains("without shadows", BakedAtlas.ImagesRefused(false, false, true, false));
            // The answer read: renderers in the bake's order, lights with their outputs.
            const string answer = "{\"images\":[\"AAEC\"],\"directionImages\":[\"AwQ=\"],\"shadowmaskImages\":null,\"placements\":[{\"lightmap\":0,\"scaleOffset\":[0.5,0.5,0,0]},{\"lightmap\":65534,\"scaleOffset\":[1,1,0,0]}],"
                + "\"order\":[1,0],\"lights\":[{\"fileId\":7,\"bakeType\":1,\"mixedMode\":0,\"channel\":0,\"probeIndex\":1,\"measured\":false}]}";
            var result = BakeResult.Of(answer, new List<(string, long)> { ("A", 11), ("B", 22) });
            Assert.Equal(new byte[] { 0, 1, 2 }, result.Images[0]);
            Assert.Equal(new byte[] { 3, 4 }, result.Directions[0]);
            Assert.Empty(result.Shadowmasks);
            Assert.Equal(22, result.Renderers[0].FileId);
            Assert.Equal(65534, result.Renderers[0].Lightmap);
            Assert.False(result.LightsMeasured);
            Assert.Equal((7L, 1, 0, 0, 1), result.Lights[0]);
        }

        [Fact]
        public void TheBakeTakesContributingRenderersBakedLightsAndLodGroups()
        {
            var go = new GameObject("taken");
            try
            {
                var renderer = go.AddComponent<MeshRenderer>();
                Assert.False(BakedAtlas.TakenByBake(renderer));
                go.m_StaticEditorFlags = 1;
                Assert.True(BakedAtlas.TakenByBake(renderer));
                var light = go.AddComponent<Light>();
                light.lightmapBakeType = LightmapBakeType.Realtime;
                Assert.False(BakedAtlas.TakenByBake(light));
                light.lightmapBakeType = LightmapBakeType.Mixed;
                Assert.True(BakedAtlas.TakenByBake(light));
                Assert.True(BakedAtlas.TakenByBake(go.AddComponent<LODGroup>()));
                Assert.False(BakedAtlas.TakenByBake(go.transform));
            }
            finally
            {
                Object.DestroyImmediate(go);
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

                // A cookie is said and not applied: the light is still of the bake.
                capture.Logs.Clear();
                var cookieFile = new LoadedFile { Path = "Assets/LitCookie.unity", Guid = "20000000000000000000000000000000" };
                sun.cookie = Texture2D.whiteTexture;
                cookieFile.Objects[1] = sun;
                Assert.Single(BakedAtlas.Lights(cookieFile, cookieFile.Path));
                Assert.Contains(capture.Logs, l => l.StartsWith("Warning: ") && l.Contains("'Sun' has a cookie"));

                // Non-directional lights whose intensity multiplies their linear colour are said
                // once for the scene and left out; directional lights are baked with linear-intensity colour.
                capture.Logs.Clear();
                var linearFile = new LoadedFile { Path = "Assets/LitLinear.unity", Guid = "30000000000000000000000000000000" };
                linearFile.Objects[1] = spot;
                linearFile.Objects[2] = soft;
                bool before = UnityEngine.Rendering.GraphicsSettings.lightsUseLinearIntensity;
                try
                {
                    UnityEngine.Rendering.GraphicsSettings.lightsUseLinearIntensity = true;
                    Assert.Empty(BakedAtlas.Lights(linearFile, linearFile.Path));
                    Assert.Single(capture.Logs, l => l.Contains("lightsUseLinearIntensity"));
                    Assert.DoesNotContain(capture.Logs, l => l.Contains("soft baked shadows"));

                    capture.Logs.Clear();
                    var linearSunFile = new LoadedFile { Path = "Assets/LitLinearSun.unity", Guid = "31000000000000000000000000000000" };
                    sun.cookie = null;
                    linearSunFile.Objects[1] = sun;
                    var linearLights = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(BakedAtlas.Lights(linearSunFile, linearSunFile.Path))).RootElement;
                    Assert.Equal(1, linearLights.GetArrayLength());
                    Assert.DoesNotContain(capture.Logs, l => l.Contains("lightsUseLinearIntensity"));
                    // Linear intensity multiplies linear color directly: 1^2.2 * 2 = 2, 0.5^2.2 * 2 = 0.42809.
                    Assert.Equal(2f, Channel(linearLights[0], "color", 0), 4);
                    Assert.Equal(0.42809f, Channel(linearLights[0], "color", 1), 4);

                    // Color temperature with linear intensity is unmeasured and skipped with a warning.
                    capture.Logs.Clear();
                    var tempFile = new LoadedFile { Path = "Assets/LitLinearTemp.unity", Guid = "32000000000000000000000000000000" };
                    sun.useColorTemperature = true;
                    tempFile.Objects[1] = sun;
                    Assert.Empty(BakedAtlas.Lights(tempFile, tempFile.Path));
                    Assert.Contains(capture.Logs, l => l.Contains("color temperature"));
                    sun.useColorTemperature = false;
                }
                finally { UnityEngine.Rendering.GraphicsSettings.lightsUseLinearIntensity = before; }

                // When a custom Lightmapping.RequestLightsDelegate is registered, BakedAtlas invokes it:
                // InitNoBake skips lights, non-Legacy point/spot falloff warns and skips, directional lights use delegate output.
                capture.Logs.Clear();
                var delegateFile = new LoadedFile { Path = "Assets/LitDelegate.unity", Guid = "33000000000000000000000000000000" };
                delegateFile.Objects[1] = sun;
                delegateFile.Objects[2] = soft;
                try
                {
                    UnityEngine.Experimental.GlobalIllumination.Lightmapping.SetDelegate((Light[] reqs, Unity.Collections.NativeArray<UnityEngine.Experimental.GlobalIllumination.LightDataGI> outs) =>
                    {
                        for (int i = 0; i < reqs.Length; i++)
                        {
                            var d = new UnityEngine.Experimental.GlobalIllumination.LightDataGI();
                            if (reqs[i].type == LightType.Directional)
                            {
                                var dl = new UnityEngine.Experimental.GlobalIllumination.DirectionalLight
                                {
                                    instanceID = reqs[i].GetInstanceID(),
                                    mode = UnityEngine.Experimental.GlobalIllumination.LightMode.Baked,
                                    color = new UnityEngine.Experimental.GlobalIllumination.LinearColor { red = 1f, green = 1f, blue = 1f, intensity = 3f },
                                    indirectColor = new UnityEngine.Experimental.GlobalIllumination.LinearColor { red = 1f, green = 1f, blue = 1f, intensity = 1.5f },
                                    orientation = reqs[i].transform.rotation,
                                    shadow = true,
                                };
                                d.Init(ref dl);
                            }
                            else
                            {
                                var pl = new UnityEngine.Experimental.GlobalIllumination.PointLight
                                {
                                    instanceID = reqs[i].GetInstanceID(),
                                    mode = UnityEngine.Experimental.GlobalIllumination.LightMode.Baked,
                                    color = UnityEngine.Experimental.GlobalIllumination.LinearColor.Convert(Color.white, 1f),
                                    indirectColor = UnityEngine.Experimental.GlobalIllumination.LinearColor.Convert(Color.white, 1f),
                                    range = 5f,
                                    falloff = UnityEngine.Experimental.GlobalIllumination.FalloffType.InverseSquared,
                                };
                                d.Init(ref pl);
                            }
                            outs[i] = d;
                        }
                    });
                    var delLights = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(BakedAtlas.Lights(delegateFile, delegateFile.Path))).RootElement;
                    Assert.Equal(1, delLights.GetArrayLength());
                    Assert.Equal(3f, Channel(delLights[0], "color", 0), 4);
                    Assert.Equal(1.5f, Channel(delLights[0], "indirectColor", 0), 4);
                    Assert.Contains(capture.Logs, l => l.Contains("InverseSquared falloff"));

                    // InitNoBake skips without warning.
                    capture.Logs.Clear();
                    BakedAtlas.ClearApproximated();
                    UnityEngine.Experimental.GlobalIllumination.Lightmapping.SetDelegate((Light[] reqs, Unity.Collections.NativeArray<UnityEngine.Experimental.GlobalIllumination.LightDataGI> outs) =>
                    {
                        for (int i = 0; i < reqs.Length; i++)
                        {
                            var d = new UnityEngine.Experimental.GlobalIllumination.LightDataGI();
                            d.InitNoBake(reqs[i].GetInstanceID());
                            outs[i] = d;
                        }
                    });
                    Assert.Empty(BakedAtlas.Lights(delegateFile, delegateFile.Path));
                    Assert.Empty(capture.Logs);
                    Assert.False(BakedAtlas.LastApproximated);

                    // Throwing delegate is caught, warns, marks Approximated, and returns empty.
                    capture.Logs.Clear();
                    BakedAtlas.ClearApproximated();
                    UnityEngine.Experimental.GlobalIllumination.Lightmapping.SetDelegate((Light[] _, Unity.Collections.NativeArray<UnityEngine.Experimental.GlobalIllumination.LightDataGI> _) =>
                        throw new System.InvalidOperationException("boom"));
                    Assert.Empty(BakedAtlas.Lights(delegateFile, "Assets/LitDelegateThrow.unity"));
                    Assert.Contains(capture.Logs, l => l.Contains("RequestLightsDelegate failed") && l.Contains("boom"));
                    Assert.True(BakedAtlas.LastApproximated);

                    // Spot with Legacy falloff + AnalyticAndInnerAngle angular falloff is rejected loudly.
                    capture.Logs.Clear();
                    BakedAtlas.ClearApproximated();
                    var angularFile = new LoadedFile { Path = "Assets/LitDelegateAngular.unity", Guid = "34000000000000000000000000000000" };
                    angularFile.Objects[1] = spot;
                    UnityEngine.Experimental.GlobalIllumination.Lightmapping.SetDelegate((Light[] reqs, Unity.Collections.NativeArray<UnityEngine.Experimental.GlobalIllumination.LightDataGI> outs) =>
                    {
                        var sl = new UnityEngine.Experimental.GlobalIllumination.SpotLight
                        {
                            instanceID = reqs[0].GetInstanceID(),
                            mode = UnityEngine.Experimental.GlobalIllumination.LightMode.Baked,
                            color = new UnityEngine.Experimental.GlobalIllumination.LinearColor { red = 1f, green = 1f, blue = 1f, intensity = 1f },
                            indirectColor = new UnityEngine.Experimental.GlobalIllumination.LinearColor { red = 1f, green = 1f, blue = 1f, intensity = 1f },
                            range = 5f,
                            coneAngle = 1f,
                            innerConeAngle = 0.5f,
                            falloff = UnityEngine.Experimental.GlobalIllumination.FalloffType.Legacy,
                            angularFalloff = UnityEngine.Experimental.GlobalIllumination.AngularFalloffType.AnalyticAndInnerAngle,
                        };
                        var d = new UnityEngine.Experimental.GlobalIllumination.LightDataGI();
                        d.Init(ref sl);
                        outs[0] = d;
                    });
                    Assert.Empty(BakedAtlas.Lights(angularFile, angularFile.Path));
                    Assert.Contains(capture.Logs, l => l.Contains("non-LUT angular falloff"));
                    Assert.True(BakedAtlas.LastApproximated);

                    // Delegate Point with Legacy falloff under lightsUseLinearIntensity = true is rejected loudly.
                    capture.Logs.Clear();
                    BakedAtlas.ClearApproximated();
                    var linDelFile = new LoadedFile { Path = "Assets/LitDelegateLinear.unity", Guid = "35000000000000000000000000000000" };
                    linDelFile.Objects[1] = soft;
                    UnityEngine.Experimental.GlobalIllumination.Lightmapping.SetDelegate((Light[] reqs, Unity.Collections.NativeArray<UnityEngine.Experimental.GlobalIllumination.LightDataGI> outs) =>
                    {
                        var pl = new UnityEngine.Experimental.GlobalIllumination.PointLight
                        {
                            instanceID = reqs[0].GetInstanceID(),
                            mode = UnityEngine.Experimental.GlobalIllumination.LightMode.Baked,
                            color = new UnityEngine.Experimental.GlobalIllumination.LinearColor { red = 1f, green = 1f, blue = 1f, intensity = 1f },
                            indirectColor = new UnityEngine.Experimental.GlobalIllumination.LinearColor { red = 1f, green = 1f, blue = 1f, intensity = 1f },
                            range = 5f,
                            falloff = UnityEngine.Experimental.GlobalIllumination.FalloffType.Legacy,
                        };
                        var d = new UnityEngine.Experimental.GlobalIllumination.LightDataGI();
                        d.Init(ref pl);
                        outs[0] = d;
                    });
                    UnityEngine.Rendering.GraphicsSettings.lightsUseLinearIntensity = true;
                    try
                    {
                        Assert.Empty(BakedAtlas.Lights(linDelFile, linDelFile.Path));
                        Assert.Contains(capture.Logs, l => l.Contains("lightsUseLinearIntensity"));
                        Assert.True(BakedAtlas.LastApproximated);

                        // Delegate Directional with useColorTemperature = true under lightsUseLinearIntensity = true is rejected loudly.
                        capture.Logs.Clear();
                        BakedAtlas.ClearApproximated();
                        var delTempFile = new LoadedFile { Path = "Assets/LitDelegateTemp.unity", Guid = "36000000000000000000000000000000" };
                        sun.useColorTemperature = true;
                        delTempFile.Objects[1] = sun;
                        UnityEngine.Experimental.GlobalIllumination.Lightmapping.SetDelegate((Light[] reqs, Unity.Collections.NativeArray<UnityEngine.Experimental.GlobalIllumination.LightDataGI> outs) =>
                        {
                            var dl = new UnityEngine.Experimental.GlobalIllumination.DirectionalLight
                            {
                                instanceID = reqs[0].GetInstanceID(),
                                mode = UnityEngine.Experimental.GlobalIllumination.LightMode.Baked,
                                color = new UnityEngine.Experimental.GlobalIllumination.LinearColor { red = 1f, green = 1f, blue = 1f, intensity = 1f },
                                indirectColor = new UnityEngine.Experimental.GlobalIllumination.LinearColor { red = 1f, green = 1f, blue = 1f, intensity = 1f },
                                orientation = reqs[0].transform.rotation,
                            };
                            var d = new UnityEngine.Experimental.GlobalIllumination.LightDataGI();
                            d.Init(ref dl);
                            outs[0] = d;
                        });
                        Assert.Empty(BakedAtlas.Lights(delTempFile, delTempFile.Path));
                        Assert.Contains(capture.Logs, l => l.Contains("color temperature"));
                        Assert.True(BakedAtlas.LastApproximated);
                        sun.useColorTemperature = false;
                    }
                    finally { UnityEngine.Rendering.GraphicsSettings.lightsUseLinearIntensity = before; }

                    // Mixed light with a render pipeline asset is refused in both delegate and non-delegate paths.
                    var prevRp = UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline;
                    var stubRp = ScriptableObject.CreateInstance<StubPipelineAsset>();
                    var mixedSun = Make("MixedSun", LightType.Directional, LightmapBakeType.Mixed);
                    var rpMixedFile = new LoadedFile { Path = "Assets/LitRpMixed.unity", Guid = "37000000000000000000000000000000" };
                    rpMixedFile.Objects[1] = mixedSun;
                    try
                    {
                        UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline = stubRp;
                        capture.Logs.Clear();
                        BakedAtlas.ClearApproximated();
                        UnityEngine.Experimental.GlobalIllumination.Lightmapping.SetDelegate((Light[] reqs, Unity.Collections.NativeArray<UnityEngine.Experimental.GlobalIllumination.LightDataGI> outs) =>
                        {
                            var dl = new UnityEngine.Experimental.GlobalIllumination.DirectionalLight
                            {
                                instanceID = reqs[0].GetInstanceID(),
                                mode = UnityEngine.Experimental.GlobalIllumination.LightMode.Mixed,
                                color = new UnityEngine.Experimental.GlobalIllumination.LinearColor { red = 1f, green = 1f, blue = 1f, intensity = 1f },
                                indirectColor = new UnityEngine.Experimental.GlobalIllumination.LinearColor { red = 1f, green = 1f, blue = 1f, intensity = 1f },
                                orientation = reqs[0].transform.rotation,
                            };
                            var d = new UnityEngine.Experimental.GlobalIllumination.LightDataGI();
                            d.Init(ref dl);
                            outs[0] = d;
                        });
                        Assert.Empty(BakedAtlas.Lights(rpMixedFile, "Assets/LitRpMixedDel.unity"));
                        Assert.Contains(capture.Logs, l => l.Contains("Mixed light with a render pipeline asset"));
                        Assert.True(BakedAtlas.LastApproximated);

                        UnityEngine.Experimental.GlobalIllumination.Lightmapping.ResetDelegate();
                        capture.Logs.Clear();
                        BakedAtlas.ClearApproximated();
                        Assert.Empty(BakedAtlas.Lights(rpMixedFile, "Assets/LitRpMixedBuiltin.unity"));
                        Assert.Contains(capture.Logs, l => l.Contains("Mixed light with a render pipeline asset"));
                        Assert.True(BakedAtlas.LastApproximated);

                        // Empty material slots under a render pipeline asset: missing slot, null slot < subMeshCount, and trailing null >= subMeshCount.
                        var shader = Shader.Create("Stub", 2000);
                        var mat = new Material(shader);
                        try
                        {
                            capture.Logs.Clear();
                            BakedAtlas.ClearApproximated();
                            Assert.True(BakedAtlas.CheckEmptySlotsForRenderPipeline("Assets/RpEmpty.unity", 1, "Short", new[] { mat }, 2, BakedAtlas.Approximated));
                            Assert.Contains(capture.Logs, l => l.Contains("empty material slot"));
                            Assert.True(BakedAtlas.LastApproximated);

                            capture.Logs.Clear();
                            BakedAtlas.ClearApproximated();
                            Assert.True(BakedAtlas.CheckEmptySlotsForRenderPipeline("Assets/RpEmpty.unity", 2, "NullSub", new[] { mat, null }, 2, BakedAtlas.Approximated));
                            Assert.Contains(capture.Logs, l => l.Contains("empty material slot"));
                            Assert.True(BakedAtlas.LastApproximated);

                            capture.Logs.Clear();
                            BakedAtlas.ClearApproximated();
                            Assert.False(BakedAtlas.CheckEmptySlotsForRenderPipeline("Assets/RpEmpty.unity", 3, "TrailingNull", new[] { mat, null }, 1, BakedAtlas.Approximated));
                            Assert.Empty(capture.Logs);
                            Assert.False(BakedAtlas.LastApproximated);
                        }
                        finally
                        {
                            Object.DestroyImmediate(mat);
                            Object.DestroyImmediate(shader);
                        }
                    }
                    finally
                    {
                        UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline = prevRp;
                        Object.DestroyImmediate(stubRp);
                    }
                }
                finally
                {
                    UnityEngine.Experimental.GlobalIllumination.Lightmapping.ResetDelegate();
                }
            }
            finally
            {
                logger.logHandler = previous;
                foreach (var go in made) Object.DestroyImmediate(go);
            }
        }

        [Fact]
        public void TheBakeTakesTheProbesOfEnabledGroupsOfActiveObjectsInWorldSpace()
        {
            var made = new List<GameObject>();
            LightProbeGroup Make(string name, params Vector3[] points)
            {
                var go = new GameObject(name);
                made.Add(go);
                var group = go.AddComponent<LightProbeGroup>();
                group.probePositions = points;
                return group;
            }
            try
            {
                var file = new LoadedFile { Path = "Assets/Probes.unity", Guid = "50000000000000000000000000000000" };
                var moved = Make("Moved", new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, 2f));
                moved.transform.position = new Vector3(0.5f, 1f, -1f);
                moved.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
                moved.transform.localScale = new Vector3(2f, 2f, 2f);
                Make("Disabled", Vector3.one).enabled = false;
                Make("Inactive", Vector3.one).gameObject.SetActive(false);
                file.Objects[7] = moved;
                file.Objects[8] = made[1].GetComponent<LightProbeGroup>();
                file.Objects[9] = made[2].GetComponent<LightProbeGroup>();
                // A group destroyed since the scene loaded is not baked.
                var destroyed = Make("Destroyed", Vector3.one);
                file.Objects[10] = destroyed;
                Object.DestroyImmediate(destroyed);

                var groups = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(BakedAtlas.Probes(file))).RootElement;
                Assert.Equal(1, groups.GetArrayLength());
                Assert.Equal(7, groups[0].GetProperty("fileId").GetInt64());
                var points = groups[0].GetProperty("points");
                // Scaled by 2 and turned a quarter about y: +x goes to -z, +z to +x.
                var expected = new[] { 0.5f, 1f, -3f, 4.5f, 1f, -1f };
                Assert.Equal(expected.Length, points.GetArrayLength());
                for (int i = 0; i < expected.Length; i++) Assert.Equal(expected[i], points[i].GetSingle(), 5);
            }
            finally
            {
                foreach (var go in made) Object.DestroyImmediate(go);
            }
        }

        /// <summary>A scene file of one lightmapped quad, in a LOD Group or not.</summary>
        static LoadedFile QuadScene(string path, bool inLodGroup, List<Object> made)
        {
            var file = new LoadedFile { Path = path, Guid = "40000000000000000000000000000000" };
            var mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, new Vector3(1f, 1f, 0f), Vector3.up },
                normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up },
                triangles = new[] { 0, 2, 1, 0, 3, 2 },
            };
            var go = new GameObject("Leveled");
            made.Add(mesh);
            made.Add(go);
            go.m_StaticEditorFlags = 1;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            file.Objects[1] = renderer;
            if (inLodGroup)
            {
                var group = go.AddComponent<LODGroup>();
                group.SetLODs(new[] { new LOD(0.5f, new Renderer[] { renderer }) });
                file.Objects[2] = group;
            }
            return file;
        }

        [Fact]
        public void LodMembershipsAssignGroupIdsAndLevelMasksForActiveEnabledGroupsOnly()
        {
            var made = new List<Object>();
            try
            {
                MeshRenderer MakeRenderer(string name, Transform parent = null)
                {
                    var go = new GameObject(name);
                    if (parent != null) go.transform.SetParent(parent, false);
                    made.Add(go);
                    return go.AddComponent<MeshRenderer>();
                }
                var file = new LoadedFile { Path = "Assets/LodMemberships.unity", Guid = "40000000000000000000000000000001" };
                var rootA = new GameObject("GroupA");
                made.Add(rootA);
                var rA0 = MakeRenderer("A0", rootA.transform);
                var rA1 = MakeRenderer("A1", rootA.transform);
                var rAShared = MakeRenderer("AShared", rootA.transform);
                var groupA = rootA.AddComponent<LODGroup>();
                groupA.SetLODs(new[]
                {
                    new LOD(0.6f, new Renderer[] { rA0, rAShared }),
                    new LOD(0.3f, new Renderer[] { rA1, rAShared }),
                });

                var rootB = new GameObject("GroupB");
                made.Add(rootB);
                var rB0 = MakeRenderer("B0", rootB.transform);
                var groupB = rootB.AddComponent<LODGroup>();
                groupB.SetLODs(new[] { new LOD(0.5f, new Renderer[] { rB0 }) });

                // Disabled LODGroup and inactive GameObject LODGroup must not attach renderers,
                // and a later disabled group listing a renderer from an active group must not steal it.
                var rootDisabled = new GameObject("GroupDisabled");
                made.Add(rootDisabled);
                var rDisabled = MakeRenderer("DisabledLod1");
                var groupDisabled = rootDisabled.AddComponent<LODGroup>();
                groupDisabled.SetLODs(new[] { new LOD(0.6f, new Renderer[] { rA0 }), new LOD(0.3f, new Renderer[] { rDisabled }) });
                groupDisabled.enabled = false;

                var rootInactive = new GameObject("GroupInactive");
                made.Add(rootInactive);
                var rInactive = MakeRenderer("InactiveLod1");
                var groupInactive = rootInactive.AddComponent<LODGroup>();
                groupInactive.SetLODs(new[] { new LOD(0.6f, System.Array.Empty<Renderer>()), new LOD(0.3f, new Renderer[] { rInactive }) });
                rootInactive.SetActive(false);

                // A destroyed LODGroup whose SetLODs previously claimed rA0 must not block groupA.
                var rootDestroyed = new GameObject("GroupDestroyed");
                made.Add(rootDestroyed);
                var groupDestroyed = rootDestroyed.AddComponent<LODGroup>();
                groupDestroyed.SetLODs(new[] { new LOD(0.6f, new Renderer[] { rA0 }) });
                Object.DestroyImmediate(groupDestroyed);

                // File IDs intentionally out of creation order: GroupB (10) precedes GroupA (20).
                file.Objects[20] = groupA;
                file.Objects[10] = groupB;
                file.Objects[30] = groupDisabled;
                file.Objects[40] = groupInactive;
                file.Objects[50] = groupDestroyed;

                var lodNotices = new BakedAtlas.LodNotices();
                var memberships = BakedAtlas.LodMemberships(file, lodNotices);
                Assert.Equal((0u, (byte)0b01), memberships[rB0]);
                Assert.Equal((1u, (byte)0b01), memberships[rA0]);
                Assert.Equal((1u, (byte)0b10), memberships[rA1]);
                Assert.Equal((1u, (byte)0b11), memberships[rAShared]);
                Assert.False(memberships.ContainsKey(rDisabled));
                Assert.False(memberships.ContainsKey(rInactive));
                Assert.Contains(rInactive, lodNotices.InactiveRefs);

                // A LOD Group no longer emits the unimplemented-levels approximation warning under 6000.0.
                var logger = Debug.unityLogger;
                var previous = logger.logHandler;
                var capture = new Capture(previous);
                try
                {
                    logger.logHandler = capture;
                    using (TestRelease.Pin(TestRelease.Measured))
                    {
                        var withLodFile = QuadScene("Assets/WithLod.unity", true, made);
                        BakedAtlas.Lay(withLodFile, new LightingSettings());
                        Assert.DoesNotContain(capture.Logs, l => l.Contains("LOD Group"));

                        // Non-ContributeGI / dynamic renderers in unmeasured LOD configurations
                        // (more than 8 levels, shared lower levels, multi-group, inactive group)
                        // must NOT trigger Approximated or block baking static ContributeGI geometry.
                        var dynamicScene = QuadScene("Assets/DynamicLodOnly.unity", false, made);
                        var dynManyRoot = new GameObject("DynManyLods");
                        made.Add(dynManyRoot);
                        var dynR = MakeRenderer("DynProp", dynManyRoot.transform); // ContributeGI is 0
                        var dynMany = dynManyRoot.AddComponent<LODGroup>();
                        var nine = new LOD[9];
                        for (int i = 0; i < 9; i++) nine[i] = new LOD(0.9f - i * 0.08f, i is 1 or 2 ? new Renderer[] { dynR } : System.Array.Empty<Renderer>());
                        dynMany.SetLODs(nine);
                        var dynMultiRoot = new GameObject("DynMultiGroup");
                        made.Add(dynMultiRoot);
                        var dynMulti = dynMultiRoot.AddComponent<LODGroup>();
                        dynMulti.SetLODs(new[] { new LOD(0.6f, new Renderer[] { dynR }) });
                        dynamicScene.Objects[10] = dynMany;
                        dynamicScene.Objects[11] = dynMulti;
                        capture.Logs.Clear();
                        BakedAtlas.Lay(dynamicScene, new LightingSettings());
                        Assert.False(BakedAtlas.LastApproximated);
                        Assert.DoesNotContain(capture.Logs, l => l.Contains("more than 8") || l.Contains("multiple lower LOD levels") || l.Contains("more than one active LODGroup"));

                        // Baked ContributeGI renderer in >8-level LODGroup must report loudly in Lay.
                        var manyFile = QuadScene("Assets/ManyLods.unity", false, made);
                        var manyRenderer = (MeshRenderer)manyFile.Objects[1];
                        var manyGroup = manyRenderer.gameObject.AddComponent<LODGroup>();
                        var nineBaked = new LOD[9];
                        for (int i = 0; i < 9; i++) nineBaked[i] = new LOD(0.9f - i * 0.08f, i == 0 ? new Renderer[] { manyRenderer } : System.Array.Empty<Renderer>());
                        manyGroup.SetLODs(nineBaked);
                        manyFile.Objects[2] = manyGroup;
                        capture.Logs.Clear();
                        BakedAtlas.Lay(manyFile, new LightingSettings());
                        Assert.Contains(capture.Logs, l => l.Contains("more than 8"));

                        // Baked ContributeGI renderer shared across lower LOD levels without LOD 0 (mask 0b110) must report loudly in Lay.
                        var lowerFile = QuadScene("Assets/LowerShared.unity", false, made);
                        var lowerRenderer = (MeshRenderer)lowerFile.Objects[1];
                        var lowerGroup = lowerRenderer.gameObject.AddComponent<LODGroup>();
                        lowerGroup.SetLODs(new[]
                        {
                            new LOD(0.6f, System.Array.Empty<Renderer>()),
                            new LOD(0.3f, new Renderer[] { lowerRenderer }),
                            new LOD(0.1f, new Renderer[] { lowerRenderer }),
                        });
                        lowerFile.Objects[2] = lowerGroup;
                        capture.Logs.Clear();
                        BakedAtlas.Lay(lowerFile, new LightingSettings());
                        Assert.Contains(capture.Logs, l => l.Contains("multiple lower LOD levels"));

                        // Baked ContributeGI renderer listed by two active LODGroups (even when a third disabled group ran SetLODs last)
                        // deterministically keeps the first group by file ID and reports loudly in Lay.
                        var multiFile = QuadScene("Assets/MultiGroup.unity", false, made);
                        var rMulti = (MeshRenderer)multiFile.Objects[1];
                        var multiA = new GameObject("MultiA");
                        var multiB = new GameObject("MultiB");
                        var multiOff = new GameObject("MultiOff");
                        made.Add(multiA); made.Add(multiB); made.Add(multiOff);
                        var gMultiA = multiA.AddComponent<LODGroup>();
                        var gMultiB = multiB.AddComponent<LODGroup>();
                        var gMultiOff = multiOff.AddComponent<LODGroup>();
                        gMultiA.SetLODs(new[] { new LOD(0.6f, new Renderer[] { rMulti }) });
                        gMultiB.SetLODs(new[] { new LOD(0.6f, System.Array.Empty<Renderer>()), new LOD(0.3f, new Renderer[] { rMulti }) });
                        gMultiOff.SetLODs(new[] { new LOD(0.6f, new Renderer[] { rMulti }) });
                        gMultiOff.enabled = false;
                        multiFile.Objects[10] = gMultiA;
                        multiFile.Objects[20] = gMultiB;
                        multiFile.Objects[30] = gMultiOff;
                        var multiNotices = new BakedAtlas.LodNotices();
                        var multiMemberships = BakedAtlas.LodMemberships(multiFile, multiNotices);
                        Assert.Equal((0u, (byte)0b01), multiMemberships[rMulti]);
                        Assert.Contains(rMulti, multiNotices.MultiGroup);
                        capture.Logs.Clear();
                        BakedAtlas.Lay(multiFile, new LightingSettings());
                        Assert.Contains(capture.Logs, l => l.Contains("more than one active LODGroup"));

                        // Lower-LOD renderer with maxBounces > 2 must report loudly in Lay.
                        var bounceFile = QuadScene("Assets/LodBounces.unity", false, made);
                        var bounceRenderer = (MeshRenderer)bounceFile.Objects[1];
                        var bounceGroup = bounceRenderer.gameObject.AddComponent<LODGroup>();
                        bounceGroup.SetLODs(new[] { new LOD(0.6f, System.Array.Empty<Renderer>()), new LOD(0.3f, new Renderer[] { bounceRenderer }) });
                        bounceFile.Objects[2] = bounceGroup;
                        capture.Logs.Clear();
                        BakedAtlas.Lay(bounceFile, new LightingSettings { maxBounces = 4 });
                        Assert.Contains(capture.Logs, l => l.Contains("maxBounces=4"));

                        // Inactive LODGroup GameObject referencing an active baked renderer (even when also in an active LODGroup)
                        // must report loudly in Lay.
                        var inactiveFile = QuadScene("Assets/LodInactiveRef.unity", true, made);
                        var activeRenderer = (MeshRenderer)inactiveFile.Objects[1];
                        var inactiveHolder = new GameObject("InactiveHolder");
                        made.Add(inactiveHolder);
                        var inactiveGroup = inactiveHolder.AddComponent<LODGroup>();
                        inactiveGroup.SetLODs(new[] { new LOD(0.6f, System.Array.Empty<Renderer>()), new LOD(0.3f, new Renderer[] { activeRenderer }) });
                        inactiveHolder.SetActive(false);
                        inactiveFile.Objects[20] = inactiveGroup;
                        capture.Logs.Clear();
                        BakedAtlas.Lay(inactiveFile, new LightingSettings());
                        Assert.Contains(capture.Logs, l => l.Contains("inactive LODGroup"));
                        Assert.Equal(((uint?)0u, 0b10), BakedAtlas.LodBakeFields(true, (0u, 0b10)));
                    }

                    // Unmeasured release families must emit the LOD Group notice and omit LOD ray filtering.
                    using (TestRelease.Pin(TestRelease.Unmeasured))
                    {
                        var unmeasuredFile = QuadScene("Assets/UnmeasuredLod.unity", true, made);
                        capture.Logs.Clear();
                        BakedAtlas.Lay(unmeasuredFile, new LightingSettings());
                        Assert.True(BakedAtlas.LastApproximated);
                        Assert.Contains(capture.Logs, l => l.Contains("LOD Group baked lightmapping is measured only for Unity 6000.0"));
                        Assert.Equal((null, 0), BakedAtlas.LodBakeFields(true, (0u, 0b10)));
                    }
                }
                finally
                {
                    logger.logHandler = previous;
                }
            }
            finally
            {
                foreach (var o in made) Object.DestroyImmediate(o);
            }
        }
    }
}
