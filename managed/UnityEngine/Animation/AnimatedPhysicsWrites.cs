using System;
using UnityEngine;

namespace Unbound.Runtime
{
    /// <summary>The pose writes of an Animator with animatePhysics. Transforms changed while one is
    /// open carry its stamp; at the next transform sync a kinematic body whose transform, or an
    /// ancestor's, carries a stamp newer than its last sync moves there as a kinematic move (its
    /// velocity the displacement over the step, carrying bodies resting on it) instead of being
    /// teleported. Other writes to the same transforms, from scripts for one, still teleport
    /// (matches the Unity 6000.0.71f1 Editor, tests/projects/animatephysics).</summary>
    internal readonly struct AnimatedPhysicsWrites : IDisposable
    {
        /// <summary>The last stamp handed out; stamps only grow.</summary>
        internal static int s_LastStamp;

        readonly int m_Outer;

        AnimatedPhysicsWrites(int outer) => m_Outer = outer;

        /// <summary>Opens the writes of <paramref name="animator"/>; writes of an Animator without
        /// animatePhysics (or of none) are left unstamped, also inside another one's.</summary>
        internal static AnimatedPhysicsWrites Begin(Animator animator)
        {
            var scope = new AnimatedPhysicsWrites(Transform.s_AnimatedPhysicsStamp);
            Transform.s_AnimatedPhysicsStamp = animator != null && animator.m_AnimatePhysics ? ++s_LastStamp : 0;
            return scope;
        }

        public void Dispose() => Transform.s_AnimatedPhysicsStamp = m_Outer;

        /// <summary>Whether an animatePhysics write changed <paramref name="t"/> or an ancestor after
        /// <paramref name="since"/>.</summary>
        internal static bool MovedSince(Transform t, int since)
        {
            for (; t != null; t = t.m_Parent)
                if (t.m_AnimatedPhysicsStamp > since) return true;
            return false;
        }
    }
}
