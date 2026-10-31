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
    static object Effect(object mixer, object group, string name, float cutoff, float q, bool bypass = false)
    {
        var effect = Activator.CreateInstance(T("AudioMixerEffectController"), F, null, new object[] { name }, null);
        Call(effect, "PreallocateGUIDs");
        Call(mixer, "AddNewSubAsset", effect, true);
        var old = (Array)Get(group, "effects");
        var next = Array.CreateInstance(T("AudioMixerEffectController"), old.Length + 1);
        Array.Copy(old, next, old.Length); next.SetValue(effect, old.Length); Set(group, "effects", next);
        Set(effect, "bypass", bypass);
        var snapshot = Get(mixer, "TargetSnapshot");
        Call(effect, "SetValueForParameter", mixer, snapshot, "Cutoff freq", cutoff);
        Call(effect, "SetValueForParameter", mixer, snapshot, "Resonance", q);
        return effect;
    }
    public static void Record()
    {
        T("MixerEffectDefinitions").GetMethod("Refresh", F).Invoke(null, null);
        foreach (var name in new[] { "Lowpass", "Highpass" })
            foreach (var definition in (Array)T("MixerEffectDefinitions").GetMethod("GetEffectParameters", F).Invoke(null, new object[] { name }))
                Debug.Log(name + " parameter " + definition.GetType().GetField("name").GetValue(definition)
                    + " min=" + definition.GetType().GetField("minRange").GetValue(definition)
                    + " max=" + definition.GetType().GetField("maxRange").GetValue(definition)
                    + " default=" + definition.GetType().GetField("defaultValue").GetValue(definition));
        Directory.CreateDirectory("Assets/Resources");
        foreach (var key in FilterProbe.ActiveCases)
        {
            var path = "Assets/Resources/" + key + ".mixer";
            if (File.Exists(path)) continue;
            var mixer = T("AudioMixerController").GetMethod("CreateMixerControllerAtPath", F).Invoke(null, new object[] { path });
            var master = Get(mixer, "masterGroup");
            var child = Call(mixer, "CreateNewGroup", "Child", true);
            Call(mixer, "AddChildToParent", child, master);
            if (Environment.GetEnvironmentVariable("FILTER_PROBE_DIAGNOSTIC_INPUT") == "1")
            {
                var input = Call(mixer, "CreateNewGroup", "Input", true);
                Call(mixer, "AddChildToParent", input, child);
            }
            object effect = null;
            if (key != "dry" && key != "step_dry" && key != "stereo_dry")
            {
                bool high = key.StartsWith("hp") || key == "tone_hp";
                float cutoff = key == "lp80q1" ? 80 : key == "hp8000q1" ? 8000 : key == "lp20000q1" ? 20000 : 1000;
                float q = key.EndsWith("q3") ? 3 : key.EndsWith("q10") ? 10 : 1;
                effect = Effect(mixer, key == "parent_lp" ? master : child, high ? "Highpass" : "Lowpass", cutoff, q, key == "lp_bypass");
                if (key == "lp_hp" || key == "hp_lp") Effect(mixer, child, high ? "Lowpass" : "Highpass", 4000, 2);
                if (key == "group_bypass") Set(child, "bypassEffects", true);
            }
            if (key == "set_lp" || key == "snapshot_lp")
            {
                var type = T("ExposedAudioParameter");
                var item = Activator.CreateInstance(type);
                type.GetField("name", F).SetValue(item, "Cutoff");
                type.GetField("guid", F).SetValue(item, Call(effect, "GetGUIDForParameter", "Cutoff freq"));
                var exposed = Array.CreateInstance(type, 1); exposed.SetValue(item, 0);
                Set(mixer, "exposedParameters", exposed);
            }
            if (key == "snapshot_lp")
            {
                Call(mixer, "CloneNewSnapshotFromTarget", true);
                var snapshot = Get(mixer, "TargetSnapshot"); ((UnityEngine.Object)snapshot).name = "Other";
                Call(effect, "SetValueForParameter", mixer, snapshot, "Cutoff freq", 4000f);
            }
            if (key == "volume_lp") Call(child, "SetValueForVolume", mixer, Get(mixer, "TargetSnapshot"), -6f);
            EditorUtility.SetDirty((UnityEngine.Object)mixer);
        }
        AssetDatabase.SaveAssets();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("FilterProbe").AddComponent<FilterProbe>();
        Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        EditorApplication.EnterPlaymode();
    }
}
