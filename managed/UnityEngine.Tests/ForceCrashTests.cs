using UnityEngine.Diagnostics;
using Xunit;

// Utils.ForceCrash categories including values outside ForcedCrashCategory (7, -1):
// tests/projects/forcecrash measures every category in its own Editor and Unbound run; here we
// verify the crash plan routing without killing the test runner process.

namespace UnityEngine.Tests
{
    public sealed class ForceCrashTests
    {
        [Fact]
        public void ValuesOutsideForcedCrashCategoryFollowMonoAbortPlan()
        {
            var mono = Utils.Plan(ForcedCrashCategory.MonoAbort);
            Assert.Equal(Utils.SIGABRT, mono.Signal);
            Assert.Single(mono.Lines);
            Assert.Null(mono.Error);

            foreach (var invalid in new[] { (ForcedCrashCategory)7, (ForcedCrashCategory)(-1), (ForcedCrashCategory)int.MaxValue })
            {
                var plan = Utils.Plan(invalid);
                Assert.Equal(mono.Signal, plan.Signal);
                Assert.Equal(mono.Lines, plan.Lines);
                Assert.Null(plan.Error);
            }

            // Control: AccessViolation uses SIGSEGV, not SIGABRT.
            var av = Utils.Plan(ForcedCrashCategory.AccessViolation);
            Assert.Equal(Utils.SIGSEGV, av.Signal);
            Assert.NotEqual(mono.Lines, av.Lines);
        }
    }
}
