using System;
using System.Collections.Generic;
using Unbound.Runtime.Playables;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Playables;

namespace Unbound.Runtime.Playables
{
    /// <summary>The state of an audio output: the AudioSource it plays through.</summary>
    internal sealed class AudioOutputState
    {
        internal AudioSource Target;
        internal bool EvaluateOnSeek = true;
    }

    /// <summary>
    /// An AudioClipPlayable: its time is its position in the clip (its duration the clip's length,
    /// held there at the end; it reports done once the end was reached a frame before, unless it
    /// loops). While the graph plays and the playable reaches an audio output playing, a voice
    /// plays the clip through the output's AudioSource from the playable's time.
    /// </summary>
    internal sealed class AudioClipKind : PlayableKind
    {
        internal AudioClip Clip;
        internal bool Looped;
        internal float Volume = 1f;
        internal float StereoPan;
        internal float SpatialBlend;
        internal double StartDelay;
        internal double PauseDelay;
        internal ulong Voice;
        internal bool VoicePlaying;
        internal AudioSource VoiceSource;
        /// <summary>Where the playing voice should be (clip seconds), to tell a seek from playback.</summary>
        internal double VoiceTime;
        internal ulong SeenFrame;

        /// <summary>It reports done itself, a frame after reaching the end (unless it loops).</summary>
        internal override bool FinishesAtDuration => false;

        internal override void Prepare(PlayableNode node, in FrameData info)
        {
            // A start delay (Seek) holds the time until it has run out.
            if (StartDelay > 0 && !info.seekOccurred)
            {
                double moved = node.Time - node.PreviousTime;
                if (moved > 0)
                {
                    double use = Math.Min(StartDelay, moved);
                    StartDelay -= use;
                    node.Time -= use;
                }
            }
            if (!Looped && node.PreviousTime >= node.Duration && node.Time >= node.Duration) node.Done = true;
        }

        internal override void Destroyed(PlayableNode node) => StopVoice(release: true);

        internal void StopVoice(bool release)
        {
            if (Voice == 0) return;
            if (VoicePlaying) AudioBackend.Current.Command(Voice, new AudioSourceCommand { Kind = AudioCommandKind.Stop });
            VoicePlaying = false;
            if (release)
            {
                AudioBackend.Current.ReleaseSource(Voice);
                Voice = 0;
            }
        }

        internal bool IsChannelPlaying()
        {
            if (Voice == 0 || !VoicePlaying) return false;
            var state = AudioBackend.Current.State(Voice);
            return state.Main == AudioVoiceState.Playing || state.Main == AudioVoiceState.Scheduled;
        }

        /// <summary>Keeps the voice in step with the playable this frame.</summary>
        internal void Drive(PlayableNode node, AudioSource source, float weight, bool wanted, bool seek)
        {
            if (Clip == null || source == null || !wanted || weight <= 0f || node.Done)
            {
                StopVoice(release: false);
                return;
            }
            Clip.EnsureLoaded();
            if (Voice == 0) Voice = AudioBackend.NextHandle();
            source.SendPacket(Voice, Clip, source.m_Volume * Volume * weight, Looped);
            double t = node.Time;
            if (Looped && Clip.length > 0f) t %= Clip.length;
            if (!VoicePlaying || VoiceSource != source || seek)
            {
                AudioBackend.Current.Command(Voice, new AudioSourceCommand { Kind = AudioCommandKind.Seek, Time = Math.Max(0.0, t) * Clip.m_Frequency });
                AudioBackend.Current.Command(Voice, new AudioSourceCommand { Kind = AudioCommandKind.Play, Time = StartDelay > 0 ? -StartDelay : 0.0 });
                VoicePlaying = true;
                VoiceSource = source;
            }
            VoiceTime = t;
        }
    }

    /// <summary>An AudioMixerPlayable: mixes its inputs by weight.</summary>
    internal sealed class AudioMixerKind : PlayableKind
    {
        internal bool NormalizeInputVolumes;
    }

