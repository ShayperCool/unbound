using System;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Unity.Collections;
public class StreamProbe : MonoBehaviour {
 readonly List<string> rows=new List<string>();
 void State(string key,AudioSource source) {rows.Add("\""+key+"\":{\"playing\":"+(source.isPlaying?1:0)+",\"sample\":"+source.timeSamples+",\"timeBits\":"+BitConverter.ToInt32(BitConverter.GetBytes(source.time),0)+"}");}
 System.Collections.IEnumerator Start(){
  var cfg=AudioSettings.GetConfiguration();cfg.sampleRate=48000;cfg.dspBufferSize=1024;cfg.speakerMode=AudioSpeakerMode.Stereo;AudioSettings.Reset(cfg);Time.captureDeltaTime=1024f/48000;
  gameObject.AddComponent<AudioListener>();var source=gameObject.AddComponent<AudioSource>();
  var buffer=new NativeArray<float>(2048,Allocator.Persistent);AudioRenderer.Start();
  string output=Environment.GetEnvironmentVariable("Q158_OUT");Directory.CreateDirectory(output);
  foreach(string clipName in new[]{"decoded","streamed"})foreach(bool loop in new[]{false,true})foreach(string timing in new[]{"play","scheduled","end"}){
   string key=clipName+"."+(loop?"loop":"once")+"."+timing;source.clip=Resources.Load<AudioClip>(clipName);source.loop=loop;source.pitch=1;
   Debug.Log("Stream clock "+key+" samples="+source.clip.samples+" frequency="+source.clip.frequency);
   double start=AudioSettings.dspTime+4*1024.0/48000;
   if(timing=="play")source.Play();else source.PlayScheduled(start);
   if(timing=="end")source.SetScheduledEndTime(start+9*1024.0/48000);
   State(key+".requested",source);
   using(var writer=new BinaryWriter(File.Create(Path.Combine(output,key+".f32"))))for(int i=0;i<48;i++){
    yield return null;State(key+".before."+i,source);AudioRenderer.Render(buffer);State(key+".render."+i,source);
    foreach(float sample in buffer)writer.Write(sample);
    if(i==40){Thread.Sleep(50);State(key+".wall."+i,source);}
   }
   source.Stop();State(key+".stopped",source);
  }
  AudioRenderer.Stop();buffer.Dispose();File.WriteAllText(Path.Combine(output,"capture.json"),"{"+string.Join(",",rows)+"}");
#if UNITY_EDITOR
  UnityEditor.EditorApplication.Exit(0);
#else
  Application.Quit(0);
#endif
 }
}
