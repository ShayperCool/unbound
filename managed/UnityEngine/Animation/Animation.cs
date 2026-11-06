using System;
using System.Collections;
using System.Collections.Generic;
using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>
    /// The legacy animation component: plays legacy AnimationClips by name, with weights, layers,
    /// cross-fades and queues. Updates in PreLateUpdate.LegacyAnimationUpdate (or with physics).
    /// </summary>
    [ClassId(111)]
    public sealed partial class Animation : Behaviour, IEnumerable
    {
        internal AnimationClip m_Animation;
        internal List<AnimationClip> m_Animations = new List<AnimationClip>();
        internal WrapMode m_WrapMode;
        internal bool m_PlayAutomatically = true;
        internal bool m_AnimatePhysics;
        internal AnimationCullingType m_CullingType;
        internal Bounds m_UserBounds;

        readonly List<AnimationState> m_States = new List<AnimationState>();
        bool m_StatesBuilt;
        AnimationBindings m_Bindings;
        bool m_Registered;
        AnimationPose[] m_LayerPoses = Array.Empty<AnimationPose>();
        AnimationPose m_AdditivePose;

        public Animation() { }

        public AnimationClip clip
        {
            get => m_Animation;
            set => m_Animation = value;
        }

        public bool playAutomatically
        {
            get => m_PlayAutomatically;
            set => m_PlayAutomatically = value;
        }

        /// <summary>The wrap mode of every state (and of states added later).</summary>
        public WrapMode wrapMode
        {
            get => m_WrapMode;
            set
            {
                m_WrapMode = value;
                foreach (var s in States) s.m_WrapMode = value;
            }
        }

        public bool isPlaying
        {
            get
            {
                foreach (var s in States)
                    if (s.m_Enabled && !s.m_WaitingInQueue) return true;
                return false;
            }
        }

        public AnimationState this[string name] => GetState(name);

        public bool animatePhysics
        {
            get => m_AnimatePhysics;
            set => m_AnimatePhysics = value;
        }

        public AnimationUpdateMode updateMode
        {
            get => m_AnimatePhysics ? AnimationUpdateMode.Fixed : AnimationUpdateMode.Normal;
            set => m_AnimatePhysics = value == AnimationUpdateMode.Fixed;
        }

        [Obsolete("Use cullingType instead")]
        public bool animateOnlyIfVisible
        {
            get => m_CullingType == AnimationCullingType.BasedOnRenderers;
            set => m_CullingType = value ? AnimationCullingType.BasedOnRenderers : AnimationCullingType.AlwaysAnimate;
        }

        /// <summary>Applied by the player loop's update (<see cref="Step"/>): BasedOnRenderers
        /// skips it while no renderer of the hierarchy is visible (see <see cref="Culled"/>).</summary>
        public AnimationCullingType cullingType
        {
            get => m_CullingType;
            set => m_CullingType = value;
        }

        public Bounds localBounds
        {
            get => m_UserBounds;
            set => m_UserBounds = value;
        }

        // ------------------------------------------------------------------ states

        List<AnimationState> States
        {
            get
            {
                if (!m_StatesBuilt) BuildStates();
                return m_States;
            }
        }

        void BuildStates()
        {
            m_StatesBuilt = true;
            foreach (var c in m_Animations)
                if (c != null && FindByName(c.name) == null) m_States.Add(CreateState(c, c.name));
            if (m_Animation != null && FindByName(m_Animation.name) == null)
                m_States.Add(CreateState(m_Animation, m_Animation.name));
        }

        AnimationState CreateState(AnimationClip clip, string name)
        {
            var wrap = clip.m_WrapMode != WrapMode.Default ? clip.m_WrapMode : m_WrapMode;
            return new AnimationState
            {
                m_Owner = this,
                m_Clip = clip,
                m_Name = name,
                m_WrapMode = wrap,
            };
        }

        AnimationState FindByName(string name)
        {
            foreach (var s in m_States)
                if (s.m_Name == name) return s;
            return null;
        }

        internal AnimationState GetState(string name)
        {
            foreach (var s in States)
                if (s.m_Name == name) return s;
            return null;
        }

        internal void RemoveState(AnimationState state)
        {
            state.m_Removed = true;
            m_States.Remove(state);
        }

        public IEnumerator GetEnumerator() => new List<AnimationState>(States).GetEnumerator();

        public int GetClipCount()
        {
            int n = 0;
            foreach (var s in States) if (!s.m_IsClone) n++;
            return n;
        }

        public AnimationClip GetClip(string name) => GetState(name)?.m_Clip;

        public void AddClip(AnimationClip clip, string newName) => AddClip(clip, newName, int.MinValue, int.MaxValue, false);

        public void AddClip(AnimationClip clip, string newName, int firstFrame, int lastFrame) =>
            AddClip(clip, newName, firstFrame, lastFrame, false);

        /// <summary>Adds the frames <paramref name="firstFrame"/>..<paramref name="lastFrame"/> of
        /// <paramref name="clip"/> as a new clip named <paramref name="newName"/>. With
        /// <paramref name="addLoopFrame"/> an extra frame matching the first frame is inserted at
        /// the end (Unity 6000.0 Animation.AddClip); see <see cref="AddLoopFrame"/>.</summary>
        public void AddClip(AnimationClip clip, string newName, int firstFrame, int lastFrame, [Internal.DefaultValue("false")] bool addLoopFrame)
        {
            if (clip == null) throw new NullReferenceException("clip");
            var used = clip;
            if (firstFrame != int.MinValue || lastFrame != int.MaxValue)
            {
                float rate = clip.frameRate > 0f ? clip.frameRate : 60f;
                used = clip.SubClip(firstFrame / rate, lastFrame / rate);
                used.m_Name = newName;
                if (addLoopFrame) AddLoopFrame(used, 1f / rate, newName, firstFrame, lastFrame, this);
            }
            else if (addLoopFrame)
            {
                Debug.LogWarning($"Unbound: Animation.AddClip(\"{newName}\", addLoopFrame): no extra loop frame is inserted when firstFrame is int.MinValue and lastFrame is int.MaxValue (the whole source clip is added as it is).", this);
            }
            var existing = GetState(newName);
            if (existing != null) RemoveState(existing);
            m_States.Add(CreateState(used, newName));
            if (!m_Animations.Contains(used)) m_Animations.Add(used);
        }

        /// <summary>
        /// Appends the loop frame to a sub-clip made by AddClip: one frame (<paramref name="frame"/>
        /// seconds) after the window's end, every curve takes the value it has at the window's
        /// start, and the clip grows by that frame. The sub-clip gets its own curves; the source
        /// clip and its curves stay as they are.
        /// <para>Documented (6000.0 Animation.AddClip, addLoopFrame): an extra frame is inserted at
        /// the end that matches the first frame. Unbound's reading, not documented: the extra frame
        /// sits one frame after lastFrame. The window's own values, tangents and steps are kept
        /// exactly (up to float rounding): a keyed segment cut by the window's end is split at
        /// it.</para>
        /// <para>Not documented and not measured, Unbound's choice: the values from the last frame
        /// to the extra frame. A float curve goes in a straight line, or steps when the window's
        /// last segment is stepped; an object reference curve keeps its last value until the extra
        /// frame. That line is taken on the raw curve values: Euler angle curves go back through
        /// the degrees in between (348 to 0 sweeps back over 347..1, not forward over 360), and
        /// curves of int, bool or enum properties pass through fractional values. A quaternion
        /// rotation (its x, y, z, w curves together) instead takes the shorter way: when the last
        /// frame's and the first frame's quaternions are in opposite hemispheres, the extra frame
        /// holds the first frame's quaternion negated (the same rotation), as
        /// AnimationClip.EnsureQuaternionContinuity aligns keys for the shortest path. Its four
        /// curves step or go in a line together, stepping when any of them ends the window on a
        /// stepped segment.</para>
        /// <para>A curve whose window values cannot be kept that way (a weighted segment cut by the
        /// window's end, a looping pre/post wrap mode the window reaches, a player clip's compiled
        /// curve), or a window too far from time 0 to place the extra frame within half a frame,
        /// leaves the whole sub-clip without the loop frame, with a warning on every such call
        /// naming every reason.</para>
        /// </summary>
        static void AddLoopFrame(AnimationClip sub, float frame, string newName, int firstFrame, int lastFrame, Object context)
        {
            float start = sub.m_StartTime, end = sub.m_StopTime, loop = end + frame;
            // A state at its end samples start + length: that time is the extra frame itself, so a
            // step to it is not missed by a rounding of the length.
            for (int i = 0; i < 4 && start + (loop - start) != loop; i++) loop = start + (loop - start);
            var unsupported = new List<string>();
            var windows = new LoopWindow[sub.m_Curves.Count];
            double wanted = (double)end + frame;
            // Far from time 0 (a firstFrame like int.MinValue), times are too coarse for the
            // extra frame: the snap above can move it by seconds, or to the window's end.
            if (!(loop > end) || !(Math.Abs(loop - wanted) <= frame * 0.5))
                unsupported.Add($"no time within half a frame of {wanted:R}s is representable on a window starting at {start:R}s (nearest {loop:R}s)");
            else
            {
                // Every curve is checked, so that the warning names all those not supported.
                for (int i = 0; i < windows.Length; i++)
                {
                    var c = sub.m_Curves[i];
                    var reason = LoopWindowOf(c, start, end, out windows[i]);
                    if (reason != null) unsupported.Add($"{c.Binding} ({reason})");
                }
            }
            if (unsupported.Count > 0)
            {
                Debug.LogWarning($"Unbound: Animation.AddClip(\"{newName}\", {firstFrame}, {lastFrame}, addLoopFrame): the extra loop frame is not inserted, the clip has only frames {firstFrame}..{lastFrame}: {string.Join("; ", unsupported)}.", context);
                return;
            }

            foreach (var rotation in QuaternionGroups(sub.m_Curves))
                AlignQuaternion(windows, rotation);
            for (int i = 0; i < windows.Length; i++)
                if (windows[i] != null) sub.m_Curves[i] = windows[i].Close(start, end, loop);
            for (int i = 0; i < sub.m_ObjectCurves.Count; i++)
                sub.m_ObjectCurves[i] = LoopFrameObjectCurve(sub.m_ObjectCurves[i], start, end, loop);
            sub.m_StopTime = loop;
            sub.m_Version++;
        }

        /// <summary>A float curve's window up to the window's end, before the loop frame closes it
        /// (see <see cref="AddLoopFrame"/>).</summary>
        sealed class LoopWindow
        {
            internal ClipCurve Source;
            /// <summary>The keys before the window's end; null for a curve with one value at every
            /// time (kept as it is unless its quaternion is negated).</summary>
            internal List<Keyframe> Keys;
            /// <summary>The key at the window's end (its out tangent is set when closed).</summary>
            internal Keyframe Split;
            internal float Start, Last;
            /// <summary>The value at the extra frame: the value at the window's start, or its
            /// negation for a quaternion closing the shorter way.</summary>
            internal float Target;
            /// <summary>Whether the curve steps to the extra frame.</summary>
            internal bool Stepped;
            internal bool HasKeys => Keys != null;

            internal ClipCurve Close(float start, float end, float loop)
            {
                var c = Source;
                List<Keyframe> list;
                Keyframe split;
                WrapMode pre, post;
                if (Keys != null)
                {
                    list = new List<Keyframe>(Keys.Count + 2);
                    list.AddRange(Keys);
                    split = Split;
                    pre = c.Curve.preWrapMode;
                    post = c.Curve.postWrapMode;
                }
                else if (Target == Start)
                    return c; // one value at every time, the extra frame's too
                else
                {
                    // A constant component of a quaternion negated at the extra frame.
                    list = new List<Keyframe>(3);
                    if (start < end) list.Add(new Keyframe(start, Start, 0f, 0f));
                    split = new Keyframe(end, Last, 0f, 0f);
                    pre = post = WrapMode.ClampForever;
                }
                float slope = Stepped ? float.PositiveInfinity : (Target - split.value) / (loop - end);
                split.outTangent = slope;
                list.Add(split);
                list.Add(new Keyframe(loop, Target, slope, 0f));
                return new ClipCurve
                {
                    Binding = c.Binding,
                    Curve = new AnimationCurve(list.ToArray()) { preWrapMode = pre, postWrapMode = post },
                    QuaternionStart = c.QuaternionStart,
                    EulerStart = c.EulerStart,
                };
            }
        }

        /// <summary>A float curve's window (see <see cref="LoopWindow"/>), or the reason it cannot
        /// have the loop frame.</summary>
        static string LoopWindowOf(ClipCurve c, float start, float end, out LoopWindow window)
        {
            window = null;
            if (c.Sampled != null)
            {
                if (c.Sampled.Times != null || c.Sampled.Samples != null)
                    return "a player clip's compiled curve: its segments are not split yet";
                float constant = c.Sampled.Constant;
                window = new LoopWindow { Source = c, Start = constant, Last = constant, Target = constant };
                return null;
            }
            var curve = c.Curve;
            if (curve == null) return null; // nothing to sample, kept as it is
            if (curve.length < 2)
            {
                float constant = curve.Evaluate(start); // one value at every time
                window = new LoopWindow { Source = c, Start = constant, Last = constant, Target = constant };
                return null;
            }

            var keys = curve.keys;
            int n = keys.Length;
            float first = keys[0].time, last = keys[n - 1].time;
            if (start < first && last > first && LoopingWrap(curve.preWrapMode))
                return $"its pre-wrap mode {curve.preWrapMode} repeats it before the first key, in the window";
            if (end > last && last > first && LoopingWrap(curve.postWrapMode))
                return $"its post-wrap mode {curve.postWrapMode} repeats it after the last key, in the window";

            float v0 = c.Evaluate(start);
            int below = 0;
            while (below < n && keys[below].time < end) below++;
            var list = new List<Keyframe>(below + 1);
            for (int i = 0; i < below; i++) list.Add(keys[i]);

            Keyframe split;
            bool stepped = false; // whether the window's last segment is stepped
            if (below < n && keys[below].time == end)
            {
                // A key at the window's end (the last of several at that time) ends the window as it is.
                int at = below;
                while (at + 1 < n && keys[at + 1].time == end) at++;
                for (int i = below; i < at; i++) list.Add(keys[i]);
                split = keys[at];
                split.weightedMode &= ~WeightedMode.Out;
                stepped = below > 0 && (float.IsInfinity(keys[below - 1].outTangent) || float.IsInfinity(keys[below].inTangent));
            }
            else if (below == 0)
            {
                // The window ends before the first key, where the curve holds that key's value.
                split = new Keyframe(end, keys[0].value, 0f, 0f);
            }
            else if (below == n)
            {
                // The window ends after the last key, which holds its value up to the split.
                var k = list[n - 1];
                k.outTangent = 0f;
                k.weightedMode &= ~WeightedMode.Out;
                list[n - 1] = k;
                split = new Keyframe(end, k.value, 0f, 0f);
            }
            else
            {
                // The window's end cuts the segment from k0 to k1: split it there, keeping its shape.
                var k0 = keys[below - 1];
                var k1 = keys[below];
                stepped = float.IsInfinity(k0.outTangent) || float.IsInfinity(k1.inTangent);
                if (stepped)
                    split = new Keyframe(end, k0.value, float.PositiveInfinity, 0f);
                else if ((k0.weightedMode & WeightedMode.Out) != 0 || (k1.weightedMode & WeightedMode.In) != 0)
                    return "the window's end cuts a weighted segment, which is not split yet";
                else
                    split = new Keyframe(end, AnimationCurve.EvaluateSegment(k0, k1, end), HermiteSlope(k0, k1, end), 0f);
            }
            window = new LoopWindow { Source = c, Keys = list, Split = split, Start = v0, Last = split.value, Target = v0, Stepped = stepped };
            return null;
        }

        static bool LoopingWrap(WrapMode mode) => mode == WrapMode.Loop || mode == WrapMode.PingPong;

        /// <summary>The quaternion rotations among a clip's curves, as four curve indices (x, y, z,
        /// w): the groups the clip marks (four curves in a row from one marked
        /// <see cref="ClipCurve.QuaternionStart"/>, as they are sampled), and the m_LocalRotation.x,
        /// .y, .z, .w curves of one Transform (bound to its rotation together). A rotation with a
        /// component missing or given twice is not a group: its curves close one by one.</summary>
        static List<int[]> QuaternionGroups(List<ClipCurve> curves)
        {
            var groups = new List<int[]>();
            var marked = new bool[curves.Count];
            for (int i = 0; i + 3 < curves.Count; i++)
            {
                if (!curves[i].QuaternionStart) continue;
                groups.Add(new[] { i, i + 1, i + 2, i + 3 });
                for (int k = 0; k < 4; k++) marked[i + k] = true;
                i += 3;
            }
            var byTarget = new Dictionary<(string, bool, uint), int[]>();
            for (int i = 0; i < curves.Count; i++)
            {
                if (marked[i]) continue;
                var b = curves[i].Binding;
                int axis = RotationAxis(b);
                if (axis < 0) continue;
                // RotationAxis already restricts these to the object's one Transform.
                // Lazy type resolution and ClassId/Type forms do not create different targets.
                var key = (b.Path ?? "", b.Hashed, b.PathHash);
                if (!byTarget.TryGetValue(key, out var g)) byTarget[key] = g = new[] { -1, -1, -1, -1, 0 };
                if (g[axis] >= 0) g[4] = 1; // given twice
                else g[axis] = i;
            }
            foreach (var g in byTarget.Values)
                if (g[4] == 0 && g[0] >= 0 && g[1] >= 0 && g[2] >= 0 && g[3] >= 0) groups.Add(new[] { g[0], g[1], g[2], g[3] });
            return groups;
        }

        /// <summary>The component (0..3 for x, y, z, w) of a Transform's m_LocalRotation a binding
        /// animates, or -1.</summary>
        static int RotationAxis(CurveBinding b)
        {
            const string head = "m_LocalRotation.";
            var a = b.Attribute;
            if (a == null || a.Length != head.Length + 1 || !a.StartsWith(head, StringComparison.Ordinal)) return -1;
            bool transform = b.Type != null ? typeof(Transform).IsAssignableFrom(b.Type)
                : b.ClassId == EngineTypes.TransformId || b.ClassId == EngineTypes.RectTransformId;
            return transform ? "xyzw".IndexOf(a[head.Length]) : -1;
        }

        /// <summary>Closes a quaternion rotation the shorter way: when its last frame and its first
        /// frame are in opposite hemispheres, the extra frame takes the negated first frame (the
        /// same rotation). Its curves step together or go in a line together.</summary>
        static void AlignQuaternion(LoopWindow[] windows, int[] group)
        {
            float dot = 0f;
            bool stepped = false;
            foreach (int i in group)
            {
                var w = windows[i];
                if (w == null) return; // a curve without values: not sampled as a rotation
                dot += w.Last * w.Start;
                stepped |= w.HasKeys && w.Stepped;
            }
            foreach (int i in group)
            {
                var w = windows[i];
                if (dot < 0f) w.Target = -w.Start;
                w.Stepped = stepped;
            }
        }

        /// <summary>The slope (value per second) at <paramref name="time"/> of an unweighted Hermite
        /// segment, as AnimationCurve evaluates it.</summary>
        static float HermiteSlope(Keyframe k0, Keyframe k1, float time)
        {
            float dx = k1.time - k0.time;
            float s = (time - k0.time) / dx;
            float m0 = k0.outTangent * dx, m1 = k1.inTangent * dx;
            float d00 = 6f * s * s - 6f * s, d10 = 3f * s * s - 4f * s + 1f;
            float d01 = -6f * s * s + 6f * s, d11 = 3f * s * s - 2f * s;
            return (d00 * k0.value + d10 * m0 + d01 * k1.value + d11 * m1) / dx;
        }

        /// <summary>An object reference curve with the loop frame: its keys up to the window's end,
        /// then the window's first value at the extra frame.</summary>
        static ClipObjectCurve LoopFrameObjectCurve(ClipObjectCurve c, float start, float end, float loop)
        {
            if (c.Values == null || c.Values.Length == 0) return c;
            var firstValue = c.Evaluate(start);
            int count = Math.Min(c.Times.Length, c.Values.Length);
            int keep = 0;
            while (keep < count && c.Times[keep] <= end) keep++;
            var times = new List<float>(keep + 2);
            var values = new List<Object>(keep + 2);
            if (keep == 0)
            {
                // Before its first key the curve has its first value.
                times.Add(end);
                values.Add(c.Values[0]);
            }
            for (int i = 0; i < keep; i++)
            {
                times.Add(c.Times[i]);
                values.Add(c.Values[i]);
            }
            times.Add(loop);
            values.Add(firstValue);
            return new ClipObjectCurve { Binding = c.Binding, Times = times.ToArray(), Values = values.ToArray() };
        }

        public void RemoveClip(AnimationClip clip)
        {
            if (clip == null) throw new NullReferenceException("clip");
            foreach (var s in States.ToArray())
                if (s.m_Clip == clip) RemoveState(s);
            m_Animations.Remove(clip);
        }

        public void RemoveClip(string clipName)
        {
            var s = GetState(clipName);
            if (s == null) return;
            RemoveState(s);
            if (FindClipUse(s.m_Clip) == null) m_Animations.Remove(s.m_Clip);
        }

        AnimationState FindClipUse(AnimationClip clip)
        {
            foreach (var s in m_States) if (s.m_Clip == clip) return s;
            return null;
        }

        // ------------------------------------------------------------------ playback

        public bool Play() => Play(PlayMode.StopSameLayer);

        public bool Play([Internal.DefaultValue("PlayMode.StopSameLayer")] PlayMode mode)
        {
            if (m_Animation == null) return false;
            return Play(m_Animation.name, mode);
        }

        public bool Play(string animation) => Play(animation, PlayMode.StopSameLayer);

        public bool Play(string animation, [Internal.DefaultValue("PlayMode.StopSameLayer")] PlayMode mode)
        {
            var state = GetState(animation);
            if (state == null)
            {
                Debug.LogWarning($"The AnimationState could not be played because it couldn't be found!\nPlease attach an animation clip with the name '{animation}' to this animation.", this);
                return false;
            }
            if (!CheckLegacy(state)) return false;
            StopOthers(state, mode);
            state.m_Enabled = true;
            state.m_WaitingInQueue = false;
            state.m_Weight = 1f;
            state.m_Fading = false;
            if (!state.m_Playing) state.Begin();
            return true;
        }

        [Obsolete("use PlayMode instead of AnimationPlayMode.")]
        public bool Play(AnimationPlayMode mode) => m_Animation != null && Play(m_Animation.name, mode);

        [Obsolete("use PlayMode instead of AnimationPlayMode.")]
        public bool Play(string animation, AnimationPlayMode mode)
        {
            switch (mode)
            {
                case AnimationPlayMode.Queue: PlayQueued(animation); return true;
                case AnimationPlayMode.Mix: Blend(animation); return true;
                default: return Play(animation);
            }
        }

        bool CheckLegacy(AnimationState state)
        {
            if (state.m_Clip != null && state.m_Clip.m_Legacy) return true;
            Debug.LogWarning($"The AnimationClip '{state.m_Clip?.name}' used by the Animation component '{name}' must be marked as Legacy.", this);
            return false;
        }

        void StopOthers(AnimationState keep, PlayMode mode)
        {
            foreach (var s in States.ToArray())
            {
                if (s == keep || s.m_WaitingInQueue) continue;
                if (mode == PlayMode.StopAll || s.m_Layer == keep.m_Layer) s.StopInternal();
            }
        }

        public void CrossFade(string animation) => CrossFade(animation, 0.3f, PlayMode.StopSameLayer);

        public void CrossFade(string animation, float fadeLength) => CrossFade(animation, fadeLength, PlayMode.StopSameLayer);

        public void CrossFade(string animation, [Internal.DefaultValue("0.3F")] float fadeLength, [Internal.DefaultValue("PlayMode.StopSameLayer")] PlayMode mode)
        {
            var state = GetState(animation);
            if (state == null)
            {
                Debug.LogWarning($"The AnimationState could not be played because it couldn't be found!\nPlease attach an animation clip with the name '{animation}' to this animation.", this);
                return;
            }
            CrossFadeState(state, fadeLength, mode);
        }

        void CrossFadeState(AnimationState state, float fadeLength, PlayMode mode)
        {
            if (!CheckLegacy(state)) return;
            if (fadeLength <= 0f)
            {
                Play(state.m_Name, mode);
                return;
            }
            foreach (var s in States.ToArray())
            {
                if (s == state || s.m_WaitingInQueue || !s.m_Enabled) continue;
                if (mode == PlayMode.StopAll || s.m_Layer == state.m_Layer) s.FadeTo(0f, fadeLength, stopWhenFaded: true);
            }
            if (!state.m_Enabled || !state.m_Playing)
            {
                state.m_Weight = state.m_Enabled ? state.m_Weight : 0f;
                state.m_Enabled = true;
                state.Begin();
            }
            state.m_WaitingInQueue = false;
            state.FadeTo(1f, fadeLength, stopWhenFaded: false);
        }

        public void Blend(string animation) => Blend(animation, 1f, 0.3f);

        public void Blend(string animation, float targetWeight) => Blend(animation, targetWeight, 0.3f);

        public void Blend(string animation, [Internal.DefaultValue("1.0F")] float targetWeight, [Internal.DefaultValue("0.3F")] float fadeLength)
        {
            var state = GetState(animation);
            if (state == null)
            {
                Debug.LogWarning($"The AnimationState could not be played because it couldn't be found!\nPlease attach an animation clip with the name '{animation}' to this animation.", this);
                return;
            }
            if (!CheckLegacy(state)) return;
            if (!state.m_Enabled)
            {
                state.m_Weight = 0f;
                state.m_Enabled = true;
                state.Begin();
            }
            if (fadeLength <= 0f)
            {
                state.m_Weight = targetWeight;
                state.m_Fading = false;
            }
            else
            {
                state.FadeTo(targetWeight, fadeLength, stopWhenFaded: false);
            }
        }

        public AnimationState CrossFadeQueued(string animation) => CrossFadeQueued(animation, 0.3f, QueueMode.CompleteOthers, PlayMode.StopSameLayer);

        public AnimationState CrossFadeQueued(string animation, float fadeLength) => CrossFadeQueued(animation, fadeLength, QueueMode.CompleteOthers, PlayMode.StopSameLayer);

        public AnimationState CrossFadeQueued(string animation, float fadeLength, QueueMode queue) => CrossFadeQueued(animation, fadeLength, queue, PlayMode.StopSameLayer);

        public AnimationState CrossFadeQueued(string animation, [Internal.DefaultValue("0.3F")] float fadeLength, [Internal.DefaultValue("QueueMode.CompleteOthers")] QueueMode queue, [Internal.DefaultValue("PlayMode.StopSameLayer")] PlayMode mode) =>
            Queue(animation, fadeLength, queue, mode);

        public AnimationState PlayQueued(string animation) => PlayQueued(animation, QueueMode.CompleteOthers, PlayMode.StopSameLayer);

        public AnimationState PlayQueued(string animation, QueueMode queue) => PlayQueued(animation, queue, PlayMode.StopSameLayer);

        public AnimationState PlayQueued(string animation, [Internal.DefaultValue("QueueMode.CompleteOthers")] QueueMode queue, [Internal.DefaultValue("PlayMode.StopSameLayer")] PlayMode mode) =>
            Queue(animation, 0f, queue, mode);

        /// <summary>A copy of the state that starts when the others finish (or now).</summary>
        AnimationState Queue(string animation, float fadeLength, QueueMode queue, PlayMode mode)
        {
            var original = GetState(animation);
            if (original == null)
            {
                Debug.LogWarning($"The AnimationState could not be played because it couldn't be found!\nPlease attach an animation clip with the name '{animation}' to this animation.", this);
                return null;
            }
            if (!CheckLegacy(original)) return null;
            var clone = new AnimationState
            {
                m_Owner = this,
                m_Clip = original.m_Clip,
                m_Name = original.m_Name + " - Queued Clone",
                m_QueuedFrom = original.m_Name,
                m_WrapMode = original.m_WrapMode,
                m_Speed = original.m_Speed,
                m_Layer = original.m_Layer,
                m_BlendMode = original.m_BlendMode,
                m_IsClone = true,
                m_WaitingInQueue = true,
                m_QueueFade = fadeLength,
                m_QueuePlayMode = mode,
            };
            m_States.Add(clone);
            if (queue == QueueMode.PlayNow) StartQueued(clone);
            return clone;
        }

        void StartQueued(AnimationState clone)
        {
            clone.m_WaitingInQueue = false;
            if (clone.m_QueueFade > 0f) CrossFadeState(clone, clone.m_QueueFade, clone.m_QueuePlayMode);
            else
            {
                StopOthers(clone, clone.m_QueuePlayMode);
                clone.m_Enabled = true;
                clone.m_Weight = 1f;
                clone.Begin();
            }
        }

        void UpdateQueue()
        {
            AnimationState waiting = null;
            foreach (var s in m_States)
                if (s.m_WaitingInQueue) { waiting = s; break; }
            if (waiting == null) return;
            foreach (var s in m_States)
            {
                if (s == waiting || s.m_WaitingInQueue || !s.m_Enabled) continue;
                if (waiting.m_QueuePlayMode != PlayMode.StopAll && s.m_Layer != waiting.m_Layer) continue;
                if (s.m_Fading && s.m_TargetWeight <= 0f) continue; // already on its way out
                // The others must complete; a cross-fade starts its fade length before their end.
                if (s.EffectiveWrap == WrapMode.Loop || s.EffectiveWrap == WrapMode.PingPong || s.EffectiveWrap == WrapMode.ClampForever) return;
                float remaining = (s.length - (float)s.m_Time) / Math.Max(Math.Abs(s.m_Speed), 1e-6f);
                if (remaining > waiting.m_QueueFade) return;
            }
            StartQueued(waiting);
        }

        public void Stop()
        {
            foreach (var s in States.ToArray())
            {
                if (s.m_IsClone) RemoveState(s);
                else s.StopInternal();
            }
        }

        public void Stop(string name)
        {
            foreach (var s in States.ToArray())
            {
                if (s.m_Name == name) s.StopInternal();
                else if (s.m_IsClone && s.m_QueuedFrom == name) RemoveState(s);
            }
        }

        public void Rewind()
        {
            foreach (var s in States) s.m_Time = 0.0;
        }

        public void Rewind(string name)
        {
            var s = GetState(name);
            if (s != null) s.m_Time = 0.0;
        }

        /// <summary>Samples the playing states at their current time, without advancing it.</summary>
        public void Sample() => Evaluate(0f, advance: false);

        public bool IsPlaying(string name)
        {
            foreach (var s in States)
            {
                if (!s.m_Enabled || s.m_WaitingInQueue) continue;
                if (s.m_Name == name || (s.m_IsClone && s.m_QueuedFrom == name)) return true;
            }
            return false;
        }

        /// <summary>
        /// Synchronizes the playback speed of all animations in the layer: Unbound's reading of the
        /// method's purpose. The 6000.0 Scripting API has no page for Animation.SyncLayer and
        /// nothing measures it, so all of the behaviour below is unmeasured.
        /// <para>Unbound's choices (unmeasured hypotheses): the layer stays synchronized from the
        /// call on, for states that join it later too (there is no call that undoes it), and a
        /// layer without states is no different until states play in it. Each update, the enabled
        /// states of the layer (not waiting in a queue) advance by one shared normalized speed: the
        /// weighted mean of their normalizedSpeed (speed / length), weighted by their weights
        /// before this update's fades. A state of weight 0 contributes nothing to the mean but
        /// advances by it; a state of length 0 contributes nothing and keeps its own speed. Their
        /// normalized times keep their offsets (they are not aligned), and their speed and
        /// normalizedSpeed properties keep their values; a Once state ends by the direction it
        /// advances in. Without any weight in the layer its states advance by their own speeds. A
        /// queued state waiting for a state of the layer to complete still estimates the time left
        /// by that state's own speed. A state started this update (sampled where it starts, it does
        /// not advance yet) still contributes its weight and normalizedSpeed to the mean.</para>
        /// <para>Since none of this is documented or measured, every call is reported (counted,
        /// the first in the process logged once) as having unmeasured semantics; the behaviour is
        /// the hypothesis above, unchanged by the notice.</para>
        /// </summary>
        public void SyncLayer(int layer)
        {
            Unbound.Runtime.Stubs.Ignored("Animation.SyncLayer",
                "has unmeasured semantics (no 6000.0 Scripting API page, no measurement): Unbound advances the layer's states by the weighted mean of their normalized speeds, an unsupported hypothesis that may differ from Unity");
            (m_SyncedLayers ??= new HashSet<int>()).Add(layer);
        }

        HashSet<int> m_SyncedLayers;

        /// <summary>The shared normalized speed of each synchronized layer with weight in it (see
        /// <see cref="SyncLayer"/>); null when no layer is synchronized.</summary>
        Dictionary<int, float> SyncedNormalizedSpeeds(List<AnimationState> states)
        {
            if (m_SyncedLayers == null) return null;
            var sums = new Dictionary<int, (double Speed, double Weight)>();
            foreach (var s in states)
            {
                if (!m_SyncedLayers.Contains(s.m_Layer) || !Advances(s) || !(s.m_Weight > 0f)) continue;
                float length = s.length;
                if (!(length > 0f)) continue;
                sums.TryGetValue(s.m_Layer, out var sum);
                sums[s.m_Layer] = (sum.Speed + (double)s.m_Weight * s.m_Speed / length, sum.Weight + s.m_Weight);
            }
            var speeds = new Dictionary<int, float>();
            foreach (var (layer, sum) in sums) speeds[layer] = (float)(sum.Speed / sum.Weight);
            return speeds;
        }

        static bool Advances(AnimationState s) => s.m_Enabled && !s.m_WaitingInQueue && s.m_Clip != null && s.m_Clip.m_Legacy;

        /// <summary>The speed (seconds of the clip per second) a state advances by this update.</summary>
        static float AdvanceSpeed(AnimationState s, Dictionary<int, float> synced)
        {
            if (synced == null || !synced.TryGetValue(s.m_Layer, out var normalized)) return s.m_Speed;
            float length = s.length;
            return length > 0f ? normalized * length : s.m_Speed;
        }

        // ------------------------------------------------------------------ engine side

        internal override void OnEngineEnable()
        {
            if (!m_Registered)
            {
                m_Registered = true;
                AnimationSystem.Add(this);
            }
            if (m_PlayAutomatically && m_Animation != null && !isPlaying) Play();
        }

        internal override void OnEngineDisable()
        {
            if (m_Registered)
            {
                m_Registered = false;
                AnimationSystem.Remove(this);
            }
            // Deactivating rewinds: the default clip plays from the start when enabled again.
            foreach (var s in m_States.ToArray())
            {
                if (s.m_IsClone) RemoveState(s);
                else s.StopInternal();
            }
        }

        internal override void OnEngineDestroy()
        {
            if (m_Registered)
            {
                m_Registered = false;
                AnimationSystem.Remove(this);
            }
            base.OnEngineDestroy();
        }

        /// <summary>One update: advances the playing states, writes the blended pose, fires events;
        /// nothing when culled (see <see cref="Culled"/>), the culled time is added to the next
        /// update that runs.</summary>
        internal void Step(float deltaTime)
        {
            if (m_CullingType != AnimationCullingType.AlwaysAnimate && Culled())
            {
                // A state's first update, which samples its start without advancing, is spent even
                // while culled (measured: the time of an Animation culled from its start catches up
                // from its second update on). Unbound's choice (unmeasured): a culled update that
                // starts any state adds nothing, and its start sample and events are not replayed.
                bool started = false, playing = false;
                foreach (var state in States)
                {
                    if (!state.m_Enabled) continue;
                    playing = true;
                    if (!state.m_Fresh) continue;
                    state.m_Fresh = false;
                    started = true;
                }
                // Nothing playing: nothing to catch up (Unbound's choice, unmeasured).
                if (!playing) m_CulledTime = 0f;
                else if (!started) m_CulledTime += deltaTime;
                return;
            }
            deltaTime += m_CulledTime;
            m_CulledTime = 0f;
            Evaluate(deltaTime, advance: true);
        }

        float m_CulledTime;

        /// <summary>
        /// Whether the player loop's update skips this Animation. Documented (6000.0
        /// AnimationCullingType): BasedOnRenderers, "Animation is disabled when renderers are not
        /// visible"; "When Animation component is culled it won't do anything: it won't update
        /// animation states, execute events or sample animations". So a culled update advances no
        /// state time, fades, queues or events and writes nothing. Measured (tests/projects/animculling,
        /// Unity 6000.0.71f1): the first update that runs again advances by the culled updates' delta
        /// times as well as its own, so the states' time catches up and the events of the skipped
        /// span fire then. Unbound's choices (unmeasured): the culled time is kept across Play,
        /// Stop and time changes made while culled; explicit calls (Sample, Play, Stop, ...) are not
        /// culled.
        /// <para>Visibility is Animator.AnyRendererVisible of the Animation's GameObject, read when
        /// the update runs (see there for the unmeasured choices). The Manual's "Cull based on the
        /// default animation pose" and Animation.localBounds are not used: visibility is the
        /// renderers' own, as their last culling left it (unmeasured).</para>
        /// <para>The obsolete BasedOnClipBounds and BasedOnUserBounds are reported once and animate
        /// as AlwaysAnimate (as before this culling existed).</para>
        /// </summary>
        bool Culled()
        {
            switch (m_CullingType)
            {
                case AnimationCullingType.BasedOnRenderers:
                    return !Animator.AnyRendererVisible(m_GameObject);
                default:
                    Unbound.Runtime.Stubs.Ignored("Animation.cullingType BasedOnClipBounds/BasedOnUserBounds",
                        "(obsolete) is not honoured: the Animation always animates");
                    return false;
            }
        }

        bool m_Evaluating;

        void Evaluate(float deltaTime, bool advance)
        {
            // Writes run game code; an evaluation from inside this one is skipped.
            if (m_GameObject == null || m_Evaluating) return;
            m_Evaluating = true;
            try
            {
                EvaluateCore(deltaTime, advance);
            }
            finally
            {
                m_Evaluating = false;
            }
        }

        void EvaluateCore(float deltaTime, bool advance)
        {
            var states = States;
            if (advance) UpdateQueue();
            m_Bindings ??= new AnimationBindings(m_GameObject);

            var playing = new List<(AnimationState State, double From, double To, bool Fresh, bool Finished)>();
            var synced = advance ? SyncedNormalizedSpeeds(states) : null;
            foreach (var s in states.ToArray())
            {
                if (!s.m_Enabled || s.m_WaitingInQueue || s.m_Clip == null || !s.m_Clip.m_Legacy) continue;
                double from = s.m_Time;
                bool finished = false;
                if (advance)
                {
                    float speed = AdvanceSpeed(s, synced);
                    // A state started since the last update is sampled where it starts (measured).
                    if (!s.m_Fresh) s.m_Time += deltaTime * speed;
                    s.UpdateFade(deltaTime);
                    if (!s.m_Enabled) continue; // faded out
                    var wrap = s.EffectiveWrap;
                    float length = s.length;
                    if (wrap == WrapMode.Once && (speed >= 0f ? s.m_Time >= length : s.m_Time <= 0.0))
                        finished = true;
                }
                playing.Add((s, from, s.m_Time, s.m_Fresh, finished));
            }

            // Bind first: binding a clip may add channels, and poses are sized by the channel count.
            foreach (var p in playing) m_Bindings.Bind(p.State.m_Clip);

            // Layers, highest first.
            var layers = new SortedDictionary<int, AnimationPose>(Comparer<int>.Create((a, b) => b.CompareTo(a)));
            AnimationPose additive = null;
            foreach (var (s, _, _, _, _) in playing)
            {
                var bound = m_Bindings.Bind(s.m_Clip);
                AnimationPose pose;
                if (s.m_BlendMode == AnimationBlendMode.Additive)
                {
                    pose = additive ??= TakePose(-1);
                }
                else if (!layers.TryGetValue(s.m_Layer, out pose))
                {
                    pose = TakePose(layers.Count);
                    layers[s.m_Layer] = pose;
                }
                var mask = s.m_Mixing != null ? s.MixingMask(m_Bindings) : null;
                ClipSampling.Sample(bound, s.SampleTime(), s.m_Weight, pose, mask);
            }
            if (layers.Count > 0 || additive != null)
            {
                var ordered = new AnimationPose[layers.Count];
                layers.Values.CopyTo(ordered, 0);
                PoseWriter.WriteLegacy(m_Bindings, ordered, ordered.Length, additive);
            }

            foreach (var (s, from, to, fresh, finished) in playing)
            {
                if (advance) s.m_Fresh = false;
                if (advance && s.m_Weight > 0f)
                {
                    float length = s.length;
                    if (length > 0f || fresh)
                    {
                        var wrap = s.EffectiveWrap;
                        bool loop = wrap == WrapMode.Loop || wrap == WrapMode.PingPong;
                        double n0 = length > 0f ? from / length : 0.0, n1 = length > 0f ? to / length : 0.0;
                        var source = s;
                        AnimationEvents.FireCrossed(s.m_Clip, n0, n1, loop, fresh, e =>
                        {
                            var fired = e.Copy();
                            fired.m_Source = AnimationEventSource.Legacy;
                            fired.m_StateSender = source;
                            AnimationEvents.Dispatch(m_GameObject, fired, source.m_Clip);
                        });
                    }
                }
                if (finished)
                {
                    if (s.m_IsClone) RemoveState(s);
                    else s.StopInternal();
                }
            }
        }

        AnimationPose TakePose(int index)
        {
            if (index < 0)
            {
                m_AdditivePose ??= new AnimationPose();
                m_AdditivePose.Reset(m_Bindings.FloatCount, m_Bindings.ObjectCount);
                return m_AdditivePose;
            }
            if (m_LayerPoses.Length <= index) Array.Resize(ref m_LayerPoses, index + 1);
            var pose = m_LayerPoses[index] ??= new AnimationPose();
            pose.Reset(m_Bindings.FloatCount, m_Bindings.ObjectCount);
            return pose;
        }

        /// <summary>Clips bound after the first frame may add channels: poses grow with them.</summary>
        internal void EnsureBound()
        {
            if (m_Bindings == null && m_GameObject != null) m_Bindings = new AnimationBindings(m_GameObject);
            foreach (var s in States)
                if (s.m_Clip != null) m_Bindings.Bind(s.m_Clip);
        }

        // ------------------------------------------------------------------ serialized data

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            var main = body.Ref("m_Animation");
            m_Animation = main.IsNull ? null : resolver.Resolve(main, typeof(AnimationClip)) as AnimationClip;
            if (m_Animation == null && !main.IsNull) MissingMotion.Report(main, $"the Animation of '{m_GameObject?.m_Name}'");
            m_Animations.Clear();
            foreach (var item in body.Get("m_Animations")?.Seq() ?? Array.Empty<YNode>())
            {
                var r = item.AsRef();
                if (r.IsNull) continue;
                if (resolver.Resolve(r, typeof(AnimationClip)) is AnimationClip c) m_Animations.Add(c);
                else MissingMotion.Report(r, $"the Animation of '{m_GameObject?.m_Name}'");
            }
            m_WrapMode = (WrapMode)body.Int("m_WrapMode");
            m_PlayAutomatically = body.Bool("m_PlayAutomatically", true);
            m_AnimatePhysics = body.Bool("m_AnimatePhysics") || body.Int("m_UpdateMode") == (int)AnimationUpdateMode.Fixed;
            m_CullingType = (AnimationCullingType)body.Int("m_CullingType");
            var bounds = body.Get("m_UserAABB");
            if (bounds != null) m_UserBounds = new Bounds(bounds.Vector3("m_Center"), bounds.Vector3("m_Extent") * 2f);
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (Animation)source;
            m_Enabled = s.m_Enabled;
            m_Animation = s.m_Animation;
            m_Animations = new List<AnimationClip>(s.m_Animations);
            m_WrapMode = s.m_WrapMode;
            m_PlayAutomatically = s.m_PlayAutomatically;
            m_AnimatePhysics = s.m_AnimatePhysics;
            m_CullingType = s.m_CullingType;
            m_UserBounds = s.m_UserBounds;
        }
    }

    /// <summary>The playback state of one clip in an Animation component.</summary>
    public sealed partial class AnimationState : TrackedReference
    {
        internal Animation m_Owner;
        internal AnimationClip m_Clip;
        internal string m_Name = "";
        internal bool m_Enabled;
        internal float m_Weight;
        internal WrapMode m_WrapMode;
        internal double m_Time;
        internal float m_Speed = 1f;
        internal int m_Layer;
        internal AnimationBlendMode m_BlendMode;
        internal bool m_Removed;
        /// <summary>Started since the last update: events at its start time fire.</summary>
        internal bool m_Fresh;
        /// <summary>Started and not stopped (enabled may pause it).</summary>
        internal bool m_Playing;

        internal bool m_Fading;
        internal float m_TargetWeight;
        internal float m_FadeSpeed;
        internal bool m_StopWhenFaded;

        internal bool m_IsClone;
        internal bool m_WaitingInQueue;
        internal string m_QueuedFrom;
        internal float m_QueueFade;
        internal PlayMode m_QueuePlayMode;

        internal List<(Transform Transform, bool Recursive)> m_Mixing;
        bool[] m_MixingMask;
        int m_MixingMaskCount = -1;

        public AnimationState() { }

        internal override bool IsTrackedAlive => !m_Removed && m_Owner != null;

        public bool enabled
        {
            get => m_Enabled;
            set
            {
                if (m_Enabled == value) return;
                m_Enabled = value;
                if (value && !m_Playing) Begin();
            }
        }

        public float weight
        {
            get => m_Weight;
            set
            {
                m_Weight = value;
                m_Fading = false;
            }
        }

        public WrapMode wrapMode
        {
            get => m_WrapMode;
            set => m_WrapMode = value;
        }

        public float time
        {
            get => (float)m_Time;
            set => m_Time = value;
        }

        public float normalizedTime
        {
            get
            {
                float l = length;
                return l > 0f ? (float)(m_Time / l) : 0f;
            }
            set => m_Time = value * length;
        }

        public float speed
        {
            get => m_Speed;
            set => m_Speed = value;
        }

        public float normalizedSpeed
        {
            get
            {
                float l = length;
                return l > 0f ? m_Speed / l : 0f;
            }
            set => m_Speed = value * length;
        }

        public float length => m_Clip != null ? m_Clip.length : 0f;

        public int layer
        {
            get => m_Layer;
            set => m_Layer = value;
        }

        public AnimationClip clip => m_Clip;

        public string name
        {
            get => m_Name;
            set => m_Name = value ?? "";
        }

        public AnimationBlendMode blendMode
        {
            get => m_BlendMode;
            set => m_BlendMode = value;
        }

        public void AddMixingTransform(Transform mix) => AddMixingTransform(mix, true);

        public void AddMixingTransform(Transform mix, [Internal.DefaultValue("true")] bool recursive)
        {
            if (mix == null) throw new NullReferenceException("mix");
            m_Mixing ??= new List<(Transform, bool)>();
            m_Mixing.Add((mix, recursive));
            m_MixingMaskCount = -1;
        }

        public void RemoveMixingTransform(Transform mix)
        {
            if (m_Mixing == null) return;
            m_Mixing.RemoveAll(m => m.Transform == mix);
            if (m_Mixing.Count == 0) m_Mixing = null;
            m_MixingMaskCount = -1;
        }

        internal bool[] MixingMask(AnimationBindings bindings)
        {
            if (m_MixingMaskCount == bindings.FloatCount) return m_MixingMask;
            m_MixingMask = new bool[bindings.FloatCount];
            for (int c = 0; c < bindings.FloatCount; c++)
            {
                var t = bindings.FloatOwner[c]?.Transform;
                m_MixingMask[c] = t != null && Mixed(t);
            }
            m_MixingMaskCount = bindings.FloatCount;
            return m_MixingMask;
        }

        bool Mixed(Transform t)
        {
            foreach (var (mix, recursive) in m_Mixing)
            {
                if (mix == null) continue;
                if (t == mix) return true;
                if (recursive)
                    for (var p = t.m_Parent; p != null; p = p.m_Parent)
                        if (p == mix) return true;
            }
            return false;
        }

        internal WrapMode EffectiveWrap
        {
            get
            {
                var w = m_WrapMode != WrapMode.Default ? m_WrapMode : m_Owner != null ? m_Owner.m_WrapMode : WrapMode.Default;
                return w == WrapMode.Default ? WrapMode.Once : w;
            }
        }

        /// <summary>Where on the clip's curves the state's time falls, by its wrap mode.</summary>
        internal float SampleTime()
        {
            float length = this.length;
            float start = m_Clip.StartTime;
            if (length <= 0f) return start;
            double t = m_Time;
            switch (EffectiveWrap)
            {
                case WrapMode.Loop:
                    t -= Math.Floor(t / length) * length;
                    break;
                case WrapMode.PingPong:
                {
                    double period = length * 2.0;
                    t -= Math.Floor(t / period) * period;
                    if (t > length) t = period - t;
                    break;
                }
                default:
                    t = Math.Clamp(t, 0.0, length);
                    break;
            }
            return start + (float)t;
        }

        internal void Begin()
        {
            m_Playing = true;
            m_Fresh = true;
        }

        internal void StopInternal()
        {
            m_Enabled = false;
            m_Playing = false;
            m_Fading = false;
            m_Time = 0.0;
        }

        internal void FadeTo(float target, float length, bool stopWhenFaded)
        {
            m_TargetWeight = target;
            m_FadeSpeed = Math.Abs(target - m_Weight) / Math.Max(length, 1e-6f);
            m_StopWhenFaded = stopWhenFaded;
            m_Fading = true;
            if (m_FadeSpeed == 0f) FinishFade();
        }

        internal void UpdateFade(float deltaTime)
        {
            if (!m_Fading) return;
            float step = m_FadeSpeed * deltaTime;
            if (Math.Abs(m_TargetWeight - m_Weight) <= step) FinishFade();
            else m_Weight += Math.Sign(m_TargetWeight - m_Weight) * step;
        }

        void FinishFade()
        {
            m_Weight = m_TargetWeight;
            m_Fading = false;
            if (m_StopWhenFaded && m_TargetWeight <= 0f)
            {
                if (m_IsClone) m_Owner?.RemoveState(this);
                StopInternal();
            }
        }
    }
}
