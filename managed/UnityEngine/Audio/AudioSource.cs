using System;
using Unbound.Runtime;
using UnityEngine.Audio;

namespace UnityEngine
{
    public partial class AudioBehaviour : Behaviour
    {
    }

    /// <summary>
    /// A source of sound. Its playback (the main voice <c>Play</c> starts, the one-shots) runs in the
    /// native mixer; this side holds the serialized settings, sends them when they change and its
    /// transform once per frame (PostLateUpdate.UpdateAudio), and reads the play state back.
    ///
    /// Unity's rules: playing needs an active and enabled source ("Can not play a disabled audio
    /// source"); <c>playOnAwake</c> plays whenever the source becomes active and enabled; disabling
    /// stops it; <c>Play</c> restarts a playing source and resumes a paused one; assigning another
    /// clip stops the main voice; <c>time</c> set while stopped is where the next <c>Play</c> starts.
    /// </summary>
    [ClassId(82)]
    [RequireComponent(typeof(Transform))]
    public sealed partial class AudioSource : AudioBehaviour
    {
        internal ulong m_Handle;
        internal AudioClip m_Clip;
        internal AudioMixerGroup m_OutputGroup;
        internal float m_Volume = 1f;
        internal float m_Pitch = 1f;
        internal bool m_Loop;
        internal bool m_Mute;
        internal bool m_PlayOnAwake = true;
        internal int m_Priority = 128;
        internal float m_PanStereo;
        internal float m_DopplerLevel = 1f;
        internal float m_MinDistance = 1f;
        internal float m_MaxDistance = 500f;
        internal AudioRolloffMode m_RolloffMode = AudioRolloffMode.Logarithmic;
        internal bool m_BypassEffects;
        internal bool m_BypassListenerEffects;
        internal bool m_BypassReverbZones;
        internal bool m_IgnoreListenerVolume;
        internal bool m_IgnoreListenerPause;
        internal bool m_Spatialize;
        internal bool m_SpatializePostEffects;
        internal AudioVelocityUpdateMode m_VelocityUpdateMode = AudioVelocityUpdateMode.Auto;
        internal GamepadSpeakerOutputType m_GamepadSpeakerOutputType = GamepadSpeakerOutputType.Speaker;
        // The distance curves; a curve of one key is the constant the matching property reads.
        internal AnimationCurve m_RolloffCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
        internal AnimationCurve m_SpatialBlendCurve = new AnimationCurve(new Keyframe(0f, 0f));
        internal AnimationCurve m_SpreadCurve = new AnimationCurve(new Keyframe(0f, 0f));
        internal AnimationCurve m_ReverbZoneMixCurve = new AnimationCurve(new Keyframe(0f, 1f));

        internal bool m_Paused;
        internal bool m_Dirty = true;
        internal bool m_CurvesDirty = true;
        internal bool m_KeepOutput;
        internal bool m_HasFilter;
        internal Vector3 m_LastPosition;
        internal Vector3 m_Velocity;
        internal bool m_HasLastPosition;
        float[] m_RolloffTable, m_SpatialBlendTable, m_SpreadTable;

        /// <summary>Samples per distance curve sent to the mixer.</summary>
        const int CurveSamples = 64;

        public AudioSource() { }

        static AudioSource() => AudioSystem.EnsureRegistered();

        // ------------------------------------------------------------------ properties

        public float volume
        {
            get => m_Volume;
            set { m_Volume = Mathf.Clamp01(value); m_Dirty = true; }
        }

        public float pitch
        {
            get => m_Pitch;
            set
            {
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    Debug.LogWarning("Attempt to set pitch to an invalid value", this);
                    return;
                }
                m_Pitch = Mathf.Clamp(value, -3f, 3f);
                m_Dirty = true;
            }
        }

        public float time
        {
            get
            {
                var clip = m_Clip;
                if (clip == null || clip.m_Frequency <= 0) return 0f;
                return (float)(State("get_time").Position / clip.m_Frequency);
            }
            set
            {
                var clip = m_Clip;
                if (clip == null || clip.m_Frequency <= 0) return;
                Seek((double)value * clip.m_Frequency, "set_time");
            }
        }

