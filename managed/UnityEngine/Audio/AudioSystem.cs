using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Audio;
using B2BodyType = Unbound.Box2D.Dynamics.BodyType;

namespace Unbound.Runtime
{
    /// <summary>
    /// The audio update (PlayerLoop PostLateUpdate.UpdateAudio, registered when an audio type is
    /// first used): snapshot transitions and mixer group levels, every active source's transform and
    /// settings, the listener; then the mixer's frame (the virtual device advances by the frame's
    /// real time). The doppler velocity of a source or listener whose <c>velocityUpdateMode</c>
    /// picks the fixed loop is sampled in FixedUpdate.AudioFixedUpdate instead (<see cref="FixedUpdate"/>).
    /// </summary>
    internal static unsafe class AudioSystem
    {
        static AudioFilterDescriptor[] s_ListenerFilters;
        static readonly List<AudioReverbZone> s_Zones = new List<AudioReverbZone>();
        // Counts the times zones appeared after none were enabled: the zone reverb then starts empty.
        static ulong s_ZoneGeneration;
        /// <summary>The hearing listener's position at the last update (reverb zone mix curves).</summary>
        internal static Vector3 s_HearingPosition;
        static readonly List<AudioSource> s_Sources = new List<AudioSource>();
        static readonly List<AudioListener> s_Listeners = new List<AudioListener>();
        static readonly List<AudioMixer> s_Mixers = new List<AudioMixer>();
        static readonly List<AudioMixerGroup> s_Groups = new List<AudioMixerGroup>();
        static readonly List<AudioClip> s_Loading = new List<AudioClip>();
        static AudioSource[] s_Snapshot = new AudioSource[16];
        static float s_ListenerVolume = float.NaN;
        static bool s_ListenerPaused;
        static int s_WarnedListeners = 1;
        static bool s_WarnedNoListener;
        static AudioStatus s_Status;

        /// <summary>Set while the mixer calls managed code (OnAudioFilterRead, PCM readers).</summary>
        [ThreadStatic] internal static bool t_InCallback;

