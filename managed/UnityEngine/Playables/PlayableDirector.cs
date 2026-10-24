using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Unbound.Runtime.Playables;
using UnityEngine.Animations;
using UnityEngine.Audio;
using PL = UnityEngine.PlayerLoop;

namespace UnityEngine.Playables
{
    /// <summary>
    /// Plays a playable asset (a timeline) on its GameObject: builds the asset's graph, binds its
    /// outputs to the scene objects of its generic bindings, resolves exposed references, and keeps
    /// the root playable's time by its wrap mode.
    /// </summary>
    [ClassId(320)]
    public partial class PlayableDirector : Behaviour, IExposedPropertyTable
    {
        internal ScriptableObject m_PlayableAsset;
        internal bool m_PlayOnAwake = true;
        internal DirectorWrapMode m_WrapMode = DirectorWrapMode.Hold;
        internal DirectorUpdateMode m_UpdateMode = DirectorUpdateMode.GameTime;
        internal double m_InitialTime;
        internal readonly List<KeyValuePair<Object, Object>> m_Bindings = new List<KeyValuePair<Object, Object>>();
        internal readonly List<KeyValuePair<PropertyName, Object>> m_References = new List<KeyValuePair<PropertyName, Object>>();

        PlayableGraphData m_Graph;
        PlayableNode m_Root;
        PlayState m_State = PlayState.Paused;
        double m_Time;
        bool m_AwakeDone;
        bool m_DeferredEvaluate;

        public PlayableDirector() { }

        public event Action<PlayableDirector> played;
        public event Action<PlayableDirector> paused;
        public event Action<PlayableDirector> stopped;

        // ------------------------------------------------------------------ properties

        public PlayState state => m_State;

        public DirectorWrapMode extrapolationMode
        {
            get => m_WrapMode;
            set
            {
                m_WrapMode = value;
                if (m_Root != null && !m_Root.Destroyed) m_Root.WrapMode = value;
            }
        }

        public PlayableAsset playableAsset
        {
            get => m_PlayableAsset as PlayableAsset;
            set
            {
                if (m_PlayableAsset == value) return;
                bool wasPlaying = m_State == PlayState.Playing;
                DestroyGraph();
                m_PlayableAsset = value;
                if (wasPlaying) m_State = PlayState.Paused;
            }
        }

        public PlayableGraph playableGraph => m_Graph != null && !m_Graph.Destroyed ? m_Graph.ToGraph() : default;

        public bool playOnAwake
        {
            get => m_PlayOnAwake;
            set => m_PlayOnAwake = value;
        }

        public DirectorUpdateMode timeUpdateMode
        {
            get => m_UpdateMode;
            set
            {
                m_UpdateMode = value;
                if (m_Graph != null && !m_Graph.Destroyed) m_Graph.UpdateMode = value;
            }
        }

        public double time
        {
            get => m_Root != null && !m_Root.Destroyed ? m_Root.Time : m_Time;
            set
            {
                m_Time = value;
                if (m_Root != null && !m_Root.Destroyed) m_Root.ToHandle().SetTime(value);
            }
        }

        public double initialTime
        {
            get => m_InitialTime;
            set => m_InitialTime = value;
        }

        public double duration => playableAsset != null ? playableAsset.duration : 0.0;

        // ------------------------------------------------------------------ play control

        public void Play()
        {
            ThrowIfDestroyed();
            if (playableAsset == null) return;
            if (!EnsureGraph()) return;
            if (m_State == PlayState.Playing) return;
            m_State = PlayState.Playing;
            m_Graph.Play();
            played?.Invoke(this);
        }

        internal void Play(FrameRate frameRate)
        {
            if (frameRate.IsValid() && EnsureGraph()) m_Graph.MatchFrameRate = frameRate;
            Play();
        }

        public void Play(PlayableAsset asset)
        {
            if (asset == null) throw new ArgumentNullException(nameof(asset));
            Play(asset, extrapolationMode);
        }

        public void Play(PlayableAsset asset, DirectorWrapMode mode)
        {
            if (asset == null) throw new ArgumentNullException(nameof(asset));
            playableAsset = asset;
            extrapolationMode = mode;
            Play();
        }

        public void Pause()
        {
            ThrowIfDestroyed();
            if (m_State != PlayState.Playing) return;
            m_State = PlayState.Paused;
            m_Graph?.Stop();
            paused?.Invoke(this);
        }

        public void Resume() => Play();

        public void Stop()
        {
            ThrowIfDestroyed();
            bool had = m_Graph != null;
            DestroyGraph();
            m_State = PlayState.Paused;
            m_Time = m_InitialTime;
            if (had) stopped?.Invoke(this);
        }

