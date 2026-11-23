using UnityEngine.Profiling;
using Xunit;

namespace UnityEngine.Tests
{
    public sealed class AudioSourceCurveStorageTests
    {
        [Fact]
        public void UnmeasuredFamilyKeepsPreviousStorageAndNullCurveHandling()
        {
            using var release = TestRelease.Pin(TestRelease.Unmeasured);
            var owner = new GameObject("Unmeasured source");
            try
            {
                var source = owner.AddComponent<AudioSource>();
                const AudioSourceCurveType type = AudioSourceCurveType.SpatialBlend;
                source.SetCustomCurve(type, AnimationCurve.Linear(0f, 0f, 1f, 1f));
                Assert.Equal(2, source.GetCustomCurve(type).length);
                source.SetCustomCurve(type, null);
                Assert.Equal(0, source.GetCustomCurve(type).length);
                Assert.Equal(0, Profiler.GetRuntimeMemorySizeLong(source));
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }
    }
}
