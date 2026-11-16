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

    StreamWriter output;
    int frames, length;
    double rate;
    readonly List<double> dsp = new List<double>(), realtime = new List<double>(), graphTime = new List<double>();
    PlayableGraph graph;
    Playable root;

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
    }

    void LateUpdate()
    {
        graphTime.Add(root.GetTime());
        if (frames < LastFrame) return;
        try { Finish(); }
        catch (Exception e) { Debug.LogException(e); output.Dispose(); EditorApplication.Exit(1); }
    }

    void Finish()
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
        // Between the audio clock's buffers the graph's time still moves, in every frame from the
        // second on (the first sample starts it).
        bool everyFrame = true;
        for (int i = 2; i < graphTime.Count; i++) everyFrame &= graphTime[i] > graphTime[i - 1];
        Put("graph.movesEveryFrame", everyFrame);
        graph.Destroy();
        Put("complete", 1);
        output.Dispose();
        EditorApplication.Exit(0);
    }
}
