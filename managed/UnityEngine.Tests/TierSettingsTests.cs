using System;
using Unbound.Runtime;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine.Rendering;
using Xunit;

namespace Unbound.Tests
{
    /// <summary>EditorGraphicsSettings.GetTierSettings reads the session's table: a group's own
    /// entry, else the entry of group 0, which stands for the groups without a rule of their own
    /// (tests/projects/tiersettings); Lightmapping.lightingDataAsset names the scene's asset.</summary>
    [Collection("RenderSettings state")]
    public sealed class TierSettingsTests
    {
        static TierSettings Quality(ShaderQuality quality) => new TierSettings { standardShaderQuality = quality, hdrMode = CameraHDRMode.FP16 };

        [Fact]
        public void AGroupWithoutItsOwnEntryTakesTheOtherGroupsOne()
        {
            try
            {
                EditorGraphicsSettings.SetTierTable(new[]
                {
                    (0, 0, Quality(ShaderQuality.High)),
                    (7, 0, Quality(ShaderQuality.Low)),
                    (7, 2, Quality(ShaderQuality.Medium)),
                });
                Assert.Equal(ShaderQuality.Low, EditorGraphicsSettings.GetTierSettings(BuildTargetGroup.Android, GraphicsTier.Tier1).standardShaderQuality);
                Assert.Equal(ShaderQuality.Medium, EditorGraphicsSettings.GetTierSettings(NamedBuildTarget.Android, GraphicsTier.Tier3).standardShaderQuality);
                // WebGL has no entry of its own: group 0's.
                Assert.Equal(ShaderQuality.High, EditorGraphicsSettings.GetTierSettings(BuildTargetGroup.WebGL, GraphicsTier.Tier1).standardShaderQuality);
                // No entry for the tier at all: the session gave no table for it, loudly.
                Assert.Throws<InvalidOperationException>(() => EditorGraphicsSettings.GetTierSettings(BuildTargetGroup.WebGL, GraphicsTier.Tier2));
                Assert.Throws<ArgumentOutOfRangeException>(() => EditorGraphicsSettings.GetTierSettings(BuildTargetGroup.Android, (GraphicsTier)5));
                // Named targets whose name is not the group's: Windows Store Apps is WSA (14), Server
                // builds for Standalone (1).
                EditorGraphicsSettings.SetTierTable(new[]
                {
                    (0, 0, Quality(ShaderQuality.High)),
                    (1, 2, Quality(ShaderQuality.Medium)),
                    (14, 0, Quality(ShaderQuality.Low)),
                });
                Assert.Equal(ShaderQuality.Low, EditorGraphicsSettings.GetTierSettings(NamedBuildTarget.WindowsStoreApps, GraphicsTier.Tier1).standardShaderQuality);
                Assert.Equal(ShaderQuality.Medium, EditorGraphicsSettings.GetTierSettings(NamedBuildTarget.Server, GraphicsTier.Tier3).standardShaderQuality);
                Assert.Equal(BuildTargetGroup.Switch, NamedBuildTarget.NintendoSwitch.ToBuildTargetGroup());
                Assert.Equal(NamedBuildTarget.WindowsStoreApps, NamedBuildTarget.FromBuildTargetGroup(BuildTargetGroup.WSA));
                Assert.Equal(NamedBuildTarget.iOS, NamedBuildTarget.FromBuildTargetGroup(BuildTargetGroup.iOS));
            }
            finally
            {
                EditorGraphicsSettings.SetTierTable(Array.Empty<(int, int, TierSettings)>());
            }
        }

        [Fact]
        public void TheLightingDataAssetIsOneObjectPerAssetNamedAsItsFile()
        {
            Assert.Null(LightingDataAsset.Of(default));
            var builtin = new ObjectRef(20201, "0000000000000000f000000000000000", 0);
            var asset = LightingDataAsset.Of(builtin);
            Assert.Equal("LightingData", asset.name);
            Assert.Same(asset, LightingDataAsset.Of(builtin));
            Assert.Throws<NotSupportedException>(() => Lightmapping.lightingDataAsset = asset);
        }
    }
}
