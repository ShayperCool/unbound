using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Experimental.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;
using Unbound.Runtime.Humanoid;

namespace Unbound.Runtime.Playables
{
    /// <summary>
    /// An animation stream: a value for every channel an Animator's bindings know, and which of
    /// them the stream animates. A stream starts with the default values (those the properties had
    /// when they were bound), animating nothing.
    /// </summary>
    internal sealed class AnimStream
    {
        internal AnimationBindings Bindings;
        internal float[] Values = Array.Empty<float>();
        internal bool[] Animated = Array.Empty<bool>();
        internal Object[] Objects = Array.Empty<Object>();
        internal bool[] ObjectAnimated = Array.Empty<bool>();
        internal int FloatCount;
        internal int ObjectCount;
        /// <summary>The frame's delta time (AnimationStream.deltaTime).</summary>
        internal float DeltaTime;
        /// <summary>Input streams of a job playable and their weights.</summary>
        internal readonly List<(AnimStream Stream, float Weight)> Inputs = new List<(AnimStream, float)>();
        /// <summary>The root motion the stream carries (unknown unless clips, mixers, layer mixers,
        /// controller playables or jobs set it).</summary>
        internal RootMotionStep Motion = RootMotionStep.Unknown;
        /// <summary>The humanoid body relative to its clip's motion, including bake/loop settings.
        /// Supported humanoid clip and mixer outputs use this sample for root movement.</summary>
        internal HumanRootMotionSample BodyMotion;
        /// <summary>Whether the pose asks for Foot IK on a humanoid (an
        /// AnimationClipPlayable's applyFootIK, a controller state's Foot IK): mixers and outputs
        /// pass it on when any input of weight above 0 asks for it.</summary>
        internal bool FootIK;
        /// <summary>Whether the stream's humanoid goal channels hold the goals in the motion's frame
        /// (the clip's body applied, <see cref="AnimationEvaluation.GoalsToMotion"/>): mixers and
        /// layers blend them there.</summary>
        internal bool GoalsInMotion;
        /// <summary>The humanoid pose that places the body (curve-indexed human values), when a
        /// layer whose mask leaves out Root changed the animated one (see
        /// <see cref="LayerMixerKind"/>); null otherwise.</summary>
        internal float[] MassCenter;
        internal bool HasMassCenter;
        /// <summary>Whether a clip was sampled into the stream, and whether an animation job ran on
        /// it: an output that wrote no humanoid curve poses the avatar by default only when its
        /// pose comes from clips alone (the measured case).</summary>
        internal bool ClipSampled, JobRan;
        /// <summary>The IK values (goal weights, hints, look-at) an animation job left in the
        /// stream's human pose, for the jobs above it.</summary>
        internal readonly Unbound.Runtime.Playables.StreamHumanIK HumanIK = new Unbound.Runtime.Playables.StreamHumanIK();
        /// <summary>The playable whose evaluation the stream holds (it keeps what the stream's
        /// buffer keeps between evaluations).</summary>
        internal AnimationKind Owner;

        static readonly Stack<AnimStream> s_Free = new Stack<AnimStream>();

        internal static AnimStream Rent(AnimationBindings b)
        {
            var s = s_Free.Count > 0 ? s_Free.Pop() : new AnimStream();
            s.Reset(b);
            return s;
        }

        internal static void Return(AnimStream s)
        {
            foreach (var (input, _) in s.Inputs) Return(input);
            s.Inputs.Clear();
            Array.Clear(s.Objects, 0, s.Objects.Length);
            s.Bindings = null;
            s_Free.Push(s);
        }

        internal void Reset(AnimationBindings b)
        {
            Bindings = b;
            Motion = RootMotionStep.Unknown;
            BodyMotion = default;
            FootIK = false;
            GoalsInMotion = false;
            HasMassCenter = false;
            ClipSampled = JobRan = false;
            HumanIK.Clear();
            Owner = null;
            FloatCount = b.FloatCount;
            ObjectCount = b.ObjectCount;
            if (Values.Length < FloatCount)
            {
                Values = new float[Math.Max(FloatCount, 16)];
                Animated = new bool[Values.Length];
            }
            if (Objects.Length < ObjectCount)
            {
                Objects = new Object[Math.Max(ObjectCount, 4)];
                ObjectAnimated = new bool[Objects.Length];
            }
            Array.Copy(b.Defaults, Values, FloatCount);
            Array.Clear(Animated, 0, FloatCount);
            Array.Copy(b.ObjectDefaults, Objects, ObjectCount);
            Array.Clear(ObjectAnimated, 0, ObjectCount);
        }

        internal void CopyFrom(AnimStream o)
        {
            BodyMotion = o.BodyMotion;
            FootIK = o.FootIK;
            GoalsInMotion = o.GoalsInMotion;
            HasMassCenter = o.HasMassCenter;
            ClipSampled = o.ClipSampled;
            JobRan = o.JobRan;
            if (o.HasMassCenter)
            {
                MassCenter ??= new float[Unbound.Runtime.Humanoid.HumanClipImport.CurveCount];
                Array.Copy(o.MassCenter, MassCenter, MassCenter.Length);
            }
            Array.Copy(o.Values, Values, FloatCount);
            Array.Copy(o.Animated, Animated, FloatCount);
            Array.Copy(o.Objects, Objects, ObjectCount);
            Array.Copy(o.ObjectAnimated, ObjectAnimated, ObjectCount);
        }
    }

    /// <summary>How the streams of one evaluation carry root motion.</summary>
    internal enum RootMotionMode
    {
        /// <summary>Not computed for unsupported humanoid graph shapes: streams
        /// carry no motion and the graph's root curves pose the root as before.</summary>
        Unsupported,
        /// <summary>A humanoid clip, or a mixer tree of them, uses the Animator's humanoid
        /// sampler, blend and feet pivot, as controller-driven humanoid motion does.</summary>
        Human,
        /// <summary>The Animator requests no root motion (applyRootMotion off, no OnAnimatorMove):
        /// streams carry zero motion, as the Animator's own root motion is then zero.</summary>
        Zero,
        /// <summary>Clips, mixers, layer mixers, controller playables and jobs compute and pass on
        /// their root motion.</summary>
        Compute,
    }

    /// <summary>
    /// The root motion a generic animation stream carries through a playable graph: the root's
    /// movement over this evaluation in the root's own frame at its start, before the root's scale,
    /// and its turn (the form of the Animator's own clip root motion). <see cref="Known"/> is false
    /// where Unbound does not compute it.
    /// </summary>
    internal struct RootMotionStep
    {
        internal bool Known;
        internal Vector3 Delta;
        internal Quaternion Turn;
        /// <summary>
        /// The playables behind the motion are known to animate neither the Animator transform's
        /// position nor its rotation, nor a root node's motion (MotionT/MotionQ): a layer mixer
        /// takes no motion from such a layer, as its pose takes none of the root's channels from
        /// it. Set by clip, mixer, layer mixer and controller playables; a job that keeps its
        /// input's motion passes it on. False, so the motion counts, wherever it is not set (motion
        /// a job set, unknown motion). Unbound's choice (unmeasured).
        /// </summary>
        internal bool RootUnanimated;

        internal static RootMotionStep Unknown => new RootMotionStep { Turn = Quaternion.identity };

        internal static RootMotionStep Zero => new RootMotionStep { Known = true, Turn = Quaternion.identity };

        /// <summary>This motion, then <paramref name="next"/> from where this one ends.</summary>
        internal RootMotionStep Then(RootMotionStep next) => new RootMotionStep
        {
            Known = Known && next.Known,
            Delta = Delta + Turn * next.Delta,
            Turn = Turn * next.Turn,
        };

        /// <summary>The motion back, in the frame where this one ends.</summary>
        internal RootMotionStep Inverse()
        {
            var inverse = Quaternion.Inverse(Turn);
            return new RootMotionStep { Known = Known, Delta = inverse * -Delta, Turn = inverse };
        }

