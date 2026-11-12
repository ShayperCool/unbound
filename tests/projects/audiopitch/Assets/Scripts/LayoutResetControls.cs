using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Audio;
using Unity.Collections;

public partial class EffectProbe
{
    static int Channels(AudioSpeakerMode mode) => mode == AudioSpeakerMode.Mono ? 1
        : mode == AudioSpeakerMode.Quad ? 4 : mode == AudioSpeakerMode.Surround ? 5
        : mode == AudioSpeakerMode.Mode5point1 ? 6 : mode == AudioSpeakerMode.Mode7point1 ? 8 : 2;

    System.Collections.IEnumerator CaptureLayoutReset(string key, AudioSource source, AudioClip clip,
        AudioMixer mixer, AudioConfiguration config, string output, List<string> records,
        List<NativeArray<float>> sinks)
    {
        int callbacks = 0, callbackMode = -1;
        bool callbackDevice = true, callbackPlaying = true, callbackRead = true;
        int callbackClipBits = -1;
        AudioClip recovered = null;
        bool controlled = Option(key, "recovery_renderer=", "0") == "1";
        bool recover = Option(key, "recover=", "1") == "1";
        records.Add("\"" + key + ".recovery_lead_frames\":" + (Option(key, "scheduled=", "0") == "1" ? 8 * config.dspBufferSize : 0));
        void Changed(bool device)
        {
            callbacks++; callbackDevice = device; callbackPlaying = source.isPlaying;
            callbackMode = (int)AudioSettings.GetConfiguration().speakerMode;
            var lost = new float[2]; callbackRead = clip.GetData(lost, 2048);
            callbackClipBits = BitConverter.SingleToInt32Bits(lost[0]);
            if (controlled && !AudioRenderer.Start()) throw new Exception("callback renderer restart failed");
            if (!recover) return;
            recovered = AudioClip.Create(key + "_recovered", ClipFrames, 2, config.sampleRate, false);
            if (!recovered.SetData(Signal(Option(key, "music=", "dc:0.25"), config.sampleRate), 0))
                throw new Exception("recovered SetData failed");
            source.clip = recovered;
            if (Option(key, "scheduled=", "0") == "1") {
                double fraction = double.Parse(Option(key, "schedule_fraction=", "0"), System.Globalization.CultureInfo.InvariantCulture);
                double start = AudioSettings.dspTime + (8.0 * config.dspBufferSize + fraction) / config.sampleRate;
                string edit = Option(key, "schedule_edit=", "");
                source.PlayScheduled(edit == "start" ? start + 4.0 * config.dspBufferSize / config.sampleRate : start);
                if (edit == "start") source.SetScheduledStartTime(start);
                if (edit == "end") source.SetScheduledEndTime(start + 2048.0 / config.sampleRate);
            }
            else source.Play();
            if (controlled) {
                double before = AudioSettings.dspTime;
                int pause = int.Parse(Option(key, "recovery_pause_ms=", "0"));
                if (pause > 0) System.Threading.Thread.Sleep(pause);
                records.Add("\"" + key + ".callback_elapsed_frames\":" + (long)Math.Round((AudioSettings.dspTime - before) * config.sampleRate));
                records.Add("\"" + key + ".callback_position\":" + source.timeSamples);
            }
        }
        var master = Array.Find(mixer.FindMatchingGroups(""), g => g.name == "Master");
        var addSink = typeof(AudioRenderer).GetMethod("AddMixerGroupSink", Flags);
        for (int segment = 0; segment < 2; segment++) {
            if (segment == 1) {
                var before = new float[2];
                if (!clip.GetData(before, 2048)) throw new Exception("before reset GetData failed");
                records.Add("\"" + key + ".before_clip_bits\":" + BitConverter.SingleToInt32Bits(before[0]));
                records.Add("\"" + key + ".before_playing\":" + (source.isPlaying ? "true" : "false"));
                config.speakerMode = (AudioSpeakerMode)Enum.Parse(typeof(AudioSpeakerMode), Option(key, "reset_to=", "Quad"));
                AudioSettings.OnAudioConfigurationChanged += Changed;
                bool ok;
                try { ok = AudioSettings.Reset(config); }
                finally { AudioSettings.OnAudioConfigurationChanged -= Changed; }
                if (!ok) throw new Exception("live layout reset failed");
                records.Add("\"" + key + ".reset\":true");
                records.Add("\"" + key + ".callbacks\":" + callbacks);
                records.Add("\"" + key + ".callback_device\":" + (callbackDevice ? "true" : "false"));
                records.Add("\"" + key + ".callback_playing\":" + (callbackPlaying ? "true" : "false"));
                records.Add("\"" + key + ".callback_mode\":" + callbackMode);
                records.Add("\"" + key + ".callback_clip_read\":" + (callbackRead ? "true" : "false"));
                records.Add("\"" + key + ".callback_clip_bits\":" + callbackClipBits);
                records.Add("\"" + key + ".after_playing\":" + (source.isPlaying ? "true" : "false"));
                if (!controlled && !AudioRenderer.Start()) throw new Exception("live layout renderer restart failed");
                config = AudioSettings.GetConfiguration();
                records.Add("\"" + key + ".after_mode\":" + (int)config.speakerMode);
            }
            string name = key + (segment == 0 ? "" : ".after");
            int channels = Channels(config.speakerMode), blocks = segment == 0 ? 8 : 24;
            var buffer = new NativeArray<float>(config.dspBufferSize * channels, Allocator.Persistent);
            var sink = new NativeArray<float>(buffer.Length, Allocator.Persistent); sinks.Add(sink);
            using (var writer = new BinaryWriter(File.Create(Path.Combine(output, name + ".f32"))))
            using (var masterWriter = new BinaryWriter(File.Create(Path.Combine(output, name + ".master.f32")))) {
                if (Engine) FlushEngine();
                for (int block = 0; block < blocks; block++) {
                    if (!Engine) yield return null;
                    if (!(bool)addSink.Invoke(null, new object[] { master, sink, false })) throw new Exception("reset sink failed");
                    double beforeRender = controlled ? AudioSettings.dspTime : 0;
                    if (!AudioRenderer.Render(buffer)) throw new Exception("reset render failed");
                    if (controlled && segment == 1) {
                        records.Add("\"" + key + ".render_frames_" + block + "\":" + (long)Math.Round((AudioSettings.dspTime - beforeRender) * config.sampleRate));
                        records.Add("\"" + key + ".source_frames_" + block + "\":" + source.timeSamples);
                    }
                    for (int i = 0; i < buffer.Length; i++) { writer.Write(buffer[i]); masterWriter.Write(sink[i]); }
                }
            }
            foreach (string suffix in new[] { "", ".master" })
                records.Add("\"" + name + suffix + "\":{\"file\":\"" + name + suffix + ".f32\",\"frames\":"
                    + (blocks * config.dspBufferSize) + ",\"channels\":" + channels + "}");
            buffer.Dispose();
        }
        source.Stop();
        if (recovered) DestroyImmediate(recovered);
    }
}
