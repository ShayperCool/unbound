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
            float v = f == 0 || f == 2048 ? 0.25f : 0f;
            if (kind == "step_lp") v = f < 2048 ? 0.125f : 0f;
            if (kind == "tone_hp") v = (float)(0.125 * Math.Sin(2 * Math.PI * 400 * f / rate) + 0.0625 * Math.Sin(2 * Math.PI * 6000 * f / rate));
            a[f * channels + c] = c == 0 ? v : (f == 31 ? -0.125f : v * 0.5f);
        }
        return a;
    }
#if UNITY_EDITOR
    void Start()
    {
        try
        {
            var configuration = AudioSettings.GetConfiguration();
            configuration.sampleRate = int.Parse(Environment.GetEnvironmentVariable("FILTER_PROBE_RATE") ?? "48000");
            configuration.speakerMode = AudioSpeakerMode.Stereo;
            if (!AudioSettings.Reset(configuration)) throw new Exception("AudioSettings.Reset failed");
            Time.captureFramerate = 60;
            var output = Environment.GetEnvironmentVariable("FILTER_PROBE_OUT");
            Directory.CreateDirectory(output);
            int rate = AudioSettings.outputSampleRate;
            int frames = rate / 60;
            AudioListener.volume = 1;
            gameObject.AddComponent<AudioListener>();
            if (!AudioRenderer.Start()) throw new Exception("AudioRenderer.Start failed");
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
                using (var buffer = new NativeArray<float>(frames * 2, Allocator.Temp))
                {
                    for (int block = 0; block < 12; block++)
                    {
                        if (block == 4 && key == "set_lp") mixer.SetFloat("Cutoff", 4000);
                        if (block == 4 && key == "snapshot_lp") mixer.FindSnapshot("Other").TransitionTo(0);
                        if (!AudioRenderer.Render(buffer)) throw new Exception("Render failed: " + key);
                        for (int i = 0; i < buffer.Length; i++) writer.Write(buffer[i]);
                    }
                }
                records.Add("\""+key+"\":{\"file\":\""+key+".f32\",\"frames\":"+(frames*12)+",\"channels\":2}");
                source.Stop(); DestroyImmediate(go); DestroyImmediate(clip);
            }
            AudioRenderer.Stop();
            File.WriteAllText(Path.Combine(output, "capture.json"), "{"+string.Join(",", records)+"}\n");
            UnityEditor.EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); UnityEditor.EditorApplication.Exit(1); }
    }
#endif
}
