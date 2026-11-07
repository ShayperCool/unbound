using System;
using System.Collections.Generic;
using UnityEngine;
using PL = UnityEngine.PlayerLoop;

namespace Unbound.Runtime
{
    /// <summary>
    /// Runs the enabled animation components where Unity's player loop does: Animators in
    /// PreLateUpdate.DirectorUpdateAnimationBegin (frame time, or unscaled time) and in
    /// FixedUpdate.DirectorFixedUpdate (AnimatorUpdateMode.Fixed); legacy Animations in
    /// PreLateUpdate.LegacyAnimationUpdate and FixedUpdate.LegacyFixedAnimationUpdate (animatePhysics).
    /// The systems are registered when the first component is enabled.
    /// </summary>
    internal static class AnimationSystem
    {
        static readonly List<Animator> s_Animators = new List<Animator>();
        static readonly List<Animation> s_Animations = new List<Animation>();
        static Animator[] s_AnimatorBuffer = Array.Empty<Animator>();
        static Animation[] s_AnimationBuffer = Array.Empty<Animation>();
        static bool s_Registered;

        // Set by Play initialization, and ended even if no Animator registered during entry.
        // This is an animation time-source interval, not a delay attached to a new player:
        // already explicitly evaluated controllers also receive the zero-time evaluation.
        static bool s_PlayModeEntry;
        internal static void BeginPlayMode() => s_PlayModeEntry = true;
        internal static void EndPlayModeEntry() => s_PlayModeEntry = false;

        /// <summary>Measured in 6000.0.71f1 Editor Play entry (animstartup): the frame's
        /// automatic animation evaluation samples and delivers events/movement with zero
        /// elapsed animation time. Fixed and explicit evaluations keep their supplied deltas.
        /// Other releases and custom loop startup intervals are not measured.</summary>
        internal static double AutomaticFrameDelta(double delta) =>
            s_PlayModeEntry && Compat.AnimatorStartupMeasured ? 0.0 : delta;

        internal static void EnsureRegistered()
        {
            if (s_Registered) return;
            s_Registered = true;
            PlayerLoopDriver.Register(typeof(PL.PreLateUpdate.DirectorUpdateAnimationBegin), () => UpdateAnimators(fixedStep: false));
            PlayerLoopDriver.Register(typeof(PL.FixedUpdate.DirectorFixedUpdate), () => UpdateAnimators(fixedStep: true));
            PlayerLoopDriver.Register(typeof(PL.PreLateUpdate.LegacyAnimationUpdate), () => UpdateAnimations(fixedStep: false));
            PlayerLoopDriver.Register(typeof(PL.FixedUpdate.LegacyFixedAnimationUpdate), () => UpdateAnimations(fixedStep: true));
        }

        internal static void Add(Animator animator)
        {
            EnsureRegistered();
            if (!s_Animators.Contains(animator)) s_Animators.Add(animator);
        }

        internal static void Remove(Animator animator) => s_Animators.Remove(animator);

        internal static void Add(Animation animation)
        {
            EnsureRegistered();
            if (!s_Animations.Contains(animation)) s_Animations.Add(animation);
        }

        internal static void Remove(Animation animation) => s_Animations.Remove(animation);

        internal static void UpdateAnimators(bool fixedStep)
        {
            if (!fixedStep) Unbound.Runtime.Playables.PlayableGraphData.ProcessOtherOutputs();
            TickAnimators(fixedStep);
        }

        /// <summary>The Animators' own update: controllers, then their pending playable outputs.</summary>
        internal static void TickAnimators(bool fixedStep)
        {
            int count = s_Animators.Count;
            if (count == 0) return;
            if (s_AnimatorBuffer.Length < count) s_AnimatorBuffer = new Animator[Math.Max(count, s_AnimatorBuffer.Length * 2)];
            s_Animators.CopyTo(s_AnimatorBuffer);
            for (int i = 0; i < count; i++)
            {
                var a = s_AnimatorBuffer[i];
                s_AnimatorBuffer[i] = null;
                if (a == null || !a.isActiveAndEnabled) continue;
                bool isFixed = a.m_UpdateMode == AnimatorUpdateMode.Fixed;
                if (isFixed != fixedStep) continue;
                float dt = a.m_UpdateMode == AnimatorUpdateMode.UnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
                if (!fixedStep) dt = (float)AutomaticFrameDelta(dt);
                try
                {
                    a.Tick(dt);
                }
                catch (Exception e)
                {
                    Debug.LogException(e, a);
                }
                // Playable outputs combine with the Animator controller's deferred pose and motion.
                if (!fixedStep) Unbound.Runtime.Playables.AnimationOutputs.RunPending(a);
            }
            if (!fixedStep) Unbound.Runtime.Playables.AnimationOutputs.RunAllPending();
        }

        internal static void UpdateAnimations(bool fixedStep)
        {
            int count = s_Animations.Count;
            if (count == 0) return;
            if (s_AnimationBuffer.Length < count) s_AnimationBuffer = new Animation[Math.Max(count, s_AnimationBuffer.Length * 2)];
            s_Animations.CopyTo(s_AnimationBuffer);
            for (int i = 0; i < count; i++)
            {
                var a = s_AnimationBuffer[i];
                s_AnimationBuffer[i] = null;
                if (a == null || !a.isActiveAndEnabled || a.m_AnimatePhysics != fixedStep) continue;
                try
                {
                    a.Step(Time.deltaTime);
                }
                catch (Exception e)
                {
                    Debug.LogException(e, a);
                }
            }
        }
    }
}
