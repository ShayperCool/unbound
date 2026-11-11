using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Unbound.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Xunit;
using Object = UnityEngine.Object;

namespace Unbound.Tests
{
    /// <summary>
    /// LightingSettings, UnityEditor.Lightmapping's scene settings and ShaderVariantCollection as
    /// the Unity 6000.0.71f1 Editor answers them (tests/projects/lightingassets,
    /// Reference/unity-6000.0.71f1/values.jsonl), plus inputs the probe does not hold.
    /// </summary>
    public class LightingAssetsTests : IDisposable
    {
        readonly Dictionary<string, string> m_Declarations = new Dictionary<string, string>();

        public LightingAssetsTests()
        {
            ShaderPassInfo.ResetForTests();
            ShaderWarmup.s_PrepareForTests = (_, _, _) => { };
            ShaderDeclarations.ResetForTests();
            ShaderDeclarations.s_FrameForTests = 1;
            ShaderDeclarations.s_CallForTests = Call;
        }

        public void Dispose()
        {
            ShaderWarmup.s_PrepareForTests = null;
            ShaderWarmup.s_SendForTests = null;
            ShaderPassInfo.ResetForTests();
            ShaderDeclarations.s_CallForTests = null;
            ShaderDeclarations.s_FrameForTests = null;
            ShaderDeclarations.ResetForTests();
        }

        string Call(string method, string request)
        {
            using var document = JsonDocument.Parse(request);
            return method == "shader-declarations" && m_Declarations.TryGetValue(document.RootElement.GetProperty("name").GetString(), out var s)
                ? s
                : "{\"found\":false,\"subshaders_known\":false,\"keywords_known\":false,\"transient\":false,\"reason\":\"\",\"subshaders\":[],\"keywords\":[]}";
        }

        static int s_Made;

        /// <summary>A shader whose declarations say it has passes with these LightMode tags (null:
        /// untagged) and these keywords; <paramref name="known"/> false: the host knows neither.</summary>
        Shader Declared(string[] lightModes, string[] keywords, bool known = true)
        {
            var shader = ShaderUtil.CreateShaderAsset("Shader \"Tests/Lighting/S" + (++s_Made) + "\" { SubShader { Pass { } } }");
            var passes = string.Join(",", lightModes.Select(m => "{\"name\":\"\",\"tags\":" + (m == null ? "[]" : "[[\"LIGHTMODE\",\"" + m + "\"]]") + "}"));
            var words = string.Join(",", keywords.Select(k => "{\"name\":\"" + k + "\",\"local\":false,\"dynamic\":false}"));
            string flag = known ? "true" : "false";
            m_Declarations[shader.name] = "{\"found\":true,\"builtin\":false,\"subshaders_known\":" + flag + ",\"keywords_known\":" + flag
                + ",\"transient\":false,\"reason\":\"\",\"subshaders\":[{\"tags\":[],\"passes\":[" + passes + "],\"lod\":0,\"restricted\":false}],\"keywords\":[" + words + "]}";
            return shader;
        }

        sealed class Resolver : IReferenceResolver
        {
            internal readonly Dictionary<(string, long), Object> Objects = new Dictionary<(string, long), Object>();
            public Object Resolve(ObjectRef reference, Type expected) =>
                Objects.TryGetValue((reference.Guid ?? "", reference.FileId), out var o) ? o : null;
        }

        static T Read<T>(string yaml, IReferenceResolver resolver = null) where T : Object, new()
        {
            var asset = new T();
            ((ISerializedAsset)asset).ReadSerialized(UnityYaml.Parse(yaml)[0].Body, resolver ?? new Resolver());
            return asset;
        }

        [Fact]
        public void GlobalWarmupRequestsEveryLoadedShaderWithoutChangingCollectionProgress()
        {
            var first = Declared(new string[] { null }, new[] { "A" });
            var second = Declared(new string[] { null }, Array.Empty<string>());
            first.maximumLOD = 1;
            var collection = new ShaderVariantCollection();
            collection.Add(new ShaderVariantCollection.ShaderVariant(first, PassType.Normal, "A"));
            var sent = new Dictionary<string, int>();
            ShaderWarmup.s_SendForTests = json =>
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                Assert.True(root.GetProperty("all_variants").GetBoolean());
                Assert.Empty(root.GetProperty("passes").EnumerateArray());
                Assert.Empty(root.GetProperty("keywords").EnumerateArray());
                var name = root.GetProperty("shader").GetString();
                sent[name] = sent.TryGetValue(name, out var count) ? count + 1 : 1;
            };
            Shader.WarmupAllShaders();
            Assert.Equal(1, sent[first.name]);
            Assert.Equal(1, sent[second.name]);
            Assert.False(collection.isWarmedUp);
            Assert.Equal(0, collection.warmedUpVariantCount);
            Object.DestroyImmediate(collection);
            Object.DestroyImmediate(first);
            Object.DestroyImmediate(second);
        }

        // ---------------------------------------------------------------- LightingSettings

        [Fact]
        public void ANewObjectHasTheEditorsDefaults()
        {
            var s = new LightingSettings();
            Assert.True(s.bakedGI);
            Assert.False(s.realtimeGI);
            Assert.False(s.realtimeEnvironmentLighting);
            Assert.Equal(MixedLightingMode.Shadowmask, s.mixedBakeMode);
            Assert.Equal(LightingSettings.Lightmapper.ProgressiveCPU, s.lightmapper);
            Assert.Equal((1024, 40f, 2), (s.lightmapMaxSize, s.lightmapResolution, s.lightmapPadding));
            Assert.Equal(LightmapCompression.HighQuality, s.lightmapCompression);
            Assert.Equal(LightmapsMode.CombinedDirectional, s.directionalityMode);
            Assert.Equal(LightingSettings.Sampling.Fixed, s.sampling);
            Assert.Equal((32, 512, 256), (s.directSampleCount, s.indirectSampleCount, s.environmentSampleCount));
            Assert.Equal((2, 2), (s.minBounces, s.maxBounces));
            Assert.Equal(LightingSettings.FilterMode.Auto, s.filteringMode);
            Assert.Equal(LightingSettings.DenoiserType.Optix, s.denoiserTypeAO);
            Assert.Equal((0.5f, 2f, 1f), (s.filteringAtrousPositionSigmaDirect, s.filteringAtrousPositionSigmaIndirect, s.filteringAtrousPositionSigmaAO));
            Assert.Equal(4f, s.lightProbeSampleCountMultiplier);
            Assert.True(s.prioritizeView);
            Assert.True(s.environmentImportanceSampling);
            Assert.Equal("", s.name);
        }

