using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Audio;

public class GeneratorBoundaryProbe : MonoBehaviour
{
    public static readonly string[] AllCases = {
        "idle", "shotPlain", "shotFilter", "generator", "generatorShot",
        "startInside", "endInside", "windowInside"
    };
    const int Rate = 48000, Frames = 1024, Channels = 2;
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    static string Json(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r") + "\"";
    static long Tick(double time) => (long)Math.Round(time * Rate);
    static void Fill(NativeArray<float> data, float value)
    {
        for (int i = 0; i < data.Length; i++) data[i] = value;
    }
    static void Exit(int code)
    {
        if (Environment.GetEnvironmentVariable("GENERATOR_BOUNDARY_ENGINE") == "1") { Application.Quit(code); return; }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(code);
#else
        Application.Quit(code);
#endif
    }
    IEnumerator Start()
    {
        var run = Capture();
        for (;;) {
            object next;
            try { if (!run.MoveNext()) yield break; next = run.Current; }
            catch (Exception e) { Debug.LogException(e); Exit(1); yield break; }
            yield return next;
        }
    }
    IEnumerator Capture()
    {
        var config = AudioSettings.GetConfiguration();
        config.sampleRate = Rate; config.dspBufferSize = Frames; config.speakerMode = AudioSpeakerMode.Stereo;
        if (!AudioSettings.Reset(config)) throw new Exception("Reset failed");
        Time.captureDeltaTime = Frames / (float)Rate;
        string output = Environment.GetEnvironmentVariable("GENERATOR_BOUNDARY_OUT");
        Directory.CreateDirectory(output);
        if (!AudioRenderer.Start()) throw new Exception("Renderer start failed");
        var actual = AudioSettings.GetConfiguration();
        var records = new List<string> { "\"release\":" + Json(Application.unityVersion), "\"rate\":48000",
            "\"layout\":{\"dspBufferSize\":" + actual.dspBufferSize + ",\"speakerMode\":" + Json(actual.speakerMode.ToString()) + "}" };
        string[] cases = Environment.GetEnvironmentVariable("GENERATOR_BOUNDARY_FOCUSED") == "1"
            ? new[] { "shotFilter", "windowInside" } : AllCases;
        records.Add("\"cases\":[" + string.Join(",", Array.ConvertAll(cases, Json)) + "]");
        var listener = gameObject.AddComponent<BoundaryListener>();
        var addSink = typeof(AudioRenderer).GetMethod("AddMixerGroupSink", F);
        // No state is configured, and no Unity API is called, inside either callback.
        foreach (string key in cases) {
            bool scheduled = key.EndsWith("Inside", StringComparison.Ordinal);
            int blocks = scheduled ? 88 : 12;
            var mixer = Resources.Load<AudioMixer>(key);
            var groups = mixer.FindMatchingGroups("");
            var master = Array.Find(groups, g => g.name == "Master");
            var child = Array.Find(groups, g => g.name == "Child");
            var go = new GameObject(key);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false; source.outputAudioMixerGroup = child;
            source.spatialBlend = 0; source.volume = 1; source.pitch = 1;
            BoundaryFilter filter = key == "shotPlain" ? null : go.AddComponent<BoundaryFilter>();
            if (filter != null) filter.Configure(128, Frames * Channels);
            var shot = AudioClip.Create("Shot", 4096, Channels, Rate, false);
            var shotData = new float[4096 * Channels];
            for (int frame = 0; frame < 4096; frame++) {
                shotData[frame * 2] = 0.125f; shotData[frame * 2 + 1] = -0.25f;
            }
            if (!shot.SetData(shotData, 0)) throw new Exception("SetData failed");
            yield return null;
            long origin = Tick(AudioSettings.dspTime);
            long start = scheduled ? 8192 + (key == "endInside" ? 0 : 73) : -1;
            long end = !scheduled ? -1 : key == "windowInside" ? 8192 + 185 : key == "endInside" ? 10240 + 185 : 12288;
            var events = new List<string>();
            void State(string phase, int block) {
                events.Add("{\"phase\":" + Json(phase) + ",\"block\":" + block + ",\"dsp\":" + (Tick(AudioSettings.dspTime) - origin)
                    + ",\"playing\":" + (source.isPlaying ? "true" : "false") + ",\"virtual\":" + (source.isVirtual ? "true" : "false")
                    + ",\"timeSamples\":" + source.timeSamples + ",\"calls\":" + (filter == null ? 0 : filter.Calls)
                    + ",\"listenerCalls\":" + listener.Calls + "}");
            }
            listener.Calls = 0;
            State("beforeSubmit", -1);
            string submit = "ok";
            try {
                if (scheduled) { source.PlayScheduled((origin + start) / (double)Rate); source.SetScheduledEndTime((origin + end) / (double)Rate); }
                else {
                    if (key == "generator" || key == "generatorShot") source.Play();
                    if (key == "shotPlain" || key == "shotFilter" || key == "generatorShot") source.PlayOneShot(shot);
                }
            } catch (Exception e) { submit = e.GetType().FullName + ": " + e.Message; }
            State("afterSubmit", -1);
            using (var buffer = new NativeArray<float>(Frames * Channels, Allocator.Persistent))
            using (var sink = new NativeArray<float>(Frames * Channels, Allocator.Persistent))
            using (var childSink = new NativeArray<float>(Frames * Channels, Allocator.Persistent))
            using (var pcm = new BinaryWriter(File.Create(Path.Combine(output, key + ".output.f32"))))
            using (var rawMaster = new BinaryWriter(File.Create(Path.Combine(output, key + ".master.f32"))))
            using (var rawChild = new BinaryWriter(File.Create(Path.Combine(output, key + ".child.f32")))) {
                for (int block = 0; block < blocks; block++) {
                    yield return null;
                    State("beforeRender", block);
                    Fill(sink, -3f); Fill(childSink, -3f); Fill(buffer, -7f);
                    if (!(bool)addSink.Invoke(null, new object[] { master, sink, false }) ||
                        !(bool)addSink.Invoke(null, new object[] { child, childSink, false })) throw new Exception("Sink failed");
                    if (!AudioRenderer.Render(buffer)) throw new Exception("Render failed");
                    State("afterRender", block);
                    for (int i = 0; i < buffer.Length; i++) { pcm.Write(buffer[i]); rawMaster.Write(sink[i]); rawChild.Write(childSink[i]); }
                }
                // Preserve the end state before an explicit Stop can change it.
                State("naturalFinal", blocks);
                source.Stop(); State("afterStop", blocks);
            }
            foreach (string suffix in new[] { "output", "master", "child" })
                records.Add(Json(key + "." + suffix) + ":{\"file\":" + Json(key + "." + suffix + ".f32") + ",\"frames\":" + (blocks * Frames) + ",\"channels\":2}");
            var calls = new List<string>();
            int count = filter == null ? 0 : filter.Calls;
            if (filter != null && filter.Overflow) throw new Exception("Callback trace capacity exceeded");
            using (var input = new BinaryWriter(File.Create(Path.Combine(output, key + ".input.f32"))))
            using (var emitted = new BinaryWriter(File.Create(Path.Combine(output, key + ".emitted.f32")))) {
                for (int call = 0; call < count; call++) {
                    calls.Add("{\"length\":" + filter.Lengths[call] + ",\"channels\":" + filter.Channels[call] + "}");
                    for (int i = 0; i < filter.Lengths[call]; i++) { input.Write(filter.Input[call][i]); emitted.Write(filter.Output[call][i]); }
                }
            }
            int samples = 0;
            if (filter != null) for (int i = 0; i < count; i++) samples += filter.Lengths[i];
            foreach (string suffix in new[] { "input", "emitted" })
                records.Add(Json(key + "." + suffix) + ":{\"file\":" + Json(key + "." + suffix + ".f32") + ",\"frames\":" + samples + ",\"channels\":1}");
            records.Add(Json(key + ".callbacks") + ":[" + string.Join(",", calls) + "]");
            records.Add(Json(key + ".events") + ":[" + string.Join(",", events) + "]");
            records.Add(Json(key + ".schedule") + ":{\"start\":" + start + ",\"end\":" + end + ",\"submit\":" + Json(submit) + "}");
            DestroyImmediate(go); DestroyImmediate(shot);
        }
        AudioRenderer.Stop(); records.Add("\"complete\":true");
        File.WriteAllText(Path.Combine(output, "capture.json"), "{" + string.Join(",", records) + "}\n");
        Exit(0);
    }
}

public class BoundaryFilter : MonoBehaviour
{
    public float[][] Input, Output;
    public int[] Lengths, Channels;
    public volatile int Calls;
    public volatile bool Overflow;
    public void Configure(int count, int length)
    {
        Input = new float[count][]; Output = new float[count][];
        Lengths = new int[count]; Channels = new int[count];
        for (int i = 0; i < count; i++) { Input[i] = new float[length]; Output[i] = new float[length]; }
    }
    void OnAudioFilterRead(float[] data, int channels)
    {
        int call = Calls;
        if (call >= Input.Length || data.Length > Input[call].Length) { Overflow = true; return; }
        Lengths[call] = data.Length; Channels[call] = channels;
        for (int i = 0; i < data.Length; i++) {
            Input[call][i] = data[i]; data[i] = data[i] * 0.5f + 0.0625f; Output[call][i] = data[i];
        }
        Calls = call + 1;
    }
}
public class BoundaryListener : MonoBehaviour
{
    public volatile int Calls;
    void OnAudioFilterRead(float[] data, int channels) { Calls++; }
}
