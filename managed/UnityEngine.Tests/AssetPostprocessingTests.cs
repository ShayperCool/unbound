using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The importers' settings that AssetPostprocessors read and change, kept in the
    /// importer's copy of its .meta section, and the order OnPostprocessAllAssets runs in
    /// (tests/projects/assetpostprocessors measures the Editor).</summary>
    [Collection("EditorState")]
    public sealed class AssetPostprocessingTests
    {
        const string TextureMeta =
            "TextureImporter:\n" +
            "  mipmaps:\n    mipMapMode: 0\n    enableMipMap: 1\n    sRGBTexture: 1\n" +
            "  isReadable: 0\n" +
            "  textureSettings:\n    serializedVersion: 2\n    filterMode: 1\n    aniso: 1\n    mipBias: 0\n    wrapU: 0\n    wrapV: 0\n    wrapW: 0\n" +
            "  nPOTScale: 1\n  spriteMode: 0\n  spritePixelsToUnits: 100\n  alphaUsage: 1\n  alphaIsTransparency: 0\n  textureType: 0\n  textureShape: 1\n" +
            "  platformSettings:\n" +
            "  - serializedVersion: 4\n    buildTarget: DefaultTexturePlatform\n    maxTextureSize: 2048\n    textureCompression: 1\n" +
            "  - serializedVersion: 4\n    buildTarget: Standalone\n    maxTextureSize: 512\n    textureCompression: 1\n" +
            "  userData: \n";

        static TextureImporter Texture() => (TextureImporter)AssetImporter.FromMeta("Assets/T.png", "0123456789abcdef0123456789abcdef", "TextureImporter",
            TestYaml.DocumentBody(TextureMeta));

        [Fact]
        public void TextureSettingsReadAndWriteTheMetaSectionTheDefaultPlatformsIncluded()
        {
            var importer = Texture();
            Assert.Equal(2048, importer.maxTextureSize);
            Assert.False(importer.m_SettingsChanged);
            importer.maxTextureSize = 32;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.isReadable = true;
            Assert.True(importer.m_SettingsChanged);
            Assert.Equal(32, importer.maxTextureSize);
            // The default platform's entry changed; Standalone's override kept its own size.
            Assert.Equal(32, MetaSettings.Int(importer.m_Settings, "platformSettings.[buildTarget=DefaultTexturePlatform].maxTextureSize"));
            Assert.Equal(512, MetaSettings.Int(importer.m_Settings, "platformSettings.[buildTarget=Standalone].maxTextureSize"));
            Assert.Equal(1, MetaSettings.Int(importer.m_Settings, "textureSettings.wrapV"));
            Assert.Equal(TextureWrapMode.Clamp, importer.wrapModeW);
            Assert.True(importer.isReadable);
            // The section's JSON keeps every key, the changed ones with their new values.
            var json = MetaSettings.ToJson(importer.m_Settings);
            Assert.Contains("\"filterMode\":\"0\"", json);
            Assert.Contains("\"buildTarget\":\"Standalone\"", json);
        }

        [Fact]
        public void ASpriteTypeSetsWhatTheTypeImplies()
        {
            // As the Editor's importer: a Default texture made a Sprite stops mip mapping, scaling to
            // powers of two and repeating, and its alpha becomes transparency.
            var importer = Texture();
            importer.textureType = TextureImporterType.Sprite;
            Assert.Equal(TextureImporterType.Sprite, importer.textureType);
            Assert.False(importer.mipmapEnabled);
            Assert.Equal(TextureImporterNPOTScale.None, importer.npotScale);
            Assert.Equal(TextureWrapMode.Clamp, importer.wrapMode);
            Assert.True(importer.alphaIsTransparency);
            // Settings the type does not imply are kept; a sprite without a mode gets Single.
            Assert.Equal(FilterMode.Bilinear, importer.filterMode);
            Assert.Equal(100, importer.spritePixelsPerUnit);
            Assert.Equal(SpriteImportMode.Single, importer.spriteImportMode);
        }

        [Fact]
        public void SettingTheTypeATextureHasChangesNothing()
        {
            // A Unity-written sprite: a preprocessor that sets the Sprite type again keeps its mode,
            // its mip maps and the file's import.
            var importer = (TextureImporter)AssetImporter.FromMeta("Assets/S.png", "4123456789abcdef0123456789abcdef", "TextureImporter",
                TestYaml.DocumentBody(TextureMeta.Replace("  spriteMode: 0\n", "  spriteMode: 1\n").Replace("  textureType: 0\n", "  textureType: 8\n")));
            var before = MetaSettings.ToJson(importer.m_Settings);
            importer.textureType = TextureImporterType.Sprite;
            Assert.Equal(before, MetaSettings.ToJson(importer.m_Settings));
            Assert.True(importer.mipmapEnabled);
            Assert.Equal(SpriteImportMode.Single, importer.spriteImportMode);
        }

        [Fact]
        public void SetTextureSettingsWritesOnlyWhatChangedIntoASparseMeta()
        {
            var importer = (TextureImporter)AssetImporter.FromMeta("Assets/P.png", "5123456789abcdef0123456789abcdef", "TextureImporter",
                TestYaml.DocumentBody("TextureImporter:\n  textureType: 0\n  userData: \n"));
            // Missing keys answer the importer's defaults.
            Assert.True(importer.mipmapEnabled);
            Assert.True(importer.sRGBTexture);
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.filterMode = FilterMode.Trilinear;
            importer.SetTextureSettings(settings);
            Assert.Equal(2, MetaSettings.Int(importer.m_Settings, "textureSettings.filterMode"));
            Assert.Null(MetaSettings.Find(importer.m_Settings, "mipmaps.sRGBTexture"));
            Assert.Null(MetaSettings.Find(importer.m_Settings, "nPOTScale"));
            // A missing key reads as the import takes it, so a value equal to a constructor default
            // but not to the import's is written: Point filtering, no NPOT scaling.
            importer.ReadTextureSettings(settings);
            Assert.Equal(TextureImporterNPOTScale.ToNearest, settings.npotScale);
            settings.filterMode = FilterMode.Point;
            settings.npotScale = TextureImporterNPOTScale.None;
            importer.SetTextureSettings(settings);
            Assert.Equal(0, MetaSettings.Int(importer.m_Settings, "textureSettings.filterMode", -1));
            Assert.Equal(0, MetaSettings.Int(importer.m_Settings, "nPOTScale", -1));
        }

        [Fact]
        public void StreamingMipmapsReadAndWriteTheMeta()
        {
            // A bake's lightmap is imported with mipmap streaming on (bakedbuffers written/).
            var importer = (TextureImporter)AssetImporter.FromMeta("Assets/L.exr", "a123456789abcdef0123456789abcdef", "TextureImporter",
                TestYaml.DocumentBody("TextureImporter:\n  streamingMipmaps: 0\n  userData: \n"));
            Assert.False(importer.streamingMipmaps);
            importer.streamingMipmaps = true;
            Assert.True(importer.streamingMipmaps);
            Assert.Equal(1, MetaSettings.Int(importer.m_Settings, "streamingMipmaps", -1));
        }

        [Fact]
        public void SetTextureSettingsWritesTheCubemapConvolutionAndSeams()
        {
            // A reflection cube's import (a bake's ReflectionProbe-0.exr): specular convolution
            // and seamless edges reach the .meta, and read back.
            var importer = (TextureImporter)AssetImporter.FromMeta("Assets/C.exr", "9123456789abcdef0123456789abcdef", "TextureImporter",
                TestYaml.DocumentBody("TextureImporter:\n  cubemapConvolution: 0\n  seamlessCubemap: 0\n  userData: \n"));
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            Assert.Equal(TextureImporterCubemapConvolution.None, settings.cubemapConvolution);
            Assert.False(settings.seamlessCubemap);
            settings.cubemapConvolution = TextureImporterCubemapConvolution.Specular;
            settings.seamlessCubemap = true;
            importer.SetTextureSettings(settings);
            Assert.Equal(1, MetaSettings.Int(importer.m_Settings, "cubemapConvolution", -1));
            Assert.Equal(1, MetaSettings.Int(importer.m_Settings, "seamlessCubemap", -1));
            var read = new TextureImporterSettings();
            importer.ReadTextureSettings(read);
            Assert.Equal(TextureImporterCubemapConvolution.Specular, read.cubemapConvolution);
            Assert.True(read.seamlessCubemap);
        }

        [Fact]
        public void ATypeChangeOnASparseMetaKeepsTheWrapModeAsked()
        {
            // A sprite without wrap keys clamps; made Default it keeps clamping (the type change
            // does not set wrap), and a Default texture made a Sprite repeats when asked to.
            var sprite = (TextureImporter)AssetImporter.FromMeta("Assets/W.png", "7123456789abcdef0123456789abcdef", "TextureImporter",
                TestYaml.DocumentBody("TextureImporter:\n  textureType: 8\n  spriteMode: 1\n  userData: \n"));
            Assert.Equal(TextureWrapMode.Clamp, sprite.wrapModeU);
            sprite.textureType = TextureImporterType.Default;
            Assert.Equal(TextureWrapMode.Clamp, sprite.wrapModeU);
            Assert.Equal(1, MetaSettings.Int(sprite.m_Settings, "textureSettings.wrapU", -1));

            var plain = (TextureImporter)AssetImporter.FromMeta("Assets/V.png", "8123456789abcdef0123456789abcdef", "TextureImporter",
                TestYaml.DocumentBody("TextureImporter:\n  textureType: 0\n  textureSettings:\n    wrapMode: 1\n  userData: \n"));
            // The older per-texture wrap mode is read for missing axes.
            Assert.Equal(TextureWrapMode.Clamp, plain.wrapModeV);
            var settings = new TextureImporterSettings();
            plain.ReadTextureSettings(settings);
            settings.textureType = TextureImporterType.Sprite;
            settings.wrapMode = TextureWrapMode.Repeat;
            plain.SetTextureSettings(settings);
            Assert.Equal(TextureWrapMode.Repeat, plain.wrapModeU);
            Assert.Equal(TextureWrapMode.Repeat, plain.wrapModeW);
        }

        [Fact]
        public void MissingKeysReadAsTheTypesDefaults()
        {
            // A sprite clamps; an older linear .meta is not sRGB; a -1 wrap is the type's default.
            var sprite = (TextureImporter)AssetImporter.FromMeta("Assets/Q.png", "6123456789abcdef0123456789abcdef", "TextureImporter",
                TestYaml.DocumentBody("TextureImporter:\n  mipmaps:\n    linearTexture: 1\n  textureSettings:\n    wrapV: -1\n  textureType: 8\n  userData: \n"));
            var settings = new TextureImporterSettings();
            sprite.ReadTextureSettings(settings);
            Assert.Equal(TextureWrapMode.Clamp, settings.wrapModeU);
            Assert.Equal(TextureWrapMode.Clamp, settings.wrapModeV);
            Assert.False(settings.sRGBTexture);
            Assert.False(sprite.sRGBTexture);
            Assert.Equal(FilterMode.Bilinear, settings.filterMode);
            Assert.Equal(SpriteMeshType.Tight, settings.spriteMeshType);
            Assert.Equal(0.5f, sprite.alphaTestReferenceValue);
            Assert.False(sprite.mipMapsPreserveCoverage);
        }

        [Fact]
        public void MinusOneReadsAsTheTypesDefaultAndAMissingWrapKeyAsTheOlderWrapMode()
        {
            // As the import reads them: a -1 is the type's default even beside an older wrapMode
            // (here Clamp), a missing wrap key takes that wrapMode.
            var plain = (TextureImporter)AssetImporter.FromMeta("Assets/R.png", "7123456789abcdef0123456789abcdef", "TextureImporter",
                TestYaml.DocumentBody("TextureImporter:\n  textureSettings:\n    filterMode: -1\n    aniso: -1\n    wrapMode: 1\n    wrapU: -1\n    wrapV: 2\n" +
                    "  mipmaps:\n    mipMapsPreserveCoverage: 1\n    alphaTestReferenceValue: 0.1\n  textureType: 0\n  userData: \n"));
            Assert.Equal(FilterMode.Bilinear, plain.filterMode);
            Assert.Equal(1, plain.anisoLevel);
            Assert.Equal(TextureWrapMode.Repeat, plain.wrapModeU);
            Assert.Equal(TextureWrapMode.Mirror, plain.wrapModeV);
            Assert.Equal(TextureWrapMode.Clamp, plain.wrapModeW);
            Assert.True(plain.mipMapsPreserveCoverage);
            Assert.Equal(0.1f, plain.alphaTestReferenceValue);
            var settings = new TextureImporterSettings();
            plain.ReadTextureSettings(settings);
            Assert.Equal(TextureWrapMode.Repeat, settings.wrapModeU);
            Assert.Equal(TextureWrapMode.Clamp, settings.wrapModeW);
            Assert.Equal(0.1f, settings.alphaTestReferenceValue);
            Assert.True(settings.mipMapsPreserveCoverage);
        }

        [Fact]
        public void ModelAndAudioSettingsReadTheirSectionsWithTheReleasesDefaults()
        {
            var model = (ModelImporter)AssetImporter.FromMeta("Assets/M.fbx", "1123456789abcdef0123456789abcdef", "ModelImporter",
                TestYaml.DocumentBody("ModelImporter:\n  materials:\n    materialImportMode: 1\n  animations:\n    isReadable: 0\n  meshes:\n    globalScale: 1\n    addColliders: 0\n  animationType: 2\n  userData: \n"));
            Assert.Equal(ModelImporterMaterialImportMode.ImportStandard, model.materialImportMode);
            // Cameras and lights import unless the section says otherwise.
            Assert.True(model.importCameras);
            Assert.True(model.importLights);
            model.globalScale = 2;
            model.addCollider = true;
            model.isReadable = true;
            Assert.Equal(2f, MetaSettings.Float(model.m_Settings, "meshes.globalScale"));
            Assert.True(MetaSettings.Bool(model.m_Settings, "meshes.addColliders"));
            Assert.True(MetaSettings.Bool(model.m_Settings, "animations.isReadable"));

            var audio = (AudioImporter)AssetImporter.FromMeta("Assets/A.wav", "2123456789abcdef0123456789abcdef", "AudioImporter",
                TestYaml.DocumentBody("AudioImporter:\n  defaultSettings:\n    loadType: 1\n    compressionFormat: 1\n    quality: 0.5\n  forceToMono: 0\n  userData: \n"));
            var settings = audio.defaultSampleSettings;
            Assert.Equal(AudioClipLoadType.CompressedInMemory, settings.loadType);
            Assert.Equal(0.5f, settings.quality);
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.PCM;
            audio.defaultSampleSettings = settings;
            audio.forceToMono = true;
            Assert.Equal(AudioClipLoadType.DecompressOnLoad, audio.defaultSampleSettings.loadType);
            Assert.Equal(0, MetaSettings.Int(audio.m_Settings, "defaultSettings.compressionFormat", -1));
            Assert.True(audio.forceToMono);
            Assert.True(audio.m_SettingsChanged);
        }

        [Fact]
        public void AnOlderModelMetaKeepsReadWriteInItsMeshesSection()
        {
            var model = (ModelImporter)AssetImporter.FromMeta("Assets/O.fbx", "3123456789abcdef0123456789abcdef", "ModelImporter",
                TestYaml.DocumentBody("ModelImporter:\n  meshes:\n    isReadable: 1\n  userData: \n"));
            Assert.True(model.isReadable);
            model.isReadable = false;
            Assert.False(MetaSettings.Bool(model.m_Settings, "meshes.isReadable", true));
            Assert.Null(MetaSettings.Find(model.m_Settings, "animations.isReadable"));
            // Sparse sections answer the importer's defaults.
            Assert.True(model.useFileScale);
            Assert.True(model.importAnimation);
            Assert.True(model.importBlendShapes);
        }

        [Fact]
        public void SetAddsMissingKeysAndAMissingSequenceEntryStaysUnset()
        {
            var root = TestYaml.DocumentBody("TextureImporter:\n  platformSettings: []\n  userData: \n");
            MetaSettings.Set(root, "bumpmap.convertToNormalMap", "1");
            Assert.Equal(1, MetaSettings.Int(root, "bumpmap.convertToNormalMap"));
            MetaSettings.Set(root, "platformSettings.[buildTarget=DefaultTexturePlatform].maxTextureSize", "64");
            Assert.Null(MetaSettings.Find(root, "platformSettings.[buildTarget=DefaultTexturePlatform].maxTextureSize"));
        }

        static readonly List<string> s_AllAssetsCalls = new List<string>();

        // Declared out of name order, with postprocess orders that would reverse them.
        sealed class ZAllAssets : AssetPostprocessor
        {
            public override int GetPostprocessOrder() => -10;
            static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom, bool didDomainReload) =>
                s_AllAssetsCalls.Add("Z " + string.Join(",", imported) + " " + didDomainReload);
        }

        sealed class AAllAssets : AssetPostprocessor
        {
            public override int GetPostprocessOrder() => 10;
            static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom) =>
                s_AllAssetsCalls.Add("A " + string.Join(",", imported));
        }

        // Sorts first by name, but asks to run after the Z class; Y asks to run before A.
        sealed class BAllAssets : AssetPostprocessor
        {
            [UnityEditor.Callbacks.RunAfterClass(typeof(ZAllAssets))]
            static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom) => s_AllAssetsCalls.Add("B");
        }

        sealed class YAllAssets : AssetPostprocessor
        {
            [UnityEditor.Callbacks.RunBeforeClass(typeof(AAllAssets))]
            static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom) => s_AllAssetsCalls.Add("Y");
        }

        [Fact]
        public void OnPostprocessAllAssetsRunsByClassNameWhateverThePostprocessOrderAsTheirOrderingAttributesAsk()
        {
            s_AllAssetsCalls.Clear();
            AssetPostprocessing.ImportedAssets(typeof(AssetPostprocessingTests).Assembly, new[] { "Assets/X.vfx" });
            Assert.Equal(new[] { "Y", "A Assets/X.vfx", "Z Assets/X.vfx False", "B" },
                s_AllAssetsCalls.FindAll(c => c.StartsWith("A ") || c.StartsWith("Z ") || c == "B" || c == "Y"));
        }

        [Fact]
        public void PixelsBeforeCompressionAreGivenForTheBlockFormatsUnboundCompresses()
        {
            Assert.True(AssetPostprocessing.BeforeCompressionApplies(TextureFormat.DXT1));
            Assert.True(AssetPostprocessing.BeforeCompressionApplies(TextureFormat.DXT5));
            Assert.True(AssetPostprocessing.BeforeCompressionApplies(TextureFormat.BC7));
            Assert.True(AssetPostprocessing.BeforeCompressionApplies(TextureFormat.ETC2_RGBA8));
            Assert.True(AssetPostprocessing.BeforeCompressionApplies(TextureFormat.ASTC_6x6));
            // Crunched and HDR imports, and uncompressed ones, keep the import as it is.
            Assert.False(AssetPostprocessing.BeforeCompressionApplies(TextureFormat.DXT1Crunched));
            Assert.False(AssetPostprocessing.BeforeCompressionApplies(TextureFormat.ETC2_RGBA8Crunched));
            Assert.False(AssetPostprocessing.BeforeCompressionApplies(TextureFormat.BC6H));
            Assert.False(AssetPostprocessing.BeforeCompressionApplies(TextureFormat.ASTC_HDR_4x4));
            Assert.False(AssetPostprocessing.BeforeCompressionApplies(TextureFormat.RGBA32));
        }

        [Fact]
        public void AComponentsFieldsTellWhetherAPostprocessorChangedIt()
        {
            // A component Unbound cannot write as a document: a change is reported, not lost silently.
            var go = new GameObject("Lit");
            try
            {
                var light = go.AddComponent<Light>();
                var before = HierarchyPostprocessing.Fields(light);
                Assert.Equal(before, HierarchyPostprocessing.Fields(light));
                light.range = light.range + 3;
                Assert.NotEqual(before, HierarchyPostprocessing.Fields(light));
                // A change inside a plain object the component holds (a particle system's module).
                var particles = go.AddComponent<ParticleSystem>();
                var unchanged = HierarchyPostprocessing.Fields(particles);
                Assert.Equal(unchanged, HierarchyPostprocessing.Fields(particles));
                var main = particles.main;
                main.startSpeed = 5f;
                Assert.NotEqual(unchanged, HierarchyPostprocessing.Fields(particles));
                // Deep values (a burst count's curve keys) read the same twice.
                var emission = particles.emission;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, new ParticleSystem.MinMaxCurve(2f, AnimationCurve.Linear(0f, 0f, 1f, 1f), AnimationCurve.Linear(0f, 1f, 1f, 3f))) });
                Assert.Equal(HierarchyPostprocessing.Fields(particles), HierarchyPostprocessing.Fields(particles));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        sealed class Logs : ILogHandler
        {
            internal readonly List<string> Lines = new List<string>();
            public void LogFormat(LogType logType, Object context, string format, params object[] args) => Lines.Add(string.Format(format, args));
            public void LogException(Exception exception, Object context) => Lines.Add(exception.Message);
        }

        [Fact]
        public void AnImportsOwnCallbacksLoadingTheAssetGetNothingWithoutALog()
        {
            // ShaderGraph's OnPreprocessAsset loads the graph it imports and expects null (the
            // Editor's first import has no artifact).
            const string guid = "0123456789abcdef0123456789abcdee";
            var logs = new Logs();
            var previous = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = logs;
            try
            {
                using (AssetDatabaseRuntime.ImportCallbacks(guid))
                    Assert.Null(AssetDatabaseRuntime.LoadFile(guid));
            }
            finally
            {
                Debug.unityLogger.logHandler = previous;
            }
            Assert.Empty(logs.Lines);
        }

        sealed class PreprocessingPostprocessor : AssetPostprocessor
        {
            void Count() => s_ImportCalls.Add("preprocess " + assetPath + " " + (assetImporter != null));
        }

        [Fact]
        public void APrefabGetsOnPreprocessAssetOnceWithoutAPrefabPostprocessor()
        {
            // OnPreprocessAsset is every import's first callback: a prefab's runs without an
            // OnPostprocessPrefab anywhere, once per session, and nothing else changes.
            s_ImportCalls.Clear();
            const string path = "Assets/OnlyPreprocessed.prefab", guid = "0123456789abcdef0123456789abcded";
            var importer = AssetImporter.FromMeta(path, guid, "PrefabImporter", TestYaml.DocumentBody("PrefabImporter:\n  userData: \n"));
            var info = new AssetDatabaseRuntime.AssetInfo { Guid = guid, Path = path, Kind = "prefab" };
            using (AssetPostprocessing.HooksForTests("OnPreprocessAsset", new List<(int, Type, System.Reflection.MethodInfo)> { Hook<PreprocessingPostprocessor>("Count") }))
            using (AssetPostprocessing.HooksForTests("OnPostprocessPrefab", new List<(int, Type, System.Reflection.MethodInfo)>()))
            using (AssetImporter.Importing(path, importer))
            {
                Assert.Equal("", HierarchyPostprocessing.Answer(guid, info));
                Assert.Equal("", HierarchyPostprocessing.Answer(guid, info));
            }
            Assert.Equal(new[] { "preprocess Assets/OnlyPreprocessed.prefab True" }, s_ImportCalls);
            Assert.Null(AssetPostprocessing.ExistingSession(path));
        }

        static readonly List<string> s_ImportCalls = new List<string>();

        sealed class CountingPostprocessor : AssetPostprocessor
        {
            int m_Calls;
            // Not a message name: the editor domain's other tests must not see a preprocessor.
            void Count() => s_ImportCalls.Add("count " + ++m_Calls + " " + assetPath);
            Object Give() { s_ImportCalls.Add("give"); return s_Given; }
        }

        sealed class LaterPostprocessor : AssetPostprocessor
        {
            Object Give() { s_ImportCalls.Add("later"); return null; }
        }

        static Object s_Given;

        static (int, Type, System.Reflection.MethodInfo) Hook<T>(string name) =>
            (0, typeof(T), typeof(T).GetMethod(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic));

        [Fact]
        public void OneImportCallsEveryCallbackOnOnePostprocessorPerClassAndAGivenMaterialEndsTheCalls()
        {
            // tests/projects/assetpostprocessors PPInstance: one object counts every callback of an
            // import; a later import makes new ones. OnAssignMaterialModel stops at the first
            // postprocessor that returns something.
            s_ImportCalls.Clear();
            var count = new List<(int, Type, System.Reflection.MethodInfo)> { Hook<CountingPostprocessor>("Count") };
            var import = new ImportSession("Assets/M.fbx", null, null);
            AssetPostprocessorHooks.Run(count, import, Array.Empty<object>());
            AssetPostprocessorHooks.Run(count, import, Array.Empty<object>());
            AssetPostprocessorHooks.Run(count, new ImportSession("Assets/M.fbx", null, null), Array.Empty<object>());
            Assert.Equal(new[] { "count 1 Assets/M.fbx", "count 2 Assets/M.fbx", "count 1 Assets/M.fbx" }, s_ImportCalls);

            var give = new List<(int, Type, System.Reflection.MethodInfo)> { Hook<CountingPostprocessor>("Give"), Hook<LaterPostprocessor>("Give") };
            s_ImportCalls.Clear();
            s_Given = null;
            AssetPostprocessorHooks.Run(give, import, Array.Empty<object>(), untilResult: true);
            Assert.Equal(new[] { "give", "later" }, s_ImportCalls);
            s_ImportCalls.Clear();
            s_Given = new GameObject("Given");
            try
            {
                Assert.Same(s_Given, AssetPostprocessorHooks.Run(give, import, Array.Empty<object>(), untilResult: true));
                Assert.Equal(new[] { "give" }, s_ImportCalls);
                // Without untilResult every postprocessor is called (the last one's result).
                s_ImportCalls.Clear();
                AssetPostprocessorHooks.Run(give, import, Array.Empty<object>());
                Assert.Equal(new[] { "give", "later" }, s_ImportCalls);
            }
            finally
            {
                Object.DestroyImmediate((GameObject)s_Given);
            }
        }
    }
}
