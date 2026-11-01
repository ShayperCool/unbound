using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Animations;

namespace Unbound.Runtime
{
    internal sealed partial class AnimatorPlayer
    {
        AnimationPose m_RootScratch;
        float m_RootDeltaTime;
        bool m_CallbacksStarted;
        bool m_HasMonoMove, m_HasStateMove;
        static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, MethodInfo> s_OnAnimatorMove =
            new System.Collections.Concurrent.ConcurrentDictionary<Type, MethodInfo>();
        static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, bool> s_StateMove =
            new System.Collections.Concurrent.ConcurrentDictionary<Type, bool>();

        static MethodInfo MoveMethod(MonoBehaviour behaviour) => s_OnAnimatorMove.GetOrAdd(behaviour.GetType(),
            type => ScriptMessages.Find(type, "OnAnimatorMove", Type.EmptyTypes));

        IEnumerable<(StateMachineBehaviour Behaviour, AnimatorStateInfo Info, int Layer)> MovingStates()
        {
            for (int i = 0; i < Layers.Length; i++)
            {
                var layer = Layers[i];
                var source = layer.Def.SyncedLayerIndex >= 0 && layer.Def.SyncedLayerIndex < Layers.Length ? Layers[layer.Def.SyncedLayerIndex] : layer;
                foreach (var run in new[] { source.Current, source.Next })
                {
                    if (run == null) continue;
                    foreach (var b in BehavioursOf(layer, run.Def))
                        if (b != null) yield return (b, StateInfo(layer, run), i);
                }
            }
        }

        static bool HandlesStateMove(StateMachineBehaviour behaviour) => s_StateMove.GetOrAdd(behaviour.GetType(), type =>
        {
            var args = new[] { typeof(Animator), typeof(AnimatorStateInfo), typeof(int) };
            var method = type.GetMethod("OnStateMove", args);
            if (method != null && method.DeclaringType != typeof(StateMachineBehaviour)) return true;
            method = type.GetMethod("OnStateMove", new[] { typeof(Animator), typeof(AnimatorStateInfo), typeof(int), typeof(AnimatorControllerPlayable) });
            return method != null && method.DeclaringType != typeof(StateMachineBehaviour);
        });

        /// <summary>Generic root curves become a delta in the root's current frame. A registered
        /// MonoBehaviour callback requests deltas even while disabled and with applyRootMotion off.
        /// State move callbacks intercept the root pose too, but do not request deltas themselves.</summary>
        void ComputeRootMotion(int count)
        {
            if (!Attached) return;
            var animator = m_Animator;
            animator.CaptureRootMotionPose();
            animator.m_DeltaPosition = Vector3.zero;
            animator.m_DeltaRotation = Quaternion.identity;
            animator.m_RootVelocity = Vector3.zero;
            animator.m_RootAngularVelocity = Vector3.zero;
            animator.m_RootMotionApplied = false;
            animator.m_RootMotionReady = false;
            m_HasMonoMove = false;
            foreach (var mb in animator.gameObject.GetComponents<MonoBehaviour>())
                if (mb != null && MoveMethod(mb) != null) { m_HasMonoMove = true; break; }
            m_HasStateMove = false;
            foreach (var state in MovingStates())
                if (HandlesStateMove(state.Behaviour)) { m_HasStateMove = true; break; }
            // Humanoid clips move the root through their body (muscle) curves.
            if (animator.Human is { } human && HumanRootMotion(animator, human)) return;
            if (!animator.m_ApplyRootMotion && !m_HasMonoMove && !m_HasStateMove) return;
            var t = animator.transform;
            int pos = Bindings.ChannelOf(t, "position"), rot = Bindings.ChannelOf(t, "rotation");
            for (int l = 0; l < count; l++)
            {
                var p = m_LayerPoses[l].AnimationPose;
                foreach (var w in new[] { p.Weight, p.Keep, p.Fill, p.Rest })
                {
                    if (pos >= 0) for (int c = pos; c < pos + 3 && c < w.Length; c++) w[c] = 0f;
                    if (rot >= 0) for (int c = rot; c < rot + 4 && c < w.Length; c++) w[c] = 0f;
                }
            }
            if (!animator.m_ApplyRootMotion && !m_HasMonoMove) return;
            var delta = Vector3.zero;
            var turn = Quaternion.identity;
            foreach (var (layer, layerIndex, run, clip, weight) in m_Contributions)
            {
                float w = weight * (layerIndex == 0 ? 1f : layer.Evaluated);
                if (w <= 0f) continue;
                var bound = Bindings.Bind(clip);
                double offset = clip.isLooping ? run.Def.CycleOffset + clip.m_CycleOffset : 0.0;
                var (d, q) = MotionDelta(bound, clip, run.Previous + offset, run.Normalized + offset, pos, rot);
                delta += d * w;
                turn *= Quaternion.Slerp(Quaternion.identity, q, w);
            }
            // Root motion scales in its own frame before applying the current world rotation.
            // TransformVector would apply a nonuniform parent's scale after the root's rotation.
            var scale = t.localScale;
            if (t.parent != null) scale = Vector3.Scale(scale, t.parent.lossyScale);
            animator.m_DeltaPosition = animator.m_MotionRootRotation * Vector3.Scale(scale, delta);
            animator.m_DeltaRotation = turn;
            MatchStep(animator, scale, pos, rot);
            if (m_RootDeltaTime != 0f)
            {
                animator.m_RootVelocity = animator.m_DeltaPosition / m_RootDeltaTime;
                animator.m_DeltaRotation.ToAngleAxis(out float angle, out var axis);
                if (angle > 180f) angle -= 360f;
                animator.m_RootAngularVelocity = animator.m_MotionRootRotation * axis * (angle * Mathf.Deg2Rad / m_RootDeltaTime);
            }
        }

