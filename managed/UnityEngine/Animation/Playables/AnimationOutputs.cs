using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Experimental.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

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
            Array.Copy(o.Values, Values, FloatCount);
            Array.Copy(o.Animated, Animated, FloatCount);
            Array.Copy(o.Objects, Objects, ObjectCount);
            Array.Copy(o.ObjectAnimated, ObjectAnimated, ObjectCount);
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
        /// controller (which wrote first) outputs blend over the current values, otherwise over the
        /// default values.</summary>
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
            if (withController)
            {
                // Base on what the controller wrote this frame.
                foreach (var g in bindings.Groups)
                {
                    if (!g.Alive) continue;
                    if (g.IsObject) result.Objects[g.First] = g.ReadObject();
                    else g.Read(result.Values, g.First);
                }
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
                KeepBound(o, s);
                BlendOver(result, s, o.Weight);
                AnimStream.Return(s);
            }
            PoseWriter.WriteStream(bindings, result.Values, result.Animated, result.Objects, result.ObjectAnimated);
            AnimStream.Return(result);
            e.AfterWrite();
        }

        /// <summary>The result of the outputs before (a PreviousInputs output's starting stream).</summary>
        internal AnimStream PreviousInputs;

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
            if (old != null && s_ByAnimator.TryGetValue(old, out var list)) list.Remove(output);
            if (target != null)
            {
                if (!s_ByAnimator.TryGetValue(target, out list)) s_ByAnimator[target] = list = new List<PlayableOutputData>();
                if (!list.Contains(output)) list.Add(output);
            }
        }

        internal static List<PlayableOutputData> Of(Animator animator) => s_ByAnimator.TryGetValue(animator, out var list) ? list : null;

        public void Process(PlayableOutputData output, double dt, bool seek)
        {
            var target = (output.Engine as AnimationOutputState)?.Target;
            if (target == null || !target.isActiveAndEnabled) return;
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