        public int timeSamples
        {
            get => m_Clip == null ? 0 : (int)State("get_timeSamples").Position;
            set
            {
                if (m_Clip == null) return;
                Seek(value, "set_timeSamples");
            }
        }

        public AudioClip clip
        {
            get => m_Clip;
            set
            {
                if (ReferenceEquals(m_Clip, value)) return;
                // Another clip stops the main voice (one-shots play on).
                if (m_Handle != 0 && AudioSystem.IsCallerAllowed())
                {
                    var state = AudioBackend.Current.State(m_Handle);
                    if (state.Main != AudioVoiceState.None)
                        AudioBackend.Current.Command(m_Handle, new AudioSourceCommand { Kind = AudioCommandKind.StopMain });
                }
                m_Paused = false;
                m_Clip = value;
                m_Dirty = true;
            }
        }

        public AudioResource resource
        {
            get => m_Clip;
            set
            {
                if (value == null || value is AudioClip)
                    clip = (AudioClip)value;
                else
                    Debug.LogWarning($"Unbound: AudioSource.resource: {value.GetType().Name} is not supported; only AudioClips play.", this);
            }
        }

        public AudioMixerGroup outputAudioMixerGroup
        {
            get => m_OutputGroup;
            set { m_OutputGroup = value; m_Dirty = true; }
        }

        public GamepadSpeakerOutputType gamepadSpeakerOutputType
        {
            get => m_GamepadSpeakerOutputType;
            set => m_GamepadSpeakerOutputType = value;
        }

        public bool isPlaying
        {
            get
            {
                if (m_Handle == 0 || m_Paused) return false;
                var state = State("get_isPlaying");
                return state.Main == AudioVoiceState.Playing || state.Main == AudioVoiceState.Scheduled || state.OneShots > 0;
            }
        }

        public bool isVirtual => m_Handle != 0 && State("get_isVirtual").IsVirtual != 0;

        public bool loop
        {
            get => m_Loop;
            set { m_Loop = value; m_Dirty = true; }
        }

        public bool ignoreListenerVolume
        {
            get => m_IgnoreListenerVolume;
            set { m_IgnoreListenerVolume = value; m_Dirty = true; }
        }

        public bool playOnAwake
        {
            get => m_PlayOnAwake;
            set => m_PlayOnAwake = value;
        }

        public bool ignoreListenerPause
        {
            get => m_IgnoreListenerPause;
            set { m_IgnoreListenerPause = value; m_Dirty = true; }
        }

        public AudioVelocityUpdateMode velocityUpdateMode
        {
            get => m_VelocityUpdateMode;
            set => m_VelocityUpdateMode = value;
        }

        public float panStereo
        {
            get => m_PanStereo;
            set { m_PanStereo = Mathf.Clamp(value, -1f, 1f); m_Dirty = true; }
        }

        public float spatialBlend
        {
            get => ConstantOf(m_SpatialBlendCurve);
            set => SetConstant(ref m_SpatialBlendCurve, Mathf.Clamp01(value));
        }

        public bool spatialize
        {
            get => m_Spatialize;
            set { m_Spatialize = value; m_Dirty = true; }
        }

        public bool spatializePostEffects
        {
            get => m_SpatializePostEffects;
            set => m_SpatializePostEffects = value;
        }

        public float reverbZoneMix
        {
            get => ConstantOf(m_ReverbZoneMixCurve);
            set => SetConstant(ref m_ReverbZoneMixCurve, Mathf.Clamp(value, 0f, 1.1f));
        }

        public bool bypassEffects
        {
            get => m_BypassEffects;
            set { m_BypassEffects = value; m_Dirty = true; }
        }

        public bool bypassListenerEffects
        {
            get => m_BypassListenerEffects;
            set { m_BypassListenerEffects = value; m_Dirty = true; }
        }

        public bool bypassReverbZones
        {
            get => m_BypassReverbZones;
            set { m_BypassReverbZones = value; m_Dirty = true; }
        }

        public float dopplerLevel
        {
            get => m_DopplerLevel;
            set { m_DopplerLevel = Mathf.Clamp(value, 0f, 5f); m_Dirty = true; }
        }