        public void Evaluate()
        {
            ThrowIfDestroyed();
            if (playableAsset == null || !EnsureGraph()) return;
            m_Root.ToHandle().SetTime(time);
            m_Graph.Evaluate(0);
        }

        public void DeferredEvaluate()
        {
            m_DeferredEvaluate = true;
            s_Deferred.Add(this);
            EnsureRegistered();
        }

        public void RebuildGraph()
        {
            ThrowIfDestroyed();
            double t = time;
            bool playing = m_State == PlayState.Playing;
            DestroyGraph();
            m_Time = t;
            if (playableAsset == null) return;
            EnsureGraph();
            if (playing) m_Graph.Play();
        }

        internal void ProcessPendingGraphChanges() { }

        internal static void ResetFrameTiming() { }

        // ------------------------------------------------------------------ the graph

        bool EnsureGraph()
        {
            if (m_Graph != null && !m_Graph.Destroyed) return true;
            var asset = playableAsset;
            if (asset == null) return false;
            var g = PlayableGraphData.Create(m_GameObject != null ? m_GameObject.name : "PlayableDirector");
            g.UpdateMode = m_UpdateMode;
            g.Resolver = this;
            g.Director = this;
            m_Graph = g;
            Playable root;
            try
            {
                root = asset.CreatePlayable(g.ToGraph(), m_GameObject);
            }
            catch (Exception e)
            {
                Debug.LogException(e, this);
                root = Playable.Null;
            }
            m_Root = PlayableGraphData.Get(root.GetHandle());
            if (m_Root != null)
            {
                m_Root.Duration = asset.duration;
                m_Root.WrapMode = m_WrapMode;
                m_Root.ToHandle().SetTime(m_Time);
            }
            BindOutputs();
            return true;
        }

        void DestroyGraph()
        {
            if (m_Graph == null) return;
            var g = m_Graph;
            m_Graph = null;
            m_Root = null;
            if (!g.Destroyed)
            {
                g.Destroy();
                PlayableGraphData.FlushDestroyed();
            }
        }

        /// <summary>Before the graph advances in DirectorUpdate.</summary>
        internal void BeforeUpdate(double dt) { }

        /// <summary>After it: a director without extrapolation stops at the end.</summary>
        internal void AfterUpdate()
        {
            if (m_Root == null || m_State != PlayState.Playing) return;
            if (m_WrapMode == DirectorWrapMode.None && m_Root.Done) Stop();
        }

        // ------------------------------------------------------------------ bindings

        public void SetGenericBinding(Object key, Object value)
        {
            for (int i = 0; i < m_Bindings.Count; i++)
            {
                if (m_Bindings[i].Key == key)
                {
                    m_Bindings[i] = new KeyValuePair<Object, Object>(key, value);
                    RebindOutputsOf(key);
                    return;
                }
            }
            m_Bindings.Add(new KeyValuePair<Object, Object>(key, value));
            RebindOutputsOf(key);
        }

        public Object GetGenericBinding(Object key)
        {
            foreach (var b in m_Bindings)
                if (b.Key == key && (object)key != null) return b.Value;
            return null;
        }

        internal bool HasGenericBinding(Object key)
        {
            foreach (var b in m_Bindings)
                if (b.Key == key && (object)key != null) return true;
            return false;
        }

        public void ClearGenericBinding(Object key)
        {
            m_Bindings.RemoveAll(b => b.Key == key);
            RebindOutputsOf(key);
        }

        public void RebindPlayableGraphOutputs() => BindOutputs();

        void RebindOutputsOf(Object key)
        {
            if (m_Graph == null || m_Graph.Destroyed) return;
            foreach (var o in m_Graph.Outputs)
                if (o.ReferenceObject == key) BindOutput(o);
        }

        void BindOutputs()
        {
            if (m_Graph == null || m_Graph.Destroyed) return;
            foreach (var o in m_Graph.Outputs.ToArray()) BindOutput(o);
        }

        /// <summary>An output's target is the generic binding of its reference object (the track),
        /// as the type of output needs it; script outputs get the bound object as user data and its
        /// GameObject's notification receivers.</summary>
        void BindOutput(PlayableOutputData o)
        {
            var key = o.ReferenceObject;
            if ((object)key == null) return;
            var value = GetGenericBinding(key);
            var go = value as GameObject ?? (value as Component)?.gameObject;
            if (o.OutputType == typeof(AnimationPlayableOutput))
            {
                var animator = value as Animator ?? (go != null ? go.GetComponent<Animator>() : null);
                new AnimationPlayableOutput(o.ToHandle()).SetTarget(animator);
            }
            else if (o.OutputType == typeof(AudioPlayableOutput))
            {
                var source = value as AudioSource ?? (go != null ? go.GetComponent<AudioSource>() : null);
                new AudioPlayableOutput(o.ToHandle()).SetTarget(source);
            }
            else if (o.OutputType == typeof(ScriptPlayableOutput))
            {
                o.UserData = value;
            }
            if (go != null)
            {
                foreach (var c in go.GetComponents<Component>())
                    if (c is INotificationReceiver r && !o.Receivers.Contains(r)) o.Receivers.Add(r);
            }
        }

