using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using UnityEngine;

namespace Unbound.Runtime
{
    /// <summary>The project's audio settings (ProjectSettings/AudioManager.asset).</summary>
    internal sealed class ProjectAudioSettings
    {
        public float Volume { get; set; } = 1f;
        public float RolloffScale { get; set; } = 1f;
        public float DopplerFactor { get; set; } = 1f;
        public int SpeakerMode { get; set; } = 2;
        public int SampleRate { get; set; }
        public int DspBufferSize { get; set; } = 1024;
        public int VirtualVoices { get; set; } = 512;
        public int RealVoices { get; set; } = 32;
        public bool DisableAudio { get; set; }
        public string SpatializerPlugin { get; set; } = "";
        public string AmbisonicDecoderPlugin { get; set; } = "";
    }

    /// <summary>
    /// Where the audio system's voices live. In a game it is the native mixer
    /// (<see cref="NativeAudioBackend"/>); without the native side (unit tests) a managed twin of its
    /// virtual device keeps the same play states and clock (<see cref="VirtualAudioBackend"/>).
    /// </summary>
    internal abstract class AudioBackend
    {
        static AudioBackend s_Current;
        static long s_NextHandle;

        internal static AudioBackend Current => s_Current ??= Native.IsReady ? new NativeAudioBackend() : new VirtualAudioBackend();

        /// <summary>Replaces the backend (tests).</summary>
        internal static void Use(AudioBackend backend) => s_Current = backend;

        internal static ulong NextHandle() => (ulong)Interlocked.Increment(ref s_NextHandle);

        internal abstract bool ImportClip(ulong handle, string guid, out AudioClipInfo info);
        internal abstract void CreateClip(ulong handle, int frames, int channels, int frequency, bool stream);
        internal abstract bool ClipData(ulong handle, int offsetFrames, Span<float> data, bool write);
        internal abstract void ReleaseClip(ulong handle);
        /// <summary>The packet's curve pointers are valid during the call only.</summary>
        internal abstract void UpdateSource(ulong handle, ref AudioSourcePacket packet);
        internal abstract void Command(ulong handle, in AudioSourceCommand command);
        internal abstract AudioSourceState State(ulong handle);
        internal abstract void ReleaseSource(ulong handle);
        internal abstract void UpdateGroup(ulong handle, float gain, float pitch);
        internal abstract AudioStatus Frame(in AudioFramePacket frame);
        internal abstract double DspTime { get; }
        /// <summary>window &lt; 0: samples; else the spectrum with that FFTWindow.</summary>
        internal abstract bool OutputData(ulong handle, int channel, Span<float> data, int window);
        internal abstract ProjectAudioSettings ProjectSettings { get; }
        internal abstract AudioConfiguration Configuration { get; }
        internal abstract bool Reset(AudioConfiguration configuration);
        internal abstract void InstallCallback();
    }

    internal sealed unsafe class NativeAudioBackend : AudioBackend
    {
        static readonly JsonSerializerOptions s_Json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        ProjectAudioSettings m_Project;

        sealed class SettingsJson
        {
            public ProjectAudioSettings Project { get; set; }
            public ConfigurationJson Configuration { get; set; }
            public bool Device { get; set; }
        }

        sealed class ConfigurationJson
        {
            public int SpeakerMode { get; set; }
            public int DspBufferSize { get; set; }
            public int SampleRate { get; set; }
            public int NumRealVoices { get; set; }
            public int NumVirtualVoices { get; set; }
        }

        SettingsJson Query() => JsonSerializer.Deserialize<SettingsJson>(Native.Call("audio-settings", "{}"), s_Json);

        internal override bool ImportClip(ulong handle, string guid, out AudioClipInfo info)
        {
            info = default;
            var bytes = Encoding.UTF8.GetBytes(guid);
            fixed (byte* p = bytes)
            fixed (AudioClipInfo* i = &info)
                return Native.Api.AudioImportClip(handle, p, bytes.Length, i) == 0;
        }

        internal override void CreateClip(ulong handle, int frames, int channels, int frequency, bool stream) =>
            Native.Api.AudioCreateClip(handle, frames, channels, frequency, stream ? 1 : 0);

        internal override bool ClipData(ulong handle, int offsetFrames, Span<float> data, bool write)
        {
            fixed (float* p = data)
                return Native.Api.AudioClipData(handle, offsetFrames, p, data.Length, write ? 1 : 0) != 0;
        }

        internal override void ReleaseClip(ulong handle)
        {
            if (handle != 0) Native.Api.AudioReleaseClip(handle);
        }

