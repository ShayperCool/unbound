using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;

public abstract class StackMessagesBase : MonoBehaviour
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected void LateUpdate()
    {
        StackMessages.Report("late");
        GC.KeepAlive(this);
    }
}

public sealed class StackMessages : StackMessagesBase
{
    static readonly List<string> Rows = new List<string>();
    static string s_Output;
    static int s_Updates;

    [Serializable]
    sealed class Row
    {
        public string k, v;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Begin()
    {
        s_Output = Environment.GetEnvironmentVariable("STACK_MESSAGES_OUT");
        if (!string.IsNullOrEmpty(s_Output)) new GameObject("StackMessages").AddComponent<StackMessages>();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void Update()
    {
        Report("update");
        GC.KeepAlive(this);
    }

    static void Put(string key, string value)
        => Rows.Add(JsonUtility.ToJson(new Row { k = key, v = value }));

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static unsafe void Report(string phase)
    {
        // Use the project prefix so the bytes and the file names can both be compared exactly.
        var buffer = new byte[4096];
        Array.Fill(buffer, (byte)0xAA);
        int count;
        string project = Path.GetDirectoryName(Application.dataPath).Replace('\\', '/') + "/";
        fixed (byte* p = buffer)
            count = Debug.ExtractStackTraceNoAlloc(p, buffer.Length, project);
        int written = buffer.Length;
        while (written > 0 && buffer[written - 1] == 0xAA) written--;
        Put(phase + ".bytes", count + "/" + written);
        Put(phase + ".text", Encoding.UTF8.GetString(buffer, 0, count));
        if (phase == "update")
        {
            s_Updates++;
            return;
        }

        Put("update.calls", s_Updates.ToString());
        Rows.Add("{\"k\":\"complete\",\"v\":true}");
        File.WriteAllLines(Path.Combine(s_Output, "values.jsonl"), Rows);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit(0);
#endif
    }
}
