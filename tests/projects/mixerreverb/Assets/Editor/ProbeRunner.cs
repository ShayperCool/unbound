using System;
using System.Globalization;
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
    static object Effect(object mixer, object group, string name, string[] overrides, bool bypass = false)
    {
        var effect = Activator.CreateInstance(T("AudioMixerEffectController"), F, null, new object[] { name }, null);
        Call(effect, "PreallocateGUIDs"); Call(mixer, "AddNewSubAsset", effect, true);
        var old = (Array)Get(group, "effects");
        var next = Array.CreateInstance(T("AudioMixerEffectController"), old.Length+1);
        Array.Copy(old, next, old.Length); next.SetValue(effect, old.Length); Set(group, "effects", next);
        Set(effect, "bypass", bypass);
        var snapshot = Get(mixer, "TargetSnapshot");
        if (overrides != null) for (int i = 1; i < overrides.Length; i++) {
            var pair = overrides[i].Split('=');
            float value = float.Parse(pair[1], CultureInfo.InvariantCulture);
            if (pair[0] == "Wet") {
                Set(effect, "enableWetMix", true);
                Call(effect, "SetValueForMixLevel", mixer, snapshot, value);
                Debug.Log("Wet level " + Call(effect, "GetValueForMixLevel", mixer, snapshot));
            }
            else Call(effect, "SetValueForParameter", mixer, snapshot, pair[0], value);
        }
        if (name == "Lowpass") Call(effect, "SetValueForParameter", mixer, snapshot, "Cutoff freq", 1000f);
        return effect;
    }
    public static void Record()
    {
        T("MixerEffectDefinitions").GetMethod("Refresh", F).Invoke(null, null);
        foreach (var definition in (Array)T("MixerEffectDefinitions").GetMethod("GetEffectParameters", F).Invoke(null, new object[] { "SFX Reverb" }))
            Debug.Log("SFX Reverb parameter " + definition.GetType().GetField("name").GetValue(definition)
                + " min=" + definition.GetType().GetField("minRange").GetValue(definition)
                + " max=" + definition.GetType().GetField("maxRange").GetValue(definition)
                + " default=" + definition.GetType().GetField("defaultValue").GetValue(definition));
        Directory.CreateDirectory("Assets/Resources");
        foreach (string key in Array.ConvertAll(ReverbProbe.Cases, row => row[0])) {
            string path = "Assets/Resources/"+key+".mixer";
            var mixer = T("AudioMixerController").GetMethod("CreateMixerControllerAtPath", F).Invoke(null, new object[] { path });
            // Keep the effect observable through quiet tails and after the source ends.
            var serialized = new SerializedObject((UnityEngine.Object)mixer);
            var suspend = serialized.FindProperty("m_EnableSuspend");
            if (suspend == null) throw new Exception("Missing mixer suspend setting");
            suspend.boolValue = false; serialized.ApplyModifiedPropertiesWithoutUndo();
            var master = Get(mixer, "masterGroup");
            var child = Call(mixer, "CreateNewGroup", "Child", true); Call(mixer, "AddChildToParent", child, master);
            object effect = null;
            if (key != "dry_stereo")
                effect = Effect(mixer, key == "parent_reverb" ? master : child, "SFX Reverb", ReverbProbe.Overrides(key), key == "effect_bypass" || key == "bypass_startup_effect");
            if (key == "reverb_lp") Effect(mixer, child, "Lowpass", null);
            if (key == "group_bypass" || key == "bypass_startup_group") Set(child, "bypassEffects", true);
            if (key == "volume_reverb") Call(child, "SetValueForVolume", mixer, Get(mixer, "TargetSnapshot"), -6f);
            if (key == "set_early") {
                var type = T("ExposedAudioParameter"); var exposed = Array.CreateInstance(type, ReverbProbe.Exposed.Length);
                for (int p = 0; p < ReverbProbe.Exposed.Length; p++) {
                    var item = Activator.CreateInstance(type);
                    type.GetField("name", F).SetValue(item, ReverbProbe.Exposed[p]);
                    type.GetField("guid", F).SetValue(item, Call(effect, "GetGUIDForParameter", ReverbProbe.Exposed[p]));
                    exposed.SetValue(item, p);
                }
                Set(mixer, "exposedParameters", exposed);
            }
            EditorUtility.SetDirty((UnityEngine.Object)mixer);
        }
        AssetDatabase.SaveAssets();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("ReverbProbe").AddComponent<ReverbProbe>(); Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        EditorApplication.EnterPlaymode();
    }
}
