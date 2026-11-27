using System;
using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>
    /// Live audio capture into an <see cref="AudioClip"/> (measured on Unity 6000.0.71f1,
    /// <c>tests/projects/audiomic</c> and <c>tests/projects/platform</c>).
    /// </summary>
    [Unbound.EditorFaithful("measured: tests/projects/audiomic and tests/projects/platform")]
    public sealed partial class Microphone
    {
        /// <summary>A list of available microphone devices, identified by name.</summary>
        public static string[] devices => AudioBackend.Current.MicrophoneDevices();

        internal static bool isAnyDeviceRecording => AudioBackend.Current.MicrophoneIsAnyRecording();

        /// <summary>Start recording with a connected microphone device into a mono AudioClip.</summary>
        public static AudioClip Start(string deviceName, bool loop, int lengthSec, int frequency)
        {
            var backend = AudioBackend.Current;
            var available = backend.MicrophoneDevices();
            bool hasDevice = string.IsNullOrEmpty(deviceName)
                ? available.Length > 0
                : Array.IndexOf(available, deviceName) >= 0;
            if (!hasDevice)
                throw new ArgumentException("Couldn't acquire device ID for device name " + deviceName);

            if (lengthSec <= 0)
                throw new ArgumentException("Length of recording must be greater than zero seconds (was: " + lengthSec + " seconds)");

            if (lengthSec > 60 * 60)
                throw new ArgumentException("Length of recording must be less than one hour (was: " + lengthSec + " seconds)");

            if (frequency <= 0)
                throw new ArgumentException("Frequency of recording must be greater than zero (was: " + frequency + " Hz)");

            // Unity 6000.0's managed wrapper rejects lengthSec > 3600 with ArgumentException while
            // native StartRecord rejects lengthSec >= 3600 with an error log and null return
            // (tests/projects/audiomic err.len_3600, err.len_3601).
            if (lengthSec >= 60 * 60)
            {
                if (Compat.MicrophoneMeasured)
                {
                    Debug.LogError("Length of the recording must be less than one hour");
                    return null;
                }
                throw new ArgumentException("Length of recording must be less than one hour (was: " + lengthSec + " seconds)");
            }

            ulong handle = AudioBackend.NextHandle();
            if (!backend.MicrophoneStart(deviceName, loop, lengthSec, frequency, handle, out int channels, out int frames, out string error))
            {
                if (!string.IsNullOrEmpty(error))
                    Debug.LogError(error);
                return null;
            }
            return AudioClip.CreateMicrophoneClip(handle, frames, channels, frequency);
        }

        public static void End(string deviceName) =>
            AudioBackend.Current.MicrophoneEnd(deviceName);

        public static bool IsRecording(string deviceName) =>
            AudioBackend.Current.MicrophoneIsRecording(deviceName);

        public static int GetPosition(string deviceName) =>
            AudioBackend.Current.MicrophoneGetPosition(deviceName);

        public static void GetDeviceCaps(string deviceName, out int minFreq, out int maxFreq) =>
            AudioBackend.Current.MicrophoneGetDeviceCaps(deviceName, out minFreq, out maxFreq);
    }
}