        void ProcessRootMotion()
        {
            if (!Attached) return;
            var animator = m_Animator;
            animator.m_RootMotionReady = true;
            try
            {
                foreach (var mb in animator.gameObject.GetComponents<MonoBehaviour>())
                {
                    if (mb == null || !mb.isActiveAndEnabled) continue;
                    var method = MoveMethod(mb);
                    if (method == null) continue;
                    try { method.Invoke(mb, null); }
                    catch (TargetInvocationException e) { Debug.LogException(e.InnerException ?? e, mb); }
                }
                if (m_CallbacksStarted)
                    foreach (var state in MovingStates())
                    {
                        try
                        {
                            state.Behaviour.OnStateMove(animator, state.Info, state.Layer);
                            state.Behaviour.OnStateMove(animator, state.Info, state.Layer, Playable);
                        }
                        catch (Exception e) { Debug.LogException(e, animator); }
                    }
                if (animator.m_ApplyRootMotion && !m_HasMonoMove && !m_HasStateMove) animator.ApplyBuiltinRootMotion();
                // API setters inside movement callbacks do not write the generic root pose.
                animator.CaptureRootMotionPose();
            }
            finally
            {
                // ApplyBuiltinRootMotion only acts during movement callbacks, once per evaluation.
                animator.m_RootMotionReady = false;
            }
        }

        (Vector3, Quaternion) MotionDelta(BoundClip bound, AnimationClip clip, double from, double to, int pos, int rot)
        {
            if (!clip.isLooping || Math.Floor(from) == Math.Floor(to))
                return RootDelta(bound, ClipTime(clip, from, clip.isLooping), ClipTime(clip, to, clip.isLooping), pos, rot);
            if (to < from)
            {
                var (back, reverse) = MotionDelta(bound, clip, to, from, pos, rot);
                var inverse = Quaternion.Inverse(reverse);
                return (inverse * -back, inverse);
            }
            var (delta, turn) = RootDelta(bound, ClipTime(clip, from, true), clip.StartTime + clip.length, pos, rot);
            var (cycleDelta, cycleTurn) = RootDelta(bound, clip.StartTime, clip.StartTime + clip.length, pos, rot);
            long cycles = (long)Math.Floor(to) - (long)Math.Floor(from) - 1;
            while (cycles > 0)
            {
                if ((cycles & 1) != 0) { delta += turn * cycleDelta; turn *= cycleTurn; }
                cycleDelta += cycleTurn * cycleDelta;
                cycleTurn *= cycleTurn;
                cycles >>= 1;
            }
            var (lastDelta, lastTurn) = RootDelta(bound, clip.StartTime, ClipTime(clip, to, true), pos, rot);
            return (delta + turn * lastDelta, turn * lastTurn);
        }

