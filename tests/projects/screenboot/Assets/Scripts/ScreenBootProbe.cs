using System;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// The screen as scripts see it while Play mode starts: Screen.width/height and safeArea at each
/// RuntimeInitializeOnLoadMethod stage, in the first component's Awake and
/// Start, and in the first three Updates. Writes values.jsonl in SCREEN_BOOT_OUT and exits.
/// </summary>
public sealed class ScreenBootProbe : MonoBehaviour
{
    static readonly StringBuilder s_Out = new StringBuilder();
    int m_Frame;

    static void Rec(string when)
    {
        var safe = Screen.safeArea;
        s_Out.Append("{\"k\":\"").Append(when).Append("\",\"v\":\"")
            .Append(Screen.width).Append('x').Append(Screen.height)
            .Append(" safe ").Append(safe.x).Append(',').Append(safe.y).Append(',').Append(safe.width).Append(',').Append(safe.height)
            .Append("\"}\n");
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void SubsystemRegistration() => Rec("subsystemRegistration");
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    static void AfterAssembliesLoaded() => Rec("afterAssembliesLoaded");
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
    static void BeforeSplashScreen() => Rec("beforeSplashScreen");
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void BeforeSceneLoad()
    {
        Rec("beforeSceneLoad");
        new GameObject("ScreenBootProbe").AddComponent<ScreenBootProbe>();
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AfterSceneLoad() => Rec("afterSceneLoad");

    void Awake() => Rec("awake");
    void Start() => Rec("start");

    void Update()
    {
        m_Frame++;
        Rec("update" + m_Frame);
        if (m_Frame < 3) return;
        s_Out.Append("{\"k\":\"complete\",\"v\":true}\n");
        string dir = Environment.GetEnvironmentVariable("SCREEN_BOOT_OUT");
        if (string.IsNullOrEmpty(dir)) Debug.Log(s_Out.ToString());
        else
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "values.jsonl"), s_Out.ToString());
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit(0);
#endif
    }
}
