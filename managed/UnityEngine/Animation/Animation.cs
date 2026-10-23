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
        Pose[] m_LayerPoses = Array.Empty<Pose>();
        Pose m_AdditivePose;

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

        /// <summary>Stored; culling is not applied (Unbound always animates).</summary>
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

        AnimationState GetState(string name)
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

        public void AddClip(AnimationClip clip, string newName, int firstFrame, int lastFrame, [Internal.DefaultValue("false")] bool addLoopFrame)
        {
            if (clip == null) throw new NullReferenceException("clip");
            var used = clip;
            if (firstFrame != int.MinValue || lastFrame != int.MaxValue)
            {
                float rate = clip.frameRate > 0f ? clip.frameRate : 60f;
                used = clip.SubClip(firstFrame / rate, lastFrame / rate);
                used.m_Name = newName;
            }
            var existing = GetState(newName);
            if (existing != null) RemoveState(existing);
            m_States.Add(CreateState(used, newName));
            if (!m_Animations.Contains(used)) m_Animations.Add(used);
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

        /// <summary>Synchronizing layers is not implemented; the states keep their own times.</summary>
        public void SyncLayer(int layer) { }

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

        /// <summary>One update: advances the playing states, writes the blended pose, fires events.</summary>
        internal void Step(float deltaTime) => Evaluate(deltaTime, advance: true);

        void Evaluate(float deltaTime, bool advance)
        {
            if (m_GameObject == null) return;
            var states = States;
            if (advance) UpdateQueue();
            m_Bindings ??= new AnimationBindings(m_GameObject);

            var playing = new List<(AnimationState State, double From, double To, bool Fresh, bool Finished)>();
            foreach (var s in states.ToArray())
            {
                if (!s.m_Enabled || s.m_WaitingInQueue || s.m_Clip == null || !s.m_Clip.m_Legacy) continue;
                double from = s.m_Time;
                bool finished = false;
                if (advance)
                {
                    s.m_Time += deltaTime * s.m_Speed;
                    s.UpdateFade(deltaTime);
                    if (!s.m_Enabled) continue; // faded out
                    var wrap = s.EffectiveWrap;
                    float length = s.length;
                    if (wrap == WrapMode.Once && (s.m_Speed >= 0f ? s.m_Time >= length : s.m_Time <= 0.0))
                        finished = true;
                }
                playing.Add((s, from, s.m_Time, s.m_Fresh, finished));
            }

            // Bind first: binding a clip may add channels, and poses are sized by the channel count.
            foreach (var p in playing) m_Bindings.Bind(p.State.m_Clip);

            // Layers, highest first.
            var layers = new SortedDictionary<int, Pose>(Comparer<int>.Create((a, b) => b.CompareTo(a)));
            Pose additive = null;
            foreach (var (s, _, _, _, _) in playing)
            {
                var bound = m_Bindings.Bind(s.m_Clip);
                Pose pose;
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
                var ordered = new Pose[layers.Count];
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

        Pose TakePose(int index)
        {
            if (index < 0)
            {
                m_AdditivePose ??= new Pose();
                m_AdditivePose.Reset(m_Bindings.FloatCount, m_Bindings.ObjectCount);
                return m_AdditivePose;
            }
            if (m_LayerPoses.Length <= index) Array.Resize(ref m_LayerPoses, index + 1);
            var pose = m_LayerPoses[index] ??= new Pose();
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
            set
            {
                m_Time = value;
                m_Fresh = true;
            }
        }

        public float normalizedTime
        {
            get
            {
                float l = length;
                return l > 0f ? (float)(m_Time / l) : 0f;
            }
            set
            {
                m_Time = value * length;
                m_Fresh = true;
            }
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
