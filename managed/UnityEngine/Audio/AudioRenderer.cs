using System;
using System.Collections.Generic;
using System.Text.Json;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.Audio;
using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>Offline rendering of the production audio graph, including raw group sinks and
    /// the device PCM conversion measured by the Native capture fixture.</summary>
    public unsafe partial class AudioRenderer
    {
        struct Sink { internal float* Pointer; internal int Length; internal bool Exclude; }
        static readonly Dictionary<ulong, Sink> s_Sinks = new Dictionary<ulong, Sink>();
        static bool s_Recording;
        public AudioRenderer() { }
        public static bool Start() => Internal_AudioRenderer_Start();
        public static bool Stop() => Internal_AudioRenderer_Stop();
        public static int GetSampleCountForCaptureFrame() => Internal_AudioRenderer_GetSampleCountForCaptureFrame();
        public static bool Render(NativeArray<float> buffer) => Internal_AudioRenderer_Render(buffer.GetUnsafePtr(), buffer.Length);
        internal static bool AddMixerGroupSink(AudioMixerGroup mixerGroup, NativeArray<float> buffer, bool excludeFromMix) =>
            Internal_AudioRenderer_AddMixerGroupSink(mixerGroup, buffer.GetUnsafePtr(), buffer.Length, excludeFromMix);

        internal static bool Internal_AudioRenderer_Start()
        {
            if (!Native.IsReady) throw new NotSupportedException("AudioRenderer requires the native audio backend");
            bool ok = JsonSerializer.Deserialize<bool>(Native.Call("audio-renderer-start", "{}"));
            if (ok) { s_Recording = true; s_Sinks.Clear(); }
            return ok;
        }
        internal static bool Internal_AudioRenderer_Stop()
        {
            if (!Native.IsReady) return false;
            bool ok = JsonSerializer.Deserialize<bool>(Native.Call("audio-renderer-stop", "{}"));
            if (ok) { s_Recording = false; s_Sinks.Clear(); }
            return ok;
        }
        internal static int Internal_AudioRenderer_GetSampleCountForCaptureFrame() =>
            Native.IsReady ? JsonSerializer.Deserialize<int>(Native.Call("audio-renderer-count", "{}")) : 0;

        internal static bool Internal_AudioRenderer_AddMixerGroupSink(AudioMixerGroup mixerGroup, void* ptr, int length, bool excludeFromMix)
        {
            if (!s_Recording || mixerGroup == null || ptr == null || length < 0) return false;
            s_Sinks[mixerGroup.Handle] = new Sink { Pointer = (float*)ptr, Length = length, Exclude = excludeFromMix };
            return true;
        }
        internal static bool Internal_AudioRenderer_Render(void* ptr, int length)
        {
            if (!s_Recording) return false;
            if (length < 0 || (ptr == null && length != 0)) return false;
            var sinks = new List<object>();
            foreach (var pair in s_Sinks)
                sinks.Add(new { handle = pair.Key, length = pair.Value.Length, excludeFromMix = pair.Value.Exclude });
            using var result = JsonDocument.Parse(Native.Call("audio-renderer-render", JsonSerializer.Serialize(new { length, sinks })));
            var root = result.RootElement;
            if (!root.GetProperty("ok").GetBoolean()) return false;
            int index = 0;
            foreach (var value in root.GetProperty("samples").EnumerateArray()) ((float*)ptr)[index++] = value.GetSingle();
            foreach (var output in root.GetProperty("sinks").EnumerateArray())
            {
                var sink = s_Sinks[output.GetProperty("handle").GetUInt64()];
                index = 0;
                foreach (var value in output.GetProperty("samples").EnumerateArray()) sink.Pointer[index++] = value.GetSingle();
            }
            s_Sinks.Clear(); // Native group sinks are registered separately for each render.
            return true;
        }
    }
}
