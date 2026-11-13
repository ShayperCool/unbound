using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Unbound.Runtime.Playables;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace Unbound.Runtime.Playables
{
    /// <summary>An AnimationClipPlayable: samples its clip at its time (wrapped when the clip loops,
    /// clamped otherwise) and fires the clip's events the time crosses during playback.</summary>
    internal sealed class ClipKind : AnimationKind
    {
        internal AnimationClip Clip;
        internal bool ApplyFootIK = true;
        internal bool ApplyPlayableIK;
        internal bool RemoveStartOffset = true;
        internal bool OverrideLoopTime;
        internal bool LoopTime;
        internal float SampleRate;
        readonly AnimationPose m_Pose = new AnimationPose();
        // The last evaluation's step and humanoid sample, reused by a replay (visibility re-blend).
        RootMotionStep m_LastMotion;
        Unbound.Runtime.Humanoid.HumanRootMotionSample m_LastBody;
        bool m_WarnedLegacy;
        // The step of time the last evaluation covered, for events.
        double m_From, m_To;
        float m_Weight;
        readonly System.Runtime.CompilerServices.ConditionalWeakTable<Animator, object> m_EventTargets = new();
        bool m_IncludeStart;

        bool Loops => OverrideLoopTime ? LoopTime : Clip != null && Clip.isLooping;

        internal override bool CanChangeInputs => false;
        internal override bool CanSetWeights => false;

        /// <summary>The clip's normalized time (time / length) at playable time <paramref name="t"/>
        /// as its pose and root motion read it (<see cref="ClipSampling.ClipNormalized"/>): a time
        /// between two singles samples the later one (6000.0.71f1, tests/projects/humanoid
        /// longClipA/B at 70000 s, clips of 1 s and 2/3 s; animculling clock/*/Manual: 0.0999999978 s
        /// samples the pose of 0.1 s), so a double time just below a loop's end already samples the
        /// loop's start.</summary>
        float Normalized(double t) => ClipSampling.ClipNormalized(t, Clip.length);

        /// <summary>The playable time as the clip's pose and root motion use it: its
        /// <see cref="Normalized"/> time in seconds (exact in double), so both agree on which loop a
        /// time is in.</summary>
        double ClipClock(double t) => Clip.length > 0f ? (double)Normalized(t) * Clip.length : (float)t;

        /// <summary>The playable time as the clip's events compare with it: the nearest single
        /// (animculling clock/*/Manual: an event at 0.1 s fires in the update that reaches
        /// 0.0999999978 s + 0.02 s, not at 0.0999999978 s, whose pose is the one of 0.1 s).</summary>
        static double EventClock(double t) => (float)t;

        /// <summary>The clip's local time at (single-precision) playable time <paramref name="t"/>.</summary>
        double ClipTime(double t)
        {
            float length = Clip.length;
            if (length <= 0f) return Clip.StartTime;
            double local = Loops ? t - Math.Floor(t / length) * length : Math.Clamp(t, 0.0, length);
            return Clip.StartTime + local;
        }

        internal override void Bind(AnimationEvaluation e, PlayableNode node)
        {
            if (Clip == null) return;
            if (Clip.m_Legacy)
            {
                if (!m_WarnedLegacy)
                {
                    m_WarnedLegacy = true;
                    Debug.LogWarning($"The AnimationClip '{Clip.name}' used by an AnimationClipPlayable must not be legacy.", Clip);
                }
                return;
            }
            e.Bindings.Bind(Clip);
        }

        internal override void Evaluate(AnimationEvaluation e, PlayableNode node, int viaPort, float weight, AnimStream into)
        {
            if (Clip == null || Clip.m_Legacy) return;
            if (!e.ReferencePose && weight > 0f && ApplyPlayableIK) e.PlayableIK = true;
            if (!e.ReferencePose && weight > 0f && ApplyFootIK) into.FootIK = true;
            into.ClipSampled = true;
            var bound = e.Bindings.Bind(Clip);
            // A humanoid clip on a humanoid Animator: a whole human pose (WholeHuman).
            bool wholeHuman = e.Animator.Human != null && Clip.HasHumanCurves;
            if (!e.ReferencePose && wholeHuman && e.Animator.Human is { } human && Clip.length > 0f)
            {
                if (e.Replay) into.BodyMotion = m_LastBody;
                else
                {
                    human.SampleMotion(Clip, Clip.HumanClipMotion, new Unbound.Runtime.Humanoid.HumanClipMotionInput
                    {
                        Time = Normalized(node.Time),
                        PreviousTime = Normalized(node.LastPlaying ? node.PreviousTime : node.Time), Speed = 1f,
                    }, out into.BodyMotion);
                    m_LastBody = into.BodyMotion;
                }
            }
            double t = e.ReferencePose ? 0.0 : ClipClock(node.Time);
            m_Pose.Reset(e.Bindings.FloatCount, e.Bindings.ObjectCount);
            // The clip's loop pose applies as in the Animator's own sampling, generic and humanoid,
            // whatever the root motion (Unity 6000.0.71f1, tests/projects/rootcurves graph.*); an
            // additive reference pose keeps the plain curves of generic clips (unmeasured).
            ClipSampling.Sample(bound, (float)ClipTime(t), 1f, m_Pose,
                loopPose: !e.ReferencePose || e.MotionMode == RootMotionMode.Human);
            if (wholeHuman) ClipSampling.CompleteHuman(bound, e.Bindings, 1f, m_Pose);
            int n = Math.Min(into.FloatCount, e.Bindings.FloatCount);
            for (int c = 0; c < n; c++)
            {
                float w = m_Pose.Weight[c];
                if (w <= 0f) continue;
                into.Values[c] = m_Pose.Value[c] / w;
                into.Set(c);
            }
            if (wholeHuman) WholeHuman(e, into, (float)ClipTime(t));
            if (!e.ReferencePose && e.MotionMode != RootMotionMode.Unsupported)
            {
                var motion = e.Replay ? m_LastMotion : e.MotionMode == RootMotionMode.Compute ? Motion(e, bound, node) : RootMotionStep.Zero;
                motion.RootUnanimated = !AnimatesRoot(e);
                into.Motion = motion;
                m_LastMotion = motion;
            }
            // Under root motion playables the root starts where the clip's first frame is removed.
            if (e.RootMotion && RemoveStartOffset) RemoveStart(e, bound, into);
            if (!e.ReferencePose && into.BodyMotion.HasMotion)
            {
                AnimationEvaluation.GoalsToMotion(into, into.BodyMotion.Body, inverse: false);
                into.GoalsInMotion = true;
            }
            int m = Math.Min(into.ObjectCount, e.Bindings.ObjectCount);
            for (int c = 0; c < m; c++)
            {
                if (m_Pose.ObjectWeight[c] <= 0f) continue;
                into.Objects[c] = m_Pose.Objects[c];
                into.ObjectAnimated[c] = true;
            }
            if (e.ReferencePose || e.Replay || Clip.m_Events.Count == 0 || weight <= 0f) return;
            // Events fire for the time playback moved through (not for seeks).
            if ((node.FrameFlags & FrameData.Flags.SeekOccured) != 0) return;
            // Startup sampling fires time-zero events once even without advancement. A first
            // positive automatic sample includes zero too (6000.0.71f1, animstartup).
            m_IncludeStart = Compat.AnimatorStartupMeasured && e.AutomaticFrame && node.LastPlaying
                && node.PreviousTime == 0 && !m_EventTargets.TryGetValue(e.Animator, out _);
            if (node.PreviousTime == node.Time && !m_IncludeStart) return;
            m_From = EventClock(node.PreviousTime);
            m_To = EventClock(node.Time);
            m_Weight = weight * e.OutputWeight;
            e.Note(this, node);
        }

        /// <summary>A humanoid clip's stream holds a whole human pose, as the mixers that blend it
        /// take it (6000.0.71f1, tests/projects/humanoid bodyGraph.*): the body curves hold the
        /// clip's body read whole (<see cref="Unbound.Runtime.Humanoid.HumanRootMotion.ReadRoot"/>:
        /// the identity's components for the RootQ curves it lacks, normalized), so a clip with
        /// some or none of them blends as a rotation (bodyGraph.partial, bodyGraph.w,
        /// bodyGraph.none, bodyGraph.layerPartial); a goal it has no curve for is the identity
        /// (which <see cref="AnimationEvaluation.GoalsToMotion"/> then places at the clip's
        /// body). The muscles and translations it lacks are at rest already
        /// (<see cref="ClipSampling.CompleteHuman"/>).</summary>
        void WholeHuman(AnimationEvaluation e, AnimStream into, float clipTime)
        {
            var b = e.Bindings;
            b.SetHumanBody(into.Values, into.Animated,
                into.BodyMotion.HasMotion ? into.BodyMotion.Root : e.Animator.Human.SampleBody(Clip, clipTime));
            var present = Clip.HumanPresent;
            for (int curve = Unbound.Runtime.Humanoid.HumanClipImport.GoalFirst; curve < Unbound.Runtime.Humanoid.HumanClipImport.MuscleFirst; curve++)
            {
                if (present[curve]) continue;
                int c = b.HumanChannel(curve);
                if (c < 0 || c >= into.FloatCount) continue;
                into.Values[c] = Unbound.Runtime.Humanoid.AnimatorHuman.DefaultValue(curve);
                into.Set(c);
            }
        }

        bool BodyMoves(AnimationEvaluation e) => Clip.BodyMovesRoot(e.Animator);

        /// <summary>The clip just sampled has curves for the Animator transform's position or
        /// rotation, a root node's motion (MotionT/MotionQ curves) or body curves that move it.</summary>
        bool AnimatesRoot(AnimationEvaluation e)
        {
            if (Clip.MotionCurves() != null || BodyMoves(e) || !RootAnimation.Channels(e, out int pos, out int rot)) return true;
            return RootAnimation.Any(m_Pose.Weight, e.Bindings.FloatCount, pos, rot);
        }

        readonly AnimationPose m_MotionPose = new AnimationPose();

        /// <summary>
        /// The root's movement over this evaluation: from the playable's previous local time to its
        /// time on the clip (wrapped when it loops, clamped otherwise), whole loops included, as the
        /// Animator's own clip root motion moves between a state's previous and current times. The
        /// root is the Animator's transform; a root node's MotionT/MotionQ curves move it when the
        /// clip has them. Unbound's choices (unmeasured): a seek (SetTime) or a paused playable
        /// moves the root by nothing; a playable whose own time wrapped at its duration
        /// (DirectorWrapMode.Loop) moved forward through the wrap.
        /// </summary>
        RootMotionStep Motion(AnimationEvaluation e, BoundClip bound, PlayableNode node)
        {
            if (!node.LastPlaying || (node.FrameFlags & FrameData.Flags.SeekOccured) != 0) return RootMotionStep.Zero;
            double from = ClipClock(node.PreviousTime), to = ClipClock(node.Time);
            if ((node.FrameFlags & FrameData.Flags.Loop) != 0 && to < from && node.Duration > 0 && node.Duration < double.MaxValue)
                to += node.Duration;
            if (from == to) return RootMotionStep.Zero;
            var root = e.Animator.transform;
            int pos = e.Bindings.ChannelOf(root, "position"), rot = e.Bindings.ChannelOf(root, "rotation");
            if (pos < 0 && rot < 0 && Clip.MotionCurves() == null && !BodyMoves(e)) return RootMotionStep.Zero;
            return Between(e, bound, from, to, pos, rot);
        }

        RootMotionStep Between(AnimationEvaluation e, BoundClip bound, double from, double to, int pos, int rot)
        {
            float length = Clip.length;
            if (!Loops || length <= 0f) return Segment(e, bound, (float)ClipTime(from), (float)ClipTime(to), pos, rot);
            if (to < from) return Between(e, bound, to, from, pos, rot).Inverse();
            double first = Math.Floor(from / length), last = Math.Floor(to / length);
            if (first == last) return Segment(e, bound, (float)ClipTime(from), (float)ClipTime(to), pos, rot);
            float start = Clip.StartTime, end = start + length;
            var motion = Segment(e, bound, (float)ClipTime(from), end, pos, rot);
            var cycle = Segment(e, bound, start, end, pos, rot);
            for (long n = (long)(last - first) - 1; n > 0; n >>= 1)
            {
                if ((n & 1) != 0) motion = motion.Then(cycle);
                cycle = cycle.Then(cycle);
            }
            return motion.Then(Segment(e, bound, start, (float)ClipTime(to), pos, rot));
        }

        /// <summary>The root's movement from one clip time to another, in its frame at the first.</summary>
        RootMotionStep Segment(AnimationEvaluation e, BoundClip bound, float t0, float t1, int pos, int rot)
        {
            var (p0, r0) = RootPose(e, bound, t0, pos, rot);
            var (p1, r1) = RootPose(e, bound, t1, pos, rot);
            var inverse = Quaternion.Inverse(r0);
            return new RootMotionStep { Known = true, Delta = inverse * (p1 - p0), Turn = inverse * r1 };
        }

        (Vector3, Quaternion) RootPose(AnimationEvaluation e, BoundClip bound, float time, int pos, int rot)
        {
            if (Clip.MotionCurves() != null) return Clip.MotionPose(time);
            if (BodyMoves(e)) return Clip.BodyMotionPose(time);
            m_MotionPose.Reset(e.Bindings.FloatCount, e.Bindings.ObjectCount);
            ClipSampling.Sample(bound, time, 1f, m_MotionPose);
            var p = Vector3.zero;
            if (pos >= 0 && m_MotionPose.Weight[pos] > 0f)
                p = new Vector3(m_MotionPose.Value[pos] / m_MotionPose.Weight[pos],
                    m_MotionPose.Value[pos + 1] / Math.Max(m_MotionPose.Weight[pos + 1], 1e-6f),
                    m_MotionPose.Value[pos + 2] / Math.Max(m_MotionPose.Weight[pos + 2], 1e-6f));
            var r = Quaternion.identity;
            if (rot >= 0 && m_MotionPose.Weight[rot] > 0f)
            {
                r = new Quaternion(m_MotionPose.Value[rot], m_MotionPose.Value[rot + 1], m_MotionPose.Value[rot + 2], m_MotionPose.Value[rot + 3]);
                float n = MathF.Sqrt(r.x * r.x + r.y * r.y + r.z * r.z + r.w * r.w);
                r = n > 1e-6f ? new Quaternion(r.x / n, r.y / n, r.z / n, r.w / n) : Quaternion.identity;
            }
            return (p, r);
        }

        readonly AnimationPose m_StartPose = new AnimationPose();

        /// <summary>The root's position and rotation relative to the clip's first frame.</summary>
        void RemoveStart(AnimationEvaluation e, BoundClip bound, AnimStream into)
        {
            var b = e.Bindings;
            var root = b.Root != null ? b.Root.transform : null;
            if (root == null) return;
            int pos = b.ChannelOf(root, "position"), rot = b.ChannelOf(root, "rotation");
            if ((pos < 0 || !into.Animated[pos]) && (rot < 0 || !into.Animated[rot])) return;
            m_StartPose.Reset(b.FloatCount, b.ObjectCount);
            ClipSampling.Sample(bound, Clip.StartTime, 1f, m_StartPose);
            var startRot = Quaternion.identity;
            if (rot >= 0 && m_StartPose.Weight[rot] > 0f)
                startRot = QuaternionMath.Normalize(new Quaternion(m_StartPose.Value[rot], m_StartPose.Value[rot + 1], m_StartPose.Value[rot + 2], m_StartPose.Value[rot + 3]));
            var inverse = Quaternion.Inverse(startRot);
            if (pos >= 0 && into.Animated[pos] && m_StartPose.Weight[pos] > 0f)
            {
                var start = new Vector3(m_StartPose.Value[pos], m_StartPose.Value[pos + 1], m_StartPose.Value[pos + 2]);
                var p = inverse * (new Vector3(into.Values[pos], into.Values[pos + 1], into.Values[pos + 2]) - start);
                into.Values[pos] = p.x; into.Values[pos + 1] = p.y; into.Values[pos + 2] = p.z;
            }
            if (rot >= 0 && into.Animated[rot])
                AnimationEvaluation.SetQ(into.Values, rot, inverse * AnimationEvaluation.Q(into.Values, rot));
        }

        internal override void AfterWrite(AnimationEvaluation e, PlayableNode node)
        {
            var animator = e.Animator;
            if (animator == null || !animator.m_FireEvents || Clip == null || Clip.length <= 0f) return;
            if (e.AutomaticFrame) m_EventTargets.GetValue(animator, _ => new object());
            FireInterval(animator, m_From, m_To, m_Weight, m_IncludeStart);
        }

        /// <summary>The event interval this evaluation fires, for a later replay.</summary>
        internal ClipEventInterval EventInterval(PlayableNode node) => new ClipEventInterval
        {
            Clip = this, Node = node, From = m_From, To = m_To, Weight = m_Weight, IncludeStart = m_IncludeStart,
        };

        internal void FireInterval(Animator animator, double from, double to, float weight, bool includeStart)
        {
            if (animator == null || !animator.m_FireEvents || Clip == null) return;
            float length = Clip.length;
            if (length <= 0f) return;
            var go = animator.m_GameObject;
            var clip = Clip;
            var clipInfo = new AnimatorClipInfo { m_Clip = clip, m_Weight = weight };
            AnimationEvents.FireCrossed(clip, from / length, to / length, Loops, includeStart, ev =>
            {
                var fired = ev.Copy();
                fired.m_Source = AnimationEventSource.Animator;
                fired.m_AnimatorClipInfo = clipInfo;
                AnimationEvents.Dispatch(go, fired, clip);
            });
        }
    }

    /// <summary>A clip playable's evaluated event interval, retained for visibility replay.</summary>
    internal struct ClipEventInterval
    {
        internal ClipKind Clip;
        internal PlayableNode Node;
        internal double From, To;
        internal float Weight;
        internal bool IncludeStart;

        internal void Fire(Animator animator)
        {
            if (!Node.Destroyed) Clip.FireInterval(animator, From, To, Weight, IncludeStart);
        }
    }

    /// <summary>An AnimationMixerPlayable: its inputs summed by weight (<see cref="InputWeight"/>),
    /// the remainder of weight one taken by the default values (channel by channel, over the
    /// inputs animating the channel); sums above one are not normalized.</summary>
    internal sealed class MixerKind : AnimationKind
    {
        /// <summary>Mixer trees deeper than this are not walked for humanoid motion (a guard
        /// against cyclic or runaway graphs; real graphs nest a few levels).</summary>
        const int MaxHumanTreeDepth = 64;

        /// <summary>A negative input weight is stored as 0 (GetInputWeight reads 0); NaN is kept
        /// (6000.0.71f1, tests/projects/animculling humanMixerShape/negative and nan).</summary>
        internal override float StoredWeight(float weight) => weight < 0f ? 0f : weight;

        /// <summary>An input's weight as the mixer uses it: a NaN weight counts as 0 (pose,
        /// curves, root motion and events), others as stored, without normalizing the sum
        /// (6000.0.71f1, tests/projects/animculling genericMixerSum and humanMixerShape; the
        /// AnimationMixerPlayable page specifies no normalization).</summary>
        internal static float InputWeight(float weight) => weight > 0f ? weight : 0f;

        /// <summary>Whether a mixer and its inputs compose humanoid root motion through the
        /// humanoid sampler: every connected input is a humanoid motion clip or such a mixer (a
        /// mixer on measured releases, <see cref="Compat.HumanPlayableMixerMotionMeasured"/>, a layer
        /// mixer, <see cref="Compat.HumanPlayableLayerMixerMotionMeasured"/>),
        /// is reached through port 0 and feeds only this mixer, at a weight that is not infinite
        /// (any sum: <see cref="HumanRootMotion.Blend"/> sums the inputs unnormalized). A clip
        /// gives its body whole, with some of the body curves or none
        /// (<see cref="ClipKind.WholeHuman"/>).</summary>
        internal static bool HumanMotionTree(PlayableNode node, int depth = 0)
        {
            if (node == null || node.Destroyed || depth > MaxHumanTreeDepth || node.Traversal != PlayableTraversalMode.Mix) return false;
            if (node.Kind is MixerKind ? !Compat.HumanPlayableMixerMotionMeasured
                : node.Kind is not LayerMixerKind || !Compat.HumanPlayableLayerMixerMotionMeasured) return false;
            int connected = 0;
            foreach (var input in node.Inputs)
            {
                if (input.Node == null) continue;
                connected++;
                if (float.IsInfinity(input.Weight) || input.SourcePort != 0 || ConsumerCount(input.Node) != 1) return false;
                if (input.Node.Kind is MixerKind or LayerMixerKind)
                {
                    if (!HumanMotionTree(input.Node, depth + 1)) return false;
                    continue;
                }
                if (!AnimationEvaluation.HumanMotionClip(input.Node)) return false;
            }
            return connected > 0;
        }

        /// <summary>Whether a tree is only clips and mixers (the shapes the visibility re-blend
        /// is measured for).</summary>
        internal static bool ClipMixerTree(PlayableNode node, int depth = 0)
        {
            if (node == null || node.Destroyed || depth > MaxHumanTreeDepth) return false;
            if (node.Kind is ClipKind) return true;
            if (node.Kind is not MixerKind) return false;
            foreach (var input in node.Inputs)
                if (input.Node != null && !ClipMixerTree(input.Node, depth + 1)) return false;
            return true;
        }

        /// <summary>A hash of a mixer tree's input weights, to tell whether they changed
        /// between two evaluations.</summary>
        internal static int TreeWeights(PlayableNode node, int depth = 0)
        {
            int hash = 17;
            if (node == null || node.Destroyed || depth > MaxHumanTreeDepth) return hash;
            foreach (var input in node.Inputs)
            {
                if (input.Node == null) continue;
                hash = hash * 31 + BitConverter.SingleToInt32Bits(input.Weight);
                if (input.Node.Kind is MixerKind) hash = hash * 31 + TreeWeights(input.Node, depth + 1);
            }
            return hash;
        }

        internal static int ConsumerCount(PlayableNode node)
        {
            int consumers = 0;
            foreach (var output in node.Outputs)
                if (output.Node != null) consumers++;
            return consumers;
        }

        internal override void Evaluate(AnimationEvaluation e, PlayableNode node, int viaPort, float weight, AnimStream into) =>
            Mix(e, node, weight, into, 0);

        static readonly Stack<List<(AnimStream Stream, float Weight)>> s_InputLists = new Stack<List<(AnimStream, float)>>();

        internal static void Mix(AnimationEvaluation e, PlayableNode node, float weight, AnimStream into, int depth)
        {
            // Nested mixers and reentrant evaluations each rent their own input list.
            var inputs = s_InputLists.Count > 0 ? s_InputLists.Pop() : new List<(AnimStream, float)>();
            e.Combining++;
            try
            {
                foreach (var i in node.Inputs)
                {
                    if (i.Node == null) continue;
                    var s = AnimStream.Rent(e.Bindings);
                    s.DeltaTime = e.DeltaTime;
                    float w = InputWeight(i.Weight);
                    inputs.Add((s, w));
                    e.EvaluateInto(i.Node, i.SourcePort, weight * w, s, depth + 1);
                }
                AnimationEvaluation.MixInto(into, inputs);
                if (e.MotionMode == RootMotionMode.Human && into.BodyMotion.HasMotion) WriteHumanRoot(e, into);
                if (e.MotionMode != RootMotionMode.Unsupported)
                {
                    var motion = RootMotionStep.Mix(inputs);
                    // The mixer animates the root when an input does, whatever its weight, as its pose
                    // marks a channel any input animates (Unbound's choice, unmeasured).
                    if (motion.Known)
                    {
                        motion.RootUnanimated = true;
                        foreach (var (s, _) in inputs)
                            if (!s.Motion.RootUnanimated) { motion.RootUnanimated = false; break; }
                    }
                    into.Motion = motion;
                }
            }
            finally
            {
                e.Combining--;
                foreach (var (s, _) in inputs) AnimStream.Return(s);
                inputs.Clear();
                s_InputLists.Push(inputs);
            }
        }

        /// <summary>The body the mixer blended from its inputs' bodies (RootT/RootQ) replaces
        /// what the body curves' channels mixed to, so that the pose's body is exactly the
        /// blended one (HumanRootMotion.PoseBody takes any difference for an additional turn).</summary>
        static void WriteHumanRoot(AnimationEvaluation e, AnimStream into) =>
            e.Bindings.SetHumanBody(into.Values, into.Animated, into.BodyMotion.Root);
    }

    /// <summary>The root motion root's channels (the Animator transform's position and rotation),
    /// for <see cref="RootMotionStep.RootUnanimated"/>.</summary>
    internal static class RootAnimation
    {
        /// <summary>The first position and rotation channels of the Animator's transform (-1 when not
        /// bound); false without an Animator.</summary>
        internal static bool Channels(AnimationEvaluation e, out int pos, out int rot)
        {
            pos = rot = -1;
            var t = e.Animator != null ? e.Animator.transform : null;
            if (t == null || e.Bindings == null) return false;
            pos = e.Bindings.ChannelOf(t, "position");
            rot = e.Bindings.ChannelOf(t, "rotation");
            return true;
        }

        /// <summary>Any of the root's channels below <paramref name="count"/> has a weight.</summary>
        internal static bool Any(float[] weight, int count, int pos, int rot)
        {
            count = Math.Min(count, weight.Length);
            for (int c = pos; pos >= 0 && c < pos + 3 && c < count; c++) if (weight[c] > 0f) return true;
            for (int c = rot; rot >= 0 && c < rot + 4 && c < count; c++) if (weight[c] > 0f) return true;
            return false;
        }

        /// <summary>Any of the root's channels below <paramref name="count"/> is animated.</summary>
        internal static bool Any(bool[] animated, int count, int pos, int rot)
        {
            count = Math.Min(count, animated.Length);
            for (int c = pos; pos >= 0 && c < pos + 3 && c < count; c++) if (animated[c]) return true;
            for (int c = rot; rot >= 0 && c < rot + 4 && c < count; c++) if (animated[c]) return true;
            return false;
        }
    }

    /// <summary>An AnimationLayerMixerPlayable: each layer over the ones below, for the channels it
    /// animates and its mask allows; additive layers add their difference to their first frame.</summary>
    internal sealed class LayerMixerKind : AnimationKind
    {
        internal readonly List<AvatarMask> Masks = new List<AvatarMask>();
        internal readonly List<bool> Additive = new List<bool>();
        internal bool SingleLayerOptimization = true;
        readonly Dictionary<int, (AvatarMask Mask, int Count, bool[] Channels)> m_MaskCache = new Dictionary<int, (AvatarMask, int, bool[])>();

        internal void Ensure(int layer)
        {
            while (Masks.Count <= layer) Masks.Add(null);
            while (Additive.Count <= layer) Additive.Add(false);
        }

        /// <summary>The channels a layer's mask lets it animate (null: all), by the same rule as an
        /// Animator controller layer's (<see cref="AvatarMask.AllowsGroup"/>): humanoid curves by
        /// body part (AnimationLayerMixerPlayable.SetLayerMaskFromAvatarMask).</summary>
        bool[] MaskChannels(AnimationEvaluation e, int layer)
        {
            var mask = layer < Masks.Count ? Masks[layer] : null;
            if (mask == null || !mask.Filters) return null;
            if (m_MaskCache.TryGetValue(layer, out var cached) && cached.Mask == mask && cached.Count == e.Bindings.FloatCount) return cached.Channels;
            var b = e.Bindings;
            var channels = new bool[b.FloatCount];
            var root = b.Root != null ? b.Root.transform : null;
            for (int c = 0; c < channels.Length; c++) channels[c] = mask.AllowsGroup(root, b.FloatOwner[c]);
            m_MaskCache[layer] = (mask, b.FloatCount, channels);
            return channels;
        }

        readonly Dictionary<int, (bool[] Source, int Count, bool[] Channels)> m_AdditiveCache = new Dictionary<int, (bool[], int, bool[])>();

        /// <summary>An additive layer's channels: its mask's, without the humanoid goal curves.</summary>
        bool[] AdditiveChannels(AnimationEvaluation e, int layer, bool[] mask)
        {
            var b = e.Bindings;
            if (b.HumanChannel(Unbound.Runtime.Humanoid.HumanClipImport.GoalFirst) < 0 && b.HumanChannel(Unbound.Runtime.Humanoid.HumanClipImport.GoalFirst + 7) < 0
                && b.HumanChannel(Unbound.Runtime.Humanoid.HumanClipImport.GoalFirst + 14) < 0 && b.HumanChannel(Unbound.Runtime.Humanoid.HumanClipImport.GoalFirst + 21) < 0)
                return mask;
            if (m_AdditiveCache.TryGetValue(layer, out var cached) && cached.Source == mask && cached.Count == b.FloatCount) return cached.Channels;
            var channels = new bool[b.FloatCount];
            for (int c = 0; c < channels.Length; c++) channels[c] = mask == null || mask[c];
            for (int curve = Unbound.Runtime.Humanoid.HumanClipImport.GoalFirst; curve < Unbound.Runtime.Humanoid.HumanClipImport.MuscleFirst; curve++)
            {
                int c = b.HumanChannel(curve);
                if (c >= 0 && c < channels.Length) channels[c] = false;
            }
            m_AdditiveCache[layer] = (mask, b.FloatCount, channels);
            return channels;
        }

        /// <summary>A layer's humanoid body parts its mask lets through (null: all).</summary>
        bool[] HumanParts(int layer)
        {
            var mask = layer < Masks.Count ? Masks[layer] : null;
            return mask != null && mask.MasksHumanoidParts ? mask.m_HumanoidParts : null;
        }

        /// <summary>Whether a layer's mask lets it animate the Animator's own transform (the root
        /// motion root), by the transform's path below the bound root as <see cref="MaskChannels"/>
        /// takes it: the mask's root entry is the empty path. No mask, or an empty one, allows it.</summary>
        bool MaskAllowsRoot(AnimationEvaluation e, int layer)
        {
            var mask = layer < Masks.Count ? Masks[layer] : null;
            if (mask == null || mask.m_Elements.Count == 0) return true;
            var root = e.Bindings.Root != null ? e.Bindings.Root.transform : null;
            var t = e.Animator != null ? e.Animator.transform : null;
            if (root == null || t == null) return true;
            var parts = new List<string>();
            for (var p = t; p != null && p != root; p = p.m_Parent) parts.Insert(0, p.m_GameObject.m_Name);
            return mask.Allows(string.Join("/", parts));
        }

        /// <summary>
        /// A layer's root motion over the motion of the layers below it. Unbound's choices
        /// (unmeasured; the Scripting API documents no root motion for layer mixers, only that
        /// "Additive layers blend with previous layers" and that layers otherwise "override the
        /// animation from previous layers"): layers apply in layer order, starting from no motion;
        /// only layers that animate the root contribute: an override layer of weight w blends the
        /// motion so far toward its own by w (deltas lerped, turns slerped, see
        /// <see cref="RootMotionStep.Over"/>); an additive layer adds its delta times w and turns by
        /// its turn slerped from none by w; a layer of weight 0, one whose mask excludes the root,
        /// or one whose playables animate neither the root's position nor its rotation nor a root
        /// node's motion (<see cref="RootMotionStep.RootUnanimated"/>) contributes nothing, as the
        /// layer's pose overrides only the channels it animates; a contributing layer whose motion
        /// is unknown makes the result unknown, unless a later override layer of weight 1 replaces
        /// it. The Animator's own controller layers instead sum their deltas by weight; this
        /// follows the layer mixer's pose rule instead. A weight above 1 (outside SetInputWeight's
        /// "between 0 and 1") scales an additive delta but not its turn, as the additive pose does
        /// (positions by w, rotations by a slerp clamped to 1); an override layer above 1 takes the
        /// layer's motion, as its pose's positions do.
        /// </summary>
        static RootMotionStep LayerMotion(RootMotionStep below, RootMotionStep layer, float w, bool additive)
        {
            if (w <= 0f) return below;
            if (!layer.Known) return RootMotionStep.Unknown;
            if (!additive) return RootMotionStep.Over(below, layer, w);
            return new RootMotionStep
            {
                Known = below.Known,
                Delta = below.Delta + layer.Delta * w,
                Turn = below.Turn * QuaternionMath.Slerp(Quaternion.identity, layer.Turn, Mathf.Clamp01(w)),
            };
        }

        internal override void Evaluate(AnimationEvaluation e, PlayableNode node, int viaPort, float weight, AnimStream into)
        {
            var motion = RootMotionStep.Zero;
            bool any = false, rootAnimated = false;
            // Humanoid body motion: override layers combine their inputs' samples as Animator
            // controller layers do (by layer weight, through their masks' body parts); additive
            // layers add their body difference through their body curves (HumanRootMotion.ApplyToPose).
            var body = default(Unbound.Runtime.Humanoid.HumanRootMotionSample);
            bool anyBody = false;
            Span<Unbound.Runtime.Humanoid.HumanRootMotionSample> layerBody = stackalloc Unbound.Runtime.Humanoid.HumanRootMotionSample[1];
            Span<float> layerBodyWeight = stackalloc float[] { 1f };
            // The pose that places the body: the layers whose mask keeps Root, as an Animator
            // controller's layers (6000.0.71f1, tests/projects/humanoid mixer.upper, mixer.noRoot);
            // Animator.layersAffectMassCenter, a controller layers' setting, does not change it
            // (mixer.noRootMassCenter).
            AnimStream mass = null;
            bool goalsSet = false;
            for (int l = 0; l < node.Inputs.Count; l++)
            {
                var input = node.Inputs[l];
                if (input.Node == null) continue;
                var s = AnimStream.Rent(e.Bindings);
                s.DeltaTime = e.DeltaTime;
                e.Combining++;
                try { e.EvaluateInto(input.Node, input.SourcePort, weight * input.Weight, s); }
                finally { e.Combining--; }
                any = true;
                if (input.Weight > 0f && s.FootIK) into.FootIK = true;
                into.ClipSampled |= s.ClipSampled;
                into.JobRan |= s.JobRan;
                bool additive = l < Additive.Count && Additive[l];
                var parts = HumanParts(l);
                // Goals blend in one frame: the motion's when both sides have it there (the body
                // below is the one the lower layers' goals were placed with). Layers without weight
                // or goals do not decide.
                if (input.Weight > 0f && AnimationEvaluation.AnimatesGoals(s))
                {
                    if (!goalsSet) into.GoalsInMotion = s.GoalsInMotion;
                    else if (into.GoalsInMotion != s.GoalsInMotion)
                    {
                        if (into.GoalsInMotion) AnimationEvaluation.GoalsToBody(into, anyBody ? body : default);
                        else AnimationEvaluation.GoalsToBody(s, s.BodyMotion);
                    }
                    goalsSet = true;
                }
                if (!additive && s.BodyMotion.HasMotion)
                {
                    layerBody[0] = s.BodyMotion;
                    Unbound.Runtime.Humanoid.HumanRootMotion.CombineLayer(ref body, ref anyBody, layerBody,
                        ReadOnlySpan<Unbound.Runtime.Humanoid.Trs>.Empty, layerBodyWeight, false, input.Weight, parts);
                }
                // A layer that does not animate the root is skipped like a masked-out one.
                if (e.MotionMode != RootMotionMode.Unsupported && MaskAllowsRoot(e, l) && !s.Motion.RootUnanimated)
                {
                    motion = LayerMotion(motion, s.Motion, input.Weight, additive);
                    rootAnimated = true;
                }
                var mask = MaskChannels(e, l);
                bool keepsRoot = parts == null || parts[0];
                if (!keepsRoot && mass == null && e.Animator != null && e.Animator.isHuman && input.Weight > 0f)
                {
                    mass = AnimStream.Rent(e.Bindings);
                    mass.CopyFrom(into);
                }
                if (additive)
                {
                    bool was = e.ReferencePose;
                    e.ReferencePose = true;
                    var reference = AnimStream.Rent(e.Bindings);
                    e.EvaluateInto(input.Node, input.SourcePort, 0f, reference);
                    e.ReferencePose = was;
                    // Additive humanoid layers leave the IK goals alone (6000.0.71f1, tests/projects/humanoid
                    // mixer.additiveNoRoot: its feet goals keep the layers' below under Foot IK).
                    var additiveMask = AdditiveChannels(e, l, mask);
                    AnimationEvaluation.AddOver(into, s, reference, input.Weight, additiveMask);
                    if (mass != null && keepsRoot) AnimationEvaluation.AddOver(mass, s, reference, input.Weight, additiveMask);
                    AnimStream.Return(reference);
                }
                else
                {
                    AnimationEvaluation.BlendOver(into, s, input.Weight, mask);
                    if (mass != null && keepsRoot) AnimationEvaluation.BlendOver(mass, s, input.Weight, mask);
                }
                AnimStream.Return(s);
            }
            // Without a connected layer the motion is unknown, as for an empty mixer (Unbound's choice).
            // A layer mixer in a layer above animates the root when one of its layers does, whatever
            // its weight, as its pose marks the channels a layer animates (Unbound's choice, unmeasured).
            if (e.MotionMode != RootMotionMode.Unsupported)
            {
                if (any && motion.Known) motion.RootUnanimated = !rootAnimated;
                into.Motion = any ? motion : RootMotionStep.Unknown;
            }
            if (anyBody) into.BodyMotion = body;
            if (mass != null)
            {
                into.MassCenter ??= new float[Unbound.Runtime.Humanoid.HumanClipImport.CurveCount];
                var b = e.Bindings;
                for (int curve = 0; curve < into.MassCenter.Length; curve++)
                {
                    int c = b.HumanChannel(curve);
                    into.MassCenter[curve] = c >= 0 ? mass.Values[c] : 0f;
                }
                into.HasMassCenter = true;
                AnimStream.Return(mass);
            }
        }
    }

    /// <summary>An AnimatorControllerPlayable: a controller's state machines, advanced in the
    /// prepare pass and sampled into the stream. Its player animates the Animator of the first
    /// animation output that reaches it.</summary>
    internal sealed class ControllerKind : AnimationKind
    {
        internal RuntimeAnimatorController Controller;
        internal AnimatorPlayer Player;
        internal PlayableNode Node;

        internal override bool CanChangeInputs => false;
        internal override bool CanSetWeights => false;
        internal override bool FinishesAtDuration => false;

        /// <summary>The player, made for the Animator an animation output of the graph targets.</summary>
        internal AnimatorPlayer EnsurePlayer()
        {
            if (Player != null || Controller == null || Node == null) return Player;
            foreach (var o in Node.Graph.Outputs)
            {
                var target = (o.Engine as AnimationOutputState)?.Target;
                if (target != null) return EnsurePlayer(target);
            }
            return null;
        }

        internal AnimatorPlayer EnsurePlayer(Animator animator)
        {
            if (Player != null || Controller == null || animator == null) return Player;
            var graph = Controller.Graph;
            if (graph == null) return null;
            Player = new AnimatorPlayer(animator, Controller, graph, animator.StreamBindings)
            {
                Playable = new UnityEngine.Animations.AnimatorControllerPlayable(Node.ToHandle()),
            };
            Player.InstantiateBehaviours();
            return Player;
        }

        internal override void Prepare(PlayableNode node, in FrameData info)
        {
            EnsurePlayer()?.StepLayers(info.deltaTime * info.effectiveSpeed);
        }

        internal override void Bind(AnimationEvaluation e, PlayableNode node) => EnsurePlayer(e.Animator);

        internal override void Evaluate(AnimationEvaluation e, PlayableNode node, int viaPort, float weight, AnimStream into)
        {
            var player = EnsurePlayer(e.Animator);
            if (player == null || player.Bindings != e.Bindings || e.ReferencePose) return;
            player.EvaluateInto(into.Values, into.Animated, into.Objects, into.ObjectAnimated);
            if (player.FootIK()) into.FootIK = true;
            if (e.Animator.Human is { } human) Humanoid(e, player, human, weight, into);
            // Its parameter curves drive its own float parameters from its own layers, before the
            // graph mixes this pose with anything (see PlayableParameters.Sample).
            PlayableParameters.Sample(player, e.Animator);
            e.Note(this, node);
        }

        Unbound.Runtime.Humanoid.HumanRootMotionSample m_LastBody;
        bool m_HumanClips;
        int m_HumanClipsVersion = -1;

        /// <summary>Whether one of the controller's clips is humanoid (its overrides included).</summary>
        internal bool HasHumanClips
        {
            get
            {
                if (Controller == null) return false;
                int version = Controller.OverrideVersion;
                if (m_HumanClipsVersion == version) return m_HumanClips;
                m_HumanClipsVersion = version;
                m_HumanClips = false;
                foreach (var clip in Controller.animationClips)
                    if (clip != null && clip.HasHumanCurves) { m_HumanClips = true; break; }
                return m_HumanClips;
            }
        }

        /// <summary>On a humanoid Animator the controller's stream carries what the Animator's own
        /// controller gives its pose and root motion passes: the humanoid root motion of its
        /// clips blended over its layers, the body and goal curves placed by it, the pose that
        /// places the body under layers masked without Root, and an IK pass for a base layer that
        /// asks for one. Matches the Unity 6000.0.71f1 Editor, tests/projects/humanoid
        /// controllerGraph.*: a controller as the source of an Animator's only output records
        /// the rows of the same controller on the Animator (a state, Foot IK, a transition, a
        /// blend tree, a Direct blend tree, an override and an additive layer, also with masks
        /// without Root and of the upper body). IK passes of the layers above the first,
        /// OnStateIK and the controller below mixers or jobs are unmeasured; other releases
        /// take the same path unmeasured. A paused playable is not prepared, so its states keep
        /// their last interval and every evaluation steps the root over it again
        /// (controllerGraph.paused: for eight updates of one delta time the Editor's root keeps
        /// the turn and the forward step of the update before the pause while the feet pivot
        /// goes on correcting the rest; another delta time while paused is unmeasured).</summary>
        void Humanoid(AnimationEvaluation e, AnimatorPlayer player, Unbound.Runtime.Humanoid.AnimatorHuman human, float weight, AnimStream into)
        {
            if (e.Replay) into.BodyMotion = m_LastBody;
            else
            {
                player.BlendHumanMotion(human, out into.BodyMotion);
                m_LastBody = into.BodyMotion;
            }
            if (into.BodyMotion.HasMotion) into.GoalsInMotion = true;
            else if (e.MotionMode == RootMotionMode.Human && (player.SampledRootNodeMotion()
                || RootAnimation.Channels(e, out int pos, out int rot) && RootAnimation.Any(into.Animated, into.FloatCount, pos, rot)))
                Stubs.Ignored("AnimatorControllerPlayable root motion of states without humanoid clips on a humanoid",
                    "a controller with humanoid clips played through an AnimatorControllerPlayable does not move the root while only its states "
                    + "without a humanoid clip play (their root curves pose the root; not measured)");
            if (weight > 0f && player.BaseLayerIKPass) e.PlayableIK = true;
            if (weight > 0f && player.UpperLayerIKPass)
                Stubs.Ignored("AnimatorControllerPlayable IK passes of layers above the first",
                    "an AnimatorControllerPlayable's controller runs the IK pass of its base layer only (the others are not measured)");
            player.MassCenterInto(into);
        }

        /// <summary>
        /// The root motion the controller's evaluation just carried into its stream (called right
        /// after <see cref="Evaluate"/>): the controller's own root motion as
        /// <see cref="AnimatorPlayer.StreamRootMotion"/> computes it for an animation stream fed by
        /// this playable, turned back into a step by <see cref="RootMotionStep.FromVelocities"/>,
        /// the conversion a job above a controller playable already uses. Zero when the Animator
        /// requests no root motion; unknown when the evaluation sampled nothing (no player, another
        /// Animator's bindings, an additive layer's reference) or humanoid clips play (their motion
        /// is the stream's body motion, <see cref="Humanoid"/>). Unbound's
        /// choices (unmeasured): the motion is the Animator's own controller root motion for the
        /// same states; a zero delta time carries no motion; the controller animates the root when
        /// its evaluation animated the root's position or rotation channels (its clips' curves,
        /// their write defaults, or, for a state without write defaults, every channel it keeps) or
        /// one of its sampled clips has a root node's motion.
        /// </summary>
        internal RootMotionStep RootMotion(AnimationEvaluation e, AnimStream into)
        {
            if (e.ReferencePose || e.MotionMode == RootMotionMode.Unsupported || e.Animator == null) return RootMotionStep.Unknown;
            var player = Player;
            if (player == null || player.Bindings != e.Bindings) return RootMotionStep.Unknown;
            var motion = RootMotionStep.Zero;
            if (e.MotionMode != RootMotionMode.Zero)
            {
                if (!player.StreamRootMotion(into.DeltaTime, out _, out _, out var velocity, out var angular)) return RootMotionStep.Unknown;
                motion = RootMotionStep.FromVelocities(e.Animator, into.DeltaTime, velocity, angular);
            }
            motion.RootUnanimated = RootAnimation.Channels(e, out int pos, out int rot)
                && !RootAnimation.Any(into.Animated, into.FloatCount, pos, rot) && !player.SampledRootNodeMotion();
            return motion;
        }

        internal override void AfterWrite(AnimationEvaluation e, PlayableNode node) => Player?.AfterEvaluate();
    }

    /// <summary>An AnimationOffsetPlayable: its input with the root's position and rotation offset.</summary>
    internal sealed class OffsetKind : AnimationKind
    {
        internal Vector3 Position;
        internal Quaternion Rotation = Quaternion.identity;

        internal override void Evaluate(AnimationEvaluation e, PlayableNode node, int viaPort, float weight, AnimStream into)
        {
            if (node.Inputs.Count > 0 && node.Inputs[0].Node != null)
                e.EvaluateInto(node.Inputs[0].Node, node.Inputs[0].SourcePort, weight, into);
            if (Position == Vector3.zero && QuaternionMath.Equal(Rotation, Quaternion.identity)) return;
            var b = e.Bindings;
            int pos = b.BindChannel(CurveBinding.ForType("", "m_LocalPosition.x", typeof(Transform)), false);
            int rot = b.BindChannel(CurveBinding.ForType("", "m_LocalRotation.x", typeof(Transform)), false);
            if (pos >= 0 && pos + 2 < into.FloatCount && into.Animated[pos])
            {
                var p = Rotation * new Vector3(into.Values[pos], into.Values[pos + 1], into.Values[pos + 2]) + Position;
                into.Values[pos] = p.x; into.Values[pos + 1] = p.y; into.Values[pos + 2] = p.z;
            }
            if (rot >= 0 && rot + 3 < into.FloatCount && into.Animated[rot])
                AnimationEvaluation.SetQ(into.Values, rot, Rotation * AnimationEvaluation.Q(into.Values, rot));
        }
    }

    /// <summary>Playables that pass their first input on (motion-to-delta, remove-scale: Unbound
    /// animates generic hierarchies without root motion, where these change nothing).</summary>
    internal sealed class PassKind : AnimationKind
    {
        internal bool AbsoluteMotion;
        /// <summary>A motion-to-delta playable: clips below it are root motion (their start offset
        /// removed); relative motion continues from where the root was when bound.</summary>
        internal bool Motion;

        internal override void Evaluate(AnimationEvaluation e, PlayableNode node, int viaPort, float weight, AnimStream into)
        {
            if (node.Inputs.Count == 0 || node.Inputs[0].Node == null) return;
            bool was = e.RootMotion;
            if (Motion) e.RootMotion = true;
            e.EvaluateInto(node.Inputs[0].Node, node.Inputs[0].SourcePort, weight, into);
            e.RootMotion = was;
            if (!Motion || AbsoluteMotion) return;
            var b = e.Bindings;
            var root = b.Root != null ? b.Root.transform : null;
            if (root == null) return;
            int pos = b.ChannelOf(root, "position"), rot = b.ChannelOf(root, "rotation");
            var baseRot = rot >= 0 ? AnimationEvaluation.Q(b.Defaults, rot) : Quaternion.identity;
            if (pos >= 0 && into.Animated[pos])
            {
                var p = new Vector3(b.Defaults[pos], b.Defaults[pos + 1], b.Defaults[pos + 2]) + baseRot * new Vector3(into.Values[pos], into.Values[pos + 1], into.Values[pos + 2]);
                into.Values[pos] = p.x; into.Values[pos + 1] = p.y; into.Values[pos + 2] = p.z;
            }
            if (rot >= 0 && into.Animated[rot])
                AnimationEvaluation.SetQ(into.Values, rot, baseRot * AnimationEvaluation.Q(into.Values, rot));
        }
    }

    /// <summary>An AnimationPosePlayable: the default pose (or the current one).</summary>
    internal sealed class PoseKind : AnimationKind
    {
        internal bool MustReadPreviousPose;
        internal bool ReadDefaultPose;
        internal bool ApplyFootIK;

        internal override void Evaluate(AnimationEvaluation e, PlayableNode node, int viaPort, float weight, AnimStream into)
        {
            if (!MustReadPreviousPose) return;
            foreach (var g in e.Bindings.Groups)
            {
                if (!g.Alive || g.IsObject) continue;
                g.Read(into.Values, g.First);
                for (int i = 0; i < g.Width; i++) into.Set(g.First + i);
            }
        }
    }

    internal static class AnimationPlayableUtil
    {
        internal static PlayableHandle Create(PlayableGraph graph, Type type, PlayableKind kind, int inputCount, bool propagateSetTime = false)
        {
            var g = PlayableGraphData.Get(graph) ?? throw new InvalidOperationException("The PlayableGraph is invalid.");
            var n = g.CreateNode(type);
            n.Kind = kind;
            n.PropagateSetTime = propagateSetTime;
            g.SetInputCount(n, inputCount);
            AnimationOutputs.EnsureRegistered();
            return n.ToHandle();
        }

        internal static T Kind<T>(PlayableHandle handle) where T : PlayableKind =>
            PlayableGraphData.Get(handle)?.Kind as T ?? throw new InvalidOperationException("The playable is invalid or not of type " + typeof(T).Name + ".");

        internal static void Check<T>(PlayableHandle handle, string name)
        {
            if (handle.IsValid() && !handle.IsPlayableOfType<T>())
                throw new InvalidCastException("Can't set handle: the playable is not an " + name + ".");
        }
    }
}

