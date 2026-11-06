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
        /// This places the pose; humanoid output root movement remains unsupported.</summary>
        internal HumanRootMotionSample BodyMotion;

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
            Array.Copy(o.Values, Values, FloatCount);
            Array.Copy(o.Animated, Animated, FloatCount);
            Array.Copy(o.Objects, Objects, ObjectCount);
            Array.Copy(o.ObjectAnimated, ObjectAnimated, ObjectCount);
        }
    }

    /// <summary>How the streams of one evaluation carry root motion.</summary>
    internal enum RootMotionMode
    {
        /// <summary>Not computed (humanoid avatars, Animators whose own controller plays): streams
        /// carry no motion and the graph's root curves pose the root as before.</summary>
        Unsupported,
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
        /// <summary>Under a motion-to-delta playable: clips remove their root start offset.</summary>
        internal bool RootMotion;
        /// <summary>Evaluating an additive layer's reference: clips sample their first frame.</summary>
        internal bool ReferencePose;
        internal bool PlayableIK;
        /// <summary>How this evaluation's streams carry root motion (set by <see cref="Run"/>).</summary>
        internal RootMotionMode MotionMode;
        readonly List<(AnimationKind Kind, PlayableNode Node)> m_Evaluated = new List<(AnimationKind, PlayableNode)>();

        internal void Note(AnimationKind kind, PlayableNode node) => m_Evaluated.Add((kind, node));

        /// <summary>A playable's pose into a fresh stream (the caller returns it).</summary>
        internal AnimStream Evaluate(PlayableNode n, int viaPort, float weight, int depth = 0)
        {
            var s = AnimStream.Rent(Bindings);
            s.DeltaTime = DeltaTime;
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
            foreach (var (kind, node) in m_Evaluated)
            {
                if (node.Destroyed) continue;
                try { kind.AfterWrite(this, node); }
                catch (Exception ex) { Debug.LogException(ex); }
            }
            m_Evaluated.Clear();
        }

        // ------------------------------------------------------------------ channel math

        static bool IsQuaternionStart(AnimationBindings b, int c, out int width)
        {
            var g = c < b.FloatOwner.Length ? b.FloatOwner[c] : null;
            width = 1;
            if (g == null || !g.IsQuaternion || g.First != c) return false;
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

        /// <summary>The mixer rule over input streams: per channel, the weighted sum of the inputs
        /// that animate it, completed to weight one with the default value.</summary>
        internal static void MixInto(AnimStream into, List<(AnimStream Stream, float Weight)> inputs)
        {
            if (inputs.Exists(input => input.Stream.BodyMotion.HasMotion))
            {
                var samples = new HumanRootMotionSample[inputs.Count];
                var weights = new float[inputs.Count];
                for (int i = 0; i < inputs.Count; i++)
                {
                    samples[i] = inputs[i].Stream.BodyMotion;
                    weights[i] = inputs[i].Weight;
                }
                into.BodyMotion = HumanRootMotion.Blend(samples, weights);
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
        internal static void Run(Animator animator, List<(PlayableOutputData Output, double Dt, bool Seek)> outputs)
        {
            if (animator == null || outputs.Count == 0) return;
            var bindings = animator.StreamBindings;
            if (bindings == null) return;
            var e = new AnimationEvaluation { Animator = animator, Bindings = bindings };
            outputs.Sort((a, b) =>
            {
                int sa = (a.Output.Engine as AnimationOutputState)?.SortingOrder ?? 100;
                int sb = (b.Output.Engine as AnimationOutputState)?.SortingOrder ?? 100;
                if (sa != sb) return sa.CompareTo(sb);
                return a.Output.Graph.Outputs.IndexOf(a.Output).CompareTo(b.Output.Graph.Outputs.IndexOf(b.Output));
            });
            foreach (var (o, _, _) in outputs)
                if (o.Source != null) e.Bind(o.Source);
            var result = AnimStream.Rent(bindings);
            var withController = animator.m_Player != null && animator.isActiveAndEnabled;
            e.MotionMode = MotionModeOf(animator, withController);
            var motion = RootMotionStep.Zero;
            float motionDeltaTime = (float)outputs[0].Dt;
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
                    animator.m_Player.EvaluateInto(result.Values, result.Animated, result.Objects, result.ObjectAnimated);
                if (e.MotionMode == RootMotionMode.Compute)
                    motion = RootMotionStep.FromVelocities(animator, motionDeltaTime, animator.velocity, animator.angularVelocity);
            }
            foreach (var (o, dt, seek) in outputs)
            {
                // An output at weight 0 still holds its channels (they show the base values).
                if (o.Destroyed || o.Source == null) continue;
                e.DeltaTime = (float)dt;
                e.Seek = seek;
                var source = (o.Engine as AnimationOutputState)?.StreamSource ?? AnimationStreamSource.DefaultValues;
                AnimStream s;
                if (source == AnimationStreamSource.PreviousInputs)
                {
                    // Rigging layers: the stream starts as the result of the outputs before.
                    s = AnimStream.Rent(bindings);
                    s.DeltaTime = e.DeltaTime;
                    s.CopyFrom(result);
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
                BlendOver(result, s, o.Weight);
                result.BodyMotion = HumanRootMotion.Override(result.BodyMotion, s.BodyMotion, o.Weight);
                AnimStream.Return(s);
            }
            if (e.MotionMode == RootMotionMode.Compute) ApplyRootMotion(animator, result, motion, motionDeltaTime);
            else if (withController && animator.m_Player.DeferOutput) animator.m_Player.ProcessRootMotion();
            bool culled = outputs.Exists(o => (o.Output.Engine as AnimationOutputState)?.Automatic == true)
                && animator.cullingMode != AnimatorCullingMode.AlwaysAnimate && !Animator.AnyRendererVisible(animator.gameObject);
            WriteOutput(animator, result, culled, e.PlayableIK);
            AnimationOutputs.ForgetCulled(animator);
            if (animator.isActiveAndEnabled && outputs.Exists(o => (o.Output.Engine as AnimationOutputState)?.Automatic == true))
                AnimationOutputs.RememberCulled(animator, result, motion, motionDeltaTime, e.MotionMode, e.PlayableIK);
            else AnimStream.Return(result);
            e.AfterWrite();
        }

        internal static void WriteOutput(Animator animator, AnimStream result, bool culled, bool playableIK)
        {
            if (result.BodyMotion.HasMotion) animator.Human?.SetPoseMotion(result.BodyMotion);
            PoseWriter.WriteStream(result.Bindings, result.Values, result.Animated, result.Objects, result.ObjectAnimated,
                onlyRoot: culled ? animator.transform : null);
            if (!culled) animator.ApplyPlayableHumanPose(playableIK);
        }

        /// <summary>The result of the outputs before (a PreviousInputs output's starting stream).</summary>
        internal AnimStream PreviousInputs;

        static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, System.Reflection.MethodInfo> s_OnAnimatorMove =
            new System.Collections.Concurrent.ConcurrentDictionary<Type, System.Reflection.MethodInfo>();

        static System.Reflection.MethodInfo MoveMethod(MonoBehaviour behaviour) => s_OnAnimatorMove.GetOrAdd(behaviour.GetType(),
            type => ScriptMessages.Find(type, "OnAnimatorMove", Type.EmptyTypes));

        /// <summary>A script on the Animator's GameObject implements OnAnimatorMove (enabled or not,
        /// as for the Animator's own root motion).</summary>
        static bool HasMoveCallback(Animator animator)
        {
            foreach (var mb in animator.gameObject.GetComponents<MonoBehaviour>())
                if (mb != null && MoveMethod(mb) != null) return true;
            return false;
        }

        /// <summary>Root motion of a graph's outputs is computed for a generic Animator when it
        /// requests root motion (applyRootMotion, or an
        /// OnAnimatorMove script, as for its own root motion). A humanoid Animator's is not (its
        /// root movement is not implemented for streams): requesting it warns once and the graph's
        /// root curves keep posing the root. During automatic evaluation the attached controller defers movement to this
        /// combined output. Explicit graph evaluation keeps its independent behavior.
        /// Unbound's choice (unmeasured): the OnAnimatorMove gate, as for the Animator's own root
        /// motion; the docs name applyRootMotion only.</summary>
        static RootMotionMode MotionModeOf(Animator animator, bool withController)
        {
            // The attached controller defers movement to the combined output during automatic
            // evaluation. Explicit graph evaluation retains its previous independent behavior.
            if (withController && !animator.m_Player.DeferOutput) return RootMotionMode.Unsupported;
            bool requested = animator.m_ApplyRootMotion || HasMoveCallback(animator);
            if (animator.isHuman)
            {
                if (requested)
                    Stubs.Ignored("Animator root motion of playable graph outputs on humanoid avatars",
                        "is not applied: the root keeps the graph's root curves and deltaPosition, velocity and OnAnimatorMove see no motion");
                return RootMotionMode.Unsupported;
            }
            return requested ? RootMotionMode.Compute : RootMotionMode.Zero;
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
        internal static void ApplyRootMotion(Animator animator, AnimStream result, RootMotionStep motion, float deltaTime)
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
                    "is not computed (only animation clips, mixers, layer mixers, controller playables and animation jobs carry it; offset, motion-to-delta, remove-scale and pose playables, mixers and layer mixers without inputs, and controller playables playing humanoid clips do not): the root keeps the graph's root curves and the Animator reports no motion");
                if (animator.m_Player != null) animator.m_Player.ProcessRootMotion();
                return;
            }
            var t = animator.transform;
            var b = result.Bindings;
            int pos = b.ChannelOf(t, "position"), rot = b.ChannelOf(t, "rotation");
            if (pos >= 0) for (int c = pos; c < pos + 3 && c < result.FloatCount; c++) result.Animated[c] = false;
            if (rot >= 0) for (int c = rot; c < rot + 4 && c < result.FloatCount; c++) result.Animated[c] = false;
            RootMotionStep.RootFrame(animator, out _, out _, out var scale);
            animator.m_DeltaPosition = animator.m_MotionRootRotation * Vector3.Scale(scale, motion.Delta);
            animator.m_DeltaRotation = motion.Turn;
            RootMotionStep.Velocities(animator.m_DeltaPosition, animator.m_DeltaRotation, animator.m_MotionRootRotation, deltaTime,
                out animator.m_RootVelocity, out animator.m_RootAngularVelocity);
            if (animator.m_Player != null)
            {
                animator.m_Player.ProcessRootMotion();
                return;
            }
            animator.m_RootMotionReady = true;
            try
            {
                bool hasMove = false;
                foreach (var mb in animator.gameObject.GetComponents<MonoBehaviour>())
                {
                    var method = mb != null ? MoveMethod(mb) : null;
                    if (method == null) continue;
                    hasMove = true;
                    if (!mb.isActiveAndEnabled) continue;
                    try { method.Invoke(mb, null); }
                    catch (System.Reflection.TargetInvocationException ex) { Debug.LogException(ex.InnerException ?? ex, mb); }
                }
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

    /// <summary>The animation outputs that target each Animator, and their evaluation: at once for
    /// PlayableGraph.Evaluate, with the Animators in the animation phase for playing graphs.</summary>
    internal sealed class AnimationOutputs : OutputSystems.IProcessor
    {
        static readonly Dictionary<Animator, List<PlayableOutputData>> s_ByAnimator = new Dictionary<Animator, List<PlayableOutputData>>();
        static readonly Dictionary<Animator, List<(PlayableOutputData, double, bool)>> s_Pending = new Dictionary<Animator, List<(PlayableOutputData, double, bool)>>();
        sealed class CulledPose
        {
            internal AnimStream Pose;
            internal RootMotionStep Motion;
            internal float DeltaTime;
            internal RootMotionMode Mode;
            internal bool IK;
        }
        static readonly Dictionary<Animator, CulledPose> s_Culled = new Dictionary<Animator, CulledPose>();
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

        internal static void ForgetCulled(Animator animator)
        {
            if (!s_Culled.Remove(animator, out var frame)) return;
            AnimStream.Return(frame.Pose);
        }

        internal static void RememberCulled(Animator animator, AnimStream pose, RootMotionStep motion, float dt, RootMotionMode mode, bool ik) =>
            s_Culled[animator] = new CulledPose { Pose = pose, Motion = motion, DeltaTime = dt, Mode = mode, IK = ik };

        internal static bool EvaluateOnVisible(Animator animator, bool move)
        {
            if (!s_Culled.Remove(animator, out var frame)) return false;
            try
            {
                if (move) animator.m_RootMotionApplied = false;
                if (move && frame.Mode == RootMotionMode.Compute)
                    AnimationEvaluation.ApplyRootMotion(animator, frame.Pose, frame.Motion, frame.DeltaTime);
                else if (move) animator.m_Player?.ProcessRootMotion();
                AnimationEvaluation.WriteOutput(animator, frame.Pose, false, frame.IK);
            }
            finally { AnimStream.Return(frame.Pose); }
            return true;
        }

        public void Process(PlayableOutputData output, double dt, bool seek)
        {
            var target = (output.Engine as AnimationOutputState)?.Target;
            if (target == null || !target.isActiveAndEnabled) return;
            if ((output.Engine as AnimationOutputState)?.SkipEvaluation == true) return;
            if (!s_Pending.TryGetValue(target, out var list)) s_Pending[target] = list = new List<(PlayableOutputData, double, bool)>();
            list.Add((output, dt, seek));
            output.Graph.DeliverNotifications(output);
        }

        public void Stopped(PlayableGraphData graph) { }

        public void Flush() => RunAllPending();

        /// <summary>The pending outputs of one Animator (after its controller wrote).</summary>
        internal static void RunPending(Animator animator)
        {
            if (s_Pending.Count == 0 || !s_Pending.TryGetValue(animator, out var list)) return;
            s_Pending.Remove(animator);
            try { AnimationEvaluation.Run(animator, list); }
            catch (Exception e) { Debug.LogException(e, animator); }
            finally { if (animator.m_Player != null) animator.m_Player.DeferOutput = false; }
        }

        /// <summary>Every pending output (PlayableGraph.Evaluate, and Animators the update skipped).</summary>
        internal static void RunAllPending()
        {
            if (s_Pending.Count == 0) return;
            var all = new List<KeyValuePair<Animator, List<(PlayableOutputData, double, bool)>>>(s_Pending);
            s_Pending.Clear();
            foreach (var pair in all)
            {
                if (pair.Key == null) continue;
                try { AnimationEvaluation.Run(pair.Key, pair.Value); }
                catch (Exception e) { Debug.LogException(e, pair.Key); }
            }
        }
    }
}
