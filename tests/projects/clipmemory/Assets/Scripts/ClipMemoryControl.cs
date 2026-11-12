using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Profiling;

public sealed class ClipMemoryControl : MonoBehaviour
{
    static readonly List<string> Values = new List<string>();
    static string output;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Begin()
    {
        output = Environment.GetEnvironmentVariable("CLIP_MEMORY_OUT");
        if (output != null) new GameObject("Clip memory control").AddComponent<ClipMemoryControl>();
    }

    static void Put(string key, object value) => Values.Add("{\"k\":\"" + key + "\",\"v\":\""
        + Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) + "\"}");

    void Start()
    {
        // First-use allocation accounting remains a separate appfacts control.
        DestroyImmediate(AudioClip.Create("probe", 64, 1, 44100, false));
        AudioClip.PCMReaderCallback reader = data => Array.Clear(data, 0, data.Length);
        foreach (var shape in new[] { new[] { 1000, 1, 44100 }, new[] { 48000, 2, 48000 }, new[] { 100000, 4, 8000 }, new[] { 3217, 2, 32002 }, new[] { 3217, 2, 32003 } })
            foreach (bool stream in new[] { false, true })
                foreach (bool callback in new[] { false, true })
                {
                    string key = "samples" + shape[0] + ".channels" + shape[1] + ".rate" + shape[2]
                        + ".stream" + stream + ".reader" + callback;
                    var clip = AudioClip.Create("probe", shape[0], shape[1], shape[2], stream, callback ? reader : null);
                    Put(key + ".memory", Profiler.GetRuntimeMemorySizeLong(clip));
                    Put(key + ".samples", clip.samples);
                    Put(key + ".channels", clip.channels);
                    Put(key + ".frequency", clip.frequency);
                    DestroyImmediate(clip);
                }
        Values.Sort(StringComparer.Ordinal);
        Put("complete", 1);
        File.WriteAllLines(output, Values);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#endif
    }
}