    /// <summary>Plays the audio clip playables an audio output reaches while their graph plays.</summary>
    internal sealed class AudioOutputs : OutputSystems.IProcessor
    {
        static bool s_Registered;
        static readonly Dictionary<PlayableOutputData, HashSet<AudioClipKind>> s_Voices = new Dictionary<PlayableOutputData, HashSet<AudioClipKind>>();

        internal static void EnsureRegistered()
        {
            if (s_Registered) return;
            s_Registered = true;
            OutputSystems.Register(typeof(AudioPlayableOutput), new AudioOutputs());
        }

        public void Process(PlayableOutputData output, double dt, bool seek)
        {
            output.Graph.DeliverNotifications(output);
            // Evaluations (scrubbing) do not play sound; the player loop does.
            if (seek) return;
            var state = output.Engine as AudioOutputState;
            var source = state?.Target;
            bool audible = source != null && source.isActiveAndEnabled && output.Graph.Playing;
            if (!s_Voices.TryGetValue(output, out var previous)) s_Voices[output] = previous = new HashSet<AudioClipKind>();
            var reached = new HashSet<AudioClipKind>();
            if (output.Source != null)
                Walk(output.Source, output.SourcePort, output.Weight, true, source, audible, reached, 0);
            foreach (var kind in previous)
                if (!reached.Contains(kind)) kind.StopVoice(release: false);
            previous.Clear();
            previous.UnionWith(reached);
        }

        static void Walk(PlayableNode n, int viaPort, float weight, bool parentPlaying, AudioSource source, bool audible, HashSet<AudioClipKind> reached, int depth)
        {
            if (n == null || n.Destroyed || depth > 64) return;
            bool playing = parentPlaying && n.State == PlayState.Playing;
            if (n.Kind is AudioClipKind clip)
            {
                reached.Add(clip);
                clip.Drive(n, source, weight, audible && playing, (n.FrameFlags & FrameData.Flags.SeekOccured) != 0);
                // Playback that reaches the end of a clip that does not loop pauses its playable.
                if (playing && !clip.Looped && n.Time >= n.Duration && n.Duration < double.MaxValue)
                {
                    clip.StopVoice(release: false);
                    n.State = PlayState.Paused;
                }
                return;
            }
            if (n.Traversal == PlayableTraversalMode.Passthrough)
            {
                if (viaPort >= 0 && viaPort < n.Inputs.Count && n.Inputs[viaPort].Node != null)
                    Walk(n.Inputs[viaPort].Node, n.Inputs[viaPort].SourcePort, weight * n.Inputs[viaPort].Weight, playing, source, audible, reached, depth + 1);
                return;
            }
            // Script playables pass their inputs on unweighted, mixers by weight.
            bool mixer = n.Kind is AudioMixerKind;
            foreach (var i in n.Inputs)
                if (i.Node != null) Walk(i.Node, i.SourcePort, mixer ? weight * i.Weight : weight, playing, source, audible, reached, depth + 1);
        }

        public void Stopped(PlayableGraphData graph)
        {
            foreach (var pair in s_Voices)
            {
                if (pair.Key.Graph != graph) continue;
                foreach (var kind in pair.Value) kind.StopVoice(release: false);
                pair.Value.Clear();
            }
        }

        public void Flush() { }
    }
}

namespace UnityEngine.Audio
{
    /// <summary>An output that plays the audio of its source playable through an AudioSource.</summary>
    public partial struct AudioPlayableOutput : IPlayableOutput
    {
        PlayableOutputHandle m_Handle;

        public static AudioPlayableOutput Null => new AudioPlayableOutput(PlayableOutputHandle.Null);

        public static AudioPlayableOutput Create(PlayableGraph graph, string name, AudioSource target)
        {
            if (!AudioPlayableGraphExtensions.InternalCreateAudioOutput(ref graph, name, out var handle)) return Null;
            var output = new AudioPlayableOutput(handle);
            output.SetTarget(target);
            return output;
        }

        internal AudioPlayableOutput(PlayableOutputHandle handle)
        {
            if (handle.IsValid() && !handle.IsPlayableOutputOfType<AudioPlayableOutput>())
                throw new InvalidCastException("Can't set handle: the playable is not an AudioPlayableOutput.");
            m_Handle = handle;
        }

