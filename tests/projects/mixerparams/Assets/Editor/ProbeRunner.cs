using System;
using System.Reflection;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ProbeRunner
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static Type T(string name) => typeof(Editor).Assembly.GetType("UnityEditor.Audio." + name, true);
    static object Call(object o, string method, params object[] args) => o.GetType().GetMethod(method, F).Invoke(o, args);
    static object Get(object o, string property) => o.GetType().GetProperty(property, F).GetValue(o);
    static void Set(object o, string property, object value) => o.GetType().GetProperty(property, F).SetValue(o, value);

    public static void Record()
    {
        T("MixerEffectDefinitions").GetMethod("Refresh", F).Invoke(null, null);
        Directory.CreateDirectory("Assets/Resources");
        foreach (string key in ParamsProbe.Cases)
        {
            bool lp = key.StartsWith("lp");
            var mixer = T("AudioMixerController").GetMethod("CreateMixerControllerAtPath", F).Invoke(null, new object[] { "Assets/Resources/" + key + ".mixer" });
            var serialized = new SerializedObject((UnityEngine.Object)mixer);
            serialized.FindProperty("m_EnableSuspend").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var group = Get(mixer, "masterGroup");
            var unit = Activator.CreateInstance(T("AudioMixerEffectController"), F, null, new object[] { lp ? "Lowpass" : "Distortion" }, null);
            Call(unit, "PreallocateGUIDs"); Call(mixer, "AddNewSubAsset", unit, true);
            var old = (Array)Get(group, "effects");
            var chain = Array.CreateInstance(T("AudioMixerEffectController"), old.Length + 1);
            Array.Copy(old, chain, old.Length); chain.SetValue(unit, old.Length); Set(group, "effects", chain);
            string parameter = lp ? "Cutoff freq" : "Level";
            var snapshot = Get(mixer, "TargetSnapshot"); ((UnityEngine.Object)snapshot).name = "Base";
            Call(unit, "SetValueForParameter", mixer, snapshot, parameter, lp ? 1000f : 0.5f);
            var type = T("ExposedAudioParameter");
            var item = Activator.CreateInstance(type);
            type.GetField("name", F).SetValue(item, "Value");
            type.GetField("guid", F).SetValue(item, Call(unit, "GetGUIDForParameter", parameter));
            var exposed = Array.CreateInstance(type, 1); exposed.SetValue(item, 0); Set(mixer, "exposedParameters", exposed);
            Call(mixer, "CloneNewSnapshotFromTarget", true);
            var other = Get(mixer, "TargetSnapshot"); ((UnityEngine.Object)other).name = "Other";
            Call(unit, "SetValueForParameter", mixer, other, parameter, lp ? 2000f : 0.25f);
            Set(mixer, "TargetSnapshot", snapshot);
            EditorUtility.SetDirty((UnityEngine.Object)mixer);
        }
        AssetDatabase.SaveAssets();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("ParamsProbe").AddComponent<ParamsProbe>(); Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        EditorApplication.EnterPlaymode();
    }
}
