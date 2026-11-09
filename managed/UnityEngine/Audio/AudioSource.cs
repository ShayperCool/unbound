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
        internal AudioClip m_StreamBuffers;
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
        internal bool m_HasAudioFrame;
        internal double m_PlaybackDspTime;
        internal Vector3 m_LastPosition;
        internal Vector3 m_Velocity;
        internal Vector3 m_SampledListenerVelocity;
        AudioListenerPose m_ListenerPose = AudioListenerPose.Identity;
        Vector3 m_DopplerListenerPosition;
        internal bool m_HasLastPosition;
        // The loop the last position was sampled in (false: the frame loop).
        internal bool m_MotionInFixedStep;
        float[] m_RolloffTable, m_SpatialBlendTable, m_SpreadTable;
        // The filter components sent with the last packet (reused), and the chain error reported.
        internal AudioFilterDescriptor[] m_FilterTable;
        string m_FilterChainError;

        /// <summary>Samples per distance curve sent to the mixer.</summary>
        const int CurveSamples = 64;

        public AudioSource() { }

        static AudioSource() => AudioSystem.EnsureRegistered();

        // ------------------------------------------------------------------ properties

        public float volume
        {
            get { MainThreadCheck.Check("get_volume"); return m_Volume; }
            set { MainThreadCheck.Check("set_volume"); m_Volume = Mathf.Clamp01(value); m_Dirty = true; }
        }

        public float pitch
        {
            get { MainThreadCheck.Check("GetPitch"); return m_Pitch; }
            set
            { MainThreadCheck.Check("SetPitch");
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
            { MainThreadCheck.Check("get_time");
                var clip = m_Clip;
                if (clip == null || clip.m_Frequency <= 0) return 0f;
                return (float)(State("get_time").Position / clip.m_Frequency);
            }
            set
            { MainThreadCheck.Check("set_time");
                var clip = m_Clip;
                if (clip == null || clip.m_Frequency <= 0) return;
                Seek((double)value * clip.m_Frequency, "set_time");
            }
        }

        public int timeSamples
        {
            get { MainThreadCheck.Check("get_timeSamples"); return m_Clip == null ? 0 : (int)State("get_timeSamples").Position; }
            set
            { MainThreadCheck.Check("set_timeSamples");
                if (m_Clip == null) return;
                Seek(value, "set_timeSamples");
            }
        }

        public AudioClip clip
        {
            get { MainThreadCheck.Check("get_resource"); return m_Clip; }
            set
            { MainThreadCheck.Check("set_clip");
                if (ReferenceEquals(m_Clip, value)) return;
                // Another clip stops the main voice (one-shots play on).
                if (m_Handle != 0 && AudioSystem.IsCallerAllowed())
                {
                    var state = AudioBackend.Current.State(m_Handle);
                    if (state.Main != AudioVoiceState.None)
                        AudioBackend.Current.Command(m_Handle, new AudioSourceCommand { Kind = AudioCommandKind.StopMain });
                }
                if (ReferenceEquals(m_Clip?.m_StreamBufferOwner, this) && m_Clip.m_StreamBufferMain)
                    m_Clip.ReleaseStreamBuffer();
                m_Paused = false;
                m_Clip = value;
                m_Dirty = true;
            }
        }

        public AudioResource resource
        {
            get { MainThreadCheck.Check("get_resource"); return m_Clip; }
            set
            { MainThreadCheck.Check("set_resource");
                if (value == null || value is AudioClip)
                    clip = (AudioClip)value;
                else
                    Debug.LogWarning($"Unbound: AudioSource.resource: {value.GetType().Name} is not supported; only AudioClips play.", this);
            }
        }

        public AudioMixerGroup outputAudioMixerGroup
        {
            get { MainThreadCheck.Check("get_outputAudioMixerGroup"); return m_OutputGroup; }
            set { MainThreadCheck.Check("set_outputAudioMixerGroup"); m_OutputGroup = value; m_Dirty = true; }
        }

        public GamepadSpeakerOutputType gamepadSpeakerOutputType
        {
            get { MainThreadCheck.Check("get_gamepadSpeakerOutputType"); return m_GamepadSpeakerOutputType; }
            set { MainThreadCheck.Check("set_gamepadSpeakerOutputType"); m_GamepadSpeakerOutputType = value; }
        }

        internal bool m_PendingPlayOnAwake;

        public bool isPlaying
        {
            get
            { MainThreadCheck.Check("get_isPlaying");
                if (m_PendingPlayOnAwake) return true;
                if (m_Handle == 0 || m_Paused) return false;
                var state = State("get_isPlaying");
                return state.Main == AudioVoiceState.Playing || state.Main == AudioVoiceState.Scheduled || state.OneShots > 0;
            }
        }

        public bool isVirtual { get { MainThreadCheck.Check("get_isVirtual"); return m_Handle != 0 && State("get_isVirtual").IsVirtual != 0; } }

        public bool loop
        {
            get { MainThreadCheck.Check("get_loop"); return m_Loop; }
            set { MainThreadCheck.Check("set_loop"); m_Loop = value; m_Dirty = true; }
        }

        public bool ignoreListenerVolume
        {
            get { MainThreadCheck.Check("get_ignoreListenerVolume"); return m_IgnoreListenerVolume; }
            set { MainThreadCheck.Check("set_ignoreListenerVolume"); m_IgnoreListenerVolume = value; m_Dirty = true; }
        }

        public bool playOnAwake
        {
            get { MainThreadCheck.Check("get_playOnAwake"); return m_PlayOnAwake; }
            set { MainThreadCheck.Check("set_playOnAwake"); m_PlayOnAwake = value; }
        }

        public bool ignoreListenerPause
        {
            get { MainThreadCheck.Check("get_ignoreListenerPause"); return m_IgnoreListenerPause; }
            set { MainThreadCheck.Check("set_ignoreListenerPause"); m_IgnoreListenerPause = value; m_Dirty = true; }
        }

        public AudioVelocityUpdateMode velocityUpdateMode
        {
            get { MainThreadCheck.Check("get_velocityUpdateMode"); return m_VelocityUpdateMode; }
            set { MainThreadCheck.Check("set_velocityUpdateMode"); m_VelocityUpdateMode = value; }
        }

        public float panStereo
        {
            get { MainThreadCheck.Check("get_panStereo"); return m_PanStereo; }
            set { MainThreadCheck.Check("set_panStereo"); m_PanStereo = Mathf.Clamp(value, -1f, 1f); m_Dirty = true; }
        }

        public float spatialBlend
        {
            get { MainThreadCheck.Check("get_spatialBlend"); return ConstantOf(m_SpatialBlendCurve); }
            set { MainThreadCheck.Check("set_spatialBlend"); SetConstant(ref m_SpatialBlendCurve, Mathf.Clamp01(value)); }
        }

        internal bool UsesSpatialBlend
        {
            get
            {
                if (m_SpatialBlendCurve.length <= 1) return spatialBlend > 0f;
                // Direct evaluation must also protect narrow intervals missed by the legacy table.
                if (Compat.AudioSpatialStereoMeasured && m_SpatialBlendCurve.Evaluate(CurveTimeAtListener()) > 0f) return true;
                for (int i = 0; i < m_SpatialBlendCurve.length; i++)
                    if (m_SpatialBlendCurve[i].value > 0f) return true;
                for (int i = 0; i < CurveSamples; i++)
                    if (m_SpatialBlendCurve.Evaluate(i / (float)(CurveSamples - 1)) > 0f) return true;
                return false;
            }
        }

        public bool spatialize
        {
            get { MainThreadCheck.Check("get_spatialize"); return m_Spatialize; }
            set { MainThreadCheck.Check("set_spatialize"); m_Spatialize = value; m_Dirty = true; }
        }

        public bool spatializePostEffects
        {
            get { MainThreadCheck.Check("get_spatializePostEffects"); return m_SpatializePostEffects; }
            set { MainThreadCheck.Check("set_spatializePostEffects"); m_SpatializePostEffects = value; }
        }

        public float reverbZoneMix
        {
            get { MainThreadCheck.Check("get_reverbZoneMix"); return ConstantOf(m_ReverbZoneMixCurve); }
            set { MainThreadCheck.Check("set_reverbZoneMix"); SetConstant(ref m_ReverbZoneMixCurve, Mathf.Clamp(value, 0f, 1.1f)); }
        }

        public bool bypassEffects
        {
            get { MainThreadCheck.Check("get_bypassEffects"); return m_BypassEffects; }
            set { MainThreadCheck.Check("set_bypassEffects"); m_BypassEffects = value; m_Dirty = true; }
        }

        public bool bypassListenerEffects
        {
            get { MainThreadCheck.Check("get_bypassListenerEffects"); return m_BypassListenerEffects; }
            set { MainThreadCheck.Check("set_bypassListenerEffects"); m_BypassListenerEffects = value; m_Dirty = true; }
        }

        public bool bypassReverbZones
        {
            get { MainThreadCheck.Check("get_bypassReverbZones"); return m_BypassReverbZones; }
            set { MainThreadCheck.Check("set_bypassReverbZones"); m_BypassReverbZones = value; m_Dirty = true; }
        }

        public float dopplerLevel
        {
            get { MainThreadCheck.Check("get_dopplerLevel"); return m_DopplerLevel; }
            set { MainThreadCheck.Check("set_dopplerLevel"); m_DopplerLevel = Mathf.Clamp(value, 0f, 5f); m_Dirty = true; }
        }

        /// <summary>Degrees, 0 to 360 (the spread curve holds it normalized).</summary>
        public float spread
        {
            get { MainThreadCheck.Check("get_spread"); return ConstantOf(m_SpreadCurve) * 360f; }
            set { MainThreadCheck.Check("set_spread"); SetConstant(ref m_SpreadCurve, Mathf.Clamp(value, 0f, 360f) / 360f); }
        }

        public int priority
        {
            get { MainThreadCheck.Check("get_priority"); return m_Priority; }
            set { MainThreadCheck.Check("set_priority"); m_Priority = Mathf.Clamp(value, 0, 256); m_Dirty = true; }
        }

        public bool mute
        {
            get { MainThreadCheck.Check("get_mute"); return m_Mute; }
            set { MainThreadCheck.Check("set_mute"); m_Mute = value; m_Dirty = true; }
        }

        public float minDistance
        {
            get { MainThreadCheck.Check("get_minDistance"); return m_MinDistance; }
            set { MainThreadCheck.Check("set_minDistance"); m_MinDistance = Mathf.Max(0f, value); m_Dirty = true; }
        }

        public float maxDistance
        {
            get { MainThreadCheck.Check("get_maxDistance"); return m_MaxDistance; }
            set { MainThreadCheck.Check("set_maxDistance"); m_MaxDistance = Mathf.Max(0f, value); m_Dirty = true; }
        }

        public AudioRolloffMode rolloffMode
        {
            get { MainThreadCheck.Check("get_rolloffMode"); return m_RolloffMode; }
            set { MainThreadCheck.Check("set_rolloffMode"); m_RolloffMode = value; m_Dirty = true; m_CurvesDirty = true; }
        }

        static float ConstantOf(AnimationCurve curve) => curve.length > 0 ? curve[0].value : 0f;

        Vector3 AudioPosition => Compat.AudioSpatialStereoMeasured && m_MotionInFixedStep && m_HasLastPosition
            ? m_LastPosition : transform.position;

        float CurveTimeAtListener()
        {
            float distance = m_GameObject != null ? Vector3.Distance(AudioPosition, Compat.AudioSpatialStereoMeasured ? m_DopplerListenerPosition : AudioSystem.s_HearingPosition) : 0f;
            return m_MaxDistance > 0f ? distance / m_MaxDistance : 1f;
        }

        /// <summary>The linear gain of this source's send into the reverb zones, as the Editor
        /// derives it: <c>reverbZoneMix</c> (a curve of more than one key at the listener's
        /// distance over maxDistance, measured for normalized keys in tests/projects/audioreverb)
        /// clamped to 0..1.1, then 2000 log10 of it in whole mB up to 1 and
        /// (mix - 1) * 10000 mB above; 0 with <c>bypassReverbZones</c>.</summary>
        internal float ReverbZoneSend()
        {
            if (m_BypassReverbZones) return 0f;
            var curve = m_ReverbZoneMixCurve;
            float mix = curve.length == 1 || !(m_MaxDistance > 0f) || m_GameObject == null
                ? ConstantOf(curve)
                : curve.Evaluate(CurveTimeAtListener());
            mix = Math.Min(1.1f, mix);
            if (mix < 0f) mix = 0f;
            int mB = mix > 1f ? (int)((mix - 1f) * 10000f) : mix > 0f ? (int)(MathF.Log10(mix) * 2000f) : -10000;
            return MathF.Pow(10f, mB / 2000f);
        }

        void SetConstant(ref AnimationCurve curve, float value)
        {
            curve = new AnimationCurve(new Keyframe(0f, value));
            m_Dirty = true;
            m_CurvesDirty = true;
        }

        public void SetCustomCurve(AudioSourceCurveType type, AnimationCurve curve)
        {
            var copy = curve == null ? new AnimationCurve() : new AnimationCurve(curve.keys) { preWrapMode = curve.preWrapMode, postWrapMode = curve.postWrapMode };
            if (Compat.AudioSpatialStereoMeasured && copy.length > 0 && copy[copy.length - 1].time > 1f)
            {
                // Distance curves ending past one scale time and tangents together. Negative
                // keys keep their relative positions (tests/projects/audioreverb, 6000.0.71f1).
                float scale = 1f / copy[copy.length - 1].time;
                var keys = copy.keys;
                for (int i = 0; i < keys.Length; i++)
                {
                    keys[i].time *= scale;
                    keys[i].inTangent /= scale;
                    keys[i].outTangent /= scale;
                }
                copy.keys = keys;
            }
            switch (type)
            {
                case AudioSourceCurveType.CustomRolloff:
                    m_RolloffCurve = copy;
                    if (!Compat.AudioSpatialStereoMeasured) m_RolloffMode = AudioRolloffMode.Custom;
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
            // The returned curve uses the default wrap modes in this measured release.
            if (Compat.AudioSpatialStereoMeasured) return new AnimationCurve(curve.keys);
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
            AudioSystem.CheckMainThread(api == "Play" ? "PlayHelper" : "Play");
            ThrowIfDestroyed();
            if (m_Clip != null) m_PendingPlayOnAwake = false;
            if (!isActiveAndEnabled)
            {
                Debug.LogWarning("Can not play a disabled audio source", this);
                return;
            }
            if (m_Clip != null && (m_Clip.channels == 0 || m_Clip.samples == 0)) return;
            m_Paused = false;
            ResetPlaybackHistoryIfIdle();
            m_Clip?.EnsureLoaded();
            PreparePlaybackMotion();
            // Play can start a clipless OnAudioFilterRead generator before the first
            // audio update. Submit its callback identity with the Play command.
            bool hasFilter = AudioFilters.Update(Handle, m_GameObject);
            if (m_HasFilter != hasFilter) { m_HasFilter = hasFilter; m_Dirty = true; }
            Flush();
            if (Command(new AudioSourceCommand { Kind = AudioCommandKind.Play, Time = time }))
                m_Clip?.ClaimStreamBuffer(this, main: true);
            if (Trace.AudioOn) Trace.Audio($"{api} '{m_Clip?.m_Name}' ({m_Clip?.length:F2}s) on '{m_GameObject.m_Name}' at {time:F3}, loop {m_Loop}, volume {m_Volume}, group '{m_OutputGroup?.m_Name}'");
        }

        internal bool HasRetainedVoices
        {
            get
            {
                if (m_Handle == 0) return false;
                var state = AudioBackend.Current.State(m_Handle);
                return state.Main != AudioVoiceState.None || state.OneShots > 0;
            }
        }

        bool ResetPlaybackHistoryIfIdle()
        {
            if (HasRetainedVoices) return false;
            m_HasAudioFrame = false;
            m_PlaybackDspTime = AudioSettings.dspTime;
            return true;
        }

        void PreparePlaybackMotion()
        {
            if (Compat.AudioSpatialStereoMeasured)
            {
                SampleListenerMotion();
                // Play establishes a fresh transform sample. A source's own body retains
                // the velocity from its selected loop, or supplies the first sample directly.
                if (m_GameObject.GetComponent<Rigidbody>() == null)
                {
                    m_Velocity = Vector3.zero;
                    UpdateMotion(0.0);
                }
                else if (!m_HasLastPosition) UpdateMotion(0.0);
                else m_LastPosition = transform.position;
            }
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
            if (ResetPlaybackHistoryIfIdle()) PreparePlaybackMotion();
            Flush();
            if (Command(new AudioSourceCommand { Kind = AudioCommandKind.PlayOneShot, Clip = clip.m_Handle, VolumeScale = volumeScale }))
                clip.ClaimStreamBuffer(this, main: false);
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
            m_PendingPlayOnAwake = false;
            m_HasAudioFrame = false;
            m_Paused = false;
            ReleaseStreamBuffers();
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

        bool Command(in AudioSourceCommand command) => AudioBackend.Current.Command(Handle, command);

        void ReleaseStreamBuffers()
        {
            while (!ReferenceEquals(m_StreamBuffers, null)) m_StreamBuffers.ReleaseStreamBuffer();
        }

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
                if (Compat.AudioSpatialStereoMeasured)
                {
                    // Evaluate the actual curves once per source update, before the audio thread.
                    // A one-value rolloff table uses the existing packet layout and storage.
                    m_RolloffTable = m_RolloffMode == AudioRolloffMode.Custom ? new float[1] : null;
                    m_SpatialBlendTable = null;
                    m_SpreadTable = null;
                }
                else
                {
                    m_RolloffTable = m_RolloffMode == AudioRolloffMode.Custom ? Sample(m_RolloffCurve) : null;
                    m_SpatialBlendTable = m_SpatialBlendCurve.length > 1 ? Sample(m_SpatialBlendCurve) : null;
                    m_SpreadTable = m_SpreadCurve.length > 1 ? Sample(m_SpreadCurve) : null;
                }
            }
            float blendValue = ConstantOf(m_SpatialBlendCurve);
            float spreadValue = ConstantOf(m_SpreadCurve);
            if (Compat.AudioSpatialStereoMeasured)
            {
                float time = CurveTimeAtListener();
                if (m_RolloffTable != null) m_RolloffTable[0] = m_RolloffCurve.Evaluate(time);
                blendValue = m_SpatialBlendCurve.Evaluate(time);
                spreadValue = m_SpreadCurve.Evaluate(time);
            }
            uint flags = 0;
            if (loop) flags |= AudioSourceFlags.Loop;
            if (m_Mute) flags |= AudioSourceFlags.Mute;
            if (m_IgnoreListenerVolume) flags |= AudioSourceFlags.IgnoreListenerVolume;
            if (m_IgnoreListenerPause) flags |= AudioSourceFlags.IgnoreListenerPause;
            if (m_BypassEffects) flags |= AudioSourceFlags.BypassEffects;
            // Listener routing is separate from bypassing this source's components. In the
            // measured release bypassEffects does not skip listener filters, even unrouted.
            if (m_BypassListenerEffects || (m_BypassEffects && !Compat.AudioListenerChainMeasured))
                flags |= AudioSourceFlags.BypassListenerEffects;
            if (m_BypassReverbZones) flags |= AudioSourceFlags.BypassReverbZones;
            if (m_Spatialize) flags |= AudioSourceFlags.Spatialize;
            if (m_KeepOutput) flags |= AudioSourceFlags.KeepOutput;
            if (Compat.DecodedAudioResamplingMeasured) flags |= AudioSourceFlags.DecodedResampling;
            if (Compat.AudioComponentTailMeasured) flags |= AudioSourceFlags.ComponentFilterTail;
            if (Compat.AudioSpatialStereoMeasured) flags |= AudioSourceFlags.SpatialStereo;
            if (Compat.AudioSpatialStereoMeasured) flags |= AudioSourceFlags.ListenerSnapshot;
            if (Compat.AudioZoneVoiceSendMeasured) flags |= AudioSourceFlags.ZoneVoiceSend;
            if (m_HasFilter && isActiveAndEnabled) flags |= AudioSourceFlags.Filter;
            var packet = new AudioSourcePacket
            {
                Clip = clip != null ? clip.m_Handle : 0,
                Group = m_OutputGroup != null ? m_OutputGroup.Handle : 0,
                Position = m_GameObject != null ? AudioPosition : Vector3.zero,
                Velocity = Compat.AudioSpatialStereoMeasured ? m_Velocity - m_SampledListenerVelocity : m_Velocity,
                ListenerPosition = m_ListenerPose.Position,
                ListenerForward = m_ListenerPose.Forward,
                ListenerUp = m_ListenerPose.Up,
                DopplerListenerPosition = m_DopplerListenerPosition,
                Volume = volume,
                Pitch = m_Pitch,
                PanStereo = m_PanStereo,
                SpatialBlend = blendValue,
                Spread = spreadValue * 360f,
                DopplerLevel = m_DopplerLevel,
                MinDistance = m_MinDistance,
                MaxDistance = m_MaxDistance,
                ReverbZoneMix = ReverbZoneSend(),
                RolloffMode = (int)m_RolloffMode,
                Priority = m_Priority,
                Flags = flags,
                CurveLength = Compat.AudioSpatialStereoMeasured ? 1 : CurveSamples,
            };
            // A clipless source with OnAudioFilterRead plays its first script as the clip.
            bool generator = packet.Clip == 0 && (flags & AudioSourceFlags.Filter) != 0;
            int filterCount = AudioFilterChain.Collect(this, generator, ref m_FilterTable);
            unsafe
            {
                fixed (float* rolloff = m_RolloffTable)
                fixed (float* blend = m_SpatialBlendTable)
                fixed (float* spread = m_SpreadTable)
                fixed (AudioFilterDescriptor* filters = m_FilterTable)
                {
                    packet.RolloffCurve = rolloff;
                    packet.SpatialBlendCurve = blend;
                    packet.SpreadCurve = spread;
                    packet.FilterCount = filterCount;
                    packet.Filters = filterCount > 0 ? filters : null;
                    AudioBackend.Current.UpdateSource(handle, ref packet);
                }
            }
        }

        /// <summary>Logs a filter chain Unbound cannot place once, until it changes.</summary>
        internal void ReportFilterChain(string error)
        {
            if (error == m_FilterChainError) return;
            m_FilterChainError = error;
            if (error != null) Debug.LogError("Unbound: AudioSource on '" + m_GameObject?.m_Name + "': " + error + "; its filter components are not applied", this);
        }

        static float[] Sample(AnimationCurve curve)
        {
            var table = new float[CurveSamples];
            for (int i = 0; i < CurveSamples; i++) table[i] = curve.Evaluate(i / (float)(CurveSamples - 1));
            return table;
        }

        /// <summary>The source's Rigidbody velocity, or its transform's motion since the last sample.</summary>
        internal void UpdateMotion(double deltaTime, AudioListenerPose? previousListenerPose = null)
        {
            var position = transform.position;
            AudioSystem.SampleMotion(m_GameObject, true, position, deltaTime,
                ref m_LastPosition, ref m_HasLastPosition, ref m_Velocity);
            SampleListenerMotion(previousListenerPose);
        }

        /// <summary>Samples the velocity when <paramref name="fixedStep"/> is the loop
        /// <c>velocityUpdateMode</c> picks (<see cref="AudioSystem.UpdatesInFixedStep"/>). When the
        /// picked loop changes, sampling starts again from the next position and the last velocity
        /// is kept until then (Unbound's choice, unmeasured).</summary>
        internal void SampleMotion(bool fixedStep, double deltaTime, AudioListenerPose? previousListenerPose = null)
        {
            bool inFixedStep = AudioSystem.UpdatesInFixedStep(m_VelocityUpdateMode, m_GameObject);
            if (inFixedStep != fixedStep) return;
            if (inFixedStep != m_MotionInFixedStep)
            {
                m_MotionInFixedStep = inFixedStep;
                m_HasLastPosition = false;
            }
            if (!fixedStep)
            {
                UpdateMotion(deltaTime, previousListenerPose);
                return;
            }
            // The step's simulated pose, not an interpolated Transform's (Unbound's choice, unmeasured).
            var position = AudioSystem.FixedStepPosition(transform);
            AudioSystem.SampleMotion(m_GameObject, true, position, deltaTime,
                ref m_LastPosition, ref m_HasLastPosition, ref m_Velocity);
            SampleListenerMotion(previousListenerPose);
        }

        void SampleListenerMotion(AudioListenerPose? previousListenerPose = null)
        {
            m_ListenerPose = previousListenerPose ?? AudioSystem.s_HearingPose;
            m_DopplerListenerPosition = AudioSystem.s_HearingPose.Position;
            m_SampledListenerVelocity = AudioSystem.s_HearingVelocity;
        }

        // ------------------------------------------------------------------ engine hooks

        internal override void OnEngineEnable()
        {
            m_PendingPlayOnAwake = Compat.MainThreadContextsMeasured && m_PlayOnAwake && m_Clip == null;
            m_HasAudioFrame = false;
            AudioSystem.Add(this);
            m_HasLastPosition = false;
            m_Velocity = Vector3.zero;
            m_Dirty = true;
            if (m_PlayOnAwake && m_Clip != null) Play();
        }

        internal override void OnEngineDisable()
        {
            m_PendingPlayOnAwake = false;
            AudioSystem.Remove(this);
            if (m_Handle != 0 && AudioSystem.IsCallerAllowed()) StopInternal();
        }

        internal override void OnEngineDestroyRequested()
        {
            if (AudioSystem.IsCallerAllowed() && Compat.AudioSourceDestroyRequestMeasured) StopInternal();
        }

        internal override void OnEngineDestroy()
        {
            ReleaseStreamBuffers();
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