        [Theory]
        [InlineData(100, 128)]
        [InlineData(80, 64)]
        [InlineData(96, 128)]
        [InlineData(3000, 2048)]
        [InlineData(8192, 4096)]
        [InlineData(0, 32)]
        [InlineData(-5, 32)]
        public void LightmapSizesAreTheClosestPowerOfTwoWithin32To4096(int set, int read) =>
            Assert.Equal(read, new LightingSettings { lightmapMaxSize = set }.lightmapMaxSize);

        [Theory]
        [InlineData(48, 64)]
        [InlineData(47, 32)]
        [InlineData(300, 256)]
        [InlineData(200, 256)]
        [InlineData(0, 1)]
        [InlineData(-3, 1)]
        [InlineData(100000, 131072)]
        [InlineData(int.MaxValue, 1 << 30)]
        public void SampleCountsAreTheClosestPowerOfTwo(int set, int read)
        {
            var s = new LightingSettings { directSampleCount = set, indirectSampleCount = set, environmentSampleCount = set };
            Assert.Equal((read, read, read), (s.directSampleCount, s.indirectSampleCount, s.environmentSampleCount));
        }

        [Fact]
        public void SettersClampAsTheEditorDoes()
        {
            var s = new LightingSettings();
            s.lightmapPadding = -3; Assert.Equal(2, s.lightmapPadding);
            s.lightmapPadding = 300; Assert.Equal(100, s.lightmapPadding);
            s.lightmapResolution = -1f; Assert.Equal(0.0001f, s.lightmapResolution);
            s.lightmapResolution = 50000f; Assert.Equal(50000f, s.lightmapResolution);
            s.indirectResolution = -1f; Assert.Equal(0.0001f, s.indirectResolution);
            s.indirectScale = -2f; Assert.Equal(0f, s.indirectScale);
            s.indirectScale = 100f; Assert.Equal(5f, s.indirectScale);
            s.albedoBoost = 0.25f; Assert.Equal(1f, s.albedoBoost);
            s.albedoBoost = 50f; Assert.Equal(10f, s.albedoBoost);
            s.albedoBoost = float.NaN; Assert.True(float.IsNaN(s.albedoBoost));
            s.aoMaxDistance = -1f; Assert.Equal(0f, s.aoMaxDistance);
            s.aoExponentDirect = 20f; Assert.Equal(10f, s.aoExponentDirect);
            s.aoExponentIndirect = -1f; Assert.Equal(0f, s.aoExponentIndirect);
            s.lightProbeSampleCountMultiplier = 0f; Assert.Equal(0.015625f, s.lightProbeSampleCountMultiplier);
            s.filteringGaussianRadiusDirect = 9f; Assert.Equal(5f, s.filteringGaussianRadiusDirect);
            s.filteringAtrousPositionSigmaDirect = 9f; Assert.Equal(2f, s.filteringAtrousPositionSigmaDirect);
            s.filteringAtrousPositionSigmaAO = -1f; Assert.Equal(0f, s.filteringAtrousPositionSigmaAO);
            // Enums: an unknown mixed mode is Shadowmask, any directionality but combined is
            // non-directional, the obsolete backend is the CPU one; the filter kinds keep anything.
            s.mixedBakeMode = MixedLightingMode.Subtractive; Assert.Equal(MixedLightingMode.Subtractive, s.mixedBakeMode);
            s.mixedBakeMode = (MixedLightingMode)7; Assert.Equal(MixedLightingMode.Shadowmask, s.mixedBakeMode);
            s.mixedBakeMode = (MixedLightingMode)(-1); Assert.Equal(MixedLightingMode.Shadowmask, s.mixedBakeMode);
            s.directionalityMode = (LightmapsMode)5; Assert.Equal(LightmapsMode.NonDirectional, s.directionalityMode);
            s.lightmapper = 0; Assert.Equal(LightingSettings.Lightmapper.ProgressiveCPU, s.lightmapper);
            s.lightmapper = (LightingSettings.Lightmapper)5; Assert.Equal(5, (int)s.lightmapper);
            s.lightmapCompression = (LightmapCompression)9; Assert.Equal(LightmapCompression.HighQuality, s.lightmapCompression);
            s.sampling = (LightingSettings.Sampling)4; Assert.Equal(LightingSettings.Sampling.Fixed, s.sampling);
            s.filteringMode = (LightingSettings.FilterMode)7; Assert.Equal(7, (int)s.filteringMode);
        }

#pragma warning disable 618
        [Fact]
        public void BouncesKeepTheMinimumAtMostTheMaximum()
        {
            var s = new LightingSettings { maxBounces = 2, minBounces = 9 };
            Assert.Equal((2, 2), (s.minBounces, s.maxBounces));
            s.maxBounces = 20; s.minBounces = 9;
            Assert.Equal((9, 20), (s.minBounces, s.maxBounces));
            s.maxBounces = 1;
            Assert.Equal((1, 1), (s.minBounces, s.maxBounces));
            s.maxBounces = 1000; s.minBounces = -4;
            Assert.Equal((0, 100), (s.minBounces, s.maxBounces));
            s.bounces = 3; s.russianRouletteStartBounce = 2;
            Assert.Equal((2, 3), (s.minBounces, s.maxBounces));
        }

