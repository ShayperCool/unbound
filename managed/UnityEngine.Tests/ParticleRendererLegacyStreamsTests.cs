using System.Collections.Generic;
using UnityEngine.VFX;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The obsolete vertex stream flags act on the active streams as the release's
    /// ParticleSystemRenderer does (Unity 6000.0.71f1, tests/projects/particlerender streams/*),
    /// and VFXManager's scrub time and runtime resources come from the Editor's settings.</summary>
    public class ParticleRendererLegacyStreamsTests
    {
#pragma warning disable CS0618
        static List<ParticleSystemVertexStream> Streams(ParticleSystemRenderer r)
        {
            var list = new List<ParticleSystemVertexStream>();
            r.GetActiveVertexStreams(list);
            return list;
        }

        [Fact]
        public void FlagsAddTheirStreamsOnceAndRemoveThem()
        {
            var go = new GameObject("streams");
            try
            {
                go.AddComponent<ParticleSystem>();
                var r = go.GetComponent<ParticleSystemRenderer>();
                Assert.Equal(ParticleSystemVertexStreams.Position | ParticleSystemVertexStreams.Normal | ParticleSystemVertexStreams.Color | ParticleSystemVertexStreams.UV,
                    r.GetEnabledVertexStreams((ParticleSystemVertexStreams)0x3FFF));
                r.EnableVertexStreams(ParticleSystemVertexStreams.Lifetime | ParticleSystemVertexStreams.Normal);
                r.EnableVertexStreams(ParticleSystemVertexStreams.Lifetime);
                Assert.Equal(new[]
                {
                    ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Normal, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV,
                    ParticleSystemVertexStream.AgePercent, ParticleSystemVertexStream.InvStartLifetime,
                }, Streams(r));
                Assert.True(r.AreVertexStreamsEnabled(ParticleSystemVertexStreams.Lifetime | ParticleSystemVertexStreams.UV));
                r.DisableVertexStreams(ParticleSystemVertexStreams.Lifetime | ParticleSystemVertexStreams.Color);
                Assert.Equal(new[] { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Normal, ParticleSystemVertexStream.UV }, Streams(r));
                Assert.False(r.AreVertexStreamsEnabled(ParticleSystemVertexStreams.Color | ParticleSystemVertexStreams.UV));
                // Random adds StableRandomXYZ but removes StableRandomXYZW: the XYZ stream stays.
                r.EnableVertexStreams(ParticleSystemVertexStreams.Random);
                r.DisableVertexStreams(ParticleSystemVertexStreams.Random);
                Assert.Equal(new[]
                {
                    ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Normal, ParticleSystemVertexStream.UV, ParticleSystemVertexStream.StableRandomXYZ,
                }, Streams(r));
            }
            finally { Object.DestroyImmediate(go); }
        }
#pragma warning restore CS0618

        [Fact]
        public void VfxScrubTimeAndResourcesDefaultToTheEditorsSettings()
        {
            float before = VFXManager.maxScrubTime;
            try
            {
                Assert.Equal(30f, before);
                Assert.Null(VFXManager.runtimeResources);
                VFXManager.maxScrubTime = 12f;
                Assert.Equal(12f, VFXManager.maxScrubTime);
            }
            finally { VFXManager.maxScrubTime = before; }
        }
    }
}
