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
        Vector3[] m_RootDeltas = Array.Empty<Vector3>();
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
            var human = animator.Human;
            if (human != null && HumanRootMotion(animator, human)) return;
            var t = animator.transform;
            int pos = Bindings.ChannelOf(t, "position"), rot = Bindings.ChannelOf(t, "rotation");
            if (!animator.m_ApplyRootMotion && !m_HasMonoMove && !m_HasStateMove)
            {
                KeepRootSteps(pos, rot);
                return;
            }
            for (int l = 0; l < count; l++)
            {
                var p = m_LayerPoses[l].AnimationPose;
                ClearRootWeights(p.Weight, pos, rot);
                ClearRootWeights(p.Keep, pos, rot);
                ClearRootWeights(p.Fill, pos, rot);
                ClearRootWeights(p.Rest, pos, rot);
            }
            if (!animator.m_ApplyRootMotion && !m_HasMonoMove)
            {
                KeepRootSteps(pos, rot);
                return;
            }
            var (delta, turn) = ContributionMotion(pos, rot);
            // Root motion scales in its own frame before applying the current world rotation.
            // TransformVector would apply a nonuniform parent's scale after the root's rotation.
            var scale = RootMotionScale(t);
            // A humanoid Animator moves by its human scale, and its pivot is the moved root
            // (Unity 6000.0.71f1, tests/projects/humanoid noRootOverGeneric).
            if (human != null) delta *= animator.humanScale;
            animator.m_DeltaPosition = animator.m_MotionRootRotation * Vector3.Scale(scale, delta);
            animator.m_DeltaRotation = turn;
            MatchStep(animator, scale, pos, rot);
            if (human != null)
            {
                var moved = animator.m_MotionRootPosition + animator.m_DeltaPosition;
                human.PivotPosition = new Unbound.Runtime.Humanoid.F4(moved.x, moved.y, moved.z, 0f);
            }
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
        /// own frame: each layer's states sum their positions and blend their rotations by weight
        /// (a holding state takes the layer's last step, see <see cref="HoldWeight"/>), then the
        /// layers add their positions by layer weight and compose their rotations.</summary>
        (Vector3 Delta, Quaternion Turn) ContributionMotion(int pos, int rot)
        {
            var delta = Vector3.zero;
            var turn = Quaternion.identity;
            if (m_RootTurns.Length != Layers.Length)
            {
                m_RootTurns = new Quaternion[Layers.Length];
                m_RootTurnWeights = new float[Layers.Length];
                m_RootDeltas = new Vector3[Layers.Length];
            }
            Array.Clear(m_RootTurns);
            Array.Clear(m_RootTurnWeights);
            Array.Clear(m_RootDeltas);
            foreach (var (layer, layerIndex, run, clip, weight, _) in m_Contributions)
            {
                // A layer of weight 0 moves nothing; its step only matters to a state that may hold it.
                if (weight <= 0f || (!m_MayHold && layerIndex > 0 && layer.Evaluated <= 0f)) continue;
                var bound = Bindings.Bind(clip);
                double offset = clip.isLooping ? run.Def.CycleOffset + clip.m_CycleOffset : 0.0;
                var (d, q) = MotionDelta(bound, clip, ClipSampling.StateClipNormalized(run.MotionPrevious ?? run.Previous, clip.length) + offset,
                    ClipSampling.StateClipNormalized(run.Normalized, clip.length) + offset, pos, rot);
                AddRootStep(layerIndex, d, q, weight);
            }
            for (int i = 0; i < Layers.Length; i++)
            {
                var layer = Layers[i];
                float hold = HoldWeight(i, out bool frozen);
                if (hold > 0f && layer.Kept.HasStep) AddRootStep(i, layer.Kept.Delta, layer.Kept.Turn, hold);
                float weight = m_RootTurnWeights[i];
                if (!frozen) layer.Own.HasStep = weight > 0f;
                if (weight <= 0) continue;
                var sum = m_RootTurns[i];
                // An empty state's remaining weight contributes no rotation.
                if (weight < 1) sum.w += sum.w < 0 ? weight - 1 : 1 - weight;
                var own = sum.normalized;
                if (!frozen)
                {
                    layer.Own.Delta = m_RootDeltas[i];
                    layer.Own.Turn = own;
                }
                float layerWeight = i == 0 ? 1f : layer.Evaluated;
                if (layerWeight <= 0f) continue;
                delta += m_RootDeltas[i] * layerWeight;
                turn *= Quaternion.Slerp(Quaternion.identity, own, i == 0 ? 1 : layerWeight);
            }
            return (delta, turn);
        }

        /// <summary>An evaluation that requests no root motion still keeps the layers' own steps
        /// for their holding states (see <see cref="HoldWeight"/>).</summary>
        void KeepRootSteps(int pos, int rot)
        {
            if (m_MayHold) ContributionMotion(pos, rot);
        }

        /// <summary>A state's step in its layer: its position by weight, its rotation into the
        /// layer's weighted sum (the same hemisphere).</summary>
        void AddRootStep(int layerIndex, Vector3 d, Quaternion q, float weight)
        {
            m_RootDeltas[layerIndex] += d * weight;
            var sum = m_RootTurns[layerIndex];
            if (Quaternion.Dot(sum, q) < 0) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
            m_RootTurns[layerIndex] = new Quaternion(sum.x + q.x * weight, sum.y + q.y * weight,
                sum.z + q.z * weight, sum.w + q.w * weight);
            m_RootTurnWeights[layerIndex] += weight;
        }

        /// <summary>
        /// The weight of a layer's states that hold (without a motion and without write defaults):
        /// 1 alone, their share of a transition. Such a state keeps the layer's last root motion as
        /// it keeps the layer's last values (see <see cref="SampleState"/>): the layer's own step of
        /// its last update with clips, generic or humanoid, takes the holding state's weight, so
        /// alone it moves the root by the same step in the root's frame every update, whatever the
        /// delta time, an Update(0) included (6000.0.71f1, tests/projects/humanoid generic.noWD,
        /// generic.noWDBack, noWDAlone, noWDFade, noWDBack, noRootNoWD; a humanoid's feet pivot
        /// correction runs again on the kept step). The layer's step becomes its last one, except
        /// while a transition leaves a holding state (<paramref name="frozen"/>), which keeps what it
        /// held when the transition began, as for its values; an update without clips in the layer
        /// forgets it. The step is kept also while no root motion is requested (Unbound's choice,
        /// as for humanoid samples; root motion turned on during a hold is unmeasured).
        /// </summary>
        float HoldWeight(int i, out bool frozen)
        {
            var layer = Layers[i];
            int synced = layer.Def.SyncedLayerIndex;
            var source = synced >= 0 && synced < Layers.Length ? Layers[synced] : layer;
            bool transitioning = source.Next != null && source.Transition != null;
            float p = transitioning ? source.Transition.Progress : 0f;
            float hold = 0f;
            if (Holds(layer, source.Current)) hold += 1f - p;
            if (transitioning && Holds(layer, source.Next)) hold += p;
            frozen = transitioning && Holds(layer, source.Current);
            return hold;
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
            foreach (var (_, _, _, clip, _, _) in m_Contributions)
                if (clip.HasHumanCurves && !clip.BodyMovesRoot(animator)) return false;
            int pos = Bindings.ChannelOf(t, "position"), rot = Bindings.ChannelOf(t, "rotation");
            if (!animator.m_ApplyRootMotion && !MoveCallbacks.Any(animator))
            {
                KeepRootSteps(pos, rot);
                return true;
            }
            var (delta, turn) = ContributionMotion(pos, rot);
            RootVelocities(startRotation * Vector3.Scale(RootMotionScale(t), delta), turn, startRotation, deltaTime,
                out velocity, out angularVelocity);
            return true;
        }

        /// <summary>A clip of the evaluation that just built this player's layer poses has a root
        /// node's motion (MotionT/MotionQ curves) or body curves that move the root, which moves
        /// it without the root's own channels showing it (for a controller playable).</summary>
        internal bool SampledRootNodeMotion()
        {
            foreach (var (_, _, _, clip, _, _) in m_Contributions)
                if (clip.MotionCurves() != null || clip.BodyMovesRoot(m_Animator)) return true;
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

        /// <summary>Whether the base layer state's motion loops (AnimatorStateInfo.loop).</summary>
        internal bool BaseStateLoops(StateRun run) => MotionLoops(MotionOf(Layers[0].Def, run.Def));

        /// <summary>The match's step this evaluation: whether the base layer's state is past the
        /// match's start, its previous and current normalized times, and the share of the remaining
        /// correction the step covers, (min(t, end) − t₀) / (end − t₀) (1 when finishing). A
        /// transition or another state interrupts the match: it finishes now or is cancelled as
        /// MatchTarget's completeMatch asked. A step that reaches the end completes the match.</summary>
        bool MatchProgress(Animator animator, out Animator.MatchState match, out double from, out double to, out float share)
        {
            match = animator.m_Match;
            from = to = 0.0;
            share = 0f;
            var run = Layers.Length > 0 ? Layers[0].Current : null;
            if (match == null || run == null) return false;
            if (match.Player != this || match.Run != run || Layers[0].Transition != null)
            {
                if (!match.CompleteOnInterruption) { animator.m_Match = null; return false; }
                match.Finish = true;
                match.Player = this;
                match.Run = run;
            }
            from = run.MotionPrevious ?? run.Previous;
            to = run.Normalized;
            if (to < match.Start) return false;
            if (match.End <= from)
            {
                if (match.Finish || MatchEnds(match, to)) animator.m_Match = null;
                return false;
            }
            share = match.Finish ? 1f : (float)((Math.Min(to, match.End) - from) / (match.End - from));
            return true;
        }

        /// <summary>The match is complete once the state's time passes its end
        /// (tests/projects/modelroot matchReview.timing, tests/projects/humanoid match.onceLate).</summary>
        static bool MatchEnds(Animator.MatchState match, double to) => to > match.End;

        /// <summary>Target matching of a generic rig: every target is the root, which the base
        /// layer's motion would bring from the previous time to the end of the match (the goals with
        /// their orientation offset).</summary>
        void MatchStep(Animator animator, Vector3 scale, int pos, int rot)
        {
            if (!MatchProgress(animator, out var match, out double from, out double to, out float share)) return;
            var run = Layers[0].Current;
            // Where the base layer's motion would bring the root by the end of the match.
            var toEnd = Vector3.zero;
            var turnToEnd = Quaternion.identity;
            foreach (var (_, layerIndex, contribution, clip, weight, _) in m_Contributions)
            {
                if (layerIndex != 0 || contribution != run || weight <= 0f) continue;
                double offset = clip.isLooping ? run.Def.CycleOffset + clip.m_CycleOffset : 0.0;
                var (d, q) = MotionDelta(Bindings.Bind(clip), clip, ClipSampling.StateClipNormalized(from, clip.length) + offset,
                    ClipSampling.StateClipNormalized(match.Finish || match.PredictCurrentPose ? to : match.End, clip.length) + offset, pos, rot);
                toEnd += d * weight;
                turnToEnd *= Quaternion.Slerp(Quaternion.identity, q, weight);
            }
            var rotation = animator.m_MotionRootRotation;
            // A goal target keeps the goals' orientation offset (tests/projects/humanoid match.genericLeftHand).
            var goalFrame = Quaternion.identity;
            if (match.Target >= AvatarTarget.LeftFoot)
            {
                var o = Unbound.Runtime.Humanoid.HumanIK.GoalOrientationOffset[(int)match.Target - (int)AvatarTarget.LeftFoot];
                goalFrame = new Quaternion(o.x, o.y, o.z, o.w);
            }
            ApplyMatch(animator, match, share, animator.m_MotionRootPosition + rotation * Vector3.Scale(scale, toEnd), rotation * turnToEnd * goalFrame);
            if (match.Finish || MatchEnds(match, to)) animator.m_Match = null;
        }

        /// <summary>Target matching of a humanoid rig: the target body part as the clips would place it
        /// at the end of the match (each clip at that normalized time, also a loop apart from the
        /// base layer) relative to their motion now, blended as their root motion is (by weight
        /// within a layer; layers whose mask keeps Root lerp towards theirs by their weight), from the
        /// root after this step (tests/projects/humanoid match.*, match.layers, match.layersLoop).</summary>
        void HumanMatchStep(Animator animator, Unbound.Runtime.Humanoid.AnimatorHuman human, Vector3 scale)
        {
            if (!MatchProgress(animator, out var match, out _, out double to, out float share)) return;
            var t = Vector3.zero;
            var q = Quaternion.identity;
            if (match.Finish)
            {
                // Finishing early: the part in the previous evaluation's pose, its position without
                // the human scale (tests/projects/humanoid match.interrupt, match.interruptB,
                // match.transition).
                var x = human.PreviousPosePart((int)match.Target);
                ApplyMatch(animator, match, share, animator.m_MotionRootPosition + animator.m_DeltaPosition
                    + animator.m_MotionRootRotation * animator.m_DeltaRotation * Vector3.Scale(scale, new Vector3(x.t.x, x.t.y, x.t.z)),
                    animator.m_MotionRootRotation * animator.m_DeltaRotation * new Quaternion(x.q.x, x.q.y, x.q.z, x.q.w));
                animator.m_Match = null;
                RootVelocities(animator.m_DeltaPosition, animator.m_DeltaRotation, animator.m_MotionRootRotation, m_RootDeltaTime,
                    out animator.m_RootVelocity, out animator.m_RootAngularVelocity);
                return;
            }
            // From no part (the identity), each layer moves the part towards its own by its weight.
            for (int i = 0; i < Layers.Length; i++)
            {
                var def = Layers[i].Def;
                float layerWeight = i == 0 ? 1f : Layers[i].Evaluated;
                if (def.Additive || layerWeight <= 0f || def.Mask != null && def.Mask.MasksHumanoidParts && !def.Mask.m_HumanoidParts[0])
                    continue;
                if (!LayerMatchPart(human, match, i, to, out var lt, out var lq)) continue;
                if (layerWeight >= 1f) { t = lt; q = lq; }
                else
                {
                    t = Vector3.Lerp(t, lt, layerWeight);
                    if (Quaternion.Dot(q, lq) < 0f) lq = new Quaternion(-lq.x, -lq.y, -lq.z, -lq.w);
                    q = new Quaternion(q.x + (lq.x - q.x) * layerWeight, q.y + (lq.y - q.y) * layerWeight, q.z + (lq.z - q.z) * layerWeight,
                        q.w + (lq.w - q.w) * layerWeight).normalized;
                }
            }
            var turned = animator.m_MotionRootRotation * animator.m_DeltaRotation;
            var moved = animator.m_MotionRootPosition + animator.m_DeltaPosition;
            ApplyMatch(animator, match, share, moved + turned * Vector3.Scale(scale, t * animator.humanScale), turned * q);
            if (MatchEnds(match, to)) animator.m_Match = null;
            RootVelocities(animator.m_DeltaPosition, animator.m_DeltaRotation, animator.m_MotionRootRotation, m_RootDeltaTime,
                out animator.m_RootVelocity, out animator.m_RootAngularVelocity);
        }

        /// <summary>One layer's humanoid clips' target part (<see cref="HumanMatchStep"/>), averaged by
        /// their weights; false without humanoid clips. Each clip goes from its own normalized time
        /// to the match's end.</summary>
        bool LayerMatchPart(Unbound.Runtime.Humanoid.AnimatorHuman human, Animator.MatchState match, int layer, double baseTime,
            out Vector3 t, out Quaternion q)
        {
            t = Vector3.zero;
            q = new Quaternion(0f, 0f, 0f, 0f);
            float total = 0f;
            foreach (var (_, layerIndex, run, clip, weight, _) in m_Contributions)
            {
                if (layerIndex != layer || weight <= 0f || !clip.HasHumanCurves) continue;
                total += weight;
                double end = match.PredictCurrentPose ? baseTime : match.End;
                var input = new Unbound.Runtime.Humanoid.HumanClipMotionInput
                {
                    Time = (float)ClipSampling.StateClipNormalized(end, clip.length),
                    PreviousTime = (float)ClipSampling.StateClipNormalized(run.Normalized, clip.length),
                    Speed = SpeedOf(run.Def),
                    CycleOffset = run.Def.CycleOffset + (float)MirrorShift(run.Def, clip),
                };
                var x = human.MatchPart(clip, clip.HumanClipMotion, input, (int)match.Target);
                t += new Vector3(x.t.x, x.t.y, x.t.z) * weight;
                var xq = new Quaternion(x.q.x, x.q.y, x.q.z, x.q.w);
                float w = Quaternion.Dot(q, xq) < 0f ? -weight : weight;
                q = new Quaternion(q.x + xq.x * w, q.y + xq.y * w, q.z + xq.z * w, q.w + xq.w * w);
            }
            if (total <= 0f) return false;
            t /= total;
            q = q.normalized;
            return true;
        }

        /// <summary>Moves the step's root motion by the share of the remaining correction: the target
        /// position less where the target part would end without it, masked per axis in the root's
        /// frame after the step, and the normalized lerp from the identity towards the remaining turn
        /// by the share times the rotation weight.</summary>
        static void ApplyMatch(Animator animator, Animator.MatchState match, float share, Vector3 predicted, Quaternion predictedRotation)
        {
            var rotation = animator.m_MotionRootRotation;
            var turned = rotation * animator.m_DeltaRotation;
            var rootCorrection = Quaternion.Inverse(turned) * (match.Position - predicted);
            animator.m_DeltaPosition += turned * Vector3.Scale(rootCorrection, match.Mask.positionXYZWeight) * share;
            var remaining = match.Rotation * Quaternion.Inverse(predictedRotation);
            float g = share * Mathf.Clamp01(match.Mask.rotationWeight);
            if (Quaternion.Dot(remaining, Quaternion.identity) < 0f) remaining = new Quaternion(-remaining.x, -remaining.y, -remaining.z, -remaining.w);
            var part = new Quaternion(remaining.x * g, remaining.y * g, remaining.z * g, 1f - g + remaining.w * g).normalized;
            animator.m_DeltaRotation = (Quaternion.Inverse(rotation) * (part * turned)).normalized;
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
            // Without a human, body curves (RootT/RootQ) move the root as a humanoid body's motion
            // would, without human scale or feet pivot; the root's own curves take no part
            // (Unity 6000.0.71f1, tests/projects/rootcurves).
            if (bound.Clip.BodyMovesRoot(m_Animator)) return bound.Clip.BodyMotionPose(time);
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

        readonly List<Unbound.Runtime.Humanoid.HumanRootMotionSample> m_StateSamples = new List<Unbound.Runtime.Humanoid.HumanRootMotionSample>();
        readonly List<float> m_StateShares = new List<float>();

        /// <summary>One state's humanoid clips join the layer's samples as one: a state's clips (a
        /// blend tree's) blend among themselves first, each by its weight within the state, as an
        /// AnimationMixerPlayable blends its inputs (<see cref="Unbound.Runtime.Humanoid.HumanRootMotion.Blend"/>
        /// with sum): every part is the sum of the clips' by weight, a rotation normalized, and
        /// where the weights do not fill the state the identity takes the rest. The layer then
        /// blends its states by their weights, so the rotations of a tree's blend are normalized
        /// before a transition blends the tree with another state. Matches the Unity 6000.0.71f1
        /// Editor, tests/projects/humanoid goals.treeTransition, goals.treeSource (trees in
        /// transitions) and body.direct* (Direct blend trees whose weights sum to 0.7 and 1.4:
        /// the body is 0.7 and 1.4 times as far from the root, body.directLow and
        /// body.directHigh; an empty child tree leaves its weight to the identity, in an Editor
        /// capture of the probe's HumanBodyDirectEmptyChild controller, not in the reference).</summary>
        void AddStateMotion(float stateWeight)
        {
            if (m_StateSamples.Count == 0) return;
            m_HumanSamples.Add(Unbound.Runtime.Humanoid.HumanRootMotion.Blend(
                System.Runtime.InteropServices.CollectionsMarshal.AsSpan(m_StateSamples),
                System.Runtime.InteropServices.CollectionsMarshal.AsSpan(m_StateShares), sum: true));
            m_HumanWeights.Add(stateWeight);
            m_StateSamples.Clear();
            m_StateShares.Clear();
        }

        /// <summary>A layer's holding states keep its last humanoid root motion (see
        /// <see cref="HoldWeight"/>): the layer's own blended sample.</summary>
        void HoldHumanMotion(int i)
        {
            var layer = Layers[i];
            float hold = HoldWeight(i, out bool frozen);
            if (hold > 0f && layer.Kept.HasHuman)
            {
                m_HumanSamples.Add(layer.Kept.Human);
                m_HumanWeights.Add(hold);
            }
            if (frozen) return;
            layer.Own.HasHuman = m_HumanSamples.Count > 0;
            if (layer.Own.HasHuman)
                layer.Own.Human = Unbound.Runtime.Humanoid.HumanRootMotion.Blend(
                    System.Runtime.InteropServices.CollectionsMarshal.AsSpan(m_HumanSamples),
                    System.Runtime.InteropServices.CollectionsMarshal.AsSpan(m_HumanWeights));
        }

        /// <summary>A humanoid Animator's root motion: each humanoid clip's body motion between its
        /// state's previous and current times as the clip reads them
        /// (<see cref="ClipSampling.StateClipNormalized"/>), blended within each state (a blend tree's clips,
        /// <see cref="AddStateMotion"/>) and then by the states' weights in each layer (a
        /// transition's); override layers move it towards theirs by
        /// the layer weight, additive layers leave it (measured, tests/projects/humanoid) and compose
        /// their body differences onto the body below instead; layer masks filter both
        /// (<see cref="Unbound.Runtime.Humanoid.HumanRootMotion.CombineLayer"/>). It becomes a delta of the root
        /// (applied like generic root motion) and places the body relative to it for the pose.
        /// False when no humanoid clip plays; the feet pivot then rests at the root
        /// (<see cref="Unbound.Runtime.Humanoid.HumanRootMotion.Idle"/>).</summary>
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
                StateRun state = null;
                float stateWeight = 0f;
                bool additive = Layers[i].Def.Additive;
                foreach (var (layer, layerIndex, run, clip, weight, sampled) in m_Contributions)
                {
                    if (layerIndex != i || weight <= 0f || !clip.HasHumanCurves) continue;
                    if (additive)
                    {
                        // Additive layers leave the root motion alone; only their body differences apply.
                        if (!Unbound.Runtime.Humanoid.HumanRootMotion.HasRoot(clip.HumanPresent)) continue;
                        m_HumanBodyDifferences.Add(human.AdditiveBodyDifference(clip, clip.HumanClipMotion, HumanInput(run, clip)));
                        m_HumanWeights.Add(weight);
                        continue;
                    }
                    // Sampled with the clip's pose, which took its body and placed its goals by it.
                    if (sampled < 0) continue;
                    float share = m_HumanClipShares[sampled];
                    if (run != state)
                    {
                        AddStateMotion(stateWeight);
                        state = run;
                        stateWeight = weight / share;
                    }
                    m_StateSamples.Add(m_HumanClipSamples[sampled]);
                    m_StateShares.Add(share);
                }
                AddStateMotion(stateWeight);
                if (!additive) HoldHumanMotion(i);
                float layerWeight = i == 0 ? 1f : Layers[i].Evaluated;
                var mask = Layers[i].Def.Mask;
                Unbound.Runtime.Humanoid.HumanRootMotion.CombineLayer(ref blended, ref any,
                    System.Runtime.InteropServices.CollectionsMarshal.AsSpan(m_HumanSamples),
                    System.Runtime.InteropServices.CollectionsMarshal.AsSpan(m_HumanBodyDifferences),
                    System.Runtime.InteropServices.CollectionsMarshal.AsSpan(m_HumanWeights), additive, layerWeight,
                    mask != null && mask.MasksHumanoidParts ? mask.m_HumanoidParts : null);
            }
            if (!any)
            {
                human.IdleMotion(m_RootDeltaTime);
                return false;
            }
            human.ApplyMotion(blended, m_RootDeltaTime, out var rm);
            if (!animator.m_ApplyRootMotion && !m_HasMonoMove) return true;
            animator.m_DeltaPosition = new Vector3(rm.DeltaPosition.x, rm.DeltaPosition.y, rm.DeltaPosition.z);
            animator.m_DeltaRotation = new Quaternion(rm.DeltaRotation.x, rm.DeltaRotation.y, rm.DeltaRotation.z, rm.DeltaRotation.w);
            animator.m_RootVelocity = new Vector3(rm.Velocity.x, rm.Velocity.y, rm.Velocity.z);
            animator.m_RootAngularVelocity = new Vector3(rm.AngularVelocity.x, rm.AngularVelocity.y, rm.AngularVelocity.z);
            if (animator.m_Match != null) HumanMatchStep(animator, human, RootMotionScale(animator.transform));
            return true;
        }
    }
}
