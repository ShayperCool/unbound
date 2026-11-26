using System;
using System.Collections.Generic;
using System.Text.Json;
using UnityEngine;
using Xunit;
using Object = UnityEngine.Object;

namespace UnityEngine.Tests
{
    /// <summary>Mipmap streaming's texture answers as the Unity 6000.0.71f1 Editor gives them
    /// (tests/projects/mipstreaming values.jsonl).</summary>
    public class MipmapStreamingTests
    {
        static Texture2D Streaming(int size, bool mips = true)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, mips);
            texture.SetStreaming(true, 2);
            return texture;
        }

        [Fact]
        public void ATextureThatDoesNotStreamIgnoresARequestedLevel()
        {
            // The probe's runtime Texture2D(64, 64, RGBA32, mips): requested/runtime/*.
            var texture = new Texture2D(64, 64, TextureFormat.RGBA32, true);
            bool active = QualitySettings.streamingMipmapsActive;
            try
            {
                QualitySettings.streamingMipmapsActive = true;
                Assert.False(texture.streamingMipmaps);
                texture.requestedMipmapLevel = 3;
                texture.minimumMipmapLevel = 2;
                Assert.Equal(-1, texture.requestedMipmapLevel);
                Assert.Equal(-1, texture.minimumMipmapLevel);
                Assert.Equal(-1, texture.calculatedMipmapLevel);
                Assert.Equal(-1, texture.desiredMipmapLevel);
                Assert.Equal(0, texture.loadedMipmapLevel);
                Assert.True(texture.IsRequestedMipmapLevelLoaded());
            }
            finally
            {
                QualitySettings.streamingMipmapsActive = active;
                Object.DestroyImmediate(texture);
            }
        }

        [Fact]
        public void AStreamingTextureWithoutMipmapsKeepsTheLevelsOfOneThatDoesNotStream()
        {
            // load/nomip/*: streams, but minimum -1 and a requested 3 ignored.
            var texture = Streaming(64, mips: false);
            try
            {
                Assert.True(texture.streamingMipmaps);
                Assert.Equal(-1, texture.minimumMipmapLevel);
                texture.requestedMipmapLevel = 3;
                Assert.Equal(-1, texture.requestedMipmapLevel);
            }
            finally { Object.DestroyImmediate(texture); }
        }

        [Fact]
        public void AStreamingTextureClampsARequestedLevelAndReportsItNotLoaded()
        {
            var texture = Streaming(64);
            var tiny = Streaming(4);
            int reduction = QualitySettings.streamingMipmapsMaxLevelReduction;
            try
            {
                Assert.Equal(2, texture.streamingMipmapsPriority);
                // */stream, */big, */tiny minimumMipmapLevel: 8 whatever the size.
                Assert.Equal(8, texture.minimumMipmapLevel);
                Assert.Equal(8, tiny.minimumMipmapLevel);
                Assert.True(texture.IsRequestedMipmapLevelLoaded());
                // requested/stream: 3 reads back 2 (the reduction); requested/tiny: 3 reads back 2 (its last mip).
                texture.requestedMipmapLevel = 3;
                Assert.Equal(2, texture.requestedMipmapLevel);
                Assert.False(texture.IsRequestedMipmapLevelLoaded());
                QualitySettings.streamingMipmapsMaxLevelReduction = 5;
                tiny.requestedMipmapLevel = 3;
                Assert.Equal(2, tiny.requestedMipmapLevel);
                // negative/tiny: -4 reads back 0.
                tiny.requestedMipmapLevel = -4;
                Assert.Equal(0, tiny.requestedMipmapLevel);
                Assert.True(tiny.IsRequestedMipmapLevelLoaded());
                texture.ClearRequestedMipmapLevel();
                Assert.Equal(-1, texture.requestedMipmapLevel);
            }
            finally
            {
                QualitySettings.streamingMipmapsMaxLevelReduction = reduction;
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(tiny);
            }
        }

        [Fact]
        public void WhileActiveTheCalculatedLevelIsTheReductionAndTheDesiredOneTheRequest()
        {
            var big = Streaming(1024);
            var tiny = Streaming(4);
            bool active = QualitySettings.streamingMipmapsActive;
            int reduction = QualitySettings.streamingMipmapsMaxLevelReduction;
            try
            {
                Assert.Equal(-1, big.calculatedMipmapLevel);
                QualitySettings.streamingMipmapsActive = true;
                QualitySettings.streamingMipmapsMaxLevelReduction = 5;
                Unbound.Runtime.Stubs.Forget("Texture2D.calculatedMipmapLevel");
                // reduction5/big, reduction5/tiny: 5 for both (not clamped to tiny's last mip).
                Assert.Equal(5, big.calculatedMipmapLevel);
                Assert.Equal(5, tiny.calculatedMipmapLevel);
                Assert.Equal(5, big.desiredMipmapLevel);
                // requested/big: a request of 0 is the desired level; the calculated one keeps 5.
                big.requestedMipmapLevel = 0;
                Assert.Equal(0, big.desiredMipmapLevel);
                Assert.Equal(5, big.calculatedMipmapLevel);
                Assert.Contains(Unbound.Runtime.Stubs.HitReport(), h => h.Key == "Texture2D.calculatedMipmapLevel");
                // minimum0*, minimum1request0: a minimum under the calculated level caps it; a request
                // is the desired level whatever the minimum.
                big.ClearRequestedMipmapLevel();
                QualitySettings.streamingMipmapsMaxLevelReduction = 2;
                big.minimumMipmapLevel = 0;
                Assert.Equal(0, big.desiredMipmapLevel);
                big.requestedMipmapLevel = 1;
                Assert.Equal(1, big.desiredMipmapLevel);
                big.minimumMipmapLevel = 1;
                big.requestedMipmapLevel = 0;
                Assert.Equal(0, big.desiredMipmapLevel);
                Assert.Equal(2, big.calculatedMipmapLevel);
            }
            finally
            {
                QualitySettings.streamingMipmapsActive = active;
                QualitySettings.streamingMipmapsMaxLevelReduction = reduction;
                Object.DestroyImmediate(big);
                Object.DestroyImmediate(tiny);
            }
        }

        [Fact]
        public void ACopyStreamsWithoutTheRequestAndReportsItsSourcesLevelAsLoaded()
        {
            var texture = Streaming(64);
            texture.Apply();
            bool active = QualitySettings.streamingMipmapsActive;
            Texture2D copy = null;
            try
            {
                QualitySettings.streamingMipmapsActive = true;
                texture.requestedMipmapLevel = 2;
                copy = Object.Instantiate(texture);
                // requested/copy/*.
                Assert.True(copy.streamingMipmaps);
                Assert.Equal(2, copy.streamingMipmapsPriority);
                Assert.Equal(-1, copy.requestedMipmapLevel);
                Assert.Equal(8, copy.minimumMipmapLevel);
                Assert.Equal(-1, copy.calculatedMipmapLevel);
                Assert.Equal(-1, copy.desiredMipmapLevel);
                Assert.Equal(2, copy.loadedMipmapLevel);
                Assert.Equal(2, copy.loadingMipmapLevel);
                Assert.True(copy.IsRequestedMipmapLevelLoaded());
            }
            finally
            {
                QualitySettings.streamingMipmapsActive = active;
                Object.DestroyImmediate(texture);
                if (copy != null) Object.DestroyImmediate(copy);
            }
        }

        [Fact]
        public void TheStreamingSettingsAreReadFromTheQualityLevel()
        {
            var levels = QualitySettings.s_LevelSettings;
            int level = QualitySettings.GetQualityLevel();
            var names = QualitySettings.names;
            bool active = QualitySettings.streamingMipmapsActive;
            float budget = QualitySettings.streamingMipmapsMemoryBudget;
            int renderers = QualitySettings.streamingMipmapsRenderersPerFrame;
            int reduction = QualitySettings.streamingMipmapsMaxLevelReduction;
            bool cameras = QualitySettings.streamingMipmapsAddAllCameras;
            int requests = QualitySettings.streamingMipmapsMaxFileIORequests;
            static JsonElement J(object value) => JsonSerializer.SerializeToElement(value);
            try
            {
                QualitySettings.Internal_SetNames(new[] { "Streaming" });
                QualitySettings.s_LevelSettings = new List<Dictionary<string, JsonElement>>
                {
                    new Dictionary<string, JsonElement>
                    {
                        ["streamingMipmapsActive"] = J(1), ["streamingMipmapsMemoryBudget"] = J(256f),
                        ["streamingMipmapsRenderersPerFrame"] = J(64), ["streamingMipmapsMaxLevelReduction"] = J(4),
                        ["streamingMipmapsAddAllCameras"] = J(0), ["streamingMipmapsMaxFileIORequests"] = J(32),
                    },
                };
                QualitySettings.SetQualityLevel(0);
                Assert.True(QualitySettings.streamingMipmapsActive);
                Assert.Equal(256f, QualitySettings.streamingMipmapsMemoryBudget);
                Assert.Equal(64, QualitySettings.streamingMipmapsRenderersPerFrame);
                Assert.Equal(4, QualitySettings.streamingMipmapsMaxLevelReduction);
                Assert.False(QualitySettings.streamingMipmapsAddAllCameras);
                Assert.Equal(32, QualitySettings.streamingMipmapsMaxFileIORequests);
            }
            finally
            {
                QualitySettings.s_LevelSettings = levels;
                QualitySettings.Internal_SetNames(names);
                QualitySettings.SetQualityLevel(level);
                QualitySettings.streamingMipmapsActive = active;
                QualitySettings.streamingMipmapsMemoryBudget = budget;
                QualitySettings.streamingMipmapsRenderersPerFrame = renderers;
                QualitySettings.streamingMipmapsMaxLevelReduction = reduction;
                QualitySettings.streamingMipmapsAddAllCameras = cameras;
                QualitySettings.streamingMipmapsMaxFileIORequests = requests;
            }
        }
    }
}
