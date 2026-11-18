using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>An enter-Play-mode request made while the Editor's domain loads for Play mode (Play mode
/// entry has begun, isPlaying still false), and what the Editor reports afterwards
/// (tests/projects/toolapis playRequest.*).</summary>
[InitializeOnLoad]
public static class ToolApisPlayRequest
{
    public static readonly List<string> Seen = new List<string>();
    static int s_Updates;

    static string State() => $"isPlaying {EditorApplication.isPlaying} willChange {EditorApplication.isPlayingOrWillChangePlaymode}";

    static ToolApisPlayRequest()
    {
        if (string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("TOOL_APIS_OUT"))) return;
        if (!EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying) return;
        Seen.Add("load before " + State());
        EditorApplication.EnterPlaymode();
        Seen.Add("load after " + State());
        EditorApplication.playModeStateChanged += s => Seen.Add("state " + s + " " + State());
        EditorApplication.update += Update;
    }

    static void Update()
    {
        if (++s_Updates > 4) { EditorApplication.update -= Update; return; }
        Seen.Add("update " + s_Updates + " " + State());
    }
}
