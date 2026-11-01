using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Audio;
using Unity.Collections;

public class SuspendProbe : MonoBehaviour
{
    public static readonly string[] Cases = {
        "default", "disabled", "enabled", "burst", "tickfast", "tickslow",
        "wall_gap", "update_gap", "wake_voice", "wake_hold", "wake_gain", "wake_wet", "wake_snapshot",
        "mono", "parent", "sibling", "muted", "bypassed", "effect_bypassed", "reset", "disable",
        "delay100", "delay250", "delay500", "delay1000", "delay1500", "delay2000", "delay3000",
        "threshold_low", "threshold_equal", "threshold_high", "threshold_left", "threshold_right",
        "threshold_antiphase", "threshold_mono", "threshold_peak", "lp", "hp", "lp_disabled", "hp_disabled"
    };
    public static float Delay(string key) => key.StartsWith("delay") ? float.Parse(key.Substring(5),System.Globalization.CultureInfo.InvariantCulture) : 5000;
    public static float Threshold(string key) => key.StartsWith("threshold") || key.StartsWith("lp") || key.StartsWith("hp") ? -40 : -80;
    public const int Blocks = 1024;
    const BindingFlags F = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static bool Engine => Environment.GetEnvironmentVariable("SUSPEND_PROBE_ENGINE") == "1";
    BinaryWriter listener;
    bool captured;
    public void RecordListener(float[] values) { if (listener != null) foreach (float x in values) listener.Write(x); }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!Engine) return;
        foreach (var old in UnityEngine.Object.FindObjectsOfType<SuspendProbe>()) DestroyImmediate(old.gameObject);
        var probe = new GameObject("SuspendProbe").AddComponent<SuspendProbe>();
        try { var run = probe.Capture(); while (run.MoveNext()) {} }
        catch (Exception e) { Debug.LogException(e); Application.Quit(1); }
    }
    void Flush()
    {
        if (!Engine) return;
        var assembly = Type.GetType("Unbound.Runtime.AudioBackend, UnityEngine.CoreModule", true).Assembly;
        typeof(Time).GetField("s_RealDeltaTime", F).SetValue(null, 0.0);
        typeof(Time).GetField("s_DeltaTime", F).SetValue(null, 0f);
        assembly.GetType("Unbound.Runtime.AudioSystem").GetMethod("Update", F).Invoke(null, null);
    }
    System.Collections.IEnumerator Start()
    {
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
    System.Collections.IEnumerator Capture()
    {
        if (captured) yield break;
        captured = true;
        var config = AudioSettings.GetConfiguration();
        config.sampleRate = int.Parse(Environment.GetEnvironmentVariable("SUSPEND_PROBE_RATE"));
        config.speakerMode = AudioSpeakerMode.Stereo; config.dspBufferSize = 1024;
        if (!AudioSettings.Reset(config)) throw new Exception("Reset failed");
        Time.captureDeltaTime = 1024f / config.sampleRate;
        int rate = AudioSettings.outputSampleRate;
        string output = Environment.GetEnvironmentVariable("SUSPEND_PROBE_OUT"); Directory.CreateDirectory(output);
        AudioListener.volume = 1; gameObject.AddComponent<AudioListener>();
        gameObject.AddComponent<SuspendListener>().Probe = this;
        if (!AudioRenderer.Start()) throw new Exception("Renderer start failed");
        var sinks = new List<NativeArray<float>>();
        var records = new List<string> { "\"release\":\""+Application.unityVersion+"\"", "\"rate\":"+rate };
        void Save() => File.WriteAllText(Path.Combine(output,"capture.json"),"{"+string.Join(",",records)+"}\n");
        Save();
        foreach (string key in Cases) {
            Debug.Log("Suspension case " + key);
            Time.captureDeltaTime = key=="tickfast" ? 0.00001f : key=="tickslow" ? 1f : 1024f/rate;
            var elapsed=System.Diagnostics.Stopwatch.StartNew();
            var mixer = Resources.Load<AudioMixer>(key); if (!mixer) throw new Exception("Missing mixer "+key);
            var groups = mixer.FindMatchingGroups("");
            var child = Array.Find(groups,g=>g.name=="Child");
            var master = Array.Find(groups,g=>g.name=="Master");
            int channels = key == "mono" || key=="threshold_mono" ? 1 : 2;
            var clip = AudioClip.Create(key,4096,channels,rate,false);
            var signal = new float[4096*channels];
            signal[2048*channels] = 0.25f;
            if (key.StartsWith("lp") || key.StartsWith("hp")) for(int i=1024*channels;i<2048*channels;i++) signal[i]=0.125f;
            if (channels == 2) signal[3072*2+1] = -0.125f;
            if (!clip.SetData(signal,0)) throw new Exception("SetData failed");
            var go = new GameObject(key); var source = go.AddComponent<AudioSource>();
            source.outputAudioMixerGroup = child; source.spatialBlend = 0; source.volume = 1; source.clip = clip;
            AudioSource other=null; AudioClip sustained=null;
            if (key == "sibling" || key.StartsWith("threshold") || key=="wake_hold") {
                other = go.AddComponent<AudioSource>();
                sustained = AudioClip.Create("sustained",1024,2,rate,false);
                var data = new float[2048]; for (int i=0;i<data.Length;i++) {
                    float amplitude=key=="threshold_low"?0.00999f:key=="threshold_high"?0.01001f:0.01f;
                    data[i] = key=="threshold_left"&&i%2==1 || key=="threshold_right"&&i%2==0 || key=="threshold_peak"&&i!=0 ? 0 : key=="threshold_antiphase"&&i%2==1 ? -amplitude : amplitude;
                }
                sustained.SetData(data,0); other.clip=sustained; other.loop=true;
                other.outputAudioMixerGroup=Array.Find(groups,g=>g.name=="Sibling"); if(key!="wake_hold") other.Play();
            }
            var sink = new NativeArray<float>(2048,Allocator.Persistent); sinks.Add(sink);
            var buffer = new NativeArray<float>(2048,Allocator.Persistent);
            var addSink = typeof(AudioRenderer).GetMethod("AddMixerGroupSink",F);
            source.Play(); Flush();
            using (var lw = new BinaryWriter(File.Create(Path.Combine(output,key+".listener.f32"))))
            using (var mw = new BinaryWriter(File.Create(Path.Combine(output,key+".master.f32"))))
            using (var pw = new BinaryWriter(File.Create(Path.Combine(output,key+".f32")))) {
                listener=lw;
                for (int block=0;block<Blocks;block++) {
                    if (!Engine && (key != "burst" || block == 0)) yield return null;
                    if (block == 8 && key == "wall_gap") System.Threading.Thread.Sleep(2500);
                    if (block == 8 && key == "update_gap") for (int i=0;i<160;i++) { if (!Engine) yield return null; Flush(); }
                    if (block == 384) {
                        if (key == "wake_voice") source.Play();
                        if (key == "wake_hold") other.Play();
                        if (key == "wake_snapshot") mixer.FindSnapshot("Other").TransitionTo(0);
                        if (key == "reset") { if(!AudioSettings.Reset(config)) throw new Exception("Reset failed"); source.Play(); }
                        if (key == "disable") { source.enabled=false; Flush(); source.enabled=true; source.Play(); }
                        if (key == "wake_gain" && !mixer.SetFloat("Gain",-6)) throw new Exception("SetFloat failed");
                        if (key == "wake_wet" && !mixer.SetFloat("Wet",0.5f)) throw new Exception("SetFloat failed");
                        Flush();
                    }
                    if (!(bool)addSink.Invoke(null,new object[]{master,sink,false})) throw new Exception("Sink failed");
                    if (!AudioRenderer.Render(buffer)) throw new Exception("Render failed");
                    for(int i=0;i<buffer.Length;i++) { pw.Write(buffer[i]); mw.Write(sink[i]); }
                    Flush();
                }
                listener=null;
            }
            foreach (string suffix in new[]{"",".master",".listener"})
                records.Add("\""+key+suffix+"\":{\"file\":\""+key+suffix+".f32\",\"frames\":"+(Blocks*1024)+",\"channels\":2}");
            Debug.Log("Completed "+key+" elapsed_ms="+elapsed.ElapsedMilliseconds);
            Save(); buffer.Dispose(); source.Stop(); DestroyImmediate(go); DestroyImmediate(clip); if(sustained) DestroyImmediate(sustained);
        }
        AudioRenderer.Stop(); foreach(var sink in sinks) sink.Dispose(); Save(); Exit(0);
    }
}
public class SuspendListener : MonoBehaviour {
    public SuspendProbe Probe;
    void OnAudioFilterRead(float[] data,int channels) => Probe.RecordListener(data);
}