        /// <summary>Degrees, 0 to 360 (the spread curve holds it normalized).</summary>
        public float spread
        {
            get => ConstantOf(m_SpreadCurve) * 360f;
            set => SetConstant(ref m_SpreadCurve, Mathf.Clamp(value, 0f, 360f) / 360f);
        }

        public int priority
        {
            get => m_Priority;
            set { m_Priority = Mathf.Clamp(value, 0, 256); m_Dirty = true; }
        }

        public bool mute
        {
            get => m_Mute;
            set { m_Mute = value; m_Dirty = true; }
        }

        public float minDistance
        {
            get => m_MinDistance;
            set { m_MinDistance = Mathf.Max(0f, value); m_Dirty = true; }
        }

        public float maxDistance
        {
            get => m_MaxDistance;
            set { m_MaxDistance = Mathf.Max(0f, value); m_Dirty = true; }
        }

        public AudioRolloffMode rolloffMode
        {
            get => m_RolloffMode;
            set { m_RolloffMode = value; m_Dirty = true; m_CurvesDirty = true; }
        }

        static float ConstantOf(AnimationCurve curve) => curve.length > 0 ? curve[0].value : 0f;

        void SetConstant(ref AnimationCurve curve, float value)
        {
            curve = new AnimationCurve(new Keyframe(0f, value));
            m_Dirty = true;
            m_CurvesDirty = true;
        }

        public void SetCustomCurve(AudioSourceCurveType type, AnimationCurve curve)
        {
            var copy = curve == null ? new AnimationCurve() : new AnimationCurve(curve.keys) { preWrapMode = curve.preWrapMode, postWrapMode = curve.postWrapMode };
            switch (type)
            {
                case AudioSourceCurveType.CustomRolloff:
                    m_RolloffCurve = copy;
                    m_RolloffMode = AudioRolloffMode.Custom;
                    break;
                case AudioSourceCurveType.SpatialBlend:
                    m_SpatialBlendCurve = copy;
                    break;
                case AudioSourceCurveType.ReverbZoneMix:
                    m_ReverbZoneMixCurve = copy;
                    break;
                case AudioSourceCurveType.Spread:
                    m_SpreadCurve = copy;
                    break;
            }
            m_Dirty = true;
            m_CurvesDirty = true;
        }

        public AnimationCurve GetCustomCurve(AudioSourceCurveType type)
        {
            var curve = type switch
            {
                AudioSourceCurveType.CustomRolloff => m_RolloffCurve,
                AudioSourceCurveType.SpatialBlend => m_SpatialBlendCurve,
                AudioSourceCurveType.ReverbZoneMix => m_ReverbZoneMixCurve,
                _ => m_SpreadCurve,
            };
            return new AnimationCurve(curve.keys) { preWrapMode = curve.preWrapMode, postWrapMode = curve.postWrapMode };
        }

        // ------------------------------------------------------------------ playback

        public void Play() => PlayAt(0.0, "Play");

        public void Play([UnityEngine.Internal.DefaultValue("0")] ulong delay) => PlayAt(delay == 0 ? 0.0 : -(delay / 44100.0), "Play");

        public void PlayDelayed(float delay) => PlayAt(delay < 0f ? 0.0 : -(double)delay, "PlayDelayed");

        public void PlayScheduled(double time) => PlayAt(time < 0.0 ? 0.0 : time, "PlayScheduled");

        /// <summary>Starts the main voice: time 0 now, &gt; 0 at that dsp time, &lt; 0 after that delay.</summary>
        void PlayAt(double time, string api)
        {
            AudioSystem.CheckMainThread(api);
            ThrowIfDestroyed();
            if (!isActiveAndEnabled)
            {
                Debug.LogWarning("Can not play a disabled audio source", this);
                return;
            }
            m_Paused = false;
            m_Clip?.EnsureLoaded();
            Flush();
            Command(new AudioSourceCommand { Kind = AudioCommandKind.Play, Time = time });
            if (Trace.AudioOn) Trace.Audio($"{api} '{m_Clip?.m_Name}' ({m_Clip?.length:F2}s) on '{m_GameObject.m_Name}' at {time:F3}, loop {m_Loop}, volume {m_Volume}, group '{m_OutputGroup?.m_Name}'");
        }

