using System;
using UnityEngine;

/// <summary>Counts the dsp buffers it is called for and keeps the largest sample it saw.</summary>
public class StallFilter : MonoBehaviour
{
    public long calls;
    public volatile float peak;
    public volatile int lastLength, lastChannels;

    void OnAudioFilterRead(float[] data, int channels)
    {
        System.Threading.Interlocked.Increment(ref calls);
        float max = peak;
        foreach (float v in data) max = Math.Max(max, Math.Abs(v));
        peak = max;
        lastLength = data.Length;
        lastChannels = channels;
    }
}
