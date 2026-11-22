using System;
using System.Reflection;
using UnityEditor.VFX;
using Unbound.Runtime;
using UnityEngine.VFX;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>An effect's temporary buffers (an output's last frames, for motion vectors): per
    /// asset descriptor and key a ring of its frame count of buffers, the one of the frame
    /// <c>pastFrameIndex</c> back by the frame index effects see; per-camera rings released once
    /// unused for longer than they keep (tests/projects/vfxoutputs `MovingVectors`).</summary>
    public partial class VisualEffectSimulationTests
    {
        static readonly FieldInfo s_Frame = typeof(VFXManager).GetField("s_Frame", BindingFlags.NonPublic | BindingFlags.Static)!;

        static VisualEffect WithTemporaryBuffers(uint frameCount)
        {
            var system = new VFXEditorSystemDesc
            {
                name = "S", type = VFXSystemType.Particle, layer = 0, capacity = 4, flags = VFXSystemFlag.SystemHasAttributeBuffer,
                tasks = Array.Empty<VFXEditorTaskDesc>(),
            };
            var data = Data(Array.Empty<VFXExpressionDesc>(), Array.Empty<VFXExpressionValueContainerDesc>(), new[] { system }, Array.Empty<VFXEventDesc>());
            data.TemporaryBuffers = new[]
            {
                new VFXTemporaryGPUBufferDesc { frameCount = frameCount, desc = new VFXGPUBufferDesc { target = GraphicsBuffer.Target.Raw, size = 36, stride = 4 } },
            };
            var asset = VisualEffectAsset.Create();
            asset.m_Data = data;
            return Running(asset);
        }

        [Fact]
        public void ATemporaryBufferIsTheRingsBufferOfItsPastFrame()
        {
            uint saved = (uint)s_Frame.GetValue(null);
            var effect = WithTemporaryBuffers(2);
            try
            {
                s_Frame.SetValue(null, 14u);
                var current = effect.TemporaryBuffer(0, 0, 0);
                var previous = effect.TemporaryBuffer(0, 0, 1);
                Assert.NotNull(current);
                Assert.NotSame(current, previous);
                Assert.Equal(36, current.count);
                // The next frame: this frame's buffer is the one back.
                s_Frame.SetValue(null, 15u);
                Assert.Same(current, effect.TemporaryBuffer(0, 0, 1));
                Assert.Same(previous, effect.TemporaryBuffer(0, 0, 0));
                // Another key (a camera's) has a ring of its own.
                Assert.NotSame(effect.TemporaryBuffer(0, 0, 0), effect.TemporaryBuffer(0, 7, 0));
                // A descriptor the asset does not have: none.
                Assert.Null(effect.TemporaryBuffer(1, 0, 0));
            }
            finally
            {
                s_Frame.SetValue(null, saved);
                Object.DestroyImmediate(effect.gameObject);
            }
        }

        [Fact]
        public void ARingOfThreeFramesTurnsThroughItsBuffers()
        {
            uint saved = (uint)s_Frame.GetValue(null);
            var effect = WithTemporaryBuffers(3);
            try
            {
                s_Frame.SetValue(null, 10u);
                var at10 = effect.TemporaryBuffer(0, 0, 0);
                s_Frame.SetValue(null, 11u);
                var at11 = effect.TemporaryBuffer(0, 0, 0);
                s_Frame.SetValue(null, 12u);
                var at12 = effect.TemporaryBuffer(0, 0, 0);
                Assert.Equal(3, new System.Collections.Generic.HashSet<GraphicsBuffer> { at10, at11, at12 }.Count);
                Assert.Same(at11, effect.TemporaryBuffer(0, 0, 1));
                Assert.Same(at10, effect.TemporaryBuffer(0, 0, 2));
                // Frame 13 takes frame 10's buffer again.
                s_Frame.SetValue(null, 13u);
                Assert.Same(at10, effect.TemporaryBuffer(0, 0, 0));
                Assert.Same(at12, effect.TemporaryBuffer(0, 0, 1));
            }
            finally
            {
                s_Frame.SetValue(null, saved);
                Object.DestroyImmediate(effect.gameObject);
            }
        }

        [Fact]
        public void ACamerasTemporaryBuffersAreKeptThroughSkippedFramesThenReleased()
        {
            uint saved = (uint)s_Frame.GetValue(null);
            var effect = WithTemporaryBuffers(2);
            try
            {
                s_Frame.SetValue(null, 20u);
                var camera = effect.TemporaryBuffer(0, 7, 0);
                var shared = effect.TemporaryBuffer(0, 0, 0);
                // A camera that renders every few frames keeps its buffers.
                s_Frame.SetValue(null, 23u);
                effect.ExpireTemporaryBuffers();
                Assert.Same(camera, effect.TemporaryBuffer(0, 7, 1));
                s_Frame.SetValue(null, 23u + VisualEffect.UnusedCameraFrames);
                effect.ExpireTemporaryBuffers();
                Assert.Contains(camera, new[] { effect.TemporaryBuffer(0, 7, 0), effect.TemporaryBuffer(0, 7, 1) });
                // Unused for longer: the camera's ring is made again; the one without a camera is kept.
                s_Frame.SetValue(null, 24u + 2 * VisualEffect.UnusedCameraFrames);
                effect.ExpireTemporaryBuffers();
                Assert.NotSame(camera, effect.TemporaryBuffer(0, 7, 0));
                Assert.NotSame(camera, effect.TemporaryBuffer(0, 7, 1));
                Assert.Contains(shared, new[] { effect.TemporaryBuffer(0, 0, 0), effect.TemporaryBuffer(0, 0, 1) });
            }
            finally
            {
                s_Frame.SetValue(null, saved);
                Object.DestroyImmediate(effect.gameObject);
            }
        }

        [Fact]
        public void ATasksTemporaryBufferIsBoundAgainForEachCameraAndFrame()
        {
            uint saved = (uint)s_Frame.GetValue(null);
            var effect = WithTemporaryBuffers(2);
            var task = new VFXEditorTaskDesc
            {
                type = VFXTaskType.ParticleQuadOutput,
                temporaryBuffers = new[] { new VFXMappingTemporary(new VFXMapping("elementToVFXBufferPrevious", 0), 1, true) },
            };
            var bindings = new VFXTemporaryBindings(task);
            try
            {
                s_Frame.SetValue(null, 30u);
                Assert.True(bindings.Changed(effect, 5, 0, out int name, out var first));
                Assert.Equal(Shader.PropertyToID("elementToVFXBufferPrevious"), name);
                Assert.Same(effect.TemporaryBuffer(0, 5, 1), first);
                Assert.False(bindings.Changed(effect, 5, 0, out _, out _));
                // Another camera's, then the first camera's again.
                Assert.True(bindings.Changed(effect, 9, 0, out _, out var second));
                Assert.NotSame(first, second);
                Assert.True(bindings.Changed(effect, 5, 0, out _, out var again));
                Assert.Same(first, again);
                // The next frame: the buffer one back is another.
                s_Frame.SetValue(null, 31u);
                Assert.True(bindings.Changed(effect, 5, 0, out _, out var next));
                Assert.NotSame(first, next);
                // A task without temporary buffers binds none.
                Assert.Equal(0, new VFXTemporaryBindings(new VFXEditorTaskDesc()).Count);
            }
            finally
            {
                s_Frame.SetValue(null, saved);
                Object.DestroyImmediate(effect.gameObject);
            }
        }

        static readonly FieldInfo s_PerCameraKernels = typeof(VFXParticleSystem).GetField("m_PerCamera", BindingFlags.NonPublic | BindingFlags.Instance)!;

        /// <summary>How many per-camera kernels the counting effect's particle system keeps when
        /// its output-update kernel writes the last frames and its output draws with
        /// <paramref name="processor"/> and reads <paramref name="read"/>.</summary>
        static int KeptKernels(Object processor, VFXMappingTemporary[] read)
        {
            var asset = Counting(30f);
            var write = new[] { new VFXMappingTemporary(new VFXMapping("elementToVFXBuffer", 0), 0, true) };
            asset.m_Data.Systems[1].tasks = new[]
            {
                new VFXEditorTaskDesc { type = VFXTaskType.PerCameraUpdate, temporaryBuffers = write },
                new VFXEditorTaskDesc { type = VFXTaskType.ParticleQuadOutput, temporaryBuffers = read },
            };
            asset.m_Data.Processors = new[]
            {
                Array.Empty<Object>(),
                new[] { ComputeShader.FromFile("Tests/VfxOutputUpdate.compute", "VfxOutputUpdate", "#pragma kernel CSMain\n"), processor },
            };
            asset.m_Data.TemporaryBuffers = new[]
            {
                new VFXTemporaryGPUBufferDesc { frameCount = 2, desc = new VFXGPUBufferDesc { target = GraphicsBuffer.Target.Raw, size = 36, stride = 4 } },
            };
            var effect = Running(asset);
            try
            {
                using var warnings = new Warnings();
                Counts(effect, 1);
                return ((Array)s_PerCameraKernels.GetValue(effect.SystemAt(1))).Length;
            }
            finally
            {
                Object.DestroyImmediate(effect.gameObject);
            }
        }

        [Fact]
        public void AKernelThatKeepsLastFramesRunsOnlyForAnOutputDrawnThatReadsThem()
        {
            var shader = UnityEditor.ShaderUtil.CreateShaderAsset("Shader \"Tests/VfxMoving\" { SubShader { Pass { Tags { \"LightMode\"=\"UniversalForwardOnly\" } } Pass { Tags { \"LightMode\"=\"MotionVectors\" } } } }");
            var material = new Material(shader);
            var read = new[] { new VFXMappingTemporary(new VFXMapping("elementToVFXBufferPrevious", 0), 1, true) };
            Assert.Equal(1, KeptKernels(material, read));
            // Its output refused (no shader): the kernel is released, as one of a list would be.
            Assert.Equal(0, KeptKernels(null, read));
            // An output drawn that reads none of them.
            Assert.Equal(0, KeptKernels(material, Array.Empty<VFXMappingTemporary>()));
        }
    }
}