        public void PlayOneShot(AudioClip clip) => PlayOneShot(clip, 1f);

        public void PlayOneShot(AudioClip clip, [UnityEngine.Internal.DefaultValue("1.0F")] float volumeScale)
        {
            if (clip == null)
            {
                Debug.LogWarning("PlayOneShot was called with a null AudioClip.");
                return;
            }
            AudioSystem.CheckMainThread("PlayOneShot");
            ThrowIfDestroyed();
            if (!isActiveAndEnabled)
            {
                Debug.LogWarning("Can not play a disabled audio source", this);
                return;
            }
            clip.EnsureLoaded();
            Flush();
            Command(new AudioSourceCommand { Kind = AudioCommandKind.PlayOneShot, Clip = clip.m_Handle, VolumeScale = volumeScale });
            if (Trace.AudioOn) Trace.Audio($"PlayOneShot '{clip.m_Name}' on '{m_GameObject.m_Name}'");
        }

        public void SetScheduledStartTime(double time)
        {
            AudioSystem.CheckMainThread("SetScheduledStartTime");
            if (m_Handle == 0) return;
            Command(new AudioSourceCommand { Kind = AudioCommandKind.SetScheduledStart, Time = time });
        }

        public void SetScheduledEndTime(double time)
        {
            AudioSystem.CheckMainThread("SetScheduledEndTime");
            if (m_Handle == 0) return;
            Command(new AudioSourceCommand { Kind = AudioCommandKind.SetScheduledEnd, Time = time });
        }

        public void Stop()
        {
            AudioSystem.CheckMainThread("Stop");
            ThrowIfDestroyed();
            StopInternal();
        }

        internal void StopInternal()
        {
            m_Paused = false;
            if (m_Handle == 0) return;
            if (Trace.AudioOn) Trace.Audio($"Stop '{m_Clip?.m_Name}' on '{m_GameObject?.m_Name}'");
            Command(new AudioSourceCommand { Kind = AudioCommandKind.Stop });
        }

        public void Pause()
        {
            AudioSystem.CheckMainThread("Pause");
            ThrowIfDestroyed();
            if (m_Handle == 0) return;
            var state = AudioBackend.Current.State(m_Handle);
            if (state.Main == AudioVoiceState.None && state.OneShots == 0) return;
            m_Paused = true;
            Command(new AudioSourceCommand { Kind = AudioCommandKind.Pause });
        }

        public void UnPause()
        {
            AudioSystem.CheckMainThread("UnPause");
            ThrowIfDestroyed();
            if (!m_Paused || m_Handle == 0) return;
            m_Paused = false;
            Command(new AudioSourceCommand { Kind = AudioCommandKind.UnPause });
        }

        public static void PlayClipAtPoint(AudioClip clip, Vector3 position) => PlayClipAtPoint(clip, position, 1f);

        public static void PlayClipAtPoint(AudioClip clip, Vector3 position, [UnityEngine.Internal.DefaultValue("1.0F")] float volume)
        {
            var gameObject = new GameObject("One shot audio");
            gameObject.transform.position = position;
            var audioSource = (AudioSource)gameObject.AddComponent(typeof(AudioSource));
            audioSource.clip = clip;
            audioSource.spatialBlend = 1f;
            audioSource.volume = volume;
            audioSource.Play();
            Object.Destroy(gameObject, clip.length * ((Time.timeScale < 0.01f) ? 0.01f : Time.timeScale));
        }

        // ------------------------------------------------------------------ platform features

        // Gamepad speakers (consoles), spatializer and ambisonic decoder plugins do not exist here:
        // these report failure as Unity does without them.
        [Unbound.EditorFaithful("gamepad speakers exist on consoles only: the desktop Editor fails too")]
        public bool PlayOnGamepad(int slot) => false;

        [Unbound.EditorFaithful("gamepad speakers exist on consoles only: the desktop Editor fails too")]
        public bool DisableGamepadOutput() => false;