        static float ClipTime(AnimationClip clip, double normalized, bool loop) => ClipSampling.ClipTime(clip, normalized, loop);

        internal bool CompletedNonLoopingBaseState(StateRun run) =>
            run.Normalized >= 1.0 && MotionOf(Layers[0].Def, run.Def) is AnimationClip clip
            && !Controller.MapClip(clip).isLooping;

        /// <summary>Target matching: once the base layer's state reaches the match's start, each
        /// evaluation moves the root by its motion plus the part of the remaining correction the
        /// step covers, (min(t, end) − t₀) / (end − t₀), the correction being the target less where
        /// the motion alone would bring the root by the end; rotations by the normalized lerp
        /// towards it. The step past the end completes the match.</summary>
        void MatchStep(Animator animator, Vector3 scale, int pos, int rot)
        {
            var match = animator.m_Match;
            var run = Layers.Length > 0 ? Layers[0].Current : null;
            if (match == null || run == null) return;
            if (match.Player != this || match.Run != run || Layers[0].Transition != null)
            {
                if (!match.CompleteOnInterruption) { animator.m_Match = null; return; }
                match.Finish = true;
                match.Player = this;
                match.Run = run;
            }
            double from = run.Previous, to = run.Normalized;
            if (to < match.Start || match.End <= from) { if (match.End <= from) animator.m_Match = null; return; }
            double f = match.Finish ? 1.0 : (Math.Min(to, match.End) - from) / (match.End - from);
            // Where the base layer's motion would bring the root by the end of the match.
            var toEnd = Vector3.zero;
            var turnToEnd = Quaternion.identity;
            foreach (var (_, layerIndex, contribution, clip, weight) in m_Contributions)
            {
                if (layerIndex != 0 || contribution != run || weight <= 0f) continue;
                double offset = clip.isLooping ? run.Def.CycleOffset + clip.m_CycleOffset : 0.0;
                var (d, q) = MotionDelta(Bindings.Bind(clip), clip, from + offset, match.End + offset, pos, rot);
                toEnd += d * weight;
                turnToEnd *= Quaternion.Slerp(Quaternion.identity, q, weight);
            }
            var position = animator.m_MotionRootPosition;
            var rotation = animator.m_MotionRootRotation;
            var predicted = position + rotation * Vector3.Scale(scale, toEnd);
            var turned = rotation * animator.m_DeltaRotation;
            var rootCorrection = Quaternion.Inverse(turned) * (match.Position - predicted);
            var correction = turned * Vector3.Scale(rootCorrection, match.Mask.positionXYZWeight) * (float)f;
            animator.m_DeltaPosition += correction;
            var remaining = match.Rotation * Quaternion.Inverse(rotation * turnToEnd);
            float g = (float)f * Mathf.Clamp01(match.Mask.rotationWeight);
            if (Quaternion.Dot(remaining, Quaternion.identity) < 0f) remaining = new Quaternion(-remaining.x, -remaining.y, -remaining.z, -remaining.w);
            var part = new Quaternion(remaining.x * g, remaining.y * g, remaining.z * g, 1f - g + remaining.w * g).normalized;
            animator.m_DeltaRotation = (Quaternion.Inverse(rotation) * (part * turned)).normalized;
            if (match.Finish || to >= match.End) animator.m_Match = null;
        }

