using Unbound.Runtime.Profiling;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using Unity.Profiling.LowLevel.Unsafe;
using Xunit;

namespace Unbound.Tests
{
    /// <summary>Which markers native plugins see (tests/projects/nativegfx profiler.script): the
    /// ones scripts ask for, not Unbound's own stats.</summary>
    public class ProfilerPluginMarkerTests
    {
        [Fact]
        public void ScriptMarkersAreSharedAndEngineStatsAreNot()
        {
            var marker = new ProfilerMarker("ProfilerPluginMarkerTests.Script");
            Assert.True(ProfilerCore.StatOf(marker.Handle).FromScript);
            var named = ProfilerUnsafeUtility.CreateMarker("ProfilerPluginMarkerTests.Named", ProfilerUnsafeUtility.CategoryScripts, MarkerFlags.Script, 0);
            Assert.True(ProfilerCore.StatOf(named).FromScript);
            var engine = ProfilerCore.StatByName(ProfilerUnsafeUtility.CategoryInternal, "Main Thread");
            Assert.NotNull(engine);
            Assert.False(engine.FromScript);
        }
    }
}
