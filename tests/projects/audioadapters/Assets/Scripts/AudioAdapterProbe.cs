using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public class AudioAdapterProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod] static void Install() { new GameObject("AudioAdapterProbe").AddComponent<AudioAdapterProbe>(); }
    [Serializable] class Row { public string key; public string value; }
    StreamWriter output;
    void Put(string key, object value) => output.WriteLine(JsonUtility.ToJson(new Row { key = key, value = Convert.ToString(value, CultureInfo.InvariantCulture) }));
    void Info(string key, AudioClip clip)
    {
        Put(key+".name", clip.name); Put(key+".samples", clip.samples); Put(key+".channels", clip.channels); Put(key+".frequency", clip.frequency);
        Put(key+".loadState", clip.loadState); Put(key+".loadType", clip.loadType); Put(key+".preload", clip.preloadAudioData); Put(key+".background", clip.loadInBackground);
    }
    void Read(string key, AudioClip clip)
    {
        float[] data = { .875f, .875f, .875f, .875f };
        Put(key+".ok", clip.GetData(data, 0));
        for (int i=0;i<data.Length;i++) Put(key+"."+i, BitConverter.SingleToInt32Bits(data[i]));
    }
    void Start()
    {
        string path = Environment.GetEnvironmentVariable("AUDIO_ADAPTER_OUT"); if (string.IsNullOrEmpty(path)) return;
        try { using (output = new StreamWriter(path)) { Record(); Put("complete", 1); } EditorApplication.Exit(0); }
        catch (Exception ex) { Debug.LogException(ex); output?.Dispose(); EditorApplication.Exit(1); }
    }
    void FontControl()
    {
        var texture=new Texture2D(2,2);
        var material=new Material(Shader.Find("Sprites/Default")){name="adapterFontMaterial",color=Color.red}; material.mainTexture=texture;
        var source=new Font {name="adapterFont",material=material,characterInfo=new[]{new CharacterInfo{index=65,advance=7,minX=1,maxX=6,minY=-2,maxY=9}}};
        var copy=Object.Instantiate(source);
        Put("font.distinct",source.GetInstanceID()!=copy.GetInstanceID()); Put("font.name",copy.name);
        Put("font.materialShared",copy.material==material); Put("font.textureShared",copy.material.mainTexture==texture);
        Put("font.characterCount",copy.characterInfo.Length); Put("font.advance",copy.characterInfo[0].advance);
        var characters=copy.characterInfo;characters[0].advance=33;copy.characterInfo=characters;
        Put("font.sourceAfterCopyWrite",source.characterInfo[0].advance); Put("font.copyAfterWrite",copy.characterInfo[0].advance);
        material.color=Color.blue;Put("font.copySeesMaterialWrite",copy.material.color==Color.blue);
        Object.DestroyImmediate(source);Put("font.survives",copy!=null);Put("font.survivingAdvance",copy.characterInfo[0].advance);
        Put("font.survivingMaterial",copy.material==material); Object.DestroyImmediate(copy);Put("font.materialSurvives",material!=null);
        Object.DestroyImmediate(material);Object.DestroyImmediate(texture);
    }
    void Record()
    {
        foreach (int channels in new[] { 1, 2 })
        {
            string key = "runtime."+channels;
            var source = AudioClip.Create("runtime"+channels, 37, channels, 11025, false);
            var values = new float[37*channels]; for(int i=0;i<values.Length;i++) values[i] = .25f;
            Put(key+".write", source.SetData(values,0));
            var copy = Object.Instantiate(source);
            Info(key+".source", source); Info(key+".copy", copy);
            Put(key+".distinct", source.GetInstanceID()!=copy.GetInstanceID());
            Read(key+".sourceData",source); Read(key+".copyData",copy);
            Object.DestroyImmediate(source); Put(key+".survivor", copy!=null); Info(key+".afterDestroy",copy); Object.DestroyImmediate(copy);
        }
        foreach (string name in new[] { "Pcm", "Unloaded", "Streaming" })
        {
            string key = "imported."+name;
            var source = Resources.Load<AudioClip>(name); var copy = Object.Instantiate(source);
            Info(key+".source",source); Info(key+".copy",copy);
            Put(key+".distinct",source.GetInstanceID()!=copy.GetInstanceID());
            if(name=="Pcm")
            {
                Put(key+".set",copy.SetData(new[]{.125f,.25f,-.375f,.5f},0));
                Read(key+".sharedSource",source); Read(key+".sharedCopy",copy);
                Put(key+".unload",copy.UnloadAudioData()); Put(key+".sourceAfterUnload",source.loadState); Put(key+".copyAfterUnload",copy.loadState);
                Put(key+".load",copy.LoadAudioData()); Read(key+".reloadedSource",source); Read(key+".reloadedCopy",copy);
                Object.DestroyImmediate(source,true); Put(key+".sourceDestroyed",source==null); Read(key+".survivingCopy",copy);
                var reloaded=Resources.Load<AudioClip>(name); Put(key+".resourceReloadIsNull",reloaded==null); if(reloaded) { Info(key+".resourceReload",reloaded); Read(key+".resourceData",reloaded); }
            }
            else
            {
                Put(key+".copyLoad",copy.LoadAudioData()); Put(key+".sourceStateAfterCopyLoad",source.loadState); Put(key+".copyStateAfterLoad",copy.loadState);
            }
            Object.DestroyImmediate(copy);
        }
        FontControl();
    }
}