        /// <summary>A clip's root movement from one time to another, in its root frame at the first.</summary>
        (Vector3, Quaternion) RootDelta(BoundClip bound, float t0, float t1, int pos, int rot)
        {
            var (p0, r0) = RootPose(bound, t0, pos, rot);
            var (p1, r1) = RootPose(bound, t1, pos, rot);
            var inverse = Quaternion.Inverse(r0);
            return (inverse * (p1 - p0), inverse * r1);
        }

        (Vector3, Quaternion) RootPose(BoundClip bound, float time, int pos, int rot)
        {
            // A root node's motion (MotionT/MotionQ) moves the root as its own curves would; the
            // node keeps its curves (measured, tests/projects/modelroot).
            if (bound.Clip.MotionCurves() != null) return bound.Clip.MotionPose(time);
            var pose = m_RootScratch ??= new AnimationPose();
            pose.Reset(Bindings.FloatCount, Bindings.ObjectCount);
            ClipSampling.Sample(bound, time, 1f, pose);
            var p = Vector3.zero;
            if (pos >= 0 && pose.Weight[pos] > 0f)
                p = new Vector3(pose.Value[pos] / pose.Weight[pos], pose.Value[pos + 1] / Math.Max(pose.Weight[pos + 1], 1e-6f), pose.Value[pos + 2] / Math.Max(pose.Weight[pos + 2], 1e-6f));
            var r = Quaternion.identity;
            if (rot >= 0 && pose.Weight[rot] > 0f)
            {
                r = new Quaternion(pose.Value[rot], pose.Value[rot + 1], pose.Value[rot + 2], pose.Value[rot + 3]);
                float n = Mathf.Sqrt(r.x * r.x + r.y * r.y + r.z * r.z + r.w * r.w);
                r = n > 1e-6f ? new Quaternion(r.x / n, r.y / n, r.z / n, r.w / n) : Quaternion.identity;
            }
            return (p, r);
        }


        /// <summary>A humanoid Animator's root motion: the clip's body motion between the state's
        /// previous and current times, as a delta of the root (applied like generic root motion) and
        /// the body placed relative to it for the pose. False when no humanoid clip plays.</summary>
        bool HumanRootMotion(Animator animator, Unbound.Runtime.Humanoid.AnimatorHuman human)
        {
            (StateRun Run, AnimationClip Clip, float Weight) found = default;
            int count = 0;
            foreach (var (layer, layerIndex, run, clip, weight) in m_Contributions)
            {
                float w = weight * (layerIndex == 0 ? 1f : layer.Evaluated);
                if (w <= 0f || !clip.HasHumanCurves) continue;
                count++;
                if (found.Clip == null || w > found.Weight) found = (run, clip, w);
            }
            human.ClearMotion();
            if (found.Clip == null) return false;
            if (count > 1 || found.Weight < 1f)
                Unbound.Runtime.Stubs.Ignored("Animator humanoid root motion blending",
                    "blended humanoid clips (transitions, several layers or blend trees) move the root with the strongest clip only");
            var motion = found.Clip.HumanClipMotion;
            var def = found.Run.Def;
            var input = new Unbound.Runtime.Humanoid.HumanClipMotionInput
            {
                Time = (float)found.Run.Normalized,
                PreviousTime = (float)found.Run.Previous,
                Speed = SpeedOf(def),
                CycleOffset = def.CycleOffset,
            };
            human.EvaluateMotion(found.Clip, motion, input, m_RootDeltaTime,
                animator.m_ApplyRootMotion || m_HasMonoMove, out var rm, out bool applied);
            if (!applied) return true;
            animator.m_DeltaPosition = new Vector3(rm.DeltaPosition.x, rm.DeltaPosition.y, rm.DeltaPosition.z);
            animator.m_DeltaRotation = new Quaternion(rm.DeltaRotation.x, rm.DeltaRotation.y, rm.DeltaRotation.z, rm.DeltaRotation.w);
            animator.m_RootVelocity = new Vector3(rm.Velocity.x, rm.Velocity.y, rm.Velocity.z);
            animator.m_RootAngularVelocity = new Vector3(rm.AngularVelocity.x, rm.AngularVelocity.y, rm.AngularVelocity.z);
            return true;
        }
    }
}
