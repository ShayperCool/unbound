using System;
using System.Collections.Generic;
using System.Globalization;
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
    static readonly Dictionary<string, string> Units = new Dictionary<string, string> { { "Shift", "Pitch Shifter" }, { "Chorus", "Chorus" }, { "Flange", "Flange" }, { "Reverb", "SFX Reverb" }, { "Comp", "Compressor" }, { "Duck", "Duck Volume" }, { "Send", "Send" } };
    public static void Record()
    {
        T("MixerEffectDefinitions").GetMethod("Refresh", F).Invoke(null, null);
        foreach (var effect in Units.Values)
            foreach (var definition in (Array)T("MixerEffectDefinitions").GetMethod("GetEffectParameters", F).Invoke(null, new object[] { effect }))
                Debug.Log(effect + " parameter " + definition.GetType().GetField("name").GetValue(definition)
                    + " min=" + definition.GetType().GetField("minRange").GetValue(definition)
                    + " max=" + definition.GetType().GetField("maxRange").GetValue(definition)
                    + " default=" + definition.GetType().GetField("defaultValue").GetValue(definition));
        Directory.CreateDirectory("Assets/Resources");
        foreach (string key in Array.ConvertAll(EffectProbe.Cases, row => row[0]))
            if (EffectProbe.Option(key, "asset=", key) == key && !key.StartsWith("device_")) Build(key);
        AssetDatabase.SaveAssets();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("EffectProbe").AddComponent<EffectProbe>(); Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        EditorApplication.EnterPlaymode();
    }

    static void Build(string key)
    {
        var mixer = T("AudioMixerController").GetMethod("CreateMixerControllerAtPath", F).Invoke(null, new object[] { "Assets/Resources/"+key+".mixer" });
        // Keep every mixer processing through silent stretches.
        var serialized = new SerializedObject((UnityEngine.Object)mixer);
        var suspend = serialized.FindProperty("m_EnableSuspend");
        if (suspend == null) throw new Exception("Missing mixer suspend setting");
        suspend.boolValue = false; serialized.ApplyModifiedPropertiesWithoutUndo();
        var snapshot = Get(mixer, "TargetSnapshot");
        var groups = new Dictionary<string, object> { { "Master", Get(mixer, "masterGroup") } };
        var music = Call(mixer, "CreateNewGroup", "Music", true); Call(mixer, "AddChildToParent", music, groups["Master"]);
        groups["Music"] = music;
        var units = new Dictionary<string, object>();
        foreach (var pair in groups) {
            var group = pair.Value;
            var attenuation = ((Array)Get(group, "effects")).GetValue(0);
            var chain = new List<object>();
            foreach (var token in EffectProbe.Chain(key, pair.Key).Split(',')) {
                object unit;
                if (token == "Att") unit = attenuation;
                else {
                    unit = Activator.CreateInstance(T("AudioMixerEffectController"), F, null, new object[] { Units[token] }, null);
                    Call(unit, "PreallocateGUIDs"); Call(mixer, "AddNewSubAsset", unit, true);
                }
                units[pair.Key + "." + token] = unit;
                chain.Add(unit);
            }
            if (!chain.Contains(attenuation)) throw new Exception("Chain without Att: " + key);
            var array = Array.CreateInstance(T("AudioMixerEffectController"), chain.Count);
            for (int i = 0; i < chain.Count; i++) array.SetValue(chain[i], i);
            Set(group, "effects", array);
            // As in the SFX Reverb and Duck Volume probes, bypass is set right after the chain.
            foreach (var token in EffectProbe.Tokens(key)) {
                if (token == pair.Key + ".bypass=1") Set(group, "bypassEffects", true);
                foreach (var unit in units)
                    if (unit.Key.StartsWith(pair.Key + ".") && token == unit.Key + ".bypass=1") Set(unit.Value, "bypass", true);
            }
        }
        foreach (var token in Array.FindAll(EffectProbe.Tokens(key), t => t.Contains("=") && t.Split('=')[0].Contains("."))) {
            var pair = token.Split('=');
            var path = pair[0].Split('.');
            float value = float.Parse(pair[1], CultureInfo.InvariantCulture);
            if (path.Length == 2 && path[1] == "Volume") Call(groups[path[0]], "SetValueForVolume", mixer, snapshot, value);
            else if (path[path.Length - 1] == "bypass") continue;  // set with the chain
            else {
                if (!units.TryGetValue(path[0] + "." + path[1], out var unit)) throw new Exception("No unit " + pair[0]);
                if (path[2] == "Level") {
                    Call(unit, "SetValueForMixLevel", mixer, snapshot, value); continue;
                }
                if (path[2] == "Wet") {
                    Set(unit, "enableWetMix", true);
                    Call(unit, "SetValueForMixLevel", mixer, snapshot, value);
                    continue;
                }
                bool known = false;
                foreach (var definition in (Array)T("MixerEffectDefinitions").GetMethod("GetEffectParameters", F).Invoke(null, new object[] { Units[path[1]] }))
                    known |= (string)definition.GetType().GetField("name").GetValue(definition) == path[2];
                if (!known) throw new Exception("No parameter " + pair[0]);
                Call(unit, "SetValueForParameter", mixer, snapshot, path[2], value);
            }
        }
        if (units.TryGetValue("Music.Send", out var send)) Set(send, "sendTarget", units["Master.Duck"]);
        if (units.TryGetValue("Music.Shift", out var shift)) {
            var names = new[] { "Pitch", "FFT size", "Overlap", "Max channels" };
            var type = T("ExposedAudioParameter"); var exposed = Array.CreateInstance(type, names.Length);
            for (int i = 0; i < names.Length; i++) {
                var item = Activator.CreateInstance(type);
                type.GetField("name", F).SetValue(item, names[i]);
                type.GetField("guid", F).SetValue(item, Call(shift, "GetGUIDForParameter", names[i]));
                exposed.SetValue(item, i);
            }
            Set(mixer, "exposedParameters", exposed);
        }
        if (key.StartsWith("slot_wet_") && units.TryGetValue("Music.Shift", out var wetUnit)) {
            var type = T("ExposedAudioParameter"); var item = Activator.CreateInstance(type);
            type.GetField("name", F).SetValue(item, "Wet");
            type.GetField("guid", F).SetValue(item, Call(wetUnit, "GetGUIDForMixLevel"));
            var previous = (Array)Get(mixer, "exposedParameters");
            var exposed = Array.CreateInstance(type, previous.Length + 1);
            Array.Copy(previous, exposed, previous.Length); exposed.SetValue(item, previous.Length);
            Set(mixer, "exposedParameters", exposed);
        }
        EditorUtility.SetDirty((UnityEngine.Object)mixer);
    }
}
