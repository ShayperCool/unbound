using System.Linq;
using Unbound.Runtime;
using UnityEngine.Diagnostics;
using Xunit;

// Utils.ForceCrash with a value outside ForcedCrashCategory is unmeasured (tests/projects/forcecrash
// measures each category in its own Editor run): it is reported and the process keeps running.

namespace UnityEngine.Tests
{
    public sealed class ForceCrashTests
    {
        static int Hits(string member) => Stubs.HitReport().FirstOrDefault(h => h.Key == member).Value;

        [Fact]
        public void AValueOutsideTheCategoriesIsReportedWithoutACrash()
        {
            const string member = "Utils.ForceCrash with a value outside ForcedCrashCategory";
            int before = Hits(member);
            Utils.ForceCrash((ForcedCrashCategory)7);
            Utils.ForceCrash((ForcedCrashCategory)(-1));
            Assert.Equal(before + 2, Hits(member));
        }
    }
}
