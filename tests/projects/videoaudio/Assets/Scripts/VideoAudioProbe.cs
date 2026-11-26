using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Video;

/// <summary>
/// Records what a VideoPlayer does with its clip's audio track in Editor Play mode. The clip
/// (Assets/Video/tone.webm: VP8 64x48, 10 frames a second, 3 seconds, a Vorbis stereo track at
/// 48 kHz) holds a tone that steps every half second through 300, 450, 600, 750, 900 and 1050 Hz,
/// its left channel at amplitude 0.5 and its right at 0.25, so the frequency heard tells the
/// clip time it came from. A filter on the target AudioSource (OnAudioFilterRead) measures each
/// block it is given: its RMS per channel and its frequency from the left channel's zero
/// crossings. Each scenario's phases are summarized as values that do not depend on the audio
/// thread's timing: whether any sound came, the order of the tone steps heard, the RMS of the
/// steady blocks, and the listener's output (AudioListener.GetOutputData) for Direct output.
/// </summary>
public sealed class VideoAudioProbe : MonoBehaviour
{
    sealed class Block
    {
        public string Phase;
        public double RmsLeft, RmsRight, Frequency, Dsp;
        public int Frames;
    }

    sealed class Recorder : MonoBehaviour
    {
        internal readonly List<Block> Blocks = new List<Block>();
        internal volatile string Phase = "";
        int m_Rate;

        void Awake() => m_Rate = AudioSettings.outputSampleRate;

        void OnAudioFilterRead(float[] data, int channels)
        {
            string phase = Phase;
            if (phase == "" || channels < 2) return;
            int frames = data.Length / channels;
            double left = 0, right = 0;
            int crossings = 0;
            for (int i = 0; i < frames; i++)
            {
                float l = data[i * channels], r = data[i * channels + 1];
                left += l * l;
                right += r * r;
                if (i > 0 && (data[(i - 1) * channels] < 0) != (l < 0)) crossings++;
            }
            var block = new Block
            {
                Phase = phase, Frames = frames,
                RmsLeft = Math.Sqrt(left / frames), RmsRight = Math.Sqrt(right / frames),
                Frequency = crossings * 0.5 * m_Rate / frames,
                Dsp = AudioSettings.dspTime,
            };
            lock (Blocks) Blocks.Add(block);
        }
    }

    StringBuilder m_Output;
    string m_Directory;
    VideoPlayer m_Player;
    AudioSource m_Source;
    Recorder m_Recorder;
    readonly List<float> m_ListenerRms = new List<float>();
    bool m_SampleListener;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void StartProbe()
    {
        var directory = Environment.GetEnvironmentVariable("VIDEO_AUDIO_OUT");
        if (string.IsNullOrEmpty(directory)) return;
        var probe = new GameObject("VideoAudioProbe").AddComponent<VideoAudioProbe>();
        probe.m_Directory = directory;
        probe.m_Output = new StringBuilder();
        new GameObject("Listener").AddComponent<AudioListener>();
    }

    void Put(string key, params object[] values) =>
        m_Output.Append("{\"k\":").Append(Json(key)).Append(",\"v\":").Append(Json(values)).Append("}\n");

    IEnumerator Start()
    {
        var clip = AssetDatabase.LoadAssetAtPath<VideoClip>("Assets/Video/tone.webm");
        Put("clip", clip.audioTrackCount, clip.GetAudioChannelCount(0), (int)clip.GetAudioSampleRate(0), clip.length, AudioSettings.outputSampleRate);
        yield return Scenario("play", clip, VideoAudioOutputMode.AudioSource, 1f, false, 1f, true, Play(3.4f));
        yield return Scenario("volume", clip, VideoAudioOutputMode.AudioSource, 0.5f, false, 1f, true, Play(1.2f));
        yield return Scenario("pause", clip, VideoAudioOutputMode.AudioSource, 1f, false, 1f, true, PauseResume());
        yield return Scenario("seek", clip, VideoAudioOutputMode.AudioSource, 1f, false, 1f, true, Seek());
        yield return Scenario("speed2", clip, VideoAudioOutputMode.AudioSource, 1f, false, 2f, true, Play(1.2f));
        yield return Scenario("trackOff", clip, VideoAudioOutputMode.AudioSource, 1f, false, 1f, false, Play(1.0f));
        yield return Scenario("direct", clip, VideoAudioOutputMode.Direct, 1f, false, 1f, true, Play(1.2f));
        yield return Scenario("none", clip, VideoAudioOutputMode.None, 1f, false, 1f, true, Play(1.0f));
        yield return Scenario("loop", clip, VideoAudioOutputMode.AudioSource, 1f, true, 1f, true, Play(4.2f));
        m_Output.Append("{\"k\":\"complete\",\"v\":true}\n");
        Directory.CreateDirectory(m_Directory);
        File.WriteAllText(Path.Combine(m_Directory, "values.jsonl"), m_Output.ToString());
        EditorApplication.Exit(0);
    }

