using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
#if UNITY_EDITOR
using Unity.Collections;
#endif

public class FilterProbe : MonoBehaviour
{
    public static readonly string[] Cases = { "dry", "lp1000q1", "lp1000q3", "lp1000q10", "hp1000q1", "hp1000q3", "hp1000q10", "lp80q1", "hp8000q1", "lp20000q1", "lp_bypass", "group_bypass", "lp_hp", "hp_lp", "parent_lp", "stereo_lp", "step_lp", "tone_hp", "set_lp", "snapshot_lp" };
    public static float[] Signal(int frames, int channels, int rate, string kind)
    {
        var a = new float[frames * channels];
        for (int f = 0; f < frames; f++) for (int c = 0; c < channels; c++)
        {
            float v = f == 0 || f == 2048 || f == 4096 || f == 6144 ? 0.25f : 0f;
            if (kind == "step_lp") v = f < 2048 ? 0.125f : 0f;
            if (kind == "tone_hp") v = (float)(0.125 * Math.Sin(2 * Math.PI * 400 * f / rate) + 0.0625 * Math.Sin(2 * Math.PI * 6000 * f / rate));
            a[f * channels + c] = c == 0 ? v : (f == 31 ? -0.125f : v * 0.5f);
        }
        return a;
    }
#if !UNITY_EDITOR
    BinaryWriter m_Writer;
    int m_Written;
    const System.Reflection.BindingFlags Flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
    void OnAudioFilterRead(float[] data, int channels)
    {
        if (m_Writer == null) return;
        foreach (float sample in data) m_Writer.Write(sample);
        m_Written += data.Length;
    }
    void RenderEngine(int frames, int rate)
    {
        var assembly = typeof(AudioSource).Assembly;
        typeof(Time).GetField("s_RealDeltaTime", Flags).SetValue(null, 0.0);
        typeof(Time).GetField("s_DeltaTime", Flags).SetValue(null, 0f);
        assembly.GetType("Unbound.Runtime.AudioSystem").GetMethod("Update", Flags).Invoke(null, null);
        var backendType = assembly.GetType("Unbound.Runtime.AudioBackend");
        var backend = backendType.GetProperty("Current", Flags).GetValue(null);
        var packet = Activator.CreateInstance(assembly.GetType("Unbound.Runtime.AudioFramePacket"));
        void Field(string name, object value) => packet.GetType().GetField(name, Flags).SetValue(packet, value);
        Field("DeltaTime", frames / (double)rate);
        Field("ListenerForward", Vector3.forward); Field("ListenerUp", Vector3.up);
        Field("ListenerVolume", 1f); Field("Flags", 5);
        backendType.GetMethod("Frame", Flags).Invoke(backend, new[] { packet });
    }
#endif
    static void Exit(int code)
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(code);
#else
        Application.Quit(code);
#endif
    }
    System.Collections.IEnumerator Start()
    {
        bool finished = false;
        var routine = Capture();
        while (!finished)
        {
            object next = null;
            try { finished = !routine.MoveNext(); if (!finished) next = routine.Current; }
            catch (Exception e) { Debug.LogException(e); Exit(1); yield break; }
            if (!finished) yield return next;
        }
    }
    System.Collections.IEnumerator Capture()
    {
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
            #if UNITY_EDITOR
            if (!AudioRenderer.Start()) throw new Exception("AudioRenderer.Start failed");
#endif
            var records = new List<string>();
            records.Add("\"release\":\"" + Application.unityVersion + "\"");
            records.Add("\"rate\":" + rate);
            foreach (string key in Cases)
            {
                var mixer = Resources.Load<AudioMixer>(key);
                if (!mixer) throw new Exception("Missing mixer " + key);
                int channels = key == "stereo_lp" ? 2 : 1;
                var go = new GameObject(key);
                var source = go.AddComponent<AudioSource>();
                source.outputAudioMixerGroup = mixer.FindMatchingGroups("Child")[0];
                source.spatialBlend = 0; source.volume = 1;
                var input = Signal(frames * 12, channels, rate, key);
                var clip = AudioClip.Create(key, frames * 12, channels, rate, false);
                clip.SetData(input, 0); source.clip = clip;
                source.Play();
                using (var writer = new BinaryWriter(File.Create(Path.Combine(output, key + ".f32"))))
                #if UNITY_EDITOR
                using (var buffer = new NativeArray<float>(frames * 2, Allocator.Persistent))
#endif
                {
#if !UNITY_EDITOR
                    m_Writer = writer; m_Written = 0;
#endif
                    for (int block = 0; block < 12; block++)
                    {
#if UNITY_EDITOR
                        yield return null;
#endif
                        if (block == 4 && key == "set_lp" && !mixer.SetFloat("Cutoff", 4000)) throw new Exception("SetFloat failed");
                        if (block == 8 && key == "set_lp" && !mixer.ClearFloat("Cutoff")) throw new Exception("ClearFloat failed");
                        if (block == 4 && key == "snapshot_lp") mixer.FindSnapshot("Other").TransitionTo(0);
                        if (key == "set_lp" || key == "snapshot_lp")
                        {
                            bool ok = mixer.GetFloat("Cutoff", out float cutoff);
                            records.Add("\""+key+".cutoff."+block+"\":\""+ok+":"+cutoff.ToString("R", System.Globalization.CultureInfo.InvariantCulture)+"\"");
                        }
#if UNITY_EDITOR
                        if (key == "dry") Debug.Log("dry block=" + block + " count=" + AudioRenderer.GetSampleCountForCaptureFrame() + " playing=" + source.isPlaying + " position=" + source.timeSamples + " dsp=" + AudioSettings.dspTime);
                        if (!AudioRenderer.Render(buffer)) throw new Exception("Render failed: " + key);
                        for (int i = 0; i < buffer.Length; i++) writer.Write(buffer[i]);
#else
                        RenderEngine(frames, rate);
#endif
                    }
                }
#if !UNITY_EDITOR
                m_Writer = null;
                if (m_Written != frames * 12 * 2) throw new Exception("Unexpected complete buffer length " + m_Written);
#endif
                records.Add("\""+key+"\":{\"file\":\""+key+".f32\",\"frames\":"+(frames*12)+",\"channels\":2}");
                source.Stop(); DestroyImmediate(go); DestroyImmediate(clip);
            }
#if UNITY_EDITOR
            AudioRenderer.Stop();
#endif
            File.WriteAllText(Path.Combine(output, "capture.json"), "{"+string.Join(",", records)+"}\n");
            Exit(0);
        }
        yield break;
    }
}
