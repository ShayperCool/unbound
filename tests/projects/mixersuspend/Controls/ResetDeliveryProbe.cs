using System;
using System.IO;
using System.Threading;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;

// One reset per process. Callbacks only copy to preallocated storage; files are written later.
public class ResetDeliveryProbe : MonoBehaviour
{
    public sealed class Blocks
    {
        readonly float[] pcm = new float[64 * 2048];
        readonly int[] phases = new int[64];
        readonly int[] lengths = new int[64];
        int count, used;
        public void Read(float[] data, int phase)
        {
            int index = count;
            if (index >= phases.Length || used + data.Length > pcm.Length) throw new Exception("Callback storage exhausted");
            phases[index] = phase; lengths[index] = data.Length;
            Array.Copy(data, 0, pcm, used, data.Length); used += data.Length;
            Volatile.Write(ref count, index + 1);
        }
        public string Save(string root, string key)
        {
            using (var writer = new BinaryWriter(File.Create(Path.Combine(root, key + ".f32"))))
                for (int i = 0; i < used; i++) writer.Write(pcm[i]);
            var metadata = new List<string>();
            for (int i = 0; i < count; i++) metadata.Add("[" + phases[i] + "," + lengths[i] + "]");
            return "\"" + key + "\":{\"file\":\"" + key + ".f32\",\"samples\":" + used + ",\"callbacks\":[" + string.Join(",", metadata) + "]}";
        }
    }
    public readonly Blocks Listener = new Blocks();
    public readonly Blocks Source = new Blocks();
    public volatile int Phase = -1;
    bool started;
    void OnAudioFilterRead(float[] data, int channels) { if (Phase >= 0) Listener.Read(data, Phase); }
    void Changed(bool deviceChanged) { if (deviceChanged) throw new Exception("Unexpected device change"); Phase = 2; }
    System.Collections.IEnumerator Start()
    {
        if (started) yield break; started = true;
        var config = AudioSettings.GetConfiguration();
        config.sampleRate = int.Parse(Environment.GetEnvironmentVariable("SUSPEND_PROBE_RATE"));
        config.speakerMode = AudioSpeakerMode.Stereo; config.dspBufferSize = 1024;
        if (!AudioSettings.Reset(config)) throw new Exception("Initial reset failed");
        Time.captureDeltaTime = 1024f / config.sampleRate;
        gameObject.AddComponent<AudioListener>();
        var sourceObject = new GameObject("delivery-source");
        var source = sourceObject.AddComponent<AudioSource>();
        sourceObject.AddComponent<ResetDeliverySource>().Probe = this;
        var clip = AudioClip.Create("delivery", 48000, 1, config.sampleRate, false);
        var data = new float[48000]; Array.Fill(data, 0.125f); clip.SetData(data, 0);
        source.clip = clip; source.loop = true;
        if (!AudioRenderer.Start()) throw new Exception("Capture start failed");
        source.Play();
        var buffer = new NativeArray<float>(2048, Allocator.Persistent);
        for (int i = 0; i < 4; i++) { yield return null; if (!AudioRenderer.Render(buffer)) throw new Exception("Warmup render failed"); }
        Phase = 0;
        if (!AudioRenderer.Render(buffer)) throw new Exception("Control render failed");
        Phase = 1;
        AudioSettings.OnAudioConfigurationChanged += Changed;
        if (!AudioSettings.Reset(config)) throw new Exception("Reset failed");
        bool notified = Phase == 2;
        Phase = 3;
        if (!AudioRenderer.Start()) throw new Exception("Capture restart failed");
        yield return null;
        Phase = 4;
        if (!AudioRenderer.Render(buffer)) throw new Exception("Restart render failed");
        Phase = -1;
        AudioSettings.OnAudioConfigurationChanged -= Changed;
        source.Stop(); AudioRenderer.Stop(); buffer.Dispose(); DestroyImmediate(sourceObject); DestroyImmediate(clip);
        string output = Environment.GetEnvironmentVariable("SUSPEND_PROBE_OUT"); Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "capture.json"), "{\"release\":\"" + Application.unityVersion + "\",\"rate\":" + config.sampleRate + ",\"notified\":" + (notified ? "true" : "false") + "," + Listener.Save(output,"listener") + "," + Source.Save(output,"source") + "}\n");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit(0);
#endif
    }
}
public class ResetDeliverySource : MonoBehaviour
{
    public ResetDeliveryProbe Probe;
    void OnAudioFilterRead(float[] data, int channels) { if (Probe != null && Probe.Phase >= 0) Probe.Source.Read(data, Probe.Phase); }
}
