using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine.TestTools;

public class SettledScene
{
    [UnityTest]
    public IEnumerator WaitAfterTheOpenSceneEntersPlay()
    {
        string output = Environment.GetEnvironmentVariable("EOF_STARTUP_OUT");
        Assert.That(output, Is.Not.Null.And.Not.Empty);
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        yield return null;
        yield return new EnterPlayMode(expectDomainReload: false);
        for (int tick = 0; tick != 120 && !File.Exists(output); ++tick)
            yield return null;
        Assert.That(File.Exists(output), "The playing scene did not finish the probe.");
        yield return new ExitPlayMode();
    }
}
