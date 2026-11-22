using System;
using System.Reflection;
using UnityEditor.VFX;
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
        public void ACamerasTemporaryBuffersAreReleasedOnceUnusedForLongerThanTheyKeep()
        {
            uint saved = (uint)s_Frame.GetValue(null);
            var effect = WithTemporaryBuffers(2);
            try
            {
                s_Frame.SetValue(null, 20u);
                var camera = effect.TemporaryBuffer(0, 7, 0);
                var shared = effect.TemporaryBuffer(0, 0, 0);
                s_Frame.SetValue(null, 22u);
                effect.ExpireTemporaryBuffers();
                Assert.Same(camera, effect.TemporaryBuffer(0, 7, 0));
                s_Frame.SetValue(null, 25u);
                effect.ExpireTemporaryBuffers();
                // The camera's ring was made again; the one without a camera is kept.
                Assert.NotSame(camera, effect.TemporaryBuffer(0, 7, 1));
                Assert.Same(shared, effect.TemporaryBuffer(0, 0, 1));
            }
            finally
            {
                s_Frame.SetValue(null, saved);
                Object.DestroyImmediate(effect.gameObject);
            }
        }
    }
}