        public PlayableOutputHandle GetHandle() => m_Handle;

        public static implicit operator PlayableOutput(AudioPlayableOutput output) => new PlayableOutput(output.GetHandle());

        public static explicit operator AudioPlayableOutput(PlayableOutput output) => new AudioPlayableOutput(output.GetHandle());

        internal AudioOutputState State
        {
            get
            {
                var o = PlayableGraphData.Get(m_Handle) ?? throw new InvalidOperationException("The PlayableOutput is invalid.");
                return o.Engine as AudioOutputState ?? (AudioOutputState)(o.Engine = new AudioOutputState());
            }
        }

        public AudioSource GetTarget() => State.Target;

        public void SetTarget(AudioSource value) => State.Target = value;

        public bool GetEvaluateOnSeek() => State.EvaluateOnSeek;

        public void SetEvaluateOnSeek(bool value) => State.EvaluateOnSeek = value;
    }

    internal static partial class AudioPlayableGraphExtensions
    {
        internal static bool InternalCreateAudioOutput(ref PlayableGraph graph, string name, out PlayableOutputHandle handle)
        {
            var g = PlayableGraphData.Get(graph);
            if (g == null)
            {
                handle = PlayableOutputHandle.Null;
                return false;
            }
            var o = g.CreateOutput(typeof(AudioPlayableOutput), name);
            o.Engine = new AudioOutputState();
            AudioOutputs.EnsureRegistered();
            handle = o.ToHandle();
            return true;
        }
    }

    public static partial class AudioPlayableBinding
    {
        public static PlayableBinding Create(string name, Object key) => PlayableBinding.CreateInternal(name, key, typeof(AudioSource), CreateAudioOutput);

        static PlayableOutput CreateAudioOutput(PlayableGraph graph, string name) => AudioPlayableOutput.Create(graph, name, null);
    }

    /// <summary>A playable playing an audio clip.</summary>
    public partial struct AudioClipPlayable : IPlayable, IEquatable<AudioClipPlayable>
    {
        PlayableHandle m_Handle;

        public static AudioClipPlayable Create(PlayableGraph graph, AudioClip clip, bool looping)
        {
            var g = PlayableGraphData.Get(graph) ?? throw new InvalidOperationException("The PlayableGraph is invalid.");
            var n = g.CreateNode(typeof(AudioClipPlayable));
            n.Kind = new AudioClipKind { Clip = clip, Looped = looping };
            AudioOutputs.EnsureRegistered();
            var playable = new AudioClipPlayable(n.ToHandle());
            if (clip != null) playable.SetDuration(clip.length);
            return playable;
        }

        internal AudioClipPlayable(PlayableHandle handle)
        {
            if (handle.IsValid() && !handle.IsPlayableOfType<AudioClipPlayable>())
                throw new InvalidCastException("Can't set handle: the playable is not an AudioClipPlayable.");
            m_Handle = handle;
        }

        public PlayableHandle GetHandle() => m_Handle;

        public static implicit operator Playable(AudioClipPlayable playable) => new Playable(playable.GetHandle());

        public static explicit operator AudioClipPlayable(Playable playable) => new AudioClipPlayable(playable.GetHandle());

        public bool Equals(AudioClipPlayable other) => GetHandle() == other.GetHandle();

        AudioClipKind Kind => PlayableGraphData.Get(m_Handle)?.Kind as AudioClipKind ?? throw new InvalidOperationException("The AudioClipPlayable is invalid.");

        public AudioClip GetClip() => Kind.Clip;

        public void SetClip(AudioClip value)
        {
            var k = Kind;
            k.StopVoice(release: false);
            k.Clip = value;
        }

        public bool GetLooped() => Kind.Looped;

        public void SetLooped(bool value) => Kind.Looped = value;

        internal float GetVolume() => Kind.Volume;

