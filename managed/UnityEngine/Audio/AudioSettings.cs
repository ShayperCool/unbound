using System;
using Unbound.Runtime;

namespace UnityEngine
{
    public partial struct AudioConfiguration
    {
        public AudioSpeakerMode speakerMode;
        public int dspBufferSize;
        public int sampleRate;
        public int numRealVoices;
        public int numVirtualVoices;
    }

    /// <summary>
    /// The audio system's configuration (from ProjectSettings/AudioManager.asset, the output device's
    /// sample rate) and its dsp clock: the frames the mixer has rendered, readable from any thread.
    /// </summary>
    public sealed partial class AudioSettings
    {
        public delegate void AudioConfigurationChangeHandler(bool deviceWasChanged);

        // The virtual output device advances with the frames: the audio update must run.
        static AudioSettings() => AudioSystem.EnsureRegistered();

        public static partial class Mobile
        {
            [Unbound.EditorFaithful("the desktop Editor's AudioModule answers false (mobile mute is iOS/Android only)")]
            public static bool muteState => false;

            public static bool stopAudioOutputOnMute
            {
                get => false;
                set => Debug.LogWarning("Setting AudioSettings.Mobile.stopAudioOutputOnMute is possible on iOS and Android only");
            }

            [Unbound.EditorFaithful("the desktop Editor's AudioModule answers true (output is started on iOS/Android only)")]
            public static bool audioOutputStarted => true;

#pragma warning disable CS0067
            public static event Action<bool> OnMuteStateChanged;
#pragma warning restore CS0067

            public static void StartAudioOutput() => Debug.LogWarning("AudioSettings.Mobile.StartAudioOutput is implemented for iOS and Android only");

            public static void StopAudioOutput() => Debug.LogWarning("AudioSettings.Mobile.StopAudioOutput is implemented for iOS and Android only");
        }

        public static event AudioConfigurationChangeHandler OnAudioConfigurationChanged;

        public static AudioSpeakerMode driverCapabilities => AudioSpeakerMode.Stereo;

        public static AudioSpeakerMode speakerMode
        {
            get => GetConfiguration().speakerMode;
            set
            {
                Debug.LogWarning("Setting AudioSettings.speakerMode is deprecated and has been replaced by audio project settings and the AudioSettings.GetConfiguration/AudioSettings.Reset API.");
                var configuration = GetConfiguration();
                configuration.speakerMode = value;
                if (!Reset(configuration)) Debug.LogWarning("Setting AudioSettings.speakerMode failed");
            }
        }

        public static double dspTime => AudioBackend.Current.DspTime;

        public static int outputSampleRate
        {
            get => GetConfiguration().sampleRate;
            set
            {
                Debug.LogWarning("Setting AudioSettings.outputSampleRate is deprecated and has been replaced by audio project settings and the AudioSettings.GetConfiguration/AudioSettings.Reset API.");
                var configuration = GetConfiguration();
                configuration.sampleRate = value;
                if (!Reset(configuration)) Debug.LogWarning("Setting AudioSettings.outputSampleRate failed");
            }
        }

        public static AudioSpatialExperience audioSpatialExperience
        {
            get => AudioSpatialExperience.Bypassed;
            set => Debug.LogWarning("AudioSettings.audioSpatialExperience is not implemented on this platform.");
        }

        public static void GetDSPBufferSize(out int bufferLength, out int numBuffers)
        {
            bufferLength = GetConfiguration().dspBufferSize;
            numBuffers = 4;
        }

        [Obsolete("AudioSettings.SetDSPBufferSize is deprecated and has been replaced by audio project settings and the AudioSettings.GetConfiguration/AudioSettings.Reset API.")]
        public static void SetDSPBufferSize(int bufferLength, int numBuffers)
        {
            Debug.LogWarning("AudioSettings.SetDSPBufferSize is deprecated and has been replaced by audio project settings and the AudioSettings.GetConfiguration/AudioSettings.Reset API.");
            var configuration = GetConfiguration();
            configuration.dspBufferSize = bufferLength;
            if (!Reset(configuration)) Debug.LogWarning("SetDSPBufferSize failed");
        }

        public static string[] GetSpatializerPluginNames()
        {
            Unbound.Runtime.Stubs.Ignored("AudioSettings.GetSpatializerPluginNames", "lists none (native audio plugins are not loaded)");
            return Array.Empty<string>();
        }

        public static string GetSpatializerPluginName() => AudioBackend.Current.ProjectSettings.SpatializerPlugin ?? "";

        public static void SetSpatializerPluginName(string pluginName)
        {
            // No spatializer plugins run: only clearing it is valid.
            if (!string.IsNullOrEmpty(pluginName))
                throw new ArgumentException("Invalid spatializer plugin name");
        }

        public static AudioConfiguration GetConfiguration() => AudioBackend.Current.Configuration;

        /// <summary>Restarts the audio system with another configuration: every source stops, and
        /// OnAudioConfigurationChanged(false) runs.</summary>
        public static bool Reset(AudioConfiguration config)
        {
            AudioSystem.CheckMainThread("Reset");
            bool ok = AudioBackend.Current.Reset(config);
            AudioSystem.OnReset();
            InvokeOnAudioConfigurationChanged(false);
            return ok;
        }

        internal static bool unityAudioDisabled
        {
            get => AudioBackend.Current.ProjectSettings.DisableAudio;
            set => AudioBackend.Current.ProjectSettings.DisableAudio = value;
        }

        internal static string GetAmbisonicDecoderPluginName() => AudioBackend.Current.ProjectSettings.AmbisonicDecoderPlugin ?? "";

        internal static void InvokeOnAudioConfigurationChanged(bool deviceWasChanged)
        {
            OnAudioConfigurationChanged?.Invoke(deviceWasChanged);
        }
    }
}
