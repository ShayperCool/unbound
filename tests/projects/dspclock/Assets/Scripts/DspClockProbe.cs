using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;

// AudioSettings.dspTime as a game's scripts read it, once per frame: in whole dsp buffers, never
// backwards, following real time, and counting a long frame in the frame right after it; and a
// playable graph on its default clock (DirectorUpdateMode.DSPClock) counting the long frame there
// too. Rows are categories: absolute times depend on the audio device and when it started.
public class DspClockProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod] static void Install() { new GameObject("DspClockProbe").AddComponent<DspClockProbe>(); }
    [Serializable] class Row { public string key; public string value; }

    /// <summary>The frame whose Update sleeps <see cref="HitchMilliseconds"/>.</summary>
    const int HitchFrame = 60;
    const int HitchMilliseconds = 300;
    /// <summary>The frame from which real time and the dsp clock are compared.</summary>
    const int FirstCompared = 10;
    const int LastFrame = 150;
    /// <summary>Phase two: sources start at <see cref="SourcesFrame"/>, the frame before
    /// <see cref="StallFrame"/> starts a short clip and schedules one, and <see cref="StallFrame"/>'s
    /// Update sleeps <see cref="StallMilliseconds"/>; the frame after it reads what the stall did.</summary>
    const int SourcesFrame = 160, StallFrame = 200, StallMilliseconds = 3000, EndFrame = 210;

    StreamWriter output;
    int frames, length;
    double rate;
    readonly List<double> dsp = new List<double>(), realtime = new List<double>(), graphTime = new List<double>();
    PlayableGraph graph;
    Playable root;
    AudioSource tone, shortClip, scheduled;
    StallFilter filter;
    double stallStartDsp, toneTimeBefore, scheduledAt;
    long callsBefore;

    void Put(string key, object value) => output.WriteLine(JsonUtility.ToJson(new Row { key = key, value = Convert.ToString(value, CultureInfo.InvariantCulture) }));

    void Start()
    {
        string path = Environment.GetEnvironmentVariable("DSP_CLOCK_OUT");
        if (string.IsNullOrEmpty(path)) { enabled = false; return; }
        output = new StreamWriter(path);
        AudioSettings.GetDSPBufferSize(out length, out _);
        rate = AudioSettings.outputSampleRate;
        Put("buffer.matchesConfiguration", length == AudioSettings.GetConfiguration().dspBufferSize);
        graph = PlayableGraph.Create("DspClock");
        Put("graph.defaultClock", graph.GetTimeUpdateMode());
        root = Playable.Create(graph);
        ScriptPlayableOutput.Create(graph, "out").SetSourcePlayable(root);
        graph.Play();
    }

    void Update()
    {
        frames++;
        dsp.Add(AudioSettings.dspTime);
        realtime.Add(Time.realtimeSinceStartupAsDouble);
        if (frames == HitchFrame) System.Threading.Thread.Sleep(HitchMilliseconds);
        if (frames == SourcesFrame) StartSources();
        if (frames == StallFrame - 1)
        {
            shortClip.Play();
            scheduledAt = AudioSettings.dspTime + 1.0;
            scheduled.PlayScheduled(scheduledAt);
        }
        if (frames == StallFrame)
        {
            stallStartDsp = AudioSettings.dspTime;
            toneTimeBefore = tone.time;
            callsBefore = System.Threading.Interlocked.Read(ref filter.calls);
            filter.peak = 0f;
            System.Threading.Thread.Sleep(StallMilliseconds);
        }
        if (frames == StallFrame + 1) ReadStall();
    }

    static AudioClip Tone(string name, float seconds, int sampleRate)
    {
        int samples = Mathf.RoundToInt(seconds * sampleRate);
        var data = new float[samples];
        for (int i = 0; i < samples; i++) data[i] = 0.5f * Mathf.Sin(2f * Mathf.PI * 440f * i / sampleRate);
        var clip = AudioClip.Create(name, samples, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    AudioSource Source(string name, AudioClip clip)
    {
        var source = new GameObject(name).AddComponent<AudioSource>();
        source.clip = clip;
        source.spatialBlend = 0f;
        return source;
    }

    void StartSources()
    {
        gameObject.AddComponent<AudioListener>();
        int sampleRate = AudioSettings.outputSampleRate;
        tone = Source("Tone", Tone("tone", 10f, sampleRate));
        filter = tone.gameObject.AddComponent<StallFilter>();
        tone.Play();
        shortClip = Source("Short", Tone("short", 0.5f, sampleRate));
        scheduled = Source("Scheduled", Tone("scheduled", 10f, sampleRate));
    }

    /// <summary>What a 3 s stall of the main thread did, read in the frame after it: the dsp clock
    /// counted it, the playing source's filter ran for every dsp buffer of it with the source's
    /// signal (a 0.5 tone at centre pan: 0.354 per channel), the source moved by it, a clip shorter
    /// than it ended, and a source scheduled within it started at its time.</summary>
    void ReadStall()
    {
        double step = length / rate;
        double counted = AudioSettings.dspTime - stallStartDsp;
        double stall = StallMilliseconds / 1000.0;
        Put("stall.dspCounted", counted >= 0.9 * stall && counted <= stall + 0.5);
        long calls = System.Threading.Interlocked.Read(ref filter.calls) - callsBefore;
        Put("stall.filterRanEveryBuffer", Math.Abs(calls - counted / step) <= 3);
        Put("stall.filterHadSignal", filter.peak > 0.3f);
        Put("stall.filterBufferLength", filter.lastLength == length * filter.lastChannels);
        Put("stall.toneMoved", Math.Abs(tone.time - toneTimeBefore - counted) <= 3 * step + 0.02);
        Put("stall.shortClipEnded", !shortClip.isPlaying);
        bool started = scheduled.isPlaying && scheduled.time > 0f;
        Put("stall.scheduledStarted", started);
        Put("stall.scheduledFromItsTime", started && Math.Abs(scheduled.time - (AudioSettings.dspTime - scheduledAt)) <= 3 * step + 0.02);
    }

    void LateUpdate()
    {
        graphTime.Add(root.GetTime());
        if (frames == LastFrame) Measure();
        if (frames < EndFrame) return;
        try { Finish(); }
        catch (Exception e) { Debug.LogException(e); output.Dispose(); EditorApplication.Exit(1); }
    }

    void Finish()
    {
        graph.Destroy();
        Put("complete", 1);
        output.Dispose();
        EditorApplication.Exit(0);
    }

    void Measure()
    {
        double step = length / rate;
        bool whole = true, monotonic = true;
        for (int i = 0; i < dsp.Count; i++)
        {
            double buffers = dsp[i] / step;
            whole &= Math.Abs(buffers - Math.Round(buffers)) < 1e-6;
            if (i > 0) monotonic &= dsp[i] >= dsp[i - 1];
        }
        Put("dsp.wholeBuffers", whole);
        Put("dsp.monotonic", monotonic);
        // Over the compared frames (the hitch included) the dsp clock moves as real time does,
        // within a tenth plus three buffers.
        int first = FirstCompared - 1, last = dsp.Count - 1;
        double real = realtime[last] - realtime[first], clock = dsp[last] - dsp[first];
        Put("dsp.followsRealTime", Math.Abs(clock - real) <= 0.1 * real + 3 * step);
        // The frame after the hitch reads a clock that has counted it.
        double hitch = HitchMilliseconds / 1000.0;
        Put("dsp.hitchCountedNextFrame", dsp[HitchFrame] - dsp[HitchFrame - 1] >= 0.8 * hitch);
        Put("dsp.hitchNotCountedBefore", dsp[HitchFrame - 1] - dsp[HitchFrame - 2] < 0.5 * hitch);
        // The graph's time, read after DirectorUpdate: it moves by the clock's advance sampled at
        // the frame's start, so the frame after the hitch has it too.
        Put("graph.hitchCountedNextFrame", graphTime[HitchFrame] - graphTime[HitchFrame - 1] >= 0.8 * hitch);
        bool graphWhole = true;
        for (int i = 0; i < graphTime.Count; i++)
        {
            double buffers = graphTime[i] / step;
            graphWhole &= Math.Abs(buffers - Math.Round(buffers)) < 1e-6;
        }
        Put("graph.wholeBuffers", graphWhole);
        // Between the audio clock's buffers the graph's time still moves, in every frame after the
        // one that made it (Start: its time is 0 in that frame's LateUpdate).
        bool everyFrame = true;
        for (int i = 1; i < graphTime.Count; i++) everyFrame &= graphTime[i] > graphTime[i - 1];
        Put("graph.movesEveryFrame", everyFrame);
    }
}
