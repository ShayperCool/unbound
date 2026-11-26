using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Measures tracker module AudioClips (.mod, .xm, .s3m, .it) in Editor Play mode:
/// - imported clip properties and AudioImporter settings (loadType, forceToMono, sampleRateOverride,
///   compressionFormat, preloadAudioData, loadInBackground, tempo control at BPM 150),
/// - AudioSource playback (signal presence, stereo separation, musical note per half, duration,
///   natural stop, loop, seek, pause/unpause/stop, pitch=2, volume=0.5),
/// - GetData / SetData return values, readback mutation, and diagnostics,
/// - UnityWebRequestMultimedia.GetAudioClip for MOD/XM/S3M/IT/UNKNOWN, mismatched types, invalid data,
///   compressed and streaming download handlers,
/// - AudioSettings.Reset to 44100 Hz (verifying tracker clip frequency follows outputSampleRate).
/// </summary>
public sealed class AudioTrackerProbe : MonoBehaviour
{
    sealed class AudioRecorder : MonoBehaviour
    {
        readonly object m_Lock = new object();
        internal volatile bool Active;
        internal readonly List<float> Left = new List<float>();
        internal readonly List<float> Right = new List<float>();

        public void Clear()
        {
            lock (m_Lock)
            {
                Left.Clear();
                Right.Clear();
            }
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            if (!Active || channels <= 0) return;
            int frames = data.Length / channels;
            lock (m_Lock)
            {
                for (int i = 0; i < frames; i++)
                {
                    Left.Add(data[i * channels]);
                    Right.Add(channels > 1 ? data[i * channels + 1] : data[i * channels]);
                }
            }
        }

        public void CopySamples(out float[] l, out float[] r)
        {
            lock (m_Lock)
            {
                l = Left.ToArray();
                r = Right.ToArray();
            }
        }
    }

    StringBuilder m_Output;
    string m_Directory;
    readonly List<string> m_LogLines = new List<string>();
    int m_Rate;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void StartProbe()
    {
        var directory = Environment.GetEnvironmentVariable("AUDIO_TRACKER_OUT");
        if (string.IsNullOrEmpty(directory)) return;
        var probe = new GameObject("AudioTrackerProbe").AddComponent<AudioTrackerProbe>();
        probe.m_Directory = directory;
        probe.m_Output = new StringBuilder();
        new GameObject("Listener").AddComponent<AudioListener>();
    }

    void OnLog(string condition, string stackTrace, LogType type)
    {
        lock (m_LogLines) m_LogLines.Add(type + ":" + condition);
    }

    List<string> DrainLogs()
    {
        lock (m_LogLines)
        {
            var copy = new List<string>(m_LogLines);
            m_LogLines.Clear();
            return copy;
        }
    }

    static bool HasLog(List<string> logs, string substring) =>
        logs.Any(l => l.IndexOf(substring, StringComparison.Ordinal) >= 0);

    void Put(string key, params object[] values) =>
        m_Output.Append("{\"k\":").Append(Json(key)).Append(",\"v\":").Append(Json(values)).Append("}\n");

