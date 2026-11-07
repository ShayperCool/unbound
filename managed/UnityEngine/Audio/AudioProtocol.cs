using System.Runtime.InteropServices;
using UnityEngine;

namespace Unbound.Runtime
{
    // The structures the audio code hands the native mixer (docs/AUDIO.md). The layouts are shared
    // with `crates/unbound-runtime/src/audio/protocol.rs`; both sides change together and
    // NativeApi.CurrentVersion is bumped when they do.

    [StructLayout(LayoutKind.Sequential)]
    internal struct AudioClipInfo
    {
        internal int Frames;
        internal int Channels;
        internal int Frequency;
        internal int LoadType;
        internal int Preload;
        internal int LoadInBackground;
        internal int Ambisonic;
        internal int CompressionFormat;
    }

    internal static class AudioSourceFlags
    {
        internal const uint Loop = 1;
        internal const uint Mute = 1 << 1;
        internal const uint IgnoreListenerVolume = 1 << 2;
        internal const uint IgnoreListenerPause = 1 << 3;
        internal const uint BypassEffects = 1 << 4;
        internal const uint BypassListenerEffects = 1 << 5;
        internal const uint BypassReverbZones = 1 << 6;
        internal const uint Filter = 1 << 7;
        internal const uint Spatialize = 1 << 8;
        internal const uint KeepOutput = 1 << 9;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct AudioSourcePacket
    {
        internal ulong Clip;
        internal ulong Group;
        internal Vector3 Position;
        internal Vector3 Velocity;
        internal float Volume;
        internal float Pitch;
        internal float PanStereo;
        internal float SpatialBlend;
        internal float Spread;
        internal float DopplerLevel;
        internal float MinDistance;
        internal float MaxDistance;
        /// <summary>The linear gain of the source's send into the reverb zones (NativeApi 21).</summary>
        internal float ReverbZoneMix;
        internal int RolloffMode;
        internal int Priority;
        internal uint Flags;
        internal int CurveLength;
        internal int Pad;
        internal float* RolloffCurve;
        internal float* SpatialBlendCurve;
        internal float* SpreadCurve;
        /// <summary>Enabled filter components (NativeApi 16); 0 when none.</summary>
        internal int FilterCount;
        internal int Pad2;
        /// <summary><see cref="FilterCount"/> descriptors in component order; null when none. Valid
        /// during the call only (the mixer copies them).</summary>
        internal AudioFilterDescriptor* Filters;
    }

    internal static class AudioFilterKind
    {
        internal const int LowPass = 1;
        internal const int HighPass = 2;
        internal const int Echo = 3;
        /// <summary>AudioReverbFilter (NativeApi 20): <see cref="AudioFilterDescriptor.Reverb"/>.</summary>
        internal const int Reverb = 4;
        /// <summary>AudioDistortionFilter (NativeApi 23): Level in Cutoff; layout unchanged.</summary>
        internal const int Distortion = 5;
        /// <summary>AudioChorusFilter (NativeApi 24): first seven Reverb entries, dry, wet1..3, delay, rate, depth.</summary>
        internal const int Chorus = 6;
        /// <summary>The highest kind: the native mixer and <see cref="VirtualAudioBackend"/> accept
        /// exactly LowPass..Last (filter_kind::LAST; a Rust test reads this class).</summary>
        internal const int Last = Chorus;
        internal static bool Supported(int kind) => kind >= LowPass && kind <= Last;
    }

    /// <summary>Where a component sits relative to the scripts with OnAudioFilterRead on its
    /// GameObject.</summary>
    internal static class AudioFilterStage
    {
        internal const int BeforeScripts = 0;
        internal const int AfterScripts = 1;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct AudioFilterDescriptor
    {
        /// <summary>The component's instance ID: the mixer keeps its state while it is the same.</summary>
        internal ulong Id;
        internal int Kind;
        internal int Stage;
        internal float Cutoff;
        internal float Resonance;
        internal float Delay;
        internal float Decay;
        internal float DryMix;
        internal float WetMix;
        /// <summary>Reverb: the AudioMixer SFX Reverb parameters in the mixer's order (Dry Level,
        /// Room, Room HF, Decay Time, Decay HF Ratio, Reflections, Reflect Delay, Reverb, Reverb
        /// Delay, Diffusion, Density, HF Reference, Room LF, LF Reference). Chorus uses the first
        /// seven entries for dry, wet1..3, delay, rate and depth.</summary>
        internal fixed float Reverb[ReverbCount];
        internal const int ReverbCount = 14;

        /// <summary>The most descriptors one source sends (a resource bound, not a Unity limit).</summary>
        internal const int MaxPerSource = 64;
    }

    internal static class AudioCommandKind
    {
        internal const int Play = 1;
        internal const int Stop = 2;
        internal const int Pause = 3;
        internal const int UnPause = 4;
        internal const int PlayOneShot = 5;
        internal const int Seek = 6;
        internal const int SetScheduledStart = 7;
        internal const int SetScheduledEnd = 8;
        internal const int StopMain = 9;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AudioSourceCommand
    {
        internal int Kind;
        internal int Pad;
        /// <summary>Play: dsp seconds (&gt; 0), a delay (&lt; 0) or now (0); Seek: clip frames.</summary>
        internal double Time;
        internal ulong Clip;
        internal float VolumeScale;
        internal int Pad2;
    }

    internal static class AudioVoiceState
    {
        internal const int None = 0;
        internal const int Scheduled = 1;
        internal const int Playing = 2;
        internal const int Paused = 3;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AudioSourceState
    {
        internal int Main;
        internal int OneShots;
        internal int IsVirtual;
        internal int Pad;
        /// <summary>Clip frames.</summary>
        internal double Position;
    }

    internal static class AudioFrameFlags
    {
        internal const int HasListener = 1;
        internal const int Paused = 1 << 1;
        internal const int ListenerFilter = 1 << 2;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct AudioFramePacket
    {
        internal double DeltaTime;
        internal Vector3 ListenerPosition;
        internal Vector3 ListenerForward;
        internal Vector3 ListenerUp;
        internal Vector3 ListenerVelocity;
        internal float ListenerVolume;
        internal int Flags;
        /// <summary>The listener's AudioReverbFilter components (NativeApi 20); 0 when none.</summary>
        internal int FilterCount;
        internal int Pad;
        /// <summary><see cref="FilterCount"/> descriptors in component order; valid during the
        /// call only.</summary>
        internal AudioFilterDescriptor* Filters;
        /// <summary>The reverb zones' SFX Reverb (NativeApi 21): kind Reverb while a zone is
        /// enabled, 0 otherwise; a new <c>Id</c> restarts the reverb empty.</summary>
        internal AudioFilterDescriptor Zone;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AudioStatus
    {
        internal double DspTime;
        internal int SampleRate;
        internal int BufferSize;
        internal int SpeakerMode;
        internal int Device;
        internal int RealVoices;
        internal int PlayingSources;
    }

    internal static class AudioCallbackKind
    {
        internal const int SourceFilter = 0;
        internal const int ListenerFilter = 1;
        internal const int PcmRead = 2;
        internal const int PcmSetPosition = 3;
        internal const int GroupOutput = 4;
    }
}
