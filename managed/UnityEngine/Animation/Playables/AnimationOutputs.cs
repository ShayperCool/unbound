using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;

namespace Unbound.Runtime.Playables
{
    /// <summary>The animation outputs that target each Animator, which the Animator evaluates.</summary>
    internal static class AnimationOutputs
    {
        static readonly Dictionary<Animator, List<PlayableOutputData>> s_ByAnimator = new Dictionary<Animator, List<PlayableOutputData>>();

        internal static void TargetChanged(PlayableOutputData output, Animator old, Animator target)
        {
            if (output == null) return;
            if (old != null && s_ByAnimator.TryGetValue(old, out var list)) list.Remove(output);
            if (target != null)
            {
                if (!s_ByAnimator.TryGetValue(target, out list)) s_ByAnimator[target] = list = new List<PlayableOutputData>();
                if (!list.Contains(output)) list.Add(output);
            }
        }

        internal static List<PlayableOutputData> Of(Animator animator) => s_ByAnimator.TryGetValue(animator, out var list) ? list : null;
    }
}
