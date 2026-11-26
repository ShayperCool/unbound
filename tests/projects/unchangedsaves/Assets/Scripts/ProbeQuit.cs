using System;
using System.IO;
using UnityEngine;

/// <summary>Records OnApplicationQuit, OnDisable and OnDestroy of a scene object for
/// tests/projects/unchangedsaves (UNCHANGEDSAVES_OUT).</summary>
public class ProbeQuit : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("UNCHANGEDSAVES_OUT"))) return;
        new GameObject("ProbeQuit").AddComponent<ProbeQuit>();
    }

    static void Line(string kind) => File.AppendAllText(Environment.GetEnvironmentVariable("UNCHANGEDSAVES_OUT"), kind + "\tplay\n");

    void OnApplicationQuit() => Line("onapplicationquit");
    void OnDisable() => Line("ondisable");
    void OnDestroy() => Line("ondestroy");
}