        /// <summary>The mixer rule for motion: deltas summed by weight; turns summed by weight in
        /// one hemisphere, the remainder of weight one taken by no turn, then normalized (the rule
        /// the Animator's own layers use for their states). Unknown without inputs or when an
        /// input's motion is unknown. Unbound's choice (unmeasured).</summary>
        internal static RootMotionStep Mix(List<(AnimStream Stream, float Weight)> inputs)
        {
            if (inputs.Count == 0) return Unknown;
            var delta = Vector3.zero;
            var sum = new Quaternion(0f, 0f, 0f, 0f);
            float total = 0f;
            foreach (var (s, weight) in inputs)
            {
                if (!s.Motion.Known) return Unknown;
                if (weight <= 0f) continue;
                delta += s.Motion.Delta * weight;
                var q = s.Motion.Turn;
                if (Quaternion.Dot(sum, q) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                sum = new Quaternion(sum.x + q.x * weight, sum.y + q.y * weight, sum.z + q.z * weight, sum.w + q.w * weight);
                total += weight;
            }
            if (total < 1f) sum.w += sum.w < 0f ? total - 1f : 1f - total;
            float m = MathF.Sqrt(sum.x * sum.x + sum.y * sum.y + sum.z * sum.z + sum.w * sum.w);
            var turn = m > 1e-8f ? new Quaternion(sum.x / m, sum.y / m, sum.z / m, sum.w / m) : Quaternion.identity;
            return new RootMotionStep { Known = true, Delta = delta, Turn = turn };
        }

        /// <summary>An output's motion over the motion so far, by the output's weight.</summary>
        internal static RootMotionStep Over(RootMotionStep to, RootMotionStep from, float w)
        {
            if (w >= 1f) return from;
            if (w <= 0f) return to;
            return new RootMotionStep
            {
                Known = to.Known && from.Known,
                Delta = to.Delta + (from.Delta - to.Delta) * w,
                Turn = Quaternion.Slerp(to.Turn, from.Turn, w),
            };
        }

        /// <summary>The root's pose in the convention of Animator.SyncRootPose and its scale for
        /// root motion (the root's local scale times its parent's lossy scale), as the Animator's
        /// own root motion takes them.</summary>
        internal static void RootFrame(Animator animator, out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            var t = animator.transform;
            var parent = t.parent;
            position = parent == null ? t.localPosition
                : parent.position + parent.rotation * Vector3.Scale(parent.lossyScale, t.localPosition);
            rotation = parent == null ? t.localRotation : parent.rotation * t.localRotation;
            scale = t.localScale;
            if (parent != null) scale = Vector3.Scale(scale, parent.lossyScale);
        }

        /// <summary>Per-second root motion, as the Animator's own velocity and angularVelocity are
        /// converted: the world delta over the step, and the delta rotation's angle (the short way)
        /// about its axis turned into world space. Zero for a zero step.</summary>
        internal static void Velocities(Vector3 deltaPosition, Quaternion deltaRotation, Quaternion rootRotation, float deltaTime,
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

        /// <summary>The motion as an animation stream reports it (AnimationStream.velocity,
        /// angularVelocity and the root's pose before it), over the stream's delta time.</summary>
        internal void ToStream(Animator animator, float deltaTime, out Vector3 start, out Quaternion startRotation,
            out Vector3 velocity, out Vector3 angularVelocity)
        {
            RootFrame(animator, out start, out startRotation, out var scale);
            Velocities(startRotation * Vector3.Scale(scale, Delta), Turn, startRotation, deltaTime, out velocity, out angularVelocity);
        }

        /// <summary>The motion a stream's velocities make over its delta time (an animation job
        /// set them): the inverse of <see cref="ToStream"/>, so that the root goes where the stream's
        /// rootMotionPosition/rootMotionRotation put it. Zero for a zero step.</summary>
        internal static RootMotionStep FromVelocities(Animator animator, float deltaTime, Vector3 velocity, Vector3 angularVelocity)
        {
            RootFrame(animator, out _, out var rotation, out var scale);
            var local = Quaternion.Inverse(rotation) * (velocity * deltaTime);
            var delta = new Vector3(scale.x != 0f ? local.x / scale.x : 0f, scale.y != 0f ? local.y / scale.y : 0f,
                scale.z != 0f ? local.z / scale.z : 0f);
            float speed = angularVelocity.magnitude;
            var turn = speed == 0f || deltaTime == 0f ? Quaternion.identity
                : Quaternion.Inverse(rotation) * Quaternion.AngleAxis(speed * deltaTime * Mathf.Rad2Deg, angularVelocity / speed) * rotation;
            return new RootMotionStep { Known = true, Delta = delta, Turn = turn };
        }
    }

    /// <summary>What a playable contributes to an animation stream.</summary>
    internal abstract class AnimationKind : PlayableKind
    {
        internal override bool AnimatorClock => true;

        /// <summary>The IK values a job left in the human pose of the stream this playable's
        /// evaluation holds (see <see cref="HumanStreamPose"/>); null until a job set any.</summary>
        internal StreamHumanIK PersistentHumanIK;

        /// <summary>Fills <paramref name="into"/> (reset to defaults) with the playable's pose.
        /// <paramref name="weight"/> is its effective weight in the output (clip events need one).</summary>
        internal abstract void Evaluate(AnimationEvaluation e, PlayableNode node, int viaPort, float weight, AnimStream into);

        /// <summary>Binds what the playable animates before streams are sized.</summary>
        internal virtual void Bind(AnimationEvaluation e, PlayableNode node) { }

        /// <summary>After the pose is written (events, state machine callbacks).</summary>
        internal virtual void AfterWrite(AnimationEvaluation e, PlayableNode node) { }
    }

    /// <summary>
    /// One evaluation of the animation outputs that target an Animator, with Unity's blending rules
    /// (measured with the Editor, tests/projects/playables): a clip animates the channels it has
    /// curves for; a mixer sums its inputs' values by weight for the channels they animate and fills
    /// a total weight below one with the default values; a layer mixer applies each layer over the
    /// layers below it for the channels it animates (an additive layer adds its difference to the
    /// clip's first frame); script and other non-animation playables pass their input through;
    /// outputs apply in sorting order, each blending over the result so far by its weight.
    /// </summary>
    internal sealed class AnimationEvaluation
    {
        internal Animator Animator;
        internal AnimationBindings Bindings;
        internal float DeltaTime;
        internal bool Seek;
        internal bool AutomaticFrame;
        /// <summary>Under a motion-to-delta playable: clips remove their root start offset.</summary>
        internal bool RootMotion;
        /// <summary>Evaluating an additive layer's reference: clips sample their first frame.</summary>
        internal bool ReferencePose;
        internal bool PlayableIK;
        /// <summary>How this evaluation's streams carry root motion (set by <see cref="Run"/>).</summary>
        internal RootMotionMode MotionMode;
        readonly List<(AnimationKind Kind, PlayableNode Node, bool Combined)> m_Evaluated = new List<(AnimationKind, PlayableNode, bool)>();

        /// <summary>Mixers and layer mixers currently combining their inputs above the playable
        /// being evaluated (passthrough playables do not count).</summary>
        internal int Combining;

        internal void Note(AnimationKind kind, PlayableNode node) => m_Evaluated.Add((kind, node, Combining > 0));

        // Movement callbacks can evaluate other graphs reentrantly: each Run rents its own.
        static readonly Stack<AnimationEvaluation> s_Free = new Stack<AnimationEvaluation>();

        /// <summary>Replay mode (<see cref="AnimationOutputs"/> visibility re-blend): clips sample
        /// their held time and reuse their last step and humanoid sample; nothing advances.</summary>
        internal bool Replay;

        internal static AnimationEvaluation RentReplay(Animator animator, AnimationBindings bindings)
        {
            var e = Rent(animator, bindings);
            e.Replay = true;
            return e;
        }

        internal static void ReleaseReplay(AnimationEvaluation e) => Release(e);

        /// <summary>The outputs' combined pose and motion in replay mode (no events fire).</summary>
        internal static AnimStream SampleReplay(AnimationEvaluation e, Animator animator, AnimationBindings bindings,
            List<(PlayableOutputData Output, double Dt, bool Seek)> outputs, out RootMotionStep motion) =>
            Sample(e, animator, bindings, outputs, out motion, out _);

        static AnimationEvaluation Rent(Animator animator, AnimationBindings bindings)
        {
            var e = s_Free.Count > 0 ? s_Free.Pop() : new AnimationEvaluation();
            e.Animator = animator;
            e.Bindings = bindings;
            return e;
        }

        static void Release(AnimationEvaluation e)
        {
            e.Animator = null;
            e.Bindings = null;
            e.DeltaTime = 0f;
            e.Seek = e.AutomaticFrame = e.RootMotion = e.ReferencePose = e.PlayableIK = e.Replay = false;
            e.MotionMode = default;
            e.Combining = 0;
            e.PreviousInputs = null;
            e.m_Evaluated.Clear();
            s_Free.Push(e);
        }

        /// <summary>A playable's pose into a fresh stream (the caller returns it).</summary>
        internal AnimStream Evaluate(PlayableNode n, int viaPort, float weight, int depth = 0)
        {
            var s = AnimStream.Rent(Bindings);
            s.DeltaTime = DeltaTime;
            s.Owner = n?.Kind as AnimationKind;
            EvaluateInto(n, viaPort, weight, s, depth);
            return s;
        }

        internal void EvaluateInto(PlayableNode n, int viaPort, float weight, AnimStream into, int depth = 0)
        {
            if (n == null || n.Destroyed || depth > 64) return;
            if (n.Kind is AnimationKind kind)
            {
                kind.Evaluate(this, n, viaPort, weight, into);
                // Clips, mixers, layer mixers, controller playables and animation jobs carry root
                // motion on. The others (offset, motion-to-delta, remove-scale and pose playables)
                // leave it unknown, so their root keeps the graph's root curves (Unbound does not
                // compute it yet).
                if (kind is ControllerKind controller) into.Motion = controller.RootMotion(this, into);
                else if (!(kind is ClipKind || kind is MixerKind || kind is LayerMixerKind || kind is JobKind)) into.Motion = RootMotionStep.Unknown;
                return;
            }
            // Script and plain playables pass the stream of the input the traversal reaches through.
            if (n.Traversal == PlayableTraversalMode.Passthrough)
            {
                if (viaPort >= 0 && viaPort < n.Inputs.Count && n.Inputs[viaPort].Node != null)
                    EvaluateInto(n.Inputs[viaPort].Node, n.Inputs[viaPort].SourcePort, weight, into, depth + 1);
                return;
            }
            int connected = 0, last = -1;
            for (int i = 0; i < n.Inputs.Count; i++)
                if (n.Inputs[i].Node != null) { connected++; last = i; }
            if (connected == 1)
            {
                EvaluateInto(n.Inputs[last].Node, n.Inputs[last].SourcePort, weight, into, depth + 1);
                return;
            }
            if (connected > 1) MixerKind.Mix(this, n, weight, into, depth);
        }

        /// <summary>Binds every clip and controller the tree animates.</summary>
        internal void Bind(PlayableNode n, int depth = 0)
        {
            if (n == null || n.Destroyed || depth > 64) return;
            (n.Kind as AnimationKind)?.Bind(this, n);
            foreach (var i in n.Inputs)
                if (i.Node != null) Bind(i.Node, depth + 1);
        }

        internal void AfterWrite()
        {
            foreach (var (kind, node, _) in m_Evaluated)
            {
                if (node.Destroyed) continue;
                try { kind.AfterWrite(this, node); }
                catch (Exception ex) { Debug.LogException(ex); }
            }
            m_Evaluated.Clear();
        }

        /// <summary>The clip-event intervals a visibility return after skipped updates fires
        /// again: those of clips a mixer or layer mixer combined
        /// (<see cref="Compat.PlayableEventsReplayOnVisibility"/>).</summary>
        internal void VisibilityEvents(List<ClipEventInterval> into)
        {
            into.Clear();
            if (!Compat.PlayableEventsReplayOnVisibility) return;
            foreach (var (kind, node, combined) in m_Evaluated)
                if (combined && kind is ClipKind clip) into.Add(clip.EventInterval(node));
        }

        static bool AnyManual(List<(PlayableOutputData Output, double Dt, bool Seek)> outputs)
        {
            foreach (var (output, _, _) in outputs)
                if (output.Graph.UpdateMode == DirectorUpdateMode.Manual) return true;
            return false;
        }

        // ------------------------------------------------------------------ channel math

        /// <summary>A quaternion's first channel: a quaternion property, or a humanoid rotation
        /// (the body's RootQ, a goal's) bound as four consecutive curves, which humanoid poses
        /// blend as rotations too (normalized lerp in the same hemisphere).</summary>
        static bool IsQuaternionStart(AnimationBindings b, int c, out int width)
        {
            var g = c < b.FloatOwner.Length ? b.FloatOwner[c] : null;
            width = 1;
            if (g == null || g.First != c) return false;
            if (!g.IsQuaternion && !b.HumanRotationStart(c)) return false;
            width = 4;
            return true;
        }

        /// <summary>to = lerp(to, from, w) on the channels <paramref name="from"/> animates
        /// (quaternions by normalized lerp in the same hemisphere); marks them animated.</summary>
        internal static void BlendOver(AnimStream to, AnimStream from, float w, bool[] mask = null)
        {
            var b = to.Bindings;
            int n = Math.Min(to.FloatCount, from.FloatCount);
            for (int c = 0; c < n; c++)
            {
                if (IsQuaternionStart(b, c, out _))
                {
                    if (c + 3 < n && from.Animated[c] && (mask == null || mask[c]))
                    {
                        Nlerp(to.Values, c, from.Values, c, w);
                        to.Animated[c] = to.Animated[c + 1] = to.Animated[c + 2] = to.Animated[c + 3] = true;
                    }
                    c += 3;
                    continue;
                }
                if (!from.Animated[c] || (mask != null && !mask[c])) continue;
                to.Values[c] = w >= 1f ? from.Values[c] : to.Values[c] + (from.Values[c] - to.Values[c]) * w;
                to.Animated[c] = true;
            }
            int m = Math.Min(to.ObjectCount, from.ObjectCount);
            for (int c = 0; c < m; c++)
            {
                if (!from.ObjectAnimated[c] || (mask != null && false)) continue;
                if (w >= 0.5f) to.Objects[c] = from.Objects[c];
                to.ObjectAnimated[c] = true;
            }
        }

        /// <summary>to += w (from - reference) on the channels <paramref name="from"/> animates
        /// (quaternions: to = to * (reference⁻¹ from)^w).</summary>
        internal static void AddOver(AnimStream to, AnimStream from, AnimStream reference, float w, bool[] mask = null)
        {
            var b = to.Bindings;
            int n = Math.Min(to.FloatCount, from.FloatCount);
            for (int c = 0; c < n; c++)
            {
                if (IsQuaternionStart(b, c, out _))
                {
                    if (c + 3 < n && from.Animated[c] && (mask == null || mask[c]))
                    {
                        var q = Q(to.Values, c);
                        var delta = Quaternion.Inverse(Q(reference.Values, c)) * Q(from.Values, c);
                        var r = q * Quaternion.Slerp(Quaternion.identity, delta, w);
                        SetQ(to.Values, c, r);
                        to.Animated[c] = to.Animated[c + 1] = to.Animated[c + 2] = to.Animated[c + 3] = true;
                    }
                    c += 3;
                    continue;
                }
                if (!from.Animated[c] || (mask != null && !mask[c])) continue;
                to.Values[c] += w * (from.Values[c] - reference.Values[c]);
                to.Animated[c] = true;
            }
        }

        internal static Quaternion Q(float[] v, int c) => new Quaternion(v[c], v[c + 1], v[c + 2], v[c + 3]);

        internal static void SetQ(float[] v, int c, Quaternion q)
        {
            v[c] = q.x; v[c + 1] = q.y; v[c + 2] = q.z; v[c + 3] = q.w;
        }

        static void Nlerp(float[] to, int c, float[] from, int f, float w)
        {
            float x = from[f], y = from[f + 1], z = from[f + 2], qw = from[f + 3];
            if (to[c] * x + to[c + 1] * y + to[c + 2] * z + to[c + 3] * qw < 0f) { x = -x; y = -y; z = -z; qw = -qw; }
            float ax = to[c] + (x - to[c]) * w, ay = to[c + 1] + (y - to[c + 1]) * w, az = to[c + 2] + (z - to[c + 2]) * w, aw = to[c + 3] + (qw - to[c + 3]) * w;
            float m = MathF.Sqrt(ax * ax + ay * ay + az * az + aw * aw);
            if (m < 1e-8f) return;
            to[c] = ax / m; to[c + 1] = ay / m; to[c + 2] = az / m; to[c + 3] = aw / m;
        }

        /// <summary>
        /// A humanoid stream's goal channels between the body's frame and the motion's: a clip
        /// samples its goals relative to its body; blending takes them in the motion's frame, with
        /// each clip's body (relative to its motion) applied (6000.0.71f1, tests/projects/humanoid
        /// mixer.*: two clips blended under Foot IK solve the feet onto the motion-frame blend; the
        /// same blend without Foot IK matches either way). <paramref name="inverse"/> takes them
        /// back relative to <paramref name="body"/>, the blended body.
        /// </summary>
        internal static void GoalsToMotion(AnimStream s, in Unbound.Runtime.Humanoid.Trs body, bool inverse)
        {
            var b = s.Bindings;
            for (int g = 0; g < 4; g++)
            {
                int curve = Unbound.Runtime.Humanoid.HumanClipImport.GoalFirst + 7 * g;
                int t = b.HumanChannel(curve), q = b.HumanChannel(curve + 3);
                if (t < 0 || q < 0 || !s.Animated[t] || !s.Animated[q]) continue;
                int tx = b.HumanChannel(curve + 1), tz = b.HumanChannel(curve + 2);
                int qy = b.HumanChannel(curve + 4), qz = b.HumanChannel(curve + 5), qw = b.HumanChannel(curve + 6);
                if (tx < 0 || tz < 0 || qy < 0 || qz < 0 || qw < 0) continue;
                var goal = new Unbound.Runtime.Humanoid.Trs(
                    new Unbound.Runtime.Humanoid.F4(s.Values[t], s.Values[tx], s.Values[tz], 0f),
                    Unbound.Runtime.Humanoid.M.QNormalize(new Unbound.Runtime.Humanoid.F4(s.Values[q], s.Values[qy], s.Values[qz], s.Values[qw])),
                    Unbound.Runtime.Humanoid.F4.One3);
                var x = inverse ? Unbound.Runtime.Humanoid.M.InvMul(body, goal) : Unbound.Runtime.Humanoid.M.Mul(body, goal);
                s.Values[t] = x.t.x; s.Values[tx] = x.t.y; s.Values[tz] = x.t.z;
                s.Values[q] = x.q.x; s.Values[qy] = x.q.y; s.Values[qz] = x.q.z; s.Values[qw] = x.q.w;
            }
        }

        /// <summary>A stream whose goals are in the motion's frame takes them back relative to its
        /// body (<paramref name="body"/>'s; without one they stay as they are) before it is blended
        /// with a stream whose goals are relative to its body (a clip's without body motion, a
        /// controller playable's while no humanoid clip plays), so both blend in one frame.</summary>
        internal static void GoalsToBody(AnimStream s, in HumanRootMotionSample body)
        {
            if (!s.GoalsInMotion) return;
            if (body.HasMotion) GoalsToMotion(s, body.Body, inverse: true);
            s.GoalsInMotion = false;
        }

        /// <summary>Whether a stream animates any humanoid goal curve.</summary>
        internal static bool AnimatesGoals(AnimStream s)
        {
            var b = s.Bindings;
            for (int curve = Unbound.Runtime.Humanoid.HumanClipImport.GoalFirst; curve < Unbound.Runtime.Humanoid.HumanClipImport.MuscleFirst; curve += 7)
            {
                int c = b.HumanChannel(curve);
                if (c >= 0 && c < s.FloatCount && s.Animated[c]) return true;
            }
            return false;
        }

        /// <summary>Mixer inputs blend their goals in the motion's frame only when every input of
        /// weight above 0 that animates goals has them there (<see cref="GoalsToBody"/>); inputs
        /// without goals or weight do not decide. Returns whether the blend is in the motion's frame.</summary>
        static bool AlignGoalFrames(List<(AnimStream Stream, float Weight)> inputs)
        {
            bool inMotion = false, relative = false;
            foreach (var (stream, weight) in inputs)
            {
                if (weight <= 0f || !AnimatesGoals(stream)) continue;
                if (stream.GoalsInMotion) inMotion = true;
                else relative = true;
            }
            if (inMotion && relative)
                foreach (var (stream, _) in inputs) GoalsToBody(stream, stream.BodyMotion);
            return inMotion && !relative;
        }

        static bool AnyBodyMotion(List<(AnimStream Stream, float Weight)> inputs)
        {
            foreach (var (stream, _) in inputs)
                if (stream.BodyMotion.HasMotion) return true;
            return false;
        }

        /// <summary>The mixer rule over input streams: per channel, the weighted sum of the inputs
        /// that animate it, completed to weight one with the default value.</summary>
        internal static void MixInto(AnimStream into, List<(AnimStream Stream, float Weight)> inputs)
        {
            into.GoalsInMotion = AlignGoalFrames(inputs);
            foreach (var (stream, weight) in inputs)
            {
                if (weight > 0f && stream.FootIK) into.FootIK = true;
                into.ClipSampled |= stream.ClipSampled;
                into.JobRan |= stream.JobRan;
            }
            if (AnyBodyMotion(inputs))
            {
                const int StackInputs = 16;
                Span<HumanRootMotionSample> samples = inputs.Count <= StackInputs
                    ? stackalloc HumanRootMotionSample[StackInputs] : new HumanRootMotionSample[inputs.Count];
                Span<float> weights = inputs.Count <= StackInputs ? stackalloc float[StackInputs] : new float[inputs.Count];
                for (int i = 0; i < inputs.Count; i++)
                {
                    samples[i] = inputs[i].Stream.BodyMotion;
                    weights[i] = inputs[i].Weight;
                }
                into.BodyMotion = HumanRootMotion.Blend(samples[..inputs.Count], weights[..inputs.Count], sum: true);
            }
            var b = into.Bindings;
            var defaults = b.Defaults;
            int n = into.FloatCount;
            for (int c = 0; c < n; c++)
            {
                if (IsQuaternionStart(b, c, out _) && c + 3 < n)
                {
                    float x = 0, y = 0, z = 0, w = 0, total = 0;
                    bool any = false;
                    foreach (var (s, weight) in inputs)
                    {
                        if (!s.Animated[c]) continue;
                        any = true;
                        if (weight <= 0f) continue;
                        float sx = s.Values[c], sy = s.Values[c + 1], sz = s.Values[c + 2], sw = s.Values[c + 3];
                        if (total > 0f && x * sx + y * sy + z * sz + w * sw < 0f) { sx = -sx; sy = -sy; sz = -sz; sw = -sw; }
                        x += weight * sx; y += weight * sy; z += weight * sz; w += weight * sw;
                        total += weight;
                    }
                    if (any)
                    {
                        if (total < 1f)
                        {
                            float r = 1f - total;
                            float dx = defaults[c], dy = defaults[c + 1], dz = defaults[c + 2], dw = defaults[c + 3];
                            if (total > 0f && x * dx + y * dy + z * dz + w * dw < 0f) { dx = -dx; dy = -dy; dz = -dz; dw = -dw; }
                            x += r * dx; y += r * dy; z += r * dz; w += r * dw;
                        }
                        float m = MathF.Sqrt(x * x + y * y + z * z + w * w);
                        if (m > 1e-8f)
                        {
                            into.Values[c] = x / m; into.Values[c + 1] = y / m; into.Values[c + 2] = z / m; into.Values[c + 3] = w / m;
                        }
                        into.Animated[c] = into.Animated[c + 1] = into.Animated[c + 2] = into.Animated[c + 3] = true;
                    }
                    c += 3;
                    continue;
                }
                float acc = 0f, sum = 0f;
                bool animated = false;
                foreach (var (s, weight) in inputs)
                {
                    if (!s.Animated[c]) continue;
                    animated = true;
                    acc += weight * s.Values[c];
                    sum += weight;
                }
                if (!animated) continue;
                if (sum < 1f) acc += (1f - sum) * defaults[c];
                into.Values[c] = acc;
                into.Animated[c] = true;
            }
            for (int c = 0; c < into.ObjectCount; c++)
            {
                float best = 0f;
                bool animated = false;
                foreach (var (s, weight) in inputs)
                {
                    if (!s.ObjectAnimated[c]) continue;
                    animated = true;
                    if (weight > best)
                    {
                        best = weight;
                        into.Objects[c] = s.Objects[c];
                    }
                }
                if (animated) into.ObjectAnimated[c] = true;
            }
        }

        // ------------------------------------------------------------------ an Animator's outputs

        /// <summary>Evaluates outputs targeting one Animator and writes the result. With a playing
        /// controller, its pending pose is the base, otherwise the default values are. Measured
        /// in Unity 6000.0.71f1 (animculling): CullUpdateTransforms keeps time, events and movement
        /// but holds script fields and the pose, except for curves on the Animator's root itself.
        /// On visibility return the combined pose is written without another time/event step.</summary>
        /// <paramref name="deferPhases"/>: the player loop's automatic update, whose movement and
        /// write wait for <see cref="AnimationPhases"/>; explicit evaluations run all at once.
        internal static void Run(Animator animator, List<(PlayableOutputData Output, double Dt, bool Seek)> outputs,
            bool suppressMovement = false, bool deferPhases = false)
        {
            if (animator == null || outputs.Count == 0) return;
            var bindings = animator.StreamBindings;
            if (bindings == null) return;
            var e = Rent(animator, bindings);
            PendingOutput pending = null;
            bool deferred = false;
            // A later evaluation of the Animator's outputs replaces a deferred one.
            animator.m_OutputEvaluations++;
            try
            {
                pending = Run(e, animator, bindings, outputs, suppressMovement);
                if (AnimationPhases.Split)
                {
                    // Clip events and the controller's state callbacks precede movement and the
                    // write (6000.0.71f1, tests/projects/animcallbacks).
                    e.AfterWrite();
                    if (deferPhases)
                    {
                        AnimationPhases.Defer(animator, pending);
                        deferred = true;
                        return;
                    }
                    pending.Move();
                    pending.Write(animator.isActiveAndEnabled);
                }
                else
                {
                    // Other releases (unmeasured) keep the events after the write.
                    pending.Move();
                    pending.Write(animator.isActiveAndEnabled);
                    e.AfterWrite();
                }
            }
            finally
            {
                Release(e);
                if (!deferred) pending?.Release();
            }
        }

        static PendingOutput Run(AnimationEvaluation e, Animator animator, AnimationBindings bindings,
            List<(PlayableOutputData Output, double Dt, bool Seek)> outputs, bool suppressMovement)
        {
            var result = Sample(e, animator, bindings, outputs, out var motion, out float motionDeltaTime);
            var evaluatedPlayer = animator.m_Player;
            var withController = evaluatedPlayer != null && animator.isActiveAndEnabled;
            var pending = PendingOutput.Rent();
            pending.Animator = animator;
            pending.Result = result;
            pending.Mode = e.MotionMode;
            pending.Motion = motion;
            pending.DeltaTime = motionDeltaTime;
            pending.SuppressMovement = suppressMovement;
            pending.ControllerMovement = withController && animator.m_Player.DeferOutput;
            pending.PlayableIK = e.PlayableIK;
            pending.Manual = AnyManual(outputs);
            pending.Weights = AnimationOutputs.TreeWeights(animator);
            pending.Evaluation = animator.m_OutputEvaluations;
            if (e.MotionMode == RootMotionMode.Human)
            {
                // The same sample and feet-pivot memory as controller-driven humanoid motion.
                // Advance it once even when motion is not requested, as the controller does.
                animator.Human.ApplyMotion(result.BodyMotion.HasMotion ? result.BodyMotion : HumanRootMotion.NoMotion, motionDeltaTime, out var step);
                // Visibility return projects the retained (blended) clip step, before this
                // evaluation's feet-pivot correction, in the root's current frame
                // (6000.0.71f1, animculling humanMixerMotion and humanMixerShape).
                var retained = step;
                retained.Delta = result.BodyMotion.Delta;
                pending.HumanStep = step;
                pending.HumanMotion = retained;
            }
            e.VisibilityEvents(pending.Events);
            if (withController) evaluatedPlayer.Ticked = true;
            return pending;
        }

        /// <summary>The outputs' combined pose and motion, without movement, writes or events.</summary>
        static AnimStream Sample(AnimationEvaluation e, Animator animator, AnimationBindings bindings,
            List<(PlayableOutputData Output, double Dt, bool Seek)> outputs, out RootMotionStep motion, out float motionDeltaTime)
        {
            if (outputs.Count > 1) outputs.Sort(static (a, b) =>
            {
                int sa = (a.Output.Engine as AnimationOutputState)?.SortingOrder ?? 100;
                int sb = (b.Output.Engine as AnimationOutputState)?.SortingOrder ?? 100;
                if (sa != sb) return sa.CompareTo(sb);
                return a.Output.Graph.Outputs.IndexOf(a.Output).CompareTo(b.Output.Graph.Outputs.IndexOf(b.Output));
            });
            foreach (var (o, _, _) in outputs)
                if (o.Source != null) e.Bind(o.Source);
            var result = AnimStream.Rent(bindings);
            var evaluatedPlayer = animator.m_Player;
            var withController = evaluatedPlayer != null && animator.isActiveAndEnabled;
            e.MotionMode = MotionModeOf(animator, outputs);
            motion = RootMotionStep.Zero;
            motionDeltaTime = (float)outputs[0].Dt;
            if (withController)
            {
                // Start from the scene, then combine the controller pose it deferred this frame.
                foreach (var g in bindings.Groups)
                {
                    if (!g.Alive) continue;
                    if (g.IsObject) result.Objects[g.First] = g.ReadObject();
                    else g.Read(result.Values, g.First);
                }
                if (animator.m_Player.DeferOutput)
                    animator.m_Player.CopyDeferredInto(result.Values, result.Animated, result.Objects, result.ObjectAnimated);
                if (e.MotionMode == RootMotionMode.Compute && animator.m_Player.DeferOutput)
                    motion = RootMotionStep.FromVelocities(animator, motionDeltaTime, animator.velocity, animator.angularVelocity);
            }
            foreach (var (o, dt, seek) in outputs)
            {
                // An output at weight 0 still holds its channels (they show the base values).
                if (o.Destroyed || o.Source == null) continue;
                e.DeltaTime = (float)dt;
                e.Seek = seek;
                e.AutomaticFrame = (o.Engine as AnimationOutputState)?.Automatic == true
                    && o.Graph.UpdateMode == DirectorUpdateMode.GameTime;
                var source = (o.Engine as AnimationOutputState)?.StreamSource ?? AnimationStreamSource.DefaultValues;
                AnimStream s;
                if (source == AnimationStreamSource.PreviousInputs)
                {
                    // Rigging layers: the stream starts as the result of the outputs before.
                    s = AnimStream.Rent(bindings);
                    s.DeltaTime = e.DeltaTime;
                    s.CopyFrom(result);
                    s.Owner = o.Source.Kind as AnimationKind;
                    e.PreviousInputs = result;
                    e.EvaluateInto(o.Source, o.SourcePort, o.Weight, s);
                    e.PreviousInputs = null;
                }
                else s = e.Evaluate(o.Source, o.SourcePort, o.Weight);
                if (e.MotionMode == RootMotionMode.Compute)
                {
                    // Outputs blend their motion as their pose: over the motion so far by weight. A
                    // PreviousInputs output whose stream carries no motion of its own keeps it.
                    // Unbound's choice (unmeasured).
                    if (s.Motion.Known)
                    {
                        motion = RootMotionStep.Over(motion, s.Motion, o.Weight);
                        motionDeltaTime = e.DeltaTime;
                    }
                    else if (source != AnimationStreamSource.PreviousInputs && o.Weight > 0f) motion = RootMotionStep.Unknown;
                }
                KeepBound(o, s);
                // Outputs blend their goals relative to their bodies (several outputs are unmeasured).
                GoalsToBody(s, s.BodyMotion);
                BlendOver(result, s, o.Weight);
                result.BodyMotion = HumanRootMotion.Override(result.BodyMotion, s.BodyMotion, o.Weight);
                if (o.Weight > 0f && s.FootIK) result.FootIK = true;
                result.ClipSampled |= s.ClipSampled;
                result.JobRan |= s.JobRan;
                if (s.HasMassCenter && o.Weight >= 1f)
                {
                    result.MassCenter ??= new float[Unbound.Runtime.Humanoid.HumanClipImport.CurveCount];
                    Array.Copy(s.MassCenter, result.MassCenter, result.MassCenter.Length);
                    result.HasMassCenter = true;
                }
                AnimStream.Return(s);
            }
            return result;
        }

        /// <summary>Writes an output pose: a culled Animator only its own Transform, the rest also
        /// the humanoid pose with its IK pass. <paramref name="preserveRoot"/> or
        /// <paramref name="rootWritten"/> (the movement step wrote it) leave the root alone.</summary>
        internal static void WriteOutput(Animator animator, AnimStream result, bool culled, bool playableIK, bool preserveRoot = false,
            bool rootWritten = false)
        {
            if (result.BodyMotion.HasMotion) animator.Human?.SetPoseMotion(result.BodyMotion);
            if (result.FootIK && animator.Human is { } footHuman) footHuman.FootIK = true;
            if (result.HasMassCenter && animator.Human is { } massHuman)
            {
                var b = result.Bindings;
                for (int curve = 0; curve < Unbound.Runtime.Humanoid.HumanClipImport.CurveCount; curve++)
                {
                    int c = b.HumanChannel(curve);
                    if (c >= 0 && result.Animated[c]) massHuman.SetMassCenter(curve, result.MassCenter[curve]);
                }
            }
            // The pose takes goals relative to its body (HumanRootMotion.ApplyToPose); the outputs'
            // blend already did so, a stream written as it is (a replay) does it here.
            GoalsToBody(result, result.BodyMotion);
            if (!(culled && rootWritten))
                PoseWriter.WriteStream(result, onlyRoot: culled ? animator.transform : null,
                    skipRoot: preserveRoot || rootWritten ? animator.transform : null);
            // An output of clips that wrote no humanoid curve poses the avatar by default (measured
            // without a controller); a controller's own pose and a job's bone writes stay (Unbound's
            // choice, unmeasured).
            if (!culled && animator.m_Player == null && result.ClipSampled && !result.JobRan) animator.Human?.DefaultPoseUnlessWritten();
            if (!culled) animator.ApplyPlayableHumanPose(playableIK);
        }

        /// <summary>The result of the outputs before (a PreviousInputs output's starting stream).</summary>
        internal AnimStream PreviousInputs;


        /// <summary>One humanoid clip, or one mixer or layer mixer tree of humanoid clips
        /// (<see cref="MixerKind.HumanMotionTree"/>), through a single full-weight default-values
        /// output without an attached controller, uses the humanoid sampler. Mixer composition is
        /// measured on 6000.0.71f1 only (<see cref="Compat.HumanPlayableMixerMotionMeasured"/>).
        /// Such an output of clips without humanoid curves, mixers and layer mixers moves a
        /// humanoid by generic root motion (<see cref="GenericClipTree"/>). An
        /// AnimatorControllerPlayable with humanoid clips as such an output's source moves the
        /// Animator by its clips' humanoid root motion, as the Animator's own controller does.
        /// Other humanoid graph shapes warn and carry no motion.</summary>
        static RootMotionMode MotionModeOf(Animator animator, List<(PlayableOutputData Output, double Dt, bool Seek)> outputs)
        {
            bool requested = animator.m_ApplyRootMotion || MoveCallbacks.Any(animator);
            if (!animator.isHuman) return requested ? RootMotionMode.Compute : RootMotionMode.Zero;
            if (animator.m_Player == null && outputs.Count == 1 && AnimationOutputs.Of(animator)?.Count == 1)
            {
                var o = outputs[0].Output;
                // Outputs of the same graph for other Animators do not take part.
                if (!o.Destroyed && o.Weight == 1f
                    && (o.Engine as AnimationOutputState)?.StreamSource == AnimationStreamSource.DefaultValues)
                {
                    // Animation jobs pass their input's humanoid motion on (6000.0.71f1,
                    // tests/projects/humanoid stream.*).
                    var source = BelowJobs(o.Source);
                    if (HumanMotionClip(source)) return RootMotionMode.Human;
                    // A controller with humanoid clips moves the Animator as its own controller
                    // would (6000.0.71f1, tests/projects/humanoid controllerGraph.*).
                    if (o.Source.Kind is ControllerKind { HasHumanClips: true } && !o.Source.HasConnectedOutput()) return RootMotionMode.Human;
                    // Generic root curves alone move a humanoid as a generic Animator's, by its
                    // human scale (6000.0.71f1, tests/projects/humanoid genericOnHuman.graph).
                    if (source == o.Source && GenericClipTree(o.Source)) return requested ? RootMotionMode.Compute : RootMotionMode.Zero;
                    if (!o.Source.HasConnectedOutput() && MixerKind.HumanMotionTree(source))
                        return RootMotionMode.Human;
                }
            }
            if (requested)
                Stubs.Ignored(outputs.Count == 1 && outputs[0].Output.Source?.Kind is MixerKind
                    ? "Animator root motion of unsupported humanoid mixer output"
                    : "Animator root motion of composed playable graph outputs on humanoid avatars",
                    "is not applied: one full-weight output of a nonmirrored humanoid clip, or on measured 6000.0.71f1 of a mixer or layer mixer tree of such clips "
                    + "with input weights that are not infinite, or of clips without humanoid curves, mixers and layer mixers, "
                    + "or of an AnimatorControllerPlayable with humanoid clips, "
                    + "is supported, without a playing controller, shared nodes or loop overrides; "
                    + "controller playables below mixers or jobs, animation jobs other than ones passing such a source on whole, "
                    + "fractional or multiple outputs remain unsupported");
            return RootMotionMode.Unsupported;
        }

        /// <summary>The node below a chain of animation jobs that pass their first input on whole
        /// (input weight 1; the node itself when it is not such a job).</summary>
        static PlayableNode BelowJobs(PlayableNode node)
        {
            for (int depth = 0; depth < 64 && node != null && !node.Destroyed && node.Kind is JobKind { ProcessInputs: true }; depth++)
            {
                if (node.Inputs.Count == 0 || node.Inputs[0].Node == null || node.Inputs[0].Weight != 1f || node.Inputs[0].SourcePort != 0
                    || MixerKind.ConsumerCount(node.Inputs[0].Node) != 1) return null;
                node = node.Inputs[0].Node;
            }
            return node;
        }

        /// <summary>Whether a tree is only clips without humanoid curves, mixers and layer mixers.</summary>
        static bool GenericClipTree(PlayableNode node, int depth = 0)
        {
            if (node == null || node.Destroyed || depth > 64) return false;
            if (node.Kind is ClipKind clip) return clip.Clip != null && !clip.Clip.HasHumanCurves;
            if (node.Kind is not (MixerKind or LayerMixerKind)) return false;
            int connected = 0;
            foreach (var input in node.Inputs)
            {
                if (input.Node == null) continue;
                connected++;
                if (!GenericClipTree(input.Node, depth + 1)) return false;
            }
            return connected > 0;
        }

        internal static bool HumanMotionClip(PlayableNode node) => node != null && !node.Destroyed
            && node.Kind is ClipKind clip && clip.Clip != null && clip.Clip.HasHumanCurves
            && clip.Clip.length > 0f && !clip.Clip.HumanClipMotion.Settings.Mirror && !clip.OverrideLoopTime;

        /// <summary>Apply the already sampled humanoid motion. Visibility replay keeps the last
        /// local step without stepping the feet-pivot memory again.</summary>
        internal static void ApplyHumanRootMotion(Animator animator, in AnimatorRootMotion motion, bool dispatch = true)
        {
            animator.CaptureRootMotionPose();
            animator.m_RootMotionApplied = false;
            bool requested = animator.m_ApplyRootMotion || MoveCallbacks.Any(animator);
            // The public pivot stays zero while no root motion is requested (6000.0.71f1,
            // animculling humanMixerMotion and humanMixerShape); the feet memory still steps.
            if (!requested) animator.Human.PivotPosition = default;
            animator.Human.PivotHidden = !requested;
            animator.m_DeltaPosition = requested ? new Vector3(motion.DeltaPosition.x, motion.DeltaPosition.y, motion.DeltaPosition.z) : Vector3.zero;
            animator.m_DeltaRotation = requested ? new Quaternion(motion.DeltaRotation.x, motion.DeltaRotation.y, motion.DeltaRotation.z, motion.DeltaRotation.w) : Quaternion.identity;
            animator.m_RootVelocity = requested ? new Vector3(motion.Velocity.x, motion.Velocity.y, motion.Velocity.z) : Vector3.zero;
            animator.m_RootAngularVelocity = requested ? new Vector3(motion.AngularVelocity.x, motion.AngularVelocity.y, motion.AngularVelocity.z) : Vector3.zero;
            if (dispatch) DispatchRootMotion(animator);
        }

        /// <summary>
        /// The outputs' root motion moves the Animator, as its own root motion does: the root's
        /// position and rotation channels are not written; deltaPosition, deltaRotation, velocity and
        /// angularVelocity take the motion (over the outputs' delta time); OnAnimatorMove runs on the
        /// enabled scripts that implement it, else applyRootMotion applies the motion
        /// (ApplyBuiltinRootMotion). Root motion the outputs do not carry (offset, motion-to-delta,
        /// remove-scale and pose playables, empty mixers and layer mixers, humanoid clips in a
        /// controller playable)
        /// is not approximated: it warns once, the root keeps the graph's root
        /// curves, and the Animator reports no motion. Unbound's choice (unmeasured): the motion is
        /// applied inside each Animator's output evaluation, before its pose is written.
        /// </summary>
        internal static void ApplyRootMotion(Animator animator, AnimStream result, RootMotionStep motion, float deltaTime, bool dispatch = true)
        {
            animator.CaptureRootMotionPose();
            animator.m_RootMotionApplied = false;
            animator.m_DeltaPosition = Vector3.zero;
            animator.m_DeltaRotation = Quaternion.identity;
            animator.m_RootVelocity = Vector3.zero;
            animator.m_RootAngularVelocity = Vector3.zero;
            if (!motion.Known)
            {
                Stubs.Ignored("Animator root motion of this playable graph output",
                    "is not computed (only animation clips, mixers, layer mixers, controller playables and animation jobs carry it; offset, motion-to-delta, remove-scale and pose playables, mixers and layer mixers without inputs and controller playables playing humanoid clips do not): the root keeps the graph's root curves and the Animator reports no motion");
                if (dispatch && animator.m_Player != null) animator.m_Player.ProcessRootMotion();
                return;
            }
            var t = animator.transform;
            var b = result.Bindings;
            int pos = b.ChannelOf(t, "position"), rot = b.ChannelOf(t, "rotation");
            if (pos >= 0) for (int c = pos; c < pos + 3 && c < result.FloatCount; c++) result.Animated[c] = false;
            if (rot >= 0) for (int c = rot; c < rot + 4 && c < result.FloatCount; c++) result.Animated[c] = false;
            RootMotionStep.RootFrame(animator, out _, out _, out var scale);
            // A humanoid Animator moves by its human scale, its pivot the moved root, as with its
            // controller (6000.0.71f1, tests/projects/humanoid genericOnHuman, genericOnHuman.graph).
            var human = animator.Human;
            var delta = human != null ? motion.Delta * animator.humanScale : motion.Delta;
            animator.m_DeltaPosition = animator.m_MotionRootRotation * Vector3.Scale(scale, delta);
            if (human != null)
            {
                var moved = animator.m_MotionRootPosition + animator.m_DeltaPosition;
                human.PivotPosition = new Unbound.Runtime.Humanoid.F4(moved.x, moved.y, moved.z, 0f);
            }
            animator.m_DeltaRotation = motion.Turn;
            RootMotionStep.Velocities(animator.m_DeltaPosition, animator.m_DeltaRotation, animator.m_MotionRootRotation, deltaTime,
                out animator.m_RootVelocity, out animator.m_RootAngularVelocity);
            if (!dispatch) return;
            DispatchRootMotion(animator);
        }

        static void DispatchRootMotion(Animator animator)
        {
            if (animator.m_Player != null)
            {
                animator.m_Player.ProcessRootMotion();
                return;
            }
            animator.m_RootMotionReady = true;
            try
            {
                bool hasMove = MoveCallbacks.Invoke(animator);
                // applyRootMotion has no effect when a script implements OnAnimatorMove (Scripting API).
                if (animator.m_ApplyRootMotion && !hasMove) animator.ApplyBuiltinRootMotion();
                // API setters inside movement callbacks do not write the generic root pose.
                animator.CaptureRootMotionPose();
            }
            finally
            {
                // ApplyBuiltinRootMotion only acts during movement callbacks, once per evaluation.
                animator.m_RootMotionReady = false;
            }
        }

        /// <summary>An output keeps animating every channel it has animated since its source was
        /// set (changes below the source do not unbind them): the ones its playables leave alone
        /// this time get their default values.</summary>
        static void KeepBound(PlayableOutputData o, AnimStream s)
        {
            if (!(o.Engine is AnimationOutputState state)) return;
            if (state.BoundSource != o.Source || state.BoundPort != o.SourcePort || state.Bound == null)
            {
                state.BoundSource = o.Source;
                state.BoundPort = o.SourcePort;
                state.Bound = new bool[s.FloatCount];
                state.ObjectBound = new bool[s.ObjectCount];
            }
            if (state.Bound.Length < s.FloatCount) Array.Resize(ref state.Bound, s.FloatCount);
            if (state.ObjectBound.Length < s.ObjectCount) Array.Resize(ref state.ObjectBound, s.ObjectCount);
            var defaults = s.Bindings.Defaults;
            for (int c = 0; c < s.FloatCount; c++)
            {
                if (s.Animated[c]) state.Bound[c] = true;
                else if (state.Bound[c])
                {
                    s.Values[c] = defaults[c];
                    s.Animated[c] = true;
                }
            }
            for (int c = 0; c < s.ObjectCount; c++)
            {
                if (s.ObjectAnimated[c]) state.ObjectBound[c] = true;
                else if (state.ObjectBound[c])
                {
                    s.Objects[c] = s.Bindings.ObjectDefaults[c];
                    s.ObjectAnimated[c] = true;
                }
            }
        }
    }

    /// <summary>One Animator's evaluated playable outputs between their evaluation and their
    /// movement and write (<see cref="AnimationPhases"/>); pooled.</summary>
    internal sealed class PendingOutput
    {
        internal Animator Animator;
        internal AnimStream Result;
        internal RootMotionMode Mode;
        internal RootMotionStep Motion;
        internal AnimatorRootMotion HumanStep;
        internal AnimatorRootMotion? HumanMotion;
        internal float DeltaTime;
        internal bool SuppressMovement, ControllerMovement, PlayableIK, Manual;
        internal int Weights;
        /// <summary>The Animator's output evaluation count when sampled: a later evaluation (an
        /// explicit one between the two leaves) makes this write stale.</summary>
        internal int Evaluation;
        internal readonly List<ClipEventInterval> Events = new List<ClipEventInterval>();

        static readonly Stack<PendingOutput> s_Free = new Stack<PendingOutput>();

        internal static PendingOutput Rent() => s_Free.Count > 0 ? s_Free.Pop() : new PendingOutput();

        /// <summary>Movement: OnAnimatorMove (or builtin root motion) with this evaluation's deltas,
        /// then the write of the Animator's own Transform; the rest waits for <see cref="Write"/>
        /// (6000.0.71f1, tests/projects/animcallbacks and playables anim.frames).</summary>
        internal void Move()
        {
            if (Result == null || Evaluation != Animator.m_OutputEvaluations) return;
            if (Mode == RootMotionMode.Human) AnimationEvaluation.ApplyHumanRootMotion(Animator, HumanStep, dispatch: !SuppressMovement);
            else if (Mode == RootMotionMode.Compute) AnimationEvaluation.ApplyRootMotion(Animator, Result, Motion, DeltaTime, dispatch: !SuppressMovement);
            else if (!SuppressMovement && ControllerMovement) Animator.m_Player?.ProcessRootMotion();
            // A movement callback may have evaluated the outputs again or disabled the Animator.
            if (Result == null || Evaluation != Animator.m_OutputEvaluations || !Animator.isActiveAndEnabled) return;
            PoseWriter.WriteStream(Result, onlyRoot: Animator.transform);
            RootWritten = true;
        }

        /// <summary><see cref="Move"/> wrote the root: <see cref="Write"/> leaves it.</summary>
        internal bool RootWritten;

        /// <summary>Writes the pose (with its IK pass) and keeps it for a later visibility return;
        /// a disabled Animator only drops its retained sample.</summary>
        internal void Write(bool live)
        {
            var result = Result;
            if (result == null) return;
            if (live && Evaluation != Animator.m_OutputEvaluations) return;
            Result = null;
            if (!live)
            {
                AnimationOutputs.ForgetCulled(Animator);
                AnimStream.Return(result);
                return;
            }
            bool culled = Animator.cullingMode != AnimatorCullingMode.AlwaysAnimate && !Animator.AnyRendererVisible(Animator.gameObject);
            AnimationEvaluation.WriteOutput(Animator, result, culled, PlayableIK, rootWritten: RootWritten);
            var culledPose = AnimationOutputs.RememberCulled(Animator, result, Motion, DeltaTime, Mode, PlayableIK, Manual, HumanMotion);
            culledPose.Weights = Weights;
            culledPose.Events.Clear();
            culledPose.Events.AddRange(Events);
        }

        internal void Release()
        {
            if (Result != null) AnimStream.Return(Result);
            Result = null;
            RootWritten = false;
            Animator = null;
            HumanMotion = null;
            Events.Clear();
            s_Free.Push(this);
        }
    }

    /// <summary>The animation outputs that target each Animator, and their evaluation: at once for
    /// PlayableGraph.Evaluate, with the Animators in the animation phase for playing graphs.</summary>
    internal sealed class AnimationOutputs : OutputSystems.IProcessor
    {
        static readonly Dictionary<Animator, List<PlayableOutputData>> s_ByAnimator = new Dictionary<Animator, List<PlayableOutputData>>();
        static readonly Dictionary<Animator, List<(PlayableOutputData, double, bool)>> s_Pending = new Dictionary<Animator, List<(PlayableOutputData, double, bool)>>();
        /// <summary>An Animator's last output sample, kept for its next visibility return.</summary>
        internal sealed class CulledPose
        {
            internal AnimStream Pose;
            internal RootMotionStep Motion;
            internal AnimatorRootMotion? HumanMotion;
            internal float DeltaTime;
            internal RootMotionMode Mode;
            internal bool IK, Manual;
            /// <summary><see cref="AnimationOutputs.TreeWeights"/> when sampled.</summary>
            internal int Weights;
            internal readonly List<ClipEventInterval> Events = new List<ClipEventInterval>();
            internal bool Writing, Retired;
        }
        static readonly Dictionary<Animator, CulledPose> s_Culled = new Dictionary<Animator, CulledPose>();
        static readonly Stack<CulledPose> s_FreeCulled = new Stack<CulledPose>();

        static void Recycle(CulledPose frame)
        {
            AnimStream.Return(frame.Pose);
            frame.Pose = null;
            frame.Events.Clear();
            frame.Writing = frame.Retired = false;
            s_FreeCulled.Push(frame);
        }
        static bool s_Registered;

        internal static void EnsureRegistered()
        {
            if (s_Registered) return;
            s_Registered = true;
            OutputSystems.Register(typeof(AnimationPlayableOutput), new AnimationOutputs());
        }

        internal static void TargetChanged(PlayableOutputData output, Animator old, Animator target)
        {
            if (output == null) return;
            EnsureRegistered();
            if (old != null && s_ByAnimator.TryGetValue(old, out var list))
            {
                list.Remove(output);
                ForgetCulled(old);
                if (list.Count == 0) s_ByAnimator.Remove(old);
            }
            if (target != null)
            {
                if (!s_ByAnimator.TryGetValue(target, out list)) s_ByAnimator[target] = list = new List<PlayableOutputData>();
                if (!list.Contains(output)) list.Add(output);
            }
        }

        internal static List<PlayableOutputData> Of(Animator animator) => s_ByAnimator.TryGetValue(animator, out var list) ? list : null;

        internal static bool HasPending(Animator animator) => s_Pending.TryGetValue(animator, out var list) && list.Count > 0;

        // The attached controller's write-defaults base also owns channels bound by its outputs
        // (6000.0.71f1, animculling explicit/*/Manual/controller). A later controller sample
        // restores them even when that graph has no automatic evaluation pending.
        /// <summary>Fills the union of the channels the Animator's live outputs have bound;
        /// false (arrays untouched) when it has no such output.</summary>
        internal static bool BoundChannels(Animator animator, ref bool[] floats, ref bool[] objects, int floatCount, int objectCount)
        {
            if (Of(animator) is not { } outputs) return false;
            bool any = false;
            foreach (var output in outputs)
            {
                if (output.Destroyed || output.Engine is not AnimationOutputState state || state.Bound == null) continue;
                if (!any)
                {
                    if (floats.Length < floatCount) floats = new bool[floatCount];
                    if (objects.Length < objectCount) objects = new bool[objectCount];
                    Array.Clear(floats, 0, floatCount);
                    Array.Clear(objects, 0, objectCount);
                    any = true;
                }
                for (int c = Math.Min(floatCount, state.Bound.Length) - 1; c >= 0; c--) floats[c] |= state.Bound[c];
                if (state.ObjectBound == null) continue;
                for (int c = Math.Min(objectCount, state.ObjectBound.Length) - 1; c >= 0; c--) objects[c] |= state.ObjectBound[c];
            }
            return any;
        }

        internal static void ForgetCulled(Animator animator)
        {
            if (!s_Culled.Remove(animator, out var frame)) return;
            frame.Retired = true;
            if (!frame.Writing) Recycle(frame);
        }

        /// <summary>Replaces the Animator's retained sample (taking <paramref name="pose"/>); the
        /// caller fills the returned record's events.</summary>
        internal static CulledPose RememberCulled(Animator animator, AnimStream pose, RootMotionStep motion, float dt, RootMotionMode mode, bool ik, bool manual, AnimatorRootMotion? humanMotion)
        {
            if (s_Culled.TryGetValue(animator, out var frame) && !frame.Writing)
            {
                // The usual case: reuse the record in place.
                AnimStream.Return(frame.Pose);
                frame.Events.Clear();
            }
            else
            {
                if (frame != null) frame.Retired = true;
                frame = s_FreeCulled.Count > 0 ? s_FreeCulled.Pop() : new CulledPose();
                s_Culled[animator] = frame;
            }
            frame.Pose = pose;
            frame.Motion = motion;
            frame.HumanMotion = humanMotion;
            frame.DeltaTime = dt;
            frame.Mode = mode;
            frame.IK = ik;
            frame.Manual = manual;
            return frame;
        }

        /// <summary>A hash of the input weights of every live output's source tree of the Animator,
        /// to tell whether they changed since its retained sample.</summary>
        internal static int TreeWeights(Animator animator)
        {
            int hash = 17;
            if (Of(animator) is { } outputs)
                foreach (var output in outputs)
                    if (!output.Destroyed && output.Source != null) hash = hash * 31 + MixerKind.TreeWeights(output.Source);
            return hash;
        }

        static readonly List<(PlayableOutputData, double, bool)> s_Replay = new List<(PlayableOutputData, double, bool)>();

        /// <summary>Mixer weights changed after the retained sample (updates skipped, or a Manual
        /// graph not evaluated): the visibility return blends the retained clip poses and steps
        /// with the current weights (6000.0.71f1, tests/projects/animculling humanMixerShape/live
        /// and genericMixerSum/liveCulled). The held tree is evaluated again in replay mode: clips
        /// sample their held time and reuse their last step; no time, event or feet-pivot memory
        /// advances. Measured for clip and mixer trees without a controller, through the held
        /// tree of <see cref="Compat.PlayableCullingHoldsAnimationPlayablesOnly"/>; other trees
        /// (layer mixers, jobs, plain playables), an attached controller's pose and other
        /// releases keep replaying the last blend and say so.</summary>
        static void Reblend(Animator animator, CulledPose frame)
        {
            if (Of(animator) is not { } outputs || animator.StreamBindings is not { } bindings) return;
            bool measured = Compat.PlayableCullingHoldsAnimationPlayablesOnly && animator.m_Player == null;
            foreach (var output in outputs)
                if (!output.Destroyed && output.Source != null && !MixerKind.ClipMixerTree(output.Source)) measured = false;
            if (!measured)
            {
                Stubs.Ignored("Animator visibility return after mixer weights changed (attached controller, other playables or unmeasured release)",
                    "replays the pose and root motion blended with the weights of the last evaluation");
                return;
            }
            s_Replay.Clear();
            foreach (var output in outputs)
                if (!output.Destroyed && output.Source != null && !output.Graph.Destroyed) s_Replay.Add((output, frame.DeltaTime, false));
            if (s_Replay.Count == 0) return;
            var e = AnimationEvaluation.RentReplay(animator, bindings);
            try
            {
                var result = AnimationEvaluation.SampleReplay(e, animator, bindings, s_Replay, out var motion);
                AnimStream.Return(frame.Pose);
                frame.Pose = result;
                frame.Motion = motion;
                if (frame.HumanMotion is { } human)
                {
                    human.Delta = result.BodyMotion.Delta;
                    frame.HumanMotion = human;
                    if (result.BodyMotion.HasMotion) animator.Human?.RepivotMotion(result.BodyMotion);
                }
                frame.Weights = TreeWeights(animator);
            }
            finally
            {
                AnimationEvaluation.ReleaseReplay(e);
                s_Replay.Clear();
            }
        }

        internal static bool EvaluateOnVisible(Animator animator, bool move)
        {
            if (!s_Culled.TryGetValue(animator, out var frame))
            {
                // A Manual graph need not have evaluated yet. First visibility still writes
                // its zero-time pose and invokes IK; an attached controller that already
                // delivered movement for CullUpdateTransforms does not deliver it again
                // (Unity 6000.0.71f1, animculling explicit/human and explicit/later).
                if (Of(animator) is not { } outputs) return false;
                var initial = new List<(PlayableOutputData, double, bool)>();
                foreach (var output in outputs)
                {
                    if (output.Destroyed || output.Source == null || output.Graph.Destroyed
                        || !output.Graph.Playing || output.Graph.UpdateMode != DirectorUpdateMode.Manual) continue;
                    if (output.Engine is not AnimationOutputState state || state.Ticked) continue;
                    state.Ticked = true;
                    state.Automatic = false;
                    initial.Add((output, 0, true));
                }
                if (initial.Count == 0) return false;
                AnimationEvaluation.Run(animator, initial, suppressMovement: animator.m_Player != null && !move);
                return true;
            }
            // Manual outputs can return to visibility more than once without evaluation. Keep
            // their last sample until replaced or invalidated. A movement callback may replace
            // it reentrantly, so defer returning that stream until this write finishes.
            if (frame.Writing) return false;
            frame.Writing = true;
            try
            {
                if (move) animator.m_RootMotionApplied = false;
                if (frame.Weights != TreeWeights(animator)) Reblend(animator, frame);
                if (frame.HumanMotion is { } humanMotion)
                {
                    if (move)
                    {
                        animator.Human.ReplayMotion(humanMotion, frame.DeltaTime, out var replay);
                        AnimationEvaluation.ApplyHumanRootMotion(animator, replay);
                    }
                    else animator.CaptureRootMotionPose();
                }
                else if (move && frame.Mode == RootMotionMode.Compute)
                    AnimationEvaluation.ApplyRootMotion(animator, frame.Pose, frame.Motion,
                        animator.m_Player?.RootMotionDeltaTime ?? frame.DeltaTime);
                else if (move) animator.m_Player?.ProcessRootMotion();
                AnimationEvaluation.WriteOutput(animator, frame.Pose, false, frame.IK,
                    preserveRoot: !move && frame.Manual && frame.Mode == RootMotionMode.Zero && animator.m_Player != null);
                // Indexed: an event handler may replace this record (it is then retired).
                if (move)
                    for (int i = 0; i < frame.Events.Count; i++) frame.Events[i].Fire(animator);
            }
            finally
            {
                frame.Writing = false;
                if (frame.Retired) Recycle(frame);
            }
            return true;
        }

        public void Process(PlayableOutputData output, double dt, bool seek)
        {
            var target = (output.Engine as AnimationOutputState)?.Target;
            if (target == null || !target.isActiveAndEnabled) return;
            if ((output.Engine as AnimationOutputState)?.SkipEvaluation == true) return;
            if (!s_Pending.TryGetValue(target, out var list)) s_Pending[target] = list = RentList();
            list.Add((output, dt, seek));
            output.Graph.DeliverNotifications(output);
        }

        public void Stopped(PlayableGraphData graph) { }

        public void Flush() => RunAllPending();

        /// <summary>The pending outputs of one Animator (after its controller wrote).</summary>
        static readonly Stack<List<(PlayableOutputData, double, bool)>> s_FreeLists = new Stack<List<(PlayableOutputData, double, bool)>>();
        static readonly Stack<List<KeyValuePair<Animator, List<(PlayableOutputData, double, bool)>>>> s_FreeBatches =
            new Stack<List<KeyValuePair<Animator, List<(PlayableOutputData, double, bool)>>>>();

        static List<(PlayableOutputData, double, bool)> RentList() =>
            s_FreeLists.Count > 0 ? s_FreeLists.Pop() : new List<(PlayableOutputData, double, bool)>();

        static void ReturnList(List<(PlayableOutputData, double, bool)> list)
        {
            list.Clear();
            s_FreeLists.Push(list);
        }

        internal static void RunPending(Animator animator, bool deferPhases = false)
        {
            if (s_Pending.Count == 0 || !s_Pending.TryGetValue(animator, out var list)) return;
            s_Pending.Remove(animator);
            try { AnimationEvaluation.Run(animator, list, deferPhases: deferPhases); }
            catch (Exception e) { Debug.LogException(e, animator); }
            finally
            {
                if (animator.m_Player != null)
                {
                    animator.m_Player.DeferOutput = false;
                    animator.m_Player.RunDeferredCallbacks();
                }
                ReturnList(list);
            }
        }

        /// <summary>Every pending output (PlayableGraph.Evaluate, and Animators the update skipped).
        /// Evaluations can queue more reentrantly, so each call takes its own batch.</summary>
        internal static void RunAllPending(bool deferPhases = false)
        {
            if (s_Pending.Count == 0) return;
            var all = s_FreeBatches.Count > 0 ? s_FreeBatches.Pop() : new List<KeyValuePair<Animator, List<(PlayableOutputData, double, bool)>>>();
            foreach (var pair in s_Pending) all.Add(pair);
            s_Pending.Clear();
            try
            {
                foreach (var pair in all)
                {
                    if (pair.Key == null) continue;
                    try { AnimationEvaluation.Run(pair.Key, pair.Value, deferPhases: deferPhases); }
                    catch (Exception e) { Debug.LogException(e, pair.Key); }
                }
            }
            finally
            {
                foreach (var pair in all) ReturnList(pair.Value);
                all.Clear();
                s_FreeBatches.Push(all);
            }
        }
    }
}
