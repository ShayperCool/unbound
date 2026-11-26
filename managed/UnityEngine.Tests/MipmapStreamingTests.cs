using System;
using System.Collections.Generic;
using UnityEngine;
using Xunit;
using Object = UnityEngine.Object;

namespace UnityEngine.Tests
{
    /// <summary>Mipmap streaming's texture answers as the Unity 6000.0.71f1 Editor gives them
    /// (tests/projects/mipstreaming values.jsonl).</summary>
    public class MipmapStreamingTests
    {
        sealed class Capture : ILogHandler
        {
            internal readonly List<string> Logs = new List<string>();
            public void LogException(Exception exception, Object context) => Logs.Add(exception.Message);
            public void LogFormat(LogType type, Object context, string format, params object[] args) => Logs.Add(string.Format(format, args));
        }

        [Fact]
        public void ATextureThatDoesNotStreamIgnoresARequestedLevel()
        {
            // The probe's runtime Texture2D(64, 64, RGBA32, mips): requested/runtime/*.
            var texture = new Texture2D(64, 64, TextureFormat.RGBA32, true);
            try
            {
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
            finally { Object.DestroyImmediate(texture); }
        }

        [Fact]
        public void AStreamingTextureClampsARequestedLevelAndReportsItNotLoaded()
        {
            var texture = new Texture2D(64, 64, TextureFormat.RGBA32, true);
            int reduction = QualitySettings.streamingMipmapsMaxLevelReduction;
            try
            {
                texture.SetStreaming(true, 2);
                Assert.True(texture.streamingMipmaps);
                Assert.Equal(2, texture.streamingMipmapsPriority);
                Assert.Equal(8, texture.minimumMipmapLevel);
                Assert.True(texture.IsRequestedMipmapLevelLoaded());
                // requested/stream/requestedMipmapLevel: 3 reads back 2 (the reduction, 2).
                texture.requestedMipmapLevel = 3;
                Assert.Equal(2, texture.requestedMipmapLevel);
                Assert.False(texture.IsRequestedMipmapLevelLoaded());
                // Beside the probe: a larger reduction is still clamped to the last mip (6 of 7).
                QualitySettings.streamingMipmapsMaxLevelReduction = 10;
                texture.requestedMipmapLevel = 9;
                Assert.Equal(6, texture.requestedMipmapLevel);
                texture.requestedMipmapLevel = -4;
                Assert.Equal(0, texture.requestedMipmapLevel);
                Assert.True(texture.IsRequestedMipmapLevelLoaded());
                texture.ClearRequestedMipmapLevel();
                Assert.Equal(-1, texture.requestedMipmapLevel);
            }
            finally
            {
                QualitySettings.streamingMipmapsMaxLevelReduction = reduction;
                Object.DestroyImmediate(texture);
            }
        }

        [Fact]
        public void TheCalculatedLevelIsANoticeWhileStreamingIsActive()
        {
            var texture = new Texture2D(64, 64, TextureFormat.RGBA32, true);
            bool active = QualitySettings.streamingMipmapsActive;
            var logs = new Capture();
            var handler = Debug.unityLogger.logHandler;
            try
            {
                texture.SetStreaming(true, 0);
                Assert.Equal(-1, texture.calculatedMipmapLevel);
                QualitySettings.streamingMipmapsActive = true;
                Unbound.Runtime.Stubs.Forget("Texture2D.calculatedMipmapLevel");
                Debug.unityLogger.logHandler = logs;
                texture.requestedMipmapLevel = 1;
                Assert.Equal(1, texture.calculatedMipmapLevel);
                Assert.Contains(Unbound.Runtime.Stubs.HitReport(), h => h.Key == "Texture2D.calculatedMipmapLevel");
            }
            finally
            {
                Debug.unityLogger.logHandler = handler;
                QualitySettings.streamingMipmapsActive = active;
                Object.DestroyImmediate(texture);
            }
        }
    }
}