        internal override void UpdateSource(ulong handle, ref AudioSourcePacket packet)
        {
            fixed (AudioSourcePacket* p = &packet) Native.Api.AudioUpdateSource(handle, p);
        }

        internal override void Command(ulong handle, in AudioSourceCommand command)
        {
            var c = command;
            Native.Api.AudioSourceCommand(handle, &c);
        }

        internal override AudioSourceState State(ulong handle)
        {
            AudioSourceState state;
            Native.Api.AudioSourceState(handle, &state);
            return state;
        }

        internal override void ReleaseSource(ulong handle)
        {
            if (handle != 0) Native.Api.AudioReleaseSource(handle);
        }

        internal override void UpdateGroup(ulong handle, float gain, float pitch) => Native.Api.AudioUpdateGroup(handle, gain, pitch);

        internal override AudioStatus Frame(in AudioFramePacket frame)
        {
            var f = frame;
            AudioStatus status;
            Native.Api.AudioFrame(&f, &status);
            return status;
        }

        internal override double DspTime => Native.Api.AudioDspTime();

        internal override bool OutputData(ulong handle, int channel, Span<float> data, int window)
        {
            fixed (float* p = data)
                return Native.Api.AudioOutputData(handle, channel, p, data.Length, window) != 0;
        }

        internal override ProjectAudioSettings ProjectSettings => m_Project ??= Query().Project ?? new ProjectAudioSettings();

        internal override AudioConfiguration Configuration
        {
            get
            {
                var c = Query().Configuration;
                return new AudioConfiguration
                {
                    speakerMode = (AudioSpeakerMode)c.SpeakerMode,
                    dspBufferSize = c.DspBufferSize,
                    sampleRate = c.SampleRate,
                    numRealVoices = c.NumRealVoices,
                    numVirtualVoices = c.NumVirtualVoices,
                };
            }
        }

        internal override bool Reset(AudioConfiguration configuration)
        {
            var request = JsonSerializer.Serialize(new ConfigurationJson
            {
                SpeakerMode = (int)configuration.speakerMode,
                DspBufferSize = configuration.dspBufferSize,
                SampleRate = configuration.sampleRate,
                NumRealVoices = configuration.numRealVoices,
                NumVirtualVoices = configuration.numVirtualVoices,
            }, s_Json);
            using var doc = JsonDocument.Parse(Native.Call("audio-reset", request));
            return doc.RootElement.GetProperty("ok").GetBoolean();
        }

        internal override void InstallCallback() =>
            Native.Api.AudioSetCallback((delegate* unmanaged<int, ulong, float*, int, int, long, void>)&AudioFilters.Invoke);
    }

    /// <summary>
    /// The managed twin of the native mixer's virtual device, for tests: the same play states,
    /// positions, scheduling and clock in whole DSP buffers, without mixing samples (no
    /// spatialization, doppler or output data).
    /// </summary>
    internal sealed class VirtualAudioBackend : AudioBackend
    {
        sealed class Clip
        {
            internal int Channels, Frequency, Frames;
            internal float[] Samples;
            internal bool Stream;
        }

        sealed class Voice
        {
            internal ulong Clip;
            internal double Position;
            internal long StartAt;
            internal long EndAt = long.MaxValue;
            internal bool Paused;
        }

        sealed class Source
        {
            internal AudioSourcePacket Params;
            internal Voice Main;
            internal readonly List<Voice> OneShots = new List<Voice>();
            internal double Pending;
        }

        readonly Dictionary<ulong, Clip> m_Clips = new Dictionary<ulong, Clip>();
        readonly Dictionary<ulong, Source> m_Sources = new Dictionary<ulong, Source>();
        readonly Dictionary<ulong, (float Gain, float Pitch)> m_Groups = new Dictionary<ulong, (float, float)>();
        AudioConfiguration m_Configuration = new AudioConfiguration
        {
            speakerMode = AudioSpeakerMode.Stereo, dspBufferSize = 1024, sampleRate = 48000, numRealVoices = 32, numVirtualVoices = 512,
        };
        long m_Frames;
        double m_Owed;
        bool m_ListenerPaused;

        internal ProjectAudioSettings Project = new ProjectAudioSettings();
        /// <summary>Clips "imported" by GUID (tests register them here).</summary>
        internal readonly Dictionary<string, (AudioClipInfo Info, float[] Samples)> Assets = new Dictionary<string, (AudioClipInfo, float[])>();

