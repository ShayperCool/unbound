using System;
using System.Reflection;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class LiveProbeRunner
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static Type T(string name) => typeof(Editor).Assembly.GetType("UnityEditor.Audio." + name, true);
    static object Call(object o, string method, params object[] args) => o.GetType().GetMethod(method, F).Invoke(o, args);
    static object Get(object o, string property) => o.GetType().GetProperty(property, F).GetValue(o);
    static void Set(object o, string property, object value) => o.GetType().GetProperty(property, F).SetValue(o, value);
    static void Parameter(object mixer, object unit, object snapshot, string name, float value)
    {
        if (name == "Wet") Call(unit, "SetValueForMixLevel", mixer, snapshot, value);
        else Call(unit, "SetValueForParameter", mixer, snapshot, name, value);
    }
    public static void Record()
    {
        T("MixerEffectDefinitions").GetMethod("Refresh", F).Invoke(null, null);
        Directory.CreateDirectory("Assets/Resources");
        foreach (var c in LiveEffectProbe.Cases) {
            var mixer = T("AudioMixerController").GetMethod("CreateMixerControllerAtPath", F).Invoke(null, new object[] { "Assets/Resources/"+c.Key+".mixer" });
            var serialized = new SerializedObject((UnityEngine.Object)mixer);
            serialized.FindProperty("m_EnableSuspend").boolValue = false; serialized.ApplyModifiedPropertiesWithoutUndo();
            var group = Get(mixer, "masterGroup");
            var snapshot = Get(mixer, "TargetSnapshot"); ((UnityEngine.Object)snapshot).name = "Base";
            if (!c.Component) {
                var unit = Activator.CreateInstance(T("AudioMixerEffectController"), F, null, new object[] { c.Kind }, null);
                Call(unit, "PreallocateGUIDs"); Call(mixer, "AddNewSubAsset", unit, true);
                var old = (Array)Get(group, "effects"); var chain = Array.CreateInstance(T("AudioMixerEffectController"), old.Length + 1);
                Array.Copy(old, chain, old.Length); chain.SetValue(unit, old.Length); Set(group, "effects", chain);
                if (c.Kind == "Chorus") {
                    Parameter(mixer, unit, snapshot, "Dry mix", .25f);
                    Parameter(mixer, unit, snapshot, "Wet mix tap 1", .75f);
                    Parameter(mixer, unit, snapshot, "Wet mix tap 2", .375f);
                    Parameter(mixer, unit, snapshot, "Delay", 17.5f);
                    Parameter(mixer, unit, snapshot, "Rate", 5);
                    Parameter(mixer, unit, snapshot, "Depth", .5f);
                }
                if (c.Parameter == "Wet" || (c.Mode == "setwet" || c.Mode.StartsWith("dry"))) Set(unit, "enableWetMix", true);
                if ((c.Mode == "setwet" || c.Mode.StartsWith("dry"))) Parameter(mixer, unit, snapshot, "Wet", -6);
                Parameter(mixer, unit, snapshot, c.Parameter, c.Initial);
                var type = T("ExposedAudioParameter"); var item = Activator.CreateInstance(type);
                type.GetField("name", F).SetValue(item, "Value");
                type.GetField("guid", F).SetValue(item, c.Parameter == "Wet" ? Call(unit, "GetGUIDForMixLevel") : Call(unit, "GetGUIDForParameter", c.Parameter));
                var exposed = Array.CreateInstance(type, c.Mode.StartsWith("dry") ? (c.Kind == "Chorus" ? 3 : 2) : 1); exposed.SetValue(item, 0);
                if (c.Mode.StartsWith("dry")) {
                    var wet = Activator.CreateInstance(type); type.GetField("name", F).SetValue(wet, "WetControl");
                    type.GetField("guid", F).SetValue(wet, Call(unit, "GetGUIDForMixLevel")); exposed.SetValue(wet, 1);
                    if (c.Kind == "Chorus") {
                        var delay = Activator.CreateInstance(type); type.GetField("name", F).SetValue(delay, "DelayControl");
                        type.GetField("guid", F).SetValue(delay, Call(unit, "GetGUIDForParameter", "Delay")); exposed.SetValue(delay, 2);
                    }
                } Set(mixer, "exposedParameters", exposed);
                Call(mixer, "CloneNewSnapshotFromTarget", true);
                var other = Get(mixer, "TargetSnapshot"); ((UnityEngine.Object)other).name = "Other";
                Parameter(mixer, unit, other, c.Parameter, c.Changed);
                Set(mixer, "TargetSnapshot", snapshot);
            }
            EditorUtility.SetDirty((UnityEngine.Object)mixer);
        }
        AssetDatabase.SaveAssets(); EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("LiveEffectProbe").AddComponent<LiveEffectProbe>(); Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), "Assets/Scenes/Live.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Live.unity", true) };
        EditorApplication.EnterPlaymode();
    }
}
