using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Audio;
using Unity.Collections;

public class EchoProbe : MonoBehaviour
{
    public static readonly string[] Cases = {
        "dry_mono", "dry_stereo", "default_mono", "default_stereo",
        "delay1", "delay1_01", "delay10_125", "delay25", "delay25_3", "delay5000",
        "decay0", "decay025", "decay05", "dry0", "dry025", "wet0", "wet025", "both0",
        "channels1", "channels2", "channels16", "tone_mono", "tone_stereo", "silence",
        "effect_bypass", "group_bypass", "echo_lp", "lp_echo", "parent_echo", "sum_echo", "volume_echo",
        "updated_control", "set_early", "snapshot_early", "decay1"
    };
    public static readonly string[] Parameters = { "Delay", "Decay", "Max channels", "Drymix", "Wetmix" };
    public static float[] Values(string key)
    {
        var v = new float[] { 25, 0.5f, 0, 1, 1 };
        if (key.StartsWith("default")) return null;
        if (key == "delay1") v[0] = 1;
        if (key == "delay1_01") v[0] = 1.01f;
        if (key == "delay10_125") v[0] = 10.125f;
        if (key == "delay25_3") v[0] = 25.3f;
        if (key == "delay5000") { v[0] = 5000; v[1] = 0; }
        if (key == "decay0") v[1] = 0;
        if (key == "decay025") v[1] = 0.25f;
        if (key == "decay1") v[1] = 1;
        if (key == "dry0" || key == "both0") v[3] = 0;
        if (key == "dry025") v[3] = 0.25f;
        if (key == "wet0" || key == "both0") v[4] = 0;
        if (key == "wet025") v[4] = 0.25f;
        if (key == "channels1") v[2] = 1;
        if (key == "channels2") v[2] = 2;
        if (key == "channels16") v[2] = 16;
        if (key == "updated_control") return Updated;
        return v;
    }
    public static float[] Updated => new float[] { 40.125f, 0.25f, 0, 0.25f, 0.5f };
    public static int Blocks(string key) => key == "delay5000" ? 768 : key.StartsWith("default") ? 96 : 32;
    const BindingFlags Flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static bool Engine => Environment.GetEnvironmentVariable("ECHO_PROBE_ENGINE") == "1";
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!Engine) return;
        foreach (var existing in UnityEngine.Object.FindObjectsOfType<EchoProbe>()) DestroyImmediate(existing.gameObject);
        var probe = new GameObject("EchoProbe").AddComponent<EchoProbe>();
        try { var capture = probe.Capture(); while (capture.MoveNext()) throw new Exception("Engine capture yielded"); }
        catch (Exception error) { Debug.LogException(error); Exit(1); }
    }
    BinaryWriter m_Listener;
    bool m_Captured;
    public void RecordListener(float[] data) { if (m_Listener != null) foreach (float x in data) m_Listener.Write(x); }
    static void Exit(int code)
    {
        if (Engine) { Application.Quit(code); return; }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(code);
#else
        Application.Quit(code);
#endif
    }
    void FlushEngine()
    {
        var assembly = Type.GetType("Unbound.Runtime.AudioBackend, UnityEngine.CoreModule", true).Assembly;
        typeof(Time).GetField("s_RealDeltaTime", Flags).SetValue(null, 0.0);
        typeof(Time).GetField("s_DeltaTime", Flags).SetValue(null, 0f);
        assembly.GetType("Unbound.Runtime.AudioSystem").GetMethod("Update", Flags).Invoke(null, null);
    }
    System.Collections.IEnumerator Start()
    {
        var routine = Capture();
        for (;;) {
            object next;
            try { if (!routine.MoveNext()) yield break; next = routine.Current; }
            catch (Exception e) { Debug.LogException(e); Exit(1); yield break; }
            yield return next;
        }
    }
    static float[] Signal(string key, int channels, int rate)
    {
        var data = new float[8192 * channels];
        for (int f = 0; f < 8192; f++) for (int c = 0; c < channels; c++) {
            float value = c == 0 ? (f == 0 || f == 2048 ? 0.25f : 0) : (f == 31 || f == 3072 ? -0.125f : 0);
            if (key.StartsWith("tone")) value = f < 4096 ? (float)(0.125 * Math.Sin(2 * Math.PI * (c == 0 ? 400 : 1700) * f / rate)) : 0;
            if (key == "silence") value = 0;
            data[f * channels + c] = value;
        }
        return data;
    }
    System.Collections.IEnumerator Capture()
    {
        if (m_Captured) yield break;
        m_Captured = true;
        var config = AudioSettings.GetConfiguration();
        config.sampleRate = int.Parse(Environment.GetEnvironmentVariable("ECHO_PROBE_RATE"));
        config.speakerMode = AudioSpeakerMode.Stereo; config.dspBufferSize = 1024;
        if (!AudioSettings.Reset(config)) throw new Exception("AudioSettings.Reset failed");
        Time.captureDeltaTime = 1024f / config.sampleRate;
        int rate = AudioSettings.outputSampleRate;
        string output = Environment.GetEnvironmentVariable("ECHO_PROBE_OUT"); Directory.CreateDirectory(output);
        AudioListener.volume = 1; gameObject.AddComponent<AudioListener>();
        gameObject.AddComponent<EchoListenerCapture>().Probe = this;
        if (!AudioRenderer.Start()) throw new Exception("AudioRenderer.Start failed");
        var sinks = new List<NativeArray<float>>();
        var records = new List<string> { "\"release\":\"" + Application.unityVersion + "\"", "\"rate\":" + rate };
        void Save() => File.WriteAllText(Path.Combine(output, "capture.json"), "{" + string.Join(",", records) + "}\n");
        Save();
        foreach (string key in Cases) {
            Debug.Log("Echo case " + key);
            var mixer = Resources.Load<AudioMixer>(key); if (!mixer) throw new Exception("Missing mixer " + key);
            if (key == "snapshot_early") mixer.FindSnapshot("Other").TransitionTo(0);
            if (key == "set_early") for (int p = 0; p < Parameters.Length; p++)
                if (!mixer.SetFloat(Parameters[p], Updated[p])) throw new Exception("SetFloat failed");
            if (key == "set_early" || key == "snapshot_early" || key == "updated_control")
                for (int p = 0; p < Parameters.Length; p++) {
                    bool ok = mixer.GetFloat(Parameters[p], out float value);
                    records.Add("\""+key+"."+Parameters[p]+"\":\""+ok+":"+BitConverter.ToInt32(BitConverter.GetBytes(value),0)+"\"");
                }
            int channels = key == "dry_mono" || key == "default_mono" || key == "tone_mono" ? 1 : 2;
            var go = new GameObject(key); var source = go.AddComponent<AudioSource>();
            source.outputAudioMixerGroup = mixer.FindMatchingGroups("Child")[0];
            source.spatialBlend = 0; source.volume = 1;
            var clip = AudioClip.Create(key, 8192, channels, rate, false);
            if (!clip.SetData(Signal(key, channels, rate), 0)) throw new Exception("SetData failed"); source.clip = clip;
            if (key == "sum_echo") {
                var second = go.AddComponent<AudioSource>(); second.clip = clip; second.volume = 0.5f;
                second.outputAudioMixerGroup = source.outputAudioMixerGroup; second.Play();
            }
            var sink = new NativeArray<float>(2048, Allocator.Persistent); sinks.Add(sink);
            var buffer = new NativeArray<float>(2048, Allocator.Persistent);
            var master = Array.Find(mixer.FindMatchingGroups(""), g => g.name == "Master");
            var addSink = typeof(AudioRenderer).GetMethod("AddMixerGroupSink", Flags);
            source.Play();
            using (var listener = new BinaryWriter(File.Create(Path.Combine(output, key+".listener.f32"))))
            using (var masterWriter = new BinaryWriter(File.Create(Path.Combine(output, key+".master.f32"))))
            using (var writer = new BinaryWriter(File.Create(Path.Combine(output, key+".f32")))) {
                m_Listener = listener;
                if (Engine) FlushEngine();
                for (int block = 0; block < Blocks(key); block++) {
                    if (!Engine) yield return null;
                    if (!(bool)addSink.Invoke(null, new object[] { master, sink, false })) throw new Exception("AddMixerGroupSink failed");
                    if (!AudioRenderer.Render(buffer)) throw new Exception("AudioRenderer.Render failed");
                    for (int i = 0; i < buffer.Length; i++) { writer.Write(buffer[i]); masterWriter.Write(sink[i]); }
                    if (Engine) FlushEngine();
                }
                m_Listener = null;
            }
            foreach (string suffix in new[] { "", ".master", ".listener" })
                records.Add("\""+key+suffix+"\":{\"file\":\""+key+suffix+".f32\",\"frames\":"+(Blocks(key)*1024)+",\"channels\":2}");
            Save(); buffer.Dispose(); source.Stop(); DestroyImmediate(go); DestroyImmediate(clip);
        }
        AudioRenderer.Stop(); foreach (var sink in sinks) sink.Dispose(); Save(); Exit(0);
    }
}
public class EchoListenerCapture : MonoBehaviour {
    public EchoProbe Probe;
    void OnAudioFilterRead(float[] data, int channels) => Probe.RecordListener(data);
}
