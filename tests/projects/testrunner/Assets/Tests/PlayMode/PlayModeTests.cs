using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>Play Mode tests: coroutines over frames and time, MonoBehaviour lifecycles,
/// [UnitySetUp]/[UnityTearDown], LogAssert, failures inside coroutines.</summary>
public class PlayModeTests
{
    GameObject m_Object;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        Debug.Log("PlayMode UnitySetUp");
        m_Object = new GameObject("PlayModeObject");
        yield return null;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        Debug.Log("PlayMode UnityTearDown");
        Object.Destroy(m_Object);
        yield return null;
    }

    [Test]
    public void IsPlaying() => Assert.IsTrue(Application.isPlaying);

    [UnityTest]
    public IEnumerator CountsUpdates()
    {
        var counter = m_Object.AddComponent<PlayCounter>();
        Assert.AreEqual(1, counter.awakes);
        Assert.AreEqual(0, counter.starts);
        yield return null;
        Assert.AreEqual(1, counter.starts);
        int before = counter.updates;
        for (int i = 0; i < 5; i++) yield return null;
        Debug.Log("updates over 5 frames: " + (counter.updates - before));
        Assert.AreEqual(5, counter.updates - before);
    }

    [UnityTest]
    public IEnumerator WaitsForFixedUpdate()
    {
        var counter = m_Object.AddComponent<PlayCounter>();
        yield return new WaitForFixedUpdate();
        Assert.Greater(counter.fixedUpdates, 0);
    }

    [UnityTest]
    public IEnumerator WaitsForSeconds()
    {
        float start = Time.time;
        yield return new WaitForSeconds(0.1f);
        Assert.GreaterOrEqual(Time.time - start, 0.1f);
    }

    [UnityTest]
    public IEnumerator ExpectsALogInAFrame()
    {
        LogAssert.Expect(LogType.Warning, "a warning");
        yield return null;
        Debug.LogWarning("a warning");
        yield return null;
    }

    [UnityTest]
    public IEnumerator FailsInsideACoroutine()
    {
        yield return null;
        Assert.AreEqual("a", "b");
    }

    [UnityTest]
    public IEnumerator FailsOnAnUnexpectedException()
    {
        m_Object.AddComponent<Thrower>();
        yield return null;
        yield return null;
    }
}

public class PlayCounter : MonoBehaviour
{
    public int awakes, starts, updates, fixedUpdates;
    void Awake() => awakes++;
    void Start() => starts++;
    void Update() => updates++;
    void FixedUpdate() => fixedUpdates++;
}


public class Thrower : MonoBehaviour
{
    void Update() => throw new System.ArgumentException("thrown from Update");
}
