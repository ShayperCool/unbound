using System;
using System.Collections.Generic;
using UnityEngine;
using Unbound.Runtime.Humanoid;
using Unbound.Runtime.Playables;

namespace Unbound.Runtime
{
    /// <summary>
    /// The player loop's Animator update in its two leaves, as the Unity 6000.0.71f1 Editor runs it
    /// (tests/projects/animcallbacks; the Execution Order manual only places "the animation
    /// update" before LateUpdate):
    /// <list type="bullet">
    /// <item>PreLateUpdate.DirectorUpdateAnimationBegin (FixedUpdate.DirectorFixedUpdate for
    /// AnimatorUpdateMode.Fixed): every Animator's state machines, animation events and state
    /// callbacks, Animator by Animator; then every Animator's movement (OnAnimatorMove, OnStateMove,
    /// builtin root motion). No pose is written yet.</item>
    /// <item>PreLateUpdate.DirectorUpdateAnimationEnd (FixedUpdate.DirectorFixedUpdatePostPhysics):
    /// the humanoid Animators write their pose, then the IK passes run layer by layer over all of
    /// them (OnAnimatorIK, then OnStateIK, then the solve), then the other Animators write
    /// theirs.</item>
    /// </list>
    /// Explicit evaluations (Animator.Update, PlayableGraph.Evaluate, a visibility return) still
    /// run all of it at once. Release-specific: <see cref="Compat.AnimationUpdateSplitMeasured"/>;
    /// other releases keep the single pass in the Begin leaf.
    /// </summary>
    internal static class AnimationPhases
    {
        struct Deferred
        {
            internal Animator Animator;
            internal AnimatorPlayer Player;
            internal PendingOutput Output;
        }

        static List<Deferred> s_Pending = new List<Deferred>();
        static List<Deferred> s_Spare = new List<Deferred>();
        static List<AnimatorHuman> s_IK = new List<AnimatorHuman>();
        static List<AnimatorHuman> s_IKSpare = new List<AnimatorHuman>();
        static bool s_CollectIK;

        /// <summary>Whether the automatic update defers movement and writes to the second leaf.</summary>
        internal static bool Split => Compat.AnimationUpdateSplitMeasured;

        /// <summary>A controller update whose movement and write wait for the later passes.</summary>
        internal static void Defer(Animator animator, AnimatorPlayer player) =>
            s_Pending.Add(new Deferred { Animator = animator, Player = player });

        /// <summary>A playable output evaluation whose movement and write wait for the later passes.</summary>
        internal static void Defer(Animator animator, PendingOutput output) =>
            s_Pending.Add(new Deferred { Animator = animator, Output = output });

        /// <summary>The Begin leaf's second pass: each deferred update's movement, in update order.</summary>
        internal static void DeliverMovement()
        {
            // Movement callbacks run explicit evaluations at once; they add nothing here.
            for (int i = 0; i < s_Pending.Count; i++)
            {
                var d = s_Pending[i];
                if (!Live(d.Animator)) continue;
                try
                {
                    if (d.Output != null) d.Output.Move();
                    else if (d.Player == d.Animator.m_Player) d.Player.DeliverDeferredMovement();
                }
                catch (Exception e) { Debug.LogException(e, d.Animator); }
            }
        }

        /// <summary>The End leaf: the deferred writes and the IK passes.</summary>
        internal static void WritePoses()
        {
            if (s_Pending.Count == 0) return;
            var pending = s_Pending;
            s_Pending = s_Spare;
            try
            {
                s_CollectIK = true;
                try { WriteAll(pending, human: true); }
                finally { s_CollectIK = false; }
                RunIK();
                WriteAll(pending, human: false);
            }
            finally
            {
                foreach (var d in pending) d.Output?.Release();
                pending.Clear();
                s_Spare = pending;
            }
        }

        static bool Live(Animator animator) => animator != null && animator.isActiveAndEnabled;

        static void WriteAll(List<Deferred> pending, bool human)
        {
            foreach (var d in pending)
            {
                if (d.Animator == null || d.Animator.isHuman != human) continue;
                try
                {
                    if (d.Output != null) d.Output.Write(Live(d.Animator));
                    // A controller swapped or rebound between the leaves has a new player.
                    else if (Live(d.Animator) && d.Player == d.Animator.m_Player) d.Player.WriteDeferred();
                }
                catch (Exception e) { Debug.LogException(e, d.Animator); }
            }
        }

        /// <summary>Applies a humanoid pose: at once with its IK passes, or (in the End leaf) before
        /// the IK passes of all Animators.</summary>
        internal static void ApplyHuman(AnimatorHuman human, AnimatorPlayer player, bool playableIK)
        {
            if (!s_CollectIK)
            {
                human.Apply(player, playableIK);
                return;
            }
            if (human.ApplyPose(player, playableIK) > 0) s_IK.Add(human);
        }

        /// <summary>Layer by layer: each posed humanoid with an IK pass on that layer, in update order.</summary>
        static void RunIK()
        {
            if (s_IK.Count == 0) return;
            var humans = s_IK;
            s_IK = s_IKSpare;
            try
            {
                int layers = 0;
                foreach (var h in humans) layers = Math.Max(layers, h.IKLayers);
                for (int layer = 0; layer < layers; layer++)
                    foreach (var h in humans)
                    {
                        try { h.RunIK(layer); }
                        catch (Exception e) { Debug.LogException(e); }
                    }
            }
            finally
            {
                humans.Clear();
                s_IKSpare = humans;
            }
        }
    }
}