        [Fact]
        public void ObsoleteMembersMapToTheirReplacements()
        {
            var s = new LightingSettings { compressLightmaps = false };
            Assert.Equal(LightmapCompression.None, s.lightmapCompression);
            s.compressLightmaps = true;
            Assert.Equal(LightmapCompression.HighQuality, s.lightmapCompression);
            s.autoGenerate = true;
            Assert.False(s.autoGenerate);
            s.filteringGaussianRadiusDirect = 2.7f;
            Assert.Equal(2, s.filteringGaussRadiusDirect);
            s.filteringGaussRadiusIndirect = 3;
            Assert.Equal(3f, s.filteringGaussianRadiusIndirect);
            s.trainingDataDestination = null;
            Assert.Equal("TrainingData", s.trainingDataDestination);
        }
#pragma warning restore 618

        [Theory]
        [InlineData(false, false, false, true)]
        [InlineData(false, true, false, true)]
        [InlineData(true, false, true, false)]
        [InlineData(true, true, false, false)]
        [InlineData(true, true, true, true)]
        public void EnvironmentLightingIsRealtimeWithoutBakedGIOrWhenChosenWithRealtimeGI(bool baked, bool realtime, bool set, bool read)
        {
            var s = new LightingSettings { bakedGI = baked, realtimeGI = realtime, realtimeEnvironmentLighting = set };
            Assert.Equal(read, s.realtimeEnvironmentLighting);
        }

        const string k_Custom = @"%YAML 1.1
--- !u!850595691 &4890085278179872738
LightingSettings:
  m_ObjectHideFlags: 0
  m_Name: Custom
  serializedVersion: 9
  m_EnableBakedLightmaps: 0
  m_EnableRealtimeLightmaps: 1
  m_RealtimeEnvironmentLighting: 0
  m_AlbedoBoost: 2.5
  m_IndirectOutputScale: 1.75
  m_BakeBackend: 2
  m_LightmapMaxSize: 256
  m_BakeResolution: 12.5
  m_Padding: 5
  m_LightmapCompression: 1
  m_AO: 1
  m_AOMaxDistance: 2.25
  m_CompAOExponent: 0.75
  m_CompAOExponentDirect: 0.5
  m_ExtractAO: 1
  m_MixedBakeMode: 1
  m_LightmapsBakeMode: 0
  m_RealtimeResolution: 0.5
  m_PVRCulling: 0
  m_PVRSampling: 1
  m_PVRDirectSampleCount: 64
  m_PVRSampleCount: 256
  m_PVREnvironmentSampleCount: 256
  m_LightProbeSampleCountMultiplier: 2
  m_PVRBounces: 4
  m_PVRMinBounces: 1
  m_PVREnvironmentImportanceSampling: 0
  m_PVRFilteringMode: 2
  m_PVRDenoiserTypeDirect: 0
  m_PVRFilterTypeDirect: 1
  m_PVRFilterTypeIndirect: 2
  m_PVRFilteringGaussRadiusDirect: 2.5
  m_PVRFilteringAtrousPositionSigmaIndirect: 1.5
  m_RespectSceneVisibilityWhenBakingGI: 1
";

        [Fact]
        public void AnAssetReadsItsValuesAndCopiesKeepThem()
        {
            var s = Read<LightingSettings>(k_Custom);
            Assert.False(s.bakedGI);
            Assert.True(s.realtimeGI);
            Assert.True(s.realtimeEnvironmentLighting);
            Assert.Equal(MixedLightingMode.Subtractive, s.mixedBakeMode);
            Assert.Equal((2.5f, 1.75f), (s.albedoBoost, s.indirectScale));
            Assert.Equal(LightingSettings.Lightmapper.ProgressiveGPU, s.lightmapper);
            Assert.Equal((256, 12.5f, 5), (s.lightmapMaxSize, s.lightmapResolution, s.lightmapPadding));
            Assert.Equal(LightmapCompression.LowQuality, s.lightmapCompression);
            Assert.True(s.ao && s.extractAO);
            Assert.Equal((2.25f, 0.75f, 0.5f), (s.aoMaxDistance, s.aoExponentIndirect, s.aoExponentDirect));
            Assert.Equal(LightmapsMode.NonDirectional, s.directionalityMode);
            Assert.Equal(0.5f, s.indirectResolution);
            Assert.False(s.prioritizeView);
            Assert.Equal((64, 256, 256), (s.directSampleCount, s.indirectSampleCount, s.environmentSampleCount));
            Assert.Equal((1, 4), (s.minBounces, s.maxBounces));
            Assert.False(s.environmentImportanceSampling);
            Assert.Equal(LightingSettings.FilterMode.Advanced, s.filteringMode);
            Assert.Equal(LightingSettings.DenoiserType.None, s.denoiserTypeDirect);
            Assert.Equal(LightingSettings.DenoiserType.Optix, s.denoiserTypeIndirect);
            Assert.Equal((LightingSettings.FilterType.ATrous, LightingSettings.FilterType.None), (s.filterTypeDirect, s.filterTypeIndirect));
            Assert.Equal(2.5f, s.filteringGaussianRadiusDirect);
            Assert.Equal(1.5f, s.filteringAtrousPositionSigmaIndirect);
            Assert.True(s.respectSceneVisibilityWhenBakingGI);

            s.name = "Custom";
            var copy = (LightingSettings)Object.Instantiate(s);
            Assert.Equal("Custom(Clone)", copy.name);
            Assert.NotEqual(s.GetInstanceID(), copy.GetInstanceID());
            Assert.Equal((256, 4), (copy.lightmapMaxSize, copy.maxBounces));
            copy.maxBounces = 7;
            Assert.Equal(4, s.maxBounces);
        }