        internal VirtualAudioBackend(int sampleRate = 48000, int bufferSize = 1024)
        {
            m_Configuration.sampleRate = sampleRate;
            m_Configuration.dspBufferSize = bufferSize;
        }

        long FrameOf(double seconds) => (long)Math.Round(Math.Max(0, seconds) * m_Configuration.sampleRate);

        internal override bool ImportClip(ulong handle, string guid, out AudioClipInfo info)
        {
            if (!Assets.TryGetValue(guid, out var asset))
            {
                info = default;
                return false;
            }
            info = asset.Info;
            m_Clips[handle] = new Clip { Channels = info.Channels, Frequency = info.Frequency, Frames = info.Frames, Samples = (float[])asset.Samples.Clone() };
            return true;
        }

        internal override void CreateClip(ulong handle, int frames, int channels, int frequency, bool stream) =>
            m_Clips[handle] = new Clip { Channels = channels, Frequency = frequency, Frames = frames, Samples = stream ? Array.Empty<float>() : new float[frames * channels], Stream = stream };

        internal override bool ClipData(ulong handle, int offsetFrames, Span<float> data, bool write)
        {
            if (!m_Clips.TryGetValue(handle, out var clip) || clip.Stream || clip.Samples.Length == 0) return false;
            int total = clip.Samples.Length;
            int start = offsetFrames % Math.Max(1, clip.Frames) * clip.Channels;
            for (int i = 0; i < data.Length; i++)
            {
                int at = (start + i) % total;
                if (write) clip.Samples[at] = data[i];
                else data[i] = clip.Samples[at];
            }
            return true;
        }

        internal override void ReleaseClip(ulong handle) => m_Clips.Remove(handle);

        Source SourceOf(ulong handle)
        {
            if (!m_Sources.TryGetValue(handle, out var source))
                m_Sources[handle] = source = new Source { Params = new AudioSourcePacket { Volume = 1, Pitch = 1, Priority = 128 } };
            return source;
        }

        internal override void UpdateSource(ulong handle, ref AudioSourcePacket packet)
        {
            var copy = packet;
            unsafe
            {
                copy.RolloffCurve = null;
                copy.SpatialBlendCurve = null;
                copy.SpreadCurve = null;
            }
            SourceOf(handle).Params = copy;
        }

        internal override void Command(ulong handle, in AudioSourceCommand command)
        {
            var source = SourceOf(handle);
            long now = m_Frames;
            long Start(double time) => time < 0 ? now + FrameOf(-time) : Math.Max(now, FrameOf(time));
            switch (command.Kind)
            {
                case AudioCommandKind.Play:
                {
                    if (source.Main != null && source.Main.Paused)
                    {
                        source.Main.Paused = false;
                        foreach (var v in source.OneShots) v.Paused = false;
                        return;
                    }
                    if (!m_Clips.TryGetValue(source.Params.Clip, out var clip))
                    {
                        source.Main = null;
                        return;
                    }
                    double position = source.Pending;
                    source.Pending = 0;
                    if (position == 0 && source.Params.Pitch < 0 && !clip.Stream) position = clip.Frames;
                    source.Main = new Voice { Clip = source.Params.Clip, Position = position, StartAt = Start(command.Time) };
                    break;
                }
                case AudioCommandKind.Stop:
                    source.Main = null;
                    source.OneShots.Clear();
                    source.Pending = 0;
                    break;
                case AudioCommandKind.StopMain:
                    source.Main = null;
                    source.Pending = 0;
                    break;
                case AudioCommandKind.Pause:
                    if (source.Main != null) source.Main.Paused = true;
                    foreach (var v in source.OneShots) v.Paused = true;
                    break;
                case AudioCommandKind.UnPause:
                    if (source.Main != null) source.Main.Paused = false;
                    foreach (var v in source.OneShots) v.Paused = false;
                    break;
                case AudioCommandKind.PlayOneShot:
                    if (m_Clips.TryGetValue(command.Clip, out var shot))
                        source.OneShots.Add(new Voice { Clip = command.Clip, Position = source.Params.Pitch < 0 ? shot.Frames : 0, StartAt = now });
                    break;
                case AudioCommandKind.Seek:
                {
                    ulong clipHandle = source.Main?.Clip ?? source.Params.Clip;
                    double frames = m_Clips.TryGetValue(clipHandle, out var c) ? c.Frames : 0;
                    double position = Math.Clamp(command.Time, 0, frames);
                    if (source.Main != null) source.Main.Position = position;
                    else source.Pending = position;
                    break;
                }
                case AudioCommandKind.SetScheduledStart:
                    if (source.Main != null && now < source.Main.StartAt) source.Main.StartAt = Start(command.Time);
                    break;
                case AudioCommandKind.SetScheduledEnd:
                    if (source.Main != null) source.Main.EndAt = FrameOf(command.Time);
                    break;
            }
        }

