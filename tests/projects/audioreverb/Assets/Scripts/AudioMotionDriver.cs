using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using Unity.Collections;

// Contrasting fixed and dynamic writes distinguish the audio velocity sample's phase.
public sealed class AudioMotionDriver : MonoBehaviour
{
    public Rigidbody body;
    public string phase;
    void FixedUpdate()
    {
        if (phase == "timing") body.linearVelocity = new Vector3(0, 0, 20);
        if (phase == "fixed") transform.position += new Vector3(0, 0, 20 * Time.fixedDeltaTime);
    }
    void Update()
    {
        if (phase == "timing") body.linearVelocity = new Vector3(0, 0, -7);
        if (phase == "dynamic") transform.position += new Vector3(0, 0, 20 * Time.deltaTime);
    }
}

public partial class AudioReverbProbe
{
    public static readonly string[] MotionCases = {
        "source_own_auto", "source_own_fixed", "source_own_dynamic",
        "source_parent_auto", "source_parent_fixed", "source_parent_dynamic",
        "listener_own_auto", "listener_own_fixed", "listener_own_dynamic",
        "listener_parent_auto", "listener_parent_fixed", "listener_parent_dynamic",
        "source_timing_auto", "source_timing_fixed", "source_timing_dynamic",
        "listener_timing_auto", "listener_timing_fixed", "listener_timing_dynamic",
        "source_transform_fixed_auto", "source_transform_fixed_fixed", "source_transform_fixed_dynamic",
        "source_transform_dynamic_auto", "source_transform_dynamic_fixed", "source_transform_dynamic_dynamic",
        "listener_transform_fixed_auto", "listener_transform_fixed_fixed", "listener_transform_fixed_dynamic",
        "listener_transform_dynamic_auto", "listener_transform_dynamic_fixed", "listener_transform_dynamic_dynamic",
        "source_parenttiming_auto", "source_parenttiming_fixed", "source_parenttiming_dynamic",
        "listener_parenttiming_auto", "listener_parenttiming_fixed", "listener_parenttiming_dynamic",
        "listener_offaxis_auto", "listener_offaxis_fixed",
    };

    System.Collections.IEnumerator CaptureMotion()
    {
        if (m_Captured) yield break;
        m_Captured = true;
        var config = AudioSettings.GetConfiguration();
        config.sampleRate = int.Parse(Environment.GetEnvironmentVariable("AUDIOREVERB_PROBE_RATE"));
        config.speakerMode = AudioSpeakerMode.Stereo; config.dspBufferSize = 1024;
        if (!AudioSettings.Reset(config)) throw new Exception("Motion reset failed");
        Time.captureDeltaTime = 1024f / config.sampleRate;
        Time.fixedDeltaTime = Time.captureDeltaTime;
        int rate = AudioSettings.outputSampleRate;
        string output = Environment.GetEnvironmentVariable("AUDIOREVERB_PROBE_OUT");
        Directory.CreateDirectory(output);
        var records = new List<string> { "\"release\":\"" + Application.unityVersion + "\"", "\"rate\":" + rate };
        records.Add("\"fixedDeltaTime\":" + BitConverter.SingleToInt32Bits(Time.fixedDeltaTime));
        var mixer = Resources.Load<AudioMixer>("Probe");
        var child = mixer.FindMatchingGroups("Child")[0];
        var master = Array.Find(mixer.FindMatchingGroups(""), g => g.name == "Master");
        var addSink = typeof(AudioRenderer).GetMethod("AddMixerGroupSink", Flags);
        if (!AudioRenderer.Start()) throw new Exception("Motion renderer start failed");
        foreach (string key in MotionCases) {
            Debug.Log("Audio motion case " + key);
            var sourceObject = new GameObject(key + ".source");
            var listenerObject = new GameObject(key + ".listener");
            var source = sourceObject.AddComponent<AudioSource>();
            var listener = listenerObject.AddComponent<AudioListener>();
            sourceObject.transform.position = new Vector3(0, 0, 100);
            if (key.Contains("offaxis")) sourceObject.transform.position = new Vector3(60, 0, 80);
            source.spatialBlend = 1; source.dopplerLevel = 1;
            source.rolloffMode = AudioRolloffMode.Custom;
            source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, AnimationCurve.Linear(0, 1, 1, 1));
            source.maxDistance = 10000;
            source.outputAudioMixerGroup = child;
            string[] parts = key.Split('_');
            var mode = parts[parts.Length - 1] == "auto" ? AudioVelocityUpdateMode.Auto
                : parts[parts.Length - 1] == "fixed" ? AudioVelocityUpdateMode.Fixed : AudioVelocityUpdateMode.Dynamic;
            source.velocityUpdateMode = mode; listener.velocityUpdateMode = mode;
            var moving = parts[0] == "source" ? sourceObject : listenerObject;
            GameObject parent = null;
            if (parts[1].StartsWith("parent")) {
                parent = new GameObject(key + ".body");
                moving.transform.SetParent(parent.transform, true);
            }
            if (parts[1] != "transform") {
                var body = (parent ? parent : moving).AddComponent<Rigidbody>();
                body.useGravity = false; body.linearDamping = 0;
                body.linearVelocity = new Vector3(0, 0, 20);
                if (parts[1].EndsWith("timing")) { var driver = moving.AddComponent<AudioMotionDriver>(); driver.body = body; driver.phase = "timing"; }
            } else {
                moving.AddComponent<AudioMotionDriver>().phase = parts[2];
            }
            // Let both loops sample motion before playback starts. No wall-clock delay is used.
            for (int warm = 0; warm < 8; warm++) yield return null;
            var clip = AudioClip.Create(key, 65536, 1, rate, false);
            var signal = new float[65536]; uint seed = 12345;
            for (int i = 0; i < signal.Length; i++) { seed = seed * 1664525u + 1013904223u; signal[i] = i < 1024 ? 0 : 0.25f * ((seed >> 8) / 8388608f - 1f); }
            clip.SetData(signal, 0); source.clip = clip; source.Play();
            using (var sink = new NativeArray<float>(2048, Allocator.Persistent))
            using (var buffer = new NativeArray<float>(2048, Allocator.Persistent))
            using (var masterWriter = new BinaryWriter(File.Create(Path.Combine(output, key + ".master.f32"))))
            using (var writer = new BinaryWriter(File.Create(Path.Combine(output, key + ".f32")))) {
                for (int block = 0; block < Blocks; block++) {
                    yield return null;
                    if (!(bool)addSink.Invoke(null, new object[] { master, sink, false })) throw new Exception("Motion sink failed");
                    if (!AudioRenderer.Render(buffer)) throw new Exception("Motion render failed");
                    for (int i = 0; i < buffer.Length; i++) { writer.Write(buffer[i]); masterWriter.Write(sink[i]); }
                }
            }
            foreach (string suffix in new[] { "", ".master" }) records.Add("\"" + key + suffix + "\":{\"file\":\"" + key + suffix + ".f32\",\"frames\":" + (Blocks * 1024) + ",\"channels\":2}");
            File.WriteAllText(Path.Combine(output, "capture.json"), "{" + string.Join(",", records) + "}\n");
            source.Stop(); DestroyImmediate(sourceObject); DestroyImmediate(listenerObject); DestroyImmediate(clip);
            if (parent) DestroyImmediate(parent);
        }
        AudioRenderer.Stop(); Exit(0);
    }
}
