using System;
using System.Collections;
using UnityEngine.TestTools;

/// <summary>The probe's steps in Edit mode (keys `edit.*`): run by `-runTests -testPlatform
/// EditMode` in the Editor and `unbound test --platform EditMode` in Unbound.</summary>
public class ProjectWritesEditTests
{
    [UnityTest]
    public IEnumerator Writes()
    {
        // Once per run: a session that starts again (Unbound starts the test run again to compile
        // new defines) finds the values written.
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PROJECT_WRITES_OUT")) || ProjectWritesProbe.Done()) yield break;
        // Errors a step logs are recorded values, not this test's failure.
        LogAssert.ignoreFailingMessages = true;
        // First, by itself: a package import ends some time after its call (the Editor unpacks
        // in the background), a failed one within it. The window asked for never comes in batch
        // mode, so nothing is waited for there.
        foreach (var step in ProjectWritesProbe.PackageSteps)
        {
            ProjectWritesProbe.PackageCall("edit", step);
            if (step == "14.package.interactive") continue;
            var until = DateTime.UtcNow.AddSeconds(30);
            while (!ProjectWritesProbe.PackageEnded && DateTime.UtcNow < until) yield return null;
            for (int i = 0; i < 5; i++) yield return null;
            ProjectWritesProbe.PackageRows(step + ".later");
        }
        ProjectWritesProbe.PackageEnd();
        ProjectWritesProbe.Steps("edit");
        // Written before anything is awaited: the define and package changes may reload the
        // domain once the Editor gets to them. run.py reads what is on disk after the run.
        ProjectWritesProbe.Finish();
        for (int i = 0; i < 30; i++) yield return null;
    }
}
