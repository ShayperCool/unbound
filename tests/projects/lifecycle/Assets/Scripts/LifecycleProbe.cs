using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>Which messages a behaviour gets, in which frame and phase, when it is enabled or added
/// at different points of a frame: from Update, from a coroutine (yield null), from LateUpdate, by
/// AddComponent, each for a behaviour with Start and one without. Writes the record to
/// <c>LIFECYCLE_PROBE_OUT</c> (JSON lines) at frame 8 and quits.</summary>
public class LifecycleProbe : MonoBehaviour
{
    internal static readonly StringBuilder Log = new StringBuilder();
    static int s_Frame0;

    internal static void Record(string who, string message) =>
        Log.Append($"{{\"frame\":{Time.frameCount - s_Frame0},\"who\":\"{who}\",\"message\":\"{message}\"}}\n");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        var go = new GameObject("LifecycleProbe");
        go.AddComponent<LifecycleProbe>();
    }

    WithStart m_UpdateWithStart, m_CoroutineWithStart, m_LateWithStart;
    WithoutStart m_UpdateWithoutStart, m_CoroutineWithoutStart, m_LateWithoutStart;

    WithStart MakeWithStart(string name)
    {
        var c = new GameObject(name).AddComponent<WithStart>();
        c.enabled = false;
        return c;
    }

    WithoutStart MakeWithoutStart(string name)
    {
        var c = new GameObject(name).AddComponent<WithoutStart>();
        c.enabled = false;
        return c;
    }

    void Start()
    {
        s_Frame0 = Time.frameCount;
        m_UpdateWithStart = MakeWithStart("update+start");
        m_UpdateWithoutStart = MakeWithoutStart("update-start");
        m_CoroutineWithStart = MakeWithStart("coroutine+start");
        m_CoroutineWithoutStart = MakeWithoutStart("coroutine-start");
        m_LateWithStart = MakeWithStart("late+start");
        m_LateWithoutStart = MakeWithoutStart("late-start");
        StartCoroutine(Routine());
    }

    IEnumerator Routine()
    {
        yield return null;
        yield return null;
        Record("probe", "coroutine enables");
        m_CoroutineWithStart.enabled = true;
        m_CoroutineWithoutStart.enabled = true;
    }

    void Update()
    {
        int frame = Time.frameCount - s_Frame0;
        if (frame == 2)
        {
            Record("probe", "update enables");
            m_UpdateWithStart.enabled = true;
            m_UpdateWithoutStart.enabled = true;
        }
        if (frame == 4)
        {
            Record("probe", "update adds");
            new GameObject("added+start").AddComponent<WithStart>();
            new GameObject("added-start").AddComponent<WithoutStart>();
        }
        if (frame == 8)
        {
            var output = Environment.GetEnvironmentVariable("LIFECYCLE_PROBE_OUT");
            if (!string.IsNullOrEmpty(output)) File.WriteAllText(output, Log.ToString());
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(0);
#else
            Application.Quit();
#endif
        }
    }

    void LateUpdate()
    {
        if (Time.frameCount - s_Frame0 == 3)
        {
            Record("probe", "late update enables");
            m_LateWithStart.enabled = true;
            m_LateWithoutStart.enabled = true;
        }
    }
}

public class WithStart : MonoBehaviour
{
    void OnEnable() => LifecycleProbe.Record(name, "OnEnable");
    void Start() => LifecycleProbe.Record(name, "Start");
    void Update() => LifecycleProbe.Record(name, "Update");
    void LateUpdate() => LifecycleProbe.Record(name, "LateUpdate");
}

public class WithoutStart : MonoBehaviour
{
    void OnEnable() => LifecycleProbe.Record(name, "OnEnable");
    void Update() => LifecycleProbe.Record(name, "Update");
    void LateUpdate() => LifecycleProbe.Record(name, "LateUpdate");
}
