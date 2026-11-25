using System;
using System.Collections.Generic;
using System.Linq;
using Unbound.SpeedTree;
using UnityEditor;
using UnityEngine.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The SpeedTree 8 importer's settings and wind. The values are the Unity 6000.0.71f1
    /// Editor's (tests/projects/speedtree8 records the whole import; these check the rules on
    /// settings the samples do not combine).</summary>
    public class SpeedTree8ImportTests
    {
        static SpeedTreeImporter Importer(string section) => (SpeedTreeImporter)AssetImporter.FromMeta("Assets/Trees/Oak.st",
            "7123456789abcdef0123456789abcdef", "SpeedTreeImporter", TestYaml.DocumentBody("SpeedTreeImporter:\n" + section + "  userData: \n"));

        static string Level(int? overrides, float height, int cast, int probes, int reflection, int windQuality) =>
            (overrides is int o ? $"  - enableSettingOverride: {o}\n    height: {height}\n" : $"  - height: {height}\n")
            + $"    castShadows: {cast}\n    receiveShadows: {cast}\n    useLightProbes: {probes}\n    reflectionProbeUsage: {reflection}\n"
            + $"    enableBump: 1\n    enableHue: 0\n    enableSubsurface: 1\n    windQuality: {windQuality}\n";

        /// <summary>A serializedVersion 7 .meta as the Editor writes it for a tree with a billboard
        /// level, with tree-wide settings that differ from the levels' own.</summary>
        static readonly string Version7 = "  externalObjects: {}\n  serializedVersion: 7\n  mainColor: {r: 1, g: 1, b: 1, a: 1}\n"
            + "  hueVariation: {r: 1, g: 0.5, b: 0, a: 0.1}\n  alphaTestRef: 0.33\n  enableHueVariation: 0\n  enableBumpMapping: 1\n"
            + "  enableSubsurfaceScattering: 1\n  bestWindQuality: 4\n  selectedWindQuality: 3\n  enableShadowCasting: 0\n"
            + "  enableShadowReceiving: 1\n  enableLightProbes: 1\n  reflectionProbeEnumValue: 1\n  hasBillboard: 1\n  lODSettings:\n"
            + Level(0, 0.5f, 1, 1, 1, 4) + Level(1, 0.01f, 1, 0, 0, 1)
            + "  enableSmoothLODTransition: 1\n  animateCrossFading: 1\n  billboardTransitionCrossFadeWidth: 0.25\n  fadeOutWidth: 0.25\n"
            + "  scaleFactor: 0.3048\n  unitConversionEnumValue: 1\n  motionVectorModeEnumValue: 1\n  generateColliders: 1\n"
            + "  generateRigidbody: 1\n  materialVersion: 3\n  materialLocation: 1\n  fileIDType: 2\n";

        /// <summary>A serializedVersion 6 .meta (the samples' older trees): levels without
        /// enableSettingOverride, no tree-wide lighting, no selected wind quality.</summary>
        static readonly string Version6 = "  serializedVersion: 6\n  mainColor: {r: 1, g: 1, b: 1, a: 1}\n"
            + "  hueVariation: {r: 1, g: 0.5, b: 0, a: 0.1}\n  alphaTestRef: 0.33\n  bestWindQuality: 4\n  hasBillboard: 1\n  lODSettings:\n"
            + Level(null, 0.5f, 0, 0, 1, 4) + Level(null, 0.01f, 0, 0, 0, 1)
            + "  enableSmoothLODTransition: 1\n  animateCrossFading: 1\n  billboardTransitionCrossFadeWidth: 0.25\n  fadeOutWidth: 0.25\n"
            + "  scaleFactor: 0.3048\n  materialVersion: 3\n  materialLocation: 0\n  fileIDType: 1\n";

        [Fact]
        public void TheSettingsAreTheMetasLevelsAndTreeWideOnes()
        {
            var importer = Importer(Version7);
            Assert.True(importer.isV8);
            Assert.Equal(new[] { 0.5f, 0.01f }, importer.LODHeights);
            Assert.Equal(new[] { false, true }, importer.enableSettingOverride);
            // The per-level arrays are the levels' own values, overriding or not.
            Assert.Equal(new[] { true, true }, importer.castShadows);
            Assert.Equal(new[] { true, false }, importer.useLightProbes);
            Assert.Equal(new[] { ReflectionProbeUsage.BlendProbes, ReflectionProbeUsage.Off }, importer.reflectionProbeUsages);
            Assert.Equal(new[] { 4, 1 }, importer.windQualities);
            Assert.False(importer.castShadowsByDefault);
            Assert.Equal(3, importer.selectedWindQuality);
            Assert.Equal(4, importer.bestWindQuality);
            Assert.Equal(SpeedTreeImporter.MaterialLocation.InPrefab, importer.materialLocation);
            Assert.Equal("Assets/Trees/Oak Materials", importer.materialFolderPath);
            Assert.Null(importer.defaultBillboardShader);
            Assert.Equal(0.3048f, importer.scaleFactor);
            Assert.Equal(new Color(1f, 0.5f, 0f, 0.1f), importer.hueVariation);
            Assert.False(importer.hasImported);
        }

        /// <summary>A level that does not override draws with the tree's settings (its own stay in
        /// the .meta); one that overrides with its own.</summary>
        [Fact]
        public void ALevelThatDoesNotOverrideTakesTheTreesSettings()
        {
            var levels = Importer(Version7).ImportLevels();
            Assert.False(levels[0].Overrides);
            Assert.False(levels[0].CastShadows);
            Assert.Equal(3, levels[0].WindQuality);
            Assert.True(levels[1].Overrides);
            Assert.True(levels[1].CastShadows);
            Assert.False(levels[1].UseLightProbes);
            Assert.Equal(ReflectionProbeUsage.Off, levels[1].ReflectionProbes);
            Assert.Equal(1, levels[1].WindQuality);
        }

        /// <summary>An older .meta's levels all override (the Editor's enableSettingOverride of the
        /// samples' serializedVersion 6 trees), and its selected wind quality is its best.</summary>
        [Fact]
        public void AnOlderMetasLevelsAllOverride()
        {
            var importer = Importer(Version6);
            Assert.Equal(new[] { true, true }, importer.enableSettingOverride);
            Assert.Equal(4, importer.selectedWindQuality);
            Assert.Equal(SpeedTreeImporter.MaterialLocation.External, importer.materialLocation);
            // Without tree-wide settings the "by default" ones are the defaults.
            Assert.True(importer.castShadowsByDefault);
            Assert.True(importer.generateColliders);
            Assert.True(importer.generateRigidbody);
            var levels = importer.ImportLevels();
            Assert.All(levels, l => Assert.True(l.Overrides));
            Assert.False(levels[0].CastShadows);
            Assert.False(levels[0].UseLightProbes);
            Assert.Equal(ReflectionProbeUsage.BlendProbes, levels[0].ReflectionProbes);
        }

        /// <summary>The import scale is the unit conversion's (the Editor's scaleFactor answers it:
        /// tests/projects/speedtree8 Card_Units0 1, Card_Inches 0.0254, Card_Custom the .meta's);
        /// an older .meta without one has its scaleFactor.</summary>
        [Theory]
        [InlineData("  unitConversionEnumValue: 0\n  scaleFactor: 0.3048\n", 1f)]
        [InlineData("  unitConversionEnumValue: 1\n  scaleFactor: 2\n", 0.3048f)]
        [InlineData("  unitConversionEnumValue: 2\n  scaleFactor: 0.3048\n", 0.01f)]
        [InlineData("  unitConversionEnumValue: 3\n  scaleFactor: 0.3048\n", 0.0254f)]
        [InlineData("  unitConversionEnumValue: 4\n  scaleFactor: 2\n", 2f)]
        [InlineData("  scaleFactor: 0.5\n", 0.5f)]
        public void TheScaleIsTheUnitConversions(string settings, float scale)
        {
            var importer = Importer("  serializedVersion: 7\n" + settings);
            Assert.Equal(scale, importer.scaleFactor);
            Assert.Equal(scale, importer.ImportScale);
        }

        /// <summary>A SpeedTree 8 tree is a GameObject to a search and to its main type, which needs
        /// no import (tests/projects/speedtree8 find/GameObject, mainType).</summary>
        [Fact]
        public void ATreeIsAGameObjectAsset()
        {
            AssetDatabase.List("""
                [
                  {"guid":"00000000000000001000000000000000","path":"Assets","kind":"folder","importer":"DefaultImporter","labels":[]},
                  {"guid":"21111111111111111111111111111111","path":"Assets/Oak.st","kind":"speedtree","importer":"SpeedTreeImporter","labels":[]},
                  {"guid":"31111111111111111111111111111111","path":"Assets/Box.prefab","kind":"prefab","importer":"PrefabImporter","labels":[]},
                  {"guid":"41111111111111111111111111111111","path":"Assets/Bark.png","kind":"texture","importer":"TextureImporter","labels":[]}
                ]
                """);
            try
            {
                Assert.Equal(new[] { "Assets/Box.prefab", "Assets/Oak.st" },
                    AssetDatabase.FindAssets("t:GameObject").Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p, StringComparer.Ordinal));
                Assert.Equal(typeof(GameObject), AssetDatabase.GetMainAssetTypeAtPath("Assets/Oak.st"));
            }
            finally
            {
                AssetDatabase.Invalidate();
            }
        }

        static SpeedTree8File.Wind NumberedWind()
        {
            float n = 0f;
            float Next() => ++n;
            float[] Curve()
            {
                var c = new float[SpeedTree8File.CurveSamples];
                for (int i = 0; i < c.Length; i++) c[i] = Next();
                return c;
            }
            var wind = new SpeedTree8File.Wind { StrengthResponse = Next(), GlobalHeight = Next(), MaxBranchLevel1Length = Next() };
            wind.Oscillation = Enumerable.Range(0, SpeedTree8File.CurveSamples).Select(_ => Curve()).ToArray();
            wind.GlobalDistance = Curve();
            wind.GlobalDirectionAdherence = Curve();
            wind.BranchLevels = Enumerable.Range(0, 2).Select(_ => new SpeedTree8File.BranchLevel
                { Distance = Curve(), DirectionAdherence = Curve(), Whip = Curve(), Turbulence = Next() }).ToArray();
            wind.LeafGroups = Enumerable.Range(0, 2).Select(_ => new SpeedTree8File.LeafGroup
                { RippleDistance = Curve(), TumbleFlip = Curve(), TumbleTwist = Curve(), TumbleDirectionAdherence = Curve(), TwitchThrow = Curve(), LeewardScalar = Next() }).ToArray();
            wind.FrondRippleDistance = Curve();
            wind.BranchWindAnchor = new SpeedTreeVector3 { X = Next(), Y = Next(), Z = Next() };
            wind.Options = new bool[SpeedTree8File.WindOptionCount];
            wind.Options[3] = true;
            return wind;
        }

        /// <summary>Heights and distances take the import scale; everything else, the branch wind
        /// anchor included, stays as the file has it.</summary>
        [Fact]
        public void TheWindAssetScalesHeightsAndDistancesOnly()
        {
            var wind = NumberedWind();
            var config = SpeedTreeWindConfig8.From(wind, 2f);
            Assert.Equal(wind.StrengthResponse, config.StrengthResponse);
            Assert.Equal(wind.GlobalHeight * 2f, config.GlobalHeight);
            Assert.Equal(wind.MaxBranchLevel1Length * 2f, config.MaxBranchLevel1Length);
            Assert.Equal(wind.GlobalDistance.Select(v => v * 2f), config.GlobalDistance);
            Assert.Equal(wind.GlobalDirectionAdherence, config.GlobalDirectionAdherence);
            Assert.Equal(wind.Oscillation[9], config.Oscillation[9]);
            for (int b = 0; b < 2; b++)
            {
                Assert.Equal(wind.BranchLevels[b].Distance.Select(v => v * 2f), config.BranchLevels[b].Distance);
                Assert.Equal(wind.BranchLevels[b].Whip, config.BranchLevels[b].Whip);
                Assert.Equal(wind.BranchLevels[b].Turbulence, config.BranchLevels[b].Turbulence);
                Assert.Equal(wind.LeafGroups[b].RippleDistance.Select(v => v * 2f), config.LeafGroups[b].RippleDistance);
                Assert.Equal(wind.LeafGroups[b].TumbleFlip, config.LeafGroups[b].TumbleFlip);
                Assert.Equal(wind.LeafGroups[b].LeewardScalar, config.LeafGroups[b].LeewardScalar);
                Assert.Equal(0f, config.LeafGroups[b].RollMaxScale);
            }
            Assert.Equal(wind.FrondRippleDistance.Select(v => v * 2f), config.FrondRippleDistance);
            Assert.Equal(new Vector3(wind.BranchWindAnchor.X, wind.BranchWindAnchor.Y, wind.BranchWindAnchor.Z), config.BranchWindAnchor);
            Assert.True(config.Options[3]);
            // The rolling noise the asset has for a file without one.
            Assert.Equal((0.005f, 9f, 32f, -0.5f), (config.RollingNoiseSize, config.RollingNoiseTwist, config.RollingNoiseTurbulence, config.RollingBranchVerticalOffset));
        }

        static void AssertBits(float expected, float actual) =>
            Assert.Equal(BitConverter.SingleToInt32Bits(expected), BitConverter.SingleToInt32Bits(actual));

        /// <summary>The card tree's wind asset: values of the Editor's (its m_Config8).</summary>
        [SpeedTreeSamplesFact]
        public void TheCardTreesWindAssetHasTheEditorsValues()
        {
            var file = SpeedTree8File.Read(SpeedTreeSamples.Read("v8/Card_Tree_Sample/card-tree-sample.st"));
            var config = SpeedTreeWindConfig8.From(file.WindConfig, 0.3048f);
            AssertBits(26.44241f, config.GlobalHeight);
            AssertBits(1.152f, config.GlobalHeightExponent);
            AssertBits(0.161947444f, config.GlobalDistance[1]);
            AssertBits(1.46349752f, config.BranchLevels[0].Distance[9]);
            AssertBits(0.075438f, config.BranchLevels[1].Distance[9]);
            AssertBits(0.04476855f, config.LeafGroups[0].RippleDistance[9]);
            AssertBits(0.0349065848f, config.LeafGroups[1].TumbleFlip[9]);
            AssertBits(0.0121920006f, config.FrondRippleDistance[9]);
            AssertBits(5.36846066f, config.MaxBranchLevel1Length);
            AssertBits(42.3625336f, config.BranchWindAnchor.y);
            AssertBits(0.05f, config.LeafGroups[0].TwitchSharpness);
            AssertBits(2.88749981f, config.Oscillation[3][9]);
            Assert.Equal(0f, config.LeafGroups[1].RollSeparation);
        }

        sealed class Logs : ILogHandler
        {
            internal readonly List<string> Messages = new List<string>();
            public void LogException(Exception exception, Object context) => Messages.Add(exception.Message);
            public void LogFormat(LogType logType, Object context, string format, params object[] args) =>
                Messages.Add(logType + ": " + string.Format(format, args));
        }

        /// <summary>A SpeedTree 8 tree's wind asset (version 0) is no SpeedTree 9 wind: its tree's
        /// draws get no wind constants, and that is reported once.</summary>
        [Fact]
        public void ASpeedTree8TreeDrawsWithoutWind()
        {
            var config = SpeedTreeWindConfig8.From(NumberedWind(), 1f);
            var asset = new SpeedTreeWindAsset(config);
            Assert.Equal(0, asset.Version);
            Assert.True(asset.IsSpeedTree8);
            var go = new GameObject("Tree8");
            var logs = new Logs();
            var previous = Debug.unityLogger.logHandler;
            try
            {
                Debug.unityLogger.logHandler = logs;
                SpeedTreeWindSystem.s_SpeedTree8WindReported = false;
                int Hits() => Unbound.Runtime.Stubs.HitReport().FirstOrDefault(h => h.Key == "SpeedTree 8 wind").Value;
                int before = Hits();
                var renderer = go.AddComponent<MeshRenderer>();
                go.AddComponent<Tree>().windAsset = asset;
                SpeedTreeWindSystem.Update();
                var properties = new Dictionary<int, object>();
                using (Unbound.Runtime.DrawPropertyPool.Begin())
                {
                    Assert.Same(properties, SpeedTreeWindSystem.Apply(renderer, properties));
                    Assert.Same(properties, SpeedTreeWindSystem.Apply(renderer, properties));
                }
                Assert.Empty(properties);
                // Reported once, however often the tree draws.
                Assert.Equal(1, Hits() - before);
            }
            finally
            {
                Debug.unityLogger.logHandler = previous;
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(asset);
            }
        }
    }
}
