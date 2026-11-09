using System;
using System.IO;
using System.Threading;
using System.Collections.Generic;
using UnityEngine;

public class ResetDeliveryThreads : MonoBehaviour
{
    readonly int[,] listener = new int[64, 3];
    readonly int[,] source = new int[64, 3];
    int listenerCount, sourceCount, mainThread;
    ResetDeliveryProbe probe;
    bool hooked;
    void Awake() { mainThread = Thread.CurrentThread.ManagedThreadId; probe = GetComponent<ResetDeliveryProbe>(); ResetDeliveryProbe.BeforeExit = Save; }
    void Update()
    {
        if (hooked) return;
        var go = GameObject.Find("delivery-source");
        if (go == null) return;
        go.AddComponent<ResetDeliverySourceThread>().Probe = this; hooked = true;
    }
    void Record(int[,] rows, ref int count, int channels)
    {
        int phase = probe.Phase;
        if (phase < 0) return;
        if (count == rows.GetLength(0)) throw new Exception("Thread callback storage exhausted");
        rows[count, 0] = phase; rows[count, 1] = channels;
        rows[count, 2] = Thread.CurrentThread.ManagedThreadId == mainThread ? 1 : 0;
        count++;
    }
    void OnAudioFilterRead(float[] data, int channels) => Record(listener, ref listenerCount, channels);
    public void SourceRead(int channels) => Record(source, ref sourceCount, channels);
    static string Rows(int[,] values, int count)
    {
        var rows = new List<string>();
        for (int i = 0; i < count; i++) rows.Add("[" + values[i, 0] + "," + values[i, 1] + "," + values[i, 2] + "]");
        return "[" + string.Join(",", rows) + "]";
    }
    void Save(string output)
    {
        File.WriteAllText(Environment.GetEnvironmentVariable("SUSPEND_THREAD_OUT"),
            "{\"listener\":" + Rows(listener, listenerCount) + ",\"source\":" + Rows(source, sourceCount) + "}\n");
    }
}
public class ResetDeliverySourceThread : MonoBehaviour
{
    public ResetDeliveryThreads Probe;
    void OnAudioFilterRead(float[] data, int channels) { if (Probe != null) Probe.SourceRead(channels); }
}
