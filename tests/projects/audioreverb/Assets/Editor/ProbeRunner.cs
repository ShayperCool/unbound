using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ProbeRunner
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static Type T(string name) => typeof(Editor).Assembly.GetType("UnityEditor.Audio." + name, true);
    static object Call(object o, string method, params object[] args) => o.GetType().GetMethod(method, F).Invoke(o, args);
    static object Get(object o, string property) => o.GetType().GetProperty(property, F).GetValue(o);

    // One effect-free mixer: its Master sink observes the source chain (filters included) as floats.
    public static void Record()
    {
        Directory.CreateDirectory("Assets/Resources");
        var mixer = T("AudioMixerController").GetMethod("CreateMixerControllerAtPath", F).Invoke(null, new object[] { "Assets/Resources/Probe.mixer" });
        var serialized = new SerializedObject((UnityEngine.Object)mixer);
        var suspend = serialized.FindProperty("m_EnableSuspend");
        if (suspend == null) throw new Exception("Missing mixer suspend setting");
        suspend.boolValue = false; serialized.ApplyModifiedPropertiesWithoutUndo();
        var child = Call(mixer, "CreateNewGroup", "Child", true); Call(mixer, "AddChildToParent", child, Get(mixer, "masterGroup"));
        EditorUtility.SetDirty((UnityEngine.Object)mixer);
        // Serialized components: the engine reads them back from the prefab the Editor wrote.
        var prefab = new GameObject("Serialized");
        prefab.AddComponent<AudioSource>();
        prefab.AddComponent<AudioReverbFilter>().reverbPreset = AudioReverbPreset.Cave;
        var user = new GameObject("User"); user.transform.SetParent(prefab.transform);
        user.AddComponent<AudioSource>().reverbZoneMix = 0.75f;
        var filter = user.AddComponent<AudioReverbFilter>();
        filter.dryLevel = -300; filter.room = -1000; filter.roomHF = -300; filter.roomLF = -200; filter.decayTime = 1.49f;
        filter.decayHFRatio = 0.59f; filter.reflectionsLevel = -1219; filter.reflectionsDelay = 0.007f; filter.reverbLevel = 441;
        filter.reverbDelay = 0.011f; filter.hfReference = 4000; filter.lfReference = 150; filter.diffusion = 80; filter.density = 70;
        var zone = prefab.AddComponent<AudioReverbZone>();
        zone.minDistance = 3; zone.maxDistance = 7; zone.reverbPreset = AudioReverbPreset.Hangar; zone.room = -700;
        zone.reflectionsDelay = 0.05f; zone.HFReference = 3000; zone.diffusion = 60;
        var userZone = user.AddComponent<AudioReverbZone>();
        userZone.reverbPreset = AudioReverbPreset.User; userZone.roomLF = -300; userZone.LFReference = 100;
        PrefabUtility.SaveAsPrefabAsset(prefab, "Assets/Resources/Serialized.prefab");
        UnityEngine.Object.DestroyImmediate(prefab);
        AssetDatabase.SaveAssets();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("AudioReverbProbe").AddComponent<AudioReverbProbe>(); Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        EditorApplication.EnterPlaymode();
    }
}