        // ------------------------------------------------------------------ exposed references

        public void SetReferenceValue(PropertyName id, Object value)
        {
            for (int i = 0; i < m_References.Count; i++)
            {
                if (m_References[i].Key == id)
                {
                    m_References[i] = new KeyValuePair<PropertyName, Object>(id, value);
                    return;
                }
            }
            m_References.Add(new KeyValuePair<PropertyName, Object>(id, value));
        }

        public Object GetReferenceValue(PropertyName id, out bool idValid)
        {
            foreach (var r in m_References)
            {
                if (r.Key != id) continue;
                idValid = true;
                return r.Value;
            }
            idValid = false;
            return null;
        }

        public void ClearReferenceValue(PropertyName id) => m_References.RemoveAll(r => r.Key == id);

        // ------------------------------------------------------------------ lifecycle

        internal override void OnEngineEnable()
        {
            if (m_AwakeDone) return;
            m_AwakeDone = true;
            if (m_PlayOnAwake) Play();
        }

        internal override void OnEngineDestroy()
        {
            DestroyGraph();
            s_Deferred.Remove(this);
        }

        static readonly List<PlayableDirector> s_Deferred = new List<PlayableDirector>();
        static bool s_Registered;

        static void EnsureRegistered()
        {
            if (s_Registered) return;
            s_Registered = true;
            PlayerLoopDriver.Register(typeof(PL.PreLateUpdate.DirectorDeferredEvaluate), RunDeferred);
        }

        static void RunDeferred()
        {
            if (s_Deferred.Count == 0) return;
            var list = s_Deferred.ToArray();
            s_Deferred.Clear();
            foreach (var d in list)
            {
                if (d == null || !d.m_DeferredEvaluate) continue;
                d.m_DeferredEvaluate = false;
                try { d.Evaluate(); }
                catch (Exception e) { Debug.LogException(e, d); }
            }
        }

        // ------------------------------------------------------------------ serialized data

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            var asset = body.Ref("m_PlayableAsset");
            m_PlayableAsset = asset.IsNull ? null : resolver.Resolve(asset, typeof(ScriptableObject)) as ScriptableObject;
            m_PlayOnAwake = body.Int("m_InitialState", 1) == 1;
            m_WrapMode = (DirectorWrapMode)body.Int("m_WrapMode", (int)DirectorWrapMode.Hold);
            m_UpdateMode = (DirectorUpdateMode)body.Int("m_DirectorUpdateMode", (int)DirectorUpdateMode.GameTime);
            m_InitialTime = body.Get("m_InitialTime")?.AsDouble() ?? 0.0;
            m_Time = m_InitialTime;
            m_Bindings.Clear();
            foreach (var item in body.Get("m_SceneBindings")?.Seq() ?? Array.Empty<YNode>())
            {
                var k = item.Ref("key");
                var v = item.Ref("value");
                var key = k.IsNull ? null : resolver.Resolve(k, typeof(Object));
                var value = v.IsNull ? null : resolver.Resolve(v, typeof(Object));
                m_Bindings.Add(new KeyValuePair<Object, Object>(key, value));
            }
            m_References.Clear();
            var refs = body.Get("m_ExposedReferences")?.Get("m_References");
            foreach (var item in refs?.Seq() ?? Array.Empty<YNode>())
            {
                if (item.Kind != YKind.Map || item.Keys == null) continue;
                for (int i = 0; i < item.Keys.Length; i++)
                {
                    var r = item[item.Keys[i]].AsRef();
                    var value = r.IsNull ? null : resolver.Resolve(r, typeof(Object));
                    m_References.Add(new KeyValuePair<PropertyName, Object>(new PropertyName(item.Keys[i]), value));
                }
            }
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (PlayableDirector)source;
            m_Enabled = s.m_Enabled;
            m_PlayableAsset = s.m_PlayableAsset;
            m_PlayOnAwake = s.m_PlayOnAwake;
            m_WrapMode = s.m_WrapMode;
            m_UpdateMode = s.m_UpdateMode;
            m_InitialTime = s.m_InitialTime;
            m_Time = s.m_InitialTime;
            m_Bindings.Clear();
            foreach (var b in s.m_Bindings) m_Bindings.Add(new KeyValuePair<Object, Object>(b.Key, remap(b.Value)));
            m_References.Clear();
            foreach (var r in s.m_References) m_References.Add(new KeyValuePair<PropertyName, Object>(r.Key, remap(r.Value)));
        }
    }
}