        [Fact]
        public void AnOlderAssetKeepsTheDefaultsOfKeysItLacks()
        {
            var s = Read<LightingSettings>("--- !u!850595691 &1\nLightingSettings:\n  m_Name: Old\n  serializedVersion: 2\n  m_EnableRealtimeLightmaps: 1\n  m_PVREnvironmentMIS: 0\n");
            Assert.True(s.bakedGI && s.realtimeGI);
            Assert.False(s.environmentImportanceSampling);
            Assert.Equal((1024, 512), (s.lightmapMaxSize, s.indirectSampleCount));
            Assert.Equal(LightmapsMode.CombinedDirectional, s.directionalityMode);
        }

        // ---------------------------------------------------------------- Lightmapping

        [Fact]
        public void TheActiveScenesSettingsFollowItsLoadAndAssignments()
        {
            using var scenes = new LightProbesTests.Scenes();
            var plain = scenes.Loaded("Plain");
            var error = Assert.Throws<Exception>(() => Lightmapping.lightingSettings);
            Assert.Equal("Lightmapping.lightingSettings is null. Please assign it to an existing asset or a new instance. ", error.Message);
            Assert.False(Lightmapping.TryGetLightingSettings(out var none));
            Assert.Null(none);
            Assert.Null(Lightmapping.GetLightingSettingsForScene(new Scene(plain.Handle)));
            // Without settings the statics read the defaults, one shared object.
            Assert.Same(Lightmapping.lightingSettingsDefaults, Lightmapping.lightingSettingsDefaults);
            Assert.True(Lightmapping.bakedGI);
            Assert.False(Lightmapping.realtimeGI);

            // A scene's LightmapSettings reference its settings.
            var custom = Read<LightingSettings>(k_Custom);
            var resolver = new Resolver();
            resolver.Objects[("a7b2584dec0792e14bc2d9240f410281", 4890085278179872738)] = custom;
            var lit = scenes.Loaded("Lit");
            SceneSettings.Apply(UnityYaml.Parse("--- !u!157 &3\nLightmapSettings:\n  m_LightingDataAsset: {fileID: 0}\n" +
                "  m_LightingSettings: {fileID: 4890085278179872738, guid: a7b2584dec0792e14bc2d9240f410281, type: 2}\n")[0], resolver, lit);
            Assert.Same(custom, Lightmapping.GetLightingSettingsForScene(new Scene(lit.Handle)));
            Assert.Throws<Exception>(() => Lightmapping.lightingSettings);
            SceneRegistry.Active = lit;
            Assert.Same(custom, Lightmapping.lightingSettings);
            Assert.True(Lightmapping.TryGetLightingSettings(out var found) && found == custom);
            Assert.False(Lightmapping.bakedGI);
            Assert.True(Lightmapping.realtimeGI);

            // Null takes them away without an exception.
            Lightmapping.lightingSettings = null;
            Assert.False(Lightmapping.TryGetLightingSettings(out _));
            Lightmapping.SetLightingSettingsForScenes(new[] { new Scene(plain.Handle), new Scene(lit.Handle) }, custom);
            Assert.Same(custom, Lightmapping.GetLightingSettingsForScene(new Scene(plain.Handle)));
            Lightmapping.SetLightingSettingsForScene(new Scene(lit.Handle), null);
            Assert.Null(Lightmapping.GetLightingSettingsForScene(new Scene(lit.Handle)));

            // Setting a static on a scene without settings gives it new ones.
            Lightmapping.bakedGI = false;
            Assert.True(Lightmapping.TryGetLightingSettings(out var made));
            Assert.NotSame(custom, made);
            Assert.Equal("", made.name);
            Assert.False(made.bakedGI);
            Assert.True(Lightmapping.lightingSettingsDefaults.bakedGI);

            // Destroyed settings are none.
            Object.DestroyImmediate(made);
            Assert.False(Lightmapping.TryGetLightingSettings(out _));
        }

        [Fact]
        public void SettingsEmbeddedInTheSceneFileAreItsSettings()
        {
            using var scenes = new LightProbesTests.Scenes();
            var scene = scenes.Loaded("Embedded");
            var documents = UnityYaml.Parse("--- !u!157 &3\nLightmapSettings:\n  m_LightingDataAsset: {fileID: 0}\n" +
                "  m_LightingSettings: {fileID: 7}\n" + k_Custom.Substring(k_Custom.IndexOf("---", StringComparison.Ordinal)).Replace("&4890085278179872738", "&7"));
            var file = ObjectGraphBuilder.Build(documents, "Assets/Embedded.unity", null, ObjectGraphBuilder.Kind.Scene, scene);
            foreach (var doc in documents) SceneSettings.Apply(doc, file, scene);
            var settings = Lightmapping.lightingSettings;
            Assert.Same(file.Objects[7], settings);
            Assert.Equal("Custom", settings.name);
            Assert.Equal(256, settings.lightmapMaxSize);
        }

        sealed class WarningCapture : ILogHandler, IDisposable
        {
            readonly ILogHandler m_Inner = Debug.unityLogger.logHandler;
            internal readonly List<string> Warnings = new List<string>();

            internal WarningCapture() { Debug.unityLogger.logHandler = this; }

            public void LogFormat(LogType type, Object context, string format, params object[] args)
            {
                if (type == LogType.Warning) Warnings.Add(args == null || args.Length == 0 ? format : string.Format(format, args));
                m_Inner.LogFormat(type, context, format, args);
            }

            public void LogException(Exception exception, Object context) => m_Inner.LogException(exception, context);
            public void Dispose() => Debug.unityLogger.logHandler = m_Inner;
        }

