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
    static readonly Dictionary<string, string> Units = new Dictionary<string, string> {
        { "Duck", "Duck Volume" }, { "Receive", "Receive" }, { "Lowpass", "Lowpass" } };
    public static void Record()
    {
        T("MixerEffectDefinitions").GetMethod("Refresh", F).Invoke(null, null);
        foreach (var effect in new[] { "Duck Volume", "Send", "Receive" })
            foreach (var definition in (Array)T("MixerEffectDefinitions").GetMethod("GetEffectParameters", F).Invoke(null, new object[] { effect }))
                Debug.Log(effect + " parameter " + definition.GetType().GetField("name").GetValue(definition)
                    + " min=" + definition.GetType().GetField("minRange").GetValue(definition)
                    + " max=" + definition.GetType().GetField("maxRange").GetValue(definition)
                    + " default=" + definition.GetType().GetField("defaultValue").GetValue(definition));
        Directory.CreateDirectory("Assets/Resources");
        foreach (string key in DuckProbe.Selected()) Build(key);
        AssetDatabase.SaveAssets();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("DuckProbe").AddComponent<DuckProbe>(); Directory.CreateDirectory("Assets/Scenes");
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
        foreach (var name in DuckProbe.Option(key, "order=", "Music,Voice,Fx").Split(',')) {
            if (DuckProbe.Chain(key, name) == null) continue;
            var group = Call(mixer, "CreateNewGroup", name, true); Call(mixer, "AddChildToParent", group, groups["Master"]);
            groups[name] = group;
        }
        // First create every unit in chain order, then resolve the send targets.
        var units = new Dictionary<string, object>();
        var sends = new List<(object, string)>();
        foreach (var pair in groups) {
            var group = pair.Value;
            var attenuation = ((Array)Get(group, "effects")).GetValue(0);
            var chain = new List<object>();
            foreach (var token in DuckProbe.Chain(key, pair.Key).Split(',')) {
                object unit;
                if (token == "Att") unit = attenuation;
                else {
                    string name = token.StartsWith("Send>") ? "Send" : Units[token];
                    unit = Activator.CreateInstance(T("AudioMixerEffectController"), F, null, new object[] { name }, null);
                    Call(unit, "PreallocateGUIDs"); Call(mixer, "AddNewSubAsset", unit, true);
                    if (name == "Send") sends.Add((unit, token.Substring(5)));
                }
                units[pair.Key + "." + (token.StartsWith("Send>") ? "Send" : token)] = unit;
                chain.Add(unit);
            }
            if (!chain.Contains(attenuation)) throw new Exception("Chain without Att: " + key);
            var array = Array.CreateInstance(T("AudioMixerEffectController"), chain.Count);
            for (int i = 0; i < chain.Count; i++) array.SetValue(chain[i], i);
            Set(group, "effects", array);
            // As in the SFX Reverb probe, bypass is set right after the chain: the setters crash
            // the 6000.0.71f1 batchmode Editor later in the setup, and a serialized write never
            // reaches the Editor's running mixer.
            foreach (var token in DuckProbe.Tokens(key)) {
                if (token == pair.Key + ".bypass=1") Set(group, "bypassEffects", true);
                foreach (var unit in units)
                    if (unit.Key.StartsWith(pair.Key + ".") && token == unit.Key + ".bypass=1") Set(unit.Value, "bypass", true);
            }
        }
        var overrides = new List<string> { "Voice.Volume=-80", "Voice.Send.Level=0" };
        overrides.AddRange(Array.FindAll(DuckProbe.Tokens(key), t => t.Contains(".") && t.Contains("=")));
        var applied = new Dictionary<string, string>();
        foreach (var token in overrides) { var pair = token.Split('='); applied[pair[0]] = pair[1]; }
        foreach (var pair in applied) {
            if (pair.Value == "") continue;  // keep the Editor default
            var path = pair.Key.Split('.');
            if (!groups.ContainsKey(path[0])) continue;
            float value = float.Parse(pair.Value, CultureInfo.InvariantCulture);
            if (path.Length == 2 && path[1] == "Volume") Call(groups[path[0]], "SetValueForVolume", mixer, snapshot, value);
            else if (path[path.Length - 1] == "bypass") continue;  // set with the chain
            else {
                // A default override names a unit that the case may leave out (nosend).
                if (!units.TryGetValue(path[0] + "." + path[1], out var unit)) {
                    if (Array.IndexOf(DuckProbe.Tokens(key), pair.Key + "=" + pair.Value) >= 0) throw new Exception("No unit " + pair.Key);
                    continue;
                }
                if (path[2] == "Level") Call(unit, "SetValueForMixLevel", mixer, snapshot, value);
                else Call(unit, "SetValueForParameter", mixer, snapshot, path[2], value);
            }
        }
        foreach (var (send, target) in sends) {
            object found = null;
            foreach (var unit in (Array)Get(groups[target], "effects")) {
                var name = (string)Get(unit, "effectName");
                if (found == null && (name == "Duck Volume" || name == "Receive")) found = unit;
            }
            if (found == null) throw new Exception("No send target in " + target + ": " + key);
            Set(send, "sendTarget", found);
        }
        string change = DuckProbe.Option(key, "change=", "");
        if (change != "") {
            var unit = units[change == "Send Level" ? "Voice.Send" : "Music.Duck"];
            ((UnityEngine.Object)snapshot).name = "Base";
            Call(mixer, "CloneNewSnapshotFromTarget", true);
            var other = Get(mixer, "TargetSnapshot"); ((UnityEngine.Object)other).name = "Other";
            float to = float.Parse(DuckProbe.Option(key, "to=", "0"), CultureInfo.InvariantCulture);
            if (change == "Send Level") Call(unit, "SetValueForMixLevel", mixer, other, to);
            else Call(unit, "SetValueForParameter", mixer, other, change, to);
            Set(mixer, "TargetSnapshot", snapshot);
            var type = T("ExposedAudioParameter"); var item = Activator.CreateInstance(type);
            type.GetField("name", F).SetValue(item, "Change");
            type.GetField("guid", F).SetValue(item, change == "Send Level" ? Call(unit, "GetGUIDForMixLevel") : Call(unit, "GetGUIDForParameter", change));
            var exposed = Array.CreateInstance(type, 1); exposed.SetValue(item, 0); Set(mixer, "exposedParameters", exposed);
        }
        EditorUtility.SetDirty((UnityEngine.Object)mixer);
    }
}
