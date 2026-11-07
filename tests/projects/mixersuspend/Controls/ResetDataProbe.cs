using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;

public class ResetDataProbe : MonoBehaviour
{
    static bool Engine => Environment.GetEnvironmentVariable("SUSPEND_RESETDATA_ENGINE") == "1";
    readonly List<string> rows = new List<string>();
    string output;
    void Value(string key, int value) => rows.Add("\"" + key + "\":" + value);
    void State(string key, AudioClip clip) {
        Value(key + ".valid", clip ? 1 : 0);
        Value(key + ".frames", clip.samples); Value(key + ".channels", clip.channels);
        Value(key + ".frequency", clip.frequency); Value(key + ".lengthBits", BitConverter.ToInt32(BitConverter.GetBytes(clip.length), 0));
        Value(key + ".load", (int)clip.loadState);
        var data = new float[8]; Array.Fill(data, 0.375f);
        Value(key + ".read", clip.GetData(data, 0) ? 1 : 0);
        Value(key + ".loadAfterRead", (int)clip.loadState);
        for (int i = 0; i < data.Length; i++) Value(key + ".data" + i, BitConverter.ToInt32(BitConverter.GetBytes(data[i]), 0));
    }
    void Save() => File.WriteAllText(Path.Combine(output, "capture.json"), "{" + string.Join(",", rows) + "}\n");
    static AudioClip Generated(string name, int rate) {
        var clip = AudioClip.Create(name, 4096, 2, rate, false); var data = new float[8192];
        Array.Fill(data, 0.25f); if (!clip.SetData(data, 0)) throw new Exception("SetData failed"); return clip;
    }
    System.Collections.IEnumerator Start() {
        var run = Capture();
        for (;;) {
            object next;
            try { if (!run.MoveNext()) yield break; next = run.Current; }
            catch (Exception e) { Debug.LogException(e); Exit(1); yield break; }
            yield return next;
        }
    }
    static void Exit(int code) {
        if (Engine) { Application.Quit(code); return; }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(code);
#else
        Application.Quit(code);
#endif
    }
    System.Collections.IEnumerator Capture() {
        output = Environment.GetEnvironmentVariable("SUSPEND_PROBE_OUT"); Directory.CreateDirectory(output);
        var disk = Resources.Load<AudioClip>("reset_disk");
        var modified = Resources.Load<AudioClip>("reset_modified");
        var config = AudioSettings.GetConfiguration(); config.sampleRate = int.Parse(Environment.GetEnvironmentVariable("SUSPEND_PROBE_RATE"));
        config.speakerMode = AudioSpeakerMode.Stereo; config.dspBufferSize = 1024;
        if (!AudioSettings.Reset(config)) throw new Exception("Initial reset failed");
        rows.Add("\"release\":\"" + Application.unityVersion + "\""); Value("rate", config.sampleRate);
        foreach (int frequency in new[] { 44100, 48000 })
        foreach (int frames in new[] { 1, 43, 44, 45, 47, 48, 49, 4096, 48000 }) {
            var timing = AudioClip.Create("length", frames, 1, frequency, false);
            Value("length." + frequency + "." + frames, BitConverter.ToInt32(BitConverter.GetBytes(timing.length), 0));
            DestroyImmediate(timing);
        }
        Time.captureDeltaTime = 1024f / config.sampleRate; gameObject.AddComponent<AudioListener>();
        var source = gameObject.AddComponent<AudioSource>();
        var played = Generated("played", config.sampleRate);
        var clips = new[] { played, Generated("never", config.sampleRate), disk, modified,
                            Instantiate(played), Instantiate(disk),
                            AudioClip.Create("callback", 4096, 2, config.sampleRate, false, data => Array.Fill(data, 0.125f)),
                            AudioClip.Create("stream", 4096, 2, config.sampleRate, true, data => Array.Fill(data, 0.125f)) };
        var cloneData = new float[8192]; Array.Fill(cloneData, 0.5f);
        Value("clone.asset.write", clips[5].SetData(cloneData, 0) ? 1 : 0);
        var changed = new float[8192]; Array.Fill(changed, 0.25f); Value("asset.write", clips[3].SetData(changed, 0) ? 1 : 0);
        try { Value("clone.script.write", clips[4].SetData(changed, 0) ? 1 : 0); }
        catch (ArgumentException) { Value("clone.script.write", 2); }
        for (int i = 0; i < clips.Length; i++) State("before." + i, clips[i]); Save();
        if (!AudioRenderer.Start()) throw new Exception("Capture start failed");
        var buffer = new NativeArray<float>(2048, Allocator.Persistent);
        source.clip = clips[0]; source.Play();
        for (int i = 0; i < 16; i++) { yield return null; if (!AudioRenderer.Render(buffer)) throw new Exception("Render failed"); }
        var lateClone = Instantiate(clips[0]); State("playedClone", lateClone); DestroyImmediate(lateClone);
        Value("reset", AudioSettings.Reset(config) ? 1 : 0);
        Value("restart", AudioRenderer.Start() ? 1 : 0);
        for (int i = 0; i < clips.Length; i++) State("after." + i, clips[i]); Save();
        for (int phase = 0; phase < clips.Length + 4; phase++) {
            source.clip = phase < clips.Length ? clips[phase] : Generated("recreated", phase == clips.Length ? config.sampleRate : 48000);
            source.loop = phase >= clips.Length + 2;
            source.pitch = phase == clips.Length + 3 ? 0.7f : 1f;
            if (source.loop) {
                source.clip.SetData(new[] { 0.125f, -0.125f }, 0);
                source.clip.SetData(new[] { 0.5f, -0.5f }, 4095);
            }
            source.Play(); Value("play." + phase, source.isPlaying ? 1 : 0);
            string file = "phase" + phase + ".f32";
            using (var writer = new BinaryWriter(File.Create(Path.Combine(output, file)))) {
                for (int block = 0; block < 8; block++) {
                    if (block != 0) yield return null;
                    if (!AudioRenderer.Render(buffer)) throw new Exception("Render failed");
                    for (int i = 0; i < buffer.Length; i++) writer.Write(buffer[i]);
                }
            }
            rows.Add("\"phase" + phase + "\":{\"file\":\"" + file + "\",\"frames\":8192,\"channels\":2}");
            source.Stop(); yield return null;
        }
        var rewrite = new float[8192]; Array.Fill(rewrite, 0.5f);
        Value("rewrite.old", clips[0].SetData(rewrite, 0) ? 1 : 0); State("rewritten", clips[0]);
        rows.Add("\"complete\":true"); AudioRenderer.Stop(); buffer.Dispose(); Save(); Exit(0);
    }
}