    IEnumerator Start()
    {
        Application.logMessageReceived += OnLog;
        m_Rate = AudioSettings.outputSampleRate;
        Put("env", m_Rate, (int)AudioSettings.speakerMode);

        string[] assetNames = {
            "tone.mod", "tone.xm", "tone.s3m", "tone.it",
            "xm_compressed.xm", "xm_streaming.xm", "xm_mono_rate.xm",
            "xm_bg_nopreload.xm", "xm_pcm.xm", "xm_bpm150.xm"
        };
        var clips = new Dictionary<string, AudioClip>();
        foreach (string name in assetNames)
        {
            DrainLogs();
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/" + name);
            clips[name] = clip;
            if (clip == null)
            {
                Put("meta." + name + ".props", "null");
                continue;
            }
            Put("meta." + name + ".props",
                clip.channels, clip.frequency, (int)clip.loadType,
                clip.preloadAudioData, clip.loadInBackground, clip.ambisonic, (int)clip.loadState);
            Put("meta." + name + ".len", clip.samples, Round(clip.length, 3));
        }

        // Background + preload=false lifecycle
        var bg = clips["xm_bg_nopreload.xm"];
        if (bg != null)
        {
            DrainLogs();
            int before = (int)bg.loadState;
            bool loadOk = bg.LoadAudioData();
            int afterImmediate = (int)bg.loadState;
            var bgLogs = DrainLogs();
            yield return null;
            yield return null;
            int afterFrames = (int)bg.loadState;
            bool unloadOk = bg.UnloadAudioData();
            int afterUnload = (int)bg.loadState;
            bool reloadOk = bg.LoadAudioData();
            int afterReload = (int)bg.loadState;
            var reloadLogs = DrainLogs();
            Put("lifecycle.bg_nopreload", before, loadOk, afterImmediate, afterFrames,
                unloadOk, afterUnload, reloadOk, afterReload,
                HasLog(bgLogs, "background loading of tracked music files is not supported"),
                HasLog(reloadLogs, "background loading of tracked music files is not supported"));
        }

        // Playback of all 4 formats + BPM 150 control + importer variants
        foreach (string name in new[] { "tone.mod", "tone.xm", "tone.s3m", "tone.it" })
            yield return MeasurePlayback("play." + name, clips[name], loop: false, volume: 1f, pitch: 1f, maxWait: 3.5f, bpm: 125);
        yield return MeasurePlayback("play.xm_bpm150", clips["xm_bpm150.xm"], loop: false, volume: 1f, pitch: 1f, maxWait: 3.0f, bpm: 150);
        yield return MeasurePlayback("play.xm_mono_rate", clips["xm_mono_rate.xm"], loop: false, volume: 1f, pitch: 1f, maxWait: 3.5f, bpm: 125);
        yield return MeasurePlayback("play.xm_compressed", clips["xm_compressed.xm"], loop: false, volume: 1f, pitch: 1f, maxWait: 3.5f, bpm: 125);
        yield return MeasurePlayback("play.xm_streaming", clips["xm_streaming.xm"], loop: false, volume: 1f, pitch: 1f, maxWait: 3.5f, bpm: 125);

        // Playback controls on tone.xm
        yield return MeasureLoop("ctrl.loop", clips["tone.xm"]);
        yield return MeasureSeek("ctrl.seek", clips["tone.xm"]);
        yield return MeasurePauseStop("ctrl.pausestop", clips["tone.xm"]);
        yield return MeasurePlayback("ctrl.pitch2", clips["tone.xm"], loop: false, volume: 1f, pitch: 2f, maxWait: 2.5f, bpm: 125);
        yield return MeasurePlayback("ctrl.vol05", clips["tone.xm"], loop: false, volume: 0.5f, pitch: 1f, maxWait: 3.5f, bpm: 125);

        // GetData / SetData across formats and loadTypes (after playback so SetData mutation does not affect playback)
        foreach (string name in new[] { "tone.mod", "tone.xm", "tone.s3m", "tone.it", "xm_compressed.xm", "xm_streaming.xm", "xm_pcm.xm" })
        {
            var clip = clips[name];
            if (clip == null) continue;
            DrainLogs();
            var buf = new float[2048];
            bool getOk = clip.GetData(buf, 0);
            var getLogs = DrainLogs();
            double peak = 0;
            for (int i = 0; i < buf.Length; i++) peak = Math.Max(peak, Math.Abs(buf[i]));
            var patch = new float[64];
            for (int i = 0; i < patch.Length; i++) patch[i] = 0.25f;
            bool setOk = clip.SetData(patch, 0);
            var setLogs = DrainLogs();
            bool readbackMatch = false;
            if (setOk)
            {
                var verify = new float[64];
                if (clip.GetData(verify, 0))
                    readbackMatch = verify.All(v => Math.Abs(v - 0.25f) < 0.002f);
            }
            Put("data." + name, getOk, peak > 0.001, setOk, readbackMatch,
                HasLog(getLogs, "streamed samples"),
                HasLog(setLogs, "shared sample"),
                HasLog(setLogs, "streamed samples"));
        }

        // UnityWebRequestMultimedia.GetAudioClip downloads
        string baseUrl = Environment.GetEnvironmentVariable("TRACKER_URL_BASE");
        if (!string.IsNullOrEmpty(baseUrl))
        {
            yield return MeasureDownload("dl.mod_exact", baseUrl + "tone.mod", AudioType.MOD, false, false, true);
            yield return MeasureDownload("dl.mod_unknown", baseUrl + "tone.mod", AudioType.UNKNOWN, false, false, false);
            yield return MeasureDownload("dl.mod_as_xm", baseUrl + "tone.mod", AudioType.XM, false, false, false);
            yield return MeasureDownload("dl.mod_as_wav", baseUrl + "tone.mod", AudioType.WAV, false, false, false);
            yield return MeasureDownload("dl.xm_exact", baseUrl + "tone.xm", AudioType.XM, false, false, true);
            yield return MeasureDownload("dl.xm_unknown", baseUrl + "tone.xm", AudioType.UNKNOWN, false, false, false);
            yield return MeasureDownload("dl.xm_compressed", baseUrl + "tone.xm", AudioType.XM, true, false, true);
            yield return MeasureDownload("dl.xm_streamed", baseUrl + "tone.xm", AudioType.XM, false, true, false);
            yield return MeasureDownload("dl.xm_streamed_unknown", baseUrl + "tone.xm", AudioType.UNKNOWN, false, true, false);
            yield return MeasureDownload("dl.s3m_exact", baseUrl + "tone.s3m", AudioType.S3M, false, false, false);
            yield return MeasureDownload("dl.s3m_unknown", baseUrl + "tone.s3m", AudioType.UNKNOWN, false, false, false);
            yield return MeasureDownload("dl.it_exact", baseUrl + "tone.it", AudioType.IT, false, false, false);
            yield return MeasureDownload("dl.it_unknown", baseUrl + "tone.it", AudioType.UNKNOWN, false, false, false);
            yield return MeasureDownload("dl.invalid_mod", baseUrl + "invalid.mod", AudioType.MOD, false, false, false);
            yield return MeasureDownload("dl.invalid_unknown", baseUrl + "invalid.mod", AudioType.UNKNOWN, false, false, false);

            // AudioSettings.Reset to 44100 Hz: verify tracker frequency follows outputSampleRate
            var cfg = AudioSettings.GetConfiguration();
            cfg.sampleRate = 44100;
            bool resetOk = AudioSettings.Reset(cfg);
            m_Rate = AudioSettings.outputSampleRate;
            var xmAfterReset = clips["tone.xm"];
            xmAfterReset.LoadAudioData();
            Put("reset.44100.asset", resetOk, m_Rate, xmAfterReset.channels, xmAfterReset.frequency);
            yield return MeasureDownload("reset.44100.dl", baseUrl + "tone.xm", AudioType.XM, false, false, false);
        }

        Application.logMessageReceived -= OnLog;
        m_Output.Append("{\"k\":\"complete\",\"v\":true}\n");
        Directory.CreateDirectory(m_Directory);
        File.WriteAllText(Path.Combine(m_Directory, "values.jsonl"), m_Output.ToString());
        EditorApplication.Exit(0);
    }