        [Unbound.EditorFaithful("gamepad speakers exist on consoles only: the desktop Editor fails too")]
        public bool SetGamepadSpeakerMixLevel(int slot, int mixLevel) => false;

        [Unbound.EditorFaithful("gamepad speakers exist on consoles only: the desktop Editor fails too")]
        public bool SetGamepadSpeakerMixLevelDefault(int slot) => false;

        [Unbound.EditorFaithful("gamepad speakers exist on consoles only: the desktop Editor fails too")]
        public bool SetGamepadSpeakerRestrictedAudio(int slot, bool restricted) => false;

        [Unbound.EditorFaithful("gamepad speakers exist on consoles only: the desktop Editor fails too")]
        public static bool GamepadSpeakerSupportsOutputType(GamepadSpeakerOutputType outputType) => false;

        public bool SetSpatializerFloat(int index, float value)
        {
            Unbound.Runtime.Stubs.Ignored("AudioSource.SetSpatializerFloat", "fails: spatializer plugins are not loaded");
            return false;
        }

        public bool GetSpatializerFloat(int index, out float value)
        {
            value = 0f;
            return false;
        }

        public bool GetAmbisonicDecoderFloat(int index, out float value)
        {
            value = 0f;
            return false;
        }

        public bool SetAmbisonicDecoderFloat(int index, float value)
        {
            Unbound.Runtime.Stubs.Ignored("AudioSource.SetAmbisonicDecoderFloat", "fails: ambisonic decoder plugins are not loaded");
            return false;
        }

        // ------------------------------------------------------------------ output data

        [Obsolete("GetOutputData returning a float[] is deprecated, use GetOutputData and pass a pre allocated array instead.")]
        public float[] GetOutputData(int numSamples, int channel)
        {
            var samples = new float[numSamples];
            GetOutputData(samples, channel);
            return samples;
        }

        public void GetOutputData(float[] samples, int channel)
        {
            AudioSystem.CheckMainThread("GetOutputData");
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            m_KeepOutput = true;
            if (m_Handle == 0)
            {
                Array.Clear(samples, 0, samples.Length);
                return;
            }
            AudioBackend.Current.OutputData(m_Handle, channel, samples, -1);
        }

        [Obsolete("GetSpectrumData returning a float[] is deprecated, use GetSpectrumData and pass a pre allocated array instead.")]
        public float[] GetSpectrumData(int numSamples, int channel, FFTWindow window)
        {
            var samples = new float[numSamples];
            GetSpectrumData(samples, channel, window);
            return samples;
        }

        public void GetSpectrumData(float[] samples, int channel, FFTWindow window)
        {
            AudioSystem.CheckMainThread("GetSpectrumData");
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            if (!AudioSystem.ValidSpectrumLength(samples.Length)) return;
            m_KeepOutput = true;
            if (m_Handle == 0)
            {
                Array.Clear(samples, 0, samples.Length);
                return;
            }
            AudioBackend.Current.OutputData(m_Handle, channel, samples, (int)window);
        }

        // ------------------------------------------------------------------ mixer link

        internal ulong Handle => m_Handle != 0 ? m_Handle : (m_Handle = AudioBackend.NextHandle());

        AudioSourceState State(string api)
        {
            AudioSystem.CheckMainThread(api);
            ThrowIfDestroyed();
            return m_Handle == 0 ? default : AudioBackend.Current.State(m_Handle);
        }

        void Seek(double frames, string api)
        {
            AudioSystem.CheckMainThread(api);
            ThrowIfDestroyed();
            Flush();
            Command(new AudioSourceCommand { Kind = AudioCommandKind.Seek, Time = Math.Max(0.0, frames) });
        }

        void Command(in AudioSourceCommand command) => AudioBackend.Current.Command(Handle, command);

        /// <summary>Sends the settings (and the transform) to the mixer when they changed.</summary>
        internal void Flush(bool always = false)
        {
            if (!m_Dirty && !always && m_Handle != 0) return;
            m_Dirty = false;
            SendPacket(Handle, m_Clip, m_Volume, m_Loop);
        }

