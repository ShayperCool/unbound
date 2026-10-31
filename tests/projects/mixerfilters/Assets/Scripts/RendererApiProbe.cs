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
            string text = value is bool b ? (b ? "true" : "false") : value is int i ? i.ToString() : "\"" + value + "\"";
            records.Add("\"" + key + "\":" + text);
        }
        Value("stop.before", () => AudioRenderer.Stop());
        Value("count.before", () => AudioRenderer.GetSampleCountForCaptureFrame());
        Value("start.first", () => AudioRenderer.Start());
        Value("start.second", () => AudioRenderer.Start());
        Value("count.started", () => AudioRenderer.GetSampleCountForCaptureFrame());
        foreach (int length in new[] { 0, 2, 2046, 2048, 2050, 4096 })
        {
            using (var buffer = new NativeArray<float>(length, Allocator.Temp))
            {
                for (int i = 0; i < buffer.Length; i++) buffer[i] = 123;
                Value("render." + length, () => AudioRenderer.Render(buffer));
                using (var writer = new BinaryWriter(File.Create(Path.Combine(output, "render_" + length + ".f32"))))
                    for (int i = 0; i < buffer.Length; i++) writer.Write(buffer[i]);
                records.Add("\"buffer." + length + "\":{\"file\":\"render_" + length + ".f32\",\"length\":" + length + "}");
            }
        }
        Value("stop.first", () => AudioRenderer.Stop());
        Value("stop.second", () => AudioRenderer.Stop());
        File.WriteAllText(Path.Combine(output, "capture.json"), "{" + string.Join(",", records) + "}\n");
    }
}