    IEnumerator MeasurePlayback(string key, AudioClip clip, bool loop, float volume, float pitch, float maxWait, int bpm)
    {
        if (clip == null) { Put(key, "null"); yield break; }
        var go = new GameObject(key);
        var src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.clip = clip;
        src.loop = loop;
        src.volume = volume;
        src.pitch = pitch;
        var rec = go.AddComponent<AudioRecorder>();
        rec.Active = true;
        src.Play();
        double startDsp = AudioSettings.dspTime;
        bool played = src.isPlaying;
        while (src.isPlaying && AudioSettings.dspTime - startDsp < maxWait)
            yield return null;
        bool endedNaturally = !src.isPlaying;
        double waitEnd = AudioSettings.dspTime + 0.08;
        while (AudioSettings.dspTime < waitEnd) yield return null;
        rec.Active = false;
        src.Stop();
        rec.CopySamples(out float[] l, out float[] r);
        Destroy(go);
        yield return null;

        int first = FirstAudible(l, r, 0.002f);
        int last = LastAudible(l, r, 0.002f);
        double dur = first >= 0 && last >= first ? (double)(last - first) / m_Rate : 0.0;
        double half = (8.0 * 6.0 * 2.5 / bpm) / pitch;
        SliceStats(l, r, first, half * 0.16, half * 0.78, out double l1, out double r1, out double fl1, out double fr1);
        SliceStats(l, r, first, half * 1.18, half * 1.80, out double l2, out double r2, out double fl2, out double fr2);
        Put(key, played, endedNaturally, first >= 0,
            l1 > 0.02, r1 > 0.02, ToMidi(fl1),
            l2 > 0.02, r2 > 0.02, ToMidi(fl2), ToMidi(fr2));
        Put(key + ".dur", Round(dur, 2));
        Put(key + ".rms", Round(l1, 2), Round(r2, 2));
    }

