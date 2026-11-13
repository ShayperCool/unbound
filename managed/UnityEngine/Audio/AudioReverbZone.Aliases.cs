using System;
using Unbound.Runtime;

namespace UnityEngine
{
    public sealed partial class AudioReverbZone
    {
        const string RoomRolloffWarning = "Warning! roomRolloffFactor is no longer supported.";

        [Obsolete(RoomRolloffWarning)]
        public float roomRolloffFactor
        {
            get
            {
                if (Compat.RemovedAudioSettingsMeasured) { Debug.LogWarning(RoomRolloffWarning); return 10f; }
                Stubs.NotImplemented("UnityEngine.AudioReverbZone.roomRolloffFactor");
                return 0f;
            }
            set
            {
                if (Compat.RemovedAudioSettingsMeasured) Debug.LogWarning(RoomRolloffWarning);
                else Stubs.NotImplemented("UnityEngine.AudioReverbZone.roomRolloffFactor");
            }
        }
    }
}
