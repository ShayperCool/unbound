using UnityEngine.VFX;
using Xunit;

namespace UnityEngine.Tests
{
    public partial class VisualEffectSimulationTests
    {
        /// <summary>An effect whose asset compiled again (a graph re-imported after a write) starts
        /// over on the new compile, as when its asset is set: it no longer runs the old systems.</summary>
        [Fact]
        public void AnEffectStartsOverOnItsAssetsNewCompile()
        {
            var asset = Counting(7.3f);
            var effect = Running(asset);
            var fresh = Running(Counting(30f));
            try
            {
                Counts(effect, 13);
                Assert.Equal(3, effect.aliveParticleCount);
                var compiled = Counting(30f).m_Data;
                asset.m_Data = compiled;
                effect.AssetRebuilt();
                Assert.Same(compiled, effect.Data);
                Assert.Equal(0, effect.aliveParticleCount);
                // It now counts as an effect of the new compile does.
                Assert.Equal(Counts(fresh, 13), Counts(effect, 13));
            }
            finally
            {
                Object.DestroyImmediate(effect.gameObject);
                Object.DestroyImmediate(fresh.gameObject);
            }
        }
    }
}