namespace UnityEngine.Animations
{
    public partial struct AnimationClipPlayable : IPlayable, IEquatable<AnimationClipPlayable>
    {
        PlayableHandle m_Handle;

        public static AnimationClipPlayable Create(PlayableGraph graph, AnimationClip clip)
        {
            var kind = new ClipKind { Clip = clip, SampleRate = clip != null ? clip.frameRate : 60f };
            return new AnimationClipPlayable(AnimationPlayableUtil.Create(graph, typeof(AnimationClipPlayable), kind, 0));
        }

        internal AnimationClipPlayable(PlayableHandle handle)
        {
            AnimationPlayableUtil.Check<AnimationClipPlayable>(handle, "AnimationClipPlayable");
            m_Handle = handle;
        }

        public PlayableHandle GetHandle() => m_Handle;

        public static implicit operator Playable(AnimationClipPlayable playable) => new Playable(playable.GetHandle());

        public static explicit operator AnimationClipPlayable(Playable playable) => new AnimationClipPlayable(playable.GetHandle());

        public bool Equals(AnimationClipPlayable other) => GetHandle() == other.GetHandle();

        ClipKind Kind => AnimationPlayableUtil.Kind<ClipKind>(m_Handle);

        public AnimationClip GetAnimationClip() => Kind.Clip;

