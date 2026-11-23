using Unbound.Runtime;
using UnityEngine.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    public class CameraCommandBufferMemoryTests
    {
        sealed class EquivalentBuffer : CommandBuffer
        {
            public override bool Equals(object other) => other is EquivalentBuffer;
            public override int GetHashCode() => 0;
        }

        [Fact]
        public void RemovingADuplicateKeepsDistinctBuffersWithEqualManagedValues()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            using var retained = new EquivalentBuffer();
            using var removed = new EquivalentBuffer();
            var owner = new GameObject();
            try
            {
                var camera = owner.AddComponent<Camera>();
                const CameraEvent evt = CameraEvent.BeforeDepthTexture;
                camera.AddCommandBuffer(evt, retained);
                camera.AddCommandBuffer(evt, removed);
                camera.AddCommandBuffer(evt, retained);
                camera.AddCommandBuffer(evt, removed);
                camera.RemoveCommandBuffer(evt, removed);
                var buffers = camera.GetCommandBuffers(evt);
                Assert.Equal(2, buffers.Length);
                Assert.All(buffers, buffer => Assert.Same(retained, buffer));
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        [Fact]
        public void UnmeasuredFamilyRetainsThePreviousCameraBufferBehavior()
        {
            using var release = TestRelease.Pin(TestRelease.Unmeasured);
            using var buffer = new CommandBuffer();
            var owner = new GameObject();
            try
            {
                var camera = owner.AddComponent<Camera>();
                const CameraEvent evt = CameraEvent.BeforeDepthTexture;
                camera.AddCommandBuffer(evt, buffer);
                camera.AddCommandBufferAsync(evt, buffer, ComputeQueueType.Default);
                camera.RemoveCommandBuffer(evt, buffer);
                Assert.Same(buffer, Assert.Single(camera.GetCommandBuffers(evt)));
                var invalid = (CameraEvent)(-1);
                camera.AddCommandBuffer(invalid, buffer);
                Assert.Same(buffer, Assert.Single(camera.GetCommandBuffers(invalid)));
                Assert.Null(MemorySizes.RuntimeSize(camera));
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }
    }
}
