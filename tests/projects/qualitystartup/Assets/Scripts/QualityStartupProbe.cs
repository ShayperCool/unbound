using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>The quality level Play mode starts with; see README.md.</summary>
public class QualityStartupProbe : MonoBehaviour
{
    static readonly List<string> s_Lines = new List<string>();

    static void Add(string key, object value)
    {
        string json = value is string s ? "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""
            : value is bool b ? (b ? "true" : "false")
            : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        s_Lines.Add("{\"k\": \"" + key + "\", \"v\": " + json + "}");
    }

    static void Level(string at)
    {
        int level = QualitySettings.GetQualityLevel();
        Add(at + ".level", level);
        Add(at + ".name", QualitySettings.names[level]);
        Add(at + ".pixelLightCount", QualitySettings.pixelLightCount);
        Add(at + ".shadowDistance", QualitySettings.shadowDistance);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Registration() => Level("registration");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void BeforeScene() => Level("beforeSceneLoad");

    int m_Frames;

    void Awake()
    {
        Add("names", string.Join(",", QualitySettings.names));
#if UNITY_EDITOR
        Add("activeBuildTarget", UnityEditor.EditorUserBuildSettings.activeBuildTarget.ToString());
#endif
        Level("awake");
    }

    void Update()
    {
        if (++m_Frames != 3) return;
        Level("frame3");
        Add("complete", true);
        string dir = Environment.GetEnvironmentVariable("QUALITY_STARTUP_OUT");
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
            File.WriteAllLines(Path.Combine(dir, "values.jsonl"), s_Lines);
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.ExitPlaymode();
        UnityEditor.EditorApplication.delayCall += () => UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
