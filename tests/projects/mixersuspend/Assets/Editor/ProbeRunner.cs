using System;
using System.Reflection;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;

public static class ProbeRunner
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static Type T(string name) => typeof(Editor).Assembly.GetType("UnityEditor.Audio." + name, true);
    static object Call(object o, string method, params object[] args) => o.GetType().GetMethod(method, F).Invoke(o, args);
    static object Get(object o, string property) => o.GetType().GetProperty(property, F).GetValue(o);
    static void Set(object o, string property, object value) => o.GetType().GetProperty(property, F).SetValue(o, value);
    static object Effect(object mixer, object group, string name, float[] values, bool bypass = false)
    {
        var effect = Activator.CreateInstance(T("AudioMixerEffectController"), F, null, new object[] { name }, null);
        Call(effect, "PreallocateGUIDs"); Call(mixer, "AddNewSubAsset", effect, true);
        var old = (Array)Get(group, "effects");
        var next = Array.CreateInstance(T("AudioMixerEffectController"), old.Length+1);
        Array.Copy(old, next, old.Length); next.SetValue(effect, old.Length); Set(group, "effects", next);
        Set(effect, "bypass", bypass);
        if (values != null) for (int i = 0; i < values.Length; i++)
            Call(effect, "SetValueForParameter", mixer, Get(mixer, "TargetSnapshot"), new[]{"Delay","Decay","Max channels","Drymix","Wetmix"}[i], values[i]);
        if (name == "Lowpass") Call(effect, "SetValueForParameter", mixer, Get(mixer, "TargetSnapshot"), "Cutoff freq", 1000f);
        return effect;
    }
    public static void Record()
    {
        try { EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity"); EditorApplication.EnterPlaymode(); } catch(Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
    public static void Prepare()
    {
        try { Create(); } catch(Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
    static void Create()
    {
        T("MixerEffectDefinitions").GetMethod("Refresh", F).Invoke(null, null);
        Directory.CreateDirectory("Assets/Resources");
        foreach (string key in SuspendProbe.Cases) {
            var mixer = T("AudioMixerController").GetMethod("CreateMixerControllerAtPath", F).Invoke(null,new object[]{"Assets/Resources/"+key+".mixer"});
            var serialized = new SerializedObject((UnityEngine.Object)mixer);
            var suspend = serialized.FindProperty("m_EnableSuspend");
            var threshold = serialized.FindProperty("m_SuspendThreshold");
            if (suspend == null || threshold == null) throw new Exception("Missing suspension settings");
            Debug.Log(key+" defaults enable="+suspend.boolValue+" threshold="+threshold.floatValue);
            if (key != "default") suspend.boolValue = !key.EndsWith("disabled");
            threshold.floatValue=SuspendProbe.Threshold(key);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var master = Get(mixer,"masterGroup");
            var child = Call(mixer,"CreateNewGroup","Child",true); Call(mixer,"AddChildToParent",child,master);
            var sibling = Call(mixer,"CreateNewGroup","Sibling",true); Call(mixer,"AddChildToParent",sibling,master);
            var effect = Effect(mixer,key == "parent" ? master : child,"Echo",new float[]{SuspendProbe.Delay(key),key.StartsWith("threshold")||key.StartsWith("level")?1:0,0,1,1},key=="effect_bypassed");
            if(key.StartsWith("lp") || key.StartsWith("hp")) {
                var filter=Effect(mixer,child,key.StartsWith("lp")?"Lowpass":"Highpass",null);
                Call(filter,"SetValueForParameter",mixer,Get(mixer,"TargetSnapshot"),"Cutoff freq",10f);
            }
            if(key=="muted") Set(child,"mute",true);
            if(key=="bypassed") Set(child,"bypassEffects",true);
            var type=T("ExposedAudioParameter"); var exposed=Array.CreateInstance(type,2);
            for (int i=0;i<2;i++) {
                var item=Activator.CreateInstance(type);
                type.GetField("name",F).SetValue(item,i==0?"Gain":"Wet");
                type.GetField("guid",F).SetValue(item,i==0?Call(child,"GetGUIDForVolume"):Call(effect,"GetGUIDForParameter","Wetmix"));
                exposed.SetValue(item,i);
            }
            Set(mixer,"exposedParameters",exposed);
            if(key=="wake_snapshot") {
                Call(mixer,"CloneNewSnapshotFromTarget",true);
                var snapshot=Get(mixer,"TargetSnapshot"); ((UnityEngine.Object)snapshot).name="Other";
                Call(child,"SetValueForVolume",mixer,snapshot,-6f);
            }
            EditorUtility.SetDirty((UnityEngine.Object)mixer);
        }
        AssetDatabase.SaveAssets();
        if(Environment.GetEnvironmentVariable("SUSPEND_LIFECYCLE_PREPARE")=="1") LifecycleRunner.PrepareAsset();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        new GameObject("SuspendProbe").AddComponent<SuspendProbe>(); Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),"Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene("Assets/Scenes/Probe.unity",true)};
        EditorApplication.Exit(0);
    }
}