        /// <summary>This source's settings (and transform) for a mixer source: its own, or a voice
        /// playing another clip through it (audio playables).</summary>
        internal void SendPacket(ulong handle, AudioClip clip, float volume, bool loop)
        {
            if (m_CurvesDirty)
            {
                m_CurvesDirty = false;
                m_RolloffTable = m_RolloffMode == AudioRolloffMode.Custom ? Sample(m_RolloffCurve) : null;
                m_SpatialBlendTable = m_SpatialBlendCurve.length > 1 ? Sample(m_SpatialBlendCurve) : null;
                m_SpreadTable = m_SpreadCurve.length > 1 ? Sample(m_SpreadCurve) : null;
            }
            uint flags = 0;
            if (loop) flags |= AudioSourceFlags.Loop;
            if (m_Mute) flags |= AudioSourceFlags.Mute;
            if (m_IgnoreListenerVolume) flags |= AudioSourceFlags.IgnoreListenerVolume;
            if (m_IgnoreListenerPause) flags |= AudioSourceFlags.IgnoreListenerPause;
            if (m_BypassEffects) flags |= AudioSourceFlags.BypassEffects;
            if (m_BypassListenerEffects) flags |= AudioSourceFlags.BypassListenerEffects;
            if (m_BypassReverbZones) flags |= AudioSourceFlags.BypassReverbZones;
            if (m_Spatialize) flags |= AudioSourceFlags.Spatialize;
            if (m_KeepOutput) flags |= AudioSourceFlags.KeepOutput;
            if (m_HasFilter && isActiveAndEnabled) flags |= AudioSourceFlags.Filter;
            var packet = new AudioSourcePacket
            {
                Clip = clip != null ? clip.m_Handle : 0,
                Group = m_OutputGroup != null ? m_OutputGroup.Handle : 0,
                Position = m_GameObject != null ? transform.position : Vector3.zero,
                Velocity = m_Velocity,
                Volume = volume,
                Pitch = m_Pitch,
                PanStereo = m_PanStereo,
                SpatialBlend = ConstantOf(m_SpatialBlendCurve),
                Spread = ConstantOf(m_SpreadCurve) * 360f,
                DopplerLevel = m_DopplerLevel,
                MinDistance = m_MinDistance,
                MaxDistance = m_MaxDistance,
                ReverbZoneMix = ConstantOf(m_ReverbZoneMixCurve),
                RolloffMode = (int)m_RolloffMode,
                Priority = m_Priority,
                Flags = flags,
                CurveLength = CurveSamples,
            };
            unsafe
            {
                fixed (float* rolloff = m_RolloffTable)
                fixed (float* blend = m_SpatialBlendTable)
                fixed (float* spread = m_SpreadTable)
                {
                    packet.RolloffCurve = rolloff;
                    packet.SpatialBlendCurve = blend;
                    packet.SpreadCurve = spread;
                    AudioBackend.Current.UpdateSource(handle, ref packet);
                }
            }
        }

        static float[] Sample(AnimationCurve curve)
        {
            var table = new float[CurveSamples];
            for (int i = 0; i < CurveSamples; i++) table[i] = curve.Evaluate(i / (float)(CurveSamples - 1));
            return table;
        }

        /// <summary>Once per frame: the velocity from the transform's motion.</summary>
        internal void UpdateMotion(float deltaTime)
        {
            var position = transform.position;
            if (m_HasLastPosition && deltaTime > 0f)
                m_Velocity = (position - m_LastPosition) / deltaTime;
            m_LastPosition = position;
            m_HasLastPosition = true;
        }

        // ------------------------------------------------------------------ engine hooks

        internal override void OnEngineEnable()
        {
            AudioSystem.Add(this);
            m_HasLastPosition = false;
            m_Velocity = Vector3.zero;
            m_Dirty = true;
            if (m_PlayOnAwake && m_Clip != null) Play();
        }

        internal override void OnEngineDisable()
        {
            AudioSystem.Remove(this);
            if (m_Handle != 0 && AudioSystem.IsCallerAllowed()) StopInternal();
        }

