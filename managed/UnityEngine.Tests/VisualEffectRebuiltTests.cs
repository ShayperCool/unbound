using Unbound.Runtime;
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

        /// <summary>A re-import restarts every live effect of the graph's asset, one on a DontSave
        /// object too (scene finders skip those), and no effect of another asset.</summary>
        [Fact]
        public void AReimportRestartsEveryLiveEffectOfItsAssetOnly()
        {
            var asset = Counting(7.3f);
            var other = Counting(7.3f);
            var shown = Running(asset);
            var hidden = Running(asset);
            hidden.gameObject.hideFlags = HideFlags.HideAndDontSave;
            var unrelated = Running(other);
            var otherData = other.m_Data;
            try
            {
                foreach (var e in new[] { shown, hidden, unrelated }) Counts(e, 13);
                var compiled = Counting(30f).m_Data;
                asset.m_Data = compiled;
                VisualEffectImports.RestartEffects(asset);
                Assert.Same(compiled, shown.Data);
                Assert.Same(compiled, hidden.Data);
                Assert.Equal(0, hidden.aliveParticleCount);
                Assert.Same(otherData, unrelated.Data);
                Assert.Equal(3, unrelated.aliveParticleCount);
            }
            finally
            {
                foreach (var e in new[] { shown, hidden, unrelated }) Object.DestroyImmediate(e.gameObject);
            }
        }
    }
}
