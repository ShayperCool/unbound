using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Profiling;
using Object = UnityEngine.Object;

static class MeasureAudioSourceMemory
{
    [Serializable] sealed class Row { public string k, v; }
    static readonly List<string> Rows = new();
    static readonly AudioSourceCurveType[] Types = (AudioSourceCurveType[])Enum.GetValues(typeof(AudioSourceCurveType));

    static void Put(string key, string value) => Rows.Add(JsonUtility.ToJson(new Row { k = key, v = value }));

    static void Measure(string key, AudioSource source, AudioSourceCurveType type)
    {
        long before = Profiler.GetRuntimeMemorySizeLong(source);
        int keys = source.GetCustomCurve(type).length;
        long after = Profiler.GetRuntimeMemorySizeLong(source);
        Put(key, FormattableString.Invariant($"{keys}/{before}/{after}"));
    }

    static AnimationCurve Curve(int count)
    {
        var keys = new Keyframe[count];
        for (int i = 0; i < count; i++)
            keys[i] = new Keyframe(i / (float)Math.Max(1, count - 1), (i % 3) * 0.3f);
        return new AnimationCurve(keys);
    }

    static void MeasureCurve(AudioSourceCurveType type)
    {
        var source = new GameObject(type.ToString()).AddComponent<AudioSource>();
        Measure(type + ".initial", source, type);
        foreach (int count in new[] { 1, 2, 3, 4, 5, 9, 17, 32 })
        {
            source.SetCustomCurve(type, Curve(count));
            Measure(type + ".grow." + count, source, type);
        }
        source.SetCustomCurve(type, Curve(3));
        Measure(type + ".shrink", source, type);
        switch (type)
        {
            case AudioSourceCurveType.SpatialBlend: source.spatialBlend = 0.8f; break;
            case AudioSourceCurveType.Spread: source.spread = 75f; break;
            case AudioSourceCurveType.ReverbZoneMix: source.reverbZoneMix = 0.8f; break;
            case AudioSourceCurveType.CustomRolloff: source.rolloffMode = AudioRolloffMode.Linear; break;
        }
        Measure(type + ".scalarOrMode", source, type);
        var shrunkClone = Object.Instantiate(source);
        Measure(type + ".shrunkClone", shrunkClone, type);
        Object.DestroyImmediate(shrunkClone.gameObject);

        var originalCurve = Curve(6);
        source.SetCustomCurve(type, originalCurve);
        Measure(type + ".sixKeys", source, type);
        originalCurve.AddKey(1.1f, 1f);
        Measure(type + ".argumentChanged", source, type);
        var currentClone = Object.Instantiate(source);
        Measure(type + ".sixKeyClone", currentClone, type);
        Object.DestroyImmediate(currentClone.gameObject);
        Object.DestroyImmediate(source.gameObject);
    }

    static void MeasureRejection(AudioSource source, AudioSourceCurveType type, string mode, AnimationCurve curve)
    {
        string key = type + "." + mode;
        var logs = new List<string>();
        void OnLog(string message, string trace, LogType logType) => logs.Add(logType + "/" + message);
        Application.logMessageReceived += OnLog;
        try
        {
            source.SetCustomCurve(type, curve);
            Put(key + ".outcome", "returned");
        }
        catch (ArgumentNullException error) { Put(key + ".outcome", error.GetType().Name + "/" + error.ParamName); }
        finally { Application.logMessageReceived -= OnLog; }
        Put(key + ".logs", string.Join("|", logs));
        Measure(key + ".after", source, type);
    }

    static void MeasureAuthoredSource()
    {
        var source = GameObject.Find("Authored Audio Source").GetComponent<AudioSource>();
        foreach (var type in Types) Measure("scene." + type, source, type);
        var initialClone = Object.Instantiate(source);
        Measure("scene.initialClone", initialClone, AudioSourceCurveType.SpatialBlend);
        Object.DestroyImmediate(initialClone.gameObject);
        foreach (var type in Types) source.SetCustomCurve(type, Curve(1));
        Measure("scene.shrunk", source, AudioSourceCurveType.SpatialBlend);
        var shrunkClone = Object.Instantiate(source);
        Measure("scene.shrunkClone", shrunkClone, AudioSourceCurveType.SpatialBlend);
        Object.DestroyImmediate(shrunkClone.gameObject);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Capture()
    {
        string output = Environment.GetEnvironmentVariable("AUDIO_SOURCE_MEMORY_OUT");
        if (string.IsNullOrEmpty(output)) return;
        Put("unity", Application.unityVersion);
        foreach (var type in Types) MeasureCurve(type);

        var mixed = new GameObject("Mixed").AddComponent<AudioSource>();
        mixed.SetCustomCurve(AudioSourceCurveType.Spread, Curve(3));
        mixed.SetCustomCurve(AudioSourceCurveType.SpatialBlend, Curve(5));
        mixed.SetCustomCurve(AudioSourceCurveType.ReverbZoneMix, Curve(7));
        mixed.SetCustomCurve(AudioSourceCurveType.CustomRolloff, Curve(9));
        Measure("mixed", mixed, AudioSourceCurveType.CustomRolloff);
        var clip = AudioClip.Create("Clip", 48000, 1, 48000, false);
        mixed.clip = clip;
        Measure("mixed.withClip", mixed, AudioSourceCurveType.CustomRolloff);
        Object.DestroyImmediate(mixed.gameObject);
        Object.DestroyImmediate(clip);

        var input = new GameObject("Invalid input controls").AddComponent<AudioSource>();
        foreach (var type in Types)
        {
            input.SetCustomCurve(type, Curve(4));
            Measure(type + ".beforeRejection", input, type);
            MeasureRejection(input, type, "empty", Curve(0));
            MeasureRejection(input, type, "null", null);
        }
        Object.DestroyImmediate(input.gameObject);
        MeasureAuthoredSource();

        Rows.Add("{\"k\":\"complete\",\"v\":true}");
        File.WriteAllLines(Path.Combine(output, "values.jsonl"), Rows);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit(0);
#endif
    }
}
