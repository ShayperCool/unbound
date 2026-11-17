using System;
using Unbound.Runtime;
using UnityEditor.VFX;
using UnityEngine.Rendering;
using UnityEngine.VFX;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Particle strips and the schedule of alive counts, as far as the CPU side goes
    /// (what the kernels keep and when the counts change is the probe's:
    /// tests/projects/vfxstrips).</summary>
    public partial class VisualEffectSimulationTests
    {
        /// <summary>A spawner at a rate and a system of two strips of four particles it feeds.</summary>
        static VisualEffectAsset Strips(float rate, VFXSystemFlag flags, VFXMapping[] updateBuffers = null)
        {
            var spawner = new VFXEditorSystemDesc
            {
                name = "Spawn", type = VFXSystemType.Spawner, layer = uint.MaxValue, buffers = new[] { new VFXMapping("spawner_output", 1) },
                tasks = new[] { new VFXEditorTaskDesc { type = VFXTaskType.ConstantRateSpawner, values = new[] { new VFXMapping("Rate", 0) } } },
            };
            var strips = new VFXEditorSystemDesc
            {
                name = "Strips", type = VFXSystemType.Particle, layer = 0, capacity = 8, flags = VFXSystemFlag.SystemHasAttributeBuffer | VFXSystemFlag.SystemHasStrips | flags,
                buffers = new[] { new VFXMapping("spawner_input", 1), new VFXMapping("stripDataBuffer", 0) },
                values = new[] { new VFXMapping("stripCount", 2), new VFXMapping("particlePerStripCount", 4) },
                tasks = new[] { new VFXEditorTaskDesc { type = VFXTaskType.Update, buffers = updateBuffers ?? Array.Empty<VFXMapping>() } },
            };
            var data = Data(new[] { Value(VFXValueType.Float) }, new[] { Constant(0, rate) }, new[] { spawner, strips }, new[]
            {
                new VFXEventDesc { name = "OnPlay", startSystems = new uint[] { 0 }, stopSystems = Array.Empty<uint>(), initSystems = Array.Empty<uint>() },
                new VFXEventDesc { name = "OnStop", startSystems = Array.Empty<uint>(), stopSystems = new uint[] { 0 }, initSystems = Array.Empty<uint>() },
            }, spawners: 1);
            // The strips' state: the count of all, and five words for each strip.
            data.GpuBuffers = new[] { new VFXGPUBufferDesc { target = GraphicsBuffer.Target.Structured, size = 11, stride = 4 } };
            var asset = VisualEffectAsset.Create();
            asset.m_Data = data;
            asset.m_Settings.CullingFlags = VFXCullingFlags.CullNone;
            asset.m_Settings.PreWarmStepCount = 0;
            return asset;
        }

        [Fact]
        public void ASystemOfStripsIsRunWithTheStripsBufferAndSizesTheCompilerGivesIt()
        {
            Assert.Null(VisualEffect.Unsupported(Strips(30f, VFXSystemFlag.SystemHasKill).m_Data));
            Assert.Null(VisualEffect.Unsupported(Strips(30f, default).m_Data));
            var without = Strips(30f, default).m_Data;
            without.Systems[1].values = Array.Empty<VFXMapping>();
            Assert.Equal("the strip system 'Strips' without its strips' buffer or sizes", VisualEffect.Unsupported(without));
            // A system whose particles are kept until the strips they spawned are empty reads
            // those strips' buffer: not measured.
            var attached = Strips(30f, VFXSystemFlag.SystemHasKill, new[] { new VFXMapping("attachedStripDataBuffer", 0) }).m_Data;
            Assert.Equal("the system 'Strips' whose particles live as long as the strips they spawn, which is not measured yet", VisualEffect.Unsupported(attached));
        }

        [Fact]
        public void StripsWhoseParticlesNeverDieCountWhatWasSpawnedAndDrawTheirRoom()
        {
            // Thirty-seven a second in frames of a thirtieth: a particle and a quarter a
            // frame, until the system's room of eight is taken.
            var effect = Running(Strips(37f, default));
            try
            {
                var alive = new int[10];
                for (int i = 0; i < alive.Length; i++)
                {
                    effect.UpdateEffect(Thirtieth);
                    alive[i] = effect.aliveParticleCount;
                }
                Assert.Equal(new[] { 1, 2, 3, 4, 6, 7, 8, 8, 8, 8 }, alive);
                var system = (VFXParticleSystem)effect.SystemAt(1);
                // An output that joins the strips draws a segment less than each strip's room;
                // one that draws the particles one by one, all the system's room.
                Assert.Equal(6u, system.StripSegments);
                Assert.Equal(8u, system.DrawnParticles);
            }
            finally { Object.DestroyImmediate(effect.gameObject); }
        }

        [Fact]
        public void TheEffectsOfAnInstancedGraphShareTheScheduleOfASystemsAliveCounts()
        {
            static VFXAliveReadback Schedule(VisualEffect effect) => ((VFXParticleSystem)effect.SystemAt(1)).AliveSchedule;

            var instanced = Strips(30f, VFXSystemFlag.SystemHasKill);
            // A graph that cannot be instanced (here: it would use GPU events), and an asset
            // that turns instancing off: each of their effects is on its own.
            var cannot = Strips(30f, VFXSystemFlag.SystemHasKill);
            cannot.m_Data.InstancingDisabledReason = VFXInstancingDisabledReason.GPUEvent;
            var off = Strips(30f, VFXSystemFlag.SystemHasKill);
            off.m_Settings.InstancingMode = VFXInstancingMode.Disabled;
            VisualEffect first = Running(instanced), second = Running(instanced), other = Running(Strips(30f, VFXSystemFlag.SystemHasKill));
            VisualEffect cannotOne = Running(cannot), cannotTwo = Running(cannot), offOne = Running(off), offTwo = Running(off);
            var all = new[] { first, second, other, cannotOne, cannotTwo, offOne, offTwo };
            try
            {
                foreach (var effect in all) effect.UpdateEffect(Thirtieth);
                Assert.True(VFXAliveReadback.Batched(first));
                Assert.False(VFXAliveReadback.Batched(cannotOne));
                Assert.False(VFXAliveReadback.Batched(offOne));
                Assert.Same(Schedule(first), Schedule(second));
                // Another asset's effects, even of the same graph, are another batch.
                Assert.NotSame(Schedule(first), Schedule(other));
                Assert.NotSame(Schedule(cannotOne), Schedule(cannotTwo));
                Assert.NotSame(Schedule(offOne), Schedule(offTwo));
                // An effect started over keeps to its asset's schedule.
                var shared = Schedule(first);
                second.Reinit();
                second.UpdateEffect(Thirtieth);
                Assert.Same(shared, Schedule(second));
            }
            finally
            {
                foreach (var effect in all) Object.DestroyImmediate(effect.gameObject);
            }
        }
    }
}
