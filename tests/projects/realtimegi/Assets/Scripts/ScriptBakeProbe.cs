using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Records the script bake (<c>ScriptBake</c>, editor code): its answer and events as Play mode
/// was entered, how many of the scene's renderers that contribute to GI then have a realtime
/// lightmap, and, in Play mode, what Lightmapping.Bake and BakeAsync answer, send and log.
/// </summary>
public class ScriptBakeProbe : MonoBehaviour
{
    public static string EnteringResult = "<not called>";
    public static string EnteringIsPlaying = "<not called>";
    public static readonly List<string> EnteringEvents = new List<string>();

    static string s_Out;
    readonly List<string> m_Lines = new List<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        s_Out = Environment.GetEnvironmentVariable("REALTIME_GI_SCRIPT_BAKE_OUT");
        if (!string.IsNullOrEmpty(s_Out)) new GameObject("ScriptBakeProbe").AddComponent<ScriptBakeProbe>();
    }

    void Write(string key, object value) =>
        m_Lines.Add("{\"key\":\"" + key + "\",\"value\":\"" + Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture).Replace("\"", "'") + "\"}");

    IEnumerator Start()
    {
        Write("entering.isPlaying", EnteringIsPlaying);
        Write("entering.result", EnteringResult);
        Write("entering.events", string.Join(",", EnteringEvents));
        int contributing = 0, lit = 0;
        var indices = new List<string>();
        foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
#if UNITY_EDITOR
            if ((UnityEditor.GameObjectUtility.GetStaticEditorFlags(r.gameObject) & UnityEditor.StaticEditorFlags.ContributeGI) == 0) continue;
#endif
            contributing++;
            indices.Add(r.name + ":" + r.realtimeLightmapIndex);
            if (r.realtimeLightmapIndex >= 0 && r.realtimeLightmapIndex < 65534) lit++;
        }
        Write("play.contributing", contributing);
        Write("play.withRealtimeLightmap", lit);
        indices.Sort(StringComparer.Ordinal);
        Write("play.indices", string.Join(" ", indices));
#if UNITY_EDITOR
        Write("play.lightingData", UnityEditor.Lightmapping.lightingDataAsset != null);
#endif
        for (int i = 0; i < 3; i++) yield return null;
#if UNITY_EDITOR
        var logs = new List<string>();
        var events = new List<string>();
        Application.LogCallback log = (m, s, t) => logs.Add(t + ": " + m);
        Action started = () => events.Add("started"), completed = () => events.Add("completed");
        Application.logMessageReceived += log;
        UnityEditor.Lightmapping.bakeStarted += started;
        UnityEditor.Lightmapping.bakeCompleted += completed;
        Write("play.bake", UnityEditor.Lightmapping.Bake());
        Write("play.bakeAsync", UnityEditor.Lightmapping.BakeAsync());
        UnityEditor.Lightmapping.bakeStarted -= started;
        UnityEditor.Lightmapping.bakeCompleted -= completed;
        Application.logMessageReceived -= log;
        Write("play.events", string.Join(",", events));
        Write("play.log", string.Join(" || ", logs));
#endif
        Write("complete", 1);
        File.WriteAllLines(s_Out + ".tmp", m_Lines);
        File.Move(s_Out + ".tmp", s_Out);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
