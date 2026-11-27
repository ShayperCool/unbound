using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Measures UnityEngine.Microphone in Editor Play mode:
/// - device enumeration and GetDeviceCaps (default null/empty, named, invalid),
/// - Start argument validation (unknown device, lengthSec <= 0, lengthSec >= 3600, frequency <= 0),
/// - returned AudioClip metadata (name, channels, frequency, samples, length, loadType, loadState),
/// - IsRecording / GetPosition / End for null, empty, and explicit device name,
/// - position advancement, non-looping completion, looping wraparound,
/// - GetData / SetData / AudioSource playback of recorded clip,
/// - double Start on same device, Destroy(clip) while recording, and AudioSettings.Reset while recording.
/// </summary>
public sealed class AudioMicProbe : MonoBehaviour
{
    StringBuilder m_Output;
    string m_Directory;
    readonly List<string> m_LogLines = new List<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void StartProbe()
    {
        var directory = Environment.GetEnvironmentVariable("AUDIO_MIC_OUT");
        if (string.IsNullOrEmpty(directory)) return;
        var probe = new GameObject("AudioMicProbe").AddComponent<AudioMicProbe>();
        probe.m_Directory = directory;
        probe.m_Output = new StringBuilder();
        new GameObject("Listener").AddComponent<AudioListener>();
    }

    void OnEnable()
    {
        Application.logMessageReceived += CaptureLog;
    }

    void OnDisable()
    {
        Application.logMessageReceived -= CaptureLog;
    }

