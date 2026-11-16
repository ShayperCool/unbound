using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEditor.VFX;
using UnityEngine.Rendering;
using UnityEngine.VFX;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Systems spawned by GPU events, as far as the CPU side goes (what the kernels
    /// count and spawn is the probe's: tests/projects/vfxevents).</summary>
    public partial class VisualEffectSimulationTests
    {
        /// <summary>A spawner at a rate, a system it feeds whose update kernel appends events to
        /// a list, and a system that initializes from that list.</summary>
        static VisualEffectAsset Spawning(float rate, VFXSystemFlag spawned)
        {
            var spawner = new VFXEditorSystemDesc
            {
                name = "Spawn", type = VFXSystemType.Spawner, layer = uint.MaxValue, buffers = new[] { new VFXMapping("spawner_output", 1) },
                tasks = new[] { new VFXEditorTaskDesc { type = VFXTaskType.ConstantRateSpawner, values = new[] { new VFXMapping("Rate", 0) } } },
            };
            var parent = new VFXEditorSystemDesc
            {
                name = "Parent", type = VFXSystemType.Particle, layer = 0, capacity = 20, flags = VFXSystemFlag.SystemHasAttributeBuffer | VFXSystemFlag.SystemHasKill,
                buffers = new[] { new VFXMapping("spawner_input", 1) },
                tasks = new[] { new VFXEditorTaskDesc { type = VFXTaskType.Update, buffers = new[] { new VFXMapping("eventListOut_a", 0) } } },
            };
            var child = new VFXEditorSystemDesc
            {
                name = "Child", type = VFXSystemType.Particle, layer = 1, capacity = 8, flags = VFXSystemFlag.SystemHasAttributeBuffer | spawned,
                buffers = new[] { new VFXMapping("eventList", 0) }, values = new[] { new VFXMapping("parentSystemIndex", 1) },
                tasks = Array.Empty<VFXEditorTaskDesc>(),
            };
            var data = Data(new[] { Value(VFXValueType.Float) }, new[] { Constant(0, rate) }, new[] { spawner, parent, child }, new[]
            {
                new VFXEventDesc { name = "OnPlay", startSystems = new uint[] { 0 }, stopSystems = Array.Empty<uint>(), initSystems = Array.Empty<uint>() },
                new VFXEventDesc { name = "OnStop", startSystems = Array.Empty<uint>(), stopSystems = new uint[] { 0 }, initSystems = Array.Empty<uint>() },
            }, spawners: 1);
            // The list: the child's capacity and the two counts.
            data.GpuBuffers = new[] { new VFXGPUBufferDesc { target = GraphicsBuffer.Target.Structured, size = 10, stride = 4 } };
            var asset = VisualEffectAsset.Create();
            asset.m_Data = data;
            asset.m_Settings.CullingFlags = VFXCullingFlags.CullNone;
            asset.m_Settings.PreWarmStepCount = 0;
            return asset;
        }

        [Fact]
        public void ASystemSpawnedByGpuEventsIsAwakeWhileTheSystemThatSpawnsIntoItIs()
        {
            const VFXSystemFlag Spawned = VFXSystemFlag.SystemReceivedEventGPU | VFXSystemFlag.SystemHasKill;
            // Ten a second in steps of a thirtieth: the parent's first particle comes with the
            // third step, and wakes it; the system it spawns into wakes with it, although
            // nothing was spawned into that one from the CPU.
            var effect = Running(Spawning(10f, Spawned));
            try
            {
                var asleep = new (bool Parent, bool Child)[4];
                for (int i = 0; i < asleep.Length; i++)
                {
                    effect.UpdateEffect(Thirtieth);
                    asleep[i] = (effect.GetParticleSystemInfo("Parent").sleeping, effect.GetParticleSystemInfo("Child").sleeping);
                }
                Assert.Equal(new[] { (true, true), (true, true), (false, false), (false, false) }, asleep);
                // Started over: both asleep again until the parent spawns.
                effect.Reinit();
                effect.UpdateEffect(Thirtieth);
                Assert.Equal((true, true), (effect.GetParticleSystemInfo("Parent").sleeping, effect.GetParticleSystemInfo("Child").sleeping));
            }
            finally { Object.DestroyImmediate(effect.gameObject); }
        }

        [Fact]
        public void ASystemSpawnedByGpuEventsRunsWhetherItsParticlesDieOrNot()
        {
            Assert.Null(VisualEffect.Unsupported(Spawning(10f, VFXSystemFlag.SystemReceivedEventGPU | VFXSystemFlag.SystemHasKill).m_Data));
            // So does one whose particles never die; strips are not run yet.
            Assert.Null(VisualEffect.Unsupported(Spawning(10f, VFXSystemFlag.SystemReceivedEventGPU).m_Data));
            Assert.Equal("SystemHasStrips of the system 'Child'",
                VisualEffect.Unsupported(Spawning(10f, VFXSystemFlag.SystemReceivedEventGPU | VFXSystemFlag.SystemHasKill | VFXSystemFlag.SystemHasStrips).m_Data));
        }
    }
}