        public bool GetApplyFootIK() => Kind.ApplyFootIK;

        public void SetApplyFootIK(bool value) => Kind.ApplyFootIK = value;

        public bool GetApplyPlayableIK() => Kind.ApplyPlayableIK;

        public void SetApplyPlayableIK(bool value) => Kind.ApplyPlayableIK = value;

        internal bool GetRemoveStartOffset() => Kind.RemoveStartOffset;

        internal void SetRemoveStartOffset(bool value) => Kind.RemoveStartOffset = value;

        internal bool GetOverrideLoopTime() => Kind.OverrideLoopTime;

        internal void SetOverrideLoopTime(bool value) => Kind.OverrideLoopTime = value;

        internal bool GetLoopTime() => Kind.LoopTime;

        internal void SetLoopTime(bool value) => Kind.LoopTime = value;

        internal float GetSampleRate() => Kind.SampleRate;

        internal void SetSampleRate(float value) => Kind.SampleRate = value;
    }

    public partial struct AnimationMixerPlayable : IPlayable, IEquatable<AnimationMixerPlayable>
    {
        PlayableHandle m_Handle;

        static readonly AnimationMixerPlayable m_NullPlayable = new AnimationMixerPlayable(PlayableHandle.Null);

        public static AnimationMixerPlayable Null => m_NullPlayable;

