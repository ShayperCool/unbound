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
        "wall_gap", "update_gap", "wake_voice", "wake_hold", "silent_voice_end", "wake_gain", "wake_wet", "wake_snapshot",
        "mono", "parent", "sibling", "muted", "bypassed", "effect_bypassed", "disable",
        "delay100", "delay250", "delay500", "delay1000", "delay1023", "delay1024", "delay1025", "delay1500", "delay2000", "delay3000",
        "level_low", "level_high", "level_left", "level_left_high", "level_antiphase", "level_mono", "voice_threshold", "threshold_low", "threshold_equal", "threshold_high", "threshold_left", "threshold_right",
        "threshold_antiphase", "threshold_mono", "threshold_peak", "threshold_disabled", "threshold_edge_low", "threshold_edge_high", "threshold_edge_left", "threshold_db20_low", "threshold_db20_high", "threshold_db0_low", "threshold_db0_high", "threshold_peak_loud", "threshold_a0001", "threshold_a0002", "threshold_a00024", "threshold_a00025", "threshold_left0003", "threshold_left00035", "threshold_peak_high", "threshold_a001", "threshold_a003", "threshold_a005", "threshold_a007", "threshold_a00705", "threshold_a00708", "threshold_a009", "threshold_left005", "threshold_left007", "threshold_left009", "voice_low", "voice_peak", "lp", "hp", "lp_disabled", "hp_disabled", "reset"
    };
    public static float Amplitude(string key) => key=="level_low"?0.00999f:key=="level_left_high"||key=="level_mono"?0.0142f:key.StartsWith("level")?0.01003f: key=="threshold_edge_low"?0.00006f:key=="threshold_edge_high"?0.00009f:key=="threshold_edge_left"?0.0001f:key=="threshold_db20_low"?0.004f:key=="threshold_db20_high"?0.006f:key=="threshold_db0_low"?0.4f:key=="threshold_db0_high"?0.6f:key=="threshold_peak_loud"?0.2f: key=="threshold_a0001"?0.0001f:key=="threshold_a0002"?0.0002f:key=="threshold_a00024"?0.00024f:key=="threshold_a00025"?0.00025f:key=="threshold_left0003"?0.0003f:key=="threshold_left00035"?0.00035f:key=="threshold_peak_high"?0.01003f: key=="threshold_a001"?0.001f:key=="threshold_a003"?0.003f:key=="threshold_a005"||key=="threshold_left005"?0.005f:key=="threshold_a007"||key=="threshold_left007"?0.007f:key=="threshold_a00705"?0.00705f:key=="threshold_a00708"?0.00708f:key=="threshold_a009"||key=="threshold_left009"?0.009f:key=="threshold_low"?0.00999f:key=="threshold_high"?0.01003f:0.01f;
    public static float Delay(string key) => key.StartsWith("threshold") || key.StartsWith("level") ? 100 : key.StartsWith("delay") ? float.Parse(key.Substring(5),System.Globalization.CultureInfo.InvariantCulture) : 5000;
    public static bool Hold(string key) => key=="wake_hold" || key=="silent_voice_end" || key=="burst" || key=="tickfast" || key=="tickslow" || key=="wall_gap" || key=="update_gap";
    public static float Threshold(string key) => key.StartsWith("threshold_db20")?-20:key.StartsWith("threshold_db0")?0: key=="voice_threshold" || key.StartsWith("level") || key.StartsWith("threshold") || key.StartsWith("lp") || key.StartsWith("hp") ? -40 : -80;
    public const int Blocks = 1024;
    const BindingFlags F = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static bool Engine => Environment.GetEnvironmentVariable("SUSPEND_PROBE_ENGINE") == "1";
    BinaryWriter listener;
    bool captured;
    public void RecordListener(float[] values) { if (listener != null) foreach (float x in values) listener.Write(x); }
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
            int channels = key == "mono" || key=="threshold_mono" || key=="level_mono" ? 1 : 2;
            int period=(((int)(100f*rate/1000f))+7)&~7;
            int clipFrames=key.StartsWith("level")?1024+period:key=="silent_voice_end"?8192:4096;
            var clip = AudioClip.Create(key,clipFrames,channels,rate,false);
            var signal = new float[clipFrames*channels];
            signal[2048*channels] = 0.25f;
            if (key.StartsWith("lp") || key.StartsWith("hp")) for(int i=1024*channels;i<2048*channels;i++) signal[i]=0.125f;
            if (channels == 2) signal[3072*2+1] = -0.125f;
            if(key.StartsWith("threshold") || key.StartsWith("level")) {
                Array.Clear(signal,0,signal.Length);
                float amplitude=Amplitude(key);
                for(int i=1024*channels;i<clipFrames*channels;i++) {
                    int c=i%channels;
                    signal[i]=(key.StartsWith("threshold_left")||key=="threshold_edge_left"||key.StartsWith("level_left"))&&c==1 || key=="threshold_right"&&c==0 || key.StartsWith("threshold_peak")&&i/channels!=2048 ? 0 : (key=="threshold_antiphase"||key=="level_antiphase")&&c==1 ? -amplitude : amplitude;
                }
            }
            if (!clip.SetData(signal,0)) throw new Exception("SetData failed");
            var go = new GameObject(key); var source = go.AddComponent<AudioSource>();
            source.outputAudioMixerGroup = child; source.spatialBlend = 0; source.volume = 1; source.clip = clip;
            AudioSource other=null; AudioClip sustained=null;
            if (key == "sibling" || key.StartsWith("voice") || Hold(key)) {
                other = go.AddComponent<AudioSource>();
                sustained = AudioClip.Create("sustained",1024,2,rate,false);
                var data = new float[2048]; for (int i=0;i<data.Length;i++) {
                    float amplitude=key=="voice_low"||key=="voice_threshold"?0.000001f:0.01f;
                    data[i] = key=="threshold_left"&&i%2==1 || key=="threshold_right"&&i%2==0 || key=="voice_peak"&&i!=0 ? 0 : key=="threshold_antiphase"&&i%2==1 ? -amplitude : amplitude;
                }
                sustained.SetData(data,0); other.clip=sustained; other.loop=true;
                other.outputAudioMixerGroup=Array.Find(groups,g=>g.name=="Sibling"); if(!Hold(key)) other.Play();
            }
            var sink = new NativeArray<float>(2048,Allocator.Persistent); sinks.Add(sink);
            var buffer = new NativeArray<float>(2048,Allocator.Persistent);
            var addSink = typeof(AudioRenderer).GetMethod("AddMixerGroupSink",F);
            source.Play();
            using (var lw = new BinaryWriter(File.Create(Path.Combine(output,key+".listener.f32"))))
            using (var mw = new BinaryWriter(File.Create(Path.Combine(output,key+".master.f32"))))
            using (var pw = new BinaryWriter(File.Create(Path.Combine(output,key+".f32")))) {
                listener=lw;
                for (int block=0;block<Blocks;block++) {
                    if (key != "burst" || block == 0) yield return null;
                    if (block == 8 && key == "wall_gap") System.Threading.Thread.Sleep(2500);
                    if (block == 8 && key == "update_gap") for (int i=0;i<160;i++) { yield return null; }
                    if (block == 384) {
                        if (key == "wake_voice") source.Play();
                        if (Hold(key)) other.Play();
                        if (key == "wake_snapshot") mixer.FindSnapshot("Other").TransitionTo(0);
                        if (key == "reset") { if(!AudioSettings.Reset(config)) throw new Exception("Reset failed"); if(!AudioRenderer.Start()) throw new Exception("Renderer restart failed"); source.Play(); }
                        if (key == "disable") { source.enabled=false; source.enabled=true; source.Play(); }
                        if (key == "wake_gain" && !mixer.SetFloat("Gain",-6)) throw new Exception("SetFloat failed");
                        if (key == "wake_wet" && !mixer.SetFloat("Wet",0.5f)) throw new Exception("SetFloat failed");
                       
                    }
                    if (!(bool)addSink.Invoke(null,new object[]{master,sink,false})) throw new Exception("Sink failed");
                    if (!AudioRenderer.Render(buffer)) throw new Exception("Render failed");
                    for(int i=0;i<buffer.Length;i++) { pw.Write(buffer[i]); mw.Write(sink[i]); }
                }
                listener=null;
            }
            foreach (string suffix in new[]{"",".master",".listener"})
                records.Add("\""+key+suffix+"\":{\"file\":\""+key+suffix+".f32\",\"frames\":"+(suffix==".listener" ? new FileInfo(Path.Combine(output,key+suffix+".f32")).Length/8 : Blocks*1024)+",\"channels\":2}");
            Debug.Log("Completed "+key+" elapsed_ms="+elapsed.ElapsedMilliseconds);
            if(!mixer.SetFloat("Wet",0)) throw new Exception("Tail cleanup failed");
            Save(); buffer.Dispose(); source.Stop(); DestroyImmediate(go); DestroyImmediate(clip); if(sustained) DestroyImmediate(sustained);
        }
        AudioRenderer.Stop(); foreach(var sink in sinks) sink.Dispose(); Save(); Exit(0);
    }
}
public class SuspendListener : MonoBehaviour {
    public SuspendProbe Probe;
    void OnAudioFilterRead(float[] data,int channels) => Probe.RecordListener(data);
}