        static JsonElement LightingData(string renderers)
        {
            using var document = JsonDocument.Parse("{\"lightmapsMode\":1,\"lightmaps\":[],\"renderers\":[" + renderers + "]}");
            return document.RootElement.Clone();
        }

        static string Renderer(long id, int indexDynamic) =>
            "{\"object\":" + id + ",\"prefab\":0,\"index\":65535,\"st\":[1,1,0,0],\"indexDynamic\":" + indexDynamic + ",\"stDynamic\":[1,1,0,0]}";

        /// <summary>A renderer with a realtime lightmap (precomputed realtime GI) draws with it:
        /// unity_DynamicLightmap and its realtime scale and offset, DYNAMICLIGHTMAP_ON, and no
        /// light probes; with a baked lightmap too it draws with both. Without a realtime
        /// lightmap index nothing is bound.</summary>
        [Fact]
        public void RealtimeLightmappedRenderersDrawWithTheirRealtimeLightmap()
        {
            var previous = LightmapSettings.lightmaps;
            var go = new GameObject("Realtime");
            var atlas = new Texture2D(8, 8, TextureFormat.RGBAHalf, false, true);
            try
            {
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.m_RealtimeLightmap = atlas;
                renderer.realtimeLightmapIndex = 0;
                renderer.realtimeLightmapScaleOffset = new Vector4(0.25f, 0.5f, 0.125f, 0f);
                var keywords = new List<string>();
                var properties = BakedLighting.Apply(renderer, null, keywords);
                Assert.Equal(new[] { "DYNAMICLIGHTMAP_ON" }, keywords);
                Assert.Same(atlas, properties[Shader.PropertyToID("unity_DynamicLightmap")]);
                Assert.Equal(new Vector4(0.25f, 0.5f, 0.125f, 0f), (Vector4)properties[Shader.PropertyToID("unity_DynamicLightmapST")]);
                Assert.False(properties.ContainsKey(Shader.PropertyToID("unity_SHAr")));

                LightmapSettings.lightmaps = new[] { new LightmapData { lightmapColor = new Texture2D(4, 4, TextureFormat.RGBAHalf, false) } };
                renderer.lightmapIndex = 0;
                keywords.Clear();
                properties = BakedLighting.Apply(renderer, null, keywords);
                Assert.Equal(new[] { "DYNAMICLIGHTMAP_ON", "LIGHTMAP_ON" }, keywords);
                Assert.Same(atlas, properties[Shader.PropertyToID("unity_DynamicLightmap")]);

                renderer.lightmapIndex = -1;
                renderer.realtimeLightmapIndex = -1;
                keywords.Clear();
                BakedLighting.Apply(renderer, null, keywords, probes: false);
                Assert.Empty(keywords);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(atlas);
                LightmapSettings.lightmaps = previous;
            }
        }

        /// <summary>With directional lightmaps a renderer whose realtime lightmap has a
        /// directionality draws with it too: unity_DynamicDirectionality and
        /// DIRLIGHTMAP_COMBINED, once whichever lightmaps it has. In the non-directional mode,
        /// or before the directionality exists, it draws as without one.</summary>
        [Fact]
        public void DirectionalRealtimeLightmapsBindTheirDirectionality()
        {
            var previous = LightmapSettings.lightmaps;
            var mode = LightmapSettings.lightmapsMode;
            var go = new GameObject("Directional");
            var atlas = new Texture2D(8, 8, TextureFormat.RGBAHalf, false, true);
            var directionality = new Texture2D(8, 8, TextureFormat.RGBA32, false, true);
            var bakedDirection = new Texture2D(4, 4, TextureFormat.RGBA32, false, true);
            int id = Shader.PropertyToID("unity_DynamicDirectionality");
            try
            {
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.m_RealtimeLightmap = atlas;
                renderer.realtimeLightmapIndex = 0;
                var keywords = new List<string>();

                LightmapSettings.lightmapsMode = LightmapsMode.CombinedDirectional;
                var properties = BakedLighting.Apply(renderer, null, keywords);
                Assert.Equal(new[] { "DYNAMICLIGHTMAP_ON" }, keywords);
                Assert.False(properties.ContainsKey(id));

                renderer.m_RealtimeDirectionality = directionality;
                keywords.Clear();
                properties = BakedLighting.Apply(renderer, null, keywords);
                Assert.Equal(new[] { "DYNAMICLIGHTMAP_ON", "DIRLIGHTMAP_COMBINED" }, keywords);
                Assert.Same(directionality, properties[id]);

                LightmapSettings.lightmaps = new[] { new LightmapData
                {
                    lightmapColor = new Texture2D(4, 4, TextureFormat.RGBAHalf, false),
                    lightmapDir = bakedDirection,
                } };
                renderer.lightmapIndex = 0;
                keywords.Clear();
                properties = BakedLighting.Apply(renderer, null, keywords);
                Assert.Equal(new[] { "DYNAMICLIGHTMAP_ON", "DIRLIGHTMAP_COMBINED", "LIGHTMAP_ON" }, keywords);
                Assert.Same(directionality, properties[id]);
                Assert.Same(bakedDirection, properties[Shader.PropertyToID("unity_LightmapInd")]);

                LightmapSettings.lightmapsMode = LightmapsMode.NonDirectional;
                keywords.Clear();
                properties = BakedLighting.Apply(renderer, null, keywords);
                Assert.Equal(new[] { "DYNAMICLIGHTMAP_ON", "LIGHTMAP_ON" }, keywords);
                Assert.False(properties.ContainsKey(id));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(atlas);
                UnityEngine.Object.DestroyImmediate(directionality);
                UnityEngine.Object.DestroyImmediate(bakedDirection);
                LightmapSettings.lightmaps = previous;
                LightmapSettings.lightmapsMode = mode;
            }
        }