        static AudioSystem()
        {
            PlayerLoopDriver.Register(typeof(UnityEngine.PlayerLoop.PostLateUpdate.UpdateAudio), Update);
            PlayerLoopDriver.Register(typeof(UnityEngine.PlayerLoop.FixedUpdate.AudioFixedUpdate), FixedUpdate);
            try { AudioBackend.Current.InstallCallback(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        internal static void EnsureRegistered() { }

        internal static IReadOnlyList<AudioListener> Listeners => s_Listeners;

        internal static float ListenerVolume
        {
            get
            {
                if (float.IsNaN(s_ListenerVolume)) s_ListenerVolume = Mathf.Clamp01(AudioBackend.Current.ProjectSettings.Volume);
                return s_ListenerVolume;
            }
            set => s_ListenerVolume = value;
        }

        internal static bool ListenerPaused
        {
            get => s_ListenerPaused;
            set => s_ListenerPaused = value;
        }

        internal static AudioStatus Status => s_Status;

        // ------------------------------------------------------------------ registries

        internal static void Add(AudioSource source)
        {
            if (!s_Sources.Contains(source)) s_Sources.Add(source);
        }

        internal static void Remove(AudioSource source) => s_Sources.Remove(source);

        internal static void Add(AudioListener listener)
        {
            s_Listeners.Remove(listener);
            s_Listeners.Add(listener);
        }

        internal static void Remove(AudioListener listener) => s_Listeners.Remove(listener);

        internal static void Add(AudioReverbZone zone)
        {
            if (s_Zones.Contains(zone)) return;
            if (s_Zones.Count == 0) s_ZoneGeneration++;
            s_Zones.Add(zone);
        }

        internal static void Remove(AudioReverbZone zone) => s_Zones.Remove(zone);

        internal static void Add(AudioMixer mixer)
        {
            if (!s_Mixers.Contains(mixer)) s_Mixers.Add(mixer);
        }

        internal static void QueueLoad(AudioClip clip)
        {
            if (!s_Loading.Contains(clip)) s_Loading.Add(clip);
        }

        /// <summary>A group got a mixer handle: send its level now (a source may play this frame).</summary>
        internal static void PushGroup(AudioMixerGroup group)
        {
            if (!s_Groups.Contains(group)) s_Groups.Add(group);
            if (group.m_Mixer != null) Add(group.m_Mixer);
            AudioBackend.Current.UpdateGroup(group);
        }

        /// <summary>AudioSettings.Reset stopped every voice.</summary>
        internal static void OnReset()
        {
            foreach (var source in s_Sources)
            {
                source.m_Paused = false;
                source.m_Dirty = true;
            }
        }

        // ------------------------------------------------------------------ threading

        /// <summary>The audio API may only be used on the main thread outside the mixer's callbacks
        /// (where the mixer is busy).</summary>
        internal static bool IsCallerAllowed() => !t_InCallback && PlayerLoopDriver.IsMainThread;

        internal static void CheckMainThread(string api)
        {
            if (!IsCallerAllowed()) MainThreadCheck.Throw(api);
        }

        internal static bool ValidSpectrumLength(int length)
        {
            if (length >= 64 && length <= 8192 && (length & (length - 1)) == 0) return true;
            Debug.LogError("Length of the samples array must be a power of two between 64 and 8192.");
            return false;
        }

        // ------------------------------------------------------------------ velocity update mode

        /// <summary>
        /// Whether the velocity of a source or listener on <paramref name="go"/> follows the fixed
        /// loop. Unity 6000.0 AudioVelocityUpdateMode: Fixed updates in the FixedUpdate loop,
        /// Dynamic in the Update loop, Auto in the FixedUpdate loop "if it is attached to a
        /// Rigidbody", dynamic otherwise. Unbound's choices (unmeasured): "attached" means a 3D
        /// Rigidbody on this GameObject or on a parent (what moves the object in fixed steps, as
        /// colliders attach to a parent's body); Rigidbody2D and ArticulationBody do not count (the
        /// page names Rigidbody); a value outside the enum is Dynamic.
        /// </summary>
        internal static bool UpdatesInFixedStep(AudioVelocityUpdateMode mode, GameObject go)
        {
            if (mode == AudioVelocityUpdateMode.Fixed) return true;
            if (mode != AudioVelocityUpdateMode.Auto || go == null) return false;
            for (var t = go.transform; t != null; t = t.m_Parent)
            {
                var owner = t.m_GameObject;
                if (owner == null) break;
                foreach (var c in owner.m_Components)
                    if (c is Rigidbody body && !body.m_Destroyed) return true;
            }
            return false;
        }

        /// <summary>
        /// The position a fixed-loop sample of <paramref name="t"/> reads: the pose of the last
        /// physics step, not the pose between steps that an interpolated or extrapolated body's
        /// Transform shows (PreUpdate.PhysicsUpdate and PreUpdate.Physics2DUpdate write that pose to
        /// the Transform each frame). Unbound's choices (unmeasured): the nearest GameObject (this
        /// one or a parent) with a Rigidbody or a Rigidbody2D decides, and on one GameObject a
        /// Rigidbody counts before a Rigidbody2D. While a 3D body's Transform shows the in-between
        /// pose (it has a body, interpolates or extrapolates, the simulation runs in FixedUpdate,
        /// and no script moved the Transform since physics wrote it), the object keeps its current
        /// offset from the body's Transform, carried by the body's rotation: simulated position +
        /// simulated rotation × (inverse Transform rotation × (object position − body Transform
        /// position)). The body itself reads exactly its simulated position (the same pose
        /// <c>Rigidbody.position</c> returns). For a Rigidbody2D see <see cref="FixedStepPosition2D"/>.
        /// Otherwise the Transform's position.
        /// </summary>
        internal static Vector3 FixedStepPosition(Transform t)
        {
            for (var p = t; p != null; p = p.m_Parent)
            {
                var owner = p.m_GameObject;
                if (owner == null) break;
                Rigidbody2D body2D = null;
                foreach (var c in owner.m_Components)
                {
                    if (c is Rigidbody2D candidate)
                    {
                        if (body2D == null && !candidate.m_Destroyed) body2D = candidate;
                        continue;
                    }
                    if (c is not Rigidbody body || body.m_Destroyed) continue;
                    if (body.m_Body == 0 || body.m_Interpolation == RigidbodyInterpolation.None
                        || Physics.s_SimulationMode != SimulationMode.FixedUpdate
                        || p.m_Version != body.m_TransformVersion)
                        return t.position;
                    if (ReferenceEquals(p, t)) return body.m_CurrentPosition;
                    var offset = Quaternion.Inverse(p.rotation) * (t.position - p.position);
                    return body.m_CurrentPosition + body.m_CurrentRotation * offset;
                }
                if (body2D != null) return FixedStepPosition2D(t, p, body2D);
            }
            return t.position;
        }

        /// <summary>
        /// <see cref="FixedStepPosition"/> for an object on, or under, the Rigidbody2D
        /// <paramref name="rb"/> on <paramref name="p"/>. The Transform shows the in-between pose
        /// under the conditions PreUpdate.Physics2DUpdate writes it: the body exists, interpolates or
        /// extrapolates, is not static, 2D physics simulates in FixedUpdate, and no script moved the
        /// Transform since physics wrote it. Then the body reads its simulated position at its
        /// Transform's z (the pose <c>Rigidbody2D.position</c> returns; 2D physics keeps z), and a
        /// child keeps its current offset from the body's Transform, turned about z by the simulated
        /// angle minus the angle the Transform shows (Unbound's choice, unmeasured). Otherwise the
        /// Transform's position.
        /// </summary>
        static Vector3 FixedStepPosition2D(Transform t, Transform p, Rigidbody2D rb)
        {
            var body = rb.m_Body;
            if (body == null || rb.m_Interpolate == RigidbodyInterpolation2D.None
                || body.BodyType == B2BodyType.StaticBody
                || Physics2D.s_SimulationMode != SimulationMode2D.FixedUpdate
                || p.m_Version != rb.m_TransformVersion)
                return t.position;
            var at = p.position;
            var simulated = new Vector3(rb.m_CurrentPosition.x, rb.m_CurrentPosition.y, at.z);
            if (ReferenceEquals(p, t)) return simulated;
            var turn = Quaternion.AngleAxis((rb.m_CurrentAngle - rb.m_SyncAngle) * Mathf.Rad2Deg, Vector3.forward);
            return simulated + turn * (t.position - at);
        }

        /// <summary>
        /// Each fixed step (FixedUpdate.AudioFixedUpdate): the velocity of the sources and the
        /// hearing listener that follow the fixed loop, from the motion since the previous fixed
        /// step over the step's <c>fixedDeltaTime</c>. Unbound's choices (unmeasured): every step
        /// samples, so the frame's packet carries the last step's velocity; a frame without a step
        /// (also every frame at timeScale 0) keeps the previous velocity; positions are read at
        /// this node, before the step's physics, as the previous step's simulated pose for an
        /// interpolated or extrapolated body (<see cref="FixedStepPosition"/>); teleports are not
        /// filtered, as in the frame loop.
        /// </summary>
        internal static void FixedUpdate()
        {
            float step = Time.fixedDeltaTime;
            int n = s_Sources.Count;
            if (n > 0)
            {
                if (s_Snapshot.Length < n) s_Snapshot = new AudioSource[Math.Max(n, s_Snapshot.Length * 2)];
                s_Sources.CopyTo(s_Snapshot);
                for (int i = 0; i < n; i++)
                {
                    var source = s_Snapshot[i];
                    s_Snapshot[i] = null;
                    if (source == null || source.m_GameObject == null) continue;
                    try { source.SampleMotion(fixedStep: true, step); }
                    catch (Exception e) { Debug.LogException(e, source); }
                }
            }
            s_Listeners.RemoveAll(l => l == null);
            if (s_Listeners.Count > 0)
            {
                var listener = s_Listeners[s_Listeners.Count - 1];
                try { listener.SampleMotion(fixedStep: true, step); }
                catch (Exception e) { Debug.LogException(e, listener); }
            }
        }

        // ------------------------------------------------------------------ the frame

        internal static void Update()
        {
            var backend = AudioBackend.Current;
            float realDelta = (float)Math.Max(0.0, Time.s_RealDeltaTime);

            foreach (var mixer in s_Mixers) mixer.Tick(Time.deltaTime, realDelta);
            for (int i = s_Groups.Count - 1; i >= 0; i--)
            {
                var group = s_Groups[i];
                if (group == null || group.m_Handle == 0)
                {
                    s_Groups.RemoveAt(i);
                    continue;
                }
                backend.UpdateGroup(group);
            }

            for (int i = s_Loading.Count - 1; i >= 0; i--)
            {
                var clip = s_Loading[i];
                if (clip != null && clip.m_LoadState == AudioDataLoadState.Loading) clip.m_LoadState = AudioDataLoadState.Loaded;
            }
            s_Loading.Clear();

            s_Listeners.RemoveAll(l => l == null);
            if (s_Listeners.Count > 0) s_HearingPosition = s_Listeners[s_Listeners.Count - 1].transform.position;
            int n = s_Sources.Count;
            if (s_Snapshot.Length < n) s_Snapshot = new AudioSource[Math.Max(n, s_Snapshot.Length * 2)];
            s_Sources.CopyTo(s_Snapshot);
            int playing = 0;
            for (int i = 0; i < n; i++)
            {
                var source = s_Snapshot[i];
                s_Snapshot[i] = null;
                if (source == null || source.m_GameObject == null) continue;
                try
                {
                    source.SampleMotion(fixedStep: false, realDelta);
                    bool hasFilter = AudioFilters.Update(source.Handle, source.m_GameObject);
                    source.m_HasFilter = hasFilter;
                    source.Flush(always: true);
                    if (source.m_Handle != 0 && !source.m_Paused)
                    {
                        var state = backend.State(source.m_Handle);
                        if (state.Main != AudioVoiceState.None || state.OneShots > 0) playing++;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogException(e, source);
                }
            }

            var frame = new AudioFramePacket
            {
                DeltaTime = realDelta,
                ListenerForward = Vector3.forward,
                ListenerUp = Vector3.up,
                ListenerVolume = ListenerVolume,
                Flags = s_ListenerPaused ? AudioFrameFlags.Paused : 0,
            };
            s_Listeners.RemoveAll(l => l == null);
            int listeners = s_Listeners.Count;
            if (listeners > 0)
            {
                // The most recently enabled listener hears.
                var listener = s_Listeners[listeners - 1];
                var t = listener.transform;
                listener.SampleMotion(fixedStep: false, realDelta);
                frame.ListenerPosition = t.position;
                frame.ListenerForward = t.forward;
                frame.ListenerUp = t.up;
                frame.ListenerVelocity = listener.m_Velocity;
                frame.Flags |= AudioFrameFlags.HasListener;
                bool scripts = AudioFilters.Update(AudioFilters.ListenerKey, listener.m_GameObject);
                if (scripts) frame.Flags |= AudioFrameFlags.ListenerFilter;
                frame.FilterCount = AudioFilterChain.CollectListener(listener, scripts, ref s_ListenerFilters);
                s_WarnedNoListener = false;
            }
            else
            {
                AudioFilters.Update(AudioFilters.ListenerKey, null);
                if (playing > 0 && !s_WarnedNoListener)
                {
                    s_WarnedNoListener = true;
                    Debug.LogWarning("There are no audio listeners in the scene. Please ensure there is always one audio listener in the scene");
                }
            }
            if (listeners > 1 && listeners != s_WarnedListeners)
                Debug.LogWarning($"There are {listeners} audio listeners in the scene. Please ensure there is always exactly one audio listener in the scene.");
            s_WarnedListeners = Math.Max(1, listeners);

            s_Zones.RemoveAll(z => z == null);
            if (s_Zones.Count > 0)
            {
                frame.Zone.Kind = AudioFilterKind.Reverb;
                frame.Zone.Id = s_ZoneGeneration;
                AudioReverbZone.Blend(s_Zones, frame.ListenerPosition, frame.Zone.Reverb);
            }
            fixed (AudioFilterDescriptor* filters = s_ListenerFilters)
            {
                if (frame.FilterCount > 0) frame.Filters = filters;
                s_Status = backend.Frame(frame);
            }
        }

        /// <summary>Forgets everything (tests).</summary>
        internal static void ResetForTests()
        {
            s_Sources.Clear();
            s_Listeners.Clear();
            s_Zones.Clear();
            s_HearingPosition = Vector3.zero;
            s_Mixers.Clear();
            s_Groups.Clear();
            s_Loading.Clear();
            s_ListenerVolume = float.NaN;
            s_ListenerPaused = false;
            s_WarnedListeners = 1;
            s_WarnedNoListener = false;
            AudioFilterChain.ResetForTests();
        }
    }

    /// <summary>
    /// <c>OnAudioFilterRead(float[] data, int channels)</c> of the scripts on a source's (or the
    /// listener's) GameObject. The mixer calls them on its own thread (the output device's), with
    /// interleaved stereo at the output rate; the lists are rebuilt on the main thread each frame.
    /// </summary>
    internal static unsafe class AudioFilters
    {
        internal const ulong ListenerKey = 0;

        static readonly ConcurrentDictionary<ulong, Action<float[], int>[]> s_Filters = new ConcurrentDictionary<ulong, Action<float[], int>[]>();
        static readonly ConcurrentDictionary<ulong, Action<float[], int>> s_GroupOutputs = new ConcurrentDictionary<ulong, Action<float[], int>>();

        internal static void ObserveGroup(AudioMixerGroup group, Action<float[], int> observer)
        {
            ulong handle = group.Handle;
            if (observer == null) s_GroupOutputs.TryRemove(handle, out _);
            else s_GroupOutputs[handle] = observer;
            group.m_ObserveOutput = observer != null;
            AudioSystem.PushGroup(group);
        }

        static readonly Dictionary<Type, MethodInfo> s_Methods = new Dictionary<Type, MethodInfo>();
        [ThreadStatic] static float[] t_Buffer;

        static MethodInfo MethodOf(Type type)
        {
            if (s_Methods.TryGetValue(type, out var method)) return method;
            for (var t = type; t != null && t != typeof(MonoBehaviour); t = t.BaseType)
            {
                method = t.GetMethod("OnAudioFilterRead", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                    null, new[] { typeof(float[]), typeof(int) }, null);
                if (method != null && method.ReturnType == typeof(void)) break;
                method = null;
            }
            s_Methods[type] = method;
            return method;
        }

        /// <summary>Whether <see cref="Update"/> collects this component's OnAudioFilterRead.</summary>
        internal static bool IsFilterScript(Component c) =>
            c is MonoBehaviour mb && !mb.m_Destroyed && mb.isActiveAndEnabled && MethodOf(mb.GetType()) != null;

        /// <summary>Rebuilds the filters of one key from its GameObject; true when there are some.</summary>
        internal static bool Update(ulong key, GameObject go)
        {
            List<Action<float[], int>> list = null;
            if (go != null)
            {
                foreach (var c in go.m_Components)
                {
                    if (!(c is MonoBehaviour mb) || mb.m_Destroyed || !mb.isActiveAndEnabled) continue;
                    var method = MethodOf(mb.GetType());
                    if (method == null) continue;
                    (list ??= new List<Action<float[], int>>()).Add((Action<float[], int>)Delegate.CreateDelegate(typeof(Action<float[], int>), mb, method));
                }
            }
            if (list == null)
            {
                s_Filters.TryRemove(key, out _);
                return false;
            }
            // Keep the same delegates when nothing changed (no allocation for the audio thread).
            if (s_Filters.TryGetValue(key, out var current) && Same(current, list)) return true;
            s_Filters[key] = list.ToArray();
            return true;
        }

        static bool Same(Action<float[], int>[] current, List<Action<float[], int>> list)
        {
            if (current.Length != list.Count) return false;
            for (int i = 0; i < current.Length; i++)
                if (current[i].Target != list[i].Target || current[i].Method != list[i].Method) return false;
            return true;
        }

        static float[] Buffer(int length)
        {
            var buffer = t_Buffer;
            if (buffer == null || buffer.Length != length) t_Buffer = buffer = new float[length];
            return buffer;
        }

        /// <summary>The mixer's calls into managed code.</summary>
        [UnmanagedCallersOnly]
        internal static void Invoke(int kind, ulong handle, float* data, int frames, int channels, long arg)
        {
            bool was = AudioSystem.t_InCallback;
            AudioSystem.t_InCallback = true;
            try
            {
                Dispatch(kind, handle, data, frames, channels, arg);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                AudioSystem.t_InCallback = was;
            }
        }

        internal static void Dispatch(int kind, ulong handle, float* data, int frames, int channels, long arg)
        {
            int count = frames * channels;
            switch (kind)
            {
                case AudioCallbackKind.SourceFilter:
                case AudioCallbackKind.ListenerFilter:
                {
                    var key = kind == AudioCallbackKind.ListenerFilter ? ListenerKey : handle;
                    if (!s_Filters.TryGetValue(key, out var filters) || count <= 0) return;
                    var buffer = Buffer(count);
                    new Span<float>(data, count).CopyTo(buffer);
                    foreach (var filter in filters)
                    {
                        try { filter(buffer, channels); }
                        catch (Exception e) { Debug.LogException(e, filter.Target as UnityEngine.Object); }
                    }
                    buffer.AsSpan().CopyTo(new Span<float>(data, count));
                    break;
                }
                case AudioCallbackKind.GroupOutput:
                {
                    if (count <= 0 || !s_GroupOutputs.TryGetValue(handle, out var observer)) return;
                    var buffer = Buffer(count);
                    new Span<float>(data, count).CopyTo(buffer);
                    observer(buffer, channels); // Observers cannot modify the production group buffer.
                    break;
                }
                case AudioCallbackKind.PcmRead:
                {
                    var clip = AudioClip.WithCallbacks(handle);
                    if (clip == null || count <= 0) return;
                    var buffer = Buffer(count);
                    Array.Clear(buffer, 0, buffer.Length);
                    clip.InvokePCMReader(buffer);
                    buffer.AsSpan().CopyTo(new Span<float>(data, count));
                    break;
                }
                case AudioCallbackKind.PcmSetPosition:
                    AudioClip.WithCallbacks(handle)?.InvokePCMSetPosition((int)arg);
                    break;
            }
        }
    }
}
