using System.Runtime.InteropServices;
using UnityEngine.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    // RayTracingAccelerationStructure.BuildSettings: the defaults the Unity 6000.0.71f1 Editor gives a
    // new value (tests/projects/defaultstate), and its value layout (two fields, as declared).
    public class RayTracingBuildSettingsTests
    {
        [Fact]
        public void ANewValueTakesTheDefaultsAndDefaultIsZero()
        {
            var settings = new RayTracingAccelerationStructure.BuildSettings();
            Assert.Equal(RayTracingAccelerationStructureBuildFlags.PreferFastTrace, settings.buildFlags);
            Assert.Equal(Vector3.zero, settings.relativeOrigin);
            var zero = default(RayTracingAccelerationStructure.BuildSettings);
            Assert.Equal(RayTracingAccelerationStructureBuildFlags.None, zero.buildFlags);
        }

        // A copy holds its own values; the layout is the flags then the origin, sequential.
        [Fact]
        public void CopiesAreIndependentAndTheLayoutIsSequential()
        {
            var original = new RayTracingAccelerationStructure.BuildSettings(RayTracingAccelerationStructureBuildFlags.MinimizeMemory, new Vector3(1, 2, 3));
            var copy = original;
            copy.relativeOrigin = Vector3.one;
            Assert.Equal(new Vector3(1, 2, 3), original.relativeOrigin);
            Assert.Equal(RayTracingAccelerationStructureBuildFlags.MinimizeMemory, copy.buildFlags);
            Assert.Equal(16, Marshal.SizeOf<RayTracingAccelerationStructure.BuildSettings>());
            Assert.Equal(4, (int)Marshal.OffsetOf<RayTracingAccelerationStructure.BuildSettings>("<relativeOrigin>k__BackingField"));
        }
    }
}