    void Update()
    {
        if (!m_SampleListener) return;
        var samples = new float[1024];
        AudioListener.GetOutputData(samples, 0);
        m_ListenerRms.Add((float)Math.Sqrt(samples.Select(s => (double)s * s).Average()));
    }

    /// <summary>A fresh player and source with the scenario's settings, prepared, then its steps.</summary>
    IEnumerator Scenario(string name, VideoClip clip, VideoAudioOutputMode mode, float volume, bool loop, float speed, bool trackEnabled, IEnumerator steps)
    {
        var go = new GameObject("Video " + name);
        m_Source = go.AddComponent<AudioSource>();
        m_Source.playOnAwake = false;
        m_Source.volume = volume;
        m_Recorder = go.AddComponent<Recorder>();
        m_Player = go.AddComponent<VideoPlayer>();
        m_Player.playOnAwake = false;
        m_Player.renderMode = VideoRenderMode.APIOnly;
        m_Player.clip = clip;
        m_Player.isLooping = loop;
        m_Player.audioOutputMode = mode;
        m_Player.EnableAudioTrack(0, trackEnabled);
        if (mode == VideoAudioOutputMode.AudioSource) m_Player.SetTargetAudioSource(0, m_Source);
        m_Player.Prepare();
        float waited = 0;
        while (!m_Player.isPrepared && waited < 10f) { waited += Time.unscaledDeltaTime; yield return null; }
        Put(name + ".prepared", m_Player.isPrepared, m_Player.audioTrackCount, m_Player.controlledAudioTrackCount, m_Player.canSetPlaybackSpeed,
            m_Player.GetTargetAudioSource(0) == m_Source, m_Player.IsAudioTrackEnabled(0), m_Player.GetDirectAudioVolume(0), m_Player.GetDirectAudioMute(0));
        m_Player.playbackSpeed = speed;
        m_ListenerRms.Clear();
        m_SampleListener = true;
        yield return steps;
        m_SampleListener = false;
        m_Recorder.Phase = "";
        m_Player.Stop();
        Put(name + ".end", m_Player.isPlaying, m_Source.isPlaying, m_Source.clip == null ? "null" : m_Source.clip.name);
        Summarize(name);
        Destroy(go);
        yield return new WaitForSecondsRealtime(0.3f);
    }

    double m_PlayDsp, m_EndDsp;

    IEnumerator Play(float seconds)
    {
        m_Recorder.Phase = "a";
        m_EndDsp = 0;
        m_Player.loopPointReached += _ => { if (m_EndDsp == 0) m_EndDsp = AudioSettings.dspTime; };
        m_PlayDsp = AudioSettings.dspTime;
        m_Player.Play();
        yield return new WaitForSecondsRealtime(seconds);
        Put(m_Player.name + ".playing", m_Player.isPlaying, m_Source.isPlaying);
    }

    IEnumerator PauseResume()
    {
        m_Recorder.Phase = "a";
        m_Player.Play();
        yield return new WaitForSecondsRealtime(0.8f);
        m_Player.Pause();
        yield return new WaitForSecondsRealtime(0.15f);
        m_Recorder.Phase = "paused";
        yield return new WaitForSecondsRealtime(0.5f);
        m_Recorder.Phase = "resumed";
        m_Player.Play();
        yield return new WaitForSecondsRealtime(0.6f);
    }

