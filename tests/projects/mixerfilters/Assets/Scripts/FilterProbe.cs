using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Audio;
using Unity.Collections;

public class FilterProbe : MonoBehaviour
{
    public static readonly string[] Cases = { "dry", "lp1000q1", "lp1000q3", "lp1000q10", "hp1000q1", "hp1000q3", "hp1000q10", "lp80q1", "hp8000q1", "lp20000q1", "lp_bypass", "group_bypass", "lp_hp", "hp_lp", "parent_lp", "stereo_lp", "step_lp", "tone_hp", "set_lp", "snapshot_lp", "step_dry", "stereo_dry", "sum_lp", "volume_lp" };
    public static IEnumerable<string> ActiveCases
    {
        get
        {
            if (Environment.GetEnvironmentVariable("FILTER_PROBE_PCM") == "1") return new[] { "pcm_output" };
            if (Environment.GetEnvironmentVariable("FILTER_PROBE_STARTUP") != "1") return Cases;
            var keys = new List<string> { "step_lp" };
            for (int frame = 1; frame <= 128; frame++)
                keys.Add("impulse_lp_" + frame);
            foreach (int frame in new[] { 160, 200, 256, 512, 1024, 2048 })
                keys.Add("impulse_lp_" + frame);
            return keys;
        }
    }
    const BindingFlags Flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static bool Engine => Environment.GetEnvironmentVariable("FILTER_PROBE_ENGINE") == "1";
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        Debug.Log("Filter probe bootstrap, engine=" + Engine);
        if (!Engine) return;
        foreach (var existing in UnityEngine.Object.FindObjectsOfType<FilterProbe>())
            DestroyImmediate(existing.gameObject);
        var probe = new GameObject("FilterProbe").AddComponent<FilterProbe>();
        try
        {
            var capture = probe.Capture();
            while (capture.MoveNext()) throw new Exception("The engine offline capture unexpectedly yielded");
        }
        catch (Exception error) { Debug.LogException(error); Exit(1); }
    }
    BinaryWriter m_Writer, m_MasterWriter, m_ListenerWriter;
    int m_Written;
    bool m_Captured;
    bool m_GroupObserved;

    public static float[] Signal(int frames, int channels, int rate, string kind)
    {
        var a = new float[frames * channels];
        for (int f = 0; f < frames; f++) for (int c = 0; c < channels; c++)
        {
            float v = f == 0 || f == 2048 || f == 4096 || f == 6144 ? 0.25f : 0f;
            if (kind.StartsWith("step")) v = f < 2048 ? 0.125f : 0f;
            if (kind == "tone_hp") v = (float)(0.125 * Math.Sin(2 * Math.PI * 400 * f / rate) + 0.0625 * Math.Sin(2 * Math.PI * 6000 * f / rate));
            if (kind.StartsWith("impulse_lp_")) v = f == int.Parse(kind.Substring("impulse_lp_".Length)) ? 0.25f : 0f;
            if (kind == "pcm_output")
            {
                float[] pcm = { 0, 0.00001f, -0.00001f, 1f/32768, -1f/32768, 2.999f/32768, -2.999f/32768,
                    0.5f, -0.5f, 0.99999f, -0.99999f, 1, -1, 1.001f, -1.001f, 1.5f, -1.5f };
                v = f < 2048 ? 0 : pcm[(f-2048) % pcm.Length];
            }
            a[f * channels + c] = c == 0 ? v : (f == 31 ? -0.125f : v * 0.5f);
        }
        return a;
    }

    public void RecordEngine(float[] data)
    {
        if (m_Writer == null) return;
        foreach (float sample in data) { m_Writer.Write(sample); if (!m_GroupObserved) m_MasterWriter.Write(sample); }
        m_Written += data.Length;
    }
    public void RecordListener(float[] data)
    {
        if (m_ListenerWriter == null) return;
        foreach (float sample in data) m_ListenerWriter.Write(sample);
    }
    void FlushEngine()
    {
        var assembly = Type.GetType("Unbound.Runtime.AudioBackend, UnityEngine.CoreModule", true).Assembly;
        typeof(Time).GetField("s_RealDeltaTime", Flags).SetValue(null, 0.0);
        typeof(Time).GetField("s_DeltaTime", Flags).SetValue(null, 0f);
        assembly.GetType("Unbound.Runtime.AudioSystem").GetMethod("Update", Flags).Invoke(null, null);
    }
    void RenderEngine(int frames, int rate)
    {
        var assembly = Type.GetType("Unbound.Runtime.AudioBackend, UnityEngine.CoreModule", true).Assembly;
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
        if (m_Captured) yield break;
        m_Captured = true;
        Debug.Log("Filter capture starting, engine=" + Engine);
        var configuration = AudioSettings.GetConfiguration();
        configuration.sampleRate = int.Parse(Environment.GetEnvironmentVariable("FILTER_PROBE_RATE") ?? "48000");
        configuration.speakerMode = AudioSpeakerMode.Stereo;
        configuration.dspBufferSize = 1024;
        if (!AudioSettings.Reset(configuration)) throw new Exception("AudioSettings.Reset failed");
        Time.captureDeltaTime = 1024f / configuration.sampleRate;
        var output = Environment.GetEnvironmentVariable("FILTER_PROBE_OUT");
        if (Environment.GetEnvironmentVariable("FILTER_PROBE_API") == "1")
        {
            RendererApiProbe.Record(output); Exit(0); yield break;
        }
        Directory.CreateDirectory(output);
        int rate = AudioSettings.outputSampleRate;
        int frames = 1024;
        AudioListener.volume = 1;
        gameObject.AddComponent<AudioListener>();
        if (Engine) gameObject.AddComponent<EngineListenerCapture>().Probe = this;
        else
        {
            if (Environment.GetEnvironmentVariable("FILTER_PROBE_LISTENER") == "1")
                gameObject.AddComponent<EngineListenerCapture>().Probe = this;
            if (!AudioRenderer.Start()) throw new Exception("AudioRenderer.Start failed");
        }
        var sinks = new List<NativeArray<float>>();
        var records = new List<string> { "\"release\":\"" + Application.unityVersion + "\"", "\"rate\":" + rate };
        bool diagnosticInput = Environment.GetEnvironmentVariable("FILTER_PROBE_DIAGNOSTIC_INPUT") == "1";
        if (Engine && diagnosticInput) throw new NotSupportedException("Input diagnostics use Native AudioRenderer");
        foreach (string key in ActiveCases)
        {
            if (diagnosticInput && key != "step_lp" && key != "tone_hp") continue;
            var mixer = Resources.Load<AudioMixer>(key);
            if (!mixer) throw new Exception("Missing mixer " + key);
            if (key == "snapshot_lp") mixer.FindSnapshot("Snapshot").TransitionTo(0);
            int channels = key.StartsWith("stereo") || key == "pcm_output" ? 2 : 1;
            var go = new GameObject(key);
            var source = go.AddComponent<AudioSource>();
            source.outputAudioMixerGroup = mixer.FindMatchingGroups(diagnosticInput ? "Input" : "Child")[0];
            source.spatialBlend = 0; source.volume = 1;
            var clip = AudioClip.Create(key, frames * 12, channels, rate, false);
            clip.SetData(Signal(frames * 12, channels, rate, key), 0); source.clip = clip;
            var sink = default(NativeArray<float>);
            var buffer = default(NativeArray<float>);
            var inputBuffer = default(NativeArray<float>);
            MethodInfo addSink = null;
            var masterGroup = Array.Find(mixer.FindMatchingGroups(""), g => g.name == "Master");
            MethodInfo observeGroup = Engine ? Type.GetType("Unbound.Runtime.AudioFilters, UnityEngine.CoreModule", true).GetMethod("ObserveGroup", Flags) : null;
            if (!Engine)
            {
                sink = new NativeArray<float>(frames * 2, Allocator.Persistent); sinks.Add(sink);
                buffer = new NativeArray<float>(frames * 2, Allocator.Persistent);
                if (diagnosticInput) { inputBuffer = new NativeArray<float>(frames * 2, Allocator.Persistent); sinks.Add(inputBuffer); }
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
            using (var inputWriter = diagnosticInput ? new BinaryWriter(File.Create(Path.Combine(output, key + ".input.f32"))) : null)
            using (var listenerWriter = Environment.GetEnvironmentVariable("FILTER_PROBE_LISTENER") == "1" ? new BinaryWriter(File.Create(Path.Combine(output, key + ".listener.f32"))) : null)
            using (var masterWriter = new BinaryWriter(File.Create(Path.Combine(output, key + ".master.f32"))))
            using (var writer = new BinaryWriter(File.Create(Path.Combine(output, key + ".f32"))))
            {
                m_ListenerWriter = listenerWriter;
                if (Engine)
                {
                    m_Writer = writer; m_MasterWriter = masterWriter; m_Written = 0;
                    m_GroupObserved = observeGroup != null;
                    if (m_GroupObserved) observeGroup.Invoke(null, new object[] { masterGroup, (Action<float[], int>)((data, channelCount) => { foreach (float sample in data) masterWriter.Write(sample); }) });
                    FlushEngine();
                }
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
                        if (diagnosticInput && !(bool)addSink.Invoke(null, new object[] { source.outputAudioMixerGroup, inputBuffer, false })) throw new Exception("Input sink failed");
                        if (!AudioRenderer.Render(buffer)) throw new Exception("Render failed: " + key);
                        for (int i = 0; i < buffer.Length; i++) writer.Write(buffer[i]);
                        for (int i = 0; i < sink.Length; i++) masterWriter.Write(sink[i]);
                        if (diagnosticInput) for (int i = 0; i < inputBuffer.Length; i++) inputWriter.Write(inputBuffer[i]);
                    }
                }
                if (Engine)
                {
                    if (m_GroupObserved) observeGroup.Invoke(null, new object[] { masterGroup, null });
                    m_Writer = m_MasterWriter = null;
                    if (m_Written != frames * 12 * 2) throw new Exception("Unexpected complete buffer length " + m_Written);
                }
                m_ListenerWriter = null;
            }
            if (!Engine) buffer.Dispose();
            records.Add("\""+key+"\":{\"file\":\""+key+".f32\",\"frames\":"+(frames*12)+",\"channels\":2}");
            records.Add("\""+key+".master\":{\"file\":\""+key+".master.f32\",\"frames\":"+(frames*12)+",\"channels\":2}");
            if (diagnosticInput) records.Add("\""+key+".input\":{\"file\":\""+key+".input.f32\",\"frames\":"+(frames*12)+",\"channels\":2}");
            if (Environment.GetEnvironmentVariable("FILTER_PROBE_LISTENER") == "1") records.Add("\""+key+".listener\":{\"file\":\""+key+".listener.f32\",\"frames\":"+(frames*12)+",\"channels\":2}");
            source.Stop(); DestroyImmediate(go); DestroyImmediate(clip);
        }
        if (!Engine) { AudioRenderer.Stop(); foreach (var sink in sinks) sink.Dispose(); }
        File.WriteAllText(Path.Combine(output, "capture.json"), "{"+string.Join(",", records)+"}\n");
        Exit(0);
    }
}

// Optional Native listener observation checks the same callback frontier as the engine.
public class EngineListenerCapture : MonoBehaviour
{
    public FilterProbe Probe;
    void OnAudioFilterRead(float[] data, int channels)
    {
        Probe.RecordListener(data);
        Probe.RecordEngine(data);
    }
}
