using System;
using System.IO;
using UnityEngine;

/// <summary>Records OnApplicationQuit for tests/projects/unchangedsaves (UNCHANGEDSAVES_OUT).</summary>
public class ProbeQuit : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("UNCHANGEDSAVES_OUT"))) return;
        new GameObject("ProbeQuit").AddComponent<ProbeQuit>();
    }

    void OnApplicationQuit() => File.AppendAllText(Environment.GetEnvironmentVariable("UNCHANGEDSAVES_OUT"), "onapplicationquit\tplay\n");
}
