using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    public class PerObjectLightingTests
    {
        static bool InLightmap(LightmapBakeType type, MixedLightingMode mode, bool baked = true)
        {
            var light = new GameObject("L").AddComponent<Light>();
            try
            {
                light.bakingOutput = new LightBakingOutput { isBaked = baked, lightmapBakeType = type, mixedLightingMode = mode };
                return FrameBuilder.InLightmap(light);
            }
            finally { Object.DestroyImmediate(light.gameObject); }
        }

        [Fact]
        public void OnlyASubtractiveBakeTakesLightsOffLightmappedRenderers()
        {
            // tests/projects/urp per-object-constants.jsonl: in the Subtractive bake the lightmapped
            // renderers get neither the mixed sun nor the baked point light; in the Shadowmask bake
            // they get both.
            Assert.True(InLightmap(LightmapBakeType.Mixed, MixedLightingMode.Subtractive));
            Assert.True(InLightmap(LightmapBakeType.Baked, MixedLightingMode.Subtractive));
            Assert.False(InLightmap(LightmapBakeType.Mixed, MixedLightingMode.Shadowmask));
            Assert.False(InLightmap(LightmapBakeType.Baked, MixedLightingMode.Shadowmask));
            Assert.False(InLightmap(LightmapBakeType.Mixed, MixedLightingMode.IndirectOnly));
            // A light the bake did not take, or a realtime one, stays.
            Assert.False(InLightmap(LightmapBakeType.Mixed, MixedLightingMode.Subtractive, baked: false));
            Assert.False(InLightmap(LightmapBakeType.Realtime, MixedLightingMode.Subtractive));
            Assert.False(FrameBuilder.InLightmap(null));
        }
    }
}
