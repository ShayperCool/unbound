using Unbound.Runtime;
using UnityEngine.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    public class CameraCommandBufferMemoryTests
    {
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
                Assert.Null(MemorySizes.RuntimeSize(camera));
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }
    }
}