        [Obsolete("normalizeWeights is obsolete. It has no effect and will be removed.")]
        public static AnimationMixerPlayable Create(PlayableGraph graph, int inputCount, bool normalizeWeights) => Create(graph, inputCount);

        public static AnimationMixerPlayable Create(PlayableGraph graph, int inputCount = 0) =>
            new AnimationMixerPlayable(AnimationPlayableUtil.Create(graph, typeof(AnimationMixerPlayable), new MixerKind(), inputCount));

        internal AnimationMixerPlayable(PlayableHandle handle)
        {
            AnimationPlayableUtil.Check<AnimationMixerPlayable>(handle, "AnimationMixerPlayable");
            m_Handle = handle;
        }

        public PlayableHandle GetHandle() => m_Handle;

        public static implicit operator Playable(AnimationMixerPlayable playable) => new Playable(playable.GetHandle());

        public static explicit operator AnimationMixerPlayable(Playable playable) => new AnimationMixerPlayable(playable.GetHandle());

        public bool Equals(AnimationMixerPlayable other) => GetHandle() == other.GetHandle();
    }

    public partial struct AnimationLayerMixerPlayable : IPlayable, IEquatable<AnimationLayerMixerPlayable>
    {
        PlayableHandle m_Handle;

        static readonly AnimationLayerMixerPlayable m_NullPlayable = new AnimationLayerMixerPlayable(PlayableHandle.Null);