        internal void SetVolume(float value)
        {
            if (value < 0f || value > 1f) throw new ArgumentException("Trying to set AudioClipPlayable volume outside of range (0.0 - 1.0): " + value);
            Kind.Volume = value;
        }

        internal float GetClipPositionSec() => (float)m_Handle.GetTime();

        internal float GetStereoPan() => Kind.StereoPan;

        internal void SetStereoPan(float value)
        {
            if (value < -1f || value > 1f) throw new ArgumentException("Trying to set AudioClipPlayable stereo pan outside of range (-1.0 - 1.0): " + value);
            Kind.StereoPan = value;
        }

        internal float GetSpatialBlend() => Kind.SpatialBlend;

        internal void SetSpatialBlend(float value)
        {
            if (value < 0f || value > 1f) throw new ArgumentException("Trying to set AudioClipPlayable spatial blend outside of range (0.0 - 1.0): " + value);
            Kind.SpatialBlend = value;
        }

        [Obsolete("IsPlaying() has been deprecated. Use IsChannelPlaying() instead (UnityUpgradable) -> IsChannelPlaying()")]
        public bool IsPlaying() => IsChannelPlaying();

        public bool IsChannelPlaying() => Kind.IsChannelPlaying();

        public double GetStartDelay() => Kind.StartDelay;

        internal void SetStartDelay(double value) => Kind.StartDelay = value;

        public double GetPauseDelay() => Kind.PauseDelay;

        internal void GetPauseDelay(double value)
        {
            double current = Kind.PauseDelay;
            if (m_Handle.GetPlayState() == PlayState.Playing && (value < 0.05 || (current != 0.0 && current < 0.05)))
                throw new ArgumentException("AudioClipPlayable.pauseDelay: Setting new delay when existing delay is too small or 0.0 (" + current + "), audio system will not be able to change in time");
            Kind.PauseDelay = value;
        }

        public void Seek(double startTime, double startDelay) => Seek(startTime, startDelay, 0.0);

        public void Seek(double startTime, double startDelay, [UnityEngine.Internal.DefaultValue("0")] double duration)
        {
            var k = Kind;
            k.StartDelay = startDelay;
            if (duration > 0.0)
            {
                if (startDelay + duration >= m_Handle.GetDuration()) m_Handle.SetDone(true);
                m_Handle.SetDuration(duration + startTime);
                k.PauseDelay = startDelay + duration;
            }
            else
            {
                m_Handle.SetDone(true);
                m_Handle.SetDuration(double.MaxValue);
                k.PauseDelay = 0.0;
            }
            m_Handle.SetTime(startTime);
            m_Handle.Play();
        }
    }

    /// <summary>A playable mixing audio playables.</summary>
    public partial struct AudioMixerPlayable : IPlayable, IEquatable<AudioMixerPlayable>
    {
        PlayableHandle m_Handle;

        public static AudioMixerPlayable Create(PlayableGraph graph, int inputCount = 0, bool normalizeInputVolumes = false)
        {
            var g = PlayableGraphData.Get(graph) ?? throw new InvalidOperationException("The PlayableGraph is invalid.");
            var n = g.CreateNode(typeof(AudioMixerPlayable));
            n.Kind = new AudioMixerKind { NormalizeInputVolumes = normalizeInputVolumes };
            g.SetInputCount(n, inputCount);
            AudioOutputs.EnsureRegistered();
            return new AudioMixerPlayable(n.ToHandle());
        }

        internal AudioMixerPlayable(PlayableHandle handle)
        {
            if (handle.IsValid() && !handle.IsPlayableOfType<AudioMixerPlayable>())
                throw new InvalidCastException("Can't set handle: the playable is not an AudioMixerPlayable.");
            m_Handle = handle;
        }

        public PlayableHandle GetHandle() => m_Handle;

        public static implicit operator Playable(AudioMixerPlayable playable) => new Playable(playable.GetHandle());

        public static explicit operator AudioMixerPlayable(Playable playable) => new AudioMixerPlayable(playable.GetHandle());

        public bool Equals(AudioMixerPlayable other) => GetHandle() == other.GetHandle();
    }
}
