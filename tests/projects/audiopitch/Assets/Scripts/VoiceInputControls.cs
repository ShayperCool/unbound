using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public partial class EffectProbe
{
    sealed class VoiceInputControl
    {
        const float DistortionLevel = 0.7f;
        readonly ProbeVoiceInputTap tap;
        readonly AudioSource source;
        readonly string live;

        public VoiceInputControl(string key, AudioSource source, int frames)
        {
            this.source = source;
            live = Option(key, "voice_live=", "");
            string stage = Option(key, "voice_effect=", "none");
            if (stage == "before") AddDistortion(source.gameObject);
            if (Option(key, "voice_tap=", "1") != "0") {
                tap = source.gameObject.AddComponent<ProbeVoiceInputTap>();
                // Profile clips have at most six channels; output devices have at most eight.
                tap.Buffer = new float[frames * 8];
            }
            if (stage == "after") AddDistortion(source.gameObject);
            source.volume = float.Parse(Option(key, "voice_volume=", "1"),
                System.Globalization.CultureInfo.InvariantCulture);
            source.pitch = float.Parse(Option(key, "voice_pitch=", "1"),
                System.Globalization.CultureInfo.InvariantCulture);
            if (tap) tap.Capture = true;
        }

        static void AddDistortion(GameObject owner)
        {
            var effect = (AudioDistortionFilter)AddConfiguredFilter(owner, "dist");
            // The reused full-wet mixer control uses the same midrange level.
            effect.distortionLevel = DistortionLevel;
        }

        public bool Change(int block)
        {
            if (live == "" || (block != 8 && block != 16 && block != 24)) return false;
            if (live == "pan") source.panStereo = block == 8 ? -0.65f : block == 16 ? 0.65f : 0;
            else source.volume = block == 8 ? 0.35f : block == 16 ? 0.7f : 1;
            return true;
        }

        public void Record(string key, string output, List<string> records)
        {
            if (!tap) return;
            tap.Capture = false;
            int count = tap.Samples, channels = tap.Channels;
            string file = key + ".source_float.f32";
            using (var writer = new BinaryWriter(File.Create(Path.Combine(output, file))))
                for (int i = 0; i < count; i++) writer.Write(tap.Buffer[i]);
            records.Add("\"" + key + ".source_channels\":" + channels);
            records.Add("\"" + key + ".source_callbacks\":" + tap.Calls);
            records.Add("\"" + key + ".source_width_changed\":" + (tap.WidthChanged ? "true" : "false"));
            records.Add("\"" + key + ".source_overflow\":" + (tap.Overflow ? "true" : "false"));
            records.Add("\"" + key + ".source_float\":{\"file\":\"" + file
                + "\",\"frames\":" + (channels == 0 ? 0 : count / channels)
                + ",\"channels\":" + channels + "}");
        }
    }
}

public sealed class ProbeVoiceInputTap : MonoBehaviour
{
    public float[] Buffer;
    public volatile bool Capture, WidthChanged, Overflow;
    public volatile int Samples, Channels, Calls;

    void OnAudioFilterRead(float[] data, int channels)
    {
        if (!Capture) return;
        if (Channels == 0) Channels = channels;
        else if (Channels != channels) WidthChanged = true;
        int count = Samples;
        if (Buffer == null || data.Length > Buffer.Length - count)
        {
            Overflow = true;
            return;
        }
        Array.Copy(data, 0, Buffer, count, data.Length);
        Samples = count + data.Length;
        Calls++;
    }
}