        public static AnimationLayerMixerPlayable Null => m_NullPlayable;

        public static AnimationLayerMixerPlayable Create(PlayableGraph graph, int inputCount = 0) => Create(graph, inputCount, true);

        public static AnimationLayerMixerPlayable Create(PlayableGraph graph, int inputCount, bool singleLayerOptimization)
        {
            var kind = new LayerMixerKind { SingleLayerOptimization = singleLayerOptimization };
            return new AnimationLayerMixerPlayable(AnimationPlayableUtil.Create(graph, typeof(AnimationLayerMixerPlayable), kind, inputCount), singleLayerOptimization);
        }

        internal AnimationLayerMixerPlayable(PlayableHandle handle, bool singleLayerOptimization = true)
        {
            AnimationPlayableUtil.Check<AnimationLayerMixerPlayable>(handle, "AnimationLayerMixerPlayable");
            m_Handle = handle;
        }

        public PlayableHandle GetHandle() => m_Handle;

        public static implicit operator Playable(AnimationLayerMixerPlayable playable) => new Playable(playable.GetHandle());

        public static explicit operator AnimationLayerMixerPlayable(Playable playable) => new AnimationLayerMixerPlayable(playable.GetHandle());

        public bool Equals(AnimationLayerMixerPlayable other) => GetHandle() == other.GetHandle();

