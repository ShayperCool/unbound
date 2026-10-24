using System;
using Unbound.Runtime.Playables;
using UnityEngine.Playables;

namespace Unbound.Runtime.Playables
{
    /// <summary>The state of an audio output: the AudioSource it plays through.</summary>
    internal sealed class AudioOutputState
    {
        internal UnityEngine.AudioSource Target;
        internal bool EvaluateOnSeek = true;
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
            handle = o.ToHandle();
            return true;
        }
    }

    public static partial class AudioPlayableBinding
    {
        public static PlayableBinding Create(string name, Object key) => PlayableBinding.CreateInternal(name, key, typeof(AudioSource), CreateAudioOutput);

        static PlayableOutput CreateAudioOutput(PlayableGraph graph, string name) => AudioPlayableOutput.Create(graph, name, null);
    }
}
