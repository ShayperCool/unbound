using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class PlayCancelProbe : MonoBehaviour
{
    static string s_Out;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        s_Out = Environment.GetEnvironmentVariable("LM_HOOKS_PLAY_OUT");
        if (!string.IsNullOrEmpty(s_Out)) new GameObject("PlayCancelProbe").AddComponent<PlayCancelProbe>();
    }

    void Update()
    {
        if (Time.frameCount < 2) return;
        enabled = false;
        var lines = new List<string>();
        try
        {
#if UNITY_EDITOR
            int cancelled = 0;
            var logs = new List<string>();
            Action onCancel = () => cancelled++;
            Application.LogCallback onLog = (m, s, t) => logs.Add(t + ":" + m);
            Lightmapping.bakeCancelled += onCancel;
            Application.logMessageReceived += onLog;
            string exStr = "ok";
            try { Lightmapping.Cancel(); }
            catch (Exception ex) { exStr = "EX:" + ex.GetType().Name; }
            Application.logMessageReceived -= onLog;
            Lightmapping.bakeCancelled -= onCancel;
            lines.Add("{\"k\":\"play.cancel\",\"v\":\"" + exStr + "|cancelled=" + cancelled + "|running=" + Lightmapping.isRunning + "|logs=" + logs.Count + "\"}");
#endif
            lines.Add("{\"k\":\"complete\",\"v\":true}");
            Directory.CreateDirectory(Path.GetDirectoryName(s_Out));
            File.WriteAllLines(s_Out, lines);
#if UNITY_EDITOR
            EditorApplication.Exit(0);
#else
            Application.Quit();
#endif
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
#if UNITY_EDITOR
            EditorApplication.Exit(1);
#endif
        }
    }
}