        [Fact]
        public void TheDoctorChecksSettingsTogetherWithSceneLightingData()
        {
            var coverage = ClassCoverage.Report("6000.0.71f1", new[] {
                new ClassCoverage.Found { ClassId = 850595691, TypeName = "LightingSettings" },
                new ClassCoverage.Found { ClassId = 200, TypeName = "ShaderVariantCollection" },
            });
            Assert.All(coverage.Classes, entry => Assert.Equal("Read", entry.Reading));
            JsonElement Body(string text)
            {
                using var document = JsonDocument.Parse(text);
                return document.RootElement.Clone();
            }
            var results = FieldCoverage.Report(new List<FieldCoverage.Input> {
                new FieldCoverage.Input { ClassId = 850595691, FileId = 1, Body = Body("{\"m_EnableRealtimeLightmaps\":\"1\"}") },
                new FieldCoverage.Input { ClassId = 850595691, FileId = 2, Body = Body("{}") },
                new FieldCoverage.Input { ClassId = 157, FileId = 3, Body = Body("{\"m_LightingDataAsset\":{\"fileID\":\"42\",\"guid\":\"abc\"},\"m_LightingSettings\":{\"fileID\":\"1\"}}") },
            });
            Assert.All(results, result => Assert.Null(result.Error));
            Assert.True(results[0].Lighting.RealtimeGI);
            Assert.False(results[1].Lighting.RealtimeGI);
            Assert.Contains("m_EnableRealtimeLightmaps", results[0].Consumed);
            Assert.Equal("abc", results[2].Lighting.DataGuid);
            Assert.Equal(42, results[2].Lighting.DataId);
            Assert.Null(results[2].Lighting.SettingsGuid);
            Assert.Equal(1, results[2].Lighting.SettingsId);
            Assert.Equal(2, BakedLighting.RealtimeContributors(LightingData(
                Renderer(1, -1) + "," + Renderer(2, 0) + "," + Renderer(3, 65533) + "," + Renderer(4, 65534) + "," + Renderer(5, 65535))));
        }

        // ---------------------------------------------------------------- ShaderVariantCollection

        static ShaderVariantCollection.ShaderVariant V(Shader shader, PassType pass, params string[] keywords) =>
            new ShaderVariantCollection.ShaderVariant { shader = shader, passType = pass, keywords = keywords };

        static string State(ShaderVariantCollection c) => $"{c.shaderCount} {c.variantCount} {c.warmedUpVariantCount} {c.isWarmedUp}";

        [Fact]
        public void QueriesKeepDeclaredKeywordsOnceInNameOrder()
        {
            var shader = Declared(new[] { null, "FORWARDBASE" }, new[] { "PLAIN_ON", "RED_ON", "DIRECTIONAL", "CASTER_ON" });
            var unlit = Declared(new string[] { null }, new string[0]);
            var c = new ShaderVariantCollection();
            Assert.Equal("0 0 0 False", State(c));
            Assert.True(c.Add(V(shader, PassType.Normal, "PLAIN_ON", "RED_ON")));
            Assert.False(c.Add(V(shader, PassType.Normal, "RED_ON", "PLAIN_ON")));
            Assert.True(c.Add(V(shader, PassType.Normal, "RED_ON", "RED_ON")));
            Assert.True(c.Contains(V(shader, PassType.Normal, "RED_ON")));
            // An undeclared keyword is dropped: the variant without keywords.
            Assert.True(c.Add(V(shader, PassType.Normal, "NOPE")));
            Assert.True(c.Contains(V(shader, PassType.Normal)));
            Assert.True(c.Contains(V(shader, PassType.Normal, (string[])null)));
            Assert.False(c.Add(V(shader, PassType.Normal, "RED_ON PLAIN_ON")));
            // A keyword of another pass of the shader is kept; pass types are not checked.
            Assert.True(c.Add(V(shader, PassType.Normal, "CASTER_ON")));
            Assert.True(c.Add(V(shader, PassType.Deferred)));
            Assert.True(c.Add(V(unlit, PassType.Normal, null)));
            Assert.False(c.Add(V(null, PassType.Normal)));
            Assert.False(c.Add(default));
            Assert.Equal("2 6 0 False", State(c));
            Assert.False(c.Remove(V(shader, PassType.ForwardBase, "DIRECTIONAL")));
            Assert.True(c.Remove(V(shader, PassType.Normal, "RED_ON", "PLAIN_ON")));
            Assert.False(c.Remove(V(null, PassType.Normal)));
            // A shader stays counted without variants, until Clear.
            Assert.True(c.Remove(V(unlit, PassType.Normal)));
            Assert.Equal("2 4 0 False", State(c));
            c.Clear();
            Assert.Equal("0 0 0 False", State(c));
        }

        [Fact]
        public void WithoutDeclarationsQueriesKeepEveryKeywordName()
        {
            var shader = Declared(new string[] { null }, new string[0], known: false);
            var c = new ShaderVariantCollection();
            Assert.True(c.Add(V(shader, PassType.Normal, "B", "A", "", "A")));
            Assert.True(c.Contains(V(shader, PassType.Normal, "A", "B")));
            // Not a keyword name either way.
            Assert.True(c.Contains(V(shader, PassType.Normal, "A", "B", "A B")));
            Assert.False(c.Contains(V(shader, PassType.Normal, "A")));
        }