        internal override AudioSourceState State(ulong handle)
        {
            if (!m_Sources.TryGetValue(handle, out var source)) return default;
            var state = new AudioSourceState { OneShots = source.OneShots.Count, Position = source.Pending };
            if (source.Main != null)
            {
                state.Main = source.Main.Paused ? AudioVoiceState.Paused : m_Frames < source.Main.StartAt ? AudioVoiceState.Scheduled : AudioVoiceState.Playing;
                state.Position = source.Main.Position;
            }
            return state;
        }

        internal override void ReleaseSource(ulong handle) => m_Sources.Remove(handle);

        internal override void UpdateGroup(ulong handle, float gain, float pitch) => m_Groups[handle] = (gain, pitch);

        internal float GroupGain(ulong handle) => m_Groups.TryGetValue(handle, out var g) ? g.Gain : 1f;

        internal override AudioStatus Frame(in AudioFramePacket frame)
        {
            m_ListenerPaused = (frame.Flags & AudioFrameFlags.Paused) != 0;
            int rate = m_Configuration.sampleRate, buffer = Math.Max(1, m_Configuration.dspBufferSize);
            m_Owed += Math.Max(0, frame.DeltaTime);
            long buffers = (long)Math.Floor(m_Owed * rate / buffer);
            if (buffers > 0)
            {
                Advance(buffers * buffer);
                m_Owed -= (double)(buffers * buffer) / rate;
            }
            return new AudioStatus { DspTime = DspTime, SampleRate = rate, BufferSize = buffer, SpeakerMode = (int)m_Configuration.speakerMode, RealVoices = m_Configuration.numRealVoices };
        }

        void Advance(long n)
        {
            long t0 = m_Frames, t1 = m_Frames + n;
            foreach (var source in m_Sources.Values)
            {
                if (m_ListenerPaused && (source.Params.Flags & AudioSourceFlags.IgnoreListenerPause) == 0) continue;
                float groupPitch = m_Groups.TryGetValue(source.Params.Group, out var g) ? g.Pitch : 1f;
                double pitch = source.Params.Pitch * groupPitch;
                bool loop = (source.Params.Flags & AudioSourceFlags.Loop) != 0;
                if (source.Main != null && AdvanceVoice(source.Main, t0, t1, pitch, loop))
                {
                    source.Main = null;
                    source.Pending = 0;
                }
                source.OneShots.RemoveAll(v => AdvanceVoice(v, t0, t1, pitch, false));
            }
            m_Frames = t1;
        }

        /// <summary>True when the voice finished within [t0, t1).</summary>
        bool AdvanceVoice(Voice voice, long t0, long t1, double pitch, bool loop)
        {
            if (!m_Clips.TryGetValue(voice.Clip, out var clip) || clip.Frames == 0) return true;
            if (voice.Paused) return false;
            long from = Math.Max(t0, voice.StartAt), to = Math.Min(t1, voice.EndAt);
            double step = pitch * clip.Frequency / m_Configuration.sampleRate;
            if (to > from)
            {
                voice.Position += step * (to - from);
                if (voice.Position >= clip.Frames || voice.Position < 0)
                {
                    if (!loop) return true;
                    voice.Position = ((voice.Position % clip.Frames) + clip.Frames) % clip.Frames;
                }
            }
            return voice.EndAt < t1;
        }

        internal override double DspTime => (double)m_Frames / m_Configuration.sampleRate;

        internal override bool OutputData(ulong handle, int channel, Span<float> data, int window)
        {
            data.Clear();
            return handle == 0 || m_Sources.ContainsKey(handle);
        }

        internal override ProjectAudioSettings ProjectSettings => Project;

        internal override AudioConfiguration Configuration => m_Configuration;

        internal override bool Reset(AudioConfiguration configuration)
        {
            double seconds = DspTime;
            m_Configuration = configuration;
            if (m_Configuration.sampleRate <= 0) m_Configuration.sampleRate = 48000;
            if (m_Configuration.dspBufferSize <= 0) m_Configuration.dspBufferSize = 1024;
            m_Frames = FrameOf(seconds);
            foreach (var source in m_Sources.Values)
            {
                source.Main = null;
                source.OneShots.Clear();
                source.Pending = 0;
            }
            return true;
        }

        internal override void InstallCallback() { }
    }
}
