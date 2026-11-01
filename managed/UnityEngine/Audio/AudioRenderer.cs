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
        struct Sink
        {
            internal float* Pointer;
            internal int Length;
            internal bool Exclude;
            internal NativeArray<float> Buffer;
            internal bool HasSafety;
            internal float* WritablePointer() => HasSafety ? (float*)Buffer.GetUnsafePtr() : Pointer;
        }
        static readonly Dictionary<ulong, Sink> s_Sinks = new Dictionary<ulong, Sink>();
        static bool s_Recording;
        public AudioRenderer() { }
        public static bool Start() => Internal_AudioRenderer_Start();
        public static bool Stop() => Internal_AudioRenderer_Stop();
        public static int GetSampleCountForCaptureFrame() => Internal_AudioRenderer_GetSampleCountForCaptureFrame();
        public static bool Render(NativeArray<float> buffer)
        {
            AudioSystem.CheckMainThread("AudioRenderer.Render");
            var sinks = TakeSinks();
            return Render((float*)buffer.GetUnsafePtr(), buffer.Length, sinks, buffer, true);
        }
        internal static bool AddMixerGroupSink(AudioMixerGroup mixerGroup, NativeArray<float> buffer, bool excludeFromMix)
        {
            AudioSystem.CheckMainThread("AudioRenderer.AddMixerGroupSink");
            if (!s_Recording || mixerGroup == null) return false;
            var pointer = (float*)buffer.GetUnsafePtr();
            if (pointer == null) return false;
            s_Sinks[mixerGroup.Handle] = new Sink { Pointer = pointer, Length = buffer.Length,
                Exclude = excludeFromMix, Buffer = buffer, HasSafety = true };
            return true;
        }

        internal static bool Internal_AudioRenderer_Start()
        {
            AudioSystem.CheckMainThread("AudioRenderer.Start");
            AudioSystem.EnsureRegistered();
            if (!Native.IsReady) throw new NotSupportedException("AudioRenderer requires the native audio backend");
            bool ok = JsonSerializer.Deserialize<bool>(Native.Call("audio-renderer-start", "{}"));
            if (ok) { s_Recording = true; s_Sinks.Clear(); }
            return ok;
        }
        internal static bool Internal_AudioRenderer_Stop()
        {
            AudioSystem.CheckMainThread("AudioRenderer.Stop");
            if (!Native.IsReady) return false;
            bool ok = JsonSerializer.Deserialize<bool>(Native.Call("audio-renderer-stop", "{}"));
            if (ok) { s_Recording = false; s_Sinks.Clear(); }
            return ok;
        }
        static float CaptureDelta => Time.captureDeltaTime != 0f ? Time.captureDeltaTime : Time.unscaledDeltaTime;
        internal static int Internal_AudioRenderer_GetSampleCountForCaptureFrame()
        {
            AudioSystem.CheckMainThread("AudioRenderer.GetSampleCountForCaptureFrame");
            return Native.IsReady ? JsonSerializer.Deserialize<int>(Native.Call("audio-renderer-count", JsonSerializer.Serialize(CaptureDelta))) : 0;
        }

        internal static bool Internal_AudioRenderer_AddMixerGroupSink(AudioMixerGroup mixerGroup, void* ptr, int length, bool excludeFromMix)
        {
            AudioSystem.CheckMainThread("AudioRenderer.AddMixerGroupSink");
            if (!s_Recording || mixerGroup == null || ptr == null || length < 0) return false;
            s_Sinks[mixerGroup.Handle] = new Sink { Pointer = (float*)ptr, Length = length, Exclude = excludeFromMix };
            return true;
        }
        internal static bool Internal_AudioRenderer_Render(void* ptr, int length)
        {
            AudioSystem.CheckMainThread("AudioRenderer.Render");
            return Render((float*)ptr, length, TakeSinks(), default, false);
        }
        static Dictionary<ulong, Sink> TakeSinks()
        {
            var sinks = new Dictionary<ulong, Sink>(s_Sinks);
            // Registrations belong to one attempt, including failed renders and invalid buffers.
            s_Sinks.Clear();
            return sinks;
        }
        static bool Render(float* ptr, int length, Dictionary<ulong, Sink> registrations,
            NativeArray<float> buffer, bool hasSafety)
        {
            if (!s_Recording) return false;
            if (length < 0 || (ptr == null && length != 0)) return false;
            var sinks = new List<object>();
            foreach (var pair in registrations)
            {
                pair.Value.WritablePointer();
                sinks.Add(new { handle = pair.Key, length = pair.Value.Length, excludeFromMix = pair.Value.Exclude });
            }
            using var result = JsonDocument.Parse(Native.Call("audio-renderer-render", JsonSerializer.Serialize(new { length, sinks, deltaTime = CaptureDelta })));
            var root = result.RootElement;
            if (!root.GetProperty("ok").GetBoolean()) return false;
            // A managed audio callback may have disposed a borrowed array during the RPC.
            if (hasSafety) ptr = (float*)buffer.GetUnsafePtr();
            var samples = root.GetProperty("samples");
            CheckOutputLength(samples, length);
            foreach (var output in root.GetProperty("sinks").EnumerateArray())
            {
                var sink = registrations[output.GetProperty("handle").GetUInt64()];
                sink.WritablePointer();
                CheckOutputLength(output.GetProperty("samples"), sink.Length);
            }
            int index = 0;
            foreach (var value in samples.EnumerateArray()) ptr[index++] = value.GetSingle();
            foreach (var output in root.GetProperty("sinks").EnumerateArray())
            {
                var sink = registrations[output.GetProperty("handle").GetUInt64()];
                float* target = sink.WritablePointer();
                index = 0;
                foreach (var value in output.GetProperty("samples").EnumerateArray()) target[index++] = value.GetSingle();
            }
            return true;
        }
        static void CheckOutputLength(JsonElement samples, int length)
        {
            if (samples.GetArrayLength() > length)
                throw new InvalidOperationException("AudioRenderer returned more samples than the registered buffer can hold");
        }
    }
}
