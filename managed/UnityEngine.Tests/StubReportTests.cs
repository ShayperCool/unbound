using System;
using System.Collections.Generic;
using System.IO;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The stub report a session writes at its end under UNBOUND_TRACE=stubs: the members it
    /// used and how often, with those of the sessions it continues after a relaunch.</summary>
    public class StubReportTests
    {
        static KeyValuePair<string, int> Hit(string member, int count) => new(member, count);

        [Fact]
        public void AFreshSessionWritesItsOwnHitsMostUsedFirst()
        {
            var dir = Path.Combine(Path.GetTempPath(), "unbound-stub-report-" + Guid.NewGuid().ToString("N"));
            var path = Path.Combine(dir, Stubs.ReportFile);
            try
            {
                Directory.CreateDirectory(dir);
                // An earlier run's report is not continued by a session that was not relaunched.
                File.WriteAllText(path, "7\tUnityEditor.Old.Member\n");
                var log = Stubs.WriteReport(path, new[] { Hit("B.Member", 2), Hit("A.Member", 2), Hit("C.Member", 5) }, false);
                Assert.Equal("5\tC.Member\n2\tA.Member\n2\tB.Member\n", File.ReadAllText(path));
                Assert.StartsWith("[trace] stubs: 3 member(s) used 9 time(s), written to " + path + "\n", log);
                Assert.EndsWith("5\tC.Member\n2\tA.Member\n2\tB.Member\n", log);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void ARelaunchedSessionAddsTheHitsOfTheSessionsItContinues()
        {
            var dir = Path.Combine(Path.GetTempPath(), "unbound-stub-report-" + Guid.NewGuid().ToString("N"));
            var path = Path.Combine(dir, Stubs.ReportFile);
            try
            {
                // The first session (the import, before a reload of the scripts) ...
                Stubs.WriteReport(path, new[] { Hit("AnimationUtility.SetKeyBroken", 260), Hit("Shared.Member", 1) }, false);
                // ... and the one relaunched after it (Play), which counts from zero again.
                var log = Stubs.WriteReport(path, new[] { Hit("Shared.Member", 3), Hit("Material.GetTag", 4) }, true);
                Assert.Equal("260\tAnimationUtility.SetKeyBroken\n4\tMaterial.GetTag\n4\tShared.Member\n", File.ReadAllText(path));
                Assert.Contains("3 member(s) used 268 time(s) with the sessions this one continues", log);
                // A relaunched session with no earlier report has its own hits.
                File.Delete(path);
                Stubs.WriteReport(path, new[] { Hit("Shared.Member", 3) }, true);
                Assert.Equal("3\tShared.Member\n", File.ReadAllText(path));
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void LinesThatAreNoCountAndMemberAreSkippedAndKeysStayOnOneLine()
        {
            var earlier = "3\tA.Member\r\nnot a count\tB.Member\n-2\tC.Member\n\tD.Member\nno tab\n4\tE.Member with\ttab\n";
            var merged = Stubs.Merge(new[] { Hit("UXML object <Columns>\nin Window.uxml", 1), Hit("A.Member", 1) }, earlier);
            Assert.Equal(new[]
            {
                Hit("A.Member", 4),
                Hit("E.Member with\ttab", 4),
                Hit("UXML object <Columns> in Window.uxml", 1),
            }, merged);
            // What a report holds reads back as it was.
            Assert.Equal(merged, Stubs.Merge(Array.Empty<KeyValuePair<string, int>>(), Stubs.Format(merged)));
        }
    }
}
