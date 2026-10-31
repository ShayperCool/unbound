using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Audio;
using Unity.Collections;

// Added only to the engine's private listener. Unity uses AudioRenderer and has no script filter.
public class EngineListenerCapture : MonoBehaviour
{
    public FilterProbe Probe;
    void OnAudioFilterRead(float[] data, int channels) => Probe.RecordEngine(data);
}

public class FilterProbe : MonoBehaviour
{
    public static readonly string[] Cases = { "dry", "lp1000q1", "lp1000q3", "lp1000q10", "hp1000q1", "hp1000q3", "hp1000q10", "lp80q1", "hp8000q1", "lp20000q1", "lp_bypass", "group_bypass", "lp_hp", "hp_lp", "parent_lp", "stereo_lp", "step_lp", "tone_hp", "set_lp", "snapshot_lp", "step_dry", "stereo_dry", "sum_lp", "volume_lp" };
    const BindingFlags Flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static bool Engine => typeof(AudioSource).Assembly.GetType("Unbound.Runtime.AudioBackend") != null;
    BinaryWriter m_Writer, m_MasterWriter;
    int m_Written;

    public static float[] Signal(int frames, int channels, int rate, string kind)
    {
        var a = new float[frames * channels];
        for (int f = 0; f < frames; f++) for (int c = 0; c < channels; c++)
        {
            float v = f == 0 || f == 2048 || f == 4096 || f == 6144 ? 0.25f : 0f;
            if (kind.StartsWith("step")) v = f < 2048 ? 0.125f : 0f;
            if (kind == "tone_hp") v = (float)(0.125 * Math.Sin(2 * Math.PI * 400 * f / rate) + 0.0625 * Math.Sin(2 * Math.PI * 6000 * f / rate));
            a[f * channels + c] = c == 0 ? v : (f == 31 ? -0.125f : v * 0.5f);
        }
        return a;
    }

