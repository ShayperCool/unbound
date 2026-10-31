using System;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using UnityEngine;

public static class RendererApiProbe
{
    public static void Record(string output)
    {
        Directory.CreateDirectory(output);
        var records = new List<string>();
        void Value(string key, Func<object> action)
        {
            object value;
            try { value = action(); }
            catch (Exception e) { value = e.GetType().Name; }
            string text = value is bool b ? (b ? "true" : "false") : value is int i ? i.ToString()
                : value is float f ? "\"" + f.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "\"" : "\"" + value + "\"";
            records.Add("\"" + key + "\":" + text);
        }
        float requested = Time.captureDeltaTime;
        foreach (int fps in new[] { 0, 24, 30, 47, 60, 120, 1000 })
        {
            Time.captureFramerate = fps;
            Value("time.fps." + fps + ".fps", () => Time.captureFramerate);
            Value("time.fps." + fps + ".delta", () => Time.captureDeltaTime);
        }
        float[] durations = { 0, 1024f/48000, 1024f/44100, 1f/60, 1f/59.94f, 1f/44.5f, 1f/45.5f, 0.1f };
        for (int i = 0; i < durations.Length; i++)
        {
            Time.captureDeltaTime = durations[i];
            Value("time.delta." + i + ".requested", () => durations[i]);
            Value("time.delta." + i + ".value", () => Time.captureDeltaTime);
            Value("time.delta." + i + ".fps", () => Time.captureFramerate);
        }
        Time.captureDeltaTime = requested;
        Value("stop.before", () => AudioRenderer.Stop());
        Value("count.before", () => AudioRenderer.GetSampleCountForCaptureFrame());
        Value("start.first", () => AudioRenderer.Start());
        Value("start.second", () => AudioRenderer.Start());
        Value("count.started", () => AudioRenderer.GetSampleCountForCaptureFrame());
        foreach (int length in new[] { 0, 1, 2, 2046, 2047, 2048, 2049, 2050, 4096 })
        {
            var buffer = new NativeArray<float>(length, Allocator.Temp);
            try
            {
                for (int i = 0; i < buffer.Length; i++) buffer[i] = 123;
                double before = AudioSettings.dspTime;
                Value("render." + length, () => AudioRenderer.Render(buffer));
                Value("advance." + length, () => (int)Math.Round((AudioSettings.dspTime-before)*AudioSettings.outputSampleRate));
                Value("count." + length, () => AudioRenderer.GetSampleCountForCaptureFrame());
                using (var writer = new BinaryWriter(File.Create(Path.Combine(output, "render_" + length + ".f32"))))
                    for (int i = 0; i < buffer.Length; i++) writer.Write(buffer[i]);
                records.Add("\"buffer." + length + "\":{\"file\":\"render_" + length + ".f32\",\"length\":" + length + "}");
            }
            finally { buffer.Dispose(); }
        }
        Value("stop.first", () => AudioRenderer.Stop());
        Value("stop.second", () => AudioRenderer.Stop());
        File.WriteAllText(Path.Combine(output, "capture.json"), "{" + string.Join(",", records) + "}\n");
    }
}