        [Fact]
        public void PassSourcesBoundCyclesAndPreferAnOwnPass()
        {
            var a = Declared(new[] { "ForwardBase" }, Array.Empty<string>());
            var b = Declared(new[] { "ForwardBase" }, Array.Empty<string>());
            Shader.Register(a); Shader.Register(b);
            string Info(string name, string fallback, string mode) => JsonSerializer.Serialize(new {
                shader = name, found = true, tags = new Dictionary<string, string>(), fallback,
                passes = new[] { new { name = "Test", tags = new Dictionary<string, string> { ["LightMode"] = mode } } }
            });
            ShaderPassInfo.s_CallForTests = name => Info(name, name == a.name ? b.name : a.name, "ForwardBase");
            int visits = 0;
            Assert.Null(ShaderPassInfo.FindPassSource(a, info => { visits++; return info.ShadowCasterPass >= 0; }, out _));
            Assert.InRange(visits, 2, 16);
            ShaderPassInfo.ResetForTests();
            int fallbackQueries = 0;
            ShaderPassInfo.s_CallForTests = name => {
                if (name != a.name) fallbackQueries++;
                return Info(name, b.name, "ShadowCaster");
            };
            Assert.Same(a, ShaderPassInfo.FindPassSource(a, info => info.ShadowCasterPass >= 0, out var selected));
            Assert.Equal(0, selected.ShadowCasterPass);
            Assert.Equal(0, fallbackQueries);
        }

        [Fact]
        public void WarmupPreparesOnlyNewVariantsAndSaturatesWithoutOverflow()
        {
            var shader = Declared(new string[] { null }, new[] { "A", "B", "C" });
            var c = new ShaderVariantCollection();
            Assert.True(c.WarmUpProgressively(1));
            Assert.False(c.isWarmedUp);
            c.WarmUp();
            Assert.True(c.isWarmedUp);
            foreach (var k in new[] { "A", "B", "C" }) c.Add(V(shader, PassType.Normal, k));
            var prepared = new List<string>();
            ShaderWarmup.s_PrepareForTests = (s, p, k) => { Assert.Same(shader, s); Assert.Equal(PassType.Normal, p); prepared.Add(string.Join(" ", k)); };
            Assert.False(c.WarmUpProgressively(1));
            Assert.Single(prepared);
            Assert.True(c.WarmUpProgressively(int.MaxValue));
            Assert.Equal(3, prepared.Count);
            Assert.Equal(3, c.warmedUpVariantCount);
            c.WarmUp();
            Assert.True(c.WarmUpProgressively(int.MaxValue));
            Assert.Equal(3, prepared.Count);
            Assert.True(c.Remove(V(shader, PassType.Normal, "B")));
            c.WarmUp();
            Assert.Equal(5, prepared.Count);
        }

        [Fact]
        public void FailedPreparationDoesNotClaimACompletedVariant()
        {
            var shader = Declared(new string[] { null }, new[] { "A" });
            var c = new ShaderVariantCollection();
            c.Add(V(shader, PassType.Normal, "A"));
            ShaderWarmup.s_PrepareForTests = (_, _, _) => throw new NotSupportedException("unsupported test program");
            Assert.Throws<NotSupportedException>(() => c.WarmUp());
            Assert.Equal(0, c.warmedUpVariantCount);
            Assert.False(c.isWarmedUp);
            ShaderWarmup.s_PrepareForTests = (_, _, _) => { };
            c.WarmUp();
            Assert.True(c.isWarmedUp);
        }

        [Fact]
        public void WarmingUpCountsVariantsAndAnyChangeMakesThemCold()
        {
            var shader = Declared(new string[] { null }, new[] { "A", "B", "C" });
            var c = new ShaderVariantCollection();
            Assert.True(c.WarmUpProgressively(1));
            Assert.False(c.isWarmedUp);
            foreach (var k in new[] { "A", "B", "C" }) c.Add(V(shader, PassType.Normal, k));
            Assert.False(c.WarmUpProgressively(2));
            Assert.False(c.WarmUpProgressively(0));
            Assert.False(c.WarmUpProgressively(-1));
            Assert.Equal("1 3 2 False", State(c));
            Assert.True(c.WarmUpProgressively(100));
            Assert.Equal("1 3 3 True", State(c));
            Assert.True(c.WarmUpProgressively(1));
            // A change that does nothing keeps the warm state; one that does makes all cold.
            Assert.False(c.Add(V(shader, PassType.Normal, "A")));
            Assert.Equal("1 3 3 True", State(c));
            Assert.True(c.Add(V(shader, PassType.Normal, "A", "B")));
            Assert.Equal("1 4 0 False", State(c));
            c.WarmUp();
            Assert.True(c.Remove(V(shader, PassType.Normal, "B", "A")));
            Assert.Equal("1 3 0 False", State(c));
            c.WarmUp();
            var copy = (ShaderVariantCollection)Object.Instantiate(c);
            Assert.Equal("1 3 0 False", State(copy));
            Assert.True(copy.Contains(V(shader, PassType.Normal, "C")));
            copy.Clear();
            Assert.Equal("1 3 3 True", State(c));
        }

