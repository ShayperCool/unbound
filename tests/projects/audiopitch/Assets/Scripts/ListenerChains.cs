using System;
using System.Collections.Generic;
using UnityEngine;

public partial class EffectProbe
{
    sealed class ListenerChainControl : IDisposable
    {
        readonly List<Component> components = new List<Component>();
        readonly List<ProbeListenerAffine> scripts = new List<ProbeListenerAffine>();
        GameObject second;
        AudioListener primary, alternate;
        string mode;

        public ListenerChainControl(string key, GameObject owner)
        {
            mode = Option(key, "listener_pair=", "");
            primary = owner.GetComponent<AudioListener>();
            foreach (var unit in Option(key, "listener_chain=", "").Split(',')) Add(owner, unit);
            if (mode != "") {
                second = new GameObject("Alternate listener");
                alternate = second.AddComponent<AudioListener>();
                string chain = Option(key, "alternate_chain=", "");
                if (chain != "") foreach (var unit in chain.Split(',')) Add(second, unit);
                else {
                    var script = second.AddComponent<ProbeListenerAffine>();
                    script.Gain = -1; script.Offset = 0.03125f; scripts.Add(script);
                }
                alternate.enabled = mode == "both";
            }
        }

        void Add(GameObject owner, string unit)
        {
            Component component;
            switch (unit) {
                case "gain":
                case "gain2": {
                    var gain = owner.AddComponent<ProbeListenerAffine>();
                    gain.Gain = unit == "gain" ? 0.5f : -1f;
                    gain.Offset = unit == "gain" ? 0.0625f : 0.03125f;
                    scripts.Add(gain); component = gain; break;
                }
                case "low": {
                    var effect = owner.AddComponent<AudioLowPassFilter>();
                    effect.cutoffFrequency = 4000; effect.lowpassResonanceQ = 1.7f; component = effect; break;
                }
                case "high": {
                    var effect = owner.AddComponent<AudioHighPassFilter>();
                    effect.cutoffFrequency = 800; effect.highpassResonanceQ = 1.2f; component = effect; break;
                }
                case "dist": {
                    var effect = owner.AddComponent<AudioDistortionFilter>();
                    effect.distortionLevel = 0.6f; component = effect; break;
                }
                case "echo": {
                    var effect = owner.AddComponent<AudioEchoFilter>();
                    effect.delay = 11; effect.decayRatio = 0.4f; effect.dryMix = 0.3f; effect.wetMix = 0.7f;
                    component = effect; break;
                }
                case "chorus": {
                    var effect = owner.AddComponent<AudioChorusFilter>();
                    effect.dryMix = 0.2f; effect.wetMix1 = 0.2f; effect.wetMix2 = 0.5f; effect.wetMix3 = 0.9f;
                    effect.delay = 11; effect.rate = 3; effect.depth = 0.7f; component = effect; break;
                }
                case "reverb": {
                    var effect = owner.AddComponent<AudioReverbFilter>();
                    effect.reverbPreset = AudioReverbPreset.User;
                    effect.dryLevel = -600; effect.room = -500; effect.roomHF = -300;
                    effect.decayTime = 0.3f; effect.reflectionsLevel = -500; effect.reflectionsDelay = 0.007f;
                    effect.reverbLevel = -500; effect.reverbDelay = 0.011f; component = effect; break;
                }
                case "": return;
                default: throw new Exception("Unknown listener chain unit " + unit);
            }
            components.Add(component);
        }

        public bool Change(int block)
        {
            if (mode != "swap" || (block != 8 && block != 16)) return false;
            primary.enabled = block == 16; alternate.enabled = block == 8;
            return true;
        }

        public void Record(string key, List<string> records)
        {
            for (int i = 0; i < scripts.Count; i++)
                records.Add("\"" + key + ".chain_channels_" + i + "\":" + scripts[i].Channels);
        }

        public void Dispose()
        {
            primary.enabled = true;
            for (int i = components.Count - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(components[i]);
            if (second) UnityEngine.Object.DestroyImmediate(second);
        }
    }
}

/// <summary>An affine listener transform makes component order observable.</summary>
public class ProbeListenerAffine : MonoBehaviour
{
    public volatile float Gain, Offset;
    public volatile int Channels;
    void OnAudioFilterRead(float[] data, int channels)
    {
        Channels = channels;
        float gain = Gain, offset = Offset;
        for (int i = 0; i < data.Length; i++) data[i] = data[i] * gain + offset;
    }
}
