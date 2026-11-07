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
            ShaderDeclarations.ResetForTests();
            ShaderDeclarations.s_FrameForTests = 1;
            ShaderDeclarations.s_CallForTests = Call;
        }

        public void Dispose()
        {
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

        [Fact]
        public void PrecomputedRealtimeGIWarnsOncePerScene()
        {
            using var capture = new WarningCapture();
            var file = new LoadedFile { Path = "Assets/Scenes/LightingTestsRealtime.unity" };
            BakedLighting.Read(LightingData(Renderer(1, 65535) + "," + Renderer(2, 0) + "," + Renderer(3, 0)), file);
            BakedLighting.Read(LightingData(Renderer(2, 0)), file);
            var warning = Assert.Single(capture.Warnings, w => w.Contains("LightingTestsRealtime"));
            Assert.Contains("Enlighten Realtime Global Illumination precomputed for 2 renderers", warning);
            Assert.Contains("unbound doctor", warning);
            // Without realtime lightmaps (none precomputed) nothing is said.
            BakedLighting.Read(LightingData(Renderer(1, 65535) + "," + Renderer(2, 65534)), new LoadedFile { Path = "Assets/Scenes/LightingTestsBaked.unity" });
            Assert.DoesNotContain(capture.Warnings, w => w.Contains("LightingTestsBaked"));
        }

        [Fact]
        public void TheDoctorListsSettingsThatEnableRealtimeGI()
        {
            ClassCoverage.Entry Listed(string text, string other) => Assert.Single(ClassCoverage.Report("6000.0.71f1", new[]
            {
                new ClassCoverage.Found { ClassId = 850595691, TypeName = "LightingSettings", Keys = new List<string> { "m_Name", "m_EnableRealtimeLightmaps" },
                    Documents = new Dictionary<string, string> { ["Assets/Scenes/A.lighting"] = text, ["Assets/Scenes/B.lighting"] = other } },
            }).Classes);
            string off = k_Custom.Replace("m_EnableRealtimeLightmaps: 1", "m_EnableRealtimeLightmaps: 0");
            var realtime = Listed(k_Custom, k_Custom);
            Assert.Equal("Unsupported", realtime.Reading);
            // The reason once, after the objects that have it.
            Assert.StartsWith("Assets/Scenes/A.lighting fileID 4890085278179872738, Assets/Scenes/B.lighting fileID 4890085278179872738: Enlighten Realtime Global Illumination", realtime.Detail);
            Assert.Single(System.Text.RegularExpressions.Regex.Matches(realtime.Detail, "Realtime Global Illumination"));
            Assert.DoesNotContain("B.lighting", Listed(k_Custom, off).Detail);
            Assert.Equal("Read", Listed(off, off).Reading);
            Assert.Equal("Read", Assert.Single(ClassCoverage.Report("6000.0.71f1", new[]
            {
                new ClassCoverage.Found { ClassId = 200, TypeName = "ShaderVariantCollection", Keys = new List<string> { "m_Name", "m_Shaders" } },
            }).Classes).Reading);
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