        LayerMixerKind Kind => AnimationPlayableUtil.Kind<LayerMixerKind>(m_Handle);

        void CheckLayer(uint layerIndex)
        {
            if (layerIndex >= m_Handle.GetInputCount())
                throw new ArgumentOutOfRangeException(nameof(layerIndex), $"layerIndex {layerIndex} must be in the range of 0 to {m_Handle.GetInputCount() - 1}.");
        }

        public bool IsLayerAdditive(uint layerIndex)
        {
            CheckLayer(layerIndex);
            var k = Kind;
            return layerIndex < k.Additive.Count && k.Additive[(int)layerIndex];
        }

        public void SetLayerAdditive(uint layerIndex, bool value)
        {
            CheckLayer(layerIndex);
            var k = Kind;
            k.Ensure((int)layerIndex);
            k.Additive[(int)layerIndex] = value;
        }

        public void SetLayerMaskFromAvatarMask(uint layerIndex, AvatarMask mask)
        {
            CheckLayer(layerIndex);
            if (mask == null) throw new ArgumentNullException(nameof(mask));
            var k = Kind;
            k.Ensure((int)layerIndex);
            // The mask is copied, as Unity does: its transforms and its humanoid body parts.
            var copy = new AvatarMask();
            copy.m_Elements.AddRange(mask.m_Elements);
            Array.Copy(mask.m_HumanoidParts, copy.m_HumanoidParts, copy.m_HumanoidParts.Length);
            k.Masks[(int)layerIndex] = copy;
        }
    }

