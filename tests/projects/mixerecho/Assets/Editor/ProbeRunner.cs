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
            Call(effect, "SetValueForParameter", mixer, Get(mixer, "TargetSnapshot"), EchoProbe.Parameters[i], values[i]);
        if (name == "Lowpass") Call(effect, "SetValueForParameter", mixer, Get(mixer, "TargetSnapshot"), "Cutoff freq", 1000f);
        return effect;
    }
    public static void Record()
    {
        T("MixerEffectDefinitions").GetMethod("Refresh", F).Invoke(null, null);
        foreach (var definition in (Array)T("MixerEffectDefinitions").GetMethod("GetEffectParameters", F).Invoke(null, new object[] { "Echo" }))
            Debug.Log("Echo parameter " + definition.GetType().GetField("name").GetValue(definition)
                + " min=" + definition.GetType().GetField("minRange").GetValue(definition)
                + " max=" + definition.GetType().GetField("maxRange").GetValue(definition)
                + " default=" + definition.GetType().GetField("defaultValue").GetValue(definition));
        Directory.CreateDirectory("Assets/Resources");
        foreach (string key in EchoProbe.Cases) {
            string path = "Assets/Resources/"+key+".mixer";
            var mixer = T("AudioMixerController").GetMethod("CreateMixerControllerAtPath", F).Invoke(null, new object[] { path });
            // Keep the effect observable through long silent gaps and after the source ends.
            var serialized = new SerializedObject((UnityEngine.Object)mixer);
            var suspend = serialized.FindProperty("m_EnableSuspend");
            if (suspend == null) throw new Exception("Missing mixer suspend setting");
            suspend.boolValue = false; serialized.ApplyModifiedPropertiesWithoutUndo();
            var master = Get(mixer, "masterGroup");
            var child = Call(mixer, "CreateNewGroup", "Child", true); Call(mixer, "AddChildToParent", child, master);
            if (key == "lp_echo") Effect(mixer, child, "Lowpass", null);
            object effect = null;
            if (!key.StartsWith("dry_")) effect = Effect(mixer, key == "parent_echo" ? master : child, "Echo", EchoProbe.Values(key), key == "effect_bypass");
            if (key == "echo_lp") Effect(mixer, child, "Lowpass", null);
            if (key == "group_bypass") Set(child, "bypassEffects", true);
            if (key == "volume_echo") Call(child, "SetValueForVolume", mixer, Get(mixer, "TargetSnapshot"), -6f);
            if (key == "set_early" || key == "snapshot_early" || key == "updated_control") {
                var type = T("ExposedAudioParameter"); var exposed = Array.CreateInstance(type, EchoProbe.Parameters.Length);
                for (int p = 0; p < EchoProbe.Parameters.Length; p++) {
                    var item = Activator.CreateInstance(type);
                    type.GetField("name", F).SetValue(item, EchoProbe.Parameters[p]);
                    type.GetField("guid", F).SetValue(item, Call(effect, "GetGUIDForParameter", EchoProbe.Parameters[p]));
                    exposed.SetValue(item, p);
                }
                Set(mixer, "exposedParameters", exposed);
            }
            if (key == "snapshot_early") {
                Call(mixer, "CloneNewSnapshotFromTarget", true);
                var snapshot = Get(mixer, "TargetSnapshot"); ((UnityEngine.Object)snapshot).name = "Other";
                for (int p = 0; p < EchoProbe.Parameters.Length; p++) Call(effect, "SetValueForParameter", mixer, snapshot, EchoProbe.Parameters[p], EchoProbe.Updated[p]);
            }
            EditorUtility.SetDirty((UnityEngine.Object)mixer);
        }
        AssetDatabase.SaveAssets();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("EchoProbe").AddComponent<EchoProbe>(); Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        EditorApplication.EnterPlaymode();
    }
}