    IEnumerator MeasureLoop(string key, AudioClip clip)
    {
        if (clip == null) { Put(key, "null"); yield break; }
        var go = new GameObject(key);
        var src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.clip = clip;
        src.loop = true;
        var rec = go.AddComponent<AudioRecorder>();
        rec.Active = true;
        src.Play();
        double startDsp = AudioSettings.dspTime;
        while (AudioSettings.dspTime - startDsp < 2.6) yield return null;
        bool stillPlaying = src.isPlaying;
        rec.Active = false;
        src.Stop();
        rec.CopySamples(out float[] l, out float[] r);
        Destroy(go);
        yield return null;

        int first = FirstAudible(l, r, 0.002f);
        SliceStats(l, r, first, 2.05, 2.45, out double l3, out double r3, out double fl3, out double fr3);
        Put(key, stillPlaying, first >= 0, l3 > 0.02, r3 > 0.02, ToMidi(fl3));
    }

    IEnumerator MeasureSeek(string key, AudioClip clip)
    {
        if (clip == null) { Put(key, "null"); yield break; }
        var go = new GameObject(key);
        var src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.clip = clip;
        src.loop = false;
        src.time = 1.10f;
        int samplesBeforePlay = src.timeSamples;
        var rec = go.AddComponent<AudioRecorder>();
        rec.Active = true;
        src.Play();
        float timeRightAfterPlay = src.time;
        double startDsp = AudioSettings.dspTime;
        while (src.isPlaying && AudioSettings.dspTime - startDsp < 2.0) yield return null;
        bool endedNaturally = !src.isPlaying;
        rec.Active = false;
        src.Stop();
        rec.CopySamples(out float[] l, out float[] r);
        Destroy(go);
        yield return null;

        int first = FirstAudible(l, r, 0.002f);
        int last = LastAudible(l, r, 0.002f);
        double dur = first >= 0 && last >= first ? (double)(last - first) / m_Rate : 0.0;
        SliceStats(l, r, first, 0.10, 0.50, out double ls, out double rs, out double fls, out double frs);
        Put(key, samplesBeforePlay, timeRightAfterPlay >= 1.05f && timeRightAfterPlay <= 1.25f,
            endedNaturally, ls > 0.02, rs > 0.02, ToMidi(fls), ToMidi(frs));
        Put(key + ".dur", Round(dur, 2));
    }

    IEnumerator MeasurePauseStop(string key, AudioClip clip)
    {
        if (clip == null) { Put(key, "null"); yield break; }
        var go = new GameObject(key);
        var src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.clip = clip;
        var rec = go.AddComponent<AudioRecorder>();
        rec.Active = true;
        src.Play();
        double t0 = AudioSettings.dspTime;
        while (AudioSettings.dspTime - t0 < 0.35) yield return null;
        src.Pause();
        bool playingWhilePaused = src.isPlaying;
        float timeAtPause = src.time;
        rec.Clear();
        double t1 = AudioSettings.dspTime;
        while (AudioSettings.dspTime - t1 < 0.25) yield return null;
        float timeAfterPause = src.time;
        rec.CopySamples(out float[] pl, out float[] pr);
        bool silentWhilePaused = FirstAudible(pl, pr, 0.002f) < 0;
        rec.Clear();
        src.UnPause();
        bool playingAfterUnpause = src.isPlaying;
        double t2 = AudioSettings.dspTime;
        while (AudioSettings.dspTime - t2 < 0.30) yield return null;
        rec.CopySamples(out float[] ul, out float[] ur);
        bool audibleAfterUnpause = FirstAudible(ul, ur, 0.002f) >= 0;
        src.Stop();
        bool playingAfterStop = src.isPlaying;
        float timeAfterStop = src.time;
        int samplesAfterStop = src.timeSamples;
        rec.Active = false;
        Destroy(go);
        yield return null;

        Put(key, playingWhilePaused, Math.Abs(timeAfterPause - timeAtPause) < 0.02f, silentWhilePaused,
            playingAfterUnpause, audibleAfterUnpause, playingAfterStop, Round(timeAfterStop, 3), samplesAfterStop);
    }