    internal partial struct AnimationOffsetPlayable : IPlayable, IEquatable<AnimationOffsetPlayable>
    {
        PlayableHandle m_Handle;

        static readonly AnimationOffsetPlayable m_NullPlayable = new AnimationOffsetPlayable(PlayableHandle.Null);

        public static AnimationOffsetPlayable Null => m_NullPlayable;

        public static AnimationOffsetPlayable Create(PlayableGraph graph, Vector3 position, Quaternion rotation, int inputCount) =>
            new AnimationOffsetPlayable(AnimationPlayableUtil.Create(graph, typeof(AnimationOffsetPlayable), new OffsetKind { Position = position, Rotation = rotation }, inputCount, propagateSetTime: true));

        internal AnimationOffsetPlayable(PlayableHandle handle)
        {
            AnimationPlayableUtil.Check<AnimationOffsetPlayable>(handle, "AnimationOffsetPlayable");
            m_Handle = handle;
        }

        public PlayableHandle GetHandle() => m_Handle;

        public static implicit operator Playable(AnimationOffsetPlayable playable) => new Playable(playable.GetHandle());

        public static explicit operator AnimationOffsetPlayable(Playable playable) => new AnimationOffsetPlayable(playable.GetHandle());

        public bool Equals(AnimationOffsetPlayable other) => GetHandle() == other.GetHandle();

        public Vector3 GetPosition() => AnimationPlayableUtil.Kind<OffsetKind>(m_Handle).Position;