    void CaptureLog(string condition, string stackTrace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Warning || type == LogType.Exception || type == LogType.Assert)
        {
            var firstLine = (condition ?? "").Split('\n')[0].Trim();
            lock (m_LogLines)
                m_LogLines.Add(type + ":" + firstLine);
        }
    }

    string DrainLogs()
    {
        lock (m_LogLines)
        {
            var s = string.Join(" | ", m_LogLines);
            m_LogLines.Clear();
            return s;
        }
    }

    IEnumerator Start()
    {
        try
        {
            yield return RunAll();
        }
        finally
        {
            try { Microphone.End(null); } catch { }
            Record("complete", true);
            Directory.CreateDirectory(m_Directory);
            File.WriteAllText(Path.Combine(m_Directory, "values.jsonl"), m_Output.ToString());
#if UNITY_EDITOR
            EditorApplication.Exit(0);
#else
            Application.Quit();
#endif
        }
    }

    IEnumerator RunAll()
    {
        yield return null;
        yield return null;
        DrainLogs();

        // 1. Device discovery.
        var devices = Microphone.devices;
        Record("devices.not_null", devices != null);
        Record("devices.count_gt_0", devices != null && devices.Length > 0);
        Record("devices.all_non_empty", devices != null && devices.All(d => !string.IsNullOrEmpty(d)));
        Record("devices.unique", devices != null && devices.Distinct().Count() == devices.Length);
        string dev0 = (devices != null && devices.Length > 0) ? devices[0] : "";
        Console.WriteLine("MIC_DEVICES=" + (devices == null ? "null" : string.Join(", ", devices)));

        // 2. Idle state before any Start.
        Record("idle.is_recording.null", Microphone.IsRecording(null));
        Record("idle.is_recording.empty", Microphone.IsRecording(""));
        Record("idle.is_recording.dev0", Microphone.IsRecording(dev0));
        Record("idle.is_recording.invalid", Microphone.IsRecording("no such device"));
        Record("idle.pos.null", Microphone.GetPosition(null));
        Record("idle.pos.empty", Microphone.GetPosition(""));
        Record("idle.pos.dev0", Microphone.GetPosition(dev0));
        Record("idle.pos.invalid", Microphone.GetPosition("no such device"));
        DrainLogs();
        Microphone.End("no such device");
        Record("idle.end_invalid.logs", DrainLogs());
        Microphone.End(null);
        Record("idle.end_null.logs", DrainLogs());

        // 3. GetDeviceCaps.
        RecordCaps("caps.null", null);
        RecordCaps("caps.empty", "");
        RecordCaps("caps.dev0", dev0);
        RecordCaps("caps.invalid", "no such device");

        // 4. Start validation / error paths.
        RecordStartCall("err.invalid_device", "no such device", false, 1, 44100);
        RecordStartCall("err.len_zero", null, false, 0, 44100);
        RecordStartCall("err.len_neg", null, false, -5, 44100);
        RecordStartCall("err.len_3600", null, false, 3600, 44100);
        RecordStartCall("err.len_3601", null, false, 3601, 44100);
        RecordStartCall("err.freq_zero", null, false, 1, 0);
        RecordStartCall("err.freq_neg", null, false, 1, -44100);

        if (string.IsNullOrEmpty(dev0))
            yield break;

        // 5. Start with null device, non-looping 2s @ 44100.
        DrainLogs();
        var clip1 = Microphone.Start(null, false, 2, 44100);
        Record("rec1.logs", DrainLogs());
        RecordClip("rec1.clip", clip1);
        Record("rec1.is_recording.null", Microphone.IsRecording(null));
        Record("rec1.is_recording.empty", Microphone.IsRecording(""));
        Record("rec1.is_recording.dev0", Microphone.IsRecording(dev0));
        Record("rec1.is_recording.invalid", Microphone.IsRecording("no such device"));
        int posStart = Microphone.GetPosition(null);
        Record("rec1.pos_start_in_range", clip1 != null && posStart >= 0 && posStart <= clip1.samples);

        yield return WaitRealtime(0.35f);
        int posMidNull = Microphone.GetPosition(null);
        int posMidEmpty = Microphone.GetPosition("");
        int posMidDev0 = Microphone.GetPosition(dev0);
        Console.WriteLine($"MIC_POS start={posStart} mid={posMidNull}/{posMidEmpty}/{posMidDev0} clipSamples={clip1?.samples}");
        Record("rec1.pos_mid.advances", posMidNull > posStart);
        Record("rec1.pos_mid.in_range", clip1 != null && posMidNull >= 0 && posMidNull <= clip1.samples);
        Record("rec1.pos_mid.empty_matches", Math.Abs(posMidEmpty - posMidNull) < 4096);
        Record("rec1.pos_mid.dev0_matches", Math.Abs(posMidDev0 - posMidNull) < 4096);

        // GetData while recording.
        if (clip1 != null)
        {
            var buf = new float[256 * clip1.channels];
            DrainLogs();
            bool gdWhile = clip1.GetData(buf, 0);
            Record("rec1.getdata_while.ok", gdWhile);
            Record("rec1.getdata_while.logs", DrainLogs());
        }

        // Wait past 2s for non-looping recording to finish naturally.
        yield return WaitRealtime(2.0f);
        Record("rec1.after_end_natural.is_recording", Microphone.IsRecording(null));
        Record("rec1.after_end_natural.pos", Microphone.GetPosition(null));

        Microphone.End(null);
        Record("rec1.after_end.is_recording", Microphone.IsRecording(null));
        Record("rec1.after_end.pos", Microphone.GetPosition(null));

        // GetData / SetData / AudioSource playback after End.
        if (clip1 != null)
        {
            var buf = new float[256 * clip1.channels];
            DrainLogs();
            bool gdAfter = clip1.GetData(buf, 0);
            Record("rec1.getdata_after.ok", gdAfter);
            Record("rec1.getdata_after.logs", DrainLogs());

            var writeBuf = new float[16 * clip1.channels];
            for (int i = 0; i < writeBuf.Length; i++) writeBuf[i] = 0.25f;
            DrainLogs();
            bool sdAfter = clip1.SetData(writeBuf, 0);
            Record("rec1.setdata_after.ok", sdAfter);
            Record("rec1.setdata_after.logs", DrainLogs());
            var rb = new float[16 * clip1.channels];
            clip1.GetData(rb, 0);
            Record("rec1.setdata_readback", rb[0]);

            var srcGo = new GameObject("MicPlayback");
            var src = srcGo.AddComponent<AudioSource>();
            src.clip = clip1;
            src.Play();
            yield return WaitRealtime(0.15f);
            Record("rec1.playback.playing_or_advanced", src.isPlaying || src.timeSamples > 0);
            src.Stop();
            Destroy(srcGo);
            Destroy(clip1);
        }

        // 6. Start with named device, looping 1s @ 48000.
        DrainLogs();
        var clip2 = Microphone.Start(dev0, true, 1, 48000);
        Record("rec2.logs", DrainLogs());
        RecordClip("rec2.clip", clip2);
        Record("rec2.is_recording", Microphone.IsRecording(dev0));

        yield return WaitRealtime(0.4f);
        int pos2a = Microphone.GetPosition(dev0);
        yield return WaitRealtime(0.9f);
        int pos2b = Microphone.GetPosition(dev0);
        Record("rec2.after_1s.is_recording", Microphone.IsRecording(dev0));
        Record("rec2.after_1s.pos_in_range", clip2 != null && pos2b >= 0 && pos2b < clip2.samples);
        Record("rec2.after_1s.advanced_or_wrapped", pos2a > 0 && pos2b != pos2a);

        // 7. Double Start while already recording on same device.
        DrainLogs();
        var clip3 = Microphone.Start("", true, 2, 44100);
        Record("rec3.double_start.logs", DrainLogs());
        Record("rec3.double_start.not_null", clip3 != null);
        if (clip3 != null)
            RecordClip("rec3.clip", clip3);
        Record("rec3.is_recording", Microphone.IsRecording(""));

        // 8. End by explicit device name stops recording started with "".
        Microphone.End(dev0);
        Record("rec3.after_end_named.is_recording", Microphone.IsRecording(null));
        Record("rec3.after_end_named.pos", Microphone.GetPosition(null));
        if (clip2 != null) Destroy(clip2);
        if (clip3 != null) Destroy(clip3);

        // 9. Destroy clip while recording.
        var clip4 = Microphone.Start(null, true, 2, 44100);
        yield return WaitRealtime(0.15f);
        DestroyImmediate(clip4);
        yield return null;
        Record("destroy_while_rec.is_recording", Microphone.IsRecording(null));
        Microphone.End(null);
        Record("destroy_while_rec.after_end", Microphone.IsRecording(null));

        // 10. AudioSettings.Reset while recording.
        var clip5 = Microphone.Start(null, true, 2, 44100);
        yield return WaitRealtime(0.15f);
        Record("reset.before.is_recording", Microphone.IsRecording(null));
        var cfg = AudioSettings.GetConfiguration();
        AudioSettings.Reset(cfg);
        yield return null;
        Record("reset.after.is_recording", Microphone.IsRecording(null));
        Record("reset.after.pos", Microphone.GetPosition(null));
        Microphone.End(null);
        if (clip5 != null) Destroy(clip5);
    }

    void RecordCaps(string prefix, string device)
    {
        DrainLogs();
        int minF = -1, maxF = -1;
        string ex = "";
        try
        {
            Microphone.GetDeviceCaps(device, out minF, out maxF);
        }
        catch (Exception e)
        {
            ex = e.GetType().FullName + ": " + e.Message;
        }
        Record(prefix + ".ex", ex);
        Record(prefix + ".min_nonneg", minF >= 0);
        Record(prefix + ".max_ge_min", maxF >= minF);
        Record(prefix + ".logs", DrainLogs());
        Console.WriteLine($"MIC_CAPS[{prefix}]={minF}..{maxF} ex={ex}");
    }

    void RecordStartCall(string prefix, string device, bool loop, int lengthSec, int freq)
    {
        DrainLogs();
        AudioClip clip = null;
        string ex = "";
        try
        {
            clip = Microphone.Start(device, loop, lengthSec, freq);
        }
        catch (Exception e)
        {
            ex = e.GetType().FullName + ": " + e.Message;
        }
        Record(prefix + ".is_null", clip == null);
        Record(prefix + ".ex", ex);
        Record(prefix + ".logs", DrainLogs());
        if (clip != null)
        {
            Microphone.End(device);
            Destroy(clip);
        }
    }

    void RecordClip(string prefix, AudioClip clip)
    {
        Record(prefix + ".not_null", clip != null);
        if (clip == null) return;
        Record(prefix + ".name", clip.name ?? "");
        Record(prefix + ".channels", clip.channels);
        Record(prefix + ".freq", clip.frequency);
        Record(prefix + ".samples", clip.samples);
        Record(prefix + ".len", clip.length);
        Record(prefix + ".load_type", clip.loadType.ToString());
        Record(prefix + ".load_state", clip.loadState.ToString());
        Record(prefix + ".load_in_bg", clip.loadInBackground);
        Record(prefix + ".ambisonic", clip.ambisonic);
    }

    IEnumerator WaitRealtime(float seconds)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < end)
            yield return null;
    }

    void Record(string key, object value)
    {
        m_Output.Append("{\"k\":\"").Append(Escape(key)).Append("\",\"v\":");
        if (value == null) m_Output.Append("null");
        else if (value is bool b) m_Output.Append(b ? "true" : "false");
        else if (value is int i) m_Output.Append(i.ToString(CultureInfo.InvariantCulture));
        else if (value is float f) m_Output.Append(float.IsFinite(f) ? f.ToString("R", CultureInfo.InvariantCulture) : "null");
        else if (value is double d) m_Output.Append(double.IsFinite(d) ? d.ToString("R", CultureInfo.InvariantCulture) : "null");
        else m_Output.Append('"').Append(Escape(value.ToString())).Append('"');
        m_Output.Append("}\n");
    }

    static string Escape(string s) => s
        .Replace("\\", "\\\\")
        .Replace("\"", "\\\"")
        .Replace("\r", "\\r")
        .Replace("\n", "\\n");
}