    IEnumerator MeasureDownload(string key, string url, AudioType type, bool compressed, bool streamAudio, bool playIt)
    {
        DrainLogs();
        using (var req = UnityWebRequestMultimedia.GetAudioClip(url, type))
        {
            var dh = (DownloadHandlerAudioClip)req.downloadHandler;
            dh.compressed = compressed;
            dh.streamAudio = streamAudio;
            yield return req.SendWebRequest();
            int res = (int)req.result;
            AudioClip clip = null;
            string getClipErr = "";
            if (req.result == UnityWebRequest.Result.Success)
            {
                try { clip = dh.audioClip; }
                catch (Exception ex) { getClipErr = ex.GetType().Name + ":" + ex.Message; }
            }
            var logs = DrainLogs();
            if (clip == null)
            {
                Put(key, res, "null", getClipErr,
                    HasLog(logs, "Tracker files (XM/IT/MOD/S3M) cannot be streamed in realtime"),
                    HasLog(logs, "Unsupported file or audio format"));
                yield break;
            }
            var buf = new float[512];
            bool getDataOk = clip.GetData(buf, 0);
            var dataLogs = DrainLogs();
            Put(key + ".props", res, clip.channels, clip.frequency,
                (int)clip.loadType, (int)clip.loadState, getDataOk,
                HasLog(logs, "Unsupported file or audio format"),
                HasLog(dataLogs, "contains no data"));
            Put(key + ".len", clip.samples, Round(clip.length, 3));
            if (playIt && clip.channels > 0)
                yield return MeasurePlayback(key + ".play", clip, loop: false, volume: 1f, pitch: 1f, maxWait: 3.5f, bpm: 125);
            Destroy(clip);
        }
    }

    static int FirstAudible(float[] l, float[] r, float threshold)
    {
        int n = Math.Min(l.Length, r.Length);
        for (int i = 0; i < n; i++)
            if (Math.Abs(l[i]) > threshold || Math.Abs(r[i]) > threshold) return i;
        return -1;
    }

    static int LastAudible(float[] l, float[] r, float threshold)
    {
        int n = Math.Min(l.Length, r.Length);
        for (int i = n - 1; i >= 0; i--)
            if (Math.Abs(l[i]) > threshold || Math.Abs(r[i]) > threshold) return i;
        return -1;
    }

    static int ToMidi(double hz) => hz < 20.0 ? 0 : (int)Math.Round(12.0 * Math.Log(hz / 440.0, 2.0) + 69.0);

    void SliceStats(float[] l, float[] r, int origin, double t0, double t1,
        out double rmsL, out double rmsR, out double freqL, out double freqR)
    {
        rmsL = rmsR = freqL = freqR = 0;
        if (origin < 0) return;
        int i0 = origin + (int)(t0 * m_Rate);
        int i1 = Math.Min(origin + (int)(t1 * m_Rate), Math.Min(l.Length, r.Length));
        if (i1 - i0 < 64) return;
        double sl = 0, sr = 0;
        int zl = 0, zr = 0;
        for (int i = i0; i < i1; i++)
        {
            sl += l[i] * l[i];
            sr += r[i] * r[i];
            if (i > i0)
            {
                if ((l[i - 1] < 0f) != (l[i] < 0f)) zl++;
                if ((r[i - 1] < 0f) != (r[i] < 0f)) zr++;
            }
        }
        int n = i1 - i0;
        rmsL = Math.Sqrt(sl / n);
        rmsR = Math.Sqrt(sr / n);
        freqL = rmsL > 0.02 ? zl * 0.5 * m_Rate / n : 0.0;
        freqR = rmsR > 0.02 ? zr * 0.5 * m_Rate / n : 0.0;
    }

    static double Round(double v, int digits) => Math.Round(v, digits);

    static string Json(object value)
    {
        if (value == null) return "null";
        if (value is bool b) return b ? "true" : "false";
        if (value is int i) return i.ToString(CultureInfo.InvariantCulture);
        if (value is float f) return f.ToString("R", CultureInfo.InvariantCulture);
        if (value is double d) return d.ToString("R", CultureInfo.InvariantCulture);
        if (value is string s) return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") + "\"";
        if (value is object[] arr) return "[" + string.Join(",", arr.Select(Json)) + "]";
        return Json(value.ToString());
    }
}