    public void RecordEngine(float[] data)
    {
        if (m_Writer == null) return;
        foreach (float sample in data) { m_Writer.Write(sample); m_MasterWriter.Write(sample); }
        m_Written += data.Length;
    }
    void FlushEngine()
    {
        var assembly = typeof(AudioSource).Assembly;
        typeof(Time).GetField("s_RealDeltaTime", Flags).SetValue(null, 0.0);
        typeof(Time).GetField("s_DeltaTime", Flags).SetValue(null, 0f);
        assembly.GetType("Unbound.Runtime.AudioSystem").GetMethod("Update", Flags).Invoke(null, null);
    }
    void RenderEngine(int frames, int rate)
    {
        var assembly = typeof(AudioSource).Assembly;
        var backendType = assembly.GetType("Unbound.Runtime.AudioBackend");
        var backend = backendType.GetProperty("Current", Flags).GetValue(null);
        var packet = Activator.CreateInstance(assembly.GetType("Unbound.Runtime.AudioFramePacket"));
        void Field(string name, object value) => packet.GetType().GetField(name, Flags).SetValue(packet, value);
        Field("DeltaTime", frames / (double)rate);
        Field("ListenerForward", Vector3.forward); Field("ListenerUp", Vector3.up);
        Field("ListenerVolume", 1f); Field("Flags", 5);
        backendType.GetMethod("Frame", Flags).Invoke(backend, new[] { packet });
    }
    static void Exit(int code)
    {
        if (Engine) { Application.Quit(code); return; }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(code);
#else
        Application.Quit(code);
#endif
    }
    System.Collections.IEnumerator Start()
    {
        var routine = Capture();
        for (;;)
        {
            object next;
            try { if (!routine.MoveNext()) yield break; next = routine.Current; }
            catch (Exception e) { Debug.LogException(e); Exit(1); yield break; }
            yield return next;
        }
    }
    System.Collections.IEnumerator Capture()
    {
        var configuration = AudioSettings.GetConfiguration();
        configuration.sampleRate = int.Parse(Environment.GetEnvironmentVariable("FILTER_PROBE_RATE") ?? "48000");
        configuration.speakerMode = AudioSpeakerMode.Stereo;
        configuration.dspBufferSize = 1024;
        if (!AudioSettings.Reset(configuration)) throw new Exception("AudioSettings.Reset failed");
        Time.captureDeltaTime = 1024f / configuration.sampleRate;
        var output = Environment.GetEnvironmentVariable("FILTER_PROBE_OUT");
        Directory.CreateDirectory(output);
        int rate = AudioSettings.outputSampleRate;
        int frames = 1024;
        AudioListener.volume = 1;
        gameObject.AddComponent<AudioListener>();
        if (Engine) gameObject.AddComponent<EngineListenerCapture>().Probe = this;
        else if (!AudioRenderer.Start()) throw new Exception("AudioRenderer.Start failed");
        var sinks = new List<NativeArray<float>>();
        var records = new List<string> { "\"release\":\"" + Application.unityVersion + "\"", "\"rate\":" + rate };
        foreach (string key in Cases)
        {
            var mixer = Resources.Load<AudioMixer>(key);
            if (!mixer) throw new Exception("Missing mixer " + key);
            if (key == "snapshot_lp") mixer.FindSnapshot("Snapshot").TransitionTo(0);
            int channels = key.StartsWith("stereo") ? 2 : 1;
            var go = new GameObject(key);
            var source = go.AddComponent<AudioSource>();
            source.outputAudioMixerGroup = mixer.FindMatchingGroups("Child")[0];
            source.spatialBlend = 0; source.volume = 1;
            var clip = AudioClip.Create(key, frames * 12, channels, rate, false);
            clip.SetData(Signal(frames * 12, channels, rate, key), 0); source.clip = clip;
            var sink = default(NativeArray<float>);
            var buffer = default(NativeArray<float>);
            MethodInfo addSink = null;
            AudioMixerGroup masterGroup = null;
            if (!Engine)
            {
                sink = new NativeArray<float>(frames * 2, Allocator.Persistent); sinks.Add(sink);
                buffer = new NativeArray<float>(frames * 2, Allocator.Persistent);
                addSink = typeof(AudioRenderer).GetMethod("AddMixerGroupSink", Flags);
                masterGroup = Array.Find(mixer.FindMatchingGroups(""), g => g.name == "Master");
                if (masterGroup == null) throw new Exception("Missing master group");
            }
            if (key == "sum_lp")
            {
                var second = go.AddComponent<AudioSource>(); second.clip = clip;
                second.outputAudioMixerGroup = source.outputAudioMixerGroup;
                second.volume = 0.5f; second.Play();
            }
            source.Play();
            using (var masterWriter = new BinaryWriter(File.Create(Path.Combine(output, key + ".master.f32"))))
            using (var writer = new BinaryWriter(File.Create(Path.Combine(output, key + ".f32"))))
            {
                if (Engine) { m_Writer = writer; m_MasterWriter = masterWriter; m_Written = 0; FlushEngine(); }
                for (int block = 0; block < 12; block++)
                {
                    if (!Engine) yield return null;
                    if (block == 4 && key == "set_lp" && !mixer.SetFloat("Cutoff", 4000)) throw new Exception("SetFloat failed");
                    if (block == 8 && key == "set_lp" && !mixer.ClearFloat("Cutoff")) throw new Exception("ClearFloat failed");
                    if (block == 4 && key == "snapshot_lp") mixer.FindSnapshot("Other").TransitionTo(0);
                    if (key == "set_lp" || key == "snapshot_lp")
                    {
                        bool ok = mixer.GetFloat("Cutoff", out float cutoff);
                        records.Add("\""+key+".cutoff."+block+"\":\""+ok+":"+cutoff.ToString("R", System.Globalization.CultureInfo.InvariantCulture)+"\"");
                    }
                    if (Engine)
                    {
                        RenderEngine(frames, rate);
                        // Native's coroutine renders before PostLateUpdate.UpdateAudio submits changes.
                        FlushEngine();
                    }
                    else
                    {
                        if (!(bool)addSink.Invoke(null, new object[] { masterGroup, sink, false })) throw new Exception("AddMixerGroupSink failed");
                        if (!AudioRenderer.Render(buffer)) throw new Exception("Render failed: " + key);
                        for (int i = 0; i < buffer.Length; i++) writer.Write(buffer[i]);
                        for (int i = 0; i < sink.Length; i++) masterWriter.Write(sink[i]);
                    }
                }
                if (Engine)
                {
                    m_Writer = m_MasterWriter = null;
                    if (m_Written != frames * 12 * 2) throw new Exception("Unexpected complete buffer length " + m_Written);
                }
            }
            if (!Engine) buffer.Dispose();
            records.Add("\""+key+"\":{\"file\":\""+key+".f32\",\"frames\":"+(frames*12)+",\"channels\":2}");
            records.Add("\""+key+".master\":{\"file\":\""+key+".master.f32\",\"frames\":"+(frames*12)+",\"channels\":2}");
            source.Stop(); DestroyImmediate(go); DestroyImmediate(clip);
        }
        if (!Engine) { AudioRenderer.Stop(); foreach (var sink in sinks) sink.Dispose(); }
        File.WriteAllText(Path.Combine(output, "capture.json"), "{"+string.Join(",", records)+"}\n");
        Exit(0);
    }
}
