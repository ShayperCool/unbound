using System;
using System.Collections.Generic;
using UnityEngine;

public partial class EffectProbe
{
    sealed class LiveTopologyControl : IDisposable
    {
        readonly GameObject target;
        readonly AudioSource source;
        readonly AudioListener primary;
        readonly List<Component> units = new List<Component>();
        readonly string operation;
        AudioDistortionFilter identity;
        ProbeListenerAffine script;
        GameObject alternateObject;
        AudioListener alternate;

        public LiveTopologyControl(string key, AudioSource source, GameObject listener)
        {
            this.source = source;
            primary = listener.GetComponent<AudioListener>();
            target = Option(key, "topology_target=", "source") == "listener" ? listener : source.gameObject;
            operation = Option(key, "topology=", "steady");
            foreach (var kind in Option(key, "initial_chain=", "echo,chorus").Split(','))
                units.Add(AddConfiguredFilter(target, kind));
            if (operation == "identity") {
                identity = target.AddComponent<AudioDistortionFilter>(); identity.distortionLevel = 0;
            }
            if (operation == "script_enabled") {
                script = target.AddComponent<ProbeListenerAffine>(); script.Gain = 1; script.Offset = 0;
            }
            bool other = Option(key, "other_listener=", "0") == "1";
            if (operation == "owner" || other) {
                alternateObject = new GameObject("Plain alternate listener");
                alternate = alternateObject.AddComponent<AudioListener>();
                alternate.enabled = other;
                if (other) {
                    var echo = (AudioEchoFilter)AddConfiguredFilter(alternateObject, "echo");
                    echo.delay = 17; echo.decayRatio = 0.2f; echo.dryMix = 0.1f; echo.wetMix = 1;
                    if (operation == "members_other") target = alternateObject;
                }
            }
        }

        public bool Change(string key, int block, List<string> records)
        {
            if (operation == "steady" || operation == "identity" || (block != 8 && block != 16 && block != 24)) return false;
            switch (operation) {
                case "members": case "members_other": case "members_disabled":
                    if (block == 16) { UnityEngine.Object.DestroyImmediate(identity); identity = null; }
                    else {
                        identity = target.AddComponent<AudioDistortionFilter>();
                        identity.distortionLevel = 0;
                        if (operation == "members_disabled") identity.enabled = false;
                    }
                    break;
                case "scripts":
                    if (block == 16) { UnityEngine.Object.DestroyImmediate(script); script = null; }
                    else { script = target.AddComponent<ProbeListenerAffine>(); script.Gain = 1; script.Offset = 0; }
                    break;
                case "script_enabled":
                    script.enabled = block == 16;
                    records.Add("\"" + key + ".script_enabled_" + block + "\":" + (script.enabled ? "true" : "false"));
                    break;
                case "enabled":
                    ((Behaviour)units[0]).enabled = block == 16;
                    break;
                case "owner_pulse":
                    primary.enabled = false; primary.enabled = true;
                    break;
                case "owner":
                    primary.enabled = block == 16;
                    alternate.enabled = block != 16;
                    break;
                default: throw new Exception("Unknown topology operation " + operation);
            }
            int count = 0;
            foreach (var component in target.GetComponents<Behaviour>())
                if (component is AudioLowPassFilter || component is AudioHighPassFilter || component is AudioDistortionFilter
                    || component is AudioEchoFilter || component is AudioChorusFilter || component is AudioReverbFilter) count++;
            records.Add("\"" + key + ".topology_update_" + block + "\":{\"filters\":" + count
                + ",\"first_enabled\":" + (((Behaviour)units[0]).enabled ? "true" : "false")
                + ",\"listener_enabled\":" + (primary.enabled ? "true" : "false")
                + ",\"source_playing\":" + (source.isPlaying ? "true" : "false")
                + ",\"source_frames\":" + source.timeSamples + "}");
            return true;
        }

        public void Dispose()
        {
            primary.enabled = true;
            if (alternateObject) UnityEngine.Object.DestroyImmediate(alternateObject);
            if (script) UnityEngine.Object.DestroyImmediate(script);
            if (identity) UnityEngine.Object.DestroyImmediate(identity);
            for (int i = units.Count - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(units[i]);
        }
    }
    static Component AddBareFilter(GameObject owner, Type kind)
    {
        if (kind == typeof(AudioLowPassFilter)) return owner.AddComponent<AudioLowPassFilter>();
        if (kind == typeof(AudioHighPassFilter)) return owner.AddComponent<AudioHighPassFilter>();
        if (kind == typeof(AudioDistortionFilter)) return owner.AddComponent<AudioDistortionFilter>();
        if (kind == typeof(AudioEchoFilter)) return owner.AddComponent<AudioEchoFilter>();
        if (kind == typeof(AudioChorusFilter)) return owner.AddComponent<AudioChorusFilter>();
        if (kind == typeof(AudioReverbFilter)) return owner.AddComponent<AudioReverbFilter>();
        throw new Exception("Unknown filter type " + kind.Name);
    }

    static void RecordFilterUniqueness(string key, List<string> records)
    {
        foreach (var kind in new[] { typeof(AudioLowPassFilter), typeof(AudioHighPassFilter),
            typeof(AudioDistortionFilter), typeof(AudioEchoFilter), typeof(AudioChorusFilter), typeof(AudioReverbFilter) })
        foreach (bool active in new[] { true, false }) {
            var owner = new GameObject("Duplicate filter control");
            owner.AddComponent<AudioSource>();
            var first = (Behaviour)AddBareFilter(owner, kind);
            first.enabled = active;
            int errors = 0;
            Application.LogCallback log = (message, trace, severity) => {
                if (severity == LogType.Error && message.Contains(kind.Name)) errors++;
            };
            Component second;
            Application.logMessageReceived += log;
            try { second = owner.AddComponent(kind); }
            finally { Application.logMessageReceived -= log; }
            int count = owner.GetComponents(kind).Length;
            bool retained = ReferenceEquals(owner.GetComponent(kind), first);
            UnityEngine.Object.DestroyImmediate(first);
            var replacement = AddBareFilter(owner, kind);
            records.Add("\"" + key + ".unique_" + kind.Name + "_" + (active ? "enabled" : "disabled")
                + "\":{\"second_null\":" + (second == null ? "true" : "false") + ",\"count\":" + count
                + ",\"retained\":" + (retained ? "true" : "false") + ",\"errors\":" + errors
                + ",\"replacement\":" + (replacement != null ? "true" : "false")
                + ",\"replacement_count\":" + owner.GetComponents(kind).Length + "}");
            UnityEngine.Object.DestroyImmediate(owner);
        }
    }

}
