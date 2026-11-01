using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using Unity.Collections;
public class LifecycleProbe : MonoBehaviour {
    public static readonly string[] Cases = { "control", "gain", "wet", "snapshot", "mono_gain", "muted_sibling", "silent_sibling" };
    public const int Blocks=1024;
    const BindingFlags F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    static bool Engine=>Environment.GetEnvironmentVariable("SUSPEND_LIFECYCLE_ENGINE")=="1";
    BinaryWriter listener;
    public void Listen(float[] data){if(listener!=null)foreach(float x in data)listener.Write(x);}
    System.Collections.IEnumerator Start(){var run=Capture();for(;;){object next;try{if(!run.MoveNext())yield break;next=run.Current;}catch(Exception e){Debug.LogException(e);Exit(1);yield break;}yield return next;}}
    static void Exit(int code){if(Engine){Application.Quit(code);return;}
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(code);
#else
        Application.Quit(code);
#endif
    }
    System.Collections.IEnumerator Capture(){
        var config=AudioSettings.GetConfiguration();config.sampleRate=int.Parse(Environment.GetEnvironmentVariable("SUSPEND_PROBE_RATE"));config.speakerMode=AudioSpeakerMode.Stereo;config.dspBufferSize=1024;
        if(!AudioSettings.Reset(config))throw new Exception("Initial reset failed");Time.captureDeltaTime=1024f/config.sampleRate;
        string output=Environment.GetEnvironmentVariable("SUSPEND_PROBE_OUT");Directory.CreateDirectory(output);
        gameObject.AddComponent<AudioListener>();gameObject.AddComponent<LifecycleListener>().Probe=this;
        if(!AudioRenderer.Start())throw new Exception("Start failed");
        var arrays=new List<NativeArray<float>>();var records=new List<string>{"\"release\":\""+Application.unityVersion+"\"","\"rate\":"+AudioSettings.outputSampleRate};
        foreach(string key in Cases){
            Debug.Log("Lifecycle case "+key);
            var mixer=Resources.Load<AudioMixer>(key=="snapshot"?"wake_snapshot":key=="muted_sibling"?"lifecycle_muted":"enabled");
            if(!mixer.ClearFloat("Gain")||!mixer.ClearFloat("Wet"))throw new Exception("ClearFloat failed");
            var initial=mixer.FindSnapshot("Snapshot");if(!initial)throw new Exception("Missing initial snapshot");initial.TransitionTo(0);
            var groups=mixer.FindMatchingGroups("");var child=Array.Find(groups,g=>g.name=="Child");var sibling=Array.Find(groups,g=>g.name=="Sibling");var master=Array.Find(groups,g=>g.name=="Master");
            int channels=key=="mono_gain"?1:2;
            var go=new GameObject(key);var source=go.AddComponent<AudioSource>();var clip=AudioClip.Create(key,4096,channels,config.sampleRate,false);var data=new float[4096*channels];data[2048*channels]=0.25f;if(channels==2)data[3072*2+1]=-0.125f;
            clip.SetData(data,0);source.clip=clip;source.outputAudioMixerGroup=child;source.Play();
            var other=go.AddComponent<AudioSource>();var sustained=AudioClip.Create("hold",1024,2,config.sampleRate,false);var constant=new float[2048];if(key!="silent_sibling")for(int i=0;i<constant.Length;i++)constant[i]=0.01f;sustained.SetData(constant,0);other.clip=sustained;other.loop=true;other.outputAudioMixerGroup=sibling;
            bool early=key=="muted_sibling"||key=="silent_sibling";if(early)other.Play();
            var sink=new NativeArray<float>(2048,Allocator.Persistent);arrays.Add(sink);var buffer=new NativeArray<float>(2048,Allocator.Persistent);arrays.Add(buffer);var addSink=typeof(AudioRenderer).GetMethod("AddMixerGroupSink",F);
            using(var lw=new BinaryWriter(File.Create(Path.Combine(output,key+".listener.f32"))))
            using(var mw=new BinaryWriter(File.Create(Path.Combine(output,key+".master.f32"))))
            using(var pw=new BinaryWriter(File.Create(Path.Combine(output,key+".f32")))){
                listener=lw;
                for(int block=0;block<Blocks;block++){
                    yield return null;
                    if(block==192){if(key=="gain"||key=="mono_gain")mixer.SetFloat("Gain",-6);if(key=="wet")mixer.SetFloat("Wet",0.5f);if(key=="snapshot")mixer.FindSnapshot("Other").TransitionTo(0);}
                    if(block==384&&!early)other.Play();
                    if(block==32||block==400)records.Add("\""+key+".other"+block+"\":{\"playing\":"+(other.isPlaying?"true":"false")+",\"virtual\":"+(other.isVirtual?"true":"false")+"}");
                    if(!(bool)addSink.Invoke(null,new object[]{master,sink,false}))throw new Exception("Sink failed");if(!AudioRenderer.Render(buffer))throw new Exception("Render failed");for(int i=0;i<buffer.Length;i++){pw.Write(buffer[i]);mw.Write(sink[i]);}
                }
                listener=null;
            }
            foreach(string suffix in new[]{"",".master",".listener"})records.Add("\""+key+suffix+"\":{\"file\":\""+key+suffix+".f32\",\"frames\":"+(new FileInfo(Path.Combine(output,key+suffix+".f32")).Length/8)+",\"channels\":2}");
            source.Stop();other.Stop();DestroyImmediate(go);DestroyImmediate(clip);DestroyImmediate(sustained);File.WriteAllText(Path.Combine(output,"capture.json"),"{"+string.Join(",",records)+"}\n");
        }
        AudioRenderer.Stop();foreach(var array in arrays)array.Dispose();Exit(0);
    }
}
public class LifecycleListener : MonoBehaviour{public LifecycleProbe Probe;void OnAudioFilterRead(float[] data,int channels)=>Probe.Listen(data);}
