using System;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Unity.Collections;

public class StreamClockProbe : MonoBehaviour
{
    readonly List<string> rows = new List<string>();
    void State(string key, AudioSource source)
    {
        rows.Add("\"" + key + "\":{\"playing\":" + (source.isPlaying ? 1 : 0)
            + ",\"sample\":" + source.timeSamples + ",\"timeBits\":" + BitConverter.SingleToInt32Bits(source.time) + "}");
    }

    System.Collections.IEnumerator Start()
    {
        const int rate = 48000, frames = 1024, blocks = 48;
        var config = AudioSettings.GetConfiguration();
        config.sampleRate = rate; config.dspBufferSize = frames; config.speakerMode = AudioSpeakerMode.Stereo;
        if (!AudioSettings.Reset(config)) throw new Exception("Reset failed");
        Time.captureDeltaTime = (float)frames / rate;
        rows.Add("\"release\":\"" + Application.unityVersion + "\""); rows.Add("\"rate\":" + rate);
        gameObject.AddComponent<AudioListener>(); var source = gameObject.AddComponent<AudioSource>();
        var buffer = new NativeArray<float>(frames * 2, Allocator.Persistent);
        if (!AudioRenderer.Start()) throw new Exception("AudioRenderer.Start failed");
        string output = Environment.GetEnvironmentVariable("Q158_OUT"); Directory.CreateDirectory(output);
        foreach (string clipName in new[] { "decoded", "streamed", "decodedshort", "streamedshort" })
        foreach (bool loop in new[] { false, true })
        foreach (string timing in new[] { "play", "scheduled", "end" })
        {
            if (!AudioSettings.Reset(config)) throw new Exception("Case reset failed");
            if (!AudioRenderer.Start()) throw new Exception("Case renderer restart failed");
            string key = clipName + "." + (loop ? "loop" : "once") + "." + timing;
            source.clip = Resources.Load<AudioClip>(clipName); source.loop = loop; source.pitch = 1;
            rows.Add("\"" + key + ".clip\":{\"samples\":" + source.clip.samples + ",\"channels\":" + source.clip.channels + "}");
            // Keep requested times away from a sample boundary when converted to DSP frames.
            double start = (Math.Round(AudioSettings.dspTime * rate) + 4 * frames + 0.25) / rate;
            if (timing == "play") source.Play(); else source.PlayScheduled(start);
            if (timing == "end") source.SetScheduledEndTime(start + 9 * (double)frames / rate);
            State(key + ".requested", source);
            using (var writer = new BinaryWriter(File.Create(Path.Combine(output, key + ".f32"))))
            for (int i = 0; i < blocks; i++)
            {
                yield return null;
                // Allow background state publication to settle on both sides of each render.
                Thread.Sleep(100); State(key + ".before." + i, source);
                if (!AudioRenderer.Render(buffer)) throw new Exception("AudioRenderer.Render failed");
                Thread.Sleep(100); State(key + ".render." + i, source);
                foreach (float sample in buffer) writer.Write(sample);
                if (i == 10 || i == 40)
                {
                    double clock = AudioSettings.dspTime;
                    Thread.Sleep(50); State(key + ".wall." + i, source);
                    rows.Add("\"" + key + ".wallAdvancesDsp." + i + "\":" + (clock != AudioSettings.dspTime ? "true" : "false"));
                }
            }
            rows.Add("\"" + key + "\":{\"file\":\"" + key + ".f32\",\"frames\":" + blocks * frames + ",\"channels\":2}");
            source.Stop(); State(key + ".stopped", source);
        }
        AudioRenderer.Stop(); buffer.Dispose();
        File.WriteAllText(Path.Combine(output, "capture.json"), "{" + string.Join(",", rows) + "}\n");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit(0);
#endif
    }
}