        [Fact]
        public void AnAssetKeepsItsKeywordsAsWrittenAndCountsMissingShaders()
        {
            var shader = Declared(new string[] { null, "FORWARDBASE" }, new[] { "PLAIN_ON", "RED_ON", "DIRECTIONAL", "BLUE_ON" });
            var standard = Declared(new[] { "FORWARDBASE" }, new[] { "DIRECTIONAL" });
            var resolver = new Resolver();
            resolver.Objects[("c12632d3c5d2d835ebafe44796be05fb", 4800000)] = shader;
            resolver.Objects[("0000000000000000f000000000000000", 46)] = standard;
            var c = Read<ShaderVariantCollection>(@"--- !u!200 &20000000
ShaderVariantCollection:
  m_Name: Authored
  m_Shaders:
  - first: {fileID: 46, guid: 0000000000000000f000000000000000, type: 0}
    second:
      variants: []
  - first: {fileID: 4800000, guid: c12632d3c5d2d835ebafe44796be05fb, type: 3}
    second:
      variants:
      - keywords: RED_ON PLAIN_ON
        passType: 0
      - keywords:
        passType: 0
      - keywords: RED_ON PLAIN_ON
        passType: 0
      - keywords: PLAIN_ON RED_ON
        passType: 0
      - keywords: DIRECTIONAL BLUE_ON
        passType: 4
  - first: {fileID: 4800000, guid: 0123456789abcdef0123456789abcdef, type: 3}
    second:
      variants:
      - keywords:
        passType: 0
      - keywords: A B
        passType: 4
", resolver);
            Assert.Equal("3 6 0 False", State(c));
            Assert.True(c.Contains(V(shader, PassType.Normal, "RED_ON", "PLAIN_ON")));
            Assert.True(c.Contains(V(shader, PassType.Normal, "NOPE")));
            Assert.False(c.Contains(V(shader, PassType.ForwardBase, "DIRECTIONAL", "BLUE_ON")));
            Assert.False(c.Contains(V(standard, PassType.ForwardBase)));
            // The sorted one goes; the one written out of order stays (no query names it).
            Assert.True(c.Remove(V(shader, PassType.Normal, "PLAIN_ON", "RED_ON")));
            Assert.False(c.Remove(V(shader, PassType.Normal, "PLAIN_ON", "RED_ON")));
            Assert.Equal("3 5 0 False", State(c));
            c.WarmUp();
            Assert.Equal("3 5 5 True", State(c));
        }

        [Fact]
        public void VariantKeywordsMustBelongToOnePassAndOneChoicePerSet()
        {
            var shader = Declared(new string[] { null }, new[] { "A", "B", "C", "OTHER" });
            m_Declarations[shader.name] = JsonSerializer.Serialize(new
            {
                found = true, subshaders_known = true, keywords_known = true,
                keywords = new[] { "A", "B", "C", "OTHER" }.Select(name => new { name, local = false, dynamic = false }),
                subshaders = new[] { new { tags = new string[0][], passes = new[] {
                    new { name = "", tags = new string[0][], keyword_sets = new[] { new[] { "_", "A", "B" } } },
                    new { name = "", tags = new string[0][], keyword_sets = new[] { new[] { "_", "C" } } },
                    new { name = "", tags = new[] { new[] { "LIGHTMODE", "FORWARDBASE" } }, keyword_sets = new[] { new[] { "OTHER" } } },
                } } },
            });
            foreach (var keys in new[] { new string[0], new[] { "A" }, new[] { "B" }, new[] { "C" }, new[] { "_" }, new[] { "", "", null } })
                new ShaderVariantCollection.ShaderVariant(shader, PassType.Normal, keys);
            foreach (var keys in new[] { new[] { "A", "B" }, new[] { "A", "C" }, new[] { "_", "A" }, new[] { "A", "A" }, new[] { "OTHER" } })
                Assert.Equal($"passed shader keyword variant not found in shader {shader.name} pass type 0",
                    Assert.Throws<ArgumentException>(() => new ShaderVariantCollection.ShaderVariant(shader, PassType.Normal, keys)).Message);
            new ShaderVariantCollection.ShaderVariant(shader, PassType.ForwardBase, "OTHER");
            Assert.Throws<ArgumentException>(() => new ShaderVariantCollection.ShaderVariant(shader, PassType.ForwardBase, "A"));
        }

        [Fact]
        public void TheVariantConstructorChecksWhatTheDeclarationsTell()
        {
            var shader = Declared(new[] { null, "FORWARDBASE", "SHADOWCASTER" }, new[] { "PLAIN_ON", "RED_ON", "DIRECTIONAL", "SHADOWS_DEPTH" });
            var made = new ShaderVariantCollection.ShaderVariant(shader, PassType.Normal, "RED_ON", "PLAIN_ON");
            Assert.Equal(new[] { "RED_ON", "PLAIN_ON" }, made.keywords);
            Assert.Null(new ShaderVariantCollection.ShaderVariant(shader, PassType.ShadowCaster, null).keywords);
            new ShaderVariantCollection.ShaderVariant(shader, PassType.Normal, "");
            new ShaderVariantCollection.ShaderVariant(shader, PassType.ForwardBase);
            string Fails(Shader s, PassType p, params string[] k) =>
                Assert.Throws<ArgumentException>(() => new ShaderVariantCollection.ShaderVariant(s, p, k)).Message;
            Assert.Equal("null shader", Fails(null, PassType.Normal));
            Assert.Equal($"shader {shader.name} does not have pass type 10", Fails(shader, PassType.Deferred));
            Assert.Equal($"shader {shader.name} does not have pass type 14", Fails(shader, PassType.ScriptableRenderPipelineDefaultUnlit));
            Assert.Equal($"passed shader keyword variant not found in shader {shader.name} pass type 0", Fails(shader, PassType.Normal, "NOPE"));
            Assert.Equal($"passed shader keyword variant not found in shader {shader.name} pass type 0", Fails(shader, PassType.Normal, "RED_ON", "RED_ON"));
            Assert.Equal($"passed shader keyword variant not found in shader {shader.name} pass type 0", Fails(shader, PassType.Normal, "RED_ON PLAIN_ON"));
            // Unknown declarations check nothing but the shader.
            var unknown = Declared(new string[] { null }, new string[0], known: false);
            new ShaderVariantCollection.ShaderVariant(unknown, PassType.Deferred, "NOPE", "NOPE");
            // A render pipeline's light mode is ScriptableRenderPipeline.
            var srp = Declared(new[] { "UniversalForward", "SRPDefaultUnlit" }, new string[0]);
            new ShaderVariantCollection.ShaderVariant(srp, PassType.ScriptableRenderPipeline);
            new ShaderVariantCollection.ShaderVariant(srp, PassType.ScriptableRenderPipelineDefaultUnlit);
            Assert.Equal($"shader {srp.name} does not have pass type 0", Fails(srp, PassType.Normal));
        }
    }
}
