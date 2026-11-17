using System;
using Unbound.Runtime;
using UnityEditor.VFX;
using UnityEngine.Rendering;
using UnityEngine.VFX;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The bounds of an effect's systems, as far as the CPU side goes (what the
    /// kernels compute and when it arrives is the probe's: tests/projects/vfxbounds).</summary>
    public partial class VisualEffectSimulationTests
    {
        /// <summary>A spawner and a system it feeds, with bounds the graph gives (a centre, a
        /// size and a padding) and, when asked, a buffer for bounds computed from its particles.</summary>
        static VisualEffectAsset Bounded(Vector3 center, Vector3 size, Vector3 padding, VFXSystemFlag flags, bool buffer)
        {
            var spawner = new VFXEditorSystemDesc
            {
                name = "Spawn", type = VFXSystemType.Spawner, layer = uint.MaxValue, buffers = new[] { new VFXMapping("spawner_output", 1) },
                tasks = new[] { new VFXEditorTaskDesc { type = VFXTaskType.ConstantRateSpawner, values = new[] { new VFXMapping("Rate", 0) } } },
            };
            var system = new VFXEditorSystemDesc
            {
                name = "Particles", type = VFXSystemType.Particle, layer = 0, capacity = 8, flags = VFXSystemFlag.SystemHasAttributeBuffer | flags,
                buffers = buffer ? new[] { new VFXMapping("spawner_input", 1), new VFXMapping("boundsBuffer", 0) } : new[] { new VFXMapping("spawner_input", 1) },
                values = new[] { new VFXMapping("bounds_center", 1), new VFXMapping("bounds_size", 2), new VFXMapping("boundsPadding", 3) },
                tasks = Array.Empty<VFXEditorTaskDesc>(),
            };
            var data = Data(
                new[] { Value(VFXValueType.Float), Value(VFXValueType.Float3), Value(VFXValueType.Float3), Value(VFXValueType.Float3) },
                new[] { Constant(0, 30f), Constant(1, center), Constant(2, size), Constant(3, padding) },
                new[] { spawner, system }, new[]
                {
                    new VFXEventDesc { name = "OnPlay", startSystems = new uint[] { 0 }, stopSystems = Array.Empty<uint>(), initSystems = Array.Empty<uint>() },
                    new VFXEventDesc { name = "OnStop", startSystems = Array.Empty<uint>(), stopSystems = new uint[] { 0 }, initSystems = Array.Empty<uint>() },
                }, spawners: 1);
            // The computed bounds: a least and a greatest corner.
            data.GpuBuffers = new[] { new VFXGPUBufferDesc { target = GraphicsBuffer.Target.Structured, size = 6, stride = 4 } };
            var asset = VisualEffectAsset.Create();
            asset.m_Data = data;
            asset.m_Settings.CullingFlags = VFXCullingFlags.CullNone;
            asset.m_Settings.PreWarmStepCount = 0;
            return asset;
        }

        const VFXSystemFlag Computing = VFXSystemFlag.SystemNeedsComputeBounds | VFXSystemFlag.SystemAutomaticBounds;

        [Fact]
        public void ASystemThatComputesItsBoundsIsRunWithTheBufferTheCompilerGivesIt()
        {
            Assert.Null(VisualEffect.Unsupported(Bounded(default, Vector3.one, default, Computing, true).m_Data));
            Assert.Equal("the system 'Particles' that computes its bounds without a buffer for them",
                VisualEffect.Unsupported(Bounded(default, Vector3.one, default, Computing, false).m_Data));
            Assert.Null(VisualEffect.Unsupported(Bounded(default, Vector3.one, default, default, false).m_Data));
        }

        [Fact]
        public void AWordOfComputedBoundsIsTheFloatWhoseBitsTheKernelFlipped()
        {
            // The kernels' order-keeping flip: the sign bit of a float that is not negative,
            // every bit of a negative one.
            static uint Flipped(float value)
            {
                uint bits = BitConverter.SingleToUInt32Bits(value);
                return bits ^ ((int)bits < 0 ? uint.MaxValue : 0x80000000u);
            }
            foreach (float value in new[] { 0f, 1.5f, -1.5f, 1300.25f, -0.0288367569f, float.MaxValue, -float.MaxValue, float.Epsilon })
                Assert.Equal(BitConverter.SingleToUInt32Bits(value), BitConverter.SingleToUInt32Bits(VFXParticleSystem.Decoded(Flipped(value))));
            Assert.True(Flipped(-2f) < Flipped(-1f) && Flipped(-1f) < Flipped(0f) && Flipped(0f) < Flipped(1f));
            // The box of no particle: the greatest least corner and the least greatest one,
            // neither a number: zero.
            Assert.Equal(0f, VFXParticleSystem.Decoded(uint.MaxValue));
            Assert.Equal(0f, VFXParticleSystem.Decoded(0u));
        }

        [Fact]
        public void AnEffectsBoundsAreTakenBackFromItsSystemsCorners()
        {
            // One system's box, as the Editor's renderer has it for these numbers
            // (tests/projects/vfxbounds, `sim/auto/f004`): the centre halfway between the
            // corners is not the box's own centre in its last place.
            Vector3 center = new Vector3(-0.0713083148f, -0.0288367569f, 0f), extents = new Vector3(0.5574998f, 0.586630464f, 0.308023155f);
            var effect = Running(Bounded(center, extents * 2f, default, default, false));
            try
            {
                effect.UpdateEffect(Thirtieth);
                Assert.True(effect.RendererBounds(out var bounds));
                Assert.Equal(BitConverter.SingleToUInt32Bits(-0.0288367271f), BitConverter.SingleToUInt32Bits(bounds.center.y));
                Assert.Equal(extents, bounds.extents);
                Assert.Equal(center, effect.GetParticleSystemInfo("Particles").bounds.center);
            }
            finally { Object.DestroyImmediate(effect.gameObject); }
        }

        [Fact]
        public void BoundsComputedFromParticlesAreTheSystemsBoundsAndStartAsNone()
        {
            var padding = new Vector3(0.5f, 0.25f, 0.125f);
            var effect = Running(Bounded(new Vector3(3f, 4f, 5f), Vector3.one, padding, Computing, true));
            var manual = Running(Bounded(new Vector3(3f, 4f, 5f), Vector3.one, padding, default, false));
            try
            {
                effect.UpdateEffect(Thirtieth);
                manual.UpdateEffect(Thirtieth);
                // No answer yet (and none without a renderer): a zero box at the effect's
                // origin, not the box the graph gives.
                Assert.Equal(default, effect.GetComputedBounds("Particles"));
                Assert.Equal(default, effect.GetParticleSystemInfo("Particles").bounds);
                Assert.True(effect.RendererBounds(out var bounds));
                Assert.Equal(default, bounds);
                Assert.Equal(padding, effect.GetCurrentBoundsPadding("Particles"));
                // A system that computes none answers the graph's box grown by its padding,
                // and no computed box; a name that is no system's answers nothing.
                Assert.Equal(new Bounds(new Vector3(3f, 4f, 5f), Vector3.one + 2f * padding), manual.GetParticleSystemInfo("Particles").bounds);
                Assert.Equal(default, manual.GetComputedBounds("Particles"));
                Assert.Equal(default, effect.GetComputedBounds("None"));
                Assert.Equal(Vector3.zero, effect.GetCurrentBoundsPadding("None"));
            }
            finally
            {
                Object.DestroyImmediate(effect.gameObject);
                Object.DestroyImmediate(manual.gameObject);
            }
        }
    }
}
