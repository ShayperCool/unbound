using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>Edit Mode tests covering what a test run reports: passes, failures, errors, ignored
/// and parameterized tests, setup/teardown order, logs, LogAssert and editor coroutines.</summary>
public class EditModeTests
{
    static int s_SetUps;

    [OneTimeSetUp]
    public void OneTimeSetUp() => Debug.Log("EditMode OneTimeSetUp");

    [OneTimeTearDown]
    public void OneTimeTearDown() => Debug.Log("EditMode OneTimeTearDown");

    [SetUp]
    public void SetUp()
    {
        s_SetUps++;
        Debug.Log("EditMode SetUp " + s_SetUps);
    }

    [TearDown]
    public void TearDown() => Debug.Log("EditMode TearDown");

    [Test]
    public void Passes() => Assert.AreEqual(4, 2 + 2);

    [Test]
    public void FailsAnAssertion() => Assert.AreEqual(5, 2 + 2, "two and two");

    [Test]
    public void ThrowsAnException() => throw new System.InvalidOperationException("thrown on purpose");

    [Test]
    [Ignore("ignored on purpose")]
    public void IsIgnored() { }

    [TestCase(1, 2, 3)]
    [TestCase(2, 2, 5)]
    public void Adds(int a, int b, int sum) => Assert.That(a + b, Is.EqualTo(sum));

    [Test]
    public void LogsAndExpectsAnError()
    {
        Debug.Log("a message");
        LogAssert.Expect(LogType.Error, "expected error");
        Debug.LogError("expected error");
    }

    [Test]
    public void FailsOnAnUnexpectedError() => Debug.LogError("unexpected error");

    [Test]
    public void IsNotPlaying() => Assert.IsFalse(Application.isPlaying);

    [Test]
    public void CreatesAGameObject()
    {
        var go = new GameObject("EditModeObject");
        var counter = go.AddComponent<EditCounter>();
        Assert.AreEqual(0, counter.awakes, "Awake does not run in Edit Mode");
        Object.DestroyImmediate(go);
    }

    [UnityTest]
    public IEnumerator YieldsEditorFrames()
    {
        int frames = 0;
        EditorApplication.CallbackFunction tick = () => frames++;
        EditorApplication.update += tick;
        for (int i = 0; i < 3; i++) yield return null;
        EditorApplication.update -= tick;
        Debug.Log("editor updates while yielding 3 times: " + frames);
        Assert.Greater(frames, 0);
    }

    [UnityTest]
    public IEnumerator FailsAfterYielding()
    {
        yield return null;
        Assert.Fail("failed after a frame");
    }
}

public class EditCounter : MonoBehaviour
{
    public int awakes;
    void Awake() => awakes++;
}
