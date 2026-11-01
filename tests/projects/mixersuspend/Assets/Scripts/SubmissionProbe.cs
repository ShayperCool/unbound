using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using Unity.Collections;

public class SubmissionProbe : MonoBehaviour {
    public static readonly string[] Cases = { "replay", "stop_play", "two_play", "disable_auto", "disable_play", "fresh", "reassign", "restart_active", "pause_unpause", "direct_replay" };
    public const int Blocks=128;
    const BindingFlags F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    static bool Engine=>Environment.GetEnvironmentVariable("SUSPEND_SUBMISSION_ENGINE")=="1";
    BinaryWriter listener;
    List<string> callbacks;
    string stage;
    public void Listen(float[] data,int channels) {
        if(listener==null)return;
        callbacks.Add("{\"stage\":\""+stage+"\",\"frames\":"+(data.Length/channels)+",\"channels\":"+channels+"}");
        foreach(float x in data)listener.Write(x);
    }
    System.Collections.IEnumerator Start() {
        var run=Capture();
        for(;;){object next;try{if(!run.MoveNext())yield break;next=run.Current;}catch(Exception e){Debug.LogException(e);Exit(1);yield break;}yield return next;}
    }
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
        gameObject.AddComponent<AudioListener>();gameObject.AddComponent<SubmissionListener>().Probe=this;
        if(!AudioRenderer.Start())throw new Exception("Start failed");
        var arrays=new List<NativeArray<float>>();var records=new List<string>{"\"release\":\""+Application.unityVersion+"\"","\"rate\":"+AudioSettings.outputSampleRate};
        foreach(string key in Cases){
            Debug.Log("Submission case "+key);
            var mixer=Resources.Load<AudioMixer>("disabled");if(!mixer.SetFloat("Wet",0))throw new Exception("SetFloat failed");
            var groups=mixer.FindMatchingGroups("");var child=Array.Find(groups,g=>g.name=="Child");var master=Array.Find(groups,g=>g.name=="Master");
            var go=new GameObject(key);var source=go.AddComponent<AudioSource>();var clip=AudioClip.Create(key,8192,2,config.sampleRate,false);var signal=new float[16384];signal[4096]=0.25f;signal[6145]=-0.125f;
            clip.SetData(signal,0);source.clip=clip;if(key!="direct_replay")source.outputAudioMixerGroup=child;source.loop=key=="restart_active"||key=="pause_unpause";source.Play();
            var sink=new NativeArray<float>(2048,Allocator.Persistent);arrays.Add(sink);var buffer=new NativeArray<float>(2048,Allocator.Persistent);arrays.Add(buffer);
            var addSink=typeof(AudioRenderer).GetMethod("AddMixerGroupSink",F);var events=new List<string>();
            void State(string name)=>events.Add("{\"stage\":\""+name+"\",\"playing\":"+(source.isPlaying?"true":"false")+",\"sample\":"+source.timeSamples+"}");
            using(var lw=new BinaryWriter(File.Create(Path.Combine(output,key+".listener.f32"))))
            using(var mw=new BinaryWriter(File.Create(Path.Combine(output,key+".master.f32"))))
            using(var pw=new BinaryWriter(File.Create(Path.Combine(output,key+".f32")))){
                listener=lw;callbacks=new List<string>();
                for(int block=0;block<Blocks;block++){
                    yield return null;
                    if(block==64){State("before_action");
                        if(key=="stop_play")source.Stop();
                        if(key.StartsWith("disable")){source.enabled=false;source.enabled=true;}
                        if(key=="fresh"){DestroyImmediate(source);source=go.AddComponent<AudioSource>();source.clip=clip;source.outputAudioMixerGroup=child;}
                        if(key=="reassign"){source.clip=null;source.clip=clip;}
                        if(key=="pause_unpause"){source.Pause();source.UnPause();}
                        else if(key!="disable_auto")source.Play();
                        if(key=="two_play")source.Play();State("after_action");
                    }
                    stage="render_"+block;
                    if(!(bool)addSink.Invoke(null,new object[]{master,sink,false}))throw new Exception("Sink failed");if(!AudioRenderer.Render(buffer))throw new Exception("Render failed");
                    for(int i=0;i<buffer.Length;i++){pw.Write(buffer[i]);mw.Write(sink[i]);}
                    if(block==64||block==65)State("after_render_"+block);
                }
                listener=null;records.Add("\""+key+".callbacks\":["+string.Join(",",callbacks)+"]");
            }
            records.Add("\""+key+".events\":["+string.Join(",",events)+"]");foreach(string suffix in new[]{"",".master",".listener"})records.Add("\""+key+suffix+"\":{\"file\":\""+key+suffix+".f32\",\"frames\":"+(new FileInfo(Path.Combine(output,key+suffix+".f32")).Length/8)+",\"channels\":2}");
            source.Stop();DestroyImmediate(go);DestroyImmediate(clip);File.WriteAllText(Path.Combine(output,"capture.json"),"{"+string.Join(",",records)+"}\n");
        }
        AudioRenderer.Stop();foreach(var array in arrays)array.Dispose();Exit(0);
    }
}
public class SubmissionListener : MonoBehaviour {public SubmissionProbe Probe;void OnAudioFilterRead(float[] data,int channels)=>Probe.Listen(data,channels);}