    /// <summary>A quarter of a second of play, then paused, a seek to 2 seconds (the 900 Hz step) and its
    /// completion awaited, then played again.</summary>
    IEnumerator Seek()
    {
        m_Recorder.Phase = "a";
        m_Player.Play();
        yield return new WaitForSecondsRealtime(0.25f);
        m_Player.Pause();
        yield return new WaitForSecondsRealtime(0.15f);
        m_Recorder.Phase = "";
        bool done = false;
        m_Player.seekCompleted += _ => done = true;
        m_Player.time = 2.0;
        float waited = 0;
        while (!done && waited < 5f) { waited += Time.unscaledDeltaTime; yield return null; }
        Put("seek.completed", done, Math.Round(m_Player.time, 1));
        m_Recorder.Phase = "afterSeek";
        m_Player.Play();
        // (The Editor's sound after the seek is steady with these waits; after half a second of
        // play before the pause it came in only one of three captures.)
        yield return new WaitForSecondsRealtime(0.35f);
    }

    /// <summary>The tone steps of a phase as far as they do not depend on how long the phase ran
    /// (the main thread can wake late under load): a clip played to its end gives them all; a
    /// looping one the last step before it wrapped and the first after; any other phase its first
    /// step and whether each next step followed the one before (at speed 2, where skipped chunks
    /// can cut a step short, whether it reached four steps instead).</summary>
    static object StepSummary(string name, List<int> steps)
    {
        if (name == "play") return steps.ToArray();
        if (name == "loop")
        {
            int at = Enumerable.Range(1, Math.Max(0, steps.Count - 1)).FirstOrDefault(i => steps[i] < steps[i - 1]);
            return at > 0 ? new object[] { steps[at - 1], steps[at] } : new object[] { steps.Count > 0 ? steps[steps.Count - 1] : -1, -1 };
        }
        bool ascending = Enumerable.Range(1, Math.Max(0, steps.Count - 1)).All(i => steps[i] == steps[i - 1] + 1);
        // At speed 2 the 1.2 s phase reaches at least four steps (about three at speed 1); a late
        // main thread only adds steps.
        if (name == "speed2") return new object[] { steps.Count > 0 ? steps[0] : -1, steps.Count >= 4 };
        return new object[] { steps.Count > 0 ? steps[0] : -1, ascending };
    }

    /// <summary>Per phase: whether sound came, the tone steps heard in order (a step counts when two
    /// blocks in a row hear it), the RMS of the blocks of a step (to 2 decimals), and the listener's
    /// output.</summary>
    void Summarize(string name)
    {
        List<Block> blocks;
        lock (m_Recorder.Blocks) blocks = m_Recorder.Blocks.ToList();
        foreach (var phase in blocks.Select(b => b.Phase).Distinct())
        {
            var mine = blocks.Where(b => b.Phase == phase).ToList();
            var sounding = mine.Where(b => b.RmsLeft > 0.01).ToList();
            var steps = new List<int>();
            int previous = -1, run = 0;
            foreach (var b in sounding)
            {
                int step = (int)Math.Round((b.Frequency - 300) / 150);
                if (step == previous) run++; else { previous = step; run = 1; }
                if (run == 2 && (steps.Count == 0 || steps[steps.Count - 1] != step)) steps.Add(step);
            }
            var steady = sounding.Where(b => Math.Abs((b.Frequency - 300) / 150 - Math.Round((b.Frequency - 300) / 150)) < 0.2).ToList();
            Put(name + "." + phase,
                sounding.Count > 0,
                StepSummary(name, steps),
                steady.Count == 0 ? 0 : Math.Round(steady.Average(b => b.RmsLeft), 2),
                steady.Count == 0 ? 0 : Math.Round(steady.Average(b => b.RmsRight), 2));
        }
        Put(name + ".listener", m_ListenerRms.Count > 0 && m_ListenerRms.Max() > 0.01);
        // When the sound starts after Play and, for a clip played to its end, how long before the
        // end (loopPointReached) its last sound came, to a tenth of a second.
        var heard = blocks.Where(b => b.Phase == "a" && b.RmsLeft > 0.01).ToList();
        if (heard.Count > 0 && m_PlayDsp > 0)
        {
            Put(name + ".start", Math.Round(heard[0].Dsp - m_PlayDsp, 1));
            if (m_EndDsp > 0 && name != "loop") Put(name + ".endGap", Math.Round(m_EndDsp - heard[heard.Count - 1].Dsp, 1));
        }
        m_PlayDsp = 0;
    }

    static string Json(object value) => value switch
    {
        null => "null",
        string text => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"",
        bool flag => flag ? "true" : "false",
        float f => f.ToString("R", CultureInfo.InvariantCulture),
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        IEnumerable items => "[" + string.Join(",", items.Cast<object>().Select(Json)) + "]",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture),
    };
}
