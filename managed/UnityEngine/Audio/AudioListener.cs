using System;
using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>
    /// The ears of the scene. One should be active: with none nothing is heard, with several Unity
    /// warns and the most recently enabled one hears (Unbound warns when the count changes rather
    /// than every frame). <c>volume</c> starts at the project's global volume
    /// (AudioManager.asset); <c>pause</c> pauses every source that does not ignore it.
    /// </summary>
    [ClassId(81)]
    [RequireComponent(typeof(Transform))]
    public sealed partial class AudioListener : AudioBehaviour
    {
        internal AudioVelocityUpdateMode m_VelocityUpdateMode = AudioVelocityUpdateMode.Auto;
        internal Vector3 m_LastPosition;
        internal Vector3 m_Velocity;
        internal bool m_HasLastPosition;

        public AudioListener() { }

        static AudioListener() => AudioSystem.EnsureRegistered();

        public static float volume
        {
            get => AudioSystem.ListenerVolume;
            set => AudioSystem.ListenerVolume = Mathf.Clamp01(value);
        }

        public static bool pause
        {
            get => AudioSystem.ListenerPaused;
            set => AudioSystem.ListenerPaused = value;
        }

        public AudioVelocityUpdateMode velocityUpdateMode
        {
            get => m_VelocityUpdateMode;
            set => m_VelocityUpdateMode = value;
        }

        [Obsolete("GetOutputData returning a float[] is deprecated, use GetOutputData and pass a pre allocated array instead.")]
        public static float[] GetOutputData(int numSamples, int channel)
        {
            var samples = new float[numSamples];
            GetOutputData(samples, channel);
            return samples;
        }

        public static void GetOutputData(float[] samples, int channel)
        {
            AudioSystem.CheckMainThread("GetOutputData");
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            AudioBackend.Current.OutputData(0, channel, samples, -1);
        }

        [Obsolete("GetSpectrumData returning a float[] is deprecated, use GetSpectrumData and pass a pre allocated array instead.")]
        public static float[] GetSpectrumData(int numSamples, int channel, FFTWindow window)
        {
            var samples = new float[numSamples];
            GetSpectrumData(samples, channel, window);
            return samples;
        }

        public static void GetSpectrumData(float[] samples, int channel, FFTWindow window)
        {
            AudioSystem.CheckMainThread("GetSpectrumData");
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            if (!AudioSystem.ValidSpectrumLength(samples.Length)) return;
            AudioBackend.Current.OutputData(0, channel, samples, (int)window);
        }

        internal void UpdateMotion(float deltaTime)
        {
            var position = transform.position;
            if (m_HasLastPosition && deltaTime > 0f)
                m_Velocity = (position - m_LastPosition) / deltaTime;
            m_LastPosition = position;
            m_HasLastPosition = true;
        }

        internal override void OnEngineEnable()
        {
            m_HasLastPosition = false;
            m_Velocity = Vector3.zero;
            AudioSystem.Add(this);
        }

        internal override void OnEngineDisable() => AudioSystem.Remove(this);

        internal override void OnEngineDestroy() => AudioSystem.Remove(this);

        internal override void CopyFrom(Component source, Func<Object, Object> remap) =>
            m_VelocityUpdateMode = ((AudioListener)source).m_VelocityUpdateMode;
    }
}
