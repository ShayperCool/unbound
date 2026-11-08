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
        internal float RootMotionDeltaTime => m_RootDeltaTime;
        bool m_CallbacksStarted;
        bool m_HasMonoMove, m_HasStateMove;
        Quaternion[] m_RootTurns = Array.Empty<Quaternion>();
        float[] m_RootTurnWeights = Array.Empty<float>();
        static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, bool> s_StateMove =
            new System.Collections.Concurrent.ConcurrentDictionary<Type, bool>();

        /// <summary>The layer whose current and next states a layer plays (its synced source).</summary>
        LayerRun PlayedBy(LayerRun layer) =>
            layer.Def.SyncedLayerIndex >= 0 && layer.Def.SyncedLayerIndex < Layers.Length ? Layers[layer.Def.SyncedLayerIndex] : layer;

        /// <summary>A behaviour of a state this update evaluated handles OnStateMove.</summary>
        bool AnyStateMove()
        {
            for (int i = 0; i < Layers.Length; i++)
            {
                var layer = Layers[i];
                var source = PlayedBy(layer);
                for (int k = 0; UpdateRun(source, k) is { } run; k++)
                    if (HandlesStateMove(layer, run)) return true;
            }
            return false;
        }

        bool HandlesStateMove(LayerRun layer, StateRun run)
        {
            if (run == null) return false;
            foreach (var b in BehavioursOf(layer, run.Def))
                if (b != null && HandlesStateMove(b)) return true;
            return false;
        }

        /// <summary>OnStateMove of the states this update evaluated, layer by layer.</summary>
        void InvokeStateMoves(Animator animator)
        {
            for (int i = 0; i < Layers.Length; i++)
            {
                var layer = Layers[i];
                var source = PlayedBy(layer);
                for (int k = 0; UpdateRun(source, k) is { } run; k++) InvokeStateMove(animator, layer, run, i);
            }
        }

        /// <summary>OnStateIK of the states the layer's update evaluated (its IK pass, after
        /// OnAnimatorIK; 6000.0.71f1, tests/projects/animcallbacks).</summary>
        internal void InvokeStateIK(Animator animator, int layerIndex)
        {
            if (layerIndex < 0 || layerIndex >= Layers.Length) return;
            var layer = Layers[layerIndex];
            var source = PlayedBy(layer);
            for (int k = 0; UpdateRun(source, k) is { } run; k++)
            {
                foreach (var b in BehavioursOf(layer, run.Def))
                {
                    if (b == null) continue;
                    try
                    {
                        var info = StateInfo(layer, run);
                        b.OnStateIK(animator, info, layerIndex);
                        b.OnStateIK(animator, info, layerIndex, Playable);
                    }
                    catch (Exception e) { Debug.LogException(e, animator); }
                }
            }
        }

        void InvokeStateMove(Animator animator, LayerRun layer, StateRun run, int layerIndex)
        {
            if (run == null) return;
            foreach (var b in BehavioursOf(layer, run.Def))
            {
                if (b == null) continue;
                try
                {
                    var info = StateInfo(layer, run);
                    b.OnStateMove(animator, info, layerIndex);
                    b.OnStateMove(animator, info, layerIndex, Playable);
                }
                catch (Exception e) { Debug.LogException(e, animator); }
            }
        }

        static void ClearRootWeights(float[] w, int pos, int rot)
        {
            if (pos >= 0) for (int c = pos; c < pos + 3 && c < w.Length; c++) w[c] = 0f;
            if (rot >= 0) for (int c = rot; c < rot + 4 && c < w.Length; c++) w[c] = 0f;
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
            m_HasMonoMove = MoveCallbacks.Any(animator);
            m_HasStateMove = AnyStateMove();
            // Humanoid clips move the root through their body (muscle) curves.
            if (animator.Human is { } human && HumanRootMotion(animator, human)) return;
            if (!animator.m_ApplyRootMotion && !m_HasMonoMove && !m_HasStateMove) return;
            var t = animator.transform;
            int pos = Bindings.ChannelOf(t, "position"), rot = Bindings.ChannelOf(t, "rotation");
            for (int l = 0; l < count; l++)
            {
                var p = m_LayerPoses[l].AnimationPose;
                ClearRootWeights(p.Weight, pos, rot);
                ClearRootWeights(p.Keep, pos, rot);
                ClearRootWeights(p.Fill, pos, rot);
                ClearRootWeights(p.Rest, pos, rot);
            }
            if (!animator.m_ApplyRootMotion && !m_HasMonoMove) return;
            var (delta, turn) = ContributionMotion(pos, rot);
            // Root motion scales in its own frame before applying the current world rotation.
            // TransformVector would apply a nonuniform parent's scale after the root's rotation.
            var scale = RootMotionScale(t);
            animator.m_DeltaPosition = animator.m_MotionRootRotation * Vector3.Scale(scale, delta);
            animator.m_DeltaRotation = turn;
            MatchStep(animator, scale, pos, rot);
            RootVelocities(animator.m_DeltaPosition, animator.m_DeltaRotation, animator.m_MotionRootRotation, m_RootDeltaTime,
                out animator.m_RootVelocity, out animator.m_RootAngularVelocity);
        }

        static Vector3 RootMotionScale(Transform t)
        {
            var scale = t.localScale;
            if (t.parent != null) scale = Vector3.Scale(scale, t.parent.lossyScale);
            return scale;
        }

        /// <summary>Per-second root motion: the world delta over the step, and the delta rotation's
        /// angle (the short way) about its axis turned into world space.</summary>
        static void RootVelocities(Vector3 deltaPosition, Quaternion deltaRotation, Quaternion rootRotation, float deltaTime,
            out Vector3 velocity, out Vector3 angularVelocity)
        {
            velocity = Vector3.zero;
            angularVelocity = Vector3.zero;
            if (deltaTime == 0f) return;
            velocity = deltaPosition / deltaTime;
            deltaRotation.ToAngleAxis(out float angle, out var axis);
            if (angle > 180f) angle -= 360f;
            angularVelocity = rootRotation * axis * (angle * Mathf.Deg2Rad / deltaTime);
        }

        /// <summary>The root's movement over the step from the layers' contributions, in the root's
        /// own frame: positions summed by weight, rotations blended per layer and composed.</summary>
        (Vector3 Delta, Quaternion Turn) ContributionMotion(int pos, int rot)
        {
            var delta = Vector3.zero;
            var turn = Quaternion.identity;
            if (m_RootTurns.Length != Layers.Length)
            {
                m_RootTurns = new Quaternion[Layers.Length];
                m_RootTurnWeights = new float[Layers.Length];
            }
            Array.Clear(m_RootTurns);
            Array.Clear(m_RootTurnWeights);
            foreach (var (layer, layerIndex, run, clip, weight) in m_Contributions)
            {
                float w = weight * (layerIndex == 0 ? 1f : layer.Evaluated);
                if (w <= 0f) continue;
                var bound = Bindings.Bind(clip);
                double offset = clip.isLooping ? run.Def.CycleOffset + clip.m_CycleOffset : 0.0;
                var (d, q) = MotionDelta(bound, clip, (run.MotionPrevious ?? run.Previous) + offset, run.Normalized + offset, pos, rot);
                delta += d * w;
                // States blend their rotation deltas in a layer; they do not compose them.
                var sum = m_RootTurns[layerIndex];
                if (Quaternion.Dot(sum, q) < 0) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                m_RootTurns[layerIndex] = new Quaternion(sum.x + q.x * weight, sum.y + q.y * weight,
                    sum.z + q.z * weight, sum.w + q.w * weight);
                m_RootTurnWeights[layerIndex] += weight;
            }
            for (int i = 0; i < Layers.Length; i++)
            {
                float weight = m_RootTurnWeights[i];
                if (weight <= 0) continue;
                var sum = m_RootTurns[i];
                // An empty state's remaining weight contributes no rotation.
                if (weight < 1) sum.w += sum.w < 0 ? weight - 1 : 1 - weight;
                turn *= Quaternion.Slerp(Quaternion.identity, sum.normalized, i == 0 ? 1 : Layers[i].Evaluated);
            }
            return (delta, turn);
        }

        /// <summary>A controller playable's root motion as an animation stream reports it
        /// (AnimationStream.velocity and angularVelocity): the contribution deltas of the evaluation
        /// that just built this player's layer poses, converted as the Animator's own root motion is,
        /// over the stream's delta time. Nothing moves and target matching does not take part. Zero
        /// motion when the Animator requests none (applyRootMotion off and no OnAnimatorMove).
        /// <paramref name="start"/>/<paramref name="startRotation"/> are the root's current pose in
        /// the root motion convention of <see cref="Animator.CaptureRootMotionPose"/>. False when
        /// humanoid clips play: their stream root motion is not computed.</summary>
        internal bool StreamRootMotion(float deltaTime, out Vector3 start, out Quaternion startRotation,
            out Vector3 velocity, out Vector3 angularVelocity)
        {
            velocity = Vector3.zero;
            angularVelocity = Vector3.zero;
            var animator = m_Animator;
            var t = animator.transform;
            var parent = t.parent;
            // The convention of Animator.SyncRootPose (the animation stream's root composition).
            start = parent == null ? t.localPosition
                : parent.position + parent.rotation * Vector3.Scale(parent.lossyScale, t.localPosition);
            startRotation = parent == null ? t.localRotation : parent.rotation * t.localRotation;
            foreach (var (_, _, _, clip, _) in m_Contributions)
                if (clip.HasHumanCurves) return false;
            if (!animator.m_ApplyRootMotion && !MoveCallbacks.Any(animator)) return true;
            int pos = Bindings.ChannelOf(t, "position"), rot = Bindings.ChannelOf(t, "rotation");
            var (delta, turn) = ContributionMotion(pos, rot);
            RootVelocities(startRotation * Vector3.Scale(RootMotionScale(t), delta), turn, startRotation, deltaTime,
                out velocity, out angularVelocity);
            return true;
        }

        /// <summary>A clip of the evaluation that just built this player's layer poses has a root
        /// node's motion (MotionT/MotionQ curves), which moves the root without the root's own
        /// channels showing it (for a controller playable in a layer mixer's layer).</summary>
        internal bool SampledRootNodeMotion()
        {
            foreach (var (_, _, _, clip, _) in m_Contributions)
                if (clip.MotionCurves() != null) return true;
            return false;
        }

        internal void ProcessRootMotion()
        {
            if (!Attached) return;
            var animator = m_Animator;
            animator.m_RootMotionReady = true;
            try
            {
                MoveCallbacks.Invoke(animator);
                if (m_CallbacksStarted) InvokeStateMoves(animator);
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
            double from = run.MotionPrevious ?? run.Previous, to = run.Normalized;
            if (to < match.Start) return;
            if (match.End <= from)
            {
                if (match.Finish || to > match.End) animator.m_Match = null;
                return;
            }
            double f = match.Finish ? 1.0 : (Math.Min(to, match.End) - from) / (match.End - from);
            // Where the base layer's motion would bring the root by the end of the match.
            var toEnd = Vector3.zero;
            var turnToEnd = Quaternion.identity;
            foreach (var (_, layerIndex, contribution, clip, weight) in m_Contributions)
            {
                if (layerIndex != 0 || contribution != run || weight <= 0f) continue;
                double offset = clip.isLooping ? run.Def.CycleOffset + clip.m_CycleOffset : 0.0;
                var (d, q) = MotionDelta(Bindings.Bind(clip), clip, from + offset,
                    (match.Finish || match.PredictCurrentPose ? to : match.End) + offset, pos, rot);
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
            if (match.Finish || to > match.End) animator.m_Match = null;
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


        readonly List<Unbound.Runtime.Humanoid.HumanRootMotionSample> m_HumanSamples = new List<Unbound.Runtime.Humanoid.HumanRootMotionSample>();
        readonly List<float> m_HumanWeights = new List<float>();
        readonly List<Unbound.Runtime.Humanoid.Trs> m_HumanBodyDifferences = new List<Unbound.Runtime.Humanoid.Trs>();

        /// <summary>A humanoid Animator's root motion: each humanoid clip's body motion between its
        /// state's previous and current times, blended by the clips' weights in each layer (the
        /// states of a transition, a blend tree's clips); override layers move it towards theirs by
        /// the layer weight, additive layers leave it (measured, tests/projects/humanoid) and compose
        /// their body differences onto the body below instead; layer masks filter both
        /// (<see cref="Unbound.Runtime.Humanoid.HumanRootMotion.CombineLayer"/>). It becomes a delta of the root
        /// (applied like generic root motion) and places the body relative to it for the pose.
        /// False when no humanoid clip plays.</summary>
        bool HumanRootMotion(Animator animator, Unbound.Runtime.Humanoid.AnimatorHuman human)
        {
            human.ClearMotion();
            Unbound.Runtime.Humanoid.HumanRootMotionSample blended = default;
            bool any = false;
            for (int i = 0; i < Layers.Length; i++)
            {
                m_HumanSamples.Clear();
                m_HumanWeights.Clear();
                m_HumanBodyDifferences.Clear();
                bool additive = Layers[i].Def.Additive;
                foreach (var (layer, layerIndex, run, clip, weight) in m_Contributions)
                {
                    if (layerIndex != i || weight <= 0f || !clip.HasHumanCurves) continue;
                    var def = run.Def;
                    var input = new Unbound.Runtime.Humanoid.HumanClipMotionInput
                    {
                        Time = (float)run.Normalized,
                        // A state entered in this update steps the whole update (see Enter).
                        PreviousTime = (float)(run.MotionPrevious ?? run.Previous),
                        Speed = SpeedOf(def),
                        CycleOffset = def.CycleOffset + (float)MirrorShift(def, clip),
                    };
                    if (additive)
                    {
                        // Additive layers leave the root motion alone; only their body differences apply.
                        if (!Unbound.Runtime.Humanoid.HumanRootMotion.HasRoot(clip.HumanPresent)) continue;
                        m_HumanBodyDifferences.Add(human.AdditiveBodyDifference(clip, clip.HumanClipMotion, input));
                        m_HumanWeights.Add(weight);
                        continue;
                    }
                    human.SampleMotion(clip, clip.HumanClipMotion, input, out var sample);
                    m_HumanSamples.Add(sample);
                    m_HumanWeights.Add(weight);
                }
                float layerWeight = i == 0 ? 1f : Layers[i].Evaluated;
                var mask = Layers[i].Def.Mask;
                Unbound.Runtime.Humanoid.HumanRootMotion.CombineLayer(ref blended, ref any,
                    System.Runtime.InteropServices.CollectionsMarshal.AsSpan(m_HumanSamples),
                    System.Runtime.InteropServices.CollectionsMarshal.AsSpan(m_HumanBodyDifferences),
                    System.Runtime.InteropServices.CollectionsMarshal.AsSpan(m_HumanWeights), additive, layerWeight,
                    mask != null && mask.MasksHumanoidParts ? mask.m_HumanoidParts : null);
            }
            if (!any) return false;
            human.ApplyMotion(blended, m_RootDeltaTime, out var rm);
            if (!animator.m_ApplyRootMotion && !m_HasMonoMove) return true;
            animator.m_DeltaPosition = new Vector3(rm.DeltaPosition.x, rm.DeltaPosition.y, rm.DeltaPosition.z);
            animator.m_DeltaRotation = new Quaternion(rm.DeltaRotation.x, rm.DeltaRotation.y, rm.DeltaRotation.z, rm.DeltaRotation.w);
            animator.m_RootVelocity = new Vector3(rm.Velocity.x, rm.Velocity.y, rm.Velocity.z);
            animator.m_RootAngularVelocity = new Vector3(rm.AngularVelocity.x, rm.AngularVelocity.y, rm.AngularVelocity.z);
            return true;
        }
    }
}