        internal override void OnEngineDestroy()
        {
            AudioSystem.Remove(this);
            if (m_Handle == 0) return;
            AudioBackend.Current.ReleaseSource(m_Handle);
            m_Handle = 0;
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (AudioSource)source;
            m_Clip = s.m_Clip;
            m_OutputGroup = s.m_OutputGroup;
            m_Volume = s.m_Volume;
            m_Pitch = s.m_Pitch;
            m_Loop = s.m_Loop;
            m_Mute = s.m_Mute;
            m_PlayOnAwake = s.m_PlayOnAwake;
            m_Priority = s.m_Priority;
            m_PanStereo = s.m_PanStereo;
            m_DopplerLevel = s.m_DopplerLevel;
            m_MinDistance = s.m_MinDistance;
            m_MaxDistance = s.m_MaxDistance;
            m_RolloffMode = s.m_RolloffMode;
            m_BypassEffects = s.m_BypassEffects;
            m_BypassListenerEffects = s.m_BypassListenerEffects;
            m_BypassReverbZones = s.m_BypassReverbZones;
            m_IgnoreListenerVolume = s.m_IgnoreListenerVolume;
            m_IgnoreListenerPause = s.m_IgnoreListenerPause;
            m_Spatialize = s.m_Spatialize;
            m_SpatializePostEffects = s.m_SpatializePostEffects;
            m_VelocityUpdateMode = s.m_VelocityUpdateMode;
            m_RolloffCurve = new AnimationCurve(s.m_RolloffCurve.keys);
            m_SpatialBlendCurve = new AnimationCurve(s.m_SpatialBlendCurve.keys);
            m_SpreadCurve = new AnimationCurve(s.m_SpreadCurve.keys);
            m_ReverbZoneMixCurve = new AnimationCurve(s.m_ReverbZoneMixCurve.keys);
            m_Dirty = true;
            m_CurvesDirty = true;
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            T Resolve<T>(string key) where T : Object
            {
                var r = body.Ref(key);
                return r.IsNull || resolver == null ? null : resolver.Resolve(r, typeof(T)) as T;
            }
            m_OutputGroup = Resolve<AudioMixerGroup>("OutputAudioMixerGroup");
            // Unity 6 serializes the AudioResource; earlier releases the clip.
            m_Clip = Resolve<AudioResource>("m_Resource") as AudioClip ?? Resolve<AudioClip>("m_audioClip");
            m_PlayOnAwake = body.Bool("m_PlayOnAwake", true);
            m_Volume = Mathf.Clamp01(body.Float("m_Volume", 1f));
            m_Pitch = body.Float("m_Pitch", 1f);
            m_Loop = body.Bool("Loop");
            m_Mute = body.Bool("Mute");
            m_Spatialize = body.Bool("Spatialize");
            m_SpatializePostEffects = body.Bool("SpatializePostEffects");
            m_Priority = body.Int("Priority", 128);
            m_DopplerLevel = body.Float("DopplerLevel", 1f);
            m_MinDistance = body.Float("MinDistance", 1f);
            m_MaxDistance = body.Float("MaxDistance", 500f);
            m_PanStereo = body.Float("Pan2D");
            m_RolloffMode = (AudioRolloffMode)body.Int("rolloffMode");
            m_BypassEffects = body.Bool("BypassEffects");
            m_BypassListenerEffects = body.Bool("BypassListenerEffects");
            m_BypassReverbZones = body.Bool("BypassReverbZones");
            AnimationCurve Curve(string key, AnimationCurve fallback) =>
                body.Get(key) is YNode node && node.Kind == YKind.Map && (node.Get("m_Curve")?.Count ?? 0) > 0 ? BuiltinReaders.AnimationCurve(node) : fallback;
            m_RolloffCurve = Curve("rolloffCustomCurve", m_RolloffCurve);
            m_SpatialBlendCurve = Curve("panLevelCustomCurve", m_SpatialBlendCurve);
            m_SpreadCurve = Curve("spreadCustomCurve", m_SpreadCurve);
            m_ReverbZoneMixCurve = Curve("reverbZoneMixCustomCurve", m_ReverbZoneMixCurve);
            m_Dirty = true;
            m_CurvesDirty = true;
        }
    }
}
