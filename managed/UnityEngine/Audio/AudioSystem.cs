using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Audio;

namespace Unbound.Runtime
{
    /// <summary>
    /// The audio update (PlayerLoop PostLateUpdate.UpdateAudio, registered when an audio type is
    /// first used): snapshot transitions and mixer group levels, every active source's transform and
    /// settings, the listener; then the mixer's frame (the virtual device advances by the frame's
    /// real time).
    /// </summary>
    internal static class AudioSystem
    {
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
            AudioBackend.Current.UpdateGroup(group.m_Handle, group.EffectiveGain(0), group.EffectivePitch(0));
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
            if (IsCallerAllowed()) return;
            throw new UnityException(api + " can only be called from the main thread.\nConstructors and field initializers will be executed from the loading thread when loading a scene.\nDon't use this function in the constructor or field initializers, instead move initialization code to the Awake or Start function.");
        }

        internal static bool ValidSpectrumLength(int length)
        {
            if (length >= 64 && length <= 8192 && (length & (length - 1)) == 0) return true;
            Debug.LogError("Length of the samples array must be a power of two between 64 and 8192.");
            return false;
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
                backend.UpdateGroup(group.m_Handle, group.EffectiveGain(0), group.EffectivePitch(0));
            }

            for (int i = s_Loading.Count - 1; i >= 0; i--)
            {
                var clip = s_Loading[i];
                if (clip != null && clip.m_LoadState == AudioDataLoadState.Loading) clip.m_LoadState = AudioDataLoadState.Loaded;
            }
            s_Loading.Clear();

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
                    source.UpdateMotion(realDelta);
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
                listener.UpdateMotion(realDelta);
                frame.ListenerPosition = t.position;
                frame.ListenerForward = t.forward;
                frame.ListenerUp = t.up;
                frame.ListenerVelocity = listener.m_Velocity;
                frame.Flags |= AudioFrameFlags.HasListener;
                if (AudioFilters.Update(AudioFilters.ListenerKey, listener.m_GameObject)) frame.Flags |= AudioFrameFlags.ListenerFilter;
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

            s_Status = backend.Frame(frame);
        }

        /// <summary>Forgets everything (tests).</summary>
        internal static void ResetForTests()
        {
            s_Sources.Clear();
            s_Listeners.Clear();
            s_Mixers.Clear();
            s_Groups.Clear();
            s_Loading.Clear();
            s_ListenerVolume = float.NaN;
            s_ListenerPaused = false;
            s_WarnedListeners = 1;
            s_WarnedNoListener = false;
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
