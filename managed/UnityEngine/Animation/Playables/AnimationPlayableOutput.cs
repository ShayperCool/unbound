using System;
using Unbound.Runtime.Playables;
using UnityEngine.Experimental.Animations;
using UnityEngine.Playables;

namespace Unbound.Runtime.Playables
{
    /// <summary>The state of an animation output: the Animator it writes and how it layers there.</summary>
    internal sealed class AnimationOutputState
    {
        internal UnityEngine.Animator Target;
        internal ushort SortingOrder = 100;
        internal AnimationStreamSource StreamSource = AnimationStreamSource.DefaultValues;
    }
}

namespace UnityEngine.Animations
{
    /// <summary>An output that writes the animation of its source playable to an Animator.</summary>
    public partial struct AnimationPlayableOutput : IPlayableOutput
    {
        PlayableOutputHandle m_Handle;

        public static AnimationPlayableOutput Null => new AnimationPlayableOutput(PlayableOutputHandle.Null);

        public static AnimationPlayableOutput Create(PlayableGraph graph, string name, Animator target)
        {
            if (!AnimationPlayableGraphExtensions.InternalCreateAnimationOutput(ref graph, name, out var handle)) return Null;
            var output = new AnimationPlayableOutput(handle);
            output.SetTarget(target);
            return output;
        }

        internal AnimationPlayableOutput(PlayableOutputHandle handle)
        {
            if (handle.IsValid() && !handle.IsPlayableOutputOfType<AnimationPlayableOutput>())
                throw new InvalidCastException("Can't set handle: the playable is not an AnimationPlayableOutput.");
            m_Handle = handle;
        }

        public PlayableOutputHandle GetHandle() => m_Handle;

        public static implicit operator PlayableOutput(AnimationPlayableOutput output) => new PlayableOutput(output.GetHandle());

        public static explicit operator AnimationPlayableOutput(PlayableOutput output) => new AnimationPlayableOutput(output.GetHandle());

        internal AnimationOutputState State
        {
            get
            {
                var o = PlayableGraphData.Get(m_Handle) ?? throw new InvalidOperationException("The PlayableOutput is invalid.");
                return o.Engine as AnimationOutputState ?? (AnimationOutputState)(o.Engine = new AnimationOutputState());
            }
        }

        public Animator GetTarget() => State.Target;

        public void SetTarget(Animator value)
        {
            var state = State;
            if (state.Target == value) return;
            var old = state.Target;
            state.Target = value;
            AnimationOutputs.TargetChanged(PlayableGraphData.Get(m_Handle), old, value);
        }
    }

    internal static partial class AnimationPlayableGraphExtensions
    {
        internal static void SyncUpdateAndTimeMode(this PlayableGraph graph, Animator animator) => InternalSyncUpdateAndTimeMode(ref graph, animator);

        internal static void DestroyOutput(this PlayableGraph graph, PlayableOutputHandle handle) => graph.DestroyOutputInternal(handle);

        internal static bool InternalCreateAnimationOutput(ref PlayableGraph graph, string name, out PlayableOutputHandle handle)
        {
            var g = PlayableGraphData.Get(graph);
            if (g == null)
            {
                handle = PlayableOutputHandle.Null;
                return false;
            }
            var o = g.CreateOutput(typeof(AnimationPlayableOutput), name);
            o.Engine = new AnimationOutputState();
            handle = o.ToHandle();
            return true;
        }

        /// <summary>The graph follows the Animator's update mode: unscaled time for UnscaledTime,
        /// game time otherwise.</summary>
        internal static void InternalSyncUpdateAndTimeMode(ref PlayableGraph graph, Animator animator)
        {
            var g = PlayableGraphData.Get(graph);
            if (g == null || animator == null) return;
            g.UpdateMode = animator.updateMode == AnimatorUpdateMode.UnscaledTime ? DirectorUpdateMode.UnscaledGameTime : DirectorUpdateMode.GameTime;
        }
    }

    public static partial class AnimationPlayableBinding
    {
        public static PlayableBinding Create(string name, Object key) => PlayableBinding.CreateInternal(name, key, typeof(Animator), CreateAnimationOutput);

        static PlayableOutput CreateAnimationOutput(PlayableGraph graph, string name) => AnimationPlayableOutput.Create(graph, name, null);
    }
}

namespace UnityEngine.Experimental.Animations
{
    public static partial class AnimationPlayableOutputExtensions
    {
        public static AnimationStreamSource GetAnimationStreamSource(this UnityEngine.Animations.AnimationPlayableOutput output) => output.State.StreamSource;

        public static void SetAnimationStreamSource(this UnityEngine.Animations.AnimationPlayableOutput output, AnimationStreamSource streamSource) => output.State.StreamSource = streamSource;

        public static ushort GetSortingOrder(this UnityEngine.Animations.AnimationPlayableOutput output) => output.State.SortingOrder;

        public static void SetSortingOrder(this UnityEngine.Animations.AnimationPlayableOutput output, ushort sortingOrder) => output.State.SortingOrder = sortingOrder;
    }
}