        public void SetPosition(Vector3 value) => AnimationPlayableUtil.Kind<OffsetKind>(m_Handle).Position = value;

        public Quaternion GetRotation() => AnimationPlayableUtil.Kind<OffsetKind>(m_Handle).Rotation;

        public void SetRotation(Quaternion value) => AnimationPlayableUtil.Kind<OffsetKind>(m_Handle).Rotation = value;
    }

    internal partial struct AnimationMotionXToDeltaPlayable : IPlayable, IEquatable<AnimationMotionXToDeltaPlayable>
    {
        PlayableHandle m_Handle;

        static readonly AnimationMotionXToDeltaPlayable m_NullPlayable = new AnimationMotionXToDeltaPlayable(PlayableHandle.Null);

        public static AnimationMotionXToDeltaPlayable Null => m_NullPlayable;

        public static AnimationMotionXToDeltaPlayable Create(PlayableGraph graph) =>
            new AnimationMotionXToDeltaPlayable(AnimationPlayableUtil.Create(graph, typeof(AnimationMotionXToDeltaPlayable), new PassKind { Motion = true }, 1));

        AnimationMotionXToDeltaPlayable(PlayableHandle handle)
        {
            AnimationPlayableUtil.Check<AnimationMotionXToDeltaPlayable>(handle, "AnimationMotionXToDeltaPlayable");
            m_Handle = handle;
        }

        public PlayableHandle GetHandle() => m_Handle;

        public static implicit operator Playable(AnimationMotionXToDeltaPlayable playable) => new Playable(playable.GetHandle());

        public static explicit operator AnimationMotionXToDeltaPlayable(Playable playable) => new AnimationMotionXToDeltaPlayable(playable.GetHandle());

        public bool Equals(AnimationMotionXToDeltaPlayable other) => GetHandle() == other.GetHandle();

        public bool IsAbsoluteMotion() => AnimationPlayableUtil.Kind<PassKind>(m_Handle).AbsoluteMotion;

        public void SetAbsoluteMotion(bool value) => AnimationPlayableUtil.Kind<PassKind>(m_Handle).AbsoluteMotion = value;
    }

    internal partial struct AnimationRemoveScalePlayable : IPlayable, IEquatable<AnimationRemoveScalePlayable>
    {
        PlayableHandle m_Handle;

        static readonly AnimationRemoveScalePlayable m_NullPlayable = new AnimationRemoveScalePlayable(PlayableHandle.Null);

        public static AnimationRemoveScalePlayable Null => m_NullPlayable;

        public static AnimationRemoveScalePlayable Create(PlayableGraph graph, int inputCount) =>
            new AnimationRemoveScalePlayable(AnimationPlayableUtil.Create(graph, typeof(AnimationRemoveScalePlayable), new PassKind(), inputCount, propagateSetTime: true));

        internal AnimationRemoveScalePlayable(PlayableHandle handle)
        {
            AnimationPlayableUtil.Check<AnimationRemoveScalePlayable>(handle, "AnimationRemoveScalePlayable");
            m_Handle = handle;
        }

        public PlayableHandle GetHandle() => m_Handle;

        public static implicit operator Playable(AnimationRemoveScalePlayable playable) => new Playable(playable.GetHandle());

        public static explicit operator AnimationRemoveScalePlayable(Playable playable) => new AnimationRemoveScalePlayable(playable.GetHandle());

        public bool Equals(AnimationRemoveScalePlayable other) => GetHandle() == other.GetHandle();
    }

    internal partial struct AnimationPosePlayable : IPlayable, IEquatable<AnimationPosePlayable>
    {
        PlayableHandle m_Handle;

        static readonly AnimationPosePlayable m_NullPlayable = new AnimationPosePlayable(PlayableHandle.Null);

        public static AnimationPosePlayable Null => m_NullPlayable;

        public static AnimationPosePlayable Create(PlayableGraph graph) =>
            new AnimationPosePlayable(AnimationPlayableUtil.Create(graph, typeof(AnimationPosePlayable), new PoseKind(), 0));

        internal AnimationPosePlayable(PlayableHandle handle)
        {
            AnimationPlayableUtil.Check<AnimationPosePlayable>(handle, "AnimationPosePlayable");
            m_Handle = handle;
        }

        public PlayableHandle GetHandle() => m_Handle;

        public static implicit operator Playable(AnimationPosePlayable playable) => new Playable(playable.GetHandle());

        public static explicit operator AnimationPosePlayable(Playable playable) => new AnimationPosePlayable(playable.GetHandle());

        public bool Equals(AnimationPosePlayable other) => GetHandle() == other.GetHandle();

        PoseKind Kind => AnimationPlayableUtil.Kind<PoseKind>(m_Handle);

        public bool GetMustReadPreviousPose() => Kind.MustReadPreviousPose;

        public void SetMustReadPreviousPose(bool value) => Kind.MustReadPreviousPose = value;

        public bool GetReadDefaultPose() => Kind.ReadDefaultPose;

        public void SetReadDefaultPose(bool value) => Kind.ReadDefaultPose = value;

        public bool GetApplyFootIK() => Kind.ApplyFootIK;

        public void SetApplyFootIK(bool value) => Kind.ApplyFootIK = value;
    }

    public static partial class AnimationPlayableExtensions
    {
        /// <summary>Declares the properties a playable animates ahead of time (the Animator binds
        /// them); Unbound binds what clips animate when they are evaluated.</summary>
        public static void SetAnimatedProperties<U>(this U playable, AnimationClip clip) where U : struct, IPlayable
        {
            var handle = playable.GetHandle();
            SetAnimatedPropertiesInternal(ref handle, clip);
        }

        internal static void SetAnimatedPropertiesInternal(ref PlayableHandle playable, AnimationClip animatedProperties) { }
    }
}

namespace UnityEngine.Playables
{
    public static partial class AnimationPlayableUtilities
    {
        [Obsolete("This function is no longer used as it overrides the Time Update Mode of the Playable Graph. Refer to the documentation for an example of an equivalent function.")]
        public static void Play(Animator animator, Playable playable, PlayableGraph graph)
        {
            var output = UnityEngine.Animations.AnimationPlayableOutput.Create(graph, "AnimationClip", animator);
            output.SetSourcePlayable(playable, 0);
            graph.SyncUpdateAndTimeMode(animator);
            graph.Play();
        }

        public static UnityEngine.Animations.AnimationClipPlayable PlayClip(Animator animator, AnimationClip clip, out PlayableGraph graph)
        {
            graph = PlayableGraph.Create();
            var output = UnityEngine.Animations.AnimationPlayableOutput.Create(graph, "AnimationClip", animator);
            var playable = UnityEngine.Animations.AnimationClipPlayable.Create(graph, clip);
            return PlayOn(output, playable, animator, graph);
        }

        public static UnityEngine.Animations.AnimationMixerPlayable PlayMixer(Animator animator, int inputCount, out PlayableGraph graph)
        {
            graph = PlayableGraph.Create();
            var output = UnityEngine.Animations.AnimationPlayableOutput.Create(graph, "Mixer", animator);
            var playable = UnityEngine.Animations.AnimationMixerPlayable.Create(graph, inputCount);
            return PlayOn(output, playable, animator, graph);
        }

        public static UnityEngine.Animations.AnimationLayerMixerPlayable PlayLayerMixer(Animator animator, int inputCount, out PlayableGraph graph)
        {
            graph = PlayableGraph.Create();
            var output = UnityEngine.Animations.AnimationPlayableOutput.Create(graph, "Mixer", animator);
            var playable = UnityEngine.Animations.AnimationLayerMixerPlayable.Create(graph, inputCount);
            return PlayOn(output, playable, animator, graph);
        }

        public static UnityEngine.Animations.AnimatorControllerPlayable PlayAnimatorController(Animator animator, RuntimeAnimatorController controller, out PlayableGraph graph)
        {
            graph = PlayableGraph.Create();
            var output = UnityEngine.Animations.AnimationPlayableOutput.Create(graph, "AnimatorControllerPlayable", animator);
            var playable = UnityEngine.Animations.AnimatorControllerPlayable.Create(graph, controller);
            return PlayOn(output, playable, animator, graph);
        }

        static V PlayOn<V>(UnityEngine.Animations.AnimationPlayableOutput output, V playable, Animator animator, PlayableGraph graph) where V : struct, IPlayable
        {
            output.SetSourcePlayable(playable);
            graph.SyncUpdateAndTimeMode(animator);
            graph.Play();
            return playable;
        }
    }
}
