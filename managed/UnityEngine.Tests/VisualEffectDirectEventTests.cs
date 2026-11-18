using System;
using Unbound.Runtime;
using UnityEditor.VFX;
using UnityEngine.VFX;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Events wired straight to an Initialize context, as far as the CPU side goes
    /// (what is drawn is the probe's: tests/projects/vfxdirect).</summary>
    public partial class VisualEffectSimulationTests
    {
        /// <summary>A system of eight particles that never die, fed by the event `Hit` alone.</summary>
        static VisualEffectAsset Wired()
        {
            var system = new VFXEditorSystemDesc
            {
                name = "Particles", type = VFXSystemType.Particle, layer = 0, capacity = 8, flags = VFXSystemFlag.SystemHasAttributeBuffer | VFXSystemFlag.SystemHasDirectLink,
                buffers = Array.Empty<VFXMapping>(), values = Array.Empty<VFXMapping>(), tasks = Array.Empty<VFXEditorTaskDesc>(),
            };
            var data = Data(Array.Empty<VFXExpressionDesc>(), Array.Empty<VFXExpressionValueContainerDesc>(), new[] { system }, new[]
            {
                new VFXEventDesc { name = "OnPlay", startSystems = Array.Empty<uint>(), stopSystems = Array.Empty<uint>(), initSystems = Array.Empty<uint>() },
                new VFXEventDesc { name = "Hit", startSystems = Array.Empty<uint>(), stopSystems = Array.Empty<uint>(), initSystems = new uint[] { 0 } },
            });
            var asset = VisualEffectAsset.Create();
            asset.m_Data = data;
            asset.m_Settings.CullingFlags = VFXCullingFlags.CullNone;
            asset.m_Settings.PreWarmStepCount = 0;
            return asset;
        }

        [Fact]
        public void AttributesOfAnotherAssetAreRefusedBeforeAnythingIsSent()
        {
            // Two assets of one layout: the attributes belong to the asset that made them, not
            // to its layout; another effect of the same asset may send them.
            VisualEffectAsset asset = Wired(), twin = Wired();
            VisualEffect effect = Running(asset), sibling = Running(asset), other = Running(twin);
            try
            {
                effect.UpdateEffect(Thirtieth);
                var foreign = other.CreateVFXEventAttribute();
                foreign.SetFloat("spawnCount", 2f);
                const string Refusal = "Invalid VFXEventAttribute provided to VisualEffect. It has been created with another VisualEffectAsset. Use CreateVFXEventAttribute.";
                Assert.Equal(Refusal, Assert.Throws<InvalidOperationException>(() => effect.SendEvent("Hit", foreign)).Message);
                Assert.Equal(Refusal, Assert.Throws<InvalidOperationException>(() => effect.Play(foreign)).Message);
                Assert.Equal(Refusal, Assert.Throws<InvalidOperationException>(() => effect.Stop(foreign)).Message);
                effect.UpdateEffect(Thirtieth);
                Assert.Equal(0, effect.aliveParticleCount);

                var shared = sibling.CreateVFXEventAttribute();
                shared.SetFloat("spawnCount", 3f);
                effect.SendEvent("Hit", shared);
                effect.SendEvent("Hit", null);
                effect.UpdateEffect(Thirtieth);
                Assert.Equal(4, effect.aliveParticleCount);
            }
            finally
            {
                Object.DestroyImmediate(effect.gameObject);
                Object.DestroyImmediate(sibling.gameObject);
                Object.DestroyImmediate(other.gameObject);
            }
        }

        [Fact]
        public void AnEventWiredToAnInitializeContextSpawnsTheCountItAsksFor()
        {
            Assert.Null(VisualEffect.Unsupported(Wired().m_Data));
            var effect = Running(Wired());
            try
            {
                void Send(float count)
                {
                    var attribute = effect.CreateVFXEventAttribute();
                    attribute.SetFloat("spawnCount", count);
                    effect.SendEvent("Hit", attribute);
                }
                effect.UpdateEffect(Thirtieth);
                Assert.Equal(0, effect.aliveParticleCount);
                // Without attributes: the asset's own, a count of one.
                effect.SendEvent("Hit");
                effect.UpdateEffect(Thirtieth);
                Assert.Equal(1, effect.aliveParticleCount);
                // Two events in a frame: both; a count that is not whole: its whole part; less
                // than one: none.
                Send(2f);
                Send(1.7f);
                Send(0.5f);
                effect.UpdateEffect(Thirtieth);
                Assert.Equal(4, effect.aliveParticleCount);
                // More than the room left: the room; then nothing more, also for a count
                // below zero, which asks for all the room there is.
                Send(100f);
                effect.UpdateEffect(Thirtieth);
                Assert.Equal(8, effect.aliveParticleCount);
                Send(-1f);
                effect.UpdateEffect(Thirtieth);
                Assert.Equal(8, effect.aliveParticleCount);
                // Started over, a count below zero fills the system.
                effect.Reinit();
                Send(-1f);
                effect.UpdateEffect(Thirtieth);
                Assert.Equal(8, effect.aliveParticleCount);
                // An event of another name is none of the system's.
                effect.Reinit();
                effect.SendEvent("Other");
                effect.UpdateEffect(Thirtieth);
                Assert.Equal(0, effect.aliveParticleCount);
            }
            finally { Object.DestroyImmediate(effect.gameObject); }
        }
    }
}
