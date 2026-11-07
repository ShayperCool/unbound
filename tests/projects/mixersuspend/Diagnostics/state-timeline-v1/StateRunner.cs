using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class StateRunner
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static Type T(string name) => typeof(Editor).Assembly.GetType("UnityEditor.Audio." + name, true);
    static object Call(object o, string method, params object[] args) => o.GetType().GetMethod(method, F).Invoke(o, args);
    static object Get(object o, string name) => o.GetType().GetProperty(name, F).GetValue(o);
    static void Set(object o, string name, object value) => o.GetType().GetProperty(name, F).SetValue(o, value);
    public static void Prepare()
    {
        try { Create(); } catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/State.unity"); EditorApplication.EnterPlaymode();
    }
    static void Create()
    {
        T("MixerEffectDefinitions").GetMethod("Refresh", F).Invoke(null, null);
        Directory.CreateDirectory("Assets/Resources");
        foreach (string key in StateProbe.Cases) {
            var mixer = T("AudioMixerController").GetMethod("CreateMixerControllerAtPath", F).Invoke(null, new object[] { "Assets/Resources/" + key + ".mixer" });
            var serialized = new SerializedObject((UnityEngine.Object)mixer);
            serialized.FindProperty("m_EnableSuspend").boolValue = !key.EndsWith("disabled");
            serialized.FindProperty("m_SuspendThreshold").floatValue = 0;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var master = Get(mixer, "masterGroup");
            var child = Call(mixer, "CreateNewGroup", "Child", true); Call(mixer, "AddChildToParent", child, master);
            string kind = key.StartsWith("echo") ? "Echo" : key.StartsWith("lp") ? "Lowpass" : key.StartsWith("hp") ? "Highpass" : key.StartsWith("reverb") ? "SFX Reverb" : key.StartsWith("duck") ? "Duck Volume" : null;
            var snapshot = Get(mixer, "TargetSnapshot"); ((UnityEngine.Object)snapshot).name = "Base";
            if (kind != null) {
                var effect = Activator.CreateInstance(T("AudioMixerEffectController"), F, null, new object[] { kind }, null);
                Call(effect, "PreallocateGUIDs"); Call(mixer, "AddNewSubAsset", effect, true);
                var old = (Array)Get(child, "effects"); var next = Array.CreateInstance(T("AudioMixerEffectController"), old.Length + 1);
                Array.Copy(old, next, old.Length); next.SetValue(effect, old.Length); Set(child, "effects", next);
                void Param(string name, float value) => Call(effect, "SetValueForParameter", mixer, snapshot, name, value);
                if (kind == "Echo") { Param("Delay", 5000); Param("Decay", 0); Param("Drymix", 1); Param("Wetmix", 1); }
                if (kind == "Lowpass" || kind == "Highpass") Param("Cutoff freq", 10);
                if (kind == "SFX Reverb") { Param("Decay Time", 10); Param("Room", 0); Param("Reverb", 0); }
                if (kind == "Duck Volume") { Param("Sidechain Mix", 0); Param("Threshold", -40); Param("Ratio", 10); Param("Attack Time", 0); Param("Release Time", 10); Param("Knee", 0); }
            }
            var type = T("ExposedAudioParameter"); var exposed = Array.CreateInstance(type, 1);
            var item = Activator.CreateInstance(type); type.GetField("name", F).SetValue(item, "Gain");
            type.GetField("guid", F).SetValue(item, Call(child, "GetGUIDForVolume")); exposed.SetValue(item, 0); Set(mixer, "exposedParameters", exposed);
            if (key.StartsWith("snap")) {
                Call(mixer, "CloneNewSnapshotFromTarget", true); var other = Get(mixer, "TargetSnapshot"); ((UnityEngine.Object)other).name = "Other";
                Call(child, "SetValueForVolume", mixer, other, -12f);
            }
            EditorUtility.SetDirty((UnityEngine.Object)mixer);
        }
        AssetDatabase.SaveAssets(); EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("StateProbe").AddComponent<StateProbe>(); Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), "Assets/Scenes/State.unity");
        EditorApplication.Exit(0);
    }
}
